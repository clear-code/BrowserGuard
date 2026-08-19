'use strict';

import { loadConfig } from './config-loader.js';
import { SERVER_NAME } from './constants.js';

export const TabCountLimit = {
  enabled: false,
  maxCount: 0,
  warning: false,

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
    await this.warn(excess.length);
  },

  async closeTabs(tabs) {
    await Promise.all(tabs.map(
      tab => chrome.tabs.remove(tab.id).catch(() => {})
    ));
  },

  // The native host puts the warning up as a dialog of its own. A browser
  // notification is at the mercy of the operating system's notification
  // settings, and a page of its own would take up a tab, which is the very
  // thing being limited.
  async warn(closedCount) {
    // The host answers only once the dialog is dismissed, so without this a
    // dialog would pile up for every tab opened behind the one already there.
    if (this.warning) return;
    this.warning = true;
    const text =
      `同時に開くことのできるタブの数は ${this.maxCount} 個までです。\n` +
      `超過して開かれたタブ ${closedCount} 個を閉じました。`;
    try {
      await chrome.runtime.sendNativeMessage(SERVER_NAME, { message: `W ${text}` });
    } catch (error) {
      // Swallowing this silently would hide the warning going missing, which is
      // exactly what a limit the user cannot see would look like.
      console.log('Cannot show the tab limit warning', JSON.stringify(error?.message));
    } finally {
      this.warning = false;
    }
  },
}
