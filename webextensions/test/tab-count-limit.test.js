'use strict';

import { describe, it, beforeEach } from 'node:test';
import assert from 'node:assert/strict';

// tab-count-limit reaches for chrome.* when it counts the tabs or puts the
// warning up, so a stub stands in for the browser and records what it was
// asked to do.
const calls = { created: [], updated: [], removed: [], focused: [] };
let session = {};
let tabs = [];
let nextTabId = 100;

// The window the stub puts every tab it opens in, so focus can be checked.
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
    get: tabId => {
      const tab = findTab(tabId);
      // A tab that is no longer open rejects, the way chrome.tabs does.
      if (!tab) return Promise.reject(new Error('No tab with id'));
      return Promise.resolve({ ...tab });
    },
    create: props => {
      calls.created.push(props);
      const tab = { id: nextTabId++, windowId: WINDOW_ID, url: props.url };
      tabs.push(tab);
      return Promise.resolve({ ...tab });
    },
    update: (tabId, props) => {
      const tab = findTab(tabId);
      if (!tab) return Promise.reject(new Error('No tab with id'));
      calls.updated.push({ tabId, ...props });
      tab.url = props.url;
      return Promise.resolve({ ...tab });
    },
    remove: tabId => {
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
  return { warnTabId: 0, warnedCount: 0, ...overrides };
}

// Tabs of the user's own, spread over more than one window so that the count
// is never taken from a single window.
function openTabs(count, windowIds = [1, 2]) {
  for (let index = 0; index < count; index++) {
    tabs.push({
      id: nextTabId++,
      windowId: windowIds[index % windowIds.length],
      url: `https://example.com/${index}`,
    });
  }
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

describe('decide', () => {
  it('does nothing while disabled', () => {
    configure({ Enabled: false, MaxCount: 3 });

    assert.equal(TabCountLimit.decide(10, state()).act, 'none');
  });

  // Otherwise a config that leaves the number out would warn about every tab.
  it('treats no limit as no limit', () => {
    configure({ MaxCount: 0 });

    assert.equal(TabCountLimit.decide(50, state()).act, 'none');
  });

  it('allows the limit itself and warns past it', () => {
    configure({ MaxCount: 3 });

    assert.equal(TabCountLimit.decide(3, state()).act, 'none');
    assert.equal(TabCountLimit.decide(4, state()).act, 'warn');
  });

  it('remembers the count it warned about', () => {
    configure({ MaxCount: 3 });

    assert.equal(TabCountLimit.decide(4, state()).state.warnedCount, 4);
  });

  // Warning on every check would interrupt the very act of closing tabs.
  it('stays quiet while the count does not grow', () => {
    configure({ MaxCount: 3 });
    const warned = state({ warnedCount: 5, warnTabId: 42 });

    assert.equal(TabCountLimit.decide(5, warned).act, 'none');
    assert.equal(TabCountLimit.decide(4, warned).act, 'none');
  });

  it('warns again once another tab is opened', () => {
    configure({ MaxCount: 3 });
    const warned = state({ warnedCount: 5, warnTabId: 42 });

    const decision = TabCountLimit.decide(6, warned);

    assert.equal(decision.act, 'warn');
    assert.equal(decision.state.warnedCount, 6);
  });

  it('takes the warning away once the count is back inside the limit', () => {
    configure({ MaxCount: 3 });
    const warned = state({ warnedCount: 5, warnTabId: 42 });

    const decision = TabCountLimit.decide(3, warned);

    assert.equal(decision.act, 'dismiss');
    assert.equal(decision.state.warnedCount, 0);
    assert.equal(decision.state.warnTabId, 0);
  });

  // The warning would be the only tab left, so taking it away closes the
  // browser.
  it('keeps the warning rather than close the last tab', () => {
    configure({ MaxCount: 3 });
    const warned = state({ warnedCount: 5, warnTabId: 42 });

    assert.equal(TabCountLimit.decide(0, warned).act, 'none');
  });

  it('has nothing to take away when it never warned', () => {
    configure({ MaxCount: 3 });

    assert.equal(TabCountLimit.decide(2, state()).act, 'none');
  });
});

describe('countTabs', () => {
  it('counts the tabs of every window together', async () => {
    openTabs(5, [1, 2, 3]);

    assert.equal(await TabCountLimit.countTabs(0), 5);
  });

  // Otherwise the warning would push the count it reports up by one.
  it('does not count the warning it opened itself', async () => {
    openTabs(5);

    assert.equal(await TabCountLimit.countTabs(tabs[0].id), 4);
  });
});

describe('check', () => {
  it('warns once the tabs of every window together pass the limit', async () => {
    configure({ MaxCount: 3 });
    openTabs(4, [1, 2]);

    await TabCountLimit.check();

    assert.equal(calls.created.length, 1);
    const url = new URL(calls.created[0].url);
    assert.ok(url.href.startsWith(`${WARNING_PAGE}?`));
    assert.equal(url.searchParams.get('count'), '4');
    assert.equal(url.searchParams.get('max'), '3');
  });

  it('leaves the browser alone while inside the limit', async () => {
    configure({ MaxCount: 3 });
    openTabs(3);

    await TabCountLimit.check();

    assert.deepEqual(calls.created, []);
    assert.deepEqual(calls.updated, []);
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
    tabs.push({ id: 42, windowId: WINDOW_ID, url: `${WARNING_PAGE}?count=4&max=3` });
    await TabCountLimit.saveState(state({ warnTabId: 42, warnedCount: 4 }));

    // One tab more than the warning already reported.
    openTabs(1);
    await TabCountLimit.check();

    assert.deepEqual(calls.created, []);
    assert.equal(calls.updated.length, 1);
    assert.equal(calls.updated[0].tabId, 42);
    assert.equal(new URL(calls.updated[0].url).searchParams.get('count'), '5');
  });

  it('opens a new warning tab when the old one was closed', async () => {
    configure({ MaxCount: 3 });
    openTabs(4);
    await TabCountLimit.saveState(state({ warnTabId: 42 }));

    await TabCountLimit.check();

    assert.equal(calls.created.length, 1);
  });

  it('takes the warning away once enough tabs are closed', async () => {
    configure({ MaxCount: 3 });
    openTabs(2);
    tabs.push({ id: 42, windowId: WINDOW_ID, url: `${WARNING_PAGE}?count=4&max=3` });
    await TabCountLimit.saveState(state({ warnTabId: 42, warnedCount: 4 }));

    await TabCountLimit.check();

    assert.deepEqual(calls.removed, [42]);
    assert.deepEqual(await TabCountLimit.loadState(), state());
  });

  // The tab is the user's own once they have navigated it somewhere else.
  it('leaves a warning tab the user navigated away alone', async () => {
    configure({ MaxCount: 3 });
    openTabs(2);
    tabs.push({ id: 42, windowId: WINDOW_ID, url: 'https://example.com/reading' });
    await TabCountLimit.saveState(state({ warnTabId: 42, warnedCount: 4 }));

    await TabCountLimit.check();

    assert.deepEqual(calls.removed, []);
  });

  // Opening the warning creates a tab, which reports that a tab was created;
  // that report must not open another warning.
  it('does not warn about the warning it is opening', async () => {
    configure({ MaxCount: 3 });
    openTabs(4);
    TabCountLimit.opening = true;

    await TabCountLimit.check();

    assert.deepEqual(calls.created, []);
  });

  it('does nothing while disabled', async () => {
    configure({ Enabled: false, MaxCount: 3 });
    openTabs(10);

    await TabCountLimit.check();

    assert.deepEqual(calls.created, []);
  });
});
