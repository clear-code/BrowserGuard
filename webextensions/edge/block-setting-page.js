'use strict';

import { loadConfig } from './config-loader.js';

export const BlockSettingPage = {
  enabled: true,
  urlPrefixes: ['edge://settings/'],

  async init() {
    const config = await loadConfig();
    const blockSettingPage = config?.BlockSettingPage;
    if (!blockSettingPage) return;
    if (typeof blockSettingPage.Enabled === 'boolean') {
      this.enabled = blockSettingPage.Enabled;
    }
    if (Array.isArray(blockSettingPage.UrlPrefixes)) {
      this.urlPrefixes = blockSettingPage.UrlPrefixes;
    }
  },

  isBlockedUrl(url) {
    return this.urlPrefixes.some(prefix => url.startsWith(prefix));
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

