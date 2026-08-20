'use strict';

import { describe, it, beforeEach } from 'node:test';
import assert from 'node:assert/strict';

// tab-count-limit reaches for chrome.* when it counts the tabs, closes them or
// warns, so a stub stands in for the browser and records what it was asked to
// do.
const calls = { removed: [], created: [], warned: [] };
let tabs = [];
let nextTabId = 100;
// How the host answers, so a dialog left up can be held open in a test.
let nativeMessage = () => Promise.resolve({ Success: true });

function findTab(tabId) {
  return tabs.find(tab => tab.id === tabId);
}

globalThis.chrome = {
  runtime: {
    getURL: path => `chrome-extension://testid/${path}`,
    // The host answers only once the user has dismissed the dialog.
    sendNativeMessage: (server, payload) => {
      calls.warned.push({ server, ...payload });
      return nativeMessage();
    },
  },
  tabs: {
    query: () => Promise.resolve(tabs.map(tab => ({ ...tab }))),
    // Nothing may open a tab: a tab to carry the warning would itself put the
    // count over the limit.
    create: props => {
      calls.created.push(props);
      return Promise.resolve({ id: nextTabId++ });
    },
    remove: tabId => {
      // A tab that is no longer open rejects, the way chrome.tabs does.
      if (!findTab(tabId)) return Promise.reject(new Error('No tab with id'));
      calls.removed.push(tabId);
      tabs = tabs.filter(tab => tab.id !== tabId);
      return Promise.resolve();
    },
  },
};

const { TabCountLimit } = await import('../edge/tab-count-limit.js');

const DEFAULTS = {
  Enabled: true,
  MaxCount: 0,
};

function configure(overrides = {}) {
  TabCountLimit.applyConfig({ ...DEFAULTS, ...overrides });
}

// Tabs of the user's own, spread over more than one window so that the count is
// never taken from a single window. They are returned oldest first, the way
// chrome.tabs.query reports them.
function openTabs(count, windowIds = [1, 2]) {
  const opened = [];
  for (let index = 0; index < count; index++) {
    const tab = {
      id: nextTabId++,
      windowId: windowIds[index % windowIds.length],
      url: `https://example.com/${index}`,
    };
    tabs.push(tab);
    opened.push(tab);
  }
  return opened;
}

beforeEach(() => {
  calls.removed = [];
  calls.created = [];
  calls.warned = [];
  tabs = [];
  nativeMessage = () => Promise.resolve({ Success: true });
  configure();
});

describe('excessTabs', () => {
  it('takes nothing away while disabled', () => {
    configure({ Enabled: false, MaxCount: 3 });

    assert.deepEqual(TabCountLimit.excessTabs(openTabs(10)), []);
  });

  // Otherwise a config that leaves the number out would close every tab.
  it('treats no limit as no limit', () => {
    configure({ MaxCount: 0 });

    assert.deepEqual(TabCountLimit.excessTabs(openTabs(50)), []);
  });

  it('leaves the limit itself alone', () => {
    configure({ MaxCount: 3 });

    assert.deepEqual(TabCountLimit.excessTabs(openTabs(3)), []);
  });

  it('takes away only what is over the limit', () => {
    configure({ MaxCount: 3 });

    assert.equal(TabCountLimit.excessTabs(openTabs(4)).length, 1);
    tabs = [];
    assert.equal(TabCountLimit.excessTabs(openTabs(9)).length, 6);
  });

  // What the user already had open is theirs; only what they just opened goes.
  it('takes the most recently opened tabs', () => {
    configure({ MaxCount: 3 });
    const opened = openTabs(5);

    const excess = TabCountLimit.excessTabs(opened);

    assert.deepEqual(excess.map(tab => tab.id), [opened[4].id, opened[3].id]);
  });

  // The order chrome.tabs.query reports is by window and position, not by age.
  it('goes by when a tab was opened rather than where it sits', () => {
    configure({ MaxCount: 2 });
    const newest = { id: 900, windowId: 1, url: 'https://example.com/newest' };
    const older = [
      { id: 100, windowId: 1, url: 'https://example.com/a' },
      { id: 200, windowId: 2, url: 'https://example.com/b' },
    ];

    const excess = TabCountLimit.excessTabs([newest, ...older]);

    assert.deepEqual(excess.map(tab => tab.id), [900]);
  });
});

describe('check', () => {
  it('closes the tab that puts the count over the limit', async () => {
    configure({ MaxCount: 3 });
    const opened = openTabs(4);

    await TabCountLimit.check();

    assert.deepEqual(calls.removed, [opened[3].id]);
    assert.ok(!findTab(opened[3].id));
  });

  it('counts every window together before closing anything', async () => {
    configure({ MaxCount: 3 });
    // Two tabs in each of two windows: neither window is over on its own.
    const opened = openTabs(4, [1, 2]);

    await TabCountLimit.check();

    assert.deepEqual(calls.removed, [opened[3].id]);
  });

  it('brings a session restored over the limit back down to it', async () => {
    configure({ MaxCount: 3 });
    openTabs(8);

    await TabCountLimit.check();

    assert.equal(calls.removed.length, 5);
    assert.equal(tabs.length, 3);
  });

  it('leaves the browser alone while inside the limit', async () => {
    configure({ MaxCount: 3 });
    openTabs(3);

    await TabCountLimit.check();

    assert.deepEqual(calls.removed, []);
    assert.deepEqual(calls.warned, []);
  });

  // A tab to carry the warning would itself put the count over the limit.
  it('never opens a tab of its own', async () => {
    configure({ MaxCount: 3 });
    openTabs(6);

    await TabCountLimit.check();

    assert.deepEqual(calls.created, []);
    assert.equal(tabs.length, 3);
  });

  it('asks the native host to show the warning', async () => {
    configure({ MaxCount: 3 });
    openTabs(5);

    await TabCountLimit.check();

    assert.equal(calls.warned.length, 1);
    assert.equal(calls.warned[0].server, 'com.clear_code.browser_guard');
    assert.match(calls.warned[0].message, /^W /);
  });

  // The host answers only once the dialog is dismissed, so a second warning
  // behind the first would leave a dialog to dismiss for every tab opened.
  it('puts up one dialog at a time', async () => {
    configure({ MaxCount: 3 });
    let dismiss = () => {};
    nativeMessage = () => new Promise(resolve => { dismiss = () => resolve({ Success: true }); });
    openTabs(4);

    const first = TabCountLimit.check();
    openTabs(1);
    await TabCountLimit.check();

    assert.equal(calls.warned.length, 1);
    // The tabs are still closed while the dialog stands.
    assert.equal(tabs.length, 3);

    dismiss();
    await first;
  });

  it('warns again once the dialog has been dismissed', async () => {
    configure({ MaxCount: 3 });
    openTabs(4);
    await TabCountLimit.check();
    openTabs(1);
    await TabCountLimit.check();

    assert.equal(calls.warned.length, 2);
  });

  it('does nothing while disabled', async () => {
    configure({ Enabled: false, MaxCount: 3 });
    openTabs(10);

    await TabCountLimit.check();

    assert.deepEqual(calls.removed, []);
    assert.deepEqual(calls.warned, []);
  });

  // The limit is at least one tab, so something is always left open.
  it('never closes the last tab', async () => {
    configure({ MaxCount: 1 });
    openTabs(3);

    await TabCountLimit.check();

    assert.equal(tabs.length, 1);
  });

  // A host that cannot be reached must not stop the limit being enforced.
  it('still closes the tabs when the warning cannot be shown', async () => {
    configure({ MaxCount: 3 });
    openTabs(4);
    nativeMessage = () => Promise.reject(new Error('host not found'));

    await TabCountLimit.check();

    assert.equal(tabs.length, 3);
  });

  // Otherwise one unreachable host would leave the warning switched off.
  it('warns again after a failure', async () => {
    configure({ MaxCount: 3 });
    openTabs(4);
    nativeMessage = () => Promise.reject(new Error('host not found'));
    await TabCountLimit.check();

    nativeMessage = () => Promise.resolve({ Success: true });
    openTabs(1);
    await TabCountLimit.check();

    assert.equal(calls.warned.length, 2);
  });
});
