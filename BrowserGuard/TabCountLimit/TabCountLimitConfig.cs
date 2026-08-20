namespace BrowserGuard.TabCountLimit
{
    // How many tabs may be open at once. The tabs are counted across every
    // window, so the limit cannot be worked around by opening another one.
    internal class TabCountLimitConfig
    {
        public bool Enabled { get; set; }
        // 0 means no limit, so a config that leaves the number out cannot start
        // warning about every tab.
        public int MaxCount { get; set; }
    }
}
