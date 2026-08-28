using BrowserGuard.Common;

namespace BrowserGuard.NetLogger
{
    internal static class NetLogDirectory
    {
        // Where the log is kept, which is a setting because someone has to be
        // able to point it at a share or a folder of their choosing.
        internal static string ForLog(string configured, DateTime? now = null) =>
            string.IsNullOrWhiteSpace(configured)
                ? Under("netlog")
                : PathMacro.Expand(configured, now ?? DateTime.Now);

        // Where the entries wait for the collector, which is not a setting: the
        // spool drains on its own and holds nothing anyone has reason to read.
        // Apart from the log all the same, the two having different lifetimes.
        internal static string ForSpool() => Under("spool");

        // Per user rather than per machine, so that the several people sharing
        // one host under AVD do not write to a single file, and cannot read
        // each other's. The collector, not this copy, is the record of account.
        // Local rather than roaming: a month of entries is far too much to
        // carry over the network, which is why this parts from Logger.
        private static string Under(string leaf) =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BrowserGuard",
                leaf);
    }
}
