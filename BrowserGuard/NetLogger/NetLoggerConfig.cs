namespace BrowserGuard.NetLogger
{
    internal class NetLoggerConfig
    {
        public bool Enabled { get; set; }
        public bool UrlAccess { get; set; }
        public bool Browsing { get; set; }
        public bool Upload { get; set; }
        public bool Download { get; set; }
        public bool Print { get; set; }

        public NetLogFileConfig LocalFile { get; set; } = new();
        public NetLogSenderConfig Sender { get; set; } = new();
    }

    // Handing the log to the collector. The counterpart of NetLogFileConfig:
    // one says where the log goes on this machine, the other where it goes off it.
    internal class NetLogSenderConfig
    {
        public bool Enabled { get; set; }
        // Nothing is sent without one, so an enabled sender still needs it.
        public string Endpoint { get; set; } = "";
        public NetLogSpoolConfig Spool { get; set; } = new();
    }

    // Holding the entries the collector would not take, including one dropped
    // because the queue waiting for the collector was full. Kept beside the
    // local log, in netlog-pending.jsonl.
    //
    // Disabled, an entry that cannot be sent is lost where it falls, and Retry
    // below has nothing left to offer again.
    internal class NetLogSpoolConfig
    {
        public bool Enabled { get; set; }
        // Beyond this the kept entries are dropped, so that a collector that
        // stays down cannot fill the disk. 0 keeps them without any limit.
        public int MaxSizeMB { get; set; } = 10;

        public NetLogRetryConfig Retry { get; set; } = new();
    }

    // Offering the kept entries to the collector again. On unless it is asked
    // for: entries are kept in order to be sent, so turning this off is the
    // unusual choice, and means they are to be collected by hand.
    internal class NetLogRetryConfig
    {
        public bool Enabled { get; set; } = true;
        public int IntervalMinutes { get; set; } = 5;
    }

    // Keeping the log on this machine, as one JSON object per line.
    internal class NetLogFileConfig
    {
        public bool Enabled { get; set; }
        // The macros PathMacro knows are expanded, as are Windows environment
        // variables. Empty means %LOCALAPPDATA%\BrowserGuard\netlog.
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
