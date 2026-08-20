namespace BrowserGuard.UploadFileBridge
{
    // Keeps a copy of a file that was uploaded, as evidence of what left the
    // machine. The copy goes to a folder that is expected to be on a file
    // server rather than on the machine itself.
    internal class UploadFileBridgeConfig
    {
        public bool Enabled { get; set; }
        // Where the copies go. The macros PathMacro knows are expanded, as are
        // Windows environment variables, so one setting can give every machine,
        // user or day a folder of its own.
        // Empty means there is nowhere to put them, so nothing is copied.
        public string Destination { get; set; } = "";
        // A file larger than this is left uncopied, so that one very large
        // upload cannot fill the file server. 0 copies whatever it is given.
        public int MaxSizeMB { get; set; }
    }
}
