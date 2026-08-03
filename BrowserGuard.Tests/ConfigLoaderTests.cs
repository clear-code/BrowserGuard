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
              "BlockSettingPage": {
                "Enabled": true,
                "UrlPrefixes": ["edge://settings/", "edge://flags/"]
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

            Assert.True(config.BlockSettingPage.Enabled);
            Assert.Equal(new[] { "edge://settings/", "edge://flags/" }, config.BlockSettingPage.UrlPrefixes);
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
        public void OmittedGroupsFallBackToDefaults()
        {
            var json = """{ "NetLogger": { "Endpoint": "https://example.com/log" } }""";

            var config = ConfigLoader.ParseConf(json);

            Assert.Equal("https://example.com/log", config.NetLogger.Endpoint);
            // Groups that are absent keep their default values.
            Assert.False(config.UploadGuard.Enabled);
            Assert.Equal(new[] { ".exe", ".bat", ".cmd", ".js", ".vbs" }, config.UploadGuard.BlockedExtensions);
            Assert.False(config.BlockSettingPage.Enabled);
            Assert.Equal(new[] { "edge://settings/" }, config.BlockSettingPage.UrlPrefixes);
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
