using System;
using System.Text.Json;
using BrowserGuard.Common;
using BrowserGuard.Configuration;
using BrowserGuard.NetLogger;
using BrowserGuard.Startup;
using BrowserGuard.UploadFileBridge;

namespace BrowserGuard.Host
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

    internal class MessageDispatcher : IDisposable
    {
        private readonly Logger? logger;
        private readonly Action<string> showDialog;

        private readonly NetLogRecorder netLog;

        // The logging configuration can be handed in so that it does not have to
        // be found through the registry, and so can the way a warning is shown,
        // so that a test does not put a dialog on the screen and then wait for
        // someone to dismiss it.
        internal MessageDispatcher(
            Logger? logger = null,
            NetLoggerConfig? netLoggerConfig = null,
            Action<string>? showDialog = null)
        {
            this.logger = logger;
            this.showDialog = showDialog ?? (text => Dialog.Show(text, logger));
            // Reading the config is put off until the first entry: one arrives
            // for every request, and this side is asked for other things too.
            netLog = new NetLogRecorder(
                netLoggerConfig is null
                    ? () => ConfigLoader.LoadConfig().NetLogger
                    : () => netLoggerConfig,
                logger);
        }

        // The commands: "L " a log entry, "W " show a warning, "C " the config,
        // "S " run the startup programs, "U " keep a copy of an upload.
        //
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

            // Handled before the configuration is read, because the text to show
            // arrives with the message and nothing in the config bears on it.
            if (message.StartsWith("W "))
            {
                logger?.Log("Command: warn");
                showDialog(message[2..].Trim());
                return new Response { Success = true };
            }

            var config = ConfigLoader.LoadConfig();
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
            else if (message.StartsWith("U "))
            {
                logger?.Log("Command: bridge upload");
                var now = DateTime.Now;
                var path = message[2..].Trim();
                var failure = FileBridge.Copy(config.UploadFileBridge, path, now, logger);
                if (failure is not null)
                {
                    RecordCopyFailure(path, failure, now);
                }
                return new Response { Success = failure is null, Error = failure };
            }

            return new Response { Success = true };
        }

        private Response? HandleLogEntry(string entry)
        {
            var failure = netLog.Record(entry);
            if (failure is null)
            {
                return null;
            }
            return new Response { Success = false, Error = failure };
        }

        // A copy that was meant to be kept and was not belongs in the audit
        // trail: the upload itself went through, so nothing else records it.
        private void RecordCopyFailure(string file, string reason, DateTime at)
        {
            var failure = netLog.Record(NetLogEntry.UploadFileBridgeFailed(file, reason, at));
            if (failure is not null)
            {
                logger?.Log($"Cannot record the failed copy: {failure}");
            }
        }

        public void Dispose() => netLog.Dispose();
    }
}
