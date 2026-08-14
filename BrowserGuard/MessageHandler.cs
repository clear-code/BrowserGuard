using System;
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

        private NetLogWriter? netLog;
        private bool netLogResolved;

        // The logging configuration can be handed in so that it does not have to
        // be found through the registry.
        internal MessageHandler(Logger? logger = null, NetLogFileConfig? netLogConfig = null)
        {
            this.logger = logger;
            if (netLogConfig is null)
            {
                return;
            }
            netLog = Create(netLogConfig);
            netLogResolved = true;
        }

        // null means nothing is sent back to the browser. Log entries arrive for
        // every request, so answering each one would double the traffic over the
        // port for no purpose; only their failures are worth reporting.
        internal Response? Handle(string message)
        {
            // Handled before the configuration is read, because an entry arrives
            // for every request and reading it here would mean a registry and a
            // file read per request.
            if (message.StartsWith("L "))
            {
                return HandleLogEntry(message[2..].Trim());
            }

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

        private Response? HandleLogEntry(string entry)
        {
            var writer = NetLog();
            if (writer is null)
            {
                return null;
            }

            var failure = writer.Write(entry);
            if (failure is null)
            {
                return null;
            }
            return new Response { Success = false, Error = failure };
        }

        // Read once and remembered, including the decision not to log at all.
        private NetLogWriter? NetLog()
        {
            if (netLogResolved)
            {
                return netLog;
            }
            netLogResolved = true;
            netLog = Create(ConfigLoader.LoadConfig().NetLogger.LocalFile);
            return netLog;
        }

        private NetLogWriter? Create(NetLogFileConfig config)
        {
            if (!config.Enabled)
            {
                logger?.Log("Command: log entry, but local logging is disabled");
                return null;
            }
            var writer = new NetLogWriter(config, logger);
            logger?.Log($"Command: log entry, writing to {writer.FilePath}");
            return writer;
        }
    }
}
