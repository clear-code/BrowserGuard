/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 ClearCode Inc.
*/

'use strict';

import { SERVER_NAME, BROWSER } from './constants.js';

export const StartupLauncher = {
  onStartup() {
    const query = 'S ' + BROWSER;
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
