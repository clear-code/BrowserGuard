'use strict';

import { SERVER_NAME, BROWSER } from './constants.js';

export const StartupLauncher = {
  onStartup() {
    const query = 'Q ' + BROWSER;
    chrome.runtime.sendNativeMessage(
      SERVER_NAME,
      query,
      (response) => {
        if (chrome.runtime.lastError) {
          console.error(chrome.runtime.lastError.message);
        }
      }
    );
  },
}
