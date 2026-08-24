using BrowserGuard.Configuration;

namespace BrowserGuard.Host.Handlers
{
    // One command of the browser's protocol: the letter a message opens with,
    // and what to do with the rest of it.
    internal interface IMessageHandler
    {
        // The letter, without the space that follows it in the message.
        string Command { get; }

        // Handed what follows the space, and a config that is only read if it
        // is asked for: a log entry arrives for every request, and reading the
        // config for each one would mean a registry and a file read per
        // request.
        //
        // null when nothing is sent back to the browser.
        Response? Run(string argument, Lazy<Config> config);
    }
}
