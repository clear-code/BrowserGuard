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
        public string Path { get; set; } = "";
        public string[] Arguments { get; set; } = [];
        public string WorkingDirectory { get; set; } = "";
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
        public string Sha256 { get; set; } = "";
    }
}
