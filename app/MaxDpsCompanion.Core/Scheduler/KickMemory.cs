// Cadence 4.6.0: kick-failure memory for interrupt insurance.
//
// The scheduler owns the write side (a rejected interrupt stroke marks the
// kick failed for the first suppression rung); the insurance predicate owns
// the read side and receives the answer as a bool. It is INSTANCE state, owned
// by one ActionScheduler: the write and the read always live in the same
// scheduler, one engine owns one scheduler, and a second scheduler (a replay,
// a test, a future second engine) must never inherit or clear the first one's
// ladder. The 4.6.0 review fixed this: it used to be a process-wide static, and
// the test suite could only be kept green by serialising every scheduler class
// behind one xunit collection - an incomplete fence around a real data race.
//
// Deterministic: every timestamp comes from the engine's NowMs, never a wall
// clock, so --replay reproduces the same verdicts.

namespace MaxDpsCompanion.Core.Scheduler;

/// <summary>
/// One scheduler's "the last kick failed" memory: the first suppression rung of
/// the failure ladder, read by <see cref="MaxDpsCompanion.Core.Knowledge.InterruptInsurance"/>
/// through the scheduler that owns it.
/// </summary>
internal sealed class KickMemory
{
    /// <summary>First suppression rung of the scheduler's failure ladder.</summary>
    internal const long FailedWindowMs = 1500;

    private long _failedUntilMs = long.MinValue;

    /// <summary>Marks the interrupt slot failed as of <paramref name="nowMs"/>.</summary>
    internal void NoteFailure(long nowMs) =>
        _failedUntilMs = nowMs + FailedWindowMs;

    /// <summary>True while a failed kick is still inside its suppression window.</summary>
    internal bool Failed(long nowMs) => nowMs < _failedUntilMs;

    /// <summary>Target change / out of combat / explicit reset.</summary>
    internal void Reset() => _failedUntilMs = long.MinValue;

    /// <summary>Test seam: reads the raw deadline without mutating it.</summary>
    internal long FailedUntilMs => _failedUntilMs;
}
