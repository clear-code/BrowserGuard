using BrowserGuard.Common;
using BrowserGuard.Configuration;
using BrowserGuard.Host.Handlers;
using BrowserGuard.NetLogger;

namespace BrowserGuard.Host
{
    // Hands a message to the handler for the letter it opens with. What each
    // command does is that handler's business; what is here is the shape of a
    // message and which handlers there are.
    internal class MessageDispatcher : IDisposable
    {
        // Shared by the entries the browser sends and the ones the host makes
        // for itself, so it is owned here rather than by either handler.
        private readonly NetLogRecorder netLog;

        private readonly Dictionary<string, IMessageHandler> handlers;

        // The logging configuration can be handed in so that it does not have to
        // be found through the registry, and so can the way a warning is shown,
        // so that a test does not put a dialog on the screen and then wait for
        // someone to dismiss it.
        internal MessageDispatcher(
            Logger? logger = null,
            NetLoggerConfig? netLoggerConfig = null,
            Action<string>? showDialog = null)
        {
            netLog = new NetLogRecorder(
                netLoggerConfig is null
                    ? () => ConfigLoader.LoadConfig().NetLogger
                    : () => netLoggerConfig,
                logger);

            handlers = new IMessageHandler[]
            {
                new LogEntryHandler(netLog),
                new WarnHandler(showDialog ?? (text => Dialog.Show(text, logger)), logger),
                new ConfigHandler(logger),
                new StartupHandler(logger),
                new UploadFileBridgeHandler(netLog, logger),
            }.ToDictionary(handler => handler.Command);
        }

        // A message is the letter, a space, and the argument. A letter nothing
        // answers to is not an error: the browser is free to say more than this
        // host knows about.
        //
        // null means nothing is sent back to the browser.
        internal Response? Handle(string message)
        {
            if (message.Length < 2 || message[1] != ' ' ||
                !handlers.TryGetValue(message[..1], out var handler))
            {
                return new Response { Success = true };
            }

            // Read at most once for the message, and only if the handler asks.
            var config = new Lazy<Config>(ConfigLoader.LoadConfig);
            return handler.Run(message[2..].Trim(), config);
        }

        public void Dispose() => netLog.Dispose();
    }
}
