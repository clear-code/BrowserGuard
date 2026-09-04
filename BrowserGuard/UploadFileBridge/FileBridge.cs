using System;
using System.IO;
using System.Text.RegularExpressions;
using BrowserGuard.Common;

namespace BrowserGuard.UploadFileBridge
{
    // Keeps a copy of a file the browser uploaded, as evidence of what left the
    // machine. The browser only knows the path of the file it sent, so the copy
    // is made here.
    internal static class FileBridge
    {
        // Beyond this something is wrong with the destination rather than with
        // the name, so it stops rather than counting for ever.
        private const int MaxNumbered = 1000;

        private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

        // null when the copy was made, or when there was nothing to do.
        // Otherwise why it could not be made.
        internal static string? Copy(
            UploadFileBridgeConfig config,
            string source,
            string url,
            DateTime now,
            Logger? logger = null)
        {
            var failure = Attempt(config, source, url, now, logger);
            if (failure is not null)
            {
                logger?.Log($"UploadFileBridge: no copy kept of {source}: {failure}");
            }
            return failure;
        }

        private static string? Attempt(
            UploadFileBridgeConfig config,
            string source,
            string url,
            DateTime now,
            Logger? logger)
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

            // Where it was going and what it is called are settled first: they
            // are a look at two strings, where the size is a look at the file.
            var refusal = AllowedUrl(config, url, logger)
                ?? AllowedExtension(config, source)
                ?? WithinSizeLimit(config, source);
            if (refusal is not null)
            {
                return refusal;
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

        // Read like the extension lists, and matched as regular expressions the
        // way UploadGuard matches its paths.
        private static string? AllowedUrl(
            UploadFileBridgeConfig config, string url, Logger? logger)
        {
            if (Matches(url, config.BlockedUrls, "blocked URL", logger))
            {
                return $"uploads to {url} are not kept";
            }
            if (config.AllowedUrls.Length > 0 && !Matches(url, config.AllowedUrls, "allowed URL", logger))
            {
                return $"uploads to {url} are not among those kept";
            }
            return null;
        }

        // An unusable pattern is dropped on its own, so one bad entry does not
        // silently turn the whole list into "match everything" or "match
        // nothing". The timeout is there so that a pattern that backtracks for
        // ever cannot hold the host up.
        private static bool Matches(string url, string[] patterns, string list, Logger? logger)
        {
            foreach (var pattern in patterns)
            {
                try
                {
                    if (Regex.IsMatch(url, pattern, RegexOptions.IgnoreCase, MatchTimeout))
                    {
                        logger?.Log($"UploadFileBridge: {url} matched the {list} pattern {pattern}");
                        return true;
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
                {
                    logger?.Log($"UploadFileBridge: ignoring the {list} pattern {pattern}: {ex.Message}");
                }
            }
            return false;
        }

        // The blocked list is checked first, so it wins over the allowed one.
        // An empty allowed list means "no restriction from this rule", the way
        // UploadGuard reads its own lists.
        private static string? AllowedExtension(UploadFileBridgeConfig config, string source)
        {
            if (HasExtension(source, config.BlockedExtensions))
            {
                return $"{source} has an extension that is not kept";
            }
            if (config.AllowedExtensions.Length > 0 &&
                !HasExtension(source, config.AllowedExtensions))
            {
                return $"{source} does not have an extension that is kept";
            }
            return null;
        }

        // Local names are case insensitive on Windows.
        private static bool HasExtension(string file, string[] extensions) =>
            extensions.Any(extension =>
                file.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

        // null when the file may be copied. Otherwise why it may not, in the
        // same shape as any other reason the copy was not made: an audit trail
        // with a gap in it has to say what is missing and why.
        private static string? WithinSizeLimit(UploadFileBridgeConfig config, string source)
        {
            var limit = Math.Max(0, config.MaxSizeMB) * 1024L * 1024L;
            if (limit == 0)
            {
                return null;
            }

            long length;
            try
            {
                length = new FileInfo(source).Length;
            }
            catch (Exception ex)
            {
                return $"cannot measure {source}: {ex.Message}";
            }
            if (length <= limit)
            {
                return null;
            }
            return $"{source} is {length} bytes, over the {config.MaxSizeMB} MB limit";
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
