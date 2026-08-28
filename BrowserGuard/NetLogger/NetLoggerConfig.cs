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
    //
    // An entry is written to a spool on disk first and sent afterwards, so
    // that spool holds what the collector has not taken yet and nothing else.
    // Where it sits is not a setting: it drains on its own and holds nothing
    // anyone has reason to go and read.
    internal class NetLogSenderConfig
    {
        public bool Enabled { get; set; }
        // Nothing is sent without one, so an enabled sender still needs it.
        public string Endpoint { get; set; } = "";
        // How long to wait after a round the collector refused. Below a minute
        // is read as a minute, so that a collector which is down cannot be
        // asked in a tight loop.
        public int RetryIntervalMinutes { get; set; } = 5;
        // Beyond this the waiting entries are dropped, so that a collector
        // which stays down cannot fill the disk. 0 lifts the limit.
        public int MaxSpoolSizeMB { get; set; } = 10;
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
