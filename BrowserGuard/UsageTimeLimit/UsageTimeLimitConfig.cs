namespace BrowserGuard.UsageTimeLimit
{
    internal class UsageTimeLimitConfig
    {
        public bool Enabled { get; set; }
        public int MaxContinuousMinutes { get; set; }
        public TimeRangeConfig[] AllowedTimeRanges { get; set; } = [];
        public UsageTimeExceededConfig OnExceeded { get; set; } = new();
    }

    // "HH:mm" in local time. A range whose End is not after its Start runs
    // past midnight, so { "22:00", "02:00" } is a five hour window.
    internal class TimeRangeConfig
    {
        public string Start { get; set; } = "";
        public string End { get; set; } = "";
    }

    internal class UsageTimeExceededConfig
    {
        // "WarnOnly" or "Terminate". Anything else is read as "WarnOnly", so a
        // misspelling cannot silently start closing the browser.
        public string Action { get; set; } = "WarnOnly";
        public int GraceSeconds { get; set; } = 60;
        public int ReWarnIntervalMinutes { get; set; } = 10;
    }
}
