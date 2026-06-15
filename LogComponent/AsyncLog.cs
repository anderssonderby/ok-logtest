using System.Collections.Concurrent;

namespace LogComponent
{
    /// <summary>
    /// Asynchronous log. <see cref="Write"/> only enqueues work, so callers never wait on
    /// disk IO; a single background thread drains the queue into an <see cref="ILogSink"/>.
    /// </summary>
    public sealed class AsyncLog : ILog
    {
        private readonly ILogSink _sink;
        private readonly TimeProvider _time;

        private readonly BlockingCollection<LogLine> _queue = new();
        private readonly Thread _worker;

        private readonly object _stopGate = new();
        private bool _stopRequested;
        private volatile bool _abort;

        /// <summary>Production default: rolling file sink in <see cref="RollingFileLogSink.DefaultDirectory"/>.</summary>
        public AsyncLog()
            : this(new RollingFileLogSink(), TimeProvider.System)
        {
        }

        public AsyncLog(ILogSink sink)
            : this(sink, TimeProvider.System)
        {
        }

        /// <param name="sink">Destination the lines are written to.</param>
        /// <param name="timeProvider">Clock used to timestamp lines (injectable for tests).</param>
        public AsyncLog(ILogSink sink, TimeProvider timeProvider)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

            _worker = new Thread(ProcessQueue)
            {
                IsBackground = true,
                Name = "AsyncLog worker"
            };
            _worker.Start();
        }

        public void Write(string text)
        {
            // Stamp at enqueue time so the line reflects when it was logged, not when it
            // happens to be flushed. The sink uses this timestamp for midnight rollover.
            var line = new LogLine { Text = text, Timestamp = _time.GetLocalNow().DateTime };

            try
            {
                _queue.Add(line);
            }
            catch (InvalidOperationException)
            {
                // The log was stopped between the check and the Add; drop the line rather
                // than throwing into the caller (Demand #4: never disrupt the application).
            }
        }

        private void ProcessQueue()
        {
            // GetConsumingEnumerable blocks while the queue is empty (no busy-wait) and ends
            // once CompleteAdding has been called and the queue has drained.
            foreach (LogLine line in _queue.GetConsumingEnumerable())
            {
                if (_abort)
                    break; // StopWithoutFlush: abandon outstanding lines.

                try
                {
                    _sink.Write(line);
                }
                catch
                {
                    // Demand #4: a logging failure must never bring down the calling
                    // application, so swallow it and keep processing the rest.
                }
            }

            try
            {
                _sink.Dispose();
            }
            catch
            {
                // Ignore failures while closing the sink.
            }
        }

        public void StopWithFlush() => Stop(abort: false);

        public void StopWithoutFlush() => Stop(abort: true);

        private void Stop(bool abort)
        {
            lock (_stopGate)
            {
                if (_stopRequested)
                    return; // Idempotent: a second stop call is a no-op.

                _stopRequested = true;
                if (abort)
                    _abort = true;

                _queue.CompleteAdding();
            }

            // Block until the worker has finished. For flush this guarantees every queued
            // line has been written before we return (honouring the ILog contract).
            _worker.Join();
        }

        public void Dispose() => StopWithFlush();
    }
}
