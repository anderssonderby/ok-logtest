using System.Text.RegularExpressions;
using Xunit;

namespace LogComponent.Tests
{
    public class AsyncLogTests : IDisposable
    {
        private readonly string _tempDir;

        public AsyncLogTests()
        {
            this._tempDir = Path.Combine(Path.GetTempPath(), "LogComponentTests", Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            if (Directory.Exists(this._tempDir))
                Directory.Delete(this._tempDir, recursive: true);
        }

        // Returns the *.log files in the log directory, ordered by name (which encodes the
        // timestamp, so this is chronological).
        private string[] LogFiles()
        {
            string[] files = Directory.GetFiles(this._tempDir, "*.log");
            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }

        // Data lines = every non-empty line except the single header row.
        private static IReadOnlyList<string> DataLines(string file)
        {
            return File.ReadAllLines(file)
                .Where(line => line.Length > 0)
                .Where(line => !line.StartsWith("Timestamp"))
                .ToList();
        }

        [Fact]
        public void Write_PersistsLines_WithHeaderAndTimestampFormat()
        {
            string[] messages = { "alpha", "beta", "gamma" };

            ILog logger = new AsyncLog(this._tempDir);
            foreach (string message in messages)
                logger.Write(message);
            logger.StopWithFlush();

            string[] files = LogFiles();
            Assert.Single(files);

            string[] allLines = File.ReadAllLines(files[0]);
            Assert.StartsWith("Timestamp", allLines[0]); // header present

            IReadOnlyList<string> data = DataLines(files[0]);
            Assert.Equal(messages.Length, data.Count);

            foreach (string message in messages)
                Assert.Contains(data, line => line.Contains(message));

            // Each data line starts with a UTC timestamp in the documented format.
            var timestamp = new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}:\d{3}\t");
            Assert.All(data, line => Assert.Matches(timestamp, line));
        }

        [Fact]
        public void Rollover_CreatesNewFile_WhenUtcDayChanges()
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 15, 23, 59, 0, TimeSpan.Zero));

            ILog logger = new AsyncLog(this._tempDir, clock);

            logger.Write("day1");
            clock.Advance(TimeSpan.FromMinutes(2)); // -> 2026-06-16 00:01 UTC
            logger.Write("day2");
            logger.StopWithFlush();

            string[] files = LogFiles();
            Assert.Equal(2, files.Length);

            Assert.Contains(DataLines(files[0]), line => line.Contains("day1"));
            Assert.Contains(DataLines(files[1]), line => line.Contains("day2"));
        }

        [Fact]
        public void StopWithoutFlush_DiscardsOutstandingLines()
        {
            const int total = 10000;

            ILog logger = new AsyncLog(this._tempDir);
            for (int i = 0; i < total; i++)
                logger.Write("Line " + i);
            logger.StopWithoutFlush();

            int written = LogFiles().Sum(f => DataLines(f).Count);

            // The worker cannot drain a 10k backlog (per-line AutoFlush disk I/O) before
            // the cancel fires, so some lines must have been dropped.
            Assert.True(written < total, $"Expected fewer than {total} lines, but {written} were written.");
        }

        [Fact]
        public void StopWithFlush_WritesAllOutstandingLines()
        {
            const int total = 500;

            ILog logger = new AsyncLog(this._tempDir);
            for (int i = 0; i < total; i++)
                logger.Write("Line " + i);
            logger.StopWithFlush();

            int written = LogFiles().Sum(f => DataLines(f).Count);
            Assert.Equal(total, written);
        } 
    }
}
