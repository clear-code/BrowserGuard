'use strict';

import { SERVER_NAME } from './constants.js';

export const UploadFileBridge = {
  onBeforeRequest(details) {
    if (!details.requestBody?.raw) return;
    for (const part of details.requestBody.raw) {
      if (part.file) {
        this.bridgeFile(part.file);
      }
    }
  },

  async bridgeFile(path) {
    const query = 'S ' + path;
    try {
      const resp = await chrome.runtime.sendNativeMessage(SERVER_NAME, query);
      if (!resp) {
        console.log('No response from native host', query);
      }
      else if (resp.result === 'OK') {
        console.log('File copied successfully', query);
      } else {
        console.log('Failed to copy file', query, resp);
      }
    } catch (e) {
      console.log('Cannot copy file', query, e.message);
    }
  }
}
