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

    internal class MessageHandler : IDisposable
    {
        private readonly Logger? logger;

        private NetLogWriter? netLogFile;
        private NetLogSender? netLogSender;
        private bool netLogResolved;

        // The logging configuration can be handed in so that it does not have to
        // be found through the registry.
        internal MessageHandler(Logger? logger = null, NetLoggerConfig? netLoggerConfig = null)
        {
            this.logger = logger;
            if (netLoggerConfig is null)
            {
                return;
            }
            ResolveNetLog(netLoggerConfig);
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
            ResolveNetLog();
            if (netLogFile is null && netLogSender is null)
            {
                return null;
            }

            // Checked once here, so that both destinations are handed the same
            // single line and a bad entry is refused before either sees it.
            var rejection = NetLogEntry.Compact(entry, out var line);
            if (rejection is not null)
            {
                return new Response { Success = false, Error = rejection };
            }

            // The collector is best effort; only the file reports a failure,
            // because that is the copy the entry was meant to survive in.
            netLogSender?.Enqueue(line);

            var failure = netLogFile?.Write(line);
            if (failure is null)
            {
                return null;
            }
            return new Response { Success = false, Error = failure };
        }

        // Read once and remembered, including the decision not to log at all.
        private void ResolveNetLog()
        {
            if (netLogResolved)
            {
                return;
            }
            ResolveNetLog(ConfigLoader.LoadConfig().NetLogger);
        }

        // NetLogger turns the whole feature off; the two destinations are
        // independent of each other below that.
        private void ResolveNetLog(NetLoggerConfig config)
        {
            netLogResolved = true;
            if (!config.Enabled)
            {
                logger?.Log("Command: log entry, but logging is disabled");
                return;
            }

            if (config.LocalFile.Enabled)
            {
                netLogFile = new NetLogWriter(config.LocalFile, logger);
                logger?.Log($"Command: log entry, writing to {netLogFile.FilePath}");
            }
            if (!string.IsNullOrWhiteSpace(config.Endpoint))
            {
                netLogSender = new NetLogSender(
                    config.Endpoint,
                    logger,
                    handler: null,
                    spool: Spool(config),
                    retryInterval: TimeSpan.FromMinutes(config.OnSendFailure.RetryIntervalMinutes));
                logger?.Log($"Command: log entry, sending to {config.Endpoint}");
            }
        }

        private NetLogSpool? Spool(NetLoggerConfig config)
        {
            var failure = config.OnSendFailure;
            if (!failure.SaveLocally)
            {
                return null;
            }
            var spool = new NetLogSpool(
                NetLogWriter.ResolveDirectory(config.LocalFile.Directory),
                // 0 asks for no limit, and travels as 0.
                Math.Max(0, failure.MaxSizeMB) * 1024L * 1024L,
                logger);
            logger?.Log($"Command: log entry, keeping what cannot be sent in {spool.FilePath}");
            return spool;
        }

        public void Dispose() => netLogSender?.Dispose();
    }
}
