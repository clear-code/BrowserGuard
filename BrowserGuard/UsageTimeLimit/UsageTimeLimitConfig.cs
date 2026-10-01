/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 ClearCode Inc.
*/

namespace BrowserGuard.UsageTimeLimit
{
    internal class UsageTimeLimitConfig
    {
        public bool Enabled { get; set; }
        public int MaxContinuousMinutes { get; set; }
        public TimeRangeConfig[] AllowedTimeRanges { get; set; } = [];
        public UsageTimeExceededConfig OnExceeded { get; set; } = new();
    }

    // "HH:mm" in local time. A range whose End is not after its Start runs
    // past midnight, so { "22:00", "02:00" } is a five hour window.
    internal class TimeRangeConfig
    {
        public string Start { get; set; } = "";
        public string End { get; set; } = "";
    }

    internal class UsageTimeExceededConfig
    {
        // "WarnOnly" or "Terminate" (ignore case). Anything else is read as "WarnOnly", so a
        // misspelling cannot silently start closing the browser.
        public string Action { get; set; } = "WarnOnly";
        public int GraceSeconds { get; set; } = 60;
        public int ReWarnIntervalMinutes { get; set; } = 10;
    }
}
