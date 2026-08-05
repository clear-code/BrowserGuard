'use strict';

import { loadConfig } from './config-loader.js';

export const SettingPageFilter = {
  enabled: true,
  notifyOnBlocked: true,
  blockedPrefixes: [
    'edge://settings',
    'edge://extensions',
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

  // onBeforeNavigate cannot cancel or redirect the way a blocking webRequest
  // listener can, so the tab is sent somewhere else instead. The explanation is
  // a page bundled with the extension rather than a data: URL, because Chromium
  // restricts top frame navigation to data: URLs.
  blockedPageUrl(blockedUrl) {
    return chrome.runtime.getURL('blocked.html') +
      '?url=' + encodeURIComponent(blockedUrl);
  },

  onBeforeNavigate(details) {
    if (details.frameId !== 0) return;
    if (!this.enabled) return;
    if (!this.isBlockedUrl(details.url)) return;

    if (this.notifyOnBlocked) {
      chrome.tabs.update(details.tabId, { url: this.blockedPageUrl(details.url) });
      return;
    }

    chrome.tabs.goBack(details.tabId).catch(() =>
      chrome.tabs.update(details.tabId, { url: 'about:blank' })
    );
  },
}

