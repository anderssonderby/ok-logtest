namespace LogComponent
{
    /// <summary>
    /// Abstraction over the destination a <see cref="LogLine"/> is written to.
    /// Implementations decide where (and how) lines are persisted, which lets the
    /// asynchronous queue/threading logic in <see cref="AsyncLog"/> stay independent of
    /// file handling and lets tests substitute an in-memory sink.
    /// </summary>
    public interface ILogSink : IDisposable
    {
        /// <summary>
        /// Persist a single log line. Called sequentially from the logging worker thread.
        /// </summary>
        void Write(LogLine line);
    }
}
