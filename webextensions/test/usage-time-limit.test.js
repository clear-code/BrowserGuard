'use strict';

import { describe, it, beforeEach } from 'node:test';
import assert from 'node:assert/strict';

// usage-time-limit reaches for chrome.* when it warns or closes the browser,
// so a stub stands in for the browser and records what it was asked to do.
const calls = { created: [], updated: [], removed: [], focused: [] };
let session = {};
let openWindows = [];
let nextTabId = 100;

// The window the stub puts every tab in, so focus can be checked.
const WINDOW_ID = 5;

globalThis.chrome = {
  runtime: {
    getURL: path => `chrome-extension://testid/${path}`,
  },
  tabs: {
    create: props => {
      calls.created.push(props);
      const id = nextTabId++;
      openTabs.push(id);
      return Promise.resolve({ id, windowId: WINDOW_ID });
    },
    update: (tabId, props) => {
      // A tab that is no longer open rejects, the way chrome.tabs does.
      if (!openTabs.includes(tabId)) return Promise.reject(new Error('No tab with id'));
      calls.updated.push({ tabId, ...props });
      return Promise.resolve({ id: tabId, windowId: WINDOW_ID });
    },
  },
  windows: {
    getAll: () => Promise.resolve(openWindows),
    remove: id => {
      calls.removed.push(id);
      return Promise.resolve();
    },
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

let openTabs = [];

const { UsageTimeLimit, parseTimeOfDay } =
  await import('../edge/usage-time-limit.js');

const DEFAULTS = {
  Enabled: true,
  MaxContinuousMinutes: 0,
  AllowedTimeRanges: [],
  OnExceeded: {
    Action: 'WarnOnly',
    GraceSeconds: 60,
    ReWarnIntervalMinutes: 10,
  },
};

function configure(overrides = {}) {
  UsageTimeLimit.applyConfig({
    ...DEFAULTS,
    ...overrides,
    OnExceeded: { ...DEFAULTS.OnExceeded, ...(overrides.OnExceeded ?? {}) },
  });
}

// A local time, so the time of day the module reads is the one intended here.
function at(hour, minute = 0, day = 15) {
  return new Date(2026, 0, day, hour, minute, 0, 0).getTime();
}

const MINUTE = 60000;

function state(overrides = {}) {
  return { sessionStart: 0, warnedAt: 0, terminateAt: 0, warnTabId: 0, ...overrides };
}

beforeEach(() => {
  calls.created = [];
  calls.updated = [];
  calls.removed = [];
  calls.focused = [];
  session = {};
  openWindows = [];
  openTabs = [];
  configure();
});

describe('parseTimeOfDay', () => {
  it('reads a time of day as minutes since midnight', () => {
    assert.equal(parseTimeOfDay('00:00'), 0);
    assert.equal(parseTimeOfDay('9:05'), 545);
    assert.equal(parseTimeOfDay('23:59'), 1439);
  });

  it('refuses what it cannot read', () => {
    for (const bad of ['', '9', '09:60', '24:00', 'noon', null, undefined]) {
      assert.equal(parseTimeOfDay(bad), null, `${bad} should not parse`);
    }
  });
});

describe('isWithinAllowedRanges', () => {
  it('allows any hour when no range is configured', () => {
    assert.equal(UsageTimeLimit.isWithinAllowedRanges(at(3)), true);
  });

  it('allows the configured range only', () => {
    configure({ AllowedTimeRanges: [{ Start: '09:00', End: '18:00' }] });

    assert.equal(UsageTimeLimit.isWithinAllowedRanges(at(8, 59)), false);
    assert.equal(UsageTimeLimit.isWithinAllowedRanges(at(9, 0)), true);
    assert.equal(UsageTimeLimit.isWithinAllowedRanges(at(17, 59)), true);
    // The end is the moment the range closes, so it is not itself allowed.
    assert.equal(UsageTimeLimit.isWithinAllowedRanges(at(18, 0)), false);
  });

  it('allows any of several ranges', () => {
    configure({
      AllowedTimeRanges: [
        { Start: '09:00', End: '12:00' },
        { Start: '13:00', End: '18:00' },
      ],
    });

    assert.equal(UsageTimeLimit.isWithinAllowedRanges(at(10)), true);
    assert.equal(UsageTimeLimit.isWithinAllowedRanges(at(12, 30)), false);
    assert.equal(UsageTimeLimit.isWithinAllowedRanges(at(14)), true);
  });

  it('carries a range past midnight', () => {
    configure({ AllowedTimeRanges: [{ Start: '22:00', End: '02:00' }] });

    assert.equal(UsageTimeLimit.isWithinAllowedRanges(at(23)), true);
    assert.equal(UsageTimeLimit.isWithinAllowedRanges(at(1)), true);
    assert.equal(UsageTimeLimit.isWithinAllowedRanges(at(3)), false);
  });

  // A mistyped range must not lock the browser out of use for good.
  it('drops a range it cannot read instead of never matching', () => {
    configure({ AllowedTimeRanges: [{ Start: '9am', End: '6pm' }] });

    assert.deepEqual(UsageTimeLimit.allowedTimeRanges, []);
    assert.equal(UsageTimeLimit.isWithinAllowedRanges(at(3)), true);
  });
});

describe('violationReason', () => {
  it('reports nothing while inside both limits', () => {
    configure({
      MaxContinuousMinutes: 60,
      AllowedTimeRanges: [{ Start: '09:00', End: '18:00' }],
    });

    assert.equal(UsageTimeLimit.violationReason(at(10), at(9, 30)), '');
  });

  it('reports the continuous limit once it is reached', () => {
    configure({ MaxContinuousMinutes: 60 });

    assert.equal(UsageTimeLimit.violationReason(at(10, 59), at(10)), '');
    assert.equal(UsageTimeLimit.violationReason(at(11), at(10)), 'continuous');
  });

  it('ignores the continuous limit when it is zero', () => {
    configure({ MaxContinuousMinutes: 0 });

    assert.equal(UsageTimeLimit.violationReason(at(23), at(0)), '');
  });

  it('reports the schedule outside the allowed hours', () => {
    configure({ AllowedTimeRanges: [{ Start: '09:00', End: '18:00' }] });

    assert.equal(UsageTimeLimit.violationReason(at(20), at(19)), 'schedule');
  });

  // Either limit on its own is enough, so both are configured together.
  it('reports whichever limit is exceeded first', () => {
    configure({
      MaxContinuousMinutes: 60,
      AllowedTimeRanges: [{ Start: '09:00', End: '18:00' }],
    });

    assert.equal(UsageTimeLimit.violationReason(at(11), at(10)), 'continuous');
    assert.equal(UsageTimeLimit.violationReason(at(19), at(18, 45)), 'schedule');
  });
});

describe('decide', () => {
  it('does nothing while disabled', () => {
    configure({ Enabled: false, MaxContinuousMinutes: 1 });

    const decision = UsageTimeLimit.decide(at(11), state({ sessionStart: at(10) }));

    assert.equal(decision.act, 'none');
  });

  it('warns the first time the limit is exceeded', () => {
    configure({ MaxContinuousMinutes: 60 });

    const decision = UsageTimeLimit.decide(at(11), state({ sessionStart: at(10) }));

    assert.equal(decision.act, 'warn');
    assert.equal(decision.reason, 'continuous');
    assert.equal(decision.state.warnedAt, at(11));
  });

  it('stays quiet until the re-warn interval has passed', () => {
    configure({ MaxContinuousMinutes: 60, OnExceeded: { ReWarnIntervalMinutes: 10 } });
    const exceeded = state({ sessionStart: at(10), warnedAt: at(11) });

    assert.equal(UsageTimeLimit.decide(at(11) + 9 * MINUTE, exceeded).act, 'none');
    assert.equal(UsageTimeLimit.decide(at(11) + 10 * MINUTE, exceeded).act, 'warn');
  });

  it('warns only once when no re-warn interval is set', () => {
    configure({ MaxContinuousMinutes: 60, OnExceeded: { ReWarnIntervalMinutes: 0 } });
    const exceeded = state({ sessionStart: at(10), warnedAt: at(11) });

    assert.equal(UsageTimeLimit.decide(at(13), exceeded).act, 'none');
  });

  // Long past the point at which a termination would have been due.
  it('never terminates while warning only', () => {
    configure({ MaxContinuousMinutes: 60, OnExceeded: { Action: 'WarnOnly' } });
    const exceeded = state({ sessionStart: at(10), warnedAt: at(11) });

    const decision = UsageTimeLimit.decide(at(20), exceeded);

    assert.equal(decision.act, 'warn');
    assert.equal(decision.state.terminateAt, 0);
  });

  it('sets the deadline when it first warns about a termination', () => {
    configure({
      MaxContinuousMinutes: 60,
      OnExceeded: { Action: 'Terminate', GraceSeconds: 90 },
    });

    const decision = UsageTimeLimit.decide(at(11), state({ sessionStart: at(10) }));

    assert.equal(decision.act, 'warn');
    assert.equal(decision.state.terminateAt, at(11) + 90000);
  });

  it('waits for the deadline before terminating', () => {
    configure({ MaxContinuousMinutes: 60, OnExceeded: { Action: 'Terminate' } });
    const warned = state({
      sessionStart: at(10),
      warnedAt: at(11),
      terminateAt: at(11) + MINUTE,
    });

    assert.equal(UsageTimeLimit.decide(at(11) + 30000, warned).act, 'none');
    assert.equal(UsageTimeLimit.decide(at(11) + MINUTE, warned).act, 'terminate');
  });

  // Only the schedule can be satisfied again; the deadline must not survive it.
  it('forgets the deadline once the browser is back inside its hours', () => {
    configure({
      AllowedTimeRanges: [{ Start: '09:00', End: '18:00' }],
      OnExceeded: { Action: 'Terminate' },
    });
    const warned = state({ sessionStart: at(8), warnedAt: at(8), terminateAt: at(8) + MINUTE });

    const decision = UsageTimeLimit.decide(at(10), warned);

    assert.equal(decision.act, 'none');
    assert.equal(decision.state.terminateAt, 0);
    assert.equal(decision.state.warnedAt, 0);
  });
});

describe('check', () => {
  it('opens the warning page carrying the reason and the deadline', async () => {
    configure({
      MaxContinuousMinutes: 60,
      OnExceeded: { Action: 'Terminate', GraceSeconds: 60 },
    });
    await UsageTimeLimit.saveState(state({ sessionStart: at(10) }));

    await UsageTimeLimit.check(at(11));

    assert.equal(calls.created.length, 1);
    const url = new URL(calls.created[0].url);
    assert.ok(url.href.startsWith('chrome-extension://testid/time-limit.html?'));
    assert.equal(url.searchParams.get('reason'), 'continuous');
    assert.equal(url.searchParams.get('deadline'), String(at(11) + 60000));
    assert.deepEqual(calls.removed, []);
  });

  it('leaves the browser alone while inside the limits', async () => {
    configure({ MaxContinuousMinutes: 60 });
    await UsageTimeLimit.saveState(state({ sessionStart: at(10) }));

    await UsageTimeLimit.check(at(10, 30));

    assert.deepEqual(calls.created, []);
    assert.deepEqual(calls.removed, []);
  });

  it('closes every window once the deadline passes', async () => {
    configure({ MaxContinuousMinutes: 60, OnExceeded: { Action: 'Terminate' } });
    openWindows = [{ id: 1 }, { id: 2 }];
    await UsageTimeLimit.saveState(state({
      sessionStart: at(10),
      warnedAt: at(11),
      terminateAt: at(11) + MINUTE,
    }));

    await UsageTimeLimit.check(at(11) + MINUTE);

    assert.deepEqual(calls.removed, [1, 2]);
  });

  // The warning must not take over whatever the user happens to be reading.
  it('opens the warning in a tab of its own and brings it to the front', async () => {
    configure({ MaxContinuousMinutes: 60 });
    await UsageTimeLimit.saveState(state({ sessionStart: at(10) }));

    await UsageTimeLimit.check(at(11));

    assert.equal(calls.created.length, 1);
    assert.equal(calls.created[0].active, true);
    // Nothing already open was navigated anywhere.
    assert.deepEqual(calls.updated, []);
    // Being active in its window is not enough if that window is behind.
    assert.deepEqual(calls.focused, [{ id: 5, focused: true }]);
  });

  it('remembers the warning tab it opened', async () => {
    configure({ MaxContinuousMinutes: 60 });
    await UsageTimeLimit.saveState(state({ sessionStart: at(10) }));

    await UsageTimeLimit.check(at(11));

    const opened = (await UsageTimeLimit.loadState()).warnTabId;
    assert.ok(opened, 'the tab it opened should be remembered');
    assert.ok(openTabs.includes(opened));
  });

  it('reuses the warning tab instead of opening another', async () => {
    configure({ MaxContinuousMinutes: 60, OnExceeded: { ReWarnIntervalMinutes: 10 } });
    openTabs = [42];
    await UsageTimeLimit.saveState(state({ sessionStart: at(10), warnTabId: 42 }));

    await UsageTimeLimit.check(at(11));

    assert.deepEqual(calls.created, []);
    assert.equal(calls.updated.length, 1);
    assert.equal(calls.updated[0].tabId, 42);
  });

  it('opens a new warning tab when the old one was closed', async () => {
    configure({ MaxContinuousMinutes: 60 });
    openTabs = [];
    await UsageTimeLimit.saveState(state({ sessionStart: at(10), warnTabId: 42 }));

    await UsageTimeLimit.check(at(11));

    assert.equal(calls.created.length, 1);
  });
});

describe('onDeadlineReached', () => {
  it('closes the browser when the deadline really has passed', async () => {
    configure({ OnExceeded: { Action: 'Terminate' } });
    openWindows = [{ id: 1 }];
    await UsageTimeLimit.saveState(state({ terminateAt: Date.now() - 1000 }));

    await UsageTimeLimit.onDeadlineReached();

    assert.deepEqual(calls.removed, [1]);
  });

  // The page reports the time, so it must not be able to close the browser early.
  it('ignores a report that arrives before the deadline', async () => {
    configure({ OnExceeded: { Action: 'Terminate' } });
    openWindows = [{ id: 1 }];
    await UsageTimeLimit.saveState(state({ terminateAt: Date.now() + 60000 }));

    await UsageTimeLimit.onDeadlineReached();

    assert.deepEqual(calls.removed, []);
  });
});

describe('startSession', () => {
  it('starts the clock when the browser has only just started', async () => {
    await UsageTimeLimit.startSession(at(9));

    assert.equal((await UsageTimeLimit.loadState()).sessionStart, at(9));
  });

  it('keeps the clock running when the worker is restarted', async () => {
    await UsageTimeLimit.startSession(at(9));

    await UsageTimeLimit.startSession(at(10));

    assert.equal((await UsageTimeLimit.loadState()).sessionStart, at(9));
  });

  it('restarts the clock when the browser restarts', async () => {
    await UsageTimeLimit.saveState(state({ sessionStart: at(9), warnedAt: at(10) }));

    await UsageTimeLimit.onStartup(at(11));

    const stored = await UsageTimeLimit.loadState();
    assert.equal(stored.sessionStart, at(11));
    assert.equal(stored.warnedAt, 0);
  });
});
