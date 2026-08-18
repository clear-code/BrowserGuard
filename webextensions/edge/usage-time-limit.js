'use strict';

import { loadConfig } from './config-loader.js';

// The service worker is not kept alive, so the deadline is recomputed from
// stored state on every alarm rather than held in a timer.
const ALARM_NAME = 'usage-time-limit';
const STATE_KEY = 'usageTimeLimitState';
const CHECK_INTERVAL_MINUTES = 1;

const EMPTY_STATE = {
  sessionStart: 0,
  warnedAt: 0,
  terminateAt: 0,
  warnTabId: 0,
};

// "HH:mm" in local time, as minutes since midnight. null when unreadable.
export function parseTimeOfDay(text) {
  const matched = /^(\d{1,2}):(\d{2})$/.exec(String(text ?? '').trim());
  if (!matched) return null;
  const hours = Number(matched[1]);
  const minutes = Number(matched[2]);
  if (hours > 23 || minutes > 59) return null;
  return hours * 60 + minutes;
}

export const UsageTimeLimit = {
  ALARM_NAME,

  enabled: false,
  maxContinuousMinutes: 0,
  allowedTimeRanges: [],
  action: 'WarnOnly',
  graceSeconds: 60,
  reWarnIntervalMinutes: 10,

  async init() {
    const config = await loadConfig();
    this.applyConfig(config?.UsageTimeLimit);
    if (!this.enabled) return;
    await this.startSession();
    chrome.alarms.create(ALARM_NAME, { periodInMinutes: CHECK_INTERVAL_MINUTES });
  },

  // Separated from init so that it can be exercised without the browser.
  applyConfig(usageTimeLimit) {
    if (!usageTimeLimit) return;
    if (typeof usageTimeLimit.Enabled === 'boolean') {
      this.enabled = usageTimeLimit.Enabled;
    }
    if (Number.isFinite(usageTimeLimit.MaxContinuousMinutes)) {
      this.maxContinuousMinutes = usageTimeLimit.MaxContinuousMinutes;
    }
    if (Array.isArray(usageTimeLimit.AllowedTimeRanges)) {
      // A range that cannot be read is dropped instead of never matching,
      // because a mistyped range would otherwise put the browser permanently
      // outside its allowed hours.
      this.allowedTimeRanges = usageTimeLimit.AllowedTimeRanges
        .map(range => ({
          start: parseTimeOfDay(range?.Start),
          end: parseTimeOfDay(range?.End),
        }))
        .filter(range => range.start !== null && range.end !== null);
    }
    const onExceeded = usageTimeLimit.OnExceeded;
    if (!onExceeded) return;
    if (onExceeded.Action === 'Terminate' || onExceeded.Action === 'WarnOnly') {
      this.action = onExceeded.Action;
    }
    if (Number.isFinite(onExceeded.GraceSeconds)) {
      this.graceSeconds = onExceeded.GraceSeconds;
    }
    if (Number.isFinite(onExceeded.ReWarnIntervalMinutes)) {
      this.reWarnIntervalMinutes = onExceeded.ReWarnIntervalMinutes;
    }
  },

  isWithinAllowedRanges(now) {
    if (this.allowedTimeRanges.length === 0) return true;
    const at = new Date(now);
    const minutes = at.getHours() * 60 + at.getMinutes();
    return this.allowedTimeRanges.some(({ start, end }) => {
      // Equal ends describe a whole day rather than an empty window.
      if (start === end) return true;
      if (start < end) return minutes >= start && minutes < end;
      return minutes >= start || minutes < end;
    });
  },

  // '' while inside both limits, otherwise which one was exceeded.
  violationReason(now, sessionStart) {
    if (this.maxContinuousMinutes > 0 && sessionStart > 0 &&
        now - sessionStart >= this.maxContinuousMinutes * 60000) {
      return 'continuous';
    }
    if (!this.isWithinAllowedRanges(now)) return 'schedule';
    return '';
  },

  // The whole decision, kept free of chrome.* so it can be tested directly.
  // Returns what to do now and the state to persist.
  decide(now, state) {
    if (!this.enabled) return { act: 'none', reason: '', state };

    const reason = this.violationReason(now, state.sessionStart);
    if (!reason) {
      // Back inside the limits, so a later violation warns again from scratch.
      return {
        act: 'none',
        reason: '',
        state: { ...state, warnedAt: 0, terminateAt: 0 },
      };
    }

    if (this.action === 'Terminate') {
      if (!state.terminateAt) {
        return {
          act: 'warn',
          reason,
          state: {
            ...state,
            warnedAt: now,
            terminateAt: now + Math.max(0, this.graceSeconds) * 1000,
          },
        };
      }
      if (now >= state.terminateAt) {
        return { act: 'terminate', reason, state };
      }
      if (this.shouldWarn(now, state)) {
        return { act: 'warn', reason, state: { ...state, warnedAt: now } };
      }
      return { act: 'none', reason, state };
    }

    if (!this.shouldWarn(now, state)) {
      return { act: 'none', reason, state };
    }
    return { act: 'warn', reason, state: { ...state, warnedAt: now } };
  },

  // The first violation always warns. After that it is repeated on the
  // interval, or not at all when no interval is set.
  shouldWarn(now, state) {
    if (!state.warnedAt) return true;
    const interval = this.reWarnIntervalMinutes * 60000;
    if (interval <= 0) return false;
    return now - state.warnedAt >= interval;
  },

  async check(now = Date.now()) {
    if (!this.enabled) return;
    const state = await this.loadState();
    const decision = this.decide(now, state);
    await this.saveState(decision.state);
    if (decision.act === 'warn') {
      await this.warn(decision.reason, decision.state.terminateAt);
      return;
    }
    if (decision.act === 'terminate') {
      await this.terminate();
    }
  },

  // The countdown on the warning page runs to the second, so it reports the
  // deadline itself rather than waiting for the next alarm.
  async onDeadlineReached() {
    if (!this.enabled) return;
    const state = await this.loadState();
    if (!state.terminateAt || Date.now() < state.terminateAt) return;
    await this.terminate();
  },

  async terminate() {
    const windows = await chrome.windows.getAll();
    await Promise.all(windows.map(
      window => chrome.windows.remove(window.id).catch(() => {})
    ));
  },

  warningPageUrl(reason, terminateAt) {
    const url = new URL(chrome.runtime.getURL('time-limit.html'));
    url.searchParams.set('reason', reason);
    if (terminateAt) url.searchParams.set('deadline', String(terminateAt));
    return url.toString();
  },

  // The warning goes in a tab of its own rather than over whatever the user is
  // reading. Repeated warnings reuse that tab so they do not pile up.
  async warn(reason, terminateAt) {
    const url = this.warningPageUrl(reason, terminateAt);
    const state = await this.loadState();
    const tab = await this.openWarningTab(state.warnTabId, url);
    if (tab?.id !== state.warnTabId) {
      await this.saveState({ ...(await this.loadState()), warnTabId: tab?.id ?? 0 });
    }
    // Making the tab active only brings it to the front of its own window, so
    // the window itself is raised too. Otherwise a warning can be left behind
    // a minimised or background window.
    if (!tab?.windowId) return;
    try {
      await chrome.windows.update(tab.windowId, { focused: true });
    } catch {
      // The window went away between opening the tab and raising it.
    }
  },

  async openWarningTab(warnTabId, url) {
    if (warnTabId) {
      try {
        return await chrome.tabs.update(warnTabId, { url, active: true });
      } catch {
        // The tab was closed in the meantime, so a new one is opened below.
      }
    }
    return chrome.tabs.create({ url, active: true });
  },

  // Session storage is cleared when the browser restarts, so a missing start
  // time means this run has only just begun.
  async startSession(now = Date.now()) {
    const state = await this.loadState();
    if (state.sessionStart) return;
    await this.saveState({ ...EMPTY_STATE, sessionStart: now });
  },

  async onStartup(now = Date.now()) {
    await this.saveState({ ...EMPTY_STATE, sessionStart: now });
  },

  async loadState() {
    const stored = await chrome.storage.session.get(STATE_KEY);
    return { ...EMPTY_STATE, ...(stored?.[STATE_KEY] ?? {}) };
  },

  async saveState(state) {
    await chrome.storage.session.set({ [STATE_KEY]: state });
  },
}
