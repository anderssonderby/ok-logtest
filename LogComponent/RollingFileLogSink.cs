namespace LogComponent
{
    /// <summary>
    /// Writes log lines to timestamped files on disk and starts a new file whenever the
    /// calendar date of an incoming line differs from the current file's date (i.e. when
    /// midnight is crossed).
    /// </summary>
    public sealed class RollingFileLogSink : ILogSink
    {
        /// <summary>Directory used when none is supplied (preserves the original behaviour).</summary>
        public const string DefaultDirectory = @"C:\LogTest";

        private readonly string _directory;

        private StreamWriter? _writer;
        private DateTime _currentFileDate;
        private bool _disposed;

        public RollingFileLogSink(string? directory = null)
        {
            _directory = directory ?? DefaultDirectory;
        }

        public void Write(LogLine line)
        {
            if (_disposed)
                return;

            EnsureWriterFor(line.Timestamp);

            _writer!.Write(
                line.Timestamp.ToString("yyyy-MM-dd HH:mm:ss:fff") + "\t" +
                line.LineText() + "\t" +
                Environment.NewLine);
        }

        /// <summary>
        /// Opens a writer for the date of <paramref name="timestamp"/>, rolling to a new
        /// file when the date changes (or when no file is open yet).
        /// </summary>
        private void EnsureWriterFor(DateTime timestamp)
        {
            if (_writer is not null && timestamp.Date == _currentFileDate)
                return;

            _writer?.Flush();
            _writer?.Dispose();

            Directory.CreateDirectory(_directory);

            string path = Path.Combine(_directory, "Log" + timestamp.ToString("yyyyMMdd HHmmss fff") + ".log");
            _writer = new StreamWriter(path, append: true) { AutoFlush = true };
            _writer.Write("Timestamp".PadRight(25, ' ') + "\t" + "Data".PadRight(15, ' ') + "\t" + Environment.NewLine);

            _currentFileDate = timestamp.Date;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }
}
