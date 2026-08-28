using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using BrowserGuard.Common;

namespace BrowserGuard.NetLogger
{
    // Appends the browsing log, one JSON object per line.
    //
    // The day's entries go to netlog.jsonl. At the turn of the day that file is
    // put aside as netlog_YYYY-MM-DD.jsonl, so a day's browsing is one file and
    // a retention period is a number of days rather than a guess at a size.
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
    internal sealed class NetLogFileWriter
    {
        internal const string FileNameBase = "netlog";
        internal const string FileExtension = ".jsonl";
        private const string DayFormat = "yyyy-MM-dd";

        private const int WriteAttempts = 10;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

        private readonly object gate = new();
        private readonly string directory;
        // Days a rotated file is kept for, or 0 to keep them all.
        private readonly int maxDays;
        // Bytes at which a day is split, or 0 to leave it whole.
        private readonly long maxSize;
        private readonly Logger? logger;

        internal NetLogFileWriter(NetLogFileConfig config, Logger? logger = null)
        {
            directory = NetLogDirectory.ForLog(config.Directory);
            maxDays = Math.Max(0, config.MaxDays);
            maxSize = Math.Max(0, config.MaxSizeMB) * 1024L * 1024L;
            this.logger = logger;
        }

        internal string FilePath => Path.Combine(directory, FileNameBase + FileExtension);

        // A day is one file unless its size forced a split, in which case the
        // segments after the first carry a number. They are named so that
        // sorting them puts the day's entries back in the order they happened.
        internal string PathForDay(DateTime day, int segment = 1)
        {
            var stamp = day.ToString(DayFormat, CultureInfo.InvariantCulture);
            return Path.Combine(directory, segment <= 1
                ? $"{FileNameBase}_{stamp}{FileExtension}"
                : $"{FileNameBase}_{stamp}_{segment}{FileExtension}");
        }

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
                    logger?.Log($"NetLogFileWriter: {ex.Message}");
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
                        logger?.Log($"NetLogFileWriter: {ex.Message}");
                        return ex.Message;
                    }
                }
            }
        }

        // The day the entries belong to is taken from the file itself, so a host
        // that was not running at midnight still rotates on its next entry.
        private void RotateIfNeeded()
        {
            var info = new FileInfo(FilePath);
            if (!info.Exists || info.Length == 0)
            {
                return;
            }

            var written = info.LastWriteTime.Date;
            // The date decides first: a day always starts a file of its own,
            // whatever the size limit would have done.
            if (written >= DateTime.Now.Date &&
                (maxSize <= 0 || info.Length < maxSize))
            {
                return;
            }

            Rotate(written);
            Purge();
        }

        private void Rotate(DateTime day)
        {
            // A file for the day can already be there, left by an earlier split
            // of the same day. Appending to it would put the day back over the
            // size that split it, so the next segment is started instead.
            var path = PathForDay(day);
            for (var segment = 2; File.Exists(path); segment++)
            {
                path = PathForDay(day, segment);
            }
            File.Move(FilePath, path);
        }

        private void Purge()
        {
            if (maxDays <= 0)
            {
                return;
            }
            var oldest = DateTime.Now.Date.AddDays(-maxDays);
            foreach (var path in System.IO.Directory.GetFiles(
                directory, $"{FileNameBase}_*{FileExtension}"))
            {
                if (!TryReadDay(path, out var day) || day >= oldest)
                {
                    continue;
                }
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                    logger?.Log($"NetLogFileWriter: {ex.Message}");
                }
            }
        }

        // Anything in the directory that is not one of ours is left alone.
        private static bool TryReadDay(string path, out DateTime day)
        {
            day = default;
            var name = Path.GetFileNameWithoutExtension(path);
            var prefix = FileNameBase + "_";
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }
            // The segment number a split day carries is not part of the date.
            var stamp = name[prefix.Length..];
            var segment = stamp.IndexOf('_');
            if (segment >= 0)
            {
                stamp = stamp[..segment];
            }
            return DateTime.TryParseExact(
                stamp,
                DayFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out day);
        }
    }
}
