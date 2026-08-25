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
