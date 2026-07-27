'use strict';

import { loadConfig } from './config-loader.js';

export const NetLogger = {
  init() {
    console.log('NetLogger initialized');
    loadConfig();
  },

  formatLocal(value) {
    const date = new Date(value);
    const p = (n) => String(n).padStart(2, '0');
    return `${date.getFullYear()}-${p(date.getMonth() + 1)}-${p(date.getDate())} ` +
          `${p(date.getHours())}:${p(date.getMinutes())}:${p(date.getSeconds())}`;
  },

  async _getConfig() {
    const config = await loadConfig();
    const netLogger = config?.NetLogger;
    if (!netLogger?.Endpoint || !netLogger?.MachineName || !netLogger?.UserName) return null;
    return netLogger;
  },

  _buildPayload(config, operation, name, url, timestamp) {
    return {
      operation,
      pcname: config.MachineName,
      userid: config.UserName,
      name,
      url,
      timestamp: this.formatLocal(timestamp),
    };
  },

  async sendToEndpoint(endpoint, data) {
    try {
      await fetch(endpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data),
      });
    } catch (error) {
      console.error('sendToEndpoint failed', error?.message);
    }
  },

  async onBeforeRequest(details) {
    const config = await this._getConfig();
    if (!config) return;

    let url;
    try {
      url = new URL(details.url);
    } catch {
      return;
    }

    if (config.Upload && details.requestBody?.raw) {
      let uploadUrl = url.href;
      if (details.frameId !== 0) {
        const tab = await chrome.tabs.get(details.tabId);
        uploadUrl = tab.url || url.href;
      }
      for (const part of details.requestBody.raw) {
        if (part.file) {
          await this.sendToEndpoint(config.Endpoint,
            this._buildPayload(config, 'upload',
              part.file.split(/[/\\]/).pop() || '',
              uploadUrl,
              details.timeStamp));
        }
      }
    }

    if (config.UrlAccess && (url.protocol === 'http:' || url.protocol === 'https:')) {
      await this.sendToEndpoint(config.Endpoint,
        this._buildPayload(config, 'urlaccess',
          url.hostname,
          url.href,
          details.timeStamp));
    }
  },

  async onNavigationCompleted(details) {
    if (details.frameId !== 0) return;

    let url;
    try {
      url = new URL(details.url);
    } catch {
      return;
    }
    if (url.protocol !== 'http:' && url.protocol !== 'https:') return;

    const config = await this._getConfig();
    if (!config) return;

    if (config.Browsing) {
      const tab = await chrome.tabs.get(details.tabId);
      await this.sendToEndpoint(config.Endpoint,
        this._buildPayload(config, 'browsing',
          tab.title || url.hostname,
          url.href,
          details.timeStamp));
    }
  },

  async onPrint(msg) {
    const config = await this._getConfig();
    if (!config) return;

    if (config.Print) {
      await this.sendToEndpoint(config.Endpoint,
        this._buildPayload(config, 'print',
          msg.title || '',
          msg.url,
          msg.timestamp));
    }
  },

  async onAuthRequired(details) {
    const config = await this._getConfig();
    if (!config) return;

    if (config.Auth) {
      await this.sendToEndpoint(config.Endpoint,
        this._buildPayload(config, 'auth',
          details.scheme || '',
          details.url,
          details.timeStamp));
    }
  },

  async onDownloadChanged(delta) {
    if (delta.state?.current !== 'complete') return;

    const config = await this._getConfig();
    if (!config) return;

    const [item] = await chrome.downloads.search({ id: delta.id });
    if (!item) return;

    if (config.Download) {
      await this.sendToEndpoint(config.Endpoint,
        this._buildPayload(config, 'download',
          item.filename || '',
          item.url || '',
          item.startTime));
    }
  },
};


