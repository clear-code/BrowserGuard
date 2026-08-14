using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
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

        // The log is read back as text, so the escaping is relaxed to leave
        // Japanese page titles legible rather than as \uXXXX escapes.
        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private readonly object gate = new();
        private readonly string directory;
        private readonly long maxSize;
        private readonly int maxGenerations;
        private readonly Logger? logger;

        internal NetLogWriter(NetLogFileConfig config, Logger? logger = null)
        {
            directory = string.IsNullOrWhiteSpace(config.Directory)
                ? DefaultDirectory()
                : config.Directory;
            maxSize = Math.Max(1, config.MaxSizeMB) * 1024L * 1024L;
            maxGenerations = Math.Max(0, config.MaxGenerations);
            this.logger = logger;
        }

        internal static string DefaultDirectory() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "BrowserGuard",
                "netlog");

        internal string FilePath => GenerationPath(0);

        // null when the entry was written, otherwise why it was not.
        internal string? Write(string entry)
        {
            string line;
            try
            {
                using var document = JsonDocument.Parse(entry);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return "entry is not a JSON object";
                }
                // Reserializing puts the entry on a single line whatever the
                // sender did with its whitespace.
                line = JsonSerializer.Serialize(document.RootElement, WriteOptions);
            }
            catch (JsonException ex)
            {
                return $"entry is not valid JSON: {ex.Message}";
            }

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
