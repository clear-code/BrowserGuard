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

        // Long enough that no round of its own runs while a test does.
        static readonly TimeSpan NoRetry = TimeSpan.FromMinutes(10);

        readonly string tempDir;

        public NetLogSenderTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "browserguard-sender-" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }

        NetLogSpool Queue(long maxSize = 1024 * 1024) => new(tempDir, maxSize);

        NetLogSender Sender(
            Collector collector, NetLogSpool? queue = null, TimeSpan? retryInterval = null) =>
            new(Endpoint, queue ?? Queue(), null, collector, retryInterval ?? NoRetry);

        string PendingPath => Path.Combine(tempDir, NetLogSpool.FileName);

        string TakenPath => Path.Combine(tempDir, NetLogSpool.TakenFileName);

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

        static void Until(Func<bool> done)
        {
            for (var i = 0; i < 100 && !done(); i++)
            {
                Thread.Sleep(50);
            }
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
            // Run at the start of every request, so a test can look at the
            // state of the disk at the moment the entry is being posted.
            internal Action? Watching;

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Watching?.Invoke();
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
            using var sender = Sender(collector);

            sender.Enqueue(Entry);

            Assert.Equal(Entry, Take(collector));
            Assert.Equal(new Uri(Endpoint), collector.LastUri);
            Assert.Contains("application/json", collector.ContentTypes[0]);
        }

        [Fact]
        public void PostsEveryEntryInTurn()
        {
            var collector = new Collector();
            using var sender = Sender(collector);

            sender.Enqueue("""{"operation":"first"}""");
            sender.Enqueue("""{"operation":"second"}""");

            var posted = new[] { Take(collector), Take(collector) };
            Assert.Contains(posted, body => body.Contains("first"));
            Assert.Contains(posted, body => body.Contains("second"));
        }

        // The point of the whole arrangement: the entry is safe on disk before
        // the collector is approached, so nothing is lost if the host is killed.
        [Fact]
        public void WritesTheEntryDownBeforeItIsPosted()
        {
            var onDisk = false;
            var collector = new Collector();
            var taken = TakenPath;
            collector.Watching = () => onDisk = File.Exists(taken);
            using var sender = Sender(collector);

            sender.Enqueue(Entry);

            Take(collector);
            Assert.True(onDisk, "the entry should have been written before it was posted");
        }

        [Fact]
        public void HandsTheEntryOverWithoutWaitingForTheCollector()
        {
            var collector = new Collector();
            var held = new ManualResetEventSlim(false);
            collector.Watching = () => held.Wait(TimeSpan.FromSeconds(5));
            using var sender = Sender(collector);

            var started = DateTime.UtcNow;
            sender.Enqueue(Entry);
            var spent = DateTime.UtcNow - started;

            held.Set();
            Assert.True(spent < TimeSpan.FromSeconds(2), $"Enqueue waited {spent}");
        }

        // The browser closes the port on its way out, and the host follows.
        // Whatever is queued has to leave first.
        [Fact]
        public void SendsWhatIsQueuedBeforeItShutsDown()
        {
            var collector = new Collector();
            var sender = Sender(collector);

            sender.Enqueue(Entry);
            sender.Dispose();

            Assert.Equal(Entry, Take(collector));
            Assert.False(File.Exists(PendingPath));
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
            using var sender = Sender(collector);

            sender.Enqueue(Entry);

            Assert.Equal(new[] { Entry }, Kept(PendingPath));
        }

        // A collector that has stopped answering must cost one round, not one
        // round for every entry that arrives while it is down.
        [Fact]
        public void WaitsOutTheIntervalRatherThanTryingForEveryEntry()
        {
            var collector = new Collector { Status = HttpStatusCode.InternalServerError };
            using var sender = Sender(collector);

            sender.Enqueue("""{"operation":"first"}""");
            Assert.Contains("first", Take(collector));

            sender.Enqueue("""{"operation":"second"}""");
            sender.Enqueue("""{"operation":"third"}""");

            Until(() => Kept(PendingPath).Length >= 3);
            Assert.Equal(3, Kept(PendingPath).Length);
            // The two that followed were written down without being offered.
            Assert.Equal(0, collector.Bodies.Count);
        }

        [Fact]
        public void OffersTheKeptEntriesAgain()
        {
            var collector = new Collector { Status = HttpStatusCode.InternalServerError };
            using var sender = Sender(collector, retryInterval: TimeSpan.FromMilliseconds(200));

            sender.Enqueue("""{"operation":"kept"}""");
            // The first round is refused, a later one is not.
            Assert.Contains("kept", Take(collector));
            collector.Status = HttpStatusCode.OK;
            Assert.Contains("kept", Take(collector));

            // Once it is taken the queue is left empty and stays that way,
            // which is what says the entry was not put back again.
            Until(() => !File.Exists(PendingPath) && !File.Exists(TakenPath));
            Assert.False(File.Exists(PendingPath), "the kept entry should have been sent");
        }

        // Rounds are counted rather than the file read: a round in progress has
        // the file moved aside, so reading it while rounds run proves nothing.
        [Fact]
        public void PutsBackWhatTheCollectorStillWillNotTake()
        {
            var collector = new Collector { Status = HttpStatusCode.InternalServerError };
            using var sender = Sender(collector, retryInterval: TimeSpan.FromMilliseconds(100));

            sender.Enqueue("""{"operation":"kept"}""");

            // Being offered a second time is only possible if the first round
            // put it back.
            Assert.Contains("kept", Take(collector));
            Assert.Contains("kept", Take(collector));
        }

        // A 4xx says the collector will never take this entry. Putting it back
        // would stall every entry behind it for good.
        [Fact]
        public void DropsAnEntryTheCollectorRefusesOutright()
        {
            var collector = new Collector { Status = HttpStatusCode.BadRequest };
            using var sender = Sender(collector);

            sender.Enqueue("""{"operation":"refused"}""");
            Assert.Contains("refused", Take(collector));

            sender.Enqueue("""{"operation":"after"}""");
            Assert.Contains("after", Take(collector));
            Until(() => !File.Exists(PendingPath));
            Assert.False(File.Exists(PendingPath), "neither entry should have been kept");
        }

        // The queue reports for itself when it is full, and the entry is lost.
        [Fact]
        public void RefusesTheEntryOnceTheQueueIsFull()
        {
            var collector = new Collector { Status = HttpStatusCode.InternalServerError };
            using var sender = Sender(collector, Queue(maxSize: 1));

            Assert.True(sender.Enqueue(Entry));
            // The round takes the file aside to post from it, so the queue only
            // looks full again once the refused entry has been put back.
            Take(collector);
            Until(() => File.Exists(PendingPath));

            Assert.False(sender.Enqueue(Entry));
        }
    }
}
