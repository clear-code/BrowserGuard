using System.Text.Json.Nodes;
using Xunit;
using BrowserGuard.SubCommands.Policy;

namespace BrowserGuard.Tests.SubCommands.Policy
{
    // The registry side needs elevation, so these cover the JSON merging that
    // PolicyCommand performs on the ExtensionSettings value.
    public class PolicyCommandTests
    {
        const string Id = "ddniogodiahgpmfkljajobgkaecabnif";
        const string Url = "file:///C:/Program%20Files/BrowserGuard/BrowserGuardExtension/manifest.xml";

        static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;

        [Fact]
        public void AddsTheEntryToAnEmptyObject()
        {
            var settings = new JsonObject();

            PolicyCommand.SetEntry(settings, Id, Url);

            Assert.Single(settings);
            var entry = (JsonObject)settings[Id]!;
            Assert.Equal("force_installed", (string?)entry["installation_mode"]);
            Assert.Equal(Url, (string?)entry["update_url"]);
            Assert.True((bool)entry["override_update_url"]!);
        }

        [Fact]
        public void KeepsSettingsBelongingToOtherExtensions()
        {
            var settings = Parse("""
            {
              "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa": { "installation_mode": "blocked" },
              "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb": { "runtime_blocked_hosts": ["*://*.example.com"] }
            }
            """);

            PolicyCommand.SetEntry(settings, Id, Url);

            Assert.Equal(3, settings.Count);
            Assert.Equal("blocked",
                (string?)settings["aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"]!["installation_mode"]);
            Assert.Single((JsonArray)settings["bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"]!["runtime_blocked_hosts"]!);
        }

        [Fact]
        public void ReplacesAnEntryThatIsAlreadyThere()
        {
            var settings = Parse($$"""
            { "{{Id}}": { "installation_mode": "normal_installed", "update_url": "http://old/x.xml" } }
            """);

            PolicyCommand.SetEntry(settings, Id, Url);

            Assert.Single(settings);
            var entry = (JsonObject)settings[Id]!;
            Assert.Equal("force_installed", (string?)entry["installation_mode"]);
            Assert.Equal(Url, (string?)entry["update_url"]);
        }

        [Fact]
        public void RemovesOnlyOurEntry()
        {
            var settings = Parse($$"""
            {
              "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa": { "installation_mode": "blocked" },
              "{{Id}}": { "installation_mode": "force_installed" }
            }
            """);

            Assert.True(settings.Remove(Id));

            Assert.Single(settings);
            Assert.True(settings.ContainsKey("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
        }

        // Values written by other products may contain braces and quotes, which
        // a hand rolled string parser would have to special case.
        [Fact]
        public void SurvivesBracesInsideOtherValues()
        {
            var settings = Parse("""
            { "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa": { "blocked_install_message": "use {IT} portal }" } }
            """);

            PolicyCommand.SetEntry(settings, Id, Url);
            var roundTripped = Parse(settings.ToJsonString());

            Assert.Equal("use {IT} portal }",
                (string?)roundTripped["aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"]!["blocked_install_message"]);
            Assert.Equal(Url, (string?)roundTripped[Id]!["update_url"]);
        }
    }
}
