using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace BrowserGuard
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

        private int dropped;

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

            worker = new Thread(Run)
            {
                IsBackground = true,
                Name = "netlog-sender",
            };
            worker.Start();

            // Without a spool there is nothing kept to offer again.
            if (spool is null || retryInterval is not { } interval || interval <= TimeSpan.Zero)
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
                if (!Post(line, PostAttempts))
                {
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

                // One attempt each, and the round stops at the first refusal:
                // if the collector is still down, working through the rest only
                // delays them.
                var sent = 0;
                while (sent < kept.Count && Post(kept[sent], 1))
                {
                    sent++;
                }
                if (sent < kept.Count)
                {
                    spool.AddRange(kept.GetRange(sent, kept.Count - sent));
                }
                else
                {
                    logger?.Log($"NetLogSender: sent {sent} kept entries to {endpoint}");
                }
                spool.Settle();
            }
        }

        private bool Post(string line, int attempts)
        {
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                if (Send(line))
                {
                    return true;
                }
                if (attempt < attempts)
                {
                    Thread.Sleep(RetryDelay);
                }
            }
            return false;
        }

        private bool Send(string line)
        {
            try
            {
                using var content = new StringContent(line, Encoding.UTF8, "application/json");
                var response = http.PostAsync(endpoint, content).GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
                logger?.Log($"NetLogSender: {endpoint} answered {(int)response.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                logger?.Log($"NetLogSender: {ex.Message}");
                return false;
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
