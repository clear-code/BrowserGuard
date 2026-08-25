'use strict';

import { loadConfig } from './config-loader.js';
import { showDialog } from './dialog.js';
import { readBoolean, readNumber } from './config-value.js';

// The service worker is not kept alive, so the deadline is recomputed from
// stored state on every alarm rather than held in a timer.
const ALARM_NAME = 'usage-time-limit';
// A dialog cannot count down the way the warning page used to, so the deadline
// gets an alarm of its own rather than waiting for the next minute's check.
const DEADLINE_ALARM_NAME = 'usage-time-limit-deadline';
const STATE_KEY = 'usageTimeLimitState';
const CHECK_INTERVAL_MINUTES = 1;

const EMPTY_STATE = {
  sessionStart: 0,
  warnedAt: 0,
  terminateAt: 0,
};

const REASONS = {
  continuous: '連続して使用できる時間の上限に達しました。',
  schedule: '使用が許可された時間帯を過ぎています。',
};

// Local time, to the second: a dialog that names the wait rather than the time
// would be wrong the moment it is left standing.
function formatClock(at) {
  const time = new Date(at);
  return [time.getHours(), time.getMinutes(), time.getSeconds()]
    .map(part => String(part).padStart(2, '0'))
    .join(':');
}

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
  DEADLINE_ALARM_NAME,

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
    this.enabled = readBoolean(usageTimeLimit, 'Enabled', this.enabled);
    this.maxContinuousMinutes =
      readNumber(usageTimeLimit, 'MaxContinuousMinutes', this.maxContinuousMinutes);
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
    this.graceSeconds = readNumber(onExceeded, 'GraceSeconds', this.graceSeconds);
    this.reWarnIntervalMinutes =
      readNumber(onExceeded, 'ReWarnIntervalMinutes', this.reWarnIntervalMinutes);
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
    await this.scheduleDeadline(decision.state.terminateAt);
    if (decision.act === 'warn') {
      await this.warn(decision.reason, decision.state.terminateAt);
      return;
    }
    if (decision.act === 'terminate') {
      await this.terminate();
    }
  },

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

  warningText(reason, terminateAt) {
    const lines = [REASONS[reason] ?? '使用時間の制限を超過しました。'];
    if (!terminateAt) {
      lines.push('作業中の内容を保存して、ブラウザーを終了してください。');
      return lines.join('\n');
    }
    lines.push(`${formatClock(terminateAt)} にブラウザーを終了します。`);
    lines.push('作業中の内容をすぐに保存してください。');
    return lines.join('\n');
  },

  async warn(reason, terminateAt) {
    await showDialog(this.warningText(reason, terminateAt));
  },

  // The browser has to close on time whether or not the dialog was dismissed,
  // so the deadline is kept as an alarm rather than left to the warning.
  async scheduleDeadline(terminateAt) {
    if (!terminateAt) {
      await chrome.alarms.clear(DEADLINE_ALARM_NAME);
      return;
    }
    await chrome.alarms.create(DEADLINE_ALARM_NAME, { when: terminateAt });
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
