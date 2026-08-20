using System;
using System.IO;
using System.Linq;
using Xunit;
using BrowserGuard.Common;

namespace BrowserGuard.Tests.Common
{
    public class LoggerTests : IDisposable
    {
        readonly string tempDir;

        public LoggerTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "browserguard-logger-" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }

        string LogPath => Path.Combine(tempDir, "BrowserGuard.log");

        string GenerationPath(int generation) =>
            Path.Combine(tempDir, $"BrowserGuard_{generation}.log");

        [Fact]
        public void WritesTheMessageWithATimestamp()
        {
            new Logger(tempDir).Log("hello");

            var line = Assert.Single(File.ReadAllLines(LogPath));
            Assert.EndsWith(" : hello", line);
            Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} : ", line);
        }

        [Fact]
        public void AppendsRatherThanReplacing()
        {
            var logger = new Logger(tempDir);

            logger.Log("first");
            logger.Log("second");

            var lines = File.ReadAllLines(LogPath);
            Assert.Equal(2, lines.Length);
            Assert.EndsWith("first", lines[0]);
            Assert.EndsWith("second", lines[1]);
        }

        [Fact]
        public void CreatesTheDirectory()
        {
            new Logger(Path.Combine(tempDir, "nested")).Log("hello");

            Assert.True(File.Exists(Path.Combine(tempDir, "nested", "BrowserGuard.log")));
        }

        [Fact]
        public void KeepsWorkingWhenItCannotWrite()
        {
            // A path that cannot be a directory, so the logger has nowhere to go.
            var file = Path.Combine(tempDir, "occupied");
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(file, "x");

            var logger = new Logger(file);

            logger.Log("hello");
        }

        // The rotation used to look for the generations in the parent of the
        // directory the log is really in, so it found nothing to move and then
        // truncated the log. Every message written so far was lost.
        [Fact]
        public void KeepsTheEarlierMessagesWhenItRotates()
        {
            var logger = new Logger(tempDir, maxLogSize: 200);

            logger.Log(new string('a', 300));
            logger.Log("after the rotation");

            Assert.True(File.Exists(GenerationPath(1)), "the log should have been moved aside");
            Assert.Contains("aaa", File.ReadAllText(GenerationPath(1)));
            Assert.Contains("after the rotation", File.ReadAllText(LogPath));
        }

        [Fact]
        public void PutsTheGenerationsBesideTheLog()
        {
            var logger = new Logger(tempDir, maxLogSize: 200);

            logger.Log(new string('a', 300));
            logger.Log(new string('b', 300));
            logger.Log("c");

            Assert.Contains("bbb", File.ReadAllText(GenerationPath(1)));
            Assert.Contains("aaa", File.ReadAllText(GenerationPath(2)));
            // Nothing may be written outside the directory it was given.
            var parent = Directory.GetFiles(Path.GetDirectoryName(tempDir)!, "BrowserGuard*.log");
            Assert.Empty(parent);
        }

        [Fact]
        public void DropsTheOldestGeneration()
        {
            var logger = new Logger(tempDir, maxLogSize: 100);

            // One more than the eleven files the rotation keeps.
            for (var i = 0; i < 13; i++)
            {
                logger.Log(new string('x', 200));
            }

            var kept = Directory.GetFiles(tempDir, "BrowserGuard*.log")
                .Select(Path.GetFileName)
                .OrderBy(name => name)
                .ToArray();
            Assert.Equal(MaxGenerations + 1, kept.Length);
            Assert.DoesNotContain($"BrowserGuard_{MaxGenerations + 1}.log", kept);
        }

        const int MaxGenerations = 10;

        // A port keeps one host process running while others come and go, so the
        // log must not be held open by whichever of them started first.
        [Fact]
        public void LetsAnotherProcessWriteWhileTheFileIsOpen()
        {
            new Logger(tempDir).Log("first");

            using (var held = new FileStream(
                LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                new Logger(tempDir).Log("second");
            }

            var lines = File.ReadAllLines(LogPath);
            Assert.Equal(2, lines.Length);
            Assert.EndsWith("second", lines[1]);
        }

        [Fact]
        public void ReleasesTheFileBetweenMessages()
        {
            var logger = new Logger(tempDir);

            logger.Log("hello");

            // Exclusive access proves nothing is still holding the file.
            using var exclusive = new FileStream(
                LogPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
    }
}
