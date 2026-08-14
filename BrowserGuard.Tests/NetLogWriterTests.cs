using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BrowserGuard;
using Xunit;

namespace BrowserGuard.Tests
{
    public class NetLogWriterTests : IDisposable
    {
        readonly string tempDir;

        public NetLogWriterTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "browserguard-netlog-" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }

        NetLogWriter Writer(int maxSizeMB = 10, int maxGenerations = 10) =>
            new(new NetLogFileConfig
            {
                Enabled = true,
                Directory = tempDir,
                MaxSizeMB = maxSizeMB,
                MaxGenerations = maxGenerations,
            });

        string LogPath => Path.Combine(tempDir, "netlog.jsonl");

        string GenerationPath(int generation) =>
            Path.Combine(tempDir, $"netlog_{generation}.jsonl");

        static string Entry(string operation) =>
            $$"""{"operation":"{{operation}}","url":"https://example.com/"}""";

        [Fact]
        public void WritesOneLinePerEntry()
        {
            var writer = Writer();

            Assert.Null(writer.Write(Entry("browsing")));
            Assert.Null(writer.Write(Entry("download")));

            var lines = File.ReadAllLines(LogPath);
            Assert.Equal(2, lines.Length);
            Assert.Equal("browsing", JsonDocument.Parse(lines[0]).RootElement
                .GetProperty("operation").GetString());
            Assert.Equal("download", JsonDocument.Parse(lines[1]).RootElement
                .GetProperty("operation").GetString());
        }

        [Fact]
        public void CreatesTheDirectory()
        {
            var writer = new NetLogWriter(new NetLogFileConfig
            {
                Enabled = true,
                Directory = Path.Combine(tempDir, "nested"),
            });

            Assert.Null(writer.Write(Entry("browsing")));

            Assert.True(File.Exists(Path.Combine(tempDir, "nested", "netlog.jsonl")));
        }

        [Fact]
        public void KeepsTheEarlierEntriesWhenItRotates()
        {
            var writer = Writer(maxSizeMB: 1);
            var padding = new string('a', 200_000);

            // Six entries of 200KB take the file past the one megabyte limit.
            for (var i = 0; i < 6; i++)
            {
                writer.Write($$"""{"operation":"browsing","name":"{{padding}}"}""");
            }
            writer.Write(Entry("after"));

            Assert.True(File.Exists(GenerationPath(1)), "the log should have been moved aside");
            var kept = File.ReadAllLines(GenerationPath(1));
            Assert.Equal(6, kept.Length);
            Assert.Equal("after", JsonDocument.Parse(File.ReadAllLines(LogPath)[0]).RootElement
                .GetProperty("operation").GetString());
        }

        [Fact]
        public void KeepsTheGenerationsBesideTheLog()
        {
            var writer = Writer(maxSizeMB: 1, maxGenerations: 2);
            var padding = new string('a', 600_000);

            for (var i = 0; i < 8; i++)
            {
                writer.Write($$"""{"operation":"browsing","name":"{{padding}}"}""");
            }

            var kept = Directory.GetFiles(tempDir)
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(
                new[] { "netlog.jsonl", "netlog_1.jsonl", "netlog_2.jsonl" },
                kept);
        }

        [Fact]
        public void DiscardsTheLogWhenNoGenerationIsKept()
        {
            var writer = Writer(maxSizeMB: 1, maxGenerations: 0);
            var padding = new string('a', 600_000);

            for (var i = 0; i < 4; i++)
            {
                writer.Write($$"""{"operation":"browsing","name":"{{padding}}"}""");
            }

            Assert.Equal(new[] { "netlog.jsonl" },
                Directory.GetFiles(tempDir).Select(Path.GetFileName).ToArray());
        }

        // The browser can be killed at any moment, so nothing may sit in a buffer.
        [Fact]
        public void LeavesNothingUnwrittenBetweenEntries()
        {
            var writer = Writer();

            writer.Write(Entry("browsing"));

            Assert.Single(File.ReadAllLines(LogPath));
        }

        // The log is collected while the browser is running. Holding the file
        // open would block anything that opens it the ordinary way, which is
        // what File.ReadAllText, copy and Notepad all do.
        [Fact]
        public void LeavesTheLogReadableByAnOrdinaryReader()
        {
            var writer = Writer();
            writer.Write(Entry("browsing"));

            using var reader = new FileStream(
                LogPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            Assert.NotEqual(0, reader.Length);
        }

        // The reader locks writers out for as long as it holds the file, so an
        // entry that arrives during a collection has to wait rather than be lost.
        [Fact]
        public void WaitsForAReaderRatherThanLosingTheEntry()
        {
            var writer = Writer();
            writer.Write(Entry("browsing"));

            using (var held = new FileStream(
                LogPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var releasing = Task.Run(() =>
                {
                    Thread.Sleep(150);
                    held.Dispose();
                });

                Assert.Null(writer.Write(Entry("download")));
                releasing.Wait();
            }

            Assert.Equal(2, File.ReadAllLines(LogPath).Length);
        }

        [Fact]
        public void ReportsAFailureRatherThanThrowing()
        {
            // A file where the directory has to go, so it can never be created.
            Directory.CreateDirectory(tempDir);
            var blocked = Path.Combine(tempDir, "blocked");
            File.WriteAllText(blocked, "x");
            var writer = new NetLogWriter(new NetLogFileConfig
            {
                Enabled = true,
                Directory = blocked,
            });

            Assert.NotNull(writer.Write(Entry("browsing")));
        }

        [Fact]
        public void FallsBackToProgramDataWhenNoDirectoryIsGiven()
        {
            var writer = new NetLogWriter(new NetLogFileConfig { Enabled = true });

            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "BrowserGuard", "netlog", "netlog.jsonl");
            Assert.Equal(expected, writer.FilePath);
        }
    }
}
