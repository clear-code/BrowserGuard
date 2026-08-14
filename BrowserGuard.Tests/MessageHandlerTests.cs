using System;
using System.IO;
using System.Text.Json;
using BrowserGuard;
using Xunit;

namespace BrowserGuard.Tests
{
    public class MessageHandlerTests : IDisposable
    {
        readonly string tempDir;

        public MessageHandlerTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "browserguard-handler-" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }

        string LogPath => Path.Combine(tempDir, "netlog.jsonl");

        // The configuration is handed in so the test does not go through the
        // registry to find out where it lives.
        MessageHandler Handler(bool enabled = true, bool localFile = true) =>
            new(null, new NetLoggerConfig
            {
                Enabled = enabled,
                LocalFile = new NetLogFileConfig { Enabled = localFile, Directory = tempDir },
            });

        const string Entry = """{"operation":"browsing","url":"https://example.com/"}""";

        // An entry arrives for every request, so acknowledging each one would
        // double the traffic over the port.
        [Fact]
        public void AnswersNothingWhenAnEntryIsWritten()
        {
            var handler = Handler();

            Assert.Null(handler.Handle("L " + Entry));

            var line = Assert.Single(File.ReadAllLines(LogPath));
            Assert.Equal("browsing", JsonDocument.Parse(line).RootElement
                .GetProperty("operation").GetString());
        }

        [Fact]
        public void WritesEveryEntryItIsGiven()
        {
            var handler = Handler();

            handler.Handle("L " + Entry);
            handler.Handle("L " + Entry);
            handler.Handle("L " + Entry);

            Assert.Equal(3, File.ReadAllLines(LogPath).Length);
        }

        [Fact]
        public void ReportsAnEntryItCannotWrite()
        {
            var handler = Handler();

            var response = handler.Handle("L this is not json");

            Assert.NotNull(response);
            Assert.False(response.Success);
            Assert.Contains("not valid JSON", response.Error);
        }

        // Nothing is written and nothing is said, because the browser knows from
        // the same configuration that it should not be sending these.
        [Fact]
        public void StaysSilentWhenLocalLoggingIsTurnedOff()
        {
            var handler = Handler(localFile: false);

            Assert.Null(handler.Handle("L " + Entry));

            Assert.False(File.Exists(LogPath));
        }

        // NetLogger turns the whole feature off, whatever LocalFile says.
        [Fact]
        public void StaysSilentWhenTheWholeLoggerIsTurnedOff()
        {
            var handler = Handler(enabled: false, localFile: true);

            Assert.Null(handler.Handle("L " + Entry));

            Assert.False(File.Exists(LogPath));
        }

        [Fact]
        public void LeavesTheOtherCommandsAlone()
        {
            var handler = Handler();

            // Anything unrecognised still gets a plain acknowledgement.
            var response = handler.Handle("X something");

            Assert.NotNull(response);
            Assert.True(response.Success);
        }
    }
}
