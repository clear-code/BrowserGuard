'use strict';

import { UploadGuard } from './upload-guard.js';
import { StartupLauncher } from './startup-launcher.js';
import { UploadFileBridge } from './upload-file-bridge.js';
import { NetLogger } from './net-logger.js';
import { SettingPageFilter } from './setting-page-filter.js';
import { UsageTimeLimit } from './usage-time-limit.js';

// The URL prefixes come from the config, so register without a filter and
// decide inside the handler instead.
SettingPageFilter.init();
chrome.webNavigation.onBeforeNavigate.addListener(
  SettingPageFilter.onBeforeNavigate.bind(SettingPageFilter)
);

UploadGuard.init();
chrome.webRequest.onBeforeRequest.addListener(
  UploadGuard.onBeforeRequest.bind(UploadGuard),
  { urls: ["<all_urls>"] },
  ["blocking", "requestBody"]
);

chrome.webRequest.onBeforeRequest.addListener(
  UploadFileBridge.onBeforeRequest.bind(UploadFileBridge),
  { urls: ["<all_urls>"] },
  ["requestBody"]
);

chrome.runtime.onStartup.addListener(() => {
  StartupLauncher.onStartup.bind(StartupLauncher)();
  UsageTimeLimit.onStartup();
});

// The service worker is unloaded when idle, so the limits are re-evaluated
// from stored state on an alarm rather than from a timer held in memory.
UsageTimeLimit.init();

chrome.alarms.onAlarm.addListener(alarm => {
  if (alarm.name !== UsageTimeLimit.ALARM_NAME) return;
  UsageTimeLimit.check();
});

NetLogger.init();

chrome.webNavigation.onCompleted.addListener(
  NetLogger.onNavigationCompleted.bind(NetLogger)
);

chrome.webRequest.onBeforeRequest.addListener(
  NetLogger.onBeforeRequest.bind(NetLogger),
  { urls: ['<all_urls>'] },
  ['requestBody']
);

chrome.downloads.onChanged.addListener(
  NetLogger.onDownloadChanged.bind(NetLogger)
);

chrome.runtime.onMessage.addListener((msg, _sender) => {
  if (msg.type === 'print') {
    NetLogger.onPrint(msg);
  }
  if (msg.type === 'usage-time-limit:expired') {
    UsageTimeLimit.onDeadlineReached();
  }
});

chrome.webRequest.onAuthRequired.addListener(
  NetLogger.onAuthRequired.bind(NetLogger),
  { urls: ['<all_urls>'] }
);
