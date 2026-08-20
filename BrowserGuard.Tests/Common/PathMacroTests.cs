using System;
using Xunit;
using BrowserGuard.Common;

namespace BrowserGuard.Tests.Common
{
    public class PathMacroTests
    {
        // A fixed day, so the date the macros produce is the one intended here.
        static readonly DateTime Now = new(2026, 8, 20, 13, 45, 30);

        [Fact]
        public void PutsTheDayIntoThePath()
        {
            Assert.Equal(
                @"\\server\audit\2026-08-20",
                PathMacro.Expand(@"\\server\audit\%DATE%", Now));
        }

        // So that a year of copies can be split a folder at a time.
        [Fact]
        public void PutsThePartsOfTheDayInSeparately()
        {
            Assert.Equal(
                @"\\server\audit\2026\08\20",
                PathMacro.Expand(@"\\server\audit\%YYYY%\%MM%\%DD%", Now));
        }

        // The same values the audit log records, so a folder named after them
        // lines up with the entries in it.
        [Fact]
        public void PutsTheMachineAndTheUserIntoThePath()
        {
            var expanded = PathMacro.Expand(@"\\server\%PCNAME%\%USERID%", Now);

            Assert.Equal($@"\\server\{System.Environment.MachineName}\{System.Environment.UserName}", expanded);
        }

        [Fact]
        public void ExpandsSeveralMacrosInOnePath()
        {
            var expanded = PathMacro.Expand(@"\\server\%PCNAME%\%DATE%\%USERID%", Now);

            Assert.Equal(
                $@"\\server\{System.Environment.MachineName}\2026-08-20\{System.Environment.UserName}",
                expanded);
        }

        [Theory]
        [InlineData("%pcname%")]
        [InlineData("%PcName%")]
        [InlineData("%PCNAME%")]
        public void DoesNotMindHowTheMacroIsSpelled(string macro)
        {
            Assert.Equal(System.Environment.MachineName, PathMacro.Expand(macro, Now));
        }

        // Dropping it would quietly put every machine in the same folder, where
        // a folder with a literal %FOO% in it is odd enough to be noticed.
        [Fact]
        public void LeavesAMacroItDoesNotKnowAsItStands()
        {
            Assert.Equal(
                @"\\server\%DEPARTMENT%\2026-08-20",
                PathMacro.Expand(@"\\server\%DEPARTMENT%\%DATE%", Now));
        }

        [Fact]
        public void LeavesAPathWithoutMacrosAlone()
        {
            Assert.Equal(@"\\server\audit", PathMacro.Expand(@"\\server\audit", Now));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void AnswersWithNothingForNothing(string? text)
        {
            Assert.Equal("", PathMacro.Expand(text!, Now));
        }

        // Written the same way as the macros, and useful for the same reason: a
        // share that moves need not be edited into every machine's config.
        [Fact]
        public void ExpandsAWindowsEnvironmentVariable()
        {
            WithVariable("BROWSERGUARD_SHARE", @"\\fileserver\audit", () =>
                Assert.Equal(
                    @"\\fileserver\audit\2026-08-20",
                    PathMacro.Expand(@"%BROWSERGUARD_SHARE%\%DATE%", Now)));
        }

        // The same as the macros do with a name they do not know.
        [Fact]
        public void LeavesAVariableItDoesNotKnowAsItStands()
        {
            WithVariable("BROWSERGUARD_NOT_SET", null, () =>
                Assert.Equal(
                    @"\\server\%BROWSERGUARD_NOT_SET%",
                    PathMacro.Expand(@"\\server\%BROWSERGUARD_NOT_SET%", Now)));
        }

        // Expanding the variables first would let a value that reads like a
        // macro be taken for one.
        [Fact]
        public void ExpandsTheMacrosBeforeTheVariables()
        {
            WithVariable("BROWSERGUARD_LOOKS_LIKE_A_MACRO", "%DATE%", () =>
                Assert.Equal("%DATE%", PathMacro.Expand("%BROWSERGUARD_LOOKS_LIKE_A_MACRO%", Now)));
        }

        // Set for the one test and put back afterwards, so the result does not
        // depend on what the machine running the tests happens to have.
        static void WithVariable(string name, string? value, Action check)
        {
            var before = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
            try
            {
                check();
            }
            finally
            {
                Environment.SetEnvironmentVariable(name, before);
            }
        }

        // A stray percent sign is not the start of a macro.
        [Fact]
        public void LeavesALonePercentSignAlone()
        {
            Assert.Equal(@"\\server\100%\audit", PathMacro.Expand(@"\\server\100%\audit", Now));
        }
    }
}
