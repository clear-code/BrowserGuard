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
