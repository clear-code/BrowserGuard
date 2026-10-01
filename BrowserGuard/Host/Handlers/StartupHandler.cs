/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 ClearCode Inc.
*/

using BrowserGuard.Common;
using BrowserGuard.Configuration;
using BrowserGuard.Startup;

namespace BrowserGuard.Host.Handlers
{
    internal sealed class StartupHandler : IMessageHandler
    {
        private readonly Logger? logger;

        internal StartupHandler(Logger? logger = null) => this.logger = logger;

        public string Command => "S";

        public string Description => "startup";

        public Response? Run(string argument, Lazy<Config> config)
        {
            var failures = StartupLauncher.Run(config.Value.StartupLauncher, logger);
            return new Response { Success = failures is null, Error = failures };
        }
    }
}
