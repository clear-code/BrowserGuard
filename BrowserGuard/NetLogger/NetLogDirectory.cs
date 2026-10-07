/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 ClearCode Inc.
*/

using BrowserGuard.Common;

namespace BrowserGuard.NetLogger
{
    internal static class NetLogDirectory
    {
        internal static string Resolve(string configured, DateTime? now = null) =>
            string.IsNullOrWhiteSpace(configured)
                ? Default()
                : PathMacro.Expand(configured, now ?? DateTime.Now);

        // Per user rather than per machine, so that the several people sharing
        // one host under AVD do not write to a single file, and cannot read
        // each other's. The collector, not this copy, is the record of account.
        // Local rather than roaming: a month of entries is far too much to
        // carry over the network.
        internal static string Default() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Chronos",
                "BrowserGuard",
                "NetLog");
    }
}
