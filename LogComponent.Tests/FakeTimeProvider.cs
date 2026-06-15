namespace LogComponent.Tests
{
    /// <summary>
    /// Minimal controllable <see cref="TimeProvider"/> for tests. Holds a fixed UTC
    /// instant that the test can move forward explicitly via <see cref="Advance"/> or
    /// <see cref="SetUtcNow"/>.
    /// </summary>
    internal sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public FakeTimeProvider(DateTimeOffset start)
        {
            this._utcNow = start;
        }

        public override DateTimeOffset GetUtcNow() => this._utcNow;

        public void Advance(TimeSpan delta) => this._utcNow = this._utcNow.Add(delta);

        public void SetUtcNow(DateTimeOffset utcNow) => this._utcNow = utcNow;
    }
}
