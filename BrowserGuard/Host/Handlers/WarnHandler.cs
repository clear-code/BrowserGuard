using BrowserGuard.Common;
using BrowserGuard.Configuration;

namespace BrowserGuard.Host.Handlers
{
    // The text to show arrives with the message, so nothing in the config
    // bears on it.
    internal sealed class WarnHandler : IMessageHandler
    {
        private readonly Action<string> show;
        private readonly Logger? logger;

        internal WarnHandler(Action<string> show, Logger? logger = null)
        {
            this.show = show;
            this.logger = logger;
        }

        public string Command => "W";

        public Response? Run(string argument, Lazy<Config> config)
        {
            logger?.Log("Command: warn");
            show(argument);
            return new Response { Success = true };
        }
    }
}
