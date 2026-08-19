'use strict';

import { loadConfig } from './config-loader.js';

// The service worker is unloaded when idle, so which tab carries the warning is
// kept in session storage rather than in memory.
const STATE_KEY = 'tabCountLimitState';

const EMPTY_STATE = {
  warnTabId: 0,
};

export const TabCountLimit = {
  enabled: false,
  maxCount: 0,
  // Opening the warning creates a tab, which reports a tab was created and
  // would take that very tab away again. Checks are ignored while that happens.
  opening: false,

  async init() {
    const config = await loadConfig();
    this.applyConfig(config?.TabCountLimit);
    if (!this.enabled) return;
    // A restored session opens its tabs before the config has been read, so the
    // count is brought back to the limit once it is known.
    await this.check();
  },

  applyConfig(tabCountLimit) {
    if (!tabCountLimit) return;
    if (typeof tabCountLimit.Enabled === 'boolean') {
      this.enabled = tabCountLimit.Enabled;
    }
    if (Number.isFinite(tabCountLimit.MaxCount)) {
      this.maxCount = tabCountLimit.MaxCount;
    }
  },

  // Which of the tabs have to go, kept free of chrome.* so it can be tested
  // directly.
  excessTabs(tabs) {
    if (!this.enabled || this.maxCount <= 0) return [];
    if (tabs.length <= this.maxCount) return [];
    // Tab ids grow as tabs are created, so the highest ids belong to the tabs
    // opened most recently. Those are the ones taken away, leaving what the
    // user already had open untouched. The limit is at least one tab, so the
    // last tab, and with it the browser, can never be closed.
    return [...tabs]
      .sort((left, right) => right.id - left.id)
      .slice(0, tabs.length - this.maxCount);
  },

  async check() {
    if (!this.enabled) return;
    if (this.opening) return;
    const state = await this.loadState();
    const tabs = await this.countableTabs(state.warnTabId);
    const excess = this.excessTabs(tabs);
    if (excess.length === 0) return;
    await this.closeTabs(excess);
    await this.warn(excess.length);
  },

  async countableTabs(warnTabId) {
    const tabs = await chrome.tabs.query({});
    // The warning tab is opened by this module, so it is neither counted as one
    // of the user's tabs nor closed as one of them.
    return tabs.filter(tab => tab.id !== warnTabId);
  },

  async closeTabs(tabs) {
    await Promise.all(tabs.map(
      tab => chrome.tabs.remove(tab.id).catch(() => {})
    ));
  },

  warningPageUrl(closedCount) {
    const url = new URL(chrome.runtime.getURL('tab-limit.html'));
    url.searchParams.set('max', String(this.maxCount));
    url.searchParams.set('closed', String(closedCount));
    return url.toString();
  },

  async warn(closedCount) {
    const url = this.warningPageUrl(closedCount);
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

  async loadState() {
    const stored = await chrome.storage.session.get(STATE_KEY);
    return { ...EMPTY_STATE, ...(stored?.[STATE_KEY] ?? {}) };
  },

  async saveState(state) {
    await chrome.storage.session.set({ [STATE_KEY]: state });
  },
}
