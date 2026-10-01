/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 ClearCode Inc.
*/

'use strict';

window.addEventListener('beforeprint', () => {
  chrome.runtime.sendMessage({
    type: 'print',
    url: location.href,
    title: document.title,
    timestamp: Date.now(),
  });
});