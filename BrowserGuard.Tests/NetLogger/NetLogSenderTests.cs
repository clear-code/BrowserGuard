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

        [Fact]
        public void PostsTheEntryToTheEndpoint()
        {
            var collector = new Collector();
            using var sender = new NetLogSender("https://collector.example.com/log", null, collector);

            Assert.True(sender.Enqueue(Entry));

            Assert.Equal(Entry, Take(collector));
            Assert.Equal(new Uri("https://collector.example.com/log"), collector.LastUri);
            Assert.Contains("application/json", collector.ContentTypes[0]);
        }

        [Fact]
        public void PostsEveryEntryInTurn()
        {
            var collector = new Collector();
            using var sender = new NetLogSender("https://collector.example.com/log", null, collector);

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
            using var sender = new NetLogSender("https://collector.example.com/log", null, collector);

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
            using var sender = new NetLogSender("https://collector.example.com/log", null, collector);

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
            var sender = new NetLogSender("https://collector.example.com/log", null, collector);
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
            var sender = new NetLogSender("https://collector.example.com/log", null, collector);
            sender.Dispose();

            Assert.False(sender.Enqueue(Entry));
        }

        // Nothing may be lost because the collector was unreachable.
        [Fact]
        public void KeepsAnEntryTheCollectorWouldNotTake()
        {
            var collector = new Collector { Status = HttpStatusCode.InternalServerError };
            var spool = Spool();
            using var sender = new NetLogSender(
                "https://collector.example.com/log", null, collector, spool);

            sender.Enqueue(Entry);

            Assert.Equal(new[] { Entry }, Kept(PendingPath));
        }

        [Fact]
        public void DropsTheEntryWhenNothingKeepsIt()
        {
            var collector = new Collector { Status = HttpStatusCode.InternalServerError };
            using var sender = new NetLogSender(
                "https://collector.example.com/log", null, collector);

            sender.Enqueue(Entry);

            Take(collector);
            Assert.False(File.Exists(PendingPath));
        }

        [Fact]
        public void OffersTheKeptEntriesAgain()
        {
            var collector = new Collector { Status = HttpStatusCode.InternalServerError };
            var spool = Spool();
            using var sender = new NetLogSender(
                "https://collector.example.com/log", null, collector, spool,
                retryInterval: TimeSpan.FromMilliseconds(200));
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
            using var sender = new NetLogSender(
                "https://collector.example.com/log", null, collector, spool,
                retryInterval: TimeSpan.FromMilliseconds(100));

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
            using var sender = new NetLogSender(
                "https://collector.example.com/log", null, collector, spool,
                retryInterval: TimeSpan.Zero);
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
            using var sender = new NetLogSender("https://collector.example.com/log", null, collector);

            sender.Enqueue("""{"operation":"first"}""");

            // Three attempts at the first entry, then it moves on.
            Assert.Contains("first", Take(collector));
            Assert.Contains("first", Take(collector));
            Assert.Contains("first", Take(collector));

            collector.Status = HttpStatusCode.OK;
            sender.Enqueue("""{"operation":"second"}""");
            Assert.Contains("second", Take(collector));
        }
    }
}
