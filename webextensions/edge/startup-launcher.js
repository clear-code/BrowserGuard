'use strict';

import { SERVER_NAME, BROWSER } from './constants.js';

export const StartupLauncher = {
  onStartup() {
    const query = 'Q ' + BROWSER;
    // sendNativeMessage only accepts an object, so the command is wrapped.
    chrome.runtime.sendNativeMessage(
      SERVER_NAME,
      { message: query },
      (_response) => {
        if (chrome.runtime.lastError) {
          console.error(chrome.runtime.lastError.message);
        }
      }
    );
  },
}
