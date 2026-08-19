'use strict';

import { describe, it, beforeEach } from 'node:test';
import assert from 'node:assert/strict';

// tab-count-limit reaches for chrome.* when it counts the tabs, closes them or
// puts the warning up, so a stub stands in for the browser and records what it
// was asked to do.
const calls = { created: [], updated: [], removed: [], focused: [] };
let session = {};
let tabs = [];
let nextTabId = 100;

// The window the stub puts the tabs it opens in, so focus can be checked.
const WINDOW_ID = 5;
const WARNING_PAGE = 'chrome-extension://testid/tab-limit.html';

function findTab(tabId) {
  return tabs.find(tab => tab.id === tabId);
}

globalThis.chrome = {
  runtime: {
    getURL: path => `chrome-extension://testid/${path}`,
  },
  tabs: {
    query: () => Promise.resolve(tabs.map(tab => ({ ...tab }))),
    create: props => {
      calls.created.push(props);
      const tab = { id: nextTabId++, windowId: WINDOW_ID, url: props.url };
      tabs.push(tab);
      return Promise.resolve({ ...tab });
    },
    update: (tabId, props) => {
      const tab = findTab(tabId);
      // A tab that is no longer open rejects, the way chrome.tabs does.
      if (!tab) return Promise.reject(new Error('No tab with id'));
      calls.updated.push({ tabId, ...props });
      tab.url = props.url;
      return Promise.resolve({ ...tab });
    },
    remove: tabId => {
      if (!findTab(tabId)) return Promise.reject(new Error('No tab with id'));
      calls.removed.push(tabId);
      tabs = tabs.filter(tab => tab.id !== tabId);
      return Promise.resolve();
    },
  },
  windows: {
    update: (id, props) => {
      calls.focused.push({ id, ...props });
      return Promise.resolve({ id });
    },
  },
  storage: {
    session: {
      get: key => Promise.resolve(key in session ? { [key]: session[key] } : {}),
      set: entries => {
        Object.assign(session, entries);
        return Promise.resolve();
      },
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

function state(overrides = {}) {
  return { warnTabId: 0, ...overrides };
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
  calls.created = [];
  calls.updated = [];
  calls.removed = [];
  calls.focused = [];
  session = {};
  tabs = [];
  TabCountLimit.opening = false;
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

describe('countableTabs', () => {
  it('counts the tabs of every window together', async () => {
    openTabs(5, [1, 2, 3]);

    assert.equal((await TabCountLimit.countableTabs(0)).length, 5);
  });

  // Otherwise the warning would push the count up by one and be closed as the
  // newest tab the moment it appeared.
  it('leaves out the warning it opened itself', async () => {
    const opened = openTabs(5);

    const countable = await TabCountLimit.countableTabs(opened[0].id);

    assert.equal(countable.length, 4);
    assert.ok(!countable.some(tab => tab.id === opened[0].id));
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
    // The warning is the only tab left beyond the limit.
    assert.equal(tabs.filter(tab => !tab.url.startsWith(WARNING_PAGE)).length, 3);
  });

  it('leaves the browser alone while inside the limit', async () => {
    configure({ MaxCount: 3 });
    openTabs(3);

    await TabCountLimit.check();

    assert.deepEqual(calls.removed, []);
    assert.deepEqual(calls.created, []);
  });

  it('says why the tabs were closed', async () => {
    configure({ MaxCount: 3 });
    openTabs(5);

    await TabCountLimit.check();

    assert.equal(calls.created.length, 1);
    const url = new URL(calls.created[0].url);
    assert.ok(url.href.startsWith(`${WARNING_PAGE}?`));
    assert.equal(url.searchParams.get('max'), '3');
    assert.equal(url.searchParams.get('closed'), '2');
  });

  // The warning must not take over whatever the user happens to be reading.
  it('opens the warning in a tab of its own and brings it to the front', async () => {
    configure({ MaxCount: 3 });
    openTabs(4);

    await TabCountLimit.check();

    assert.equal(calls.created[0].active, true);
    assert.deepEqual(calls.updated, []);
    // Being active in its window is not enough if that window is behind.
    assert.deepEqual(calls.focused, [{ id: WINDOW_ID, focused: true }]);
  });

  it('remembers the warning tab it opened', async () => {
    configure({ MaxCount: 3 });
    openTabs(4);

    await TabCountLimit.check();

    const opened = (await TabCountLimit.loadState()).warnTabId;
    assert.ok(opened, 'the tab it opened should be remembered');
    assert.ok(findTab(opened));
  });

  it('reuses the warning tab instead of opening another', async () => {
    configure({ MaxCount: 3 });
    openTabs(4);
    tabs.push({ id: 42, windowId: WINDOW_ID, url: `${WARNING_PAGE}?max=3&closed=1` });
    await TabCountLimit.saveState(state({ warnTabId: 42 }));

    await TabCountLimit.check();

    assert.deepEqual(calls.created, []);
    assert.equal(calls.updated.length, 1);
    assert.equal(calls.updated[0].tabId, 42);
  });

  // The warning tab is left out of the count, so it must survive the closing.
  it('never closes the warning tab itself', async () => {
    configure({ MaxCount: 3 });
    openTabs(4);
    // A higher id than every other tab, so it would go first if it counted.
    tabs.push({ id: 9999, windowId: WINDOW_ID, url: `${WARNING_PAGE}?max=3&closed=1` });
    await TabCountLimit.saveState(state({ warnTabId: 9999 }));

    await TabCountLimit.check();

    assert.ok(!calls.removed.includes(9999));
    assert.ok(findTab(9999));
  });

  it('opens a new warning tab when the old one was closed', async () => {
    configure({ MaxCount: 3 });
    openTabs(4);
    await TabCountLimit.saveState(state({ warnTabId: 42 }));

    await TabCountLimit.check();

    assert.equal(calls.created.length, 1);
  });

  // Opening the warning creates a tab, which reports that a tab was created;
  // acting on that report would close the warning as the newest tab.
  it('does not act while it is opening the warning', async () => {
    configure({ MaxCount: 3 });
    openTabs(4);
    TabCountLimit.opening = true;

    await TabCountLimit.check();

    assert.deepEqual(calls.removed, []);
    assert.deepEqual(calls.created, []);
  });

  it('does nothing while disabled', async () => {
    configure({ Enabled: false, MaxCount: 3 });
    openTabs(10);

    await TabCountLimit.check();

    assert.deepEqual(calls.removed, []);
    assert.deepEqual(calls.created, []);
  });

  // The limit is at least one tab, so something is always left open.
  it('never closes the last tab', async () => {
    configure({ MaxCount: 1 });
    openTabs(3);

    await TabCountLimit.check();

    assert.equal(tabs.filter(tab => !tab.url.startsWith(WARNING_PAGE)).length, 1);
  });
});
