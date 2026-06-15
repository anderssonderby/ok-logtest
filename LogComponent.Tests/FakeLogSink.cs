using System.Collections.Concurrent;
using LogComponent;

namespace LogComponent.Tests
{
    /// <summary>
    /// In-memory <see cref="ILogSink"/> for tests. Records every line it receives and can
    /// optionally pause on each write so timing-sensitive stop behaviour can be exercised
    /// deterministically.
    /// </summary>
    internal sealed class FakeLogSink : ILogSink
    {
        private readonly ConcurrentQueue<string> _written = new();
        private readonly TimeSpan _perWriteDelay;

        public FakeLogSink(TimeSpan? perWriteDelay = null)
        {
            _perWriteDelay = perWriteDelay ?? TimeSpan.Zero;
        }

        public IReadOnlyCollection<string> Written => _written.ToArray();

        public int Count => _written.Count;

        public bool Disposed { get; private set; }

        public void Write(LogLine line)
        {
            if (_perWriteDelay > TimeSpan.Zero)
                Thread.Sleep(_perWriteDelay);

            _written.Enqueue(line.Text);
        }

        public void Dispose() => Disposed = true;
    }
}
