using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace BrowserGuard
{
    // Posts the entries to the collector.
    //
    // The browser used to do this itself, which meant its own requests went
    // through webRequest and were logged in turn. Sending from here keeps the
    // log out of the browser's own traffic entirely.
    //
    // An entry arrives for every request and the message loop is single
    // threaded, so entries are queued and posted on a thread of their own. The
    // loop never waits on the network.
    internal sealed class NetLogSender : IDisposable
    {
        private const int MaxQueued = 10000;
        private const int PostAttempts = 3;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
        // How long the browser's exit waits for the queue to drain.
        private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(10);

        private readonly BlockingCollection<string> queue = new(MaxQueued);
        private readonly HttpClient http;
        private readonly string endpoint;
        private readonly Logger? logger;
        private readonly Thread worker;

        private int dropped;

        internal NetLogSender(string endpoint, Logger? logger = null, HttpMessageHandler? handler = null)
        {
            this.endpoint = endpoint;
            this.logger = logger;
            http = handler is null ? new HttpClient() : new HttpClient(handler);
            http.Timeout = RequestTimeout;

            worker = new Thread(Run)
            {
                IsBackground = true,
                Name = "netlog-sender",
            };
            worker.Start();
        }

        // The queue is bounded: a collector that has stopped answering must cost
        // memory that stops growing, not memory that grows until the host dies.
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
                logger?.Log($"NetLogSender: queue is full, dropping entries for {endpoint}");
            }
            return false;
        }

        private void Run()
        {
            foreach (var line in queue.GetConsumingEnumerable())
            {
                Post(line);
            }
        }

        private void Post(string line)
        {
            for (var attempt = 1; attempt <= PostAttempts; attempt++)
            {
                try
                {
                    using var content = new StringContent(line, Encoding.UTF8, "application/json");
                    var response = http.PostAsync(endpoint, content).GetAwaiter().GetResult();
                    if (response.IsSuccessStatusCode)
                    {
                        return;
                    }
                    logger?.Log($"NetLogSender: {endpoint} answered {(int)response.StatusCode}");
                }
                catch (Exception ex)
                {
                    logger?.Log($"NetLogSender: {ex.Message}");
                }

                if (attempt < PostAttempts)
                {
                    Thread.Sleep(RetryDelay);
                }
            }
        }

        public void Dispose()
        {
            queue.CompleteAdding();
            // The browser has gone; give what is already queued a chance to
            // leave before the process does.
            worker.Join(FlushTimeout);
            http.Dispose();
            queue.Dispose();
        }
    }
}
