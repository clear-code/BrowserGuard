'use strict';

import { describe, it, beforeEach } from 'node:test';
import assert from 'node:assert/strict';

// net-logger fans an entry out to an endpoint, to the native host, or to both,
// so both destinations are stubbed and the calls recorded.
const calls = { posted: [], hosted: [] };

globalThis.chrome = {
  runtime: {
    connectNative: () => ({
      onDisconnect: { addListener: () => {} },
      onMessage: { addListener: () => {} },
      postMessage: message => calls.hosted.push(message),
    }),
  },
};

globalThis.fetch = (url, options) => {
  calls.posted.push({ url, body: JSON.parse(options.body) });
  return Promise.resolve({ ok: true });
};

const { NetLogger } = await import('../edge/net-logger.js');

const CONFIG = {
  Enabled: true,
  Endpoint: 'https://collector.example.com/log',
  MachineName: 'PC-1',
  UserName: 'user1',
  LocalFile: { Enabled: true },
};

function config(overrides = {}) {
  return { ...CONFIG, ...overrides };
}

const PAYLOAD = { operation: 'browsing', url: 'https://example.com/' };

beforeEach(() => {
  calls.posted = [];
  calls.hosted = [];
});

describe('_targets', () => {
  it('sends to both when both are configured', () => {
    assert.deepEqual(NetLogger._targets(config()), { endpoint: true, localFile: true });
  });

  it('sends to the endpoint alone', () => {
    assert.deepEqual(
      NetLogger._targets(config({ LocalFile: { Enabled: false } })),
      { endpoint: true, localFile: false });
  });

  // Local logging used to be impossible without an endpoint as well.
  it('sends to the file alone', () => {
    assert.deepEqual(
      NetLogger._targets(config({ Endpoint: '' })),
      { endpoint: false, localFile: true });
  });

  it('records nothing when neither is configured', () => {
    assert.equal(NetLogger._targets(config({ Endpoint: '', LocalFile: { Enabled: false } })), null);
    assert.equal(NetLogger._targets(config({ Endpoint: '', LocalFile: undefined })), null);
  });

  // The entry carries these, so there is nothing worth recording without them.
  it('records nothing without a machine or a user', () => {
    assert.equal(NetLogger._targets(config({ MachineName: '' })), null);
    assert.equal(NetLogger._targets(config({ UserName: '' })), null);
  });

  // Enabled turns the whole feature off, whatever the destinations say.
  it('records nothing while disabled', () => {
    assert.equal(NetLogger._targets(config({ Enabled: false })), null);
    assert.equal(NetLogger._targets(config({ Enabled: undefined })), null);
  });

  it('records nothing when there is no configuration at all', () => {
    assert.equal(NetLogger._targets(undefined), null);
    assert.equal(NetLogger._targets(null), null);
  });
});

describe('_send', () => {
  it('posts to the endpoint and hands the entry to the host', async () => {
    await NetLogger._send(config(), PAYLOAD);

    assert.equal(calls.posted.length, 1);
    assert.equal(calls.posted[0].url, CONFIG.Endpoint);
    assert.deepEqual(calls.posted[0].body, PAYLOAD);
    assert.deepEqual(calls.hosted, [{ message: 'L ' + JSON.stringify(PAYLOAD) }]);
  });

  it('leaves the host alone when only the endpoint is configured', async () => {
    await NetLogger._send(config({ LocalFile: { Enabled: false } }), PAYLOAD);

    assert.equal(calls.posted.length, 1);
    assert.deepEqual(calls.hosted, []);
  });

  it('leaves the endpoint alone when only the file is configured', async () => {
    await NetLogger._send(config({ Endpoint: '' }), PAYLOAD);

    assert.deepEqual(calls.posted, []);
    assert.equal(calls.hosted.length, 1);
  });

  it('does nothing when nothing is configured', async () => {
    await NetLogger._send(config({ Endpoint: '', LocalFile: { Enabled: false } }), PAYLOAD);

    assert.deepEqual(calls.posted, []);
    assert.deepEqual(calls.hosted, []);
  });
});
