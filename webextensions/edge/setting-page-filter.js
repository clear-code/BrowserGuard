'use strict';

import { loadConfig } from './config-loader.js';
import { showDialog } from './dialog.js';

export const SettingPageFilter = {
  enabled: false,
  notifyOnBlocked: true,
  blockedPrefixes: [
    'edge://settings',
    //'edge://extensions',
    'edge://flags',
    'edge://policy',
  ],

  async init() {
    const config = await loadConfig();
    this.applyConfig(config?.SettingPageFilter);
  },

  // Separated from init so that it can be exercised without the browser.
  applyConfig(settingPageFilter) {
    if (!settingPageFilter) return;
    if (typeof settingPageFilter.Enabled === 'boolean') {
      this.enabled = settingPageFilter.Enabled;
    }
    if (typeof settingPageFilter.NotifyOnBlocked === 'boolean') {
      this.notifyOnBlocked = settingPageFilter.NotifyOnBlocked;
    }
    if (Array.isArray(settingPageFilter.BlockedPrefixes)) {
      this.blockedPrefixes = settingPageFilter.BlockedPrefixes;
    }
  },

  isBlockedUrl(url) {
    return this.blockedPrefixes.some(prefix => url.startsWith(prefix));
  },

  warningText(blockedUrl) {
    return `アクセスがブロックされました:\n${blockedUrl}\n\n` +
      '拡張機能のポリシーにより、このページは表示できません。';
  },

  onBeforeNavigate(details) {
    if (details.frameId !== 0) return;
    if (!this.enabled) return;
    if (!this.isBlockedUrl(details.url)) return;

    if (this.notifyOnBlocked) {
      // Not awaited: the tab is taken off the blocked address at once, while
      // the dialog stands until it is dismissed.
      showDialog(this.warningText(details.url));
    }

    // onBeforeNavigate cannot cancel or redirect the way a blocking webRequest
    // listener can, so the tab is sent back where it came from instead.
    chrome.tabs.goBack(details.tabId).catch(() =>
      chrome.tabs.update(details.tabId, { url: 'about:blank' })
    );
  },
}

