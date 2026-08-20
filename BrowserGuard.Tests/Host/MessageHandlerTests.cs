using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;
using BrowserGuard.Host;
using BrowserGuard.Configuration;
using BrowserGuard.NetLogger;

namespace BrowserGuard.Tests.Host
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

        // The host makes this entry itself when it cannot keep a copy of an
        // uploaded file. Handle() would read the config from the registry, so
        // the writing is exercised through the seam the entry travels.
        [Fact]
        public void WritesAnEntryTheHostMadeItself()
        {
            var handler = Handler();

            var failure = handler.WriteLogEntry(
                NetLogEntry.UploadFileBridgeFailed(@"C:	mp
eport.xlsx", "access denied", DateTime.Now));

            Assert.Null(failure);
            var line = Assert.Single(File.ReadAllLines(LogPath));
            var root = JsonDocument.Parse(line).RootElement;
            Assert.Equal("upload-file-bridge", root.GetProperty("operation").GetString());
            Assert.Equal("access denied", root.GetProperty("reason").GetString());
        }

        // Turning the log off must not turn the copying off with it.
        [Fact]
        public void TakesAnEntryNowhereWhenTheLogIsOff()
        {
            var handler = Handler(enabled: false);

            var failure = handler.WriteLogEntry(
                NetLogEntry.UploadFileBridgeFailed("C:\a\f.txt", "no destination", DateTime.Now));

            Assert.Null(failure);
            Assert.False(File.Exists(LogPath));
        }

        // A dialog put on the screen for real would wait for someone to dismiss
        // it, so the test is handed somewhere else to put the text.
        MessageHandler DialogHandler(List<string> shown) =>
            new(null, null, shown.Add);

        [Fact]
        public void ShowsTheTextItIsGivenAsADialog()
        {
            var shown = new List<string>();

            var response = DialogHandler(shown).Handle("W タブを閉じました。");

            Assert.True(response!.Success);
            Assert.Equal("タブを閉じました。", Assert.Single(shown));
        }

        // The text is the whole of the message, so a limit that mentions several
        // numbers has to survive intact.
        [Fact]
        public void KeepsTheWholeWarningTogether()
        {
            var shown = new List<string>();

            DialogHandler(shown).Handle("W 上限は 5 個です。1 個を閉じました。");

            Assert.Equal("上限は 5 個です。1 個を閉じました。", Assert.Single(shown));
        }

        // Reading it would mean a registry and a file read for a message that
        // carries everything it needs.
        [Fact]
        public void ShowsTheDialogWithoutReadingTheConfiguration()
        {
            var shown = new List<string>();

            var response = DialogHandler(shown).Handle("W 警告");

            Assert.True(response!.Success);
            Assert.Null(response.Error);
        }

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
