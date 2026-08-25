using BrowserGuard.Common;
using BrowserGuard.SubCommands.Policy;

namespace BrowserGuard.Host
{
    class Program
    {
        static Logger Logger { get; } = new Logger();

        static int Main(string[] args)
        {
            // The browser starts the host with the calling origin as an argument,
            // so only the explicit subcommand name is treated as one.
            if (args.Length > 0 &&
                args[0].Equals(PolicyCommand.CommandName, StringComparison.OrdinalIgnoreCase))
            {
                return PolicyCommand.Run(args);
            }

            RunNativeMessagingHost();
            return 0;
        }

        static void RunNativeMessagingHost()
        {
            using var stdin = Console.OpenStandardInput();
            using var stdout = Console.OpenStandardOutput();
            var communicator = new MessageCommunicator(stdin, stdout, Logger);
            // Disposed on the way out so that queued entries still reach the
            // collector after the browser has closed the port.
            using var dispatcher = new MessageDispatcher(Logger);

            while (true)
            {
                try
                {
                    var message = communicator.ReadMessage();
                    var response = dispatcher.Handle(message);
                    // A handler that answers with nothing wants the browser left
                    // alone; a log entry does not need acknowledging.
                    if (response is null)
                    {
                        continue;
                    }
                    // Logger.Log($"Response: {response}");
                    communicator.WriteMessage(response);
                }
                catch (EndOfStreamException) { break; }
                catch (Exception ex)
                {
                    Logger.Log(ex.Message);
                    communicator.WriteMessage(new Response { Success = false, Error = ex.Message });
                }
            }
        }
    }
}
