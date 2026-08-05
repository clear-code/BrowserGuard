'use strict';

import { UploadGuard } from './upload-guard.js';
import { StartupLauncher } from './startup-launcher.js';
import { UploadFileBridge } from './upload-file-bridge.js';
import { NetLogger } from './net-logger.js';
import { SettingPageFilter } from './setting-page-filter.js';

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
});

chrome.runtime.onStartup.addListener(
  ScriptExecutor.restoreIfNeeded.bind(ScriptExecutor)
);

chrome.runtime.onInstalled.addListener(
  ScriptExecutor.restoreIfNeeded.bind(ScriptExecutor)
);

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
});

chrome.webRequest.onAuthRequired.addListener(
  NetLogger.onAuthRequired.bind(NetLogger),
  { urls: ['<all_urls>'] }
);
