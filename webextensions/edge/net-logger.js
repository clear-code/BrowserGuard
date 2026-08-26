'use strict';

import { loadConfig } from './config-loader.js';
import { NetLogPort } from './net-log-port.js';

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

  // Whether an entry is worth handing to the host at all. The host decides
  // where it then goes, so this only asks whether it has anywhere to put it.
  _shouldLog(netLogger) {
    if (!netLogger?.Enabled) return false;
    return Boolean(netLogger.Sender?.Enabled && netLogger.Sender?.Endpoint) ||
           Boolean(netLogger.LocalFile?.Enabled);
  },

  async _getConfig() {
    const config = await loadConfig();
    const netLogger = config?.NetLogger;
    return this._shouldLog(netLogger) ? netLogger : null;
  },

  // Everything goes to the host, including what is bound for the collector.
  // Posting from here would put the log into the browser's own traffic, where
  // webRequest would see it and log it again.
  _send(config, payload) {
    if (!this._shouldLog(config)) return;
    NetLogPort.send(payload);
  },

  // The machine and the user are added by the host, which is the only side
  // that knows them for certain.
  _buildPayload(operation, name, url, timestamp, extra) {
    return {
      operation,
      name,
      url,
      timestamp: this.formatLocal(timestamp),
      ...extra,
    };
  },

  // Records an event the caller names, in the same shape as _buildPayload but
  // handing it straight on. The individual switches (Upload, Browsing and the
  // rest) belong to the traffic this module watches for itself, so what is
  // handed in here is recorded whenever the logger is on at all: it comes from
  // an action that was refused or that failed, not from ordinary browsing.
  //
  // Callers are blocking listeners that cannot wait for the config, so they do
  // not await this.
  async record(operation, name, url, timestamp, extra) {
    const config = await this._getConfig();
    if (!config) return;

    this._send(config, this._buildPayload(operation, name, url, timestamp, extra));
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
          this._send(config,
            this._buildPayload('upload',
              part.file.split(/[/\\]/).pop() || '',
              uploadUrl,
              details.timeStamp));
        }
      }
    }

    if (config.UrlAccess && (url.protocol === 'http:' || url.protocol === 'https:')) {
      this._send(config,
        this._buildPayload('urlaccess',
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
      this._send(config,
        this._buildPayload('browsing',
          tab.title || url.hostname,
          url.href,
          details.timeStamp));
    }
  },

  async onPrint(msg) {
    const config = await this._getConfig();
    if (!config) return;

    if (config.Print) {
      this._send(config,
        this._buildPayload('print',
          msg.title || '',
          msg.url,
          msg.timestamp));
    }
  },

  async onDownloadChanged(delta) {
    if (delta.state?.current !== 'complete') return;

    const config = await this._getConfig();
    if (!config) return;

    const [item] = await chrome.downloads.search({ id: delta.id });
    if (!item) return;

    if (config.Download) {
      this._send(config,
        this._buildPayload('download',
          item.filename || '',
          item.url || '',
          item.startTime));
    }
  },
};


