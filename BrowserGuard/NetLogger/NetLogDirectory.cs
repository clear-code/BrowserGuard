using BrowserGuard.Common;

namespace BrowserGuard.NetLogger
{
    internal static class NetLogDirectory
    {
        internal static string Resolve(string configured, DateTime? now = null) =>
            string.IsNullOrWhiteSpace(configured)
                ? Default()
                : PathMacro.Expand(configured, now ?? DateTime.Now);

        // Per user rather than per machine, so that the several people sharing
        // one host under AVD do not write to a single file, and cannot read
        // each other's. The collector, not this copy, is the record of account.
        // Local rather than roaming: a month of entries is far too much to
        // carry over the network, which is why this parts from Logger.
        internal static string Default() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BrowserGuard",
                "netlog");
    }
}
