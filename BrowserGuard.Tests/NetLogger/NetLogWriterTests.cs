using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using BrowserGuard.NetLogger;

namespace BrowserGuard.Tests.NetLogger
{
    public class NetLogWriterTests : IDisposable
    {
        readonly string tempDir;

        public NetLogWriterTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "browserguard-netlog-" + Guid.NewGuid().ToString("N"));
            // Several tests put a day's file in place before the writer runs.
            Directory.CreateDirectory(tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }

        NetLogWriter Writer(int maxDays = 30, int maxSizeMB = 0) =>
            new(new NetLogFileConfig
            {
                Enabled = true,
                Directory = tempDir,
                MaxDays = maxDays,
                MaxSizeMB = maxSizeMB,
            });

        string LogPath => Path.Combine(tempDir, "netlog.jsonl");

        string PathForDay(DateTime day) =>
            Path.Combine(tempDir, $"netlog_{day:yyyy-MM-dd}.jsonl");

        string PathForSegment(DateTime day, int segment) =>
            Path.Combine(tempDir, $"netlog_{day:yyyy-MM-dd}_{segment}.jsonl");

        static string Entry(string operation) =>
            $$"""{"operation":"{{operation}}","url":"https://example.com/"}""";

        // The turn of the day is what triggers a rotation, and the day the
        // entries belong to is taken from the file itself. Backdating the file
        // is therefore the same thing as waiting for midnight.
        void PretendTheLogIsFrom(DateTime day) =>
            File.SetLastWriteTime(LogPath, day.Date.AddHours(23));

        static string[] Days(string dir) =>
            Directory.GetFiles(dir, "netlog_*.jsonl")
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray()!;

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
        public void KeepsTheDaysEntriesInOneFile()
        {
            var writer = Writer();

            for (var i = 0; i < 20; i++)
            {
                writer.Write(Entry("browsing"));
            }

            Assert.Equal(20, File.ReadAllLines(LogPath).Length);
            Assert.Empty(Days(tempDir));
        }

        [Fact]
        public void PutsTheDayAsideWhenTheDateChanges()
        {
            var writer = Writer();
            var yesterday = DateTime.Now.Date.AddDays(-1);
            writer.Write(Entry("yesterday"));
            PretendTheLogIsFrom(yesterday);

            writer.Write(Entry("today"));

            Assert.Contains("yesterday", File.ReadAllText(PathForDay(yesterday)));
            var lines = File.ReadAllLines(LogPath);
            Assert.Single(lines);
            Assert.Contains("today", lines[0]);
        }

        // Nothing may be lost at the turn of the day.
        [Fact]
        public void KeepsEveryEntryAcrossARotation()
        {
            var writer = Writer();
            var yesterday = DateTime.Now.Date.AddDays(-1);
            writer.Write(Entry("first"));
            writer.Write(Entry("second"));
            PretendTheLogIsFrom(yesterday);

            writer.Write(Entry("third"));

            Assert.Equal(2, File.ReadAllLines(PathForDay(yesterday)).Length);
            Assert.Single(File.ReadAllLines(LogPath));
        }

        // A host that was not running at midnight rotates on its next entry, and
        // the file it puts aside carries the date its entries were written on.
        [Fact]
        public void NamesTheFileAfterTheDayItsEntriesAreFrom()
        {
            var writer = Writer();
            var lastWeek = DateTime.Now.Date.AddDays(-7);
            writer.Write(Entry("old"));
            PretendTheLogIsFrom(lastWeek);

            writer.Write(Entry("new"));

            Assert.True(File.Exists(PathForDay(lastWeek)));
            Assert.Equal(new[] { Path.GetFileName(PathForDay(lastWeek)) }, Days(tempDir));
        }

        // Appending to the file an earlier split left behind would put the day
        // straight back over the size that split it.
        [Fact]
        public void StartsANewSegmentWhenTheDayAlreadyHasAFile()
        {
            var writer = Writer();
            var yesterday = DateTime.Now.Date.AddDays(-1);
            File.WriteAllLines(PathForDay(yesterday), new[] { Entry("earlier") });
            writer.Write(Entry("later"));
            PretendTheLogIsFrom(yesterday);

            writer.Write(Entry("today"));

            Assert.Single(File.ReadAllLines(PathForDay(yesterday)));
            Assert.Contains("later", File.ReadAllText(PathForSegment(yesterday, 2)));
        }

        [Fact]
        public void SplitsADayThatGrowsPastTheSizeLimit()
        {
            var writer = Writer(maxSizeMB: 1);
            var padding = new string('a', 200_000);

            // Six entries of 200KB take the day past one megabyte.
            for (var i = 0; i < 6; i++)
            {
                writer.Write($$"""{"operation":"browsing","name":"{{padding}}"}""");
            }
            writer.Write(Entry("after"));

            var today = DateTime.Now.Date;
            Assert.Equal(6, File.ReadAllLines(PathForDay(today)).Length);
            Assert.Contains("after", File.ReadAllText(LogPath));
        }

        // The segments have to sort into the order the entries happened in.
        [Fact]
        public void NumbersTheSegmentsOfASplitDay()
        {
            var writer = Writer(maxSizeMB: 1);
            var padding = new string('a', 200_000);

            for (var i = 0; i < 18; i++)
            {
                writer.Write($$"""{"operation":"browsing","n":{{i}},"name":"{{padding}}"}""");
            }

            var today = DateTime.Now.Date;
            Assert.True(File.Exists(PathForDay(today)));
            Assert.True(File.Exists(PathForSegment(today, 2)));
            Assert.Contains("\"n\":0", File.ReadAllLines(PathForDay(today))[0]);
            Assert.Contains("\"n\":6", File.ReadAllLines(PathForSegment(today, 2))[0]);
        }

        [Fact]
        public void LeavesTheDayWholeWhenNoSizeLimitIsSet()
        {
            var writer = Writer(maxSizeMB: 0);
            var padding = new string('a', 200_000);

            for (var i = 0; i < 12; i++)
            {
                writer.Write($$"""{"operation":"browsing","name":"{{padding}}"}""");
            }

            Assert.Empty(Days(tempDir));
            Assert.Equal(12, File.ReadAllLines(LogPath).Length);
        }

        // The date decides first: a new day starts a file of its own even when
        // the one before it was nowhere near the size limit.
        [Fact]
        public void StartsANewDayWellUnderTheSizeLimit()
        {
            var writer = Writer(maxSizeMB: 100);
            var yesterday = DateTime.Now.Date.AddDays(-1);
            writer.Write(Entry("yesterday"));
            PretendTheLogIsFrom(yesterday);

            writer.Write(Entry("today"));

            Assert.Contains("yesterday", File.ReadAllText(PathForDay(yesterday)));
            Assert.Contains("today", File.ReadAllText(LogPath));
        }

        // A split day is still that day, so it goes when the day does.
        [Fact]
        public void DropsEverySegmentOfADayPastTheRetention()
        {
            var writer = Writer(maxDays: 7, maxSizeMB: 1);
            var today = DateTime.Now.Date;
            var stale = today.AddDays(-30);
            File.WriteAllText(PathForDay(stale), Entry("stale") + "\n");
            File.WriteAllText(PathForSegment(stale, 2), Entry("stale too") + "\n");
            writer.Write(Entry("yesterday"));
            PretendTheLogIsFrom(today.AddDays(-1));

            writer.Write(Entry("today"));

            Assert.False(File.Exists(PathForDay(stale)));
            Assert.False(File.Exists(PathForSegment(stale, 2)));
        }

        [Fact]
        public void RotatesNothingWhileTheDayIsUnchanged()
        {
            var writer = Writer();
            writer.Write(Entry("first"));

            writer.Write(Entry("second"));

            Assert.Empty(Days(tempDir));
            Assert.Equal(2, File.ReadAllLines(LogPath).Length);
        }

        [Fact]
        public void DropsTheDaysOlderThanTheRetention()
        {
            var writer = Writer(maxDays: 7);
            var today = DateTime.Now.Date;
            File.WriteAllText(PathForDay(today.AddDays(-3)), Entry("kept") + "\n");
            File.WriteAllText(PathForDay(today.AddDays(-30)), Entry("stale") + "\n");
            writer.Write(Entry("yesterday"));
            PretendTheLogIsFrom(today.AddDays(-1));

            writer.Write(Entry("today"));

            Assert.True(File.Exists(PathForDay(today.AddDays(-3))));
            Assert.False(File.Exists(PathForDay(today.AddDays(-30))), "the stale day should be gone");
        }

        [Fact]
        public void KeepsEveryDayWhenNoRetentionIsSet()
        {
            var writer = Writer(maxDays: 0);
            var today = DateTime.Now.Date;
            File.WriteAllText(PathForDay(today.AddDays(-3650)), Entry("ancient") + "\n");
            writer.Write(Entry("yesterday"));
            PretendTheLogIsFrom(today.AddDays(-1));

            writer.Write(Entry("today"));

            Assert.True(File.Exists(PathForDay(today.AddDays(-3650))));
        }

        // The entries kept for the collector live in the same directory.
        [Fact]
        public void LeavesTheOtherFilesInTheDirectoryAlone()
        {
            var writer = Writer(maxDays: 1);
            Directory.CreateDirectory(tempDir);
            var pending = Path.Combine(tempDir, "netlog-pending.jsonl");
            var unrelated = Path.Combine(tempDir, "netlog_not-a-date.jsonl");
            File.WriteAllText(pending, Entry("pending") + "\n");
            File.WriteAllText(unrelated, "whatever\n");
            File.SetLastWriteTime(pending, DateTime.Now.AddDays(-100));
            File.SetLastWriteTime(unrelated, DateTime.Now.AddDays(-100));
            writer.Write(Entry("yesterday"));
            PretendTheLogIsFrom(DateTime.Now.Date.AddDays(-1));

            writer.Write(Entry("today"));

            Assert.True(File.Exists(pending), "the spool must not be touched");
            Assert.True(File.Exists(unrelated), "a file that is not a day must not be touched");
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

        // The same macros the copies of uploaded files are filed under, so one
        // machine's log can be told from another's on a shared drive.
        [Fact]
        public void ExpandsTheMacrosInTheDirectory()
        {
            var configured = Path.Combine(tempDir, "%PCNAME%", "%DATE%");

            var resolved = NetLogWriter.ResolveDirectory(configured, new DateTime(2026, 8, 20));

            Assert.Equal(
                Path.Combine(tempDir, Environment.MachineName, "2026-08-20"),
                resolved);
        }

        [Fact]
        public void ExpandsAnEnvironmentVariableInTheDirectory()
        {
            var before = Environment.GetEnvironmentVariable("BROWSERGUARD_LOGS");
            Environment.SetEnvironmentVariable("BROWSERGUARD_LOGS", tempDir);
            try
            {
                Assert.Equal(
                    Path.Combine(tempDir, "netlog"),
                    NetLogWriter.ResolveDirectory(Path.Combine("%BROWSERGUARD_LOGS%", "netlog")));
            }
            finally
            {
                Environment.SetEnvironmentVariable("BROWSERGUARD_LOGS", before);
            }
        }

        // The log really lands where the macros said, not just the path.
        [Fact]
        public void WritesIntoTheDirectoryTheMacrosNamed()
        {
            var config = new NetLogFileConfig
            {
                Enabled = true,
                Directory = Path.Combine(tempDir, "%PCNAME%"),
            };

            var writer = new NetLogWriter(config);
            writer.Write("""{"operation":"browsing"}""");

            var expected = Path.Combine(tempDir, Environment.MachineName, "netlog.jsonl");
            Assert.True(File.Exists(expected), $"not found: {expected}");
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
