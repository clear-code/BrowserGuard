using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Threading;
using BrowserGuard.Common;

namespace BrowserGuard.NetLogger
{
    // Posts the entries to the collector.
    //
    // An entry arrives for every request and the message loop is single
    // threaded, so entries are queued and posted on a thread of their own. The
    // loop never waits on the network.
    //
    // An entry the collector would not take is handed to the spool, if there is
    // one, rather than dropped. A second thread offers what the spool holds to
    // the collector again on an interval.
    //
    // After the first refusal the entries go straight to the spool, so that a
    // collector which has stopped answering costs the attempts once rather than
    // once per entry. That second thread is what notices it is back.
    internal sealed class NetLogSender : IDisposable
    {
        private const int MaxQueued = 10000;
        private const int PostAttempts = 3;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
        // How long the browser's exit waits for the queue to drain.
        private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(10);

        private readonly BlockingCollection<string> queue = new(MaxQueued);
        private readonly ManualResetEventSlim stopping = new(false);
        private readonly HttpClient http;
        private readonly string endpoint;
        private readonly Logger? logger;
        private readonly NetLogSpool? spool;
        private readonly Thread worker;
        private readonly Thread? retrier;
        private readonly bool retriesLater;

        private int dropped;
        private volatile bool collectorIsDown;

        internal NetLogSender(
            string endpoint,
            Logger? logger = null,
            HttpMessageHandler? handler = null,
            NetLogSpool? spool = null,
            TimeSpan? retryInterval = null)
        {
            this.endpoint = endpoint;
            this.logger = logger;
            this.spool = spool;
            http = handler is null ? new HttpClient() : new HttpClient(handler);
            http.Timeout = RequestTimeout;

            // Without a spool there is nothing kept to offer again.
            var interval = spool is null ? TimeSpan.Zero : retryInterval ?? TimeSpan.Zero;
            // Settled before the worker starts, which reads it.
            retriesLater = interval > TimeSpan.Zero;

            worker = new Thread(Run)
            {
                IsBackground = true,
                Name = "netlog-sender",
            };
            worker.Start();

            if (!retriesLater)
            {
                return;
            }
            retrier = new Thread(() => RetryKept(interval))
            {
                IsBackground = true,
                Name = "netlog-retry",
            };
            retrier.Start();
        }

        // The queue is bounded: a collector that has stopped answering must cost
        // memory that stops growing, not memory that grows until the host dies.
        // What will not fit is kept rather than lost, if there is a spool.
        internal bool Enqueue(string line)
        {
            try
            {
                if (queue.TryAdd(line))
                {
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
                // The host is on its way out and no longer takes entries.
                return false;
            }

            // Only the first drop is logged, or the diagnostic log becomes the
            // thing filling the disk.
            if (Interlocked.Increment(ref dropped) == 1)
            {
                logger?.Log($"NetLogSender: queue is full for {endpoint}");
            }
            return Keep(line);
        }

        private void Run()
        {
            foreach (var line in queue.GetConsumingEnumerable())
            {
                // The collector has already refused, and the retry thread is
                // what finds out it is back. Spending the attempts again on
                // every entry only holds up writing them down.
                if (retriesLater && collectorIsDown)
                {
                    Keep(line);
                    continue;
                }
                var result = Post(line, PostAttempts);
                if (result == SendResult.Refused)
                {
                    // Keeping it would stall every entry behind it, for a
                    // collector that will never take this one anyway.
                    logger?.Log($"NetLogSender: {endpoint} refused an entry, which was dropped");
                    continue;
                }
                if (result == SendResult.Unavailable)
                {
                    collectorIsDown = true;
                    Keep(line);
                }
            }
        }

        private bool Keep(string line)
        {
            if (spool is null)
            {
                return false;
            }
            return spool.Add(line);
        }

        private void RetryKept(TimeSpan interval)
        {
            while (!stopping.Wait(interval))
            {
                var kept = spool!.Take();
                if (kept.Count == 0)
                {
                    spool.Settle();
                    continue;
                }

                // One attempt each, and the round stops as soon as the
                // collector is unreachable: if it is still down, working
                // through the rest only delays them. One it refuses outright
                // is dropped, so that it cannot stall the ones behind it.
                var done = 0;
                var sent = 0;
                var refused = 0;
                while (done < kept.Count)
                {
                    var result = Post(kept[done], 1);
                    if (result == SendResult.Unavailable)
                    {
                        break;
                    }
                    if (result == SendResult.Refused)
                    {
                        refused++;
                    }
                    else
                    {
                        sent++;
                    }
                    done++;
                }

                if (done < kept.Count)
                {
                    spool.AddRange(kept.GetRange(done, kept.Count - done));
                }
                if (refused > 0)
                {
                    logger?.Log($"NetLogSender: {endpoint} refused {refused} kept entries, which were dropped");
                }
                if (sent > 0)
                {
                    // Anything at all getting through says the collector
                    // answers again, so the queue may go back to posting.
                    collectorIsDown = false;
                    logger?.Log($"NetLogSender: sent {sent} kept entries to {endpoint}");
                }
                spool.Settle();
            }
        }

        private SendResult Post(string line, int attempts)
        {
            for (var attempt = 1; ; attempt++)
            {
                var result = Send(line);
                // Trying again only makes sense while it is the collector at
                // fault: a refusal of this entry will be a refusal every time.
                if (result != SendResult.Unavailable || attempt >= attempts)
                {
                    return result;
                }
                Thread.Sleep(RetryDelay);
            }
        }

        private enum SendResult
        {
            Sent,
            // The collector took against this entry and always will.
            Refused,
            // The collector is not answering; the entry is fine.
            Unavailable,
        }

        private SendResult Send(string line)
        {
            try
            {
                using var content = new StringContent(line, Encoding.UTF8, "application/json");
                var response = http.PostAsync(endpoint, content).GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode)
                {
                    return SendResult.Sent;
                }
                logger?.Log($"NetLogSender: {endpoint} answered {(int)response.StatusCode}");
                // A 4xx is about the entry, a 5xx about the collector.
                return (int)response.StatusCode is >= 400 and < 500
                    ? SendResult.Refused
                    : SendResult.Unavailable;
            }
            catch (Exception ex)
            {
                logger?.Log($"NetLogSender: {ex.Message}");
                return SendResult.Unavailable;
            }
        }

        public void Dispose()
        {
            stopping.Set();
            queue.CompleteAdding();
            // The browser has gone; give what is already queued a chance to
            // leave before the process does. Whatever does not make it is kept.
            worker.Join(FlushTimeout);
            retrier?.Join(FlushTimeout);
            http.Dispose();
            queue.Dispose();
            stopping.Dispose();
        }
    }
}
