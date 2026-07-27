using System;
using System.IO;
using System.Text;
using System.Text.Json;
using NetLogger;
using Xunit;

namespace NetLogger.Tests
{
    public class ReadMessageTests
    {
        // Native messaging delivers the payload as a JSON-encoded value.
        static BinaryReader MakeReader(string text)
        {
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(text));
            var stream = new MemoryStream();
            stream.Write(BitConverter.GetBytes((uint)body.Length), 0, 4);
            stream.Write(body, 0, body.Length);
            stream.Position = 0;
            return new BinaryReader(stream);
        }

        [Fact]
        public void ParsesCommandAndPath()
        {
            var command = Program.ReadMessage(MakeReader("C"));

            Assert.Equal('C', command);
        }

        [Fact]
        public void ParsesJsonEncodedWindowsPath()
        {
            var command = Program.ReadMessage(
                MakeReader(@"S"));

            Assert.Equal('S', command);
        }

        [Fact]
        public void TrimsSurroundingWhitespace()
        {
            var command = Program.ReadMessage(MakeReader("  C \r\n"));

            Assert.Equal('C', command);
        }

        [Theory]
        [InlineData("")]
        public void ThrowsFormatExceptionWhenTooShort(string text)
        {
            Assert.Throws<InvalidDataException>(() => Program.ReadMessage(MakeReader(text)));
        }


        [Fact]
        public void ThrowsEndOfStreamWhenStreamIsEmpty()
        {
            var reader = new BinaryReader(new MemoryStream());

            Assert.Throws<EndOfStreamException>(() => Program.ReadMessage(reader));
        }

        [Fact]
        public void ThrowsEndOfStreamWhenLengthPrefixIsIncomplete()
        {
            var reader = new BinaryReader(new MemoryStream(new byte[] { 0x01, 0x02 }));

            Assert.Throws<EndOfStreamException>(() => Program.ReadMessage(reader));
        }
    }
}
