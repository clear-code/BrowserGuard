using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Xunit;
using BrowserGuard.Startup;

namespace BrowserGuard.Tests.Startup
{
    public class StartupLauncherTests : IDisposable
    {
        // A real file to hash and a real program to start, so the checks are
        // exercised against the filesystem rather than a stand-in.
        readonly string tempDir;
        readonly string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        public StartupLauncherTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "browserguard-startup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }

        string WriteFile(string name, string content)
        {
            var path = Path.Combine(tempDir, name);
            File.WriteAllText(path, content);
            return path;
        }

        static string Sha256Of(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        [Fact]
        public void ComputesTheHashOfAFile()
        {
            var path = WriteFile("a.txt", "hello");

            // Known SHA-256 of "hello".
            Assert.Equal(
                "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
                StartupLauncher.ComputeSha256(path));
        }

        [Fact]
        public void AcceptsAProgramWithNoHashConfigured()
        {
            var program = new StartupProgramConfig { Path = WriteFile("a.txt", "x") };

            Assert.Null(StartupLauncher.Verify(program));
        }

        [Fact]
        public void AcceptsAProgramWhoseHashMatches()
        {
            var path = WriteFile("a.txt", "x");
            var program = new StartupProgramConfig { Path = path, Sha256 = Sha256Of(path) };

            Assert.Null(StartupLauncher.Verify(program));
        }

        [Fact]
        public void AcceptsAHashWrittenInUpperCase()
        {
            var path = WriteFile("a.txt", "x");
            var program = new StartupProgramConfig { Path = path, Sha256 = Sha256Of(path).ToUpperInvariant() };

            Assert.Null(StartupLauncher.Verify(program));
        }

        // The point of the hash: a swapped binary must not be started.
        [Fact]
        public void RejectsAProgramWhoseHashDoesNotMatch()
        {
            var path = WriteFile("a.txt", "x");
            var program = new StartupProgramConfig { Path = path, Sha256 = Sha256Of(path) };
            File.WriteAllText(path, "tampered");

            var rejection = StartupLauncher.Verify(program);

            Assert.NotNull(rejection);
            Assert.Contains("hash mismatch", rejection);
        }

        [Fact]
        public void RejectsAMissingProgram()
        {
            var program = new StartupProgramConfig { Path = Path.Combine(tempDir, "nope.exe") };

            Assert.Contains("not found", StartupLauncher.Verify(program));
        }

        [Fact]
        public void RejectsAProgramWithoutAPath()
        {
            Assert.NotNull(StartupLauncher.Verify(new StartupProgramConfig()));
        }

        [Fact]
        public void BuildsTheStartInfoFromTheConfiguration()
        {
            var program = new StartupProgramConfig
            {
                Path = cmd,
                Arguments = ["/c", "echo hello world"],
                WorkingDirectory = tempDir,
                EnvironmentVariables = new Dictionary<string, string> { ["BROWSERGUARD_TEST"] = "1" },
            };

            var info = StartupLauncher.BuildStartInfo(program);

            Assert.Equal(cmd, info.FileName);
            Assert.Equal(["/c", "echo hello world"], info.ArgumentList);
            Assert.Equal(tempDir, info.WorkingDirectory);
            Assert.Equal("1", info.Environment["BROWSERGUARD_TEST"]);
            // Required for the environment to be handed to the child.
            Assert.False(info.UseShellExecute);
        }

        [Fact]
        public void FallsBackToTheProgramsOwnDirectory()
        {
            var program = new StartupProgramConfig { Path = cmd, WorkingDirectory = "" };

            var info = StartupLauncher.BuildStartInfo(program);

            Assert.Equal(Path.GetDirectoryName(cmd), info.WorkingDirectory);
        }

        [Fact]
        public void StartsNothingWhileDisabled()
        {
            var marker = Path.Combine(tempDir, "ran.txt");
            var config = new StartupLauncherConfig
            {
                Enabled = false,
                Programs = [MarkerProgram()],
            };

            Assert.Null(StartupLauncher.Run(config));
            Assert.False(File.Exists(marker));
        }

        [Fact]
        public void StartsTheConfiguredProgram()
        {
            var marker = Path.Combine(tempDir, "ran.txt");
            var config = new StartupLauncherConfig
            {
                Enabled = true,
                Programs = [MarkerProgram()],
            };

            Assert.Null(StartupLauncher.Run(config));

            Assert.True(WaitForFile(marker), "the program did not run");
        }

        [Fact]
        public void PassesTheEnvironmentToTheProgram()
        {
            var marker = Path.Combine(tempDir, "env.txt");
            var program = BatchProgram("env.bat", "@echo %BROWSERGUARD_TEST% > env.txt");
            program.EnvironmentVariables = new Dictionary<string, string> { ["BROWSERGUARD_TEST"] = "from-config" };
            var config = new StartupLauncherConfig { Enabled = true, Programs = [program] };

            Assert.Null(StartupLauncher.Run(config));

            Assert.True(WaitForFile(marker), "the program did not run");
            Assert.Contains("from-config", File.ReadAllText(marker));
        }

        // A failure is reported but must not stop the programs after it.
        [Fact]
        public void KeepsGoingAfterOneProgramFails()
        {
            var marker = Path.Combine(tempDir, "ran.txt");
            var config = new StartupLauncherConfig
            {
                Enabled = true,
                Programs =
                [
                    new StartupProgramConfig { Path = Path.Combine(tempDir, "nope.exe") },
                    MarkerProgram(),
                ],
            };

            var failures = StartupLauncher.Run(config);

            Assert.NotNull(failures);
            Assert.Contains("not found", failures);
            Assert.True(WaitForFile(marker), "the second program did not run");
        }

        // The work is put in a batch file run from the temp directory, so the
        // test does not depend on how cmd.exe parses quotes inside an argument.
        // The name is prefixed with .\ because cmd does not look in the current
        // directory for a command to run.
        StartupProgramConfig BatchProgram(string name, string script)
        {
            WriteFile(name, script);
            return new StartupProgramConfig
            {
                Path = cmd,
                Arguments = ["/c", @".\" + name],
                WorkingDirectory = tempDir,
            };
        }

        StartupProgramConfig MarkerProgram() =>
            BatchProgram("marker.bat", "@echo ran > ran.txt");

        static bool WaitForFile(string path)
        {
            for (var i = 0; i < 50 && !File.Exists(path); i++)
            {
                System.Threading.Thread.Sleep(100);
            }
            return File.Exists(path);
        }
    }
}
