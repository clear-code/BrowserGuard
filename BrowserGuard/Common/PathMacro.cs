using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BrowserGuard.Common
{
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

            return Environment.ExpandEnvironmentVariables(expanded);
        }

        // The machine and the user are the ones the audit log records, so a
        // folder named after them lines up with the entries in it.
        private static string? ResolveOriginalMacro(string name, DateTime now) =>
            name.ToUpperInvariant() switch
            {
                "PCNAME" => Environment.MachineName,
                "USERID" => Environment.UserName,
                "DATE" => now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "YYYY" => now.ToString("yyyy", CultureInfo.InvariantCulture),
                "MM" => now.ToString("MM", CultureInfo.InvariantCulture),
                "DD" => now.ToString("dd", CultureInfo.InvariantCulture),
                _ => null,
            };
    }
}
