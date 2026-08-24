'use strict';

import { describe, it, beforeEach } from 'node:test';
import assert from 'node:assert/strict';

// The bridge hands the file to the host over a one-shot native message, so a
// stub stands in for the host and records what it was asked to copy.
const asked = [];

globalThis.chrome = {
  runtime: {
    sendNativeMessage: (_server, payload) => {
      asked.push(payload.message);
      return Promise.resolve({ Success: true });
    },
  },
};

const { UploadFileBridge } = await import('../edge/upload-file-bridge.js');

const URL_ = 'https://example.com/upload';

// What the host is handed, read back as the object the bridge built.
const sent = () => asked.map(message => JSON.parse(message.slice(2)));

function upload(files, url = URL_) {
  UploadFileBridge.onBeforeRequest({
    url,
    requestBody: { raw: files.map(file => (typeof file === 'string' ? { file } : file)) },
  });
}

beforeEach(() => {
  asked.length = 0;
});

describe('onBeforeRequest', () => {
  it('hands the host the file and where it was going', () => {
    upload(['C:\\tmp\\report.xlsx']);

    assert.equal(asked.length, 1);
    assert.match(asked[0], /^U /);
    assert.deepEqual(sent()[0], { file: 'C:\\tmp\\report.xlsx', url: URL_ });
  });

  // A path may hold spaces, so the two cannot be put either side of one.
  it('keeps a path with spaces in it whole', () => {
    upload(['C:\\My Documents\\quarterly report.xlsx']);

    assert.equal(sent()[0].file, 'C:\\My Documents\\quarterly report.xlsx');
  });

  it('hands over every file in the request', () => {
    upload(['C:\\a\\one.txt', { bytes: 1 }, 'C:\\a\\two.txt']);

    assert.deepEqual(sent().map(one => one.file), ['C:\\a\\one.txt', 'C:\\a\\two.txt']);
  });

  it('asks nothing of the host for a request without files', () => {
    UploadFileBridge.onBeforeRequest({ url: URL_, requestBody: {} });

    assert.deepEqual(asked, []);
  });
});
