'use strict';

export const StartupLauncher = {
  BROWSER: 'edge',
  SERVER_NAME: 'com.clear_code.browser_startup_launcher',
  onStartup() {
    const query = 'Q ' + this.BROWSER;
    chrome.runtime.sendNativeMessage(
      this.SERVER_NAME,
      query,
      (response) => {
        if (chrome.runtime.lastError) {
          console.error(chrome.runtime.lastError.message);
        }
      }
    );
  },
}
