using Xunit;
using BrowserGuard.Configuration;

namespace BrowserGuard.Tests.Configuration
{
    public class ConfigLoaderTests
    {
        [Fact]
        public void ParsesNestedFeatureGroups()
        {
            var json = """
            {
              "NetLogger": {
                "Enabled": false,
                "Endpoint": "https://example.com/api/log",
                "UrlAccess": true,
                "Browsing": true,
                "Upload": true,
                "Download": false,
                "Print": true
              },
              "UploadGuard": {
                "Enabled": false,
                "BlockedExtensions": [".exe", ".zip"]
              },
              "SettingPageFilter": {
                "Enabled": true,
                "BlockedPrefixes": ["edge://settings/", "edge://flags/"]
              }
            }
            """;

            var config = ConfigLoader.ParseConf(json);

            Assert.Equal("https://example.com/api/log", config.NetLogger.Endpoint);
            Assert.True(config.NetLogger.UrlAccess);
            Assert.False(config.NetLogger.Download);

            Assert.False(config.UploadGuard.Enabled);
            Assert.Equal(new[] { ".exe", ".zip" }, config.UploadGuard.BlockedExtensions);

            Assert.True(config.SettingPageFilter.Enabled);
            Assert.Equal(new[] { "edge://settings/", "edge://flags/" }, config.SettingPageFilter.BlockedPrefixes);
        }

        [Fact]
        public void ParsesUploadPathsAndExtensions()
        {
            var json = """
            {
              "UploadGuard": {
                "Enabled": true,
                "BlockedExtensions": [".exe"],
                "AllowedExtensions": [".pdf", ".docx"],
                "AllowedPaths": ["^C:\\\\Users\\\\[^\\\\]+\\\\Documents\\\\", "^D:\\\\Share\\\\"],
                "BlockedPaths": ["\\\\Confidential\\\\"]
              }
            }
            """;

            var config = ConfigLoader.ParseConf(json);

            Assert.True(config.UploadGuard.Enabled);
            Assert.Equal(new[] { ".exe" }, config.UploadGuard.BlockedExtensions);
            Assert.Equal(new[] { ".pdf", ".docx" }, config.UploadGuard.AllowedExtensions);
            Assert.Equal(
                new[] { @"^C:\\Users\\[^\\]+\\Documents\\", @"^D:\\Share\\" },
                config.UploadGuard.AllowedPaths);
            Assert.Equal(new[] { @"\\Confidential\\" }, config.UploadGuard.BlockedPaths);
        }

        [Fact]
        public void UploadPathListsDefaultToEmpty()
        {
            var config = ConfigLoader.ParseConf("""{ "UploadGuard": { "Enabled": true } }""");

            // Empty allowed lists mean "no restriction from that rule".
            Assert.Empty(config.UploadGuard.AllowedExtensions);
            Assert.Empty(config.UploadGuard.AllowedPaths);
            Assert.Empty(config.UploadGuard.BlockedPaths);
            Assert.Equal(new[] { ".exe", ".bat", ".cmd", ".js", ".vbs" }, config.UploadGuard.BlockedExtensions);
        }

        [Fact]
        public void ParsesStartupPrograms()
        {
            var json = """
            {
              "StartupLauncher": {
                "Enabled": true,
                "Programs": [
                  {
                    "Path": "C:\\Program Files\\Contoso\\agent.exe",
                    "Arguments": ["--mode", "kiosk"],
                    "WorkingDirectory": "C:\\Program Files\\Contoso",
                    "EnvironmentVariables": { "CONTOSO_PROFILE": "default", "LANG": "ja" },
                    "Sha256": "abc123"
                  },
                  {
                    "Path": "C:\\Tools\\notify.exe"
                  }
                ]
              }
            }
            """;

            var config = ConfigLoader.ParseConf(json);

            Assert.True(config.StartupLauncher.Enabled);
            Assert.Equal(2, config.StartupLauncher.Programs.Length);

            var first = config.StartupLauncher.Programs[0];
            Assert.Equal(@"C:\Program Files\Contoso\agent.exe", first.Path);
            Assert.Equal(new[] { "--mode", "kiosk" }, first.Arguments);
            Assert.Equal(@"C:\Program Files\Contoso", first.WorkingDirectory);
            Assert.Equal("default", first.EnvironmentVariables["CONTOSO_PROFILE"]);
            Assert.Equal("ja", first.EnvironmentVariables["LANG"]);
            Assert.Equal("abc123", first.Sha256);

            // Everything but the path may be left out.
            var second = config.StartupLauncher.Programs[1];
            Assert.Equal(@"C:\Tools\notify.exe", second.Path);
            Assert.Empty(second.Arguments);
            Assert.Equal("", second.WorkingDirectory);
            Assert.Empty(second.EnvironmentVariables);
            Assert.Equal("", second.Sha256);
        }

        [Fact]
        public void ReadsWhereTheLogIsKept()
        {
            var json = """
            {
              "NetLogger": {
                "Enabled": true,
                "Endpoint": "https://collector.example.com/log",
                "LocalFile": {
                  "Enabled": true,
                  "Directory": "D:\\logs",
                  "MaxDays": 90,
                  "MaxSizeMB": 250
                },
                "OnSendFailure": {
                  "SaveLocally": true,
                  "RetryIntervalMinutes": 15,
                  "MaxSizeMB": 50
                }
              }
            }
            """;

            var config = ConfigLoader.ParseConf(json).NetLogger;

            Assert.True(config.Enabled);
            Assert.True(config.LocalFile.Enabled);
            Assert.Equal(@"D:\logs", config.LocalFile.Directory);
            Assert.Equal(90, config.LocalFile.MaxDays);
            Assert.Equal(250, config.LocalFile.MaxSizeMB);
            Assert.True(config.OnSendFailure.SaveLocally);
            Assert.Equal(15, config.OnSendFailure.RetryIntervalMinutes);
            Assert.Equal(50, config.OnSendFailure.MaxSizeMB);
        }

        // 0 asks for no limit rather than for a limit of nothing.
        [Fact]
        public void ReadsAnUnlimitedSpool()
        {
            var config = ConfigLoader.ParseConf(
                """{"NetLogger":{"OnSendFailure":{"SaveLocally":true,"MaxSizeMB":0}}}""");

            Assert.Equal(0, config.NetLogger.OnSendFailure.MaxSizeMB);
        }

        // Nothing may be written to this machine unless it was asked for.
        [Fact]
        public void KeepsNothingLocallyByDefault()
        {
            var config = ConfigLoader.ParseConf("{}").NetLogger;

            Assert.False(config.Enabled);
            Assert.False(config.LocalFile.Enabled);
            Assert.False(config.OnSendFailure.SaveLocally);
            Assert.Equal("", config.LocalFile.Directory);
        }

        // The files that ship with the installer have to stay loadable.
        [Theory]
        [InlineData("Resources/BrowserGuard.json")]
        [InlineData("BrowserGuard/BrowserGuard.sample.json")]
        public void ParsesTheShippedConfigFiles(string relativePath)
        {
            var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var path = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"not found: {path}");

            var config = ConfigLoader.ParseConf(File.ReadAllText(path));

            // A member that failed to bind would come back as the default, so a
            // group is spot checked rather than only asserting no exception.
            Assert.NotNull(config.StartupLauncher.Programs);
            Assert.NotEmpty(config.SettingPageFilter.BlockedPrefixes);
            Assert.NotEqual(0, config.NetLogger.LocalFile.MaxDays);
            Assert.NotEqual(0, config.NetLogger.OnSendFailure.MaxSizeMB);
            Assert.NotNull(config.UsageTimeLimit.AllowedTimeRanges);
            Assert.NotEqual("", config.UsageTimeLimit.OnExceeded.Action);
        }

        [Fact]
        public void ReadsTheUsageTimeLimit()
        {
            var json = """
            {
              "UsageTimeLimit": {
                "Enabled": true,
                "MaxContinuousMinutes": 240,
                "AllowedTimeRanges": [
                  { "Start": "09:00", "End": "12:00" },
                  { "Start": "13:00", "End": "18:00" }
                ],
                "OnExceeded": {
                  "Action": "Terminate",
                  "GraceSeconds": 90,
                  "ReWarnIntervalMinutes": 5
                }
              }
            }
            """;

            var config = ConfigLoader.ParseConf(json);

            Assert.True(config.UsageTimeLimit.Enabled);
            Assert.Equal(240, config.UsageTimeLimit.MaxContinuousMinutes);
            Assert.Equal(2, config.UsageTimeLimit.AllowedTimeRanges.Length);
            Assert.Equal("09:00", config.UsageTimeLimit.AllowedTimeRanges[0].Start);
            Assert.Equal("18:00", config.UsageTimeLimit.AllowedTimeRanges[1].End);
            Assert.Equal("Terminate", config.UsageTimeLimit.OnExceeded.Action);
            Assert.Equal(90, config.UsageTimeLimit.OnExceeded.GraceSeconds);
            Assert.Equal(5, config.UsageTimeLimit.OnExceeded.ReWarnIntervalMinutes);
        }

        // Nothing may start closing the browser unless it was asked for.
        [Fact]
        public void UsageTimeLimitDefaultsToDisabledAndWarningOnly()
        {
            var config = ConfigLoader.ParseConf("{}");

            Assert.False(config.UsageTimeLimit.Enabled);
            Assert.Equal(0, config.UsageTimeLimit.MaxContinuousMinutes);
            Assert.Empty(config.UsageTimeLimit.AllowedTimeRanges);
            Assert.Equal("WarnOnly", config.UsageTimeLimit.OnExceeded.Action);
        }

        [Fact]
        public void ReadsTheTabCountLimit()
        {
            var json = """
            {
              "TabCountLimit": {
                "Enabled": true,
                "MaxCount": 20
              }
            }
            """;

            var config = ConfigLoader.ParseConf(json);

            Assert.True(config.TabCountLimit.Enabled);
            Assert.Equal(20, config.TabCountLimit.MaxCount);
        }

        // Zero is "no limit", so a config without a number cannot start warning
        // about every tab.
        [Fact]
        public void TabCountLimitDefaultsToDisabledWithoutALimit()
        {
            var config = ConfigLoader.ParseConf("{}");

            Assert.False(config.TabCountLimit.Enabled);
            Assert.Equal(0, config.TabCountLimit.MaxCount);
        }

        [Fact]
        public void ReadsTheUploadFileBridge()
        {
            var json = """
            {
              "UploadFileBridge": {
                "Enabled": true,
                "Destination": "\\\\fileserver\\audit\\%PCNAME%",
                "MaxSizeMB": 100
              }
            }
            """;

            var config = ConfigLoader.ParseConf(json);

            Assert.True(config.UploadFileBridge.Enabled);
            // The macros are left as they stand here; PathMacro expands them
            // when the copy is actually made.
            Assert.Equal(@"\\fileserver\audit\%PCNAME%", config.UploadFileBridge.Destination);
            Assert.Equal(100, config.UploadFileBridge.MaxSizeMB);
        }

        // Nothing is copied off the machine unless it was asked for, and an
        // empty destination leaves nowhere to copy it to.
        [Fact]
        public void UploadFileBridgeDefaultsToDisabledWithNoDestination()
        {
            var config = ConfigLoader.ParseConf("{}");

            Assert.False(config.UploadFileBridge.Enabled);
            Assert.Equal("", config.UploadFileBridge.Destination);
            // 0 is no limit, so leaving it out copies whatever it is given.
            Assert.Equal(0, config.UploadFileBridge.MaxSizeMB);
        }

        [Fact]
        public void StartupLauncherDefaultsToDisabledWithNoPrograms()
        {
            var config = ConfigLoader.ParseConf("{}");

            Assert.False(config.StartupLauncher.Enabled);
            Assert.Empty(config.StartupLauncher.Programs);
        }

        [Fact]
        public void OmittedGroupsFallBackToDefaults()
        {
            var json = """{ "NetLogger": { "Endpoint": "https://example.com/log" } }""";

            var config = ConfigLoader.ParseConf(json);

            Assert.Equal("https://example.com/log", config.NetLogger.Endpoint);
            // Groups that are absent keep their default values.
            Assert.False(config.UploadGuard.Enabled);
            Assert.Equal(new[] { ".exe", ".bat", ".cmd", ".js", ".vbs" }, config.UploadGuard.BlockedExtensions);
            Assert.False(config.SettingPageFilter.Enabled);
            Assert.Equal(
                new[] { "edge://settings", "edge://flags", "edge://policy" },
                config.SettingPageFilter.BlockedPrefixes);
            Assert.False(config.StartupLauncher.Enabled);
            Assert.Empty(config.StartupLauncher.Programs);
        }

        [Fact]
        public void PropertyNamesAreCaseInsensitive()
        {
            var json = """{ "netlogger": { "endpoint": "https://example.com/log", "upload": true } }""";

            var config = ConfigLoader.ParseConf(json);

            Assert.Equal("https://example.com/log", config.NetLogger.Endpoint);
            Assert.True(config.NetLogger.Upload);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void EmptyContentReturnsDefaults(string data)
        {
            var config = ConfigLoader.ParseConf(data);

            Assert.Equal("", config.NetLogger.Endpoint);
            Assert.False(config.UploadGuard.Enabled);
        }
    }
}
