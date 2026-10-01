/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 ClearCode Inc.
*/

using BrowserGuard.Configuration;

namespace BrowserGuard.Host.Handlers
{
    // The browser reads the config for itself: most of what it holds is acted
    // on there rather than here.
    internal sealed class ConfigHandler : IMessageHandler
    {
        public string Command => "C";

        public string Description => "load config";

        public Response? Run(string argument, Lazy<Config> config)
        {
            return new ConfigResponse { Success = true, Config = config.Value };
        }
    }
}
