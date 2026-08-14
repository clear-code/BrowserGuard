'use strict';

import { SERVER_NAME } from './constants.js';

// A log entry arrives for every request, and sendNativeMessage starts a host
// process per message, so the entries go over a port instead: one process for
// as long as it is connected.
//
// The port is opened on the first entry rather than at start up. The service
// worker is unloaded when idle, which takes the port and the host process with
// it; the next entry simply opens a new one.
export const NetLogPort = {
  port: null,

  connect() {
    if (this.port) return this.port;

    const port = chrome.runtime.connectNative(SERVER_NAME);
    port.onDisconnect.addListener(() => {
      // Only forget it if it is still the one in use, so a disconnect arriving
      // late cannot discard a port opened after it.
      if (this.port === port) this.port = null;
      const error = chrome.runtime.lastError;
      if (error) console.error('Net log port disconnected', error.message);
    });
    // The host answers only when it could not write the entry.
    port.onMessage.addListener(response => {
      if (response?.Success === false) {
        console.error('Cannot write the log entry', response.Error);
      }
    });

    this.port = port;
    return port;
  },

  send(entry) {
    try {
      this.connect().postMessage({ message: 'L ' + JSON.stringify(entry) });
      return true;
    } catch (error) {
      // Opening the port fails when the host is not registered, and posting
      // fails on a port that has just gone away. Either way the next entry
      // starts over rather than reusing something broken.
      this.port = null;
      console.error('Cannot reach the native host', error?.message);
      return false;
    }
  },
}
