using System;
using System.IO;

namespace BrowserGuard
{
    // Keeps a copy of a file the browser uploaded, as evidence of what left the
    // machine. The browser only knows the path of the file it sent, so the copy
    // is made here.
    internal static class FileBridge
    {
        // Beyond this something is wrong with the destination rather than with
        // the name, so it stops rather than counting for ever.
        private const int MaxNumbered = 1000;

        // null when the copy was made, or when there was nothing to do.
        // Otherwise why it could not be made.
        internal static string? Copy(
            UploadFileBridgeConfig config, string source, DateTime now, Logger? logger = null)
        {
            if (!config.Enabled)
            {
                return null;
            }
            if (string.IsNullOrWhiteSpace(config.Destination))
            {
                return "no destination is configured";
            }
            if (string.IsNullOrWhiteSpace(source))
            {
                return "no file to copy";
            }

            // Expanded now rather than when the config was read, because a
            // destination naming the day changes at midnight.
            var destination = PathMacro.Expand(config.Destination, now);
            try
            {
                Directory.CreateDirectory(destination);
            }
            catch (Exception ex)
            {
                return $"cannot create {destination}: {ex.Message}";
            }

            try
            {
                var copied = CopyWithoutOverwriting(source, destination);
                logger?.Log($"UploadFileBridge: copied {source} to {copied}");
                return null;
            }
            catch (Exception ex)
            {
                return $"cannot copy {source} to {destination}: {ex.Message}";
            }
        }

        // A file that is already there is numbered: "report.xlsx", then
        // "report_2.xlsx". The copy refuses to overwrite, so a name taken
        // between the check and the copy is simply passed over.
        private static string CopyWithoutOverwriting(string source, string destination)
        {
            var name = Path.GetFileNameWithoutExtension(source);
            var extension = Path.GetExtension(source);

            for (var number = 1; number <= MaxNumbered; number++)
            {
                var candidate = Path.Combine(
                    destination,
                    number == 1 ? $"{name}{extension}" : $"{name}_{number}{extension}");
                if (File.Exists(candidate))
                {
                    continue;
                }
                try
                {
                    File.Copy(source, candidate, overwrite: false);
                    return candidate;
                }
                catch (IOException) when (File.Exists(candidate))
                {
                    // Taken since it was looked at, so the next number is tried.
                }
            }
            throw new IOException(
                $"{name}{extension} is already there {MaxNumbered} times over");
        }
    }
}
