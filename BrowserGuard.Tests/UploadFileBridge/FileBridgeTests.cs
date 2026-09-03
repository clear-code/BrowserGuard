using System;
using System.IO;
using Xunit;
using BrowserGuard.Common;
using BrowserGuard.UploadFileBridge;

namespace BrowserGuard.Tests.UploadFileBridge
{
    public class FileBridgeTests : IDisposable
    {
        readonly string tempDir;

        public FileBridgeTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "browserguard-bridge-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }

        // Where the upload was going. Only the URL tests care what it is.
        const string Url = "https://example.com/upload";

        // The host's own log, read back to see what was said about a copy.
        string LogPath => Path.Combine(tempDir, "log", "BrowserGuard.log");

        string LogText()
        {
            var path = LogPath;
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }

        Logger Log() => new(Path.Combine(tempDir, "log"));

        // A fixed day, so a destination naming the day is the one intended here.
        static readonly DateTime Now = new(2026, 8, 20, 13, 45, 30);

        string Destination => Path.Combine(tempDir, "audit");

        UploadFileBridgeConfig Config(
            string? destination = null,
            int maxSizeMB = 0,
            string[]? blocked = null,
            string[]? allowed = null,
            string[]? blockedUrls = null,
            string[]? allowedUrls = null) =>
            new()
            {
                Enabled = true,
                Destination = destination ?? Destination,
                MaxSizeMB = maxSizeMB,
                BlockedExtensions = blocked ?? [],
                AllowedExtensions = allowed ?? [],
                BlockedUrls = blockedUrls ?? [],
                AllowedUrls = allowedUrls ?? [],
            };

        const int MB = 1024 * 1024;

        // A file of a given size, standing in for a large upload.
        string SourceOfSize(long bytes, string name = "big.bin")
        {
            var path = Path.Combine(tempDir, name);
            File.WriteAllBytes(path, new byte[bytes]);
            return path;
        }

        // A file of the user's own, standing in for one being uploaded.
        string Source(string name = "report.xlsx", string content = "hello")
        {
            var path = Path.Combine(tempDir, name);
            File.WriteAllText(path, content);
            return path;
        }

        string[] Copies => Directory.Exists(Destination)
            ? [.. Directory.GetFiles(Destination).Select(path => Path.GetFileName(path) ?? "").OrderBy(name => name)]
            : [];

        [Fact]
        public void KeepsACopyOfTheFile()
        {
            var source = Source(content: "the contents");

            var failure = FileBridge.Copy(Config(), source, Url, Now);

            Assert.Null(failure);
            Assert.Equal("the contents", File.ReadAllText(Path.Combine(Destination, "report.xlsx")));
        }

        // The destination is on a file server that may have nothing on it yet.
        [Fact]
        public void MakesTheDestinationWhenItIsNotThere()
        {
            Assert.False(Directory.Exists(Destination));

            FileBridge.Copy(Config(), Source(), Url, Now);

            Assert.True(Directory.Exists(Destination));
        }

        [Fact]
        public void ExpandsTheMacrosInTheDestination()
        {
            var config = Config(Path.Combine(tempDir, "%DATE%", "%PCNAME%"));

            var failure = FileBridge.Copy(config, Source(), Url, Now);

            Assert.Null(failure);
            var expected = Path.Combine(tempDir, "2026-08-20", Environment.MachineName, "report.xlsx");
            Assert.True(File.Exists(expected), $"not found: {expected}");
        }

        // Nothing that was uploaded may be lost to something uploaded later.
        [Fact]
        public void NumbersACopyRatherThanOverwriteOne()
        {
            FileBridge.Copy(Config(), Source(content: "first"), Url, Now);
            FileBridge.Copy(Config(), Source(content: "second"), Url, Now);

            Assert.Equal(["report_2.xlsx", "report.xlsx"], Copies);
            Assert.Equal("first", File.ReadAllText(Path.Combine(Destination, "report.xlsx")));
            Assert.Equal("second", File.ReadAllText(Path.Combine(Destination, "report_2.xlsx")));
        }

        [Fact]
        public void KeepsNumberingBeyondTheSecond()
        {
            for (var time = 0; time < 4; time++)
            {
                FileBridge.Copy(Config(), Source(content: $"copy {time}"), Url, Now);
            }

            Assert.Equal(
                ["report_2.xlsx", "report_3.xlsx", "report_4.xlsx", "report.xlsx"],
                Copies);
        }

        // The number goes before the extension, so the copy still opens.
        [Fact]
        public void KeepsTheExtensionOnANumberedCopy()
        {
            FileBridge.Copy(Config(), Source("notes.tar.gz"), Url, Now);
            FileBridge.Copy(Config(), Source("notes.tar.gz"), Url, Now);

            Assert.Equal(["notes.tar_2.gz", "notes.tar.gz"], Copies);
        }

        [Fact]
        public void CopiesAFileWithNoExtension()
        {
            FileBridge.Copy(Config(), Source("LICENSE"), Url, Now);
            FileBridge.Copy(Config(), Source("LICENSE"), Url, Now);

            Assert.Equal(["LICENSE", "LICENSE_2"], Copies);
        }

        // Nothing is copied off the machine unless it was asked for.
        [Fact]
        public void CopiesNothingWhileDisabled()
        {
            var config = Config();
            config.Enabled = false;

            var failure = FileBridge.Copy(config, Source(), Url, Now);

            Assert.Null(failure);
            Assert.False(Directory.Exists(Destination));
        }

        // One very large upload must not be able to fill the file server.
        [Fact]
        public void LeavesAFileOverTheLimitUncopied()
        {
            var failure = FileBridge.Copy(Config(maxSizeMB: 1), SourceOfSize(MB + 1), Url, Now);

            Assert.NotNull(failure);
            Assert.Contains("1 MB limit", failure);
            Assert.Empty(Copies);
        }

        // The limit is what may be copied, so the size itself is still allowed.
        [Fact]
        public void CopiesAFileExactlyOnTheLimit()
        {
            var failure = FileBridge.Copy(Config(maxSizeMB: 1), SourceOfSize(MB), Url, Now);

            Assert.Null(failure);
            Assert.Equal(["big.bin"], Copies);
        }

        [Fact]
        public void CopiesAFileUnderTheLimit()
        {
            var failure = FileBridge.Copy(Config(maxSizeMB: 1), SourceOfSize(MB - 1), Url, Now);

            Assert.Null(failure);
            Assert.Equal(["big.bin"], Copies);
        }

        // 0 is no limit, so a config that leaves the number out copies whatever
        // it is given rather than nothing at all.
        [Fact]
        public void CopiesAnySizeWhenNoLimitIsSet()
        {
            var failure = FileBridge.Copy(Config(maxSizeMB: 0), SourceOfSize(MB * 2), Url, Now);

            Assert.Null(failure);
            Assert.Equal(["big.bin"], Copies);
        }

        // Read the way UploadGuard reads its own lists.
        [Fact]
        public void LeavesABlockedExtensionUncopied()
        {
            var failure = FileBridge.Copy(Config(blocked: [".tmp"]), Source("scratch.tmp"), Url, Now);

            Assert.NotNull(failure);
            Assert.Empty(Copies);
        }

        [Fact]
        public void CopiesWhatIsNotOnTheBlockedList()
        {
            var failure = FileBridge.Copy(Config(blocked: [".tmp"]), Source(), Url, Now);

            Assert.Null(failure);
            Assert.Equal(["report.xlsx"], Copies);
        }

        // An empty allowed list is no restriction from that rule.
        [Fact]
        public void CopiesAnythingWhileNoAllowedListIsSet()
        {
            Assert.Null(FileBridge.Copy(Config(), Source("scratch.tmp"), Url, Now));
            Assert.Equal(["scratch.tmp"], Copies);
        }

        [Fact]
        public void CopiesOnlyTheAllowedExtensionsOnceOneIsSet()
        {
            var config = Config(allowed: [".xlsx", ".docx"]);

            Assert.Null(FileBridge.Copy(config, Source("report.xlsx"), Url, Now));
            Assert.NotNull(FileBridge.Copy(config, Source("scratch.tmp"), Url, Now));

            Assert.Equal(["report.xlsx"], Copies);
        }

        // The blocked list is checked first, so it wins over the allowed one.
        [Fact]
        public void LetsTheBlockedListWinOverTheAllowedOne()
        {
            var config = Config(blocked: [".xlsx"], allowed: [".xlsx"]);

            Assert.NotNull(FileBridge.Copy(config, Source("report.xlsx"), Url, Now));
            Assert.Empty(Copies);
        }

        // Local names are case insensitive on Windows.
        [Fact]
        public void MatchesAnExtensionRegardlessOfCase()
        {
            var failure = FileBridge.Copy(Config(blocked: [".TMP"]), Source("scratch.tmp"), Url, Now);

            Assert.NotNull(failure);
            Assert.Empty(Copies);
        }

        // Matched as regular expressions, the way UploadGuard matches its paths.
        [Fact]
        public void LeavesAnUploadToABlockedDestinationUncopied()
        {
            var config = Config(blockedUrls: [@"^https://example\.com/"]);

            Assert.NotNull(FileBridge.Copy(config, Source(), Url, Now));
            Assert.Empty(Copies);
        }

        [Fact]
        public void CopiesAnUploadToADestinationThatIsNotBlocked()
        {
            var config = Config(blockedUrls: [@"^https://intranet\."]);

            Assert.Null(FileBridge.Copy(config, Source(), Url, Now));
            Assert.Equal(["report.xlsx"], Copies);
        }

        // An empty allowed list is no restriction from that rule.
        [Fact]
        public void CopiesAnyDestinationWhileNoAllowedListIsSet()
        {
            Assert.Null(FileBridge.Copy(Config(), Source(), Url, Now));
            Assert.Equal(["report.xlsx"], Copies);
        }

        [Fact]
        public void CopiesOnlyTheAllowedDestinationsOnceOneIsSet()
        {
            var config = Config(allowedUrls: [@"^https://example\.com/"]);

            Assert.Null(FileBridge.Copy(config, Source("report.xlsx"), Url, Now));
            Assert.NotNull(FileBridge.Copy(config, Source("other.xlsx"), "https://elsewhere.test/", Now));

            Assert.Equal(["report.xlsx"], Copies);
        }

        // The blocked list is checked first, so it wins over the allowed one.
        [Fact]
        public void LetsTheBlockedDestinationWinOverTheAllowedOne()
        {
            var config = Config(
                blockedUrls: ["example"],
                allowedUrls: ["example"]);

            Assert.NotNull(FileBridge.Copy(config, Source(), Url, Now));
            Assert.Empty(Copies);
        }

        [Fact]
        public void MatchesADestinationRegardlessOfCase()
        {
            var config = Config(blockedUrls: [@"EXAMPLE\.COM"]);

            Assert.NotNull(FileBridge.Copy(config, Source(), Url, Now));
        }

        // One bad entry must not turn the list into "match everything" or
        // "match nothing".
        [Fact]
        public void DropsAnUnusablePatternWithoutLosingTheRest()
        {
            var config = Config(blockedUrls: ["[", "example"]);

            Assert.NotNull(FileBridge.Copy(config, Source(), Url, Now));
        }

        [Fact]
        public void CopiesWhenOnlyTheUnusablePatternWouldHaveMatched()
        {
            var config = Config(blockedUrls: ["["]);

            Assert.Null(FileBridge.Copy(config, Source(), Url, Now));
            Assert.Equal(["report.xlsx"], Copies);
        }

        [Fact]
        public void ReportsThatThereIsNowhereToPutIt()
        {
            var failure = FileBridge.Copy(Config(""), Source(), Url, Now);

            Assert.NotNull(failure);
        }

        [Fact]
        public void ReportsAFileThatIsNotThere()
        {
            var failure = FileBridge.Copy(Config(), Path.Combine(tempDir, "gone.xlsx"), Url, Now);

            Assert.NotNull(failure);
            Assert.Contains("gone.xlsx", failure);
        }

        [Fact]
        public void ReportsAPathItCannotUse()
        {
            var failure = FileBridge.Copy(Config(), "", Url, Now);

            Assert.NotNull(failure);
        }
        // A copy that was meant to be kept and was not has to say so, and say
        // why: the upload itself went through, so nothing else shows it.
        [Fact]
        public void SaysWhyNoCopyWasKept()
        {
            var config = Config(destination: Path.Combine(tempDir, "audit"), maxSizeMB: 1);
            var source = Source("report.xlsx", new string('a', 2 * 1024 * 1024));

            var failure = FileBridge.Copy(config, source, Url, Now, Log());

            Assert.NotNull(failure);
            var text = LogText();
            Assert.Contains("UploadFileBridge: no copy kept of", text);
            Assert.Contains(source, text);
            // The reason travels with it, rather than only reaching the caller.
            Assert.Contains(failure!, text);
        }

        [Fact]
        public void SaysNothingWhenTheCopyWasMade()
        {
            var failure = FileBridge.Copy(Config(), Source(), Url, Now, Log());

            Assert.Null(failure);
            Assert.DoesNotContain("no copy kept", LogText());
            Assert.Contains("UploadFileBridge: copied", LogText());
        }

    }
}
