using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BrowserGuard.Common
{
    // Expands a path written with macros: the ones listed below, and Windows
    // environment variables, which are written the same way. One setting can
    // then give every machine, user or day a folder of its own.
    internal static class PathMacro
    {
        // %NAME%, the shape Windows paths already use.
        private static readonly Regex Pattern = new(@"%(\w+)%", RegexOptions.Compiled);

        internal static string Expand(string text, DateTime now)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            // A name that is not one of the macros is left as it stands.
            var expanded = Pattern.Replace(text, match =>
                ResolveOriginalMacro(match.Groups[1].Value, now) ?? match.Value);

            // The macros go first, so that a variable holding something that
            // reads like one is not taken for it. Windows leaves a variable it
            // does not know as it stands, which is what the macros do too.
            return Environment.ExpandEnvironmentVariables(expanded);
        }

        // The machine and the user are the ones the audit log records, so a
        // folder named after them lines up with the entries in it.
        //
        // %COMPUTERNAME% and %USERNAME% hold the same two names, which is why
        // these were dropped once as redundant. They are not: an environment
        // variable is the user's to set, so anyone could have filed their own
        // evidence under another name, or somewhere else entirely. These read
        // the token and the OS instead, and cannot be talked out of either.
        private static string? ResolveOriginalMacro(string name, DateTime now) =>
            name.ToUpperInvariant() switch
            {
                "MACHINENAME" => Environment.MachineName,
                "USER" => Environment.UserName,
                "DATE" => now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "YYYY" => now.ToString("yyyy", CultureInfo.InvariantCulture),
                "MM" => now.ToString("MM", CultureInfo.InvariantCulture),
                "DD" => now.ToString("dd", CultureInfo.InvariantCulture),
                _ => null,
            };
    }
}
