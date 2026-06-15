using System.Collections.Concurrent;
using System.Text;

namespace LogComponent
{
    public class AsyncLog : ILog
    {
        private Thread _runThread;
        private BlockingCollection<LogLine> _lines = new BlockingCollection<LogLine>();

        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();

        private StreamWriter _writer = null!;

        // UTC calendar date of the file currently open; used to detect midnight rollover.
        private DateTime _currentLogDate;

        private readonly string _logDirectory;

        // Injectable so tests can control time.
        private readonly TimeProvider _timeProvider;

        private bool _disposed;

        public AsyncLog(string logDirectory = @"C:\LogTest", TimeProvider? timeProvider = null)
        {
            this._logDirectory = logDirectory;
            this._timeProvider = timeProvider ?? TimeProvider.System;

            if (!Directory.Exists(this._logDirectory))
                Directory.CreateDirectory(this._logDirectory);

            this.OpenNewFile(this._timeProvider.GetUtcNow().UtcDateTime);

            this._runThread = new Thread(this.MainLoop);
            this._runThread.Start();
        }

        private void MainLoop()
        {
            try
            {
                // Blocks when the queue is empty and wakes the instant a line arrives.
                // Completes when CompleteAdding() is called and the queue is drained, or
                // throws OperationCanceledException when the token is cancelled.
                foreach (LogLine logLine in this._lines.GetConsumingEnumerable(this._cancellation.Token))
                {
                    try
                    {
                        // Roll over to a new file when the line falls on a later UTC day
                        // than the one the current file was opened for.
                        if (logLine.Timestamp.Date != this._currentLogDate)
                        {
                            this._writer.Dispose();
                            this.OpenNewFile(logLine.Timestamp);
                        }

                        StringBuilder stringBuilder = new StringBuilder();

                        stringBuilder.Append(logLine.Timestamp.ToString("yyyy-MM-dd HH:mm:ss:fff"));
                        stringBuilder.Append("\t");
                        stringBuilder.Append(logLine.LineText());
                        stringBuilder.Append("\t");

                        stringBuilder.Append(Environment.NewLine);

                        this._writer.Write(stringBuilder.ToString());
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Swallow per-line failures so one bad write can't crash the host
                        // app; the filter lets cancellation through so shutdown still unwinds.
                        // TODO: log this exception to a fallback sink (e.g. event log / stderr).
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // StopWithoutFlush(): discard any lines still queued.
            }
        }

        // The caller is responsible for disposing any previously open writer.
        private void OpenNewFile(DateTime timestamp)
        {
            this._currentLogDate = timestamp.Date;

            string fileName = "Log" + timestamp.ToString("yyyyMMdd HHmmss fff") + ".log";

            this._writer = File.AppendText(Path.Combine(this._logDirectory, fileName));

            this._writer.Write("Timestamp".PadRight(25, ' ') + "\t" + "Data".PadRight(15, ' ') + "\t" + Environment.NewLine);

            this._writer.AutoFlush = true;
        }

        public void StopWithoutFlush()
        {
            this._lines.CompleteAdding();
            this._cancellation.Cancel();
            this._runThread.Join();
            this.Cleanup();
        }

        public void StopWithFlush()
        {
            this._lines.CompleteAdding();
            this._runThread.Join();
            this.Cleanup();
        }

        public void Write(string text)
        {
            this._lines.Add(new LogLine() { Text = text, Timestamp = this._timeProvider.GetUtcNow().UtcDateTime });
        }

        public void Dispose()
        {
            // Make sure the worker has stopped before releasing resources. If neither
            // Stop* was called, default to a flush so queued lines aren't silently lost.
            if (!this._lines.IsAddingCompleted)
                this._lines.CompleteAdding();

            if (this._runThread.IsAlive)
                this._runThread.Join();

            this.Cleanup();

            GC.SuppressFinalize(this);
        }

        // Releases the worker's resources. Safe to call multiple times and only
        // ever runs after the worker thread has terminated, so there is no race
        // on _writer.
        private void Cleanup()
        {
            if (this._disposed)
                return;

            this._disposed = true;

            this._writer.Dispose();
            this._cancellation.Dispose();
            this._lines.Dispose();
        }
    }
}
