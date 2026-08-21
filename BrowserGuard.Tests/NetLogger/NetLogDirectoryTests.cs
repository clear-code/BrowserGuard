using System;
using System.IO;
using Xunit;
using BrowserGuard.NetLogger;

namespace BrowserGuard.Tests.NetLogger
{
    public class NetLogDirectoryTests
    {
        // A fixed day, so a directory naming the day is the one intended here.
        static readonly DateTime Now = new(2026, 8, 20, 13, 45, 30);

        const string Root = @"\\fileserver\netlog";

        // The same macros the copies of uploaded files are filed under, so one
        // machine's log can be told from another's on a shared drive.
        [Fact]
        public void ExpandsTheMacros()
        {
            var resolved = NetLogDirectory.Resolve(Path.Combine(Root, "%PCNAME%", "%DATE%"), Now);

            Assert.Equal(
                Path.Combine(Root, Environment.MachineName, "2026-08-20"),
                resolved);
        }

        [Fact]
        public void ExpandsAWindowsEnvironmentVariable()
        {
            var before = Environment.GetEnvironmentVariable("BROWSERGUARD_LOGS");
            Environment.SetEnvironmentVariable("BROWSERGUARD_LOGS", Root);
            try
            {
                Assert.Equal(
                    Path.Combine(Root, "netlog"),
                    NetLogDirectory.Resolve(Path.Combine("%BROWSERGUARD_LOGS%", "netlog")));
            }
            finally
            {
                Environment.SetEnvironmentVariable("BROWSERGUARD_LOGS", before);
            }
        }

        [Fact]
        public void TakesAPlainPathAsItStands()
        {
            Assert.Equal(Root, NetLogDirectory.Resolve(Root, Now));
        }

        [Fact]
        public void FallsBackToProgramDataWhenNothingIsConfigured()
        {
            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "BrowserGuard", "netlog");

            Assert.Equal(expected, NetLogDirectory.Resolve("", Now));
            Assert.Equal(expected, NetLogDirectory.Resolve("   ", Now));
            Assert.Equal(expected, NetLogDirectory.Default());
        }
    }
}
