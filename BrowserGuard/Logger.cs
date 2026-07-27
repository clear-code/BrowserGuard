using System;
using System.IO;

namespace BrowserGuard
{
    internal class Logger
    {
        private static readonly int MaxGeneration = 10;

        private static readonly long MaxLogSize = 10 * 1024 * 1024;

        private static readonly object LockObject = new object();

        private StreamWriter LogStream { get; set; }

        private string FilePath { get; set; } = "";

        private string LogFileNameBase { get; } = "BrowserGuard";

        private bool EnableLogging { get; }

        internal void Log(string message) => NoException(() => LogImpl(message));
        internal void Log(Exception e) => NoException(() => LogImpl(e));

        internal Logger()
        {
            EnableLogging = false;
            try
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var logDirectory = Path.Combine(appDataPath, "BrowserGuard");
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }
                FilePath = Path.Combine(logDirectory, $"{LogFileNameBase}.log");
                EnableLogging = true;
            }
            catch
            {
                // ログ出力できないが、全体の処理は続行する。
            }
        }

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
            lock (LockObject)
            {
                RotateIfNeed();
                LogStream.WriteLine($"{GetTimestamp()} : {message}");
                LogStream.Flush();
            }
        }

        private void LogImpl(Exception e)
        {
            if (!EnableLogging)
            {
                return;
            }
            LogImpl(e.ToString());
        }

        private string GetTimestamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private void RotateIfNeed()
        {
            if (!File.Exists(FilePath))
            {
                LogStream?.Close();
                LogStream = null;
            }

            if (LogStream is null)
            {
                var fileStream = new FileStream(FilePath, FileMode.OpenOrCreate);
                fileStream.Seek(0, SeekOrigin.End);
                LogStream = new StreamWriter(fileStream);
            }

            var fi = new FileInfo(FilePath);
            if (fi.Length > MaxLogSize)
            {
                Rotate();
            }
        }

        private void Rotate()
        {
            lock (LockObject)
            {
                LogStream?.Close();

                string previousFileName;
                string previousFilePath;
                string rotatedFileName;
                string rotatedFilePath;
                string userDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                for (int i = MaxGeneration - 1; i >= 0; i--)
                {
                    if (i > 0)
                    {
                        previousFileName = $"{LogFileNameBase}_{i}.log";
                    }
                    else
                    {
                        previousFileName = $"{LogFileNameBase}.log";
                    }

                    previousFilePath = Path.Combine(userDir, previousFileName);

                    if (!File.Exists(previousFilePath))
                    {
                        continue;
                    }
                    rotatedFileName = $"{LogFileNameBase}_{i + 1}.log";
                    rotatedFilePath = Path.Combine(userDir, rotatedFileName);

                    File.Copy(previousFilePath, rotatedFilePath, true);
                    File.Delete(previousFilePath);
                }

                LogStream = new StreamWriter(new FileStream(FilePath, FileMode.Create));
            }
        }
    }
}
