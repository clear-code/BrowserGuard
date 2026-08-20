'use strict';

import { loadConfig } from './config-loader.js';
import { showDialog } from './dialog.js';

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
    if (typeof tabCountLimit.Enabled === 'boolean') {
      this.enabled = tabCountLimit.Enabled;
    }
    if (Number.isFinite(tabCountLimit.MaxCount)) {
      this.maxCount = tabCountLimit.MaxCount;
    }
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
    await Promise.all(tabs.map(
      tab => chrome.tabs.remove(tab.id).catch(() => {})
    ));
  },

  async warn() {
    await showDialog(
      `同時に開くことのできるタブの数は ${this.maxCount} 個までです。`
    );
  }
}
