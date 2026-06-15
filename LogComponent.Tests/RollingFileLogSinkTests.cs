using LogComponent;

namespace LogComponent.Tests
{
    public class RollingFileLogSinkTests : IDisposable
    {
        private readonly string _dir;

        public RollingFileLogSinkTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "LogTest_" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }

        private static LogLine Line(string text, DateTime timestamp) =>
            new() { Text = text, Timestamp = timestamp };

        [Fact]
        public void Write_creates_a_file_containing_the_text()
        {
            using (var sink = new RollingFileLogSink(_dir))
            {
                sink.Write(Line("hello world", new DateTime(2026, 6, 15, 10, 0, 0)));
            }

            string file = Assert.Single(Directory.GetFiles(_dir, "*.log"));
            Assert.Contains("hello world", File.ReadAllText(file));
        }

        // Demand: "If we cross midnight a new file with new timestamp must be made."
        [Fact]
        public void Crossing_midnight_starts_a_new_file()
        {
            using (var sink = new RollingFileLogSink(_dir))
            {
                sink.Write(Line("before midnight", new DateTime(2026, 6, 15, 23, 59, 59)));
                sink.Write(Line("after midnight", new DateTime(2026, 6, 16, 0, 0, 1)));
            }

            string[] files = Directory.GetFiles(_dir, "*.log");
            Assert.Equal(2, files.Length);

            string allContent = string.Concat(files.Select(File.ReadAllText));
            Assert.Contains("before midnight", allContent);
            Assert.Contains("after midnight", allContent);

            // Each line lands in its own (separate) file.
            Assert.Single(files.Where(f => File.ReadAllText(f).Contains("before midnight")));
            Assert.Single(files.Where(f => File.ReadAllText(f).Contains("after midnight")));
        }

        [Fact]
        public void Lines_on_the_same_day_stay_in_one_file()
        {
            using (var sink = new RollingFileLogSink(_dir))
            {
                sink.Write(Line("morning", new DateTime(2026, 6, 15, 8, 0, 0)));
                sink.Write(Line("evening", new DateTime(2026, 6, 15, 20, 0, 0)));
            }

            Assert.Single(Directory.GetFiles(_dir, "*.log"));
        }
    }
}
