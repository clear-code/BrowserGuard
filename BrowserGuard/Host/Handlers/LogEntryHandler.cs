using BrowserGuard.Configuration;
using BrowserGuard.NetLogger;

namespace BrowserGuard.Host.Handlers
{
    // An entry for the audit log. The recorder is not this handler's to close:
    // the entries the host makes for itself go to the same one.
    internal sealed class LogEntryHandler : IMessageHandler
    {
        private readonly NetLogRecorder recorder;

        internal LogEntryHandler(NetLogRecorder recorder) => this.recorder = recorder;

        public string Command => "L";

        // An entry arrives for every request; naming each one would fill
        // the diagnostic log faster than anything else on the machine.
        public string Description => "";

        // Answering every entry would double the traffic over the port for no
        // purpose, so only a failure is reported.
        public Response? Run(string argument, Lazy<Config> config)
        {
            var failure = recorder.Record(argument);
            if (failure is null)
            {
                return null;
            }
            return new Response { Success = false, Error = failure };
        }
    }
}
