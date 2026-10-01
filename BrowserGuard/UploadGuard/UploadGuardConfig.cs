/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 ClearCode Inc.
*/

namespace BrowserGuard.UploadGuard
{
    // Controls which local files may be uploaded.
    // The blocked lists are checked first, so they win over the allowed ones.
    // An empty allowed list means "no restriction from this rule".
    internal class UploadGuardConfig
    {
        public bool Enabled { get; set; }
        public string[] BlockedExtensions { get; set; } = [];
        public string[] AllowedExtensions { get; set; } = [];
        public string[] AllowedPaths { get; set; } = [];
        public string[] BlockedPaths { get; set; } = [];
    }
}
