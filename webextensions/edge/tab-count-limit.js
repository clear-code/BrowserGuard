'use strict';

import { loadConfig } from './config-loader.js';
import { showDialog } from './dialog.js';
import { readBoolean, readNumber } from './config-value.js';

// Somewhere ordinary to send a tab that the browser will not close while it is
// showing the new tab page.
const BLANK_URL = 'about:blank';
const RETRY_INTERVAL_MS = 50;
const RETRY_LIMIT = 10;

const delay = ms => new Promise(resolve => setTimeout(resolve, ms));

export const TabCountLimit = {
  enabled: false,
  maxCount: 0,

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
    this.enabled = readBoolean(tabCountLimit, 'Enabled', this.enabled);
    this.maxCount = readNumber(tabCountLimit, 'MaxCount', this.maxCount);
  },

  excessTabs(tabs) {
    if (!this.enabled || this.maxCount <= 0) return [];
    if (tabs.length <= this.maxCount) return [];
    // Tab ids grow as tabs are created, so the highest ids belong to the tabs
    // opened most recently.
    return [...tabs]
      .sort((left, right) => right.id - left.id)
      .slice(0, tabs.length - this.maxCount);
  },

  async check() {
    if (!this.enabled) return;
    // Every window is counted together, so the limit cannot be worked around by
    // opening another one.
    const tabs = await chrome.tabs.query({});
    const excess = this.excessTabs(tabs);
    if (excess.length === 0) return;
    await this.closeTabs(excess);
    await this.warn();
  },

  async closeTabs(tabs) {
    await Promise.all(tabs.map(tab => this.closeTab(tab)));
  },

  // Edge refuses to close a tab that is showing the new tab page, answering
  // "Cannot remove NTP tab.". Taking it off that page first leaves an ordinary
  // tab, which closes like any other.
  async closeTab(tab) {
    if (await this.removeTab(tab.id)) return true;
    try {
      await chrome.tabs.update(tab.id, { url: BLANK_URL });
    } catch {
      return false;
    }
    // The refusal is about the page the tab is on, so closing is tried again
    // while the blank page takes over from the new tab page.
    for (let attempt = 0; attempt < RETRY_LIMIT; attempt++) {
      await delay(RETRY_INTERVAL_MS);
      if (await this.removeTab(tab.id)) return true;
    }
    console.log('Cannot close the tab', tab.id);
    return false;
  },

  // Whether the tab is gone, however that came about.
  async removeTab(tabId) {
    try {
      await chrome.tabs.remove(tabId);
      return true;
    } catch {
      return chrome.tabs.get(tabId).then(() => false, () => true);
    }
  },

  async warn() {
    await showDialog(
      `同時に開くことのできるタブの数は ${this.maxCount} 個までです。`
    );
  }
}
