using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using BrowserGuard.Startup;

namespace BrowserGuard.Tests.Startup
{
    public class EnvDumpTests : IDisposable
    {
        readonly string tempDir;

        public EnvDumpTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "browserguard-envdump-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }

        // Built next to the test assembly only when the whole solution is built,
        // so the check is skipped rather than failing a host-only build.
        static string? FindEnvDump()
        {
            var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var candidates = new[] { "Debug", "Release" }
                .Select(c => Path.Combine(repoRoot, "tools", "EnvDump", "bin", c, "net8.0", "EnvDump.exe"));
            return candidates.FirstOrDefault(File.Exists);
        }

        [Fact]
        public void StartsTheProgramWithTheConfiguredDirectoryAndEnvironment()
        {
            var envDump = FindEnvDump();
            if (envDump is null)
            {
                return;
            }

            var config = new StartupLauncherConfig
            {
                Enabled = true,
                Programs =
                [
                    new StartupProgramConfig
                    {
                        Path = envDump,
                        WorkingDirectory = tempDir,
                        EnvironmentVariables = new Dictionary<string, string>
                        {
                            ["BROWSERGUARD_STARTUP_TEST"] = "from-config",
                        },
                    },
                ],
            };

            Assert.Null(StartupLauncher.Run(config));

            var lines = ReadReport(Path.Combine(tempDir, "envdump.txt"));
            Assert.True(lines is not null, "the program did not write its report");

            Assert.Contains($"WorkingDirectory={tempDir}", lines!);
            Assert.Contains("BROWSERGUARD_STARTUP_TEST=from-config", lines);
            // The configured variables are added to the inherited environment.
            Assert.Contains(lines, line => line.StartsWith("PATH=", StringComparison.OrdinalIgnoreCase));
        }

        // The program writes under another name and moves the file into place,
        // so whatever is there is whole. The read can still land in the moment
        // of the move, when the file cannot be opened at all, hence the retry.
        // Waiting on File.Exists alone would read a file that is not there yet.
        static string[]? ReadReport(string path)
        {
            for (var i = 0; i < 100; i++)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        return File.ReadAllLines(path);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                System.Threading.Thread.Sleep(100);
            }
            return null;
        }
    }
}
