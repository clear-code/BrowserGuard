'use strict';

export function message(key, substitutions) {
  const text = chrome.i18n.getMessage(key, substitutions);
  // A key that is not in the catalogue comes back empty, which would put up a
  // blank dialog. Showing the key at least says which message went missing.
  return text || key;
}
