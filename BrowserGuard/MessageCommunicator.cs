using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace BrowserGuard
{
    // runtime.sendNativeMessage only accepts an object, so the browser wraps the
    // command in one instead of sending a bare JSON string.
    internal class Request
    {
        public string? Message { get; set; }
    }

    internal class MessageCommunicator
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private readonly BinaryReader reader;
        private readonly BinaryWriter writer;
        private readonly Logger? logger;

        internal MessageCommunicator(Stream input, Stream output, Logger? logger = null)
        {
            reader = new BinaryReader(input);
            writer = new BinaryWriter(output);
            this.logger = logger;
        }

        internal string ReadMessage()
        {
            var lenBytes = reader.ReadBytes(4);
            logger?.Log($"Read {lenBytes.Length} bytes for message length");
            if (lenBytes.Length < 4)
                throw new EndOfStreamException();

            var len = BitConverter.ToUInt32(lenBytes, 0);

            var body = reader.ReadBytes((int)len);
            logger?.Log($"Read {body.Length} bytes for message body");
            if (body.Length < len)
                throw new EndOfStreamException();

            // Native messaging delivers the payload as JSON.
            string? text;
            try
            {
                text = JsonSerializer.Deserialize<Request>(body, JsonOptions)?.Message?.Trim();
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Message is not valid JSON: {ex.Message}");
            }
            logger?.Log($"text: {text}");
            if (string.IsNullOrEmpty(text))
                throw new InvalidDataException("Message is empty");
            return text;
        }

        internal void WriteMessage(object obj)
        {
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj));
            writer.Write((uint)body.Length);
            writer.Write(body);
            writer.Flush();
        }
    }
}
