'use strict';

import { describe, it, beforeEach } from 'node:test';
import assert from 'node:assert/strict';

// usage-time-limit reaches for chrome.* when it warns or closes the browser,
// so a stub stands in for the browser and records what it was asked to do.
// The warning is a dialog the native host puts up, so nothing here opens a tab.
const calls = { warned: [], removed: [], alarms: [], cleared: [] };
let session = {};
let openWindows = [];

globalThis.chrome = {
  runtime: {
    sendNativeMessage: (server, payload) => {
      calls.warned.push({ server, ...payload });
      return Promise.resolve({ Success: true });
    },
  },
  alarms: {
    create: (name, info) => {
      calls.alarms.push({ name, ...info });
      return Promise.resolve();
    },
    clear: name => {
      calls.cleared.push(name);
      return Promise.resolve(true);
    },
  },
  windows: {
    getAll: () => Promise.resolve(openWindows),
    remove: id => {
      calls.removed.push(id);
      return Promise.resolve();
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
  return { sessionStart: 0, warnedAt: 0, terminateAt: 0, ...overrides };
}

beforeEach(() => {
  calls.warned = [];
  calls.removed = [];
  calls.alarms = [];
  calls.cleared = [];
  session = {};
  openWindows = [];
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

  // One warning tab that was closed or never noticed must not be the only
  // notice the browser is about to close.
  it('puts the warning back up while the grace period runs', () => {
    configure({
      MaxContinuousMinutes: 60,
      OnExceeded: { Action: 'Terminate', GraceSeconds: 3600, ReWarnIntervalMinutes: 10 },
    });
    const warned = state({
      sessionStart: at(10),
      warnedAt: at(11),
      terminateAt: at(11) + 60 * MINUTE,
    });

    assert.equal(UsageTimeLimit.decide(at(11) + 9 * MINUTE, warned).act, 'none');
    assert.equal(UsageTimeLimit.decide(at(11) + 10 * MINUTE, warned).act, 'warn');
  });

  // The countdown on the page is driven by the deadline it is given.
  it('carries the same deadline into the warning it repeats', () => {
    configure({
      MaxContinuousMinutes: 60,
      OnExceeded: { Action: 'Terminate', GraceSeconds: 3600, ReWarnIntervalMinutes: 10 },
    });
    const deadline = at(11) + 60 * MINUTE;
    const warned = state({ sessionStart: at(10), warnedAt: at(11), terminateAt: deadline });

    const decision = UsageTimeLimit.decide(at(11) + 10 * MINUTE, warned);

    assert.equal(decision.state.terminateAt, deadline);
  });

  it('warns once during the grace period when no interval is set', () => {
    configure({
      MaxContinuousMinutes: 60,
      OnExceeded: { Action: 'Terminate', GraceSeconds: 3600, ReWarnIntervalMinutes: 0 },
    });
    const warned = state({
      sessionStart: at(10),
      warnedAt: at(11),
      terminateAt: at(11) + 60 * MINUTE,
    });

    assert.equal(UsageTimeLimit.decide(at(11) + 30 * MINUTE, warned).act, 'none');
  });

  // Repeating the warning must not push the deadline back.
  it('terminates on time however often it warned', () => {
    configure({
      MaxContinuousMinutes: 60,
      OnExceeded: { Action: 'Terminate', GraceSeconds: 3600, ReWarnIntervalMinutes: 10 },
    });
    const deadline = at(11) + 60 * MINUTE;
    let current = state({ sessionStart: at(10) });

    // Every minute from the first violation to just after the deadline.
    let terminatedAt = 0;
    for (let minute = 0; minute <= 61 && !terminatedAt; minute++) {
      const decision = UsageTimeLimit.decide(at(11) + minute * MINUTE, current);
      current = decision.state;
      if (decision.act === 'terminate') terminatedAt = at(11) + minute * MINUTE;
    }

    assert.equal(terminatedAt, deadline);
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
  it('warns with the reason and the time the browser will close', async () => {
    configure({
      MaxContinuousMinutes: 60,
      OnExceeded: { Action: 'Terminate', GraceSeconds: 60 },
    });
    await UsageTimeLimit.saveState(state({ sessionStart: at(10) }));

    await UsageTimeLimit.check(at(11));

    assert.equal(calls.warned.length, 1);
    assert.match(calls.warned[0].message, /^W /);
    assert.match(calls.warned[0].message, /連続して使用できる時間/);
    assert.match(calls.warned[0].message, /11:01:00 に/);
    assert.deepEqual(calls.removed, []);
  });

  it('leaves the browser alone while inside the limits', async () => {
    configure({ MaxContinuousMinutes: 60 });
    await UsageTimeLimit.saveState(state({ sessionStart: at(10) }));

    await UsageTimeLimit.check(at(10, 30));

    assert.deepEqual(calls.warned, []);
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

  // A tab of its own would take over whatever the user is reading, and the
  // dialog the host puts up cannot be styled away by the page.
  it('never opens a tab or a page of its own', async () => {
    configure({ MaxContinuousMinutes: 60 });
    await UsageTimeLimit.saveState(state({ sessionStart: at(10) }));

    await UsageTimeLimit.check(at(11));

    assert.equal(chrome.tabs, undefined, 'nothing should reach for chrome.tabs');
    assert.equal(calls.warned.length, 1);
  });

  // Nothing counts down any more, so the deadline needs an alarm of its own.
  it('sets an alarm for the deadline it just gave out', async () => {
    configure({
      MaxContinuousMinutes: 60,
      OnExceeded: { Action: 'Terminate', GraceSeconds: 90 },
    });
    await UsageTimeLimit.saveState(state({ sessionStart: at(10) }));

    await UsageTimeLimit.check(at(11));

    const deadline = calls.alarms.find(alarm => alarm.name === 'usage-time-limit-deadline');
    assert.ok(deadline, 'the deadline should have an alarm');
    assert.equal(deadline.when, at(11) + 90000);
  });

  it('takes the deadline alarm away once back inside the limits', async () => {
    configure({
      AllowedTimeRanges: [{ Start: '09:00', End: '18:00' }],
      OnExceeded: { Action: 'Terminate' },
    });
    await UsageTimeLimit.saveState(state({
      sessionStart: at(8),
      warnedAt: at(8),
      terminateAt: at(8) + MINUTE,
    }));

    await UsageTimeLimit.check(at(10));

    assert.ok(calls.cleared.includes('usage-time-limit-deadline'));
  });

  // Warning only never closes anything, so there is no deadline to keep.
  it('sets no deadline alarm while warning only', async () => {
    configure({ MaxContinuousMinutes: 60, OnExceeded: { Action: 'WarnOnly' } });
    await UsageTimeLimit.saveState(state({ sessionStart: at(10) }));

    await UsageTimeLimit.check(at(11));

    assert.ok(!calls.alarms.some(alarm => alarm.name === 'usage-time-limit-deadline'));
    assert.equal(calls.warned.length, 1);
  });
});

describe('warningText', () => {
  it('tells the user to save now when the browser will close', () => {
    configure({ OnExceeded: { Action: 'Terminate' } });

    const text = UsageTimeLimit.warningText('continuous', at(11, 30));

    assert.match(text, /連続して使用できる時間の上限に達しました。/);
    assert.match(text, /11:30:00 にブラウザーを終了します。/);
    // The exact wording is free to change; that it says to save is not.
    assert.match(text, /保存/);
  });

  // Nothing is going to close on its own, so it must not claim otherwise.
  it('names no closing time when there is none', () => {
    const text = UsageTimeLimit.warningText('schedule', 0);

    assert.match(text, /使用が許可された時間帯を過ぎています。/);
    assert.ok(!text.includes('にブラウザーを終了します'));
    assert.match(text, /保存/);
  });

  it('still says something for a reason it does not know', () => {
    const text = UsageTimeLimit.warningText('', 0);

    assert.match(text, /使用時間の制限/);
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

  // An alarm can fire early, so it must not be able to close the browser before
  // the deadline it was set for.
  it('ignores an alarm that arrives before the deadline', async () => {
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
