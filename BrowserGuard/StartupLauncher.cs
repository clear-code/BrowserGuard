using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace BrowserGuard
{
    internal static class StartupLauncher
    {
        internal static string? Run(StartupLauncherConfig config, Logger? logger = null)
        {
            if (!config.Enabled)
            {
                logger?.Log("StartupLauncher is disabled");
                return null;
            }

            var failures = new List<string>();
            foreach (var program in config.Programs)
            {
                var failure = Start(program, logger);
                if (failure is not null)
                {
                    logger?.Log($"StartupLauncher: {failure}");
                    failures.Add(failure);
                }
                else
                {
                    logger?.Log($"StartupLauncher started {program.Path}");
                }
            }

            return failures.Count == 0 ? null : string.Join("; ", failures);
        }

        private static string? Start(StartupProgramConfig program, Logger? logger)
        {
            var rejection = Verify(program);
            if (rejection is not null)
            {
                return rejection;
            }

            try
            {
                using var process = Process.Start(BuildStartInfo(program))
                    ?? throw new InvalidOperationException("no process was started");
                return WaitUntilStarted(process, program, logger);
            }
            catch (Exception ex)
            {
                return $"{program.Path}: {ex.Message}";
            }
        }

        internal static string? Verify(StartupProgramConfig program)
        {
            if (string.IsNullOrWhiteSpace(program.Path))
            {
                return "a program without a path was configured";
            }
            if (!File.Exists(program.Path))
            {
                return $"{program.Path}: not found";
            }
            if (string.IsNullOrWhiteSpace(program.Sha256))
            {
                return null;
            }

            var actual = ComputeSha256(program.Path);
            if (!actual.Equals(program.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return $"{program.Path}: hash mismatch, expected {program.Sha256} but found {actual}";
            }
            return null;
        }

        internal static string ComputeSha256(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        internal static ProcessStartInfo BuildStartInfo(StartupProgramConfig program)
        {
            // UseShellExecute has to be off for the environment to be passed on.
            var info = new ProcessStartInfo
            {
                FileName = program.Path,
                UseShellExecute = false,
                WorkingDirectory = string.IsNullOrWhiteSpace(program.WorkingDirectory)
                    ? Path.GetDirectoryName(Path.GetFullPath(program.Path)) ?? ""
                    : program.WorkingDirectory,
            };

            foreach (var argument in program.Arguments)
            {
                info.ArgumentList.Add(argument);
            }
            foreach (var variable in program.EnvironmentVariables)
            {
                info.Environment[variable.Key] = variable.Value;
            }

            return info;
        }


        /// <summary>
        /// Wait until the program is ready to receive input, or until the timeout expires.
        /// TimeoutSeconds bounds how long we wait for the program to finish starting, not how long it may run. 
        /// </summary>
        /// <param name="process"></param>
        /// <param name="program"></param>
        /// <param name="logger"></param>
        /// <returns></returns>
        private static string? WaitUntilStarted(Process process, StartupProgramConfig program, Logger? logger)
        {
            if (program.TimeoutSeconds <= 0)
            {
                return null;
            }

            try
            {
                if (!process.WaitForInputIdle(program.TimeoutSeconds * 1000))
                {
                    return $"{program.Path}: still not ready after {program.TimeoutSeconds}s";
                }
            }
            catch (InvalidOperationException)
            {
                // No message loop, so there is no idle state to wait for.
                logger?.Log($"StartupLauncher: {program.Path} has no UI, not waiting");
            }
            return null;
        }
    }
}
