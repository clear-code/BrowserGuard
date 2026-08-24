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

        public Response? Run(string argument, Lazy<Config> config)
        {
            logger?.Log("Command: startup");
            var failures = StartupLauncher.Run(config.Value.StartupLauncher, logger);
            return new Response { Success = failures is null, Error = failures };
        }
    }
}
