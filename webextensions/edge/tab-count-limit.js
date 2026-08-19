'use strict';

import { loadConfig } from './config-loader.js';

// The service worker is unloaded when idle, so which warning is already up is
// kept in session storage rather than in memory.
const STATE_KEY = 'tabCountLimitState';

const EMPTY_STATE = {
  // The tab this module opened to carry the warning, so it can be reused.
  warnTabId: 0,
  // The number of tabs the last warning reported, so that the warning is not
  // put back up until the user opens yet another one.
  warnedCount: 0,
};

export const TabCountLimit = {
  enabled: false,
  maxCount: 0,

  // Opening the warning creates a tab, which reports a tab was created and
  // would ask for another warning. Checks are ignored while that happens.
  opening: false,

  async init() {
    const config = await loadConfig();
    this.applyConfig(config?.TabCountLimit);
    if (!this.enabled) return;
    // The worker may have been restarted with the tabs already over the limit.
    await this.check();
  },

  // Separated from init so that it can be exercised without the browser.
  applyConfig(tabCountLimit) {
    if (!tabCountLimit) return;
    if (typeof tabCountLimit.Enabled === 'boolean') {
      this.enabled = tabCountLimit.Enabled;
    }
    if (Number.isFinite(tabCountLimit.MaxCount)) {
      this.maxCount = tabCountLimit.MaxCount;
    }
  },

  // The whole decision, kept free of chrome.* so it can be tested directly.
  // Returns what to do now and the state to persist.
  decide(count, state) {
    // No limit at zero, so a config that leaves the number out cannot start
    // warning about every single tab.
    if (!this.enabled || this.maxCount <= 0) return { act: 'none', state };

    if (count <= this.maxCount) {
      // Back inside the limit, so the warning is taken away and a later
      // violation warns again from scratch. A count of zero means the warning
      // is the only tab left, and closing it would close the browser.
      if (count > 0 && state.warnTabId) {
        return { act: 'dismiss', state: { ...EMPTY_STATE } };
      }
      return { act: 'none', state: { ...state, warnedCount: 0 } };
    }

    // The user has already been told about this many tabs. Warning again on
    // every check would interrupt the very act of closing tabs, so the warning
    // waits until the count grows further.
    if (count <= state.warnedCount) return { act: 'none', state };

    return { act: 'warn', state: { ...state, warnedCount: count } };
  },

  async check() {
    if (!this.enabled) return;
    if (this.opening) return;
    const state = await this.loadState();
    const count = await this.countTabs(state.warnTabId);
    const decision = this.decide(count, state);
    await this.saveState(decision.state);
    if (decision.act === 'warn') {
      await this.warn(count);
      return;
    }
    if (decision.act === 'dismiss') {
      await this.dismiss(state.warnTabId);
    }
  },

  // Every window is counted together, so tabs spread over several windows are
  // held to the same limit as tabs kept in one.
  async countTabs(warnTabId) {
    const tabs = await chrome.tabs.query({});
    // The warning tab is opened by this module, so it is not counted as one of
    // the tabs the user has open.
    return tabs.filter(tab => tab.id !== warnTabId).length;
  },

  warningPageUrl(count) {
    const url = new URL(chrome.runtime.getURL('tab-limit.html'));
    url.searchParams.set('count', String(count));
    url.searchParams.set('max', String(this.maxCount));
    return url.toString();
  },

  // The warning goes in a tab of its own rather than over whatever the user is
  // reading. Repeated warnings reuse that tab so they do not pile up.
  async warn(count) {
    const url = this.warningPageUrl(count);
    const state = await this.loadState();
    let tab = null;
    this.opening = true;
    try {
      tab = await this.openWarningTab(state.warnTabId, url);
      if (tab?.id !== state.warnTabId) {
        await this.saveState({ ...(await this.loadState()), warnTabId: tab?.id ?? 0 });
      }
    } finally {
      this.opening = false;
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

  // Only the warning this module put up is taken away. A tab the user has
  // since navigated somewhere else is theirs, and is left alone.
  async dismiss(warnTabId) {
    if (!warnTabId) return;
    try {
      const tab = await chrome.tabs.get(warnTabId);
      const warningPage = chrome.runtime.getURL('tab-limit.html');
      if (!tab?.url?.startsWith(warningPage)) return;
      await chrome.tabs.remove(warnTabId);
    } catch {
      // The tab was already closed.
    }
  },

  async loadState() {
    const stored = await chrome.storage.session.get(STATE_KEY);
    return { ...EMPTY_STATE, ...(stored?.[STATE_KEY] ?? {}) };
  },

  async saveState(state) {
    await chrome.storage.session.set({ [STATE_KEY]: state });
  },
}
