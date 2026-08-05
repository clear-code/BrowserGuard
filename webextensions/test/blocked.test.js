'use strict';

import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

// blocked.js is a page script: it runs against the document as soon as it
// loads. Importing it would only execute once, so the source is evaluated
// afresh for each case with the few browser globals it touches passed in.
const source = readFileSync(new URL('../edge/blocked.js', import.meta.url), 'utf8');
const run = new Function('location', 'document', 'history', 'chrome', source);

function load({ search = '', historyLength = 3 } = {}) {
  const calls = [];
  const url = { textContent: '' };
  let onClick = null;

  const document = {
    getElementById: id => (id === 'url' ? url : {
      addEventListener(type, handler) {
        if (type === 'click') onClick = handler;
      },
    }),
  };
  const history = {
    length: historyLength,
    back: () => calls.push('history.back'),
  };
  const chrome = {
    tabs: {
      getCurrent: async () => ({ id: 42 }),
      remove: id => calls.push(`tabs.remove:${id}`),
    },
  };

  run({ search }, document, history, chrome);

  return { calls, url, click: () => onClick() };
}

describe('the address it reports', () => {
  it('shows the address it was given', () => {
    const page = load({ search: '?url=' + encodeURIComponent('edge://settings/privacy') });

    assert.equal(page.url.textContent, 'edge://settings/privacy');
  });

  it('keeps the query and fragment of the address intact', () => {
    const original = 'edge://settings/?a=1&b=2#x';
    const page = load({ search: '?url=' + encodeURIComponent(original) });

    assert.equal(page.url.textContent, original);
  });

  it('shows nothing when no address was passed', () => {
    const page = load({ search: '' });

    assert.equal(page.url.textContent, '');
  });
});

describe('the back button', () => {
  it('goes back when there is somewhere to go', async () => {
    const page = load({ historyLength: 3 });

    await page.click();

    assert.deepEqual(page.calls, ['history.back']);
  });

  // The blocked address may have opened a new tab, leaving no history.
  it('closes the tab when there is nothing to go back to', async () => {
    const page = load({ historyLength: 1 });

    await page.click();

    assert.deepEqual(page.calls, ['tabs.remove:42']);
  });
});
