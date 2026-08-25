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
