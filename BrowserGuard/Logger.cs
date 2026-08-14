using System;
using System.IO;
using System.Threading;

namespace BrowserGuard
{
    internal class Logger
    {
        private const int MaxGeneration = 10;

        private const long DefaultMaxLogSize = 10 * 1024 * 1024;

        private const string LogFileNameBase = "BrowserGuard";

        // The browser starts a host process per message, and a long lived one
        // once a port is opened, so several of them write here at the same time.
        // The file is therefore opened only for the moment of the write, and
        // this mutex keeps a rotation from happening underneath another writer.
        private static readonly Mutex FileMutex = new(false, @"Local\BrowserGuard.Logger");

        private static readonly TimeSpan MutexTimeout = TimeSpan.FromSeconds(5);

        private readonly long maxLogSize;

        private string FilePath { get; } = "";

        private string LogDirectory { get; } = "";

        private bool EnableLogging { get; }

        internal void Log(string message) => NoException(() => LogImpl(message));
        internal void Log(Exception e) => NoException(() => LogImpl(e));

        internal Logger() : this(DefaultDirectory()) { }

        // The directory and the size are arguments so that the rotation can be
        // exercised without writing to the real log.
        internal Logger(string directory, long maxLogSize = DefaultMaxLogSize)
        {
            this.maxLogSize = maxLogSize;
            EnableLogging = false;
            try
            {
                System.IO.Directory.CreateDirectory(directory);
                LogDirectory = directory;
                FilePath = Path.Combine(directory, $"{LogFileNameBase}.log");
                EnableLogging = true;
            }
            catch
            {
                // ログ出力できないが、全体の処理は続行する。
            }
        }

        private static string DefaultDirectory() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BrowserGuard");

        private void NoException(Action func)
        {
            try { func(); } catch { }
        }

        private void LogImpl(string message)
        {
            if (!EnableLogging)
            {
                return;
            }
            Write($"{GetTimestamp()} : {message}");
        }

        private void LogImpl(Exception e)
        {
            if (!EnableLogging)
            {
                return;
            }
            LogImpl(e.ToString());
        }

        private void Write(string line)
        {
            var held = false;
            try
            {
                try
                {
                    held = FileMutex.WaitOne(MutexTimeout);
                }
                catch (AbandonedMutexException)
                {
                    // A host process died holding it; the file itself is fine.
                    held = true;
                }

                RotateIfNeeded();
                // Sharing the file is what lets the other host processes keep
                // logging while a port is open.
                using var stream = new FileStream(
                    FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream);
                writer.WriteLine(line);
            }
            finally
            {
                if (held)
                {
                    FileMutex.ReleaseMutex();
                }
            }
        }

        private string GetTimestamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private void RotateIfNeeded()
        {
            var info = new FileInfo(FilePath);
            if (info.Exists && info.Length > maxLogSize)
            {
                Rotate();
            }
        }

        // The generations live beside the log itself. Reading them from
        // somewhere else would find nothing to move and then truncate the log.
        private void Rotate()
        {
            var oldest = GenerationPath(MaxGeneration);
            if (File.Exists(oldest))
            {
                File.Delete(oldest);
            }

            for (var i = MaxGeneration - 1; i >= 0; i--)
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
            Path.Combine(LogDirectory, generation == 0
                ? $"{LogFileNameBase}.log"
                : $"{LogFileNameBase}_{generation}.log");
    }
}
