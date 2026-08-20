using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Xunit;
using BrowserGuard.Host;

namespace BrowserGuard.Tests.Host
{
    public class ReadMessageTests
    {
        // The browser wraps the command in an object, because
        // runtime.sendNativeMessage does not accept a bare string.
        static MessageCommunicator MakeCommunicator(string text)
        {
            return MakeRawCommunicator(JsonSerializer.Serialize(new { message = text }));
        }

        static MessageCommunicator MakeRawCommunicator(string json)
        {
            var body = Encoding.UTF8.GetBytes(json);
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

        [Theory]
        [InlineData("{}")]
        [InlineData("""{ "message": null }""")]
        public void ThrowsWhenTheMessageMemberIsMissing(string json)
        {
            Assert.Throws<InvalidDataException>(() => MakeRawCommunicator(json).ReadMessage());
        }

        [Fact]
        public void ThrowsWhenTheBodyIsNotJson()
        {
            Assert.Throws<InvalidDataException>(() => MakeRawCommunicator("not json").ReadMessage());
        }

        // The browser sends a lowercase member name.
        [Fact]
        public void MemberNameIsCaseInsensitive()
        {
            Assert.Equal("C edge", MakeRawCommunicator("""{ "Message": "C edge" }""").ReadMessage());
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
