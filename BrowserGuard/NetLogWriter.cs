using System;
using System.IO;
using System.Text;
using System.Threading;

namespace BrowserGuard
{
    // Appends the browsing log, one JSON object per line.
    //
    // The file is opened only for the moment of the write. Holding it open is
    // measurably faster, but a writer's open handle makes the file unreadable to
    // anything that opens it the ordinary way (FileShare.Read) -- which is what
    // File.ReadAllText, copy and Notepad all do. An audit log nobody can collect
    // while the browser is running is worth less than the throughput. Reopening
    // still sustains a couple of thousand entries a second, far above what
    // browsing produces.
    //
    // Only the process holding the browser's native messaging port writes here,
    // which is why a lock is enough where the diagnostic Logger needs a named
    // mutex across processes.
    internal sealed class NetLogWriter
    {
        internal const string FileNameBase = "netlog";
        internal const string FileExtension = ".jsonl";

        private const int WriteAttempts = 10;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

        private readonly object gate = new();
        private readonly string directory;
        private readonly long maxSize;
        private readonly int maxGenerations;
        private readonly Logger? logger;

        internal NetLogWriter(NetLogFileConfig config, Logger? logger = null)
        {
            directory = ResolveDirectory(config.Directory);
            var sizeMB = config.MaxSizeMB;
            if (sizeMB <= 0)
            {
                logger?.Log($"NetLogWriter: MaxSizeMB of {sizeMB} is not usable, " +
                    $"rotating at {NetLogFileConfig.DefaultMaxSizeMB}MB instead");
                sizeMB = NetLogFileConfig.DefaultMaxSizeMB;
            }
            maxSize = sizeMB * 1024L * 1024L;
            maxGenerations = Math.Max(0, config.MaxGenerations);
            this.logger = logger;
        }

        // The entries kept for a collector that would not take them live here
        // too, so both are configured and permitted in one place.
        internal static string ResolveDirectory(string configured) =>
            string.IsNullOrWhiteSpace(configured) ? DefaultDirectory() : configured;

        internal static string DefaultDirectory() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "BrowserGuard",
                "netlog");

        internal string FilePath => GenerationPath(0);

        // Takes an entry already checked and put on one line by NetLogEntry.
        // null when it was written, otherwise why it was not.
        internal string? Write(string line)
        {
            lock (gate)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(directory);
                }
                catch (Exception ex)
                {
                    logger?.Log($"NetLogWriter: {ex.Message}");
                    return ex.Message;
                }

                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        RotateIfNeeded();
                        using var file = new FileStream(
                            FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                        using var writer = new StreamWriter(file, new UTF8Encoding(false));
                        writer.WriteLine(line);
                        return null;
                    }
                    catch (IOException) when (attempt < WriteAttempts)
                    {
                        // Something holds the file for a moment: a tool copying
                        // the log, or a virus scanner. Anything that opens it the
                        // ordinary way locks writers out while it reads. Waiting
                        // for it beats dropping an audit entry.
                        Thread.Sleep(RetryDelay);
                    }
                    catch (Exception ex)
                    {
                        logger?.Log($"NetLogWriter: {ex.Message}");
                        return ex.Message;
                    }
                }
            }
        }

        private void RotateIfNeeded()
        {
            var info = new FileInfo(FilePath);
            if (!info.Exists || info.Length <= maxSize)
            {
                return;
            }
            Rotate();
        }

        private void Rotate()
        {
            var oldest = GenerationPath(maxGenerations);
            if (File.Exists(oldest))
            {
                File.Delete(oldest);
            }

            for (var i = maxGenerations - 1; i >= 0; i--)
            {
                var from = GenerationPath(i);
                if (!File.Exists(from))
                {
                    continue;
                }
                File.Move(from, GenerationPath(i + 1), true);
            }
        }

        private string GenerationPath(int generation) =>
            Path.Combine(directory, generation == 0
                ? FileNameBase + FileExtension
                : $"{FileNameBase}_{generation}{FileExtension}");
    }
}
