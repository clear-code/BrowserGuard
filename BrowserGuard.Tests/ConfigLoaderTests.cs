using BrowserGuard;
using Xunit;

namespace BrowserGuard.Tests
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
                "Auth": true,
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
            Assert.True(config.NetLogger.Auth);
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
