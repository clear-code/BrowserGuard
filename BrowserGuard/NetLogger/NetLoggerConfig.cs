namespace BrowserGuard.NetLogger
{
    internal class NetLoggerConfig
    {
        public bool Enabled { get; set; }
        public string Endpoint { get; set; } = "";
        public bool UrlAccess { get; set; }
        public bool Browsing { get; set; }
        public bool Upload { get; set; }
        public bool Download { get; set; }
        public bool Print { get; set; }

        public NetLogFileConfig LocalFile { get; set; } = new();
        public NetLogFailureConfig OnSendFailure { get; set; } = new();
    }

    // What becomes of an entry the collector would not take, including one
    // dropped because the queue waiting for the collector was full.
    internal class NetLogFailureConfig
    {
        // Kept beside the local log, in netlog-pending.jsonl.
        public bool SaveLocally { get; set; }
        // How often the kept entries are offered to the collector again.
        // 0 keeps them without ever retrying, for collection by hand.
        public int RetryIntervalMinutes { get; set; } = 5;
        // Beyond this the kept entries are dropped, so that a collector that
        // stays down cannot fill the disk. 0 keeps them without any limit.
        public int MaxSizeMB { get; set; } = 10;
    }

    // Keeping the log on this machine, as one JSON object per line.
    internal class NetLogFileConfig
    {
        public bool Enabled { get; set; }
        // The macros PathMacro knows are expanded, as are Windows environment
        // variables. Empty means %ProgramData%\BrowserGuard\netlog.
        public string Directory { get; set; } = "";
        // The log is rotated at the turn of the day, and a day's file is kept
        // for this many days. 0 keeps every day for good.
        public int MaxDays { get; set; } = 30;
        // A day that grows past this is split, so that one busy day cannot
        // produce a file too large to handle. 0 leaves the day whole however
        // large it gets.
        public int MaxSizeMB { get; set; }
    }
}
