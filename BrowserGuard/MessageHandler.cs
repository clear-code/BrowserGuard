using System.Text.Json;

namespace BrowserGuard
{
    internal class Response
    {
        public bool Success { get; set; }
        public string? Error { get; set; }

        // Serialize by runtime type so that derived properties are emitted too.
        public override string ToString() => JsonSerializer.Serialize(this, GetType());
    }

    internal class ConfigResponse : Response
    {
        public Config? Config { get; set; }
    }

    internal class MessageHandler
    {
        private readonly Logger? logger;

        internal MessageHandler(Logger? logger = null)
        {
            this.logger = logger;
        }

        internal Response Handle(string message)
        {
            var config = ConfigLoader.LoadConfig();
            // "C " load config, "S " run the startup programs.
            if (message.StartsWith("C "))
            {
                logger?.Log("Command: load config");
                return new ConfigResponse { Success = true, Config = config };
            }
            else if (message.StartsWith("S "))
            {
                logger?.Log("Command: startup");
                var failures = StartupLauncher.Run(config.StartupLauncher, logger);
                return new Response { Success = failures is null, Error = failures };
            }

            return new Response { Success = true };
        }
    }
}
