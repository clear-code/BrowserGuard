using System;
using System.Text.Json;
using Xunit;
using BrowserGuard.NetLogger;

namespace BrowserGuard.Tests.NetLogger
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

        // The browser is told none of these, so it cannot send them.
        [Fact]
        public void StampsOnWhoRecordedTheEntryAndWhere()
        {
            NetLogEntry.Compact("""{"operation":"browsing"}""", out var line);

            var entry = Parse(line);
            Assert.Equal(Environment.MachineName, entry.GetProperty("host").GetString());
            // The operation log's shape, so the two line up on the account.
            Assert.Equal(
                $@"{Environment.UserDomainName}\{Environment.UserName}",
                entry.GetProperty("user").GetString());
            Assert.Equal(NetLogIdentity.Session, entry.GetProperty("session").GetInt32());
        }

        // The operation log leaves it out when it cannot be had, and so does this.
        [Fact]
        public void LeavesOutADisplayNameItCouldNotFind()
        {
            NetLogEntry.Compact("""{"operation":"browsing"}""", out var line);

            var entry = Parse(line);
            Assert.Equal(
                NetLogIdentity.DisplayName.Length > 0,
                entry.TryGetProperty("user_displayName", out _));
        }

        // An entry must not be able to claim it came from somewhere else.
        [Fact]
        public void ReplacesAMachineAndUserTheSenderSuppliedAnyway()
        {
            NetLogEntry.Compact(
                """
                {"operation":"browsing","host":"SOMEONE-ELSE","user":"OTHER\\root",
                 "user_displayName":"Someone Else","session":999}
                """,
                out var line);

            var entry = Parse(line);
            Assert.Equal(Environment.MachineName, entry.GetProperty("host").GetString());
            Assert.Equal(NetLogIdentity.Session, entry.GetProperty("session").GetInt32());
            Assert.DoesNotContain("SOMEONE-ELSE", line);
            Assert.DoesNotContain("Someone Else", line);
            Assert.DoesNotContain("999", line);
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

        // The upload went through; keeping a copy of it did not. The name has
        // to say so, or the trail reads as though the upload itself failed.
        [Fact]
        public void MakesAnEntryForACopyThatCouldNotBeKept()
        {
            var entry = NetLogEntry.UploadFileBridgeFailed(
                @"C:\tmp\report.xlsx", "https://example.com/upload", "access denied", new DateTime(2026, 8, 20, 13, 45, 30));

            using var document = JsonDocument.Parse(entry);
            var root = document.RootElement;
            Assert.Equal("upload-file-bridge", root.GetProperty("operation").GetString());
            Assert.Equal(@"C:\tmp\report.xlsx", root.GetProperty("name").GetString());
            Assert.Equal("https://example.com/upload", root.GetProperty("url").GetString());
            Assert.Equal("2026-08-20 13:45:30", root.GetProperty("timestamp").GetString());
            Assert.Equal("access denied", root.GetProperty("reason").GetString());
        }

        // It goes back through Compact, so it is stamped like any other entry.
        [Fact]
        public void StampsAnEntryTheHostMadeItself()
        {
            var entry = NetLogEntry.UploadFileBridgeFailed(
                @"C:\tmp\report.xlsx", "https://example.com/upload", "no destination", DateTime.Now);

            Assert.Null(NetLogEntry.Compact(entry, out var line));

            using var document = JsonDocument.Parse(line);
            Assert.Equal(Environment.MachineName,
                document.RootElement.GetProperty(NetLogEntry.HostProperty).GetString());
            Assert.Equal(NetLogIdentity.Account,
                document.RootElement.GetProperty(NetLogEntry.UserProperty).GetString());
        }
    }
}
