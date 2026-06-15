namespace LogComponent
{
    /// <summary>
    /// Public contract for the asynchronous logging component.
    /// </summary>
    public interface ILog : IDisposable
    {
        /// <summary>
        /// Stop the logging immediately. Any outstanding (not yet written) logs are discarded.
        /// </summary>
        void StopWithoutFlush();

        /// <summary>
        /// Stop the logging. The call will not return until all outstanding logs have been written.
        /// </summary>
        void StopWithFlush();

        /// <summary>
        /// Write a message to the log. Returns as fast as possible; the actual file write
        /// happens on a background thread.
        /// </summary>
        /// <param name="text">The text to be written to the log.</param>
        void Write(string text);
    }
}
