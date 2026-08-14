'use strict';

import { describe, it, beforeEach } from 'node:test';
import assert from 'node:assert/strict';

// A stand in for the port chrome.runtime.connectNative hands back, recording
// what was posted and letting a test fire the listeners the real one would.
const calls = { connected: [], posted: [], errors: [] };
let failToConnect = null;
let failToPost = null;

function makePort() {
  const port = {
    posted: [],
    disconnectListeners: [],
    messageListeners: [],
    onDisconnect: { addListener: fn => port.disconnectListeners.push(fn) },
    onMessage: { addListener: fn => port.messageListeners.push(fn) },
    postMessage: message => {
      if (failToPost) throw new Error(failToPost);
      port.posted.push(message);
      calls.posted.push(message);
    },
    disconnect: () => port.disconnectListeners.forEach(fn => fn()),
    reply: message => port.messageListeners.forEach(fn => fn(message)),
  };
  return port;
}

globalThis.chrome = {
  runtime: {
    lastError: undefined,
    connectNative: name => {
      calls.connected.push(name);
      if (failToConnect) throw new Error(failToConnect);
      return makePort();
    },
  },
};

console.error = (...args) => calls.errors.push(args.join(' '));

const { NetLogPort } = await import('../edge/net-log-port.js');

const ENTRY = { operation: 'browsing', url: 'https://example.com/' };

beforeEach(() => {
  calls.connected = [];
  calls.posted = [];
  calls.errors = [];
  failToConnect = null;
  failToPost = null;
  chrome.runtime.lastError = undefined;
  NetLogPort.port = null;
});

describe('send', () => {
  it('carries the entry as the L command', () => {
    assert.equal(NetLogPort.send(ENTRY), true);

    assert.deepEqual(calls.posted, [{ message: 'L ' + JSON.stringify(ENTRY) }]);
  });

  it('names the native host it connects to', () => {
    NetLogPort.send(ENTRY);

    assert.deepEqual(calls.connected, ['com.clear_code.browser_guard']);
  });

  // Opening a process per entry is the thing the port exists to avoid.
  it('opens the port once and keeps using it', () => {
    NetLogPort.send(ENTRY);
    NetLogPort.send(ENTRY);
    NetLogPort.send(ENTRY);

    assert.equal(calls.connected.length, 1);
    assert.equal(calls.posted.length, 3);
  });

  it('opens a new port after the host has gone away', () => {
    NetLogPort.send(ENTRY);
    NetLogPort.port.disconnect();

    NetLogPort.send(ENTRY);

    assert.equal(calls.connected.length, 2);
    assert.equal(calls.posted.length, 2);
  });

  it('reports that it could not open the port', () => {
    failToConnect = 'host not found';

    assert.equal(NetLogPort.send(ENTRY), false);

    assert.equal(NetLogPort.port, null);
    assert.match(calls.errors.join('\n'), /host not found/);
  });

  // A port can go away between the last entry and this one.
  it('starts over when posting fails', () => {
    NetLogPort.send(ENTRY);
    failToPost = 'port closed';

    assert.equal(NetLogPort.send(ENTRY), false);

    assert.equal(NetLogPort.port, null);
    failToPost = null;
    assert.equal(NetLogPort.send(ENTRY), true);
    assert.equal(calls.connected.length, 2);
  });
});

describe('the port itself', () => {
  it('reports an entry the host could not write', () => {
    NetLogPort.send(ENTRY);

    NetLogPort.port.reply({ Success: false, Error: 'entry is not valid JSON' });

    assert.match(calls.errors.join('\n'), /entry is not valid JSON/);
  });

  // The host stays quiet when it wrote the entry, so nothing should be logged.
  it('says nothing about an entry that was written', () => {
    NetLogPort.send(ENTRY);

    NetLogPort.port.reply(undefined);

    assert.deepEqual(calls.errors, []);
  });

  it('reports why the port was disconnected', () => {
    NetLogPort.send(ENTRY);
    const port = NetLogPort.port;
    chrome.runtime.lastError = { message: 'native host has exited' };

    port.disconnect();

    assert.match(calls.errors.join('\n'), /native host has exited/);
  });

  // A disconnect for the previous port must not discard the current one.
  it('keeps a port opened after an earlier one was closed', () => {
    NetLogPort.send(ENTRY);
    const first = NetLogPort.port;
    first.disconnect();
    NetLogPort.send(ENTRY);
    const second = NetLogPort.port;

    first.disconnect();

    assert.equal(NetLogPort.port, second);
  });
});
