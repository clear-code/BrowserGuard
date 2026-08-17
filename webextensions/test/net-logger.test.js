'use strict';

import { describe, it, beforeEach } from 'node:test';
import assert from 'node:assert/strict';

// Everything an entry does now goes through the native host, so the port is
// stubbed and the calls recorded. fetch is stubbed to fail the test if the
// extension ever posts anything itself again.
const calls = { hosted: [], fetched: [] };

globalThis.chrome = {
  runtime: {
    connectNative: () => ({
      onDisconnect: { addListener: () => {} },
      onMessage: { addListener: () => {} },
      postMessage: message => calls.hosted.push(message),
    }),
  },
};

globalThis.fetch = url => {
  calls.fetched.push(url);
  return Promise.resolve({ ok: true });
};

const { NetLogger } = await import('../edge/net-logger.js');

const CONFIG = {
  Enabled: true,
  Endpoint: 'https://collector.example.com/log',
  LocalFile: { Enabled: true },
};

function config(overrides = {}) {
  return { ...CONFIG, ...overrides };
}

const PAYLOAD = { operation: 'browsing', url: 'https://example.com/' };

beforeEach(() => {
  calls.hosted = [];
  calls.fetched = [];
});

describe('_shouldLog', () => {
  it('logs when both destinations are configured', () => {
    assert.equal(NetLogger._shouldLog(config()), true);
  });

  it('logs when only the endpoint is configured', () => {
    assert.equal(NetLogger._shouldLog(config({ LocalFile: { Enabled: false } })), true);
  });

  // Local logging used to be impossible without an endpoint as well.
  it('logs when only the file is configured', () => {
    assert.equal(NetLogger._shouldLog(config({ Endpoint: '' })), true);
  });

  it('logs nothing when the host has nowhere to put it', () => {
    assert.equal(
      NetLogger._shouldLog(config({ Endpoint: '', LocalFile: { Enabled: false } })), false);
    assert.equal(NetLogger._shouldLog(config({ Endpoint: '', LocalFile: undefined })), false);
  });

  // Enabled turns the whole feature off, whatever the destinations say.
  it('logs nothing while disabled', () => {
    assert.equal(NetLogger._shouldLog(config({ Enabled: false })), false);
    assert.equal(NetLogger._shouldLog(config({ Enabled: undefined })), false);
  });

  it('logs nothing when there is no configuration at all', () => {
    assert.equal(NetLogger._shouldLog(undefined), false);
    assert.equal(NetLogger._shouldLog(null), false);
  });
});

describe('_buildPayload', () => {
  it('carries what the browser knows', () => {
    const payload = NetLogger._buildPayload(
      'browsing', 'Example', 'https://example.com/', Date.parse('2026-08-07T12:34:56'));

    assert.equal(payload.operation, 'browsing');
    assert.equal(payload.name, 'Example');
    assert.equal(payload.url, 'https://example.com/');
    assert.equal(payload.timestamp, '2026-08-07 12:34:56');
  });

  // The host stamps these on, and is never told them from here.
  it('leaves the machine and the user to the host', () => {
    const payload = NetLogger._buildPayload('browsing', 'Example', 'https://example.com/', 0);

    assert.equal('pcname' in payload, false);
    assert.equal('userid' in payload, false);
  });
});

describe('_send', () => {
  it('hands the entry to the host', () => {
    NetLogger._send(config(), PAYLOAD);

    assert.deepEqual(calls.hosted, [{ message: 'L ' + JSON.stringify(PAYLOAD) }]);
  });

  // Posting from the extension would put the log into the browser's own
  // traffic, where webRequest sees it and logs it again.
  it('never posts to the collector itself', () => {
    NetLogger._send(config(), PAYLOAD);
    NetLogger._send(config({ LocalFile: { Enabled: false } }), PAYLOAD);

    assert.deepEqual(calls.fetched, []);
    assert.equal(calls.hosted.length, 2);
  });

  it('sends the entry once even with both destinations configured', () => {
    NetLogger._send(config(), PAYLOAD);

    assert.equal(calls.hosted.length, 1);
  });

  it('does nothing when nothing is configured', () => {
    NetLogger._send(config({ Endpoint: '', LocalFile: { Enabled: false } }), PAYLOAD);

    assert.deepEqual(calls.hosted, []);
  });
});
