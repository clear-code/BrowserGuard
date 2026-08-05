'use strict';

import { describe, it, beforeEach } from 'node:test';
import assert from 'node:assert/strict';

// setting-page-filter reaches for chrome.* when it blocks something, so a stub
// stands in for the browser and records what it was asked to do.
const calls = { updates: [], goBacks: [] };

globalThis.chrome = {
  runtime: {
    getURL: path => `chrome-extension://testid/${path}`,
  },
  tabs: {
    update: (tabId, props) => {
      calls.updates.push({ tabId, ...props });
      return Promise.resolve();
    },
    goBack: tabId => {
      calls.goBacks.push(tabId);
      return Promise.resolve();
    },
  },
};

const { SettingPageFilter } = await import('../edge/setting-page-filter.js');

const EMPTY = {
  Enabled: true,
  NotifyOnBlocked: true,
  BlockedPrefixes: ['edge://settings'],
};

function configure(overrides) {
  SettingPageFilter.applyConfig({ ...EMPTY, ...overrides });
}

function navigate(url, frameId = 0) {
  return SettingPageFilter.onBeforeNavigate({ frameId, tabId: 7, url });
}

beforeEach(() => {
  calls.updates = [];
  calls.goBacks = [];
  configure({});
});

describe('isBlockedUrl', () => {
  it('matches a configured prefix', () => {
    assert.equal(SettingPageFilter.isBlockedUrl('edge://settings/privacy'), true);
    assert.equal(SettingPageFilter.isBlockedUrl('https://example.com/'), false);
  });

  it('matches any of several prefixes', () => {
    configure({ BlockedPrefixes: ['edge://settings', 'edge://flags'] });

    assert.equal(SettingPageFilter.isBlockedUrl('edge://flags/#foo'), true);
    assert.equal(SettingPageFilter.isBlockedUrl('edge://policy'), false);
  });
});

describe('applyConfig', () => {
  it('ignores a missing config rather than clearing everything', () => {
    configure({ BlockedPrefixes: ['edge://policy'] });

    SettingPageFilter.applyConfig(undefined);

    assert.deepEqual(SettingPageFilter.blockedPrefixes, ['edge://policy']);
  });
});

describe('onBeforeNavigate', () => {
  it('leaves an allowed address alone', () => {
    navigate('https://example.com/');

    assert.deepEqual(calls.updates, []);
    assert.deepEqual(calls.goBacks, []);
  });

  it('ignores navigation in a sub frame', () => {
    navigate('edge://settings/privacy', 1);

    assert.deepEqual(calls.updates, []);
  });

  it('does nothing while disabled', () => {
    configure({ Enabled: false });

    navigate('edge://settings/privacy');

    assert.deepEqual(calls.updates, []);
    assert.deepEqual(calls.goBacks, []);
  });

  it('sends the tab to the bundled page carrying the blocked address', () => {
    navigate('edge://settings/privacy');

    assert.equal(calls.updates.length, 1);
    const { tabId, url } = calls.updates[0];
    assert.equal(tabId, 7);
    assert.ok(url.startsWith('chrome-extension://testid/blocked.html?'));
    assert.equal(
      new URL(url).searchParams.get('url'),
      'edge://settings/privacy');
    // The explanation replaces going back, so history is left alone.
    assert.deepEqual(calls.goBacks, []);
  });

  it('goes back silently when notifications are turned off', () => {
    configure({ NotifyOnBlocked: false });

    navigate('edge://settings/privacy');

    assert.deepEqual(calls.goBacks, [7]);
    assert.deepEqual(calls.updates, []);
  });
});

describe('blockedPageUrl', () => {
  // A crafted address must not be able to add parameters of its own.
  it('escapes the address it carries', () => {
    const url = SettingPageFilter.blockedPageUrl('edge://settings/?a=1&b=2#x');

    assert.equal(
      new URL(url).searchParams.get('url'),
      'edge://settings/?a=1&b=2#x');
  });
});
