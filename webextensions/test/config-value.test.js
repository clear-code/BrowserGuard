'use strict';

import { describe, it } from 'node:test';
import assert from 'node:assert/strict';

import { readBoolean, readNumber, readArray } from '../edge/config-value.js';

describe('readBoolean', () => {
  it('takes the value the config gives', () => {
    assert.equal(readBoolean({ Enabled: true }, 'Enabled', false), true);
  });

  // The setting being off is a value, not a missing one: falling back here
  // would leave a feature on that the config turned off.
  it('takes false as a value rather than as nothing', () => {
    assert.equal(readBoolean({ Enabled: false }, 'Enabled', true), false);
  });

  it('keeps what it had when the key is not there', () => {
    assert.equal(readBoolean({}, 'Enabled', true), true);
    assert.equal(readBoolean(undefined, 'Enabled', true), true);
    assert.equal(readBoolean(null, 'Enabled', true), true);
  });

  // A config that says "true" as a string, or 1, is not saying a boolean.
  it('keeps what it had for a value of the wrong shape', () => {
    assert.equal(readBoolean({ Enabled: 'true' }, 'Enabled', false), false);
    assert.equal(readBoolean({ Enabled: 1 }, 'Enabled', false), false);
    assert.equal(readBoolean({ Enabled: null }, 'Enabled', false), false);
  });
});

describe('readNumber', () => {
  it('takes the value the config gives', () => {
    assert.equal(readNumber({ MaxCount: 5 }, 'MaxCount', 0), 5);
  });

  // 0 means "no limit" for several settings, so it has to survive.
  it('takes zero as a value rather than as nothing', () => {
    assert.equal(readNumber({ MaxCount: 0 }, 'MaxCount', 20), 0);
  });

  it('keeps what it had when the key is not there', () => {
    assert.equal(readNumber({}, 'MaxCount', 20), 20);
    assert.equal(readNumber(undefined, 'MaxCount', 20), 20);
  });

  it('keeps what it had for a value of the wrong shape', () => {
    assert.equal(readNumber({ MaxCount: '5' }, 'MaxCount', 20), 20);
    assert.equal(readNumber({ MaxCount: NaN }, 'MaxCount', 20), 20);
    assert.equal(readNumber({ MaxCount: Infinity }, 'MaxCount', 20), 20);
    assert.equal(readNumber({ MaxCount: null }, 'MaxCount', 20), 20);
  });
});

describe('readArray', () => {
  it('takes the value the config gives', () => {
    assert.deepEqual(readArray({ Blocked: ['.exe'] }, 'Blocked', []), ['.exe']);
  });

  // An empty list means "no restriction from this rule", so it has to survive.
  it('takes an empty list as a value rather than as nothing', () => {
    assert.deepEqual(readArray({ Blocked: [] }, 'Blocked', ['.exe']), []);
  });

  it('keeps what it had when the key is not there', () => {
    assert.deepEqual(readArray({}, 'Blocked', ['.exe']), ['.exe']);
    assert.deepEqual(readArray(undefined, 'Blocked', ['.exe']), ['.exe']);
  });

  it('keeps what it had for a value of the wrong shape', () => {
    assert.deepEqual(readArray({ Blocked: '.exe' }, 'Blocked', ['.bat']), ['.bat']);
    assert.deepEqual(readArray({ Blocked: null }, 'Blocked', ['.bat']), ['.bat']);
  });
});
