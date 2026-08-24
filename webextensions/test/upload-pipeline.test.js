'use strict';

import { describe, it, beforeEach } from 'node:test';
import assert from 'node:assert/strict';

// The guard and the bridge are the real ones: what is being checked is how
// they are put together, so standing either of them in would check nothing.
// Only the host is stubbed, which is where the bridge's request for a copy and
// the guard's warning both end up.
const asked = [];

globalThis.chrome = {
  runtime: {
    sendNativeMessage: (_server, payload) => {
      asked.push(payload.message);
      return Promise.resolve({ Config: { NetLogger: { Enabled: false } }, Success: true });
    },
    connectNative: () => ({
      onDisconnect: { addListener: () => {} },
      onMessage: { addListener: () => {} },
      postMessage: () => {},
    }),
  },
};

const { UploadPipeline } = await import('../edge/upload-pipeline.js');
const { UploadGuard } = await import('../edge/upload-guard.js');

// What the bridge asked the host to keep a copy of. The guard's warning goes
// out the same way, so the copies are picked out by the command.
const copied = () =>
  asked.filter(message => message.startsWith('U ')).map(message => JSON.parse(message.slice(2)).file);

const EMPTY = {
  Enabled: true,
  BlockedExtensions: [],
  AllowedExtensions: [],
  AllowedPaths: [],
  BlockedPaths: [],
};

function configure(overrides = {}) {
  UploadGuard.applyConfig({ ...EMPTY, ...overrides });
}

function upload(files, type = 'sub_frame') {
  return UploadPipeline.onBeforeRequest({
    type,
    url: 'https://example.com/upload',
    timeStamp: Date.parse('2026-08-07T12:34:56'),
    requestBody: { raw: files.map(file => ({ file })) },
  });
}

beforeEach(() => {
  asked.length = 0;
  configure();
});

describe('onBeforeRequest', () => {
  it('keeps a copy of an upload that went through', () => {
    const response = upload(['C:\\a\\notes.txt']);

    assert.deepEqual(response, {});
    assert.deepEqual(copied(), ['C:\\a\\notes.txt']);
  });

  // A file that never left the machine has no place in the evidence of what
  // did.
  it('keeps no copy of an upload the guard refused', () => {
    configure({ BlockedExtensions: ['.exe'] });

    const response = upload(['C:\\a\\setup.exe']);

    assert.deepEqual(response, { cancel: true });
    assert.deepEqual(copied(), []);
  });

  // The guard refuses the whole request over the one file, so none of them
  // left the machine: copying the others would be evidence of an upload that
  // never happened.
  it('keeps no copy of the files beside a refused one', () => {
    configure({ BlockedExtensions: ['.exe'] });

    upload(['C:\\a\\notes.txt', 'C:\\a\\setup.exe']);

    assert.deepEqual(copied(), []);
  });

  // The guard being off does not mean the copies stop.
  it('keeps copies while the guard is turned off', () => {
    configure({ Enabled: false, BlockedExtensions: ['.exe'] });

    const response = upload(['C:\\a\\setup.exe']);

    assert.deepEqual(response, {});
    assert.deepEqual(copied(), ['C:\\a\\setup.exe']);
  });

  // The main frame is sent back rather than cancelled, which is a refusal all
  // the same.
  it('keeps no copy when the main frame is sent back', () => {
    configure({ BlockedExtensions: ['.exe'] });

    const response = upload(['C:\\a\\setup.exe'], 'main_frame');

    assert.ok(response.redirectUrl);
    assert.deepEqual(copied(), []);
  });

  it('asks for nothing when the request carries no upload', () => {
    const response = UploadPipeline.onBeforeRequest({
      type: 'sub_frame',
      url: 'https://example.com/',
      requestBody: {},
    });

    assert.deepEqual(response, {});
    assert.deepEqual(copied(), []);
  });
});
