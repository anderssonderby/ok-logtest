# Log Component — Solution Notes

## What the component does

`LogComponent` is a small DLL that writes strings to timestamped files **asynchronously**:
`ILog.Write` only enqueues, and a single background thread drains the queue to disk. It
rolls to a new file at midnight, supports two stop modes, and never lets a logging error
crash the host application.

## Architecture

Responsibilities were split so each type has one job (SRP) and dependencies are injected
(DIP), which also makes the file behaviour testable without touching `C:\LogTest`.

| Type | Responsibility |
|------|----------------|
| `ILog` | Public contract: `Write`, `StopWithFlush`, `StopWithoutFlush` (`: IDisposable`). |
| `AsyncLog : ILog` | Queueing, the consumer thread, stop semantics, error isolation. |
| `ILogSink` | Abstraction for *where* lines go (`: IDisposable`). |
| `RollingFileLogSink : ILogSink` | File naming, midnight rollover, header, flush, dispose. |
| `LogLine` | A single line + its formatting (unchanged). |

`AsyncLog` depends on `ILogSink` and `TimeProvider` (built into .NET 8). Production wiring
(`new AsyncLog()`) uses `RollingFileLogSink` + `TimeProvider.System`; tests inject a fake
sink and control timestamps, so the midnight test runs instantly instead of waiting for
real midnight.

## Bugs found and fixed

1. **Busy-wait at 100% CPU** — the old loop's `Thread.Sleep(50)` was *inside*
   `if (!_lines.IsEmpty)`, so an empty queue span the CPU. Replaced the
   `ConcurrentQueue` + polling with a `BlockingCollection<LogLine>`; the consumer now
   blocks while idle (`GetConsumingEnumerable`).
2. **Midnight rollover was wrong** — `(DateTime.Now - _curDate).Days != 0` measures *24h
   elapsed*, not a calendar-date change (23:59 → 00:01 has `.Days == 0`). Now compares
   `timestamp.Date` against the current file's date.
3. **Old file leaked on rollover** — the previous `StreamWriter` was never flushed or
   disposed. `RollingFileLogSink` now flushes + disposes before opening the next file.
4. **`StopWithFlush` didn't honour its contract** — it set a flag and returned
   immediately. It now calls `CompleteAdding()` then `Join()`, so it blocks until every
   queued line is written.
5. **Writer never disposed / no `IDisposable`** — the worker now disposes the sink on
   exit, and both `AsyncLog` and the sink implement `IDisposable`.
6. **No error isolation** (Demand #4) — each `sink.Write` is wrapped in try/catch so a
   failing write (or a bad sink) can never propagate into the calling application.
7. **Convoluted control flow** — removed the `processed < 5` batching and the
   `!_exit || _QuitWithFlush` tangle. One `_abort` flag distinguishes the two stop modes;
   stop is idempotent and guarded by a lock.
8. **Hardcoded `C:\LogTest`** — the directory is injectable (defaults to `C:\LogTest` so
   existing output is preserved).

## How the stop modes work

- **`StopWithFlush`** → `CompleteAdding()` + `Join()`. The queue drains fully, the sink is
  disposed, and only then does the call return.
- **`StopWithoutFlush`** → sets `_abort`, then `CompleteAdding()` + `Join()`. The consumer
  breaks out of its loop without writing the lines still queued, so it returns promptly.

## Unit tests (xUnit, `LogComponent.Tests`)

- `Write_then_StopWithFlush_persists_the_line` — proves a write reaches the sink (Demand 1).
- `StopWithFlush_writes_every_queued_line_before_returning` — proves the blocking-drain
  contract with a slow sink (Demand 3).
- `StopWithoutFlush_discards_outstanding_lines` — slow sink keeps lines queued; asserts
  fewer than N were written and that stop returned promptly (Demand 3).
- `Stopping_twice_is_a_safe_no_op` — idempotency.
- `Crossing_midnight_starts_a_new_file` + same-day/single-file tests — drive
  `RollingFileLogSink` with hand-built timestamps across midnight (Demand 2).

## Running

This machine has the SDK under `~/.dotnet`, so commands use that host:

```
~/.dotnet/dotnet.exe test                       # build + run all tests (7 passing)
~/.dotnet/dotnet.exe run --project Application   # produces the two log files in C:\LogTest
```

The application produces two files: one complete `0 → 14` file (stopped *with* flush) and
one `50 → …` file (stopped *without* flush).

> Note on the "without flush" file: it only visibly truncates if lines are still queued
> when stop is called. Because the demo's producer sleeps 20 ms between writes, a fast
> consumer can fully drain the queue first, in which case all 50 lines appear — which is
> still correct (`StopWithoutFlush` drops only *outstanding* lines). The discard behaviour
> is proven deterministically by `StopWithoutFlush_discards_outstanding_lines`.

## AI assistance

AI tooling (Claude Code) was used to analyse the original code, identify the bugs above,
discuss the SOLID redesign, and draft the implementation and tests. Design decisions
(concurrency primitive, test framework, architectural depth) were chosen explicitly rather
than accepted blindly; see the accompanying AI-session transcript.
