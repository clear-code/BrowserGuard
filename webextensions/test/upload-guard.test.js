'use strict';

import { describe, it, beforeEach } from 'node:test';
import assert from 'node:assert/strict';

import { UploadGuard } from '../edge/upload-guard.js';

// A blocked upload is reported through net-logger, which reaches the host over
// a port. Both ends are stubbed so the report can be observed.
const reported = [];

globalThis.chrome = {
  runtime: {
    sendNativeMessage: () => Promise.resolve({
      Config: {
        NetLogger: { Enabled: true, Endpoint: 'https://collector.example.com/log' },
      },
    }),
    connectNative: () => ({
      onDisconnect: { addListener: () => {} },
      onMessage: { addListener: () => {} },
      postMessage: message => reported.push(JSON.parse(message.message.slice(2))),
    }),
  },
};

// The report is not awaited by the listener, so it lands a few turns later.
// Every test that blocks an upload reports it, so the one wanted is picked out
// rather than assuming it is the only one to have arrived.
async function waitForReport(match) {
  for (let i = 0; i < 50; i++) {
    const found = reported.find(match);
    if (found) return found;
    await new Promise(resolve => setTimeout(resolve, 10));
  }
  return undefined;
}

// UploadGuard is a singleton, so every test starts from a known configuration
// rather than whatever the previous one left behind.
const EMPTY = {
  Enabled: true,
  BlockedExtensions: [],
  AllowedExtensions: [],
  AllowedPaths: [],
  BlockedPaths: [],
};

function configure(overrides) {
  UploadGuard.applyConfig({ ...EMPTY, ...overrides });
}

function upload(path, type = 'sub_frame') {
  return UploadGuard.onBeforeRequest({
    type,
    url: 'https://example.com/upload',
    timeStamp: Date.parse('2026-08-07T12:34:56'),
    requestBody: { raw: [{ file: path }] },
  });
}

const DOCUMENTS = '^C:\\\\Users\\\\[^\\\\]+\\\\Documents\\\\';

beforeEach(() => configure({}));

describe('getBlockReason', () => {
  it('allows a file when nothing is configured', () => {
    assert.equal(UploadGuard.getBlockReason('C:\\anywhere\\notes.txt'), null);
  });

  it('blocks a blocked extension', () => {
    configure({ BlockedExtensions: ['.exe', '.bat'] });

    assert.equal(UploadGuard.getBlockReason('C:\\a\\setup.exe'), '禁止された拡張子です');
    assert.equal(UploadGuard.getBlockReason('C:\\a\\notes.txt'), null);
  });

  it('allows only the listed extensions once AllowedExtensions is set', () => {
    configure({ AllowedExtensions: ['.pdf', '.docx'] });

    assert.equal(UploadGuard.getBlockReason('C:\\a\\report.pdf'), null);
    assert.equal(UploadGuard.getBlockReason('C:\\a\\notes.txt'), '許可された拡張子ではありません');
  });

  it('allows only the listed paths once AllowedPaths is set', () => {
    configure({ AllowedPaths: [DOCUMENTS] });

    assert.equal(UploadGuard.getBlockReason('C:\\Users\\taro\\Documents\\a.txt'), null);
    assert.equal(
      UploadGuard.getBlockReason('C:\\Users\\taro\\Desktop\\a.txt'),
      'アップロードが許可されていない場所のファイルです');
  });

  it('carves a folder back out of an allowed path', () => {
    configure({ AllowedPaths: [DOCUMENTS], BlockedPaths: ['\\\\Confidential\\\\'] });

    assert.equal(UploadGuard.getBlockReason('C:\\Users\\taro\\Documents\\a.txt'), null);
    assert.equal(
      UploadGuard.getBlockReason('C:\\Users\\taro\\Documents\\Confidential\\a.txt'),
      'アップロードが禁止された場所のファイルです');
    assert.equal(
      UploadGuard.getBlockReason('C:\\Users\\taro\\Documents\\Sub\\Confidential\\a.txt'),
      'アップロードが禁止された場所のファイルです');
  });

  // The pattern is anchored on path separators, so it must not fire on a file
  // whose name merely starts with the same word.
  it('does not treat a file name as a folder', () => {
    configure({ BlockedPaths: ['\\\\Confidential\\\\'] });

    assert.equal(UploadGuard.getBlockReason('C:\\a\\ConfidentialReport.pdf'), null);
  });

  it('matches paths and extensions regardless of case', () => {
    configure({
      AllowedPaths: [DOCUMENTS],
      AllowedExtensions: ['.pdf'],
    });

    assert.equal(UploadGuard.getBlockReason('C:\\USERS\\TARO\\DOCUMENTS\\REPORT.PDF'), null);
  });

  it('lets a blocked rule win over an allowed one', () => {
    configure({
      AllowedPaths: ['^C:\\\\'],
      AllowedExtensions: ['.exe'],
      BlockedExtensions: ['.exe'],
      BlockedPaths: ['^C:\\\\Secret\\\\'],
    });

    assert.equal(UploadGuard.getBlockReason('C:\\Work\\a.exe'), '禁止された拡張子です');
    assert.equal(
      UploadGuard.getBlockReason('C:\\Secret\\a.pdf'),
      'アップロードが禁止された場所のファイルです');
  });
});

describe('applyConfig', () => {
  it('drops an unusable pattern without losing the rest of the list', () => {
    configure({ AllowedPaths: ['^C:\\\\', '[', '^D:\\\\'] });

    assert.equal(UploadGuard.allowedPatterns.length, 2);
    assert.equal(UploadGuard.getBlockReason('C:\\a\\x.txt'), null);
    assert.equal(UploadGuard.getBlockReason('D:\\a\\x.txt'), null);
    assert.notEqual(UploadGuard.getBlockReason('E:\\a\\x.txt'), null);
  });

  it('ignores a missing config rather than clearing everything', () => {
    configure({ BlockedExtensions: ['.exe'] });

    UploadGuard.applyConfig(undefined);

    assert.deepEqual(UploadGuard.blockedExtensions, ['.exe']);
  });
});

describe('onBeforeRequest', () => {
  it('does nothing while disabled', () => {
    configure({ Enabled: false, BlockedExtensions: ['.exe'] });

    assert.deepEqual(upload('C:\\a\\setup.exe'), {});
  });

  it('does nothing for a request without an upload body', () => {
    configure({ BlockedExtensions: ['.exe'] });

    assert.deepEqual(UploadGuard.onBeforeRequest({ type: 'sub_frame' }), {});
  });

  it('lets an allowed file through', () => {
    configure({ BlockedExtensions: ['.exe'] });

    assert.deepEqual(upload('C:\\a\\notes.txt'), {});
  });

  it('redirects to a page explaining why the upload was blocked', () => {
    configure({ BlockedExtensions: ['.exe'] });

    const response = upload('C:\\a\\setup.exe');
    assert.ok(response.redirectUrl.startsWith('data:text/html;'));

    const html = decodeURIComponent(response.redirectUrl.split(',').slice(1).join(','));
    assert.ok(html.includes('禁止された拡張子です'));
    assert.ok(html.includes('setup.exe'));
    // Only a main frame can be sent back to where it came from.
    assert.ok(!html.includes('history.back()'));
  });

  it('sends the main frame back after blocking', () => {
    configure({ BlockedExtensions: ['.exe'] });

    const response = upload('C:\\a\\setup.exe', 'main_frame');
    const html = decodeURIComponent(response.redirectUrl.split(',').slice(1).join(','));
    assert.ok(html.includes('history.back()'));
  });

  it('skips parts that are not files', () => {
    configure({ BlockedExtensions: ['.exe'] });

    const response = UploadGuard.onBeforeRequest({
      type: 'sub_frame',
      requestBody: { raw: [{ bytes: new ArrayBuffer(4) }, { file: 'C:\\a\\notes.txt' }] },
    });

    assert.deepEqual(response, {});
  });

  it('blocks as soon as one of several files is not allowed', () => {
    configure({ BlockedExtensions: ['.exe'] });

    const response = UploadGuard.onBeforeRequest({
      type: 'sub_frame',
      requestBody: { raw: [{ file: 'C:\\a\\notes.txt' }, { file: 'C:\\a\\setup.exe' }] },
    });

    assert.ok(response.redirectUrl);
  });
});

describe('the audit trail', () => {
  it('reports a blocked upload to the host', async () => {
    configure({ BlockedExtensions: ['.exe'] });

    upload('C:\\tmp\\setup.exe');

    const entry = await waitForReport(e => e.name === 'C:\\tmp\\setup.exe');
    assert.ok(entry, 'the block should have been reported');
    assert.equal(entry.operation, 'uploadblocked');
    assert.equal(entry.name, 'C:\\tmp\\setup.exe');
    assert.equal(entry.url, 'https://example.com/upload');
    assert.equal(entry.reason, '禁止された拡張子です');
    assert.equal(entry.timestamp, '2026-08-07 12:34:56');
  });

  // The report goes out on its own; the upload is refused straight away.
  it('answers the listener without waiting for the report', () => {
    configure({ BlockedExtensions: ['.exe'] });

    const response = upload('C:\\tmp\\setup.exe');

    assert.ok(response.redirectUrl);
  });
});
