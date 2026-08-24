using System;
using System.IO;
using System.Text.Json;
using Xunit;
using BrowserGuard.NetLogger;

namespace BrowserGuard.Tests.NetLogger
{
    public class NetLogRecorderTests : IDisposable
    {
        readonly string tempDir;

        public NetLogRecorderTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "browserguard-recorder-" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }

        string LogPath => Path.Combine(tempDir, "netlog.jsonl");

        // The configuration is handed in, so the test does not go through the
        // registry to find out where the log lives.
        NetLoggerConfig Config(bool enabled = true, bool localFile = true) =>
            new()
            {
                Enabled = enabled,
                LocalFile = new NetLogFileConfig { Enabled = localFile, Directory = tempDir },
            };

        const string Entry = """{"operation":"browsing","url":"https://example.com/"}""";

        [Fact]
        public void WritesAnEntryToTheLogOnThisMachine()
        {
            using var recorder = new NetLogRecorder(() => Config());

            var failure = recorder.Record(Entry);

            Assert.Null(failure);
            var line = Assert.Single(File.ReadAllLines(LogPath));
            Assert.Equal("browsing", JsonDocument.Parse(line).RootElement
                .GetProperty("operation").GetString());
        }

        // The one the host makes for itself travels the same way as the ones
        // the browser sends, so that it is stamped and written alike.
        [Fact]
        public void WritesAnEntryTheHostMadeItself()
        {
            using var recorder = new NetLogRecorder(() => Config());

            var failure = recorder.Record(NetLogEntry.UploadFileBridgeFailed(
                @"C:\tmp\report.xlsx", "https://example.com/upload", "access denied", DateTime.Now));

            Assert.Null(failure);
            var root = JsonDocument.Parse(Assert.Single(File.ReadAllLines(LogPath))).RootElement;
            Assert.Equal("upload-file-bridge", root.GetProperty("operation").GetString());
            Assert.Equal(@"C:\tmp\report.xlsx", root.GetProperty("name").GetString());
            Assert.Equal("access denied", root.GetProperty("reason").GetString());
        }

        // Turning the log off must not turn what it records off with it: the
        // copying and the blocking carry on, they simply go unrecorded.
        [Fact]
        public void TakesAnEntryNowhereWhenTheLogIsOff()
        {
            using var recorder = new NetLogRecorder(() => Config(enabled: false));

            var failure = recorder.Record(Entry);

            Assert.Null(failure);
            Assert.False(File.Exists(LogPath));
        }

        [Fact]
        public void TakesAnEntryNowhereWhenNoDestinationIsOn()
        {
            using var recorder = new NetLogRecorder(() => Config(localFile: false));

            Assert.Null(recorder.Record(Entry));
            Assert.False(File.Exists(LogPath));
        }

        [Fact]
        public void ReportsAnEntryItCannotRead()
        {
            using var recorder = new NetLogRecorder(() => Config());

            var failure = recorder.Record("not json at all");

            Assert.Contains("not valid JSON", failure);
            Assert.False(File.Exists(LogPath));
        }

        // An entry arrives for every request, so settling this again each time
        // would mean a registry and a file read per request.
        [Fact]
        public void SettlesWhereEntriesGoOnlyOnce()
        {
            var reads = 0;
            using var recorder = new NetLogRecorder(() => { reads++; return Config(); });

            recorder.Record(Entry);
            recorder.Record(Entry);
            recorder.Record(Entry);

            Assert.Equal(1, reads);
            Assert.Equal(3, File.ReadAllLines(LogPath).Length);
        }

        // Including the decision not to log at all, which is the case that
        // would otherwise be settled again on every entry.
        [Fact]
        public void SettlesOnceEvenWhenThereIsNowhereToWrite()
        {
            var reads = 0;
            using var recorder = new NetLogRecorder(() => { reads++; return Config(enabled: false); });

            recorder.Record(Entry);
            recorder.Record(Entry);

            Assert.Equal(1, reads);
        }
    }
}
