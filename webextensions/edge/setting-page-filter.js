'use strict';

import { loadConfig } from './config-loader.js';

export const SettingPageFilter = {
  enabled: true,
  blockedPrefixes: ['edge://settings/'],

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
    if (Array.isArray(settingPageFilter.BlockedPrefixes)) {
      this.blockedPrefixes = settingPageFilter.BlockedPrefixes;
    }
  },

  isBlockedUrl(url) {
    return this.blockedPrefixes.some(prefix => url.startsWith(prefix));
  },

  onBeforeNavigate(details) {
    if (details.frameId !== 0) return;
    if (!this.enabled) return;
    if (!this.isBlockedUrl(details.url)) return;
    chrome.tabs.goBack(details.tabId).catch(() =>
      chrome.tabs.update(details.tabId, { url: 'about:blank' })
    );
    chrome.notifications.create('settings-blocked', {
      type: 'basic',
      iconUrl: 'misc/128x128.png',
      title: '設定画面へのアクセスはブロックされています',
      message: '拡張機能のポリシーにより edge://settings/ は表示できません。',
    });
  },
}

