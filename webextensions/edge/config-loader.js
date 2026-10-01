/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 ClearCode Inc.
*/

'use strict';

import { SERVER_NAME, BROWSER } from './constants.js';

// Fetch the config from the native host. Shared by every module and fetched
// only once, so the native host process is not spawned repeatedly.
let configPromise = null;

export function loadConfig() {
  return configPromise ??= fetchConfig();
}

async function fetchConfig() {
  const query = 'C ' + BROWSER;
  try {
    // sendNativeMessage only accepts an object, so the command is wrapped.
    const resp = await chrome.runtime.sendNativeMessage(SERVER_NAME, { message: query });
    if (!resp) {
      console.log('Cannot fetch config: empty response');
      return null;
    }
    console.log('Fetch config', JSON.stringify(resp.Config));
    return resp.Config;
  } catch (error) {
    console.log('Cannot fetch config', JSON.stringify(error?.message));
    return null;
  }
}
