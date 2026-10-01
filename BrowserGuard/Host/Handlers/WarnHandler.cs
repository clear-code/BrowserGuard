/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 ClearCode Inc.
*/

using BrowserGuard.Configuration;

namespace BrowserGuard.Host.Handlers
{
    // The text to show arrives with the message, so nothing in the config
    // bears on it.
    internal sealed class WarnHandler : IMessageHandler
    {
        private readonly Action<string> show;

        internal WarnHandler(Action<string> show) => this.show = show;

        public string Command => "W";

        public string Description => "warn";

        public Response? Run(string argument, Lazy<Config> config)
        {
            show(argument);
            return new Response { Success = true };
        }
    }
}
