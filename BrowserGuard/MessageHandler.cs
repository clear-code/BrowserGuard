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
            if (message.StartsWith("C "))
            {
                logger?.Log("Command: load config");
                var config = ConfigLoader.LoadConfig();
                return new ConfigResponse { Success = true, Config = config };
            }

            return new Response { Success = true };
        }
    }
}
