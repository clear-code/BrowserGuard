namespace BrowserGuard.Startup
{
    // Programs started when the browser launches.
    internal class StartupLauncherConfig
    {
        public bool Enabled { get; set; }
        public StartupProgramConfig[] Programs { get; set; } = [];
    }

    internal class StartupProgramConfig
    {
        // Taken as it stands. Deliberately not put through PathMacro the way
        // the log and the copied uploads are: the environment is inherited from
        // the browser, so a variable in the path is one the logged-on user can
        // set, and this is the one path that decides what gets run. Sha256 is
        // empty by default, so nothing else would notice the swap.
        public string Path { get; set; } = "";
        public string[] Arguments { get; set; } = [];
        public string WorkingDirectory { get; set; } = "";
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
        public string Sha256 { get; set; } = "";
    }
}
