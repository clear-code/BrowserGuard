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
    const resp = await chrome.runtime.sendNativeMessage(SERVER_NAME, query);
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
