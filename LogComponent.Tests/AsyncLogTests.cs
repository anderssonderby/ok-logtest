using System.Diagnostics;
using LogComponent;

namespace LogComponent.Tests
{
    public class AsyncLogTests
    {
        // Demand: "A call to ILog will end up in writing something."
        [Fact]
        public void Write_then_StopWithFlush_persists_the_line()
        {
            var sink = new FakeLogSink();
            var log = new AsyncLog(sink);

            log.Write("hello");
            log.StopWithFlush();

            Assert.Contains("hello", sink.Written);
            Assert.True(sink.Disposed);
        }

        // Demand: stop-with-flush must not return until ALL outstanding lines are written.
        [Fact]
        public void StopWithFlush_writes_every_queued_line_before_returning()
        {
            // A small per-write delay guarantees lines are still queued when we ask to stop,
            // so we are really proving the call blocks until the queue has drained.
            var sink = new FakeLogSink(perWriteDelay: TimeSpan.FromMilliseconds(5));
            var log = new AsyncLog(sink);

            const int count = 50;
            for (int i = 0; i < count; i++)
                log.Write("line " + i);

            log.StopWithFlush();

            Assert.Equal(count, sink.Count);
            Assert.True(sink.Disposed);
        }

        // Demand: stop-now must discard outstanding lines (and return promptly).
        [Fact]
        public void StopWithoutFlush_discards_outstanding_lines()
        {
            // Each write is slow, so the vast majority of the 50 lines are still queued when
            // we abort. After abort the worker must stop instead of draining them.
            var sink = new FakeLogSink(perWriteDelay: TimeSpan.FromMilliseconds(50));
            var log = new AsyncLog(sink);

            const int count = 50;
            for (int i = 0; i < count; i++)
                log.Write("line " + i);

            var sw = Stopwatch.StartNew();
            log.StopWithoutFlush();
            sw.Stop();

            Assert.True(sink.Count < count, $"Expected fewer than {count} lines, got {sink.Count}.");
            Assert.True(sw.ElapsedMilliseconds < count * 50, "StopWithoutFlush should not drain the whole queue.");
        }

        [Fact]
        public void Stopping_twice_is_a_safe_no_op()
        {
            var log = new AsyncLog(new FakeLogSink());

            log.StopWithFlush();
            log.StopWithFlush();        // idempotent
            log.StopWithoutFlush();     // still safe after a stop
        }
    }
}
