using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BrowserGuard
{
    // A destination folder is written with macros, so that one setting can
    // spread what it writes over a folder per machine, per user or per day
    // without anything having to be configured per machine.
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
            // A name that is not one of the macros is left as it stands. A
            // folder with a literal %FOO% in it is odd enough to be noticed,
            // where dropping it would quietly put everyone in the same folder.
            return Pattern.Replace(text, match =>
                Resolve(match.Groups[1].Value, now) ?? match.Value);
        }

        // The machine and the user are the ones the audit log records, so a
        // folder named after them lines up with the entries in it.
        private static string? Resolve(string name, DateTime now) =>
            name.ToUpperInvariant() switch
            {
                "PCNAME" => NetLogEntry.MachineName,
                "USERID" => NetLogEntry.UserName,
                "DATE" => now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "YYYY" => now.ToString("yyyy", CultureInfo.InvariantCulture),
                "MM" => now.ToString("MM", CultureInfo.InvariantCulture),
                "DD" => now.ToString("dd", CultureInfo.InvariantCulture),
                _ => null,
            };
    }
}
