using System.Text.Json;
using BrowserGuard.Common;
using BrowserGuard.Configuration;
using BrowserGuard.NetLogger;
using BrowserGuard.UploadFileBridge;

namespace BrowserGuard.Host.Handlers
{
    internal sealed class UploadFileBridgeHandler : IMessageHandler
    {
        // The browser sends the path and where it was going, as JSON: a path
        // may hold spaces, so the two cannot simply be put either side of one.
        private sealed class Upload
        {
            public string File { get; set; } = "";
            public string Url { get; set; } = "";
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private readonly NetLogRecorder recorder;
        private readonly Logger? logger;

        internal UploadFileBridgeHandler(NetLogRecorder recorder, Logger? logger = null)
        {
            this.recorder = recorder;
            this.logger = logger;
        }

        public string Command => "U";

        public string Description => "bridge upload";

        public Response? Run(string argument, Lazy<Config> config)
        {
            Upload? upload;
            try
            {
                upload = JsonSerializer.Deserialize<Upload>(argument, JsonOptions);
            }
            catch (JsonException ex)
            {
                return new Response { Success = false, Error = $"cannot read the upload: {ex.Message}" };
            }
            if (string.IsNullOrWhiteSpace(upload?.File))
            {
                return new Response { Success = false, Error = "the upload names no file" };
            }

            var now = DateTime.Now;
            var failure = FileBridge.Copy(
                config.Value.UploadFileBridge, upload.File, upload.Url, now, logger);
            if (failure is not null)
            {
                RecordFailure(upload, failure, now);
            }
            return new Response { Success = failure is null, Error = failure };
        }

        // A copy that was meant to be kept and was not belongs in the audit
        // trail: the upload itself went through, so nothing else records it.
        private void RecordFailure(Upload upload, string reason, DateTime at)
        {
            var failure = recorder.Record(
                NetLogEntry.UploadFileBridgeFailed(upload.File, upload.Url, reason, at));
            if (failure is not null)
            {
                logger?.Log($"Cannot record the failed copy: {failure}");
            }
        }
    }
}
