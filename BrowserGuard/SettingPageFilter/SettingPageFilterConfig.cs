/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 ClearCode Inc.
*/

namespace BrowserGuard.SettingPageFilter
{
    internal class SettingPageFilterConfig
    {
        public bool Enabled { get; set; }
        public bool NotifyOnBlocked { get; set; }
        public string[] BlockedPrefixes { get; set; } = [
                "edge://settings",
                "edge://flags",
                "edge://policy"
            ];
    }
}
