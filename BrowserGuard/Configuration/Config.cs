/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 ClearCode Inc.
*/

using BrowserGuard.NetLogger;
using BrowserGuard.SettingPageFilter;
using BrowserGuard.Startup;
using BrowserGuard.TabCountLimit;
using BrowserGuard.UploadFileBridge;
using BrowserGuard.UploadGuard;
using BrowserGuard.UsageTimeLimit;

namespace BrowserGuard.Configuration
{
    internal class Config
    {
        public NetLoggerConfig NetLogger { get; set; } = new();
        public UploadGuardConfig UploadGuard { get; set; } = new();
        public SettingPageFilterConfig SettingPageFilter { get; set; } = new();
        public StartupLauncherConfig StartupLauncher { get; set; } = new();
        public UsageTimeLimitConfig UsageTimeLimit { get; set; } = new();
        public TabCountLimitConfig TabCountLimit { get; set; } = new();
        public UploadFileBridgeConfig UploadFileBridge { get; set; } = new();
    }
}
