/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 ClearCode Inc.
*/

using BrowserGuard.Configuration;

namespace BrowserGuard.Host.Handlers
{
    // One command of the browser's protocol: the letter a message opens with,
    // and what to do with the rest of it.
    internal interface IMessageHandler
    {
        // The letter, without the space that follows it in the message.
        string Command { get; }

        // What the dispatcher writes to the diagnostic log when the command
        // arrives. Empty for a command that arrives with every request: a line
        // apiece would make the log the busiest thing on the machine.
        string Description { get; }

        // Handed what follows the space, and a config that is only read if it
        // is asked for: a log entry arrives for every request, and reading the
        // config for each one would mean a registry and a file read per
        // request.
        //
        // null when nothing is sent back to the browser.
        Response? Run(string argument, Lazy<Config> config);
    }
}
