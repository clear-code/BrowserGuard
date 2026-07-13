'use strict';

import { UploadGuard } from './upload-guard.js';

chrome.webRequest.onBeforeRequest.addListener(
  UploadGuard.onBeforeRequest.bind(UploadGuard),
  { urls: ["<all_urls>"] },
  ["blocking", "requestBody"]
);