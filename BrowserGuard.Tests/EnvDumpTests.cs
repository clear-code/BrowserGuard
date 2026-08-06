using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrowserGuard;
using Xunit;

namespace BrowserGuard.Tests
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

            var report = Path.Combine(tempDir, "envdump.txt");
            Assert.True(WaitForFile(report), "the program did not write its report");

            var lines = File.ReadAllLines(report);
            Assert.Contains($"WorkingDirectory={tempDir}", lines);
            Assert.Contains("BROWSERGUARD_STARTUP_TEST=from-config", lines);
            // The configured variables are added to the inherited environment.
            Assert.Contains(lines, line => line.StartsWith("PATH=", StringComparison.OrdinalIgnoreCase));
        }

        static bool WaitForFile(string path)
        {
            for (var i = 0; i < 100 && !File.Exists(path); i++)
            {
                System.Threading.Thread.Sleep(100);
            }
            return File.Exists(path);
        }
    }
}
