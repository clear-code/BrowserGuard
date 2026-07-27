namespace BrowserGuard
{
    class Program
    {
        static Logger Logger { get; } = new Logger();

        static void Main()
        {
            using var stdin = Console.OpenStandardInput();
            using var stdout = Console.OpenStandardOutput();
            var communicator = new MessageCommunicator(stdin, stdout, Logger);
            var handler = new MessageHandler(Logger);

            while (true)
            {
                try
                {
                    var message = communicator.ReadMessage();
                    var response = handler.Handle(message);
                    Logger.Log($"Response: {response}");
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
