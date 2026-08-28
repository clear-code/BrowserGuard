using BrowserGuard.Common;

namespace BrowserGuard.NetLogger
{
    // Where an entry goes: the log kept on this machine, the collector, or
    // both. Which of them are in use is settled once, on the first entry,
    // because an entry arrives for every request and settling it again would
    // mean reading the config for every one of them.
    internal sealed class NetLogRecorder : IDisposable
    {
        // Handed in rather than read here, so that this side knows nothing of
        // where a config comes from. It is called at most once.
        private readonly Func<NetLoggerConfig> readConfig;
        private readonly Logger? logger;

        private NetLogFileWriter? file;
        private NetLogSender? sender;
        private bool resolved;

        internal NetLogRecorder(Func<NetLoggerConfig> readConfig, Logger? logger = null)
        {
            this.readConfig = readConfig;
            this.logger = logger;
        }

        // null when the entry was taken, or when there is nowhere to put it.
        // Otherwise why it was not.
        internal string? Record(string entry)
        {
            Resolve();
            if (file is null && sender is null)
            {
                return null;
            }

            // Checked once here, so that both destinations are handed the same
            // single line and a bad entry is refused before either sees it.
            var rejection = NetLogEntry.Compact(entry, out var line);
            if (rejection is not null)
            {
                return rejection;
            }

            // The collector is best effort; only the file reports a failure,
            // because that is the copy the entry was meant to survive in.
            sender?.Enqueue(line);

            return file?.Write(line);
        }

        // Read once and remembered, including the decision not to log at all.
        // NetLogger turns the whole feature off; the two destinations are
        // independent of each other below that.
        private void Resolve()
        {
            if (resolved)
            {
                return;
            }
            resolved = true;

            var config = readConfig();
            if (!config.Enabled)
            {
                logger?.Log("Command: log entry, but logging is disabled");
                return;
            }

            if (config.LocalFile.Enabled)
            {
                file = new NetLogFileWriter(config.LocalFile, logger);
                logger?.Log($"Command: log entry, writing to {file.FilePath}");
            }
            if (config.Sender.Enabled && !string.IsNullOrWhiteSpace(config.Sender.Endpoint))
            {
                var spool = new NetLogSpool(
                    NetLogDirectory.ForSpool(),
                    // 0 asks for no limit, and travels as 0.
                    Math.Max(0, config.Sender.MaxSpoolSizeMB) * 1024L * 1024L,
                    logger);
                sender = new NetLogSender(
                    config.Sender.Endpoint,
                    spool,
                    logger,
                    handler: null,
                    // Below a minute is read as a minute, so that a collector
                    // which is down cannot be asked in a tight loop.
                    retryInterval: TimeSpan.FromMinutes(Math.Max(1, config.Sender.RetryIntervalMinutes)));
                logger?.Log(
                    $"Command: log entry, sending to {config.Sender.Endpoint} by way of {spool.FilePath}");
            }
        }

        public void Dispose() => sender?.Dispose();
    }
}
