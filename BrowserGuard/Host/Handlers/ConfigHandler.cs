using BrowserGuard.Common;
using BrowserGuard.Configuration;

namespace BrowserGuard.Host.Handlers
{
    // The browser reads the config for itself: most of what it holds is acted
    // on there rather than here.
    internal sealed class ConfigHandler : IMessageHandler
    {
        private readonly Logger? logger;

        internal ConfigHandler(Logger? logger = null) => this.logger = logger;

        public string Command => "C";

        public Response? Run(string argument, Lazy<Config> config)
        {
            logger?.Log("Command: load config");
            return new ConfigResponse { Success = true, Config = config.Value };
        }
    }
}
