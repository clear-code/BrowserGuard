using System;
using System.IO;
using System.Linq;
using Xunit;
using BrowserGuard.NetLogger;

namespace BrowserGuard.Tests.NetLogger
{
    public class NetLogSpoolTests : IDisposable
    {
        readonly string tempDir;

        public NetLogSpoolTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "browserguard-spool-" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }

        NetLogSpool Spool(long maxSize = 1024 * 1024) => new(tempDir, maxSize);

        string PendingPath => Path.Combine(tempDir, "netlog-pending.jsonl");

        string TakenPath => Path.Combine(tempDir, "netlog-pending.taken.jsonl");

        static string Entry(string operation) => $$"""{"operation":"{{operation}}"}""";

        [Fact]
        public void KeepsAnEntry()
        {
            var spool = Spool();

            Assert.True(spool.Add(Entry("browsing")));

            Assert.Equal(new[] { Entry("browsing") }, File.ReadAllLines(PendingPath));
        }

        [Fact]
        public void KeepsTheEntriesInOrder()
        {
            var spool = Spool();

            spool.Add(Entry("first"));
            spool.AddRange(new[] { Entry("second"), Entry("third") });

            Assert.Equal(
                new[] { Entry("first"), Entry("second"), Entry("third") },
                File.ReadAllLines(PendingPath));
        }

        [Fact]
        public void HandsBackWhatItKept()
        {
            var spool = Spool();
            spool.Add(Entry("first"));
            spool.Add(Entry("second"));

            var taken = spool.Take();

            Assert.Equal(new[] { Entry("first"), Entry("second") }, taken);
        }

        // Entries arriving during a round must not be swept up with it.
        [Fact]
        public void KeepsWhatArrivesDuringARound()
        {
            var spool = Spool();
            spool.Add(Entry("before"));

            var taken = spool.Take();
            spool.Add(Entry("during"));
            spool.Settle();

            Assert.Equal(new[] { Entry("before") }, taken);
            Assert.Equal(new[] { Entry("during") }, File.ReadAllLines(PendingPath));
        }

        [Fact]
        public void TakesWhatWasPutBack()
        {
            var spool = Spool();
            spool.Add(Entry("first"));
            spool.Add(Entry("second"));

            var taken = spool.Take();
            spool.AddRange(taken.Skip(1));
            spool.Settle();

            Assert.Equal(new[] { Entry("second") }, spool.Take());
        }

        [Fact]
        public void LeavesNothingBehindOnceARoundIsSettled()
        {
            var spool = Spool();
            spool.Add(Entry("first"));

            spool.Take();
            spool.Settle();

            Assert.False(File.Exists(TakenPath));
            Assert.False(File.Exists(PendingPath));
            Assert.Empty(spool.Take());
        }

        // A host that stopped mid round left its entries in the file it had
        // moved aside. Losing them would be worse than sending one twice.
        [Fact]
        public void RecoversARoundThatNeverFinished()
        {
            var spool = Spool();
            spool.Add(Entry("interrupted"));
            spool.Take();
            // Settle is never reached, as if the process had stopped here.

            var recovered = new NetLogSpool(tempDir, 1024 * 1024).Take();

            Assert.Equal(new[] { Entry("interrupted") }, recovered);
        }

        [Fact]
        public void ReturnsNothingWhenItHasKeptNothing()
        {
            Assert.Empty(Spool().Take());
        }

        // A collector that stays down must not be able to fill the disk.
        [Fact]
        public void StopsKeepingEntriesOnceItIsFull()
        {
            var spool = Spool(maxSize: 200);

            Assert.True(spool.Add(new string('a', 300)));
            Assert.False(spool.Add(Entry("dropped")));

            Assert.DoesNotContain("dropped", File.ReadAllText(PendingPath));
        }

        // A site that would rather lose disk space than an audit entry.
        [Fact]
        public void KeepsEveryEntryWhenNoLimitIsSet()
        {
            var spool = Spool(maxSize: 0);

            spool.Add(new string('a', 300));
            Assert.True(spool.Add(Entry("still kept")));
            Assert.True(spool.Add(new string('b', 5000)));

            Assert.Equal(3, File.ReadAllLines(PendingPath).Length);
            Assert.Contains("still kept", File.ReadAllText(PendingPath));
        }

        [Fact]
        public void KeepsEntriesAgainOnceThereIsRoom()
        {
            var spool = Spool(maxSize: 200);
            spool.Add(new string('a', 300));
            Assert.False(spool.Add(Entry("dropped")));

            spool.Take();
            spool.Settle();

            Assert.True(spool.Add(Entry("later")));
        }

        [Fact]
        public void CreatesTheDirectory()
        {
            var spool = new NetLogSpool(Path.Combine(tempDir, "nested"), 1024 * 1024);

            Assert.True(spool.Add(Entry("browsing")));

            Assert.True(File.Exists(Path.Combine(tempDir, "nested", "netlog-pending.jsonl")));
        }
    }
}
