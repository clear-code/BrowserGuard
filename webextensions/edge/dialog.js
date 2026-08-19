'use strict';

import { SERVER_NAME } from './constants.js';

// The native host puts a warning up as a dialog of its own. A browser
// notification is at the mercy of the operating system's notification settings,
// and a page of its own takes up a tab and can be styled away by the site.

// The host answers only once the dialog is dismissed, so without this one
// dialog would pile up behind another.
let showing = false;

export async function showDialog(text) {
  if (showing) return false;
  showing = true;
  try {
    await chrome.runtime.sendNativeMessage(SERVER_NAME, { message: `W ${text}` });
    return true;
  } catch (error) {
    // Swallowing this silently would hide a warning going missing, which looks
    // exactly like the feature not working at all.
    console.log('Cannot show the dialog', JSON.stringify(error?.message));
    return false;
  } finally {
    showing = false;
  }
}
