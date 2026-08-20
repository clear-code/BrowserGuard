namespace BrowserGuard.SettingPageFilter
{
    internal class SettingPageFilterConfig
    {
        public bool Enabled { get; set; }
        public bool NotifyOnBlocked { get; set; }
        public string[] BlockedPrefixes { get; set; } = [
                "edge://settings",
                "edge://flags",
                "edge://policy"
            ];
    }
}
