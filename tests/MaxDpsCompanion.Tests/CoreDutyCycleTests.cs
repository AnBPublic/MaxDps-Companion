using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 4.1 A2.8 parity: CORE's engine loop still used the pre-4.1 duty-cycle guard
/// (<c>dutyFloorMs &lt;= 250</c> — a hard 250 ms ceiling that silently dropped
/// the stretch for any tick slower than ~25 ms, and stretched a 20 ms poll to
/// 250 ms otherwise). The Windows engine got the capped-stretch form in 4.1
/// (<c>stretch = dutyFloorMs - wait; wait += Math.Min(stretch, Math.Max(0, fast))</c>);
/// Core now calls the same formula through <see cref="RotationEngine.DutyCycleWait"/>.
///
/// The loop thread itself is not drivable from a unit test (it owns the real
/// Stopwatch + IClock cadence), so the extracted pure formula carries the
/// assertions: the stretch stays capped at ONE configured poll, never at 250 ms
/// and never at 10x a slow tick.
/// </summary>
public class CoreDutyCycleTests
{
    /// <summary>A 20 ms poll interval is the shipped default.</summary>
    private const int Fast = 20;

    [Fact]
    public void A_Slow_Tick_Stretches_By_At_Most_One_Poll_Interval()
    {
        // 100 ms tick ⇒ the 10%-CPU floor is 1000 ms. The OLD guard dropped
        // that stretch entirely (floor > 250) and kept the 20 ms cadence; the
        // new one adds one poll, so the wait is 40 ms — bounded latency with
        // the guard still throttling a runaway tick.
        Assert.Equal(40, RotationEngine.DutyCycleWait(wait: 20, Fast, tickUs: 100_000));
        Assert.NotEqual(250, RotationEngine.DutyCycleWait(wait: 20, Fast, tickUs: 100_000));
    }

    [Fact]
    public void The_Cap_Tracks_The_Configured_Poll_Not_A_Fixed_Ceiling()
    {
        // 10 ms cadence, 50 ms poll: the cap is one POLL (50 ms), so a slow
        // tick may add 50 → 60 ms. The retired 250 ms ceiling would have let a
        // merely-fast tick (already covered by the cap) add far more.
        Assert.Equal(60, RotationEngine.DutyCycleWait(wait: 10, fast: 50, tickUs: 100_000));
    }

    [Fact]
    public void A_Moderate_Tick_Adds_Only_The_Missing_Milliseconds()
    {
        // 3 ms tick ⇒ floor 30 ms; wait 20 ⇒ stretch 10 ⇒ 30 ms (the capped
        // form does not overshoot past the duty floor when it fits under fast).
        Assert.Equal(30, RotationEngine.DutyCycleWait(wait: 20, Fast, tickUs: 3_000));
    }

    [Fact]
    public void A_Cheap_Tick_Leaves_The_Wait_Untouched()
    {
        // 1 ms tick ⇒ floor 10 ms is below the wait: no stretch, and the value
        // never shrinks (a negative stretch must not speed the cadence up).
        Assert.Equal(20, RotationEngine.DutyCycleWait(wait: 20, Fast, tickUs: 1_000));
    }

    [Fact]
    public void A_Zero_Tick_Is_Ignored()
    {
        // Stopwatch deltas can round to 0; the old guard was inside `if (tickUs > 0)`.
        Assert.Equal(120, RotationEngine.DutyCycleWait(wait: 120, Fast, tickUs: 0));
    }
}
