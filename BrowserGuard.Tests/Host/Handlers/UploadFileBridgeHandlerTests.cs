using System;
using System.IO;
using System.Text.Json;
using Xunit;
using BrowserGuard.Configuration;
using BrowserGuard.Host.Handlers;
using BrowserGuard.NetLogger;
using BrowserGuard.UploadFileBridge;

namespace BrowserGuard.Tests.Host.Handlers
{
    public class UploadFileBridgeHandlerTests : IDisposable
    {
        readonly string tempDir;

        public UploadFileBridgeHandlerTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "browserguard-uhandler-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }

        const string Url = "https://example.com/upload";

        string Destination => Path.Combine(tempDir, "audit");
        string LogPath => Path.Combine(tempDir, "netlog", "netlog.jsonl");

        // The log and the copies both go under the test's own folder, so the
        // machine running the tests is left alone.
        NetLogRecorder Recorder() => new(() => new NetLoggerConfig
        {
            Enabled = true,
            LocalFile = new NetLogFileConfig
            {
                Enabled = true,
                Directory = Path.Combine(tempDir, "netlog"),
            },
        });

        Lazy<Config> Config(string[]? blocked = null, string[]? blockedUrls = null) => new(() => new Config
        {
            UploadFileBridge = new UploadFileBridgeConfig
            {
                Enabled = true,
                Destination = Destination,
                BlockedExtensions = blocked ?? [],
                BlockedUrls = blockedUrls ?? [],
            },
        });

        // What the browser sends: the path and where it was going, as JSON.
        static string Message(string file, string url = Url) =>
            JsonSerializer.Serialize(new { file, url });

        string Source(string name)
        {
            var path = Path.Combine(tempDir, name);
            File.WriteAllText(path, "hello");
            return path;
        }

        JsonElement OnlyEntry() =>
            JsonDocument.Parse(Assert.Single(File.ReadAllLines(LogPath))).RootElement;

        // The upload itself went through, so nothing else records that no copy
        // of it was kept.
        [Fact]
        public void RecordsACopyThatWasRefused()
        {
            using var recorder = Recorder();
            var handler = new UploadFileBridgeHandler(recorder);
            var source = Source("scratch.tmp");

            var response = handler.Run(Message(source), Config(blocked: [".tmp"]));

            Assert.False(response!.Success);
            var root = OnlyEntry();
            Assert.Equal("upload-file-bridge", root.GetProperty("operation").GetString());
            Assert.Equal(source, root.GetProperty("name").GetString());
            Assert.Contains("scratch.tmp", root.GetProperty("reason").GetString());
        }

        // Where it was going is the point of the entry when the destination is
        // what refused it.
        [Fact]
        public void RecordsWhereTheUploadWasGoing()
        {
            using var recorder = Recorder();
            var handler = new UploadFileBridgeHandler(recorder);

            var response = handler.Run(
                Message(Source("report.xlsx")), Config(blockedUrls: ["example"]));

            Assert.False(response!.Success);
            Assert.Equal(Url, OnlyEntry().GetProperty("url").GetString());
        }

        // A copy that was kept is not a gap in the trail, so it needs no entry
        // of its own: the upload is already recorded by the browser.
        [Fact]
        public void RecordsNothingWhenTheCopyWasKept()
        {
            using var recorder = Recorder();
            var handler = new UploadFileBridgeHandler(recorder);

            var response = handler.Run(Message(Source("report.xlsx")), Config());

            Assert.True(response!.Success);
            Assert.True(File.Exists(Path.Combine(Destination, "report.xlsx")));
            Assert.False(File.Exists(LogPath));
        }

        [Fact]
        public void ReportsAMessageItCannotRead()
        {
            using var recorder = Recorder();
            var handler = new UploadFileBridgeHandler(recorder);

            var response = handler.Run("not json at all", Config());

            Assert.False(response!.Success);
            Assert.NotNull(response.Error);
        }

        [Fact]
        public void ReportsAMessageThatNamesNoFile()
        {
            using var recorder = Recorder();
            var handler = new UploadFileBridgeHandler(recorder);

            var response = handler.Run("""{"url":"https://example.com/"}""", Config());

            Assert.False(response!.Success);
            Assert.NotNull(response.Error);
        }
    }
}
