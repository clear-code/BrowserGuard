using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using BrowserGuard.NetLogger;

namespace BrowserGuard.Tests.NetLogger
{
    public class NetLogSenderTests : IDisposable
    {
        const string Endpoint = "https://collector.example.com/log";
        const string Entry = """{"operation":"browsing","url":"https://example.com/"}""";

        readonly string tempDir;

        public NetLogSenderTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "browserguard-sender-" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }

        NetLogSpool Spool(long maxSize = 1024 * 1024) => new(tempDir, maxSize);

        // The seam the production constructor does not offer: a cap in bytes
        // and an interval in milliseconds, so a round can be driven at once.
        NetLogSender Sender(
            Collector collector, NetLogSpool? spool = null, TimeSpan? retryInterval = null) =>
            new(Endpoint, spool, retryInterval ?? TimeSpan.Zero, null, collector);

        string PendingPath => Path.Combine(tempDir, "netlog-pending.jsonl");

        static string[] Kept(string path)
        {
            for (var i = 0; i < 100; i++)
            {
                if (File.Exists(path))
                {
                    try { return File.ReadAllLines(path); } catch (IOException) { }
                }
                Thread.Sleep(50);
            }
            return Array.Empty<string>();
        }

        // Answers every request with a status the test chooses, and records what
        // it was sent, so the whole HttpClient path is exercised for real.
        sealed class Collector : HttpMessageHandler
        {
            internal readonly BlockingCollection<string> Bodies = new();
            internal readonly List<string> ContentTypes = new();
            internal HttpStatusCode Status = HttpStatusCode.OK;
            internal int FailuresLeft;
            internal Uri? LastUri;

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastUri = request.RequestUri;
                ContentTypes.Add(request.Content?.Headers.ContentType?.ToString() ?? "");
                Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));

                if (FailuresLeft > 0)
                {
                    FailuresLeft--;
                    throw new HttpRequestException("the collector is unreachable");
                }
                return new HttpResponseMessage(Status);
            }
        }

        static string Take(Collector collector) =>
            collector.Bodies.TryTake(out var body, TimeSpan.FromSeconds(5))
                ? body
                : throw new TimeoutException("nothing was posted");

        // The spool is the sender's own, so the constructor the host uses makes
        // it rather than being handed one.
        [Fact]
        public void MakesItsOwnSpoolFromTheConfig()
        {
            var config = new NetLogSenderConfig
            {
                Enabled = true,
                Endpoint = Endpoint,
                Spool = new NetLogSpoolConfig { Enabled = true },
            };

            using var sender = new NetLogSender(config, tempDir);

            Assert.Equal(PendingPath, sender.SpoolPath);
        }

        // Nothing is kept unless it was asked for.
        [Fact]
        public void MakesNoSpoolWhenItIsNotAskedFor()
        {
            var config = new NetLogSenderConfig { Enabled = true, Endpoint = Endpoint };

            using var sender = new NetLogSender(config, tempDir);

            Assert.Null(sender.SpoolPath);
        }

        [Fact]
        public void PostsTheEntryToTheEndpoint()
        {
            var collector = new Collector();
            using var sender = Sender(collector);

            Assert.True(sender.Enqueue(Entry));

            Assert.Equal(Entry, Take(collector));
            Assert.Equal(new Uri(Endpoint), collector.LastUri);
            Assert.Contains("application/json", collector.ContentTypes[0]);
        }

        [Fact]
        public void PostsEveryEntryInTurn()
        {
            var collector = new Collector();
            using var sender = Sender(collector);

            for (var i = 0; i < 5; i++)
            {
                sender.Enqueue($$"""{"operation":"browsing","n":{{i}}}""");
            }

            for (var i = 0; i < 5; i++)
            {
                Assert.Contains($"\"n\":{i}", Take(collector));
            }
        }

        // The message loop must not wait on the network.
        [Fact]
        public void HandsTheEntryOverWithoutWaitingForTheCollector()
        {
            var collector = new Collector();
            using var sender = Sender(collector);

            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < 100; i++)
            {
                sender.Enqueue(Entry);
            }
            elapsed.Stop();

            Assert.True(elapsed.ElapsedMilliseconds < 1000,
                $"queueing 100 entries took {elapsed.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void TriesAgainAfterAFailure()
        {
            var collector = new Collector { FailuresLeft = 2 };
            using var sender = Sender(collector);

            sender.Enqueue(Entry);

            Take(collector);
            Take(collector);
            Assert.Equal(Entry, Take(collector));
        }

        // The browser closes the port on its way out, and the host follows.
        // Whatever is queued has to leave first.
        [Fact]
        public void SendsWhatIsQueuedBeforeItShutsDown()
        {
            var collector = new Collector();
            var sender = Sender(collector);
            for (var i = 0; i < 10; i++)
            {
                sender.Enqueue(Entry);
            }

            sender.Dispose();

            Assert.Equal(10, collector.Bodies.Count);
        }

        [Fact]
        public void TakesNoFurtherEntriesOnceItHasShutDown()
        {
            var collector = new Collector();
            var sender = Sender(collector);
            sender.Dispose();

            Assert.False(sender.Enqueue(Entry));
        }

        // Nothing may be lost because the collector was unreachable.
        [Fact]
        public void KeepsAnEntryTheCollectorWouldNotTake()
        {
            var collector = new Collector { Status = HttpStatusCode.InternalServerError };
            var spool = Spool();
            using var sender = Sender(collector, spool);

            sender.Enqueue(Entry);

            Assert.Equal(new[] { Entry }, Kept(PendingPath));
        }

        [Fact]
        public void DropsTheEntryWhenNothingKeepsIt()
        {
            var collector = new Collector { Status = HttpStatusCode.InternalServerError };
            using var sender = Sender(collector);

            sender.Enqueue(Entry);

            Take(collector);
            Assert.False(File.Exists(PendingPath));
        }

        [Fact]
        public void OffersTheKeptEntriesAgain()
        {
            var collector = new Collector { Status = HttpStatusCode.InternalServerError };
            var spool = Spool();
            using var sender = Sender(collector, spool, retryInterval: TimeSpan.FromMilliseconds(200));
            sender.Enqueue("""{"operation":"kept"}""");
            Assert.Equal(1, Kept(PendingPath).Length);

            collector.Status = HttpStatusCode.OK;

            // The retry round empties the spool once the collector answers.
            for (var i = 0; i < 100 && File.Exists(PendingPath); i++)
            {
                Thread.Sleep(50);
            }
            Assert.False(File.Exists(PendingPath), "the kept entry should have been sent");
        }

        [Fact]
        public void PutsBackWhatTheCollectorStillWillNotTake()
        {
            var collector = new Collector { Status = HttpStatusCode.InternalServerError };
            var spool = Spool();
            using var sender = Sender(collector, spool, retryInterval: TimeSpan.FromMilliseconds(100));

            sender.Enqueue("""{"operation":"kept"}""");

            Thread.Sleep(500);
            Assert.Contains("kept", string.Join("\n", Kept(PendingPath)));
        }

        // Retrying is optional: the entries are then kept for collection by hand.
        [Fact]
        public void KeepsWithoutRetryingWhenNoIntervalIsSet()
        {
            var collector = new Collector { Status = HttpStatusCode.InternalServerError };
            var spool = Spool();
            using var sender = Sender(collector, spool, retryInterval: TimeSpan.Zero);
            sender.Enqueue(Entry);
            Assert.Single(Kept(PendingPath));
            var attempts = collector.Bodies.Count;

            collector.Status = HttpStatusCode.OK;
            Thread.Sleep(500);

            Assert.Equal(attempts, collector.Bodies.Count);
            Assert.Single(Kept(PendingPath));
        }

        // A collector that answers with an error must not stop the ones after it.
        [Fact]
        public void KeepsGoingAfterTheCollectorRefusesAnEntry()
        {
            var collector = new Collector { Status = HttpStatusCode.InternalServerError };
            using var sender = Sender(collector);

            sender.Enqueue("""{"operation":"first"}""");

            // Three attempts at the first entry, then it moves on.
            Assert.Contains("first", Take(collector));
            Assert.Contains("first", Take(collector));
            Assert.Contains("first", Take(collector));

            collector.Status = HttpStatusCode.OK;
            sender.Enqueue("""{"operation":"second"}""");
            Assert.Contains("second", Take(collector));
        }

        // A collector that has stopped answering must cost the attempts once,
        // not once for every entry waiting to be written down.
        [Fact]
        public void WritesStraightToTheSpoolOnceTheCollectorHasRefused()
        {
            var collector = new Collector { Status = HttpStatusCode.InternalServerError };
            var spool = Spool();
            using var sender = Sender(
                collector, spool,
                // Long enough that no retry round runs while the test does.
                retryInterval: TimeSpan.FromMinutes(10));

            sender.Enqueue("""{"operation":"first"}""");
            Assert.Contains("first", Take(collector));
            Assert.Contains("first", Take(collector));
            Assert.Contains("first", Take(collector));

            sender.Enqueue("""{"operation":"second"}""");
            sender.Enqueue("""{"operation":"third"}""");

            for (var i = 0; i < 100 && Kept(PendingPath).Length < 3; i++)
            {
                Thread.Sleep(50);
            }
            Assert.Equal(3, Kept(PendingPath).Length);
            // The two that followed were kept without being offered at all.
            Assert.Equal(0, collector.Bodies.Count);
        }

        // A 4xx says the collector will never take this entry. Keeping it would
        // stall every entry behind it in the spool, for good.
        [Fact]
        public void DropsAnEntryTheCollectorRefusesOutright()
        {
            var collector = new Collector { Status = HttpStatusCode.BadRequest };
            var spool = Spool();
            using var sender = Sender(collector, spool, retryInterval: TimeSpan.FromMinutes(10));

            sender.Enqueue("""{"operation":"refused"}""");
            Assert.Contains("refused", Take(collector));

            // One attempt rather than three, and nothing is kept.
            Thread.Sleep(500);
            Assert.Equal(0, collector.Bodies.Count);
            Assert.False(File.Exists(PendingPath));
        }
    }
}
