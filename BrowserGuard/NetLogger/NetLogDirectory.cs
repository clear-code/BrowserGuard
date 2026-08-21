using BrowserGuard.Common;

namespace BrowserGuard.NetLogger
{
    internal static class NetLogDirectory
    {
        internal static string Resolve(string configured, DateTime? now = null) =>
            string.IsNullOrWhiteSpace(configured)
                ? Default()
                : PathMacro.Expand(configured, now ?? DateTime.Now);

        internal static string Default() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "BrowserGuard",
                "netlog");
    }
}
