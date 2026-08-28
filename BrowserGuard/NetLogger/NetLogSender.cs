using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using BrowserGuard.Common;

namespace BrowserGuard.NetLogger
{
    // Posts the entries to the collector.
    //
    // An entry is written to the spool on disk first and posted afterwards, on
    // a thread of its own. The message loop therefore pays a file append and
    // never waits on the network, so a collector that is slow or gone costs
    // nothing beyond that append.
    //
    // The spool holds what the collector has not taken yet: an entry leaves it
    // only once it is sent, which is why there is no position to remember. A
    // round the collector refuses waits out the interval before the next one,
    // rather than being tried again for every entry that arrives meanwhile.
    internal sealed class NetLogSender : IDisposable
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan DefaultRetryInterval = TimeSpan.FromMinutes(5);
        // How long the browser's exit waits for a round already under way.
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

        private const int Stopped = 0;

        private readonly ManualResetEvent stopping = new(false);
        private readonly AutoResetEvent arrived = new(false);
        private readonly HttpClient http;
        private readonly string endpoint;
        private readonly Logger? logger;
        private readonly NetLogSpool spool;
        private readonly TimeSpan retryInterval;
        private readonly Thread worker;

        private volatile bool stopped;

        internal NetLogSender(
            string endpoint,
            NetLogSpool spool,
            Logger? logger = null,
            HttpMessageHandler? handler = null,
            TimeSpan? retryInterval = null)
        {
            this.endpoint = endpoint;
            this.spool = spool;
            this.logger = logger;
            this.retryInterval = retryInterval ?? DefaultRetryInterval;
            http = handler is null ? new HttpClient() : new HttpClient(handler);
            http.Timeout = RequestTimeout;

            worker = new Thread(Run)
            {
                IsBackground = true,
                Name = "netlog-sender",
            };
            worker.Start();
        }

        // Written down before anything is attempted. False when the spool would
        // not take it, which the spool reports for itself.
        internal bool Enqueue(string line)
        {
            // The host is on its way out and no longer takes entries.
            if (stopped)
            {
                return false;
            }
            if (!spool.Add(line))
            {
                return false;
            }
            arrived.Set();
            return true;
        }

        private void Run()
        {
            var waits = new WaitHandle[] { stopping, arrived };
            while (true)
            {
                if (!SendRound())
                {
                    // Wait the collector out. Entries arriving meanwhile go to
                    // the spool and are picked up by the round after this one.
                    if (stopping.WaitOne(retryInterval))
                    {
                        return;
                    }
                    continue;
                }
                // Nothing left to send; sleep until something arrives.
                if (WaitHandle.WaitAny(waits) == Stopped)
                {
                    // The browser has gone. Whatever came in last still goes,
                    // so that a session's tail does not wait for the next one.
                    SendRound();
                    return;
                }
            }
        }

        // True when the collector took everything the spool held, which it also
        // did when the spool held nothing.
        private bool SendRound()
        {
            var pending = spool.Take();
            if (pending.Count == 0)
            {
                spool.Settle();
                return true;
            }

            // The round stops as soon as the collector is unreachable: if it is
            // still down, working through the rest only delays them. One it
            // refuses outright is dropped instead, so that a single entry it
            // will never take cannot stall every entry behind it.
            var done = 0;
            var sent = 0;
            var refused = 0;
            while (done < pending.Count)
            {
                var result = Send(pending[done]);
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

            if (done < pending.Count)
            {
                spool.AddRange(pending.GetRange(done, pending.Count - done));
            }
            if (refused > 0)
            {
                logger?.Log($"NetLogSender: {endpoint} refused {refused} entries, which were dropped");
            }
            if (sent > 0)
            {
                logger?.Log($"NetLogSender: sent {sent} entries to {endpoint}");
            }
            spool.Settle();
            return done == pending.Count;
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
            stopped = true;
            stopping.Set();
            // What a round under way does not finish is folded back in by the
            // spool when the host next starts.
            worker.Join(StopTimeout);
            http.Dispose();
            stopping.Dispose();
            arrived.Dispose();
        }
    }
}
