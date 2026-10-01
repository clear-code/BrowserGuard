/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 ClearCode Inc.
*/

'use strict';

export function message(key, substitutions) {
  const text = chrome.i18n.getMessage(key, substitutions);
  // A key that is not in the catalogue comes back empty, which would put up a
  // blank dialog. Showing the key at least says which message went missing.
  return text || key;
}
