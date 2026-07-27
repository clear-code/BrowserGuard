using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Xml.Serialization;
using BrowserGuard;
using static BrowserGuard.Program;

namespace BrowserGuard
{
    class Program
    {
        internal class Response
        {
            public bool Success { get; set; }
            public string? Error { get; set; }
            public Config? Config { get; set; }

            public override string ToString() => JsonSerializer.Serialize(this);
        }

        static Logger Logger { get; } = new Logger();

        static void Main()
        {
            using var stdin = new BinaryReader(Console.OpenStandardInput());
            using var stdout = new BinaryWriter(Console.OpenStandardOutput());

            var config = ConfigLoader.LoadConfig();

            while (true)
            {
                try
                {
                    var command = ReadMessage(stdin);
                    var response = new Response
                    {
                        Success = true,
                        Config = config,
                    };
                    Logger.Log($"Response: {response}");
                    WriteMessage(stdout, response);
                }
                catch (EndOfStreamException) { break; }
                catch (Exception ex)
                {
                    Logger.Log(ex.Message);
                    WriteMessage(stdout, new Response { Success = false, Error = ex.Message });
                }
            }
        }

        internal static char ReadMessage(BinaryReader reader)
        {
            var lenBytes = reader.ReadBytes(4);
            Logger.Log($"Read {lenBytes.Length} bytes for message length");
            if (lenBytes.Length < 4)
                throw new EndOfStreamException();

            var len = BitConverter.ToUInt32(lenBytes, 0);

            var body = reader.ReadBytes((int)len);
            Logger.Log($"Read {body.Length} bytes for message body");
            if (body.Length < len)
                throw new EndOfStreamException();

            // Native messaging delivers the payload as a JSON-encoded value.
            string? text = JsonSerializer.Deserialize<string>(body)?.Trim();
            Logger.Log($"text: {text}");
            if (string.IsNullOrEmpty(text))
                throw new InvalidDataException("Message is empty");
            return text[0];
        }

        static void WriteMessage(BinaryWriter writer, object obj)
        {
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj));
            writer.Write((uint)body.Length);
            writer.Write(body);
            writer.Flush();
        }
    }
}