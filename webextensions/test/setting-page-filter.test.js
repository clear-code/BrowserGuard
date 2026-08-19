'use strict';

import { describe, it, beforeEach } from 'node:test';
import assert from 'node:assert/strict';

// setting-page-filter reaches for chrome.* when it blocks something, so a stub
// stands in for the browser and records what it was asked to do. The warning is
// a dialog the native host puts up.
const calls = { updates: [], goBacks: [], warned: [] };

globalThis.chrome = {
  runtime: {
    sendNativeMessage: (server, payload) => {
      calls.warned.push({ server, ...payload });
      return Promise.resolve({ Success: true });
    },
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
  calls.warned = [];
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

  it('says which address was blocked', () => {
    navigate('edge://settings/privacy');

    assert.equal(calls.warned.length, 1);
    assert.match(calls.warned[0].message, /^W /);
    assert.ok(calls.warned[0].message.includes('edge://settings/privacy'));
  });

  // The address is gone whether or not the block is announced, because
  // onBeforeNavigate cannot cancel the navigation itself.
  it('takes the tab off the blocked address either way', () => {
    navigate('edge://settings/privacy');

    assert.deepEqual(calls.goBacks, [7]);
    assert.deepEqual(calls.updates, []);
  });

  it('goes back silently when notifications are turned off', () => {
    configure({ NotifyOnBlocked: false });

    navigate('edge://settings/privacy');

    assert.deepEqual(calls.goBacks, [7]);
    assert.deepEqual(calls.warned, []);
    assert.deepEqual(calls.updates, []);
  });
});

describe('warningText', () => {
  // A crafted address must not be able to pass for the text around it.
  it('carries the address it was given as it stands', () => {
    const text = SettingPageFilter.warningText('edge://settings/?a=1&b=2#x');

    assert.ok(text.includes('edge://settings/?a=1&b=2#x'));
  });
});
