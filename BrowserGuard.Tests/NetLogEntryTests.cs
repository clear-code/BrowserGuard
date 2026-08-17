using System;
using System.Text.Json;
using BrowserGuard;
using Xunit;

namespace BrowserGuard.Tests
{
    public class NetLogEntryTests
    {
        static JsonElement Parse(string line) => JsonDocument.Parse(line).RootElement;

        [Fact]
        public void AcceptsAnEntry()
        {
            var rejection = NetLogEntry.Compact(
                """{"operation":"browsing","url":"https://example.com/"}""", out var line);

            Assert.Null(rejection);
            var entry = Parse(line);
            Assert.Equal("browsing", entry.GetProperty("operation").GetString());
            Assert.Equal("https://example.com/", entry.GetProperty("url").GetString());
        }

        // The browser is never told either of these, so it cannot send them.
        [Fact]
        public void StampsOnTheMachineAndTheUser()
        {
            NetLogEntry.Compact("""{"operation":"browsing"}""", out var line);

            var entry = Parse(line);
            Assert.Equal(Environment.MachineName, entry.GetProperty("pcname").GetString());
            Assert.Equal(Environment.UserName, entry.GetProperty("userid").GetString());
        }

        // An entry must not be able to claim it came from somewhere else.
        [Fact]
        public void ReplacesAMachineAndUserTheSenderSuppliedAnyway()
        {
            NetLogEntry.Compact(
                """{"operation":"browsing","pcname":"SOMEONE-ELSE","userid":"root"}""",
                out var line);

            var entry = Parse(line);
            Assert.Equal(Environment.MachineName, entry.GetProperty("pcname").GetString());
            Assert.Equal(Environment.UserName, entry.GetProperty("userid").GetString());
            Assert.DoesNotContain("SOMEONE-ELSE", line);
            Assert.DoesNotContain("root", line);
        }

        // A sender that pretty printed its entry must not break the one line per
        // entry rule the file depends on.
        [Fact]
        public void PutsAPrettyPrintedEntryOnASingleLine()
        {
            NetLogEntry.Compact(
                "{\n  \"operation\": \"browsing\",\n  \"url\": \"https://example.com/\"\n}",
                out var line);

            Assert.DoesNotContain("\n", line);
            Assert.Equal("browsing", Parse(line).GetProperty("operation").GetString());
        }

        [Fact]
        public void KeepsTheValuesTheSenderDidGive()
        {
            NetLogEntry.Compact(
                """{"operation":"download","name":"a.pdf","timestamp":"2026-08-07 12:00:00"}""",
                out var line);

            var entry = Parse(line);
            Assert.Equal("download", entry.GetProperty("operation").GetString());
            Assert.Equal("a.pdf", entry.GetProperty("name").GetString());
            Assert.Equal("2026-08-07 12:00:00", entry.GetProperty("timestamp").GetString());
        }

        [Fact]
        public void LeavesJapaneseLegible()
        {
            NetLogEntry.Compact("""{"operation":"browsing","name":"ページ名"}""", out var line);

            Assert.Contains("ページ名", line);
        }

        [Fact]
        public void RefusesSomethingThatIsNotJson()
        {
            var rejection = NetLogEntry.Compact("not json at all", out var line);

            Assert.Contains("not valid JSON", rejection);
            Assert.Equal("", line);
        }

        // A bare string or an array is valid JSON but not a log entry.
        [Fact]
        public void RefusesJsonThatIsNotAnObject()
        {
            Assert.Contains("not a JSON object", NetLogEntry.Compact("\"browsing\"", out _));
            Assert.Contains("not a JSON object", NetLogEntry.Compact("[1,2,3]", out _));
        }
    }
}
