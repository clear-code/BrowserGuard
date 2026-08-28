using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BrowserGuard.Common;

namespace BrowserGuard.NetLogger
{
    // Holds the entries the collector would not take.
    //
    // A round of retries moves the file aside rather than reading it in place,
    // so entries arriving while the round runs are not swept up with it. If the
    // host stops mid round the file that was moved aside is folded back in on
    // the next round: an entry may then be sent twice, which for an audit log is
    // the better of the two mistakes.
    internal sealed class NetLogSpool
    {
        internal const string FileName = "netlog-pending.jsonl";
        internal const string TakenFileName = "netlog-pending.taken.jsonl";

        private const int WriteAttempts = 10;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

        private readonly object gate = new();
        private readonly string directory;
        // Bytes, or 0 for no limit.
        private readonly long maxSize;
        private readonly Logger? logger;

        private bool reportedFull;

        internal NetLogSpool(string directory, long maxSize, Logger? logger = null)
        {
            this.directory = directory;
            this.maxSize = maxSize;
            this.logger = logger;
        }

        internal string FilePath => Path.Combine(directory, FileName);

        private string TakenPath => Path.Combine(directory, TakenFileName);

        internal bool Add(string line) => AddRange(new[] { line });

        internal bool AddRange(IEnumerable<string> lines)
        {
            var pending = lines.ToArray();
            if (pending.Length == 0)
            {
                return true;
            }

            lock (gate)
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    if (IsFull())
                    {
                        // Only the first is reported, or the diagnostic log
                        // becomes the thing filling the disk.
                        if (!reportedFull)
                        {
                            reportedFull = true;
                            logger?.Log($"NetLogSpool: {FilePath} is full, dropping entries");
                        }
                        return false;
                    }
                    reportedFull = false;
                    Append(FilePath, pending);
                    return true;
                }
                catch (Exception ex)
                {
                    logger?.Log($"NetLogSpool: {ex.Message}");
                    return false;
                }
            }
        }

        // The kept entries, taken out of the file so the caller owns them.
        // Whatever it could not send goes back through AddRange, and Settle
        // closes the round.
        internal List<string> Take()
        {
            lock (gate)
            {
                try
                {
                    Recover();
                    if (!File.Exists(FilePath))
                    {
                        return new List<string>();
                    }
                    File.Move(FilePath, TakenPath, true);
                    return File.ReadAllLines(TakenPath)
                        .Where(line => line.Length > 0)
                        .ToList();
                }
                catch (Exception ex)
                {
                    logger?.Log($"NetLogSpool: {ex.Message}");
                    return new List<string>();
                }
            }
        }

        internal void Settle()
        {
            lock (gate)
            {
                try
                {
                    if (File.Exists(TakenPath))
                    {
                        File.Delete(TakenPath);
                    }
                }
                catch (Exception ex)
                {
                    logger?.Log($"NetLogSpool: {ex.Message}");
                }
            }
        }

        // A round that never finished left its entries behind.
        private void Recover()
        {
            if (!File.Exists(TakenPath))
            {
                return;
            }
            var left = File.ReadAllLines(TakenPath).Where(line => line.Length > 0).ToArray();
            File.Delete(TakenPath);
            if (left.Length > 0)
            {
                logger?.Log($"NetLogSpool: recovered {left.Length} entries from an unfinished round");
                Append(FilePath, left);
            }
        }

        // A cap of zero means there is none: the entries are kept however many
        // of them there are.
        private bool IsFull()
        {
            if (maxSize <= 0)
            {
                return false;
            }
            var info = new FileInfo(FilePath);
            return info.Exists && info.Length >= maxSize;
        }

        // Shared with anything reading the file, the same way the log itself is.
        private static void Append(string path, IEnumerable<string> lines)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    using var file = new FileStream(
                        path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var writer = new StreamWriter(file, new UTF8Encoding(false));
                    foreach (var line in lines)
                    {
                        writer.WriteLine(line);
                    }
                    return;
                }
                catch (IOException) when (attempt < WriteAttempts)
                {
                    Thread.Sleep(RetryDelay);
                }
            }
        }
    }
}
