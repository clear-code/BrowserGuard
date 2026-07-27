using System;
using System.IO;
using System.Text;
using System.Text.Json;
using BrowserGuard;
using Xunit;

namespace BrowserGuard.Tests
{
    public class ReadMessageTests
    {
        // Native messaging delivers the payload as a JSON-encoded value.
        static MessageCommunicator MakeCommunicator(string text)
        {
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(text));
            var stream = new MemoryStream();
            stream.Write(BitConverter.GetBytes((uint)body.Length), 0, 4);
            stream.Write(body, 0, body.Length);
            stream.Position = 0;
            return new MessageCommunicator(stream, new MemoryStream());
        }

        static MessageCommunicator MakeCommunicator(Stream input)
        {
            return new MessageCommunicator(input, new MemoryStream());
        }

        [Fact]
        public void ParsesCommandAndPath()
        {
            var command = MakeCommunicator("C").ReadMessage();

            Assert.Equal("C", command);
        }

        [Fact]
        public void ParsesJsonEncodedWindowsPath()
        {
            var command = MakeCommunicator(@"S").ReadMessage();

            Assert.Equal("S", command);
        }

        [Fact]
        public void TrimsSurroundingWhitespace()
        {
            var command = MakeCommunicator("  C \r\n").ReadMessage();

            Assert.Equal("C", command);
        }

        [Theory]
        [InlineData("")]
        public void ThrowsFormatExceptionWhenTooShort(string text)
        {
            Assert.Throws<InvalidDataException>(() => MakeCommunicator(text).ReadMessage());
        }


        [Fact]
        public void ThrowsEndOfStreamWhenStreamIsEmpty()
        {
            var communicator = MakeCommunicator(new MemoryStream());

            Assert.Throws<EndOfStreamException>(() => communicator.ReadMessage());
        }

        [Fact]
        public void ThrowsEndOfStreamWhenLengthPrefixIsIncomplete()
        {
            var communicator = MakeCommunicator(new MemoryStream(new byte[] { 0x01, 0x02 }));

            Assert.Throws<EndOfStreamException>(() => communicator.ReadMessage());
        }
    }
}
