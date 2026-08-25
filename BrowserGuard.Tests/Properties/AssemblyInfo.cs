using System.Runtime.Versioning;

// Matches the assembly under test, which is Windows only. Without it every
// call into that assembly draws CA1416.
[assembly: SupportedOSPlatform("windows")]
