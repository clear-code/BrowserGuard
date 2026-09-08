'use strict';

import { readFileSync } from 'node:fs';

// chrome.i18n, answered from the catalogue that actually ships. Resolving the
// keys for real is the point: a key that is missing or misspelt comes back
// empty here, exactly as it would in the browser, and the test that asks for it
// fails rather than passing against a stub that echoes whatever it is given.
const catalogue = JSON.parse(readFileSync(
    new URL('../edge/_locales/ja/messages.json', import.meta.url), 'utf8'));

export function getMessage(key, substitutions) {
    const entry = catalogue[key];
    if (!entry) return '';

    const args = substitutions === undefined ? [] : [].concat(substitutions);
    let text = entry.message;
    for (const [name, definition] of Object.entries(entry.placeholders ?? {})) {
        const index = Number(String(definition.content).replace('$', '')) - 1;
        text = text.replace(
            new RegExp('\\$' + name + '\\$', 'gi'),
            args[index] ?? '');
    }
    return text;
}

export const i18n = { getMessage };
