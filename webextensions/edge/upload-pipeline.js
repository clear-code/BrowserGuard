/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 ClearCode Inc.
*/

'use strict';

import { UploadGuard } from './upload-guard.js';
import { UploadFileBridge } from './upload-file-bridge.js';

// upload-guard and upload-file-bridge in one listener rather than one each.
// Registered separately they would both be called even when the upload is
// refused, and a file that never left the machine has no place in the evidence
// of what did.
//
// The guard's own answer decides it, so nothing here has to know how the guard
// makes up its mind: that one file is enough to refuse the whole request, or
// that the guard may be turned off altogether.
export const UploadPipeline = {
  onBeforeRequest(details) {
    const response = UploadGuard.onBeforeRequest(details);
    if (response.cancel || response.redirectUrl) return response;

    UploadFileBridge.onBeforeRequest(details);
    return response;
  },
}
