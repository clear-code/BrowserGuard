using BrowserGuard.Common;
using BrowserGuard.Configuration;
using BrowserGuard.NetLogger;
using BrowserGuard.UploadFileBridge;

namespace BrowserGuard.Host.Handlers
{
    internal sealed class UploadFileBridgeHandler : IMessageHandler
    {
        private readonly NetLogRecorder recorder;
        private readonly Logger? logger;

        internal UploadFileBridgeHandler(NetLogRecorder recorder, Logger? logger = null)
        {
            this.recorder = recorder;
            this.logger = logger;
        }

        public string Command => "U";

        public Response? Run(string argument, Lazy<Config> config)
        {
            logger?.Log("Command: bridge upload");
            var now = DateTime.Now;
            var failure = FileBridge.Copy(config.Value.UploadFileBridge, argument, now, logger);
            if (failure is not null)
            {
                RecordFailure(argument, failure, now);
            }
            return new Response { Success = failure is null, Error = failure };
        }

        // A copy that was meant to be kept and was not belongs in the audit
        // trail: the upload itself went through, so nothing else records it.
        private void RecordFailure(string file, string reason, DateTime at)
        {
            var failure = recorder.Record(NetLogEntry.UploadFileBridgeFailed(file, reason, at));
            if (failure is not null)
            {
                logger?.Log($"Cannot record the failed copy: {failure}");
            }
        }
    }
}
