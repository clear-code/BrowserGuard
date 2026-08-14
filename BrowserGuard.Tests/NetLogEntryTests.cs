using BrowserGuard;
using Xunit;

namespace BrowserGuard.Tests
{
    public class NetLogEntryTests
    {
        [Fact]
        public void AcceptsAnEntry()
        {
            var rejection = NetLogEntry.Compact(
                """{"operation":"browsing","url":"https://example.com/"}""", out var line);

            Assert.Null(rejection);
            Assert.Equal("""{"operation":"browsing","url":"https://example.com/"}""", line);
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
            Assert.Equal("""{"operation":"browsing","url":"https://example.com/"}""", line);
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
