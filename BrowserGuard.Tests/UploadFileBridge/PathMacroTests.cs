using System;
using Xunit;
using BrowserGuard.UploadFileBridge;

namespace BrowserGuard.Tests.UploadFileBridge
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

            Assert.Equal($@"\\server\{Environment.MachineName}\{Environment.UserName}", expanded);
        }

        [Fact]
        public void ExpandsSeveralMacrosInOnePath()
        {
            var expanded = PathMacro.Expand(@"\\server\%PCNAME%\%DATE%\%USERID%", Now);

            Assert.Equal(
                $@"\\server\{Environment.MachineName}\2026-08-20\{Environment.UserName}",
                expanded);
        }

        [Theory]
        [InlineData("%pcname%")]
        [InlineData("%PcName%")]
        [InlineData("%PCNAME%")]
        public void DoesNotMindHowTheMacroIsSpelled(string macro)
        {
            Assert.Equal(Environment.MachineName, PathMacro.Expand(macro, Now));
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

        // A stray percent sign is not the start of a macro.
        [Fact]
        public void LeavesALonePercentSignAlone()
        {
            Assert.Equal(@"\\server\100%\audit", PathMacro.Expand(@"\\server\100%\audit", Now));
        }
    }
}
