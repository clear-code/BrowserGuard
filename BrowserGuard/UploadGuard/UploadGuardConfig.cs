namespace BrowserGuard.UploadGuard
{
    // Controls which local files may be uploaded.
    // The blocked lists are checked first, so they win over the allowed ones.
    // An empty allowed list means "no restriction from this rule".
    internal class UploadGuardConfig
    {
        public bool Enabled { get; set; }
        public string[] BlockedExtensions { get; set; } = [".exe", ".bat", ".cmd", ".js", ".vbs"];
        public string[] AllowedExtensions { get; set; } = [];
        public string[] AllowedPaths { get; set; } = [];
        public string[] BlockedPaths { get; set; } = [];
    }
}
