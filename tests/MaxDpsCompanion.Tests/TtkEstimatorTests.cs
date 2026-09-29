using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// A1: the pure per-target TTK estimator (fake clock). Every input is a decoded
/// frame field; the estimator never reads a game value. These tests are the
/// frozen contract for the §3.1 behaviour: steady decline converges, target
/// switch / heal resets, no-target and sustained-unknown reset, coarse-band
/// staircase still converges, noise is ignored, trickle clamps to 300 s, and the
/// same series always yields the same output.
/// </summary>
public class TtkEstimatorTests
{
    private const double MaxTtk = 300.0;

    /// <summary>Band 0..14 for a target HP fraction (band top+bottom midpoint).</summary>
    private static int Band(double frac)
    {
        var band = (int)Math.Floor(frac * 15.0);
        return Math.Clamp(band, 0, 14);
    }

    private static int BandForPct(double pct) => Band(pct / 100.0);

    /// <summary>Drives a linear HP decline at <paramref name="ttkSec"/> true TTK from <paramref name="startPct"/>.</summary>
    private static TtkEstimator FeedDecline(
        double ttkSec, double startPct, double stepSec, double stopSec, out double lastFrac)
    {
        var est = new TtkEstimator();
        TtkEstimate sample = default;
        lastFrac = 0;
        for (var t = 0.0; t <= stopSec + 1e-9; t += stepSec)
        {
            var frac = startPct / 100.0 - t / ttkSec;
            if (frac <= 0.0) break;
            lastFrac = frac;
            sample = est.Update((long)Math.Round(t * 1000.0), hasTarget: true, targetHpValid: true, targetHpBand: Band(frac));
        }
        _ = sample;
        return est;
    }

    [Fact]
    public void Steady_Decline_Converges_To_True_Ttk()
    {
        // True rate = 1/20 frac/s. At t=8s the remaining TTK is ~12s.
        var est = FeedDecline(ttkSec: 20, startPct: 96.6667, stepSec: 0.5, stopSec: 8, out var lastFrac);
        var e = est.Estimate;
        Assert.True(e.Valid);
        var trueRemaining = lastFrac / (1.0 / 20.0);
        Assert.InRange(e.TtkSec, trueRemaining * 0.8, trueRemaining * 1.2);
    }

    [Fact]
    public void Target_Switch_Jump_Up_Resets_To_Invalid()
    {
        var est = FeedDecline(ttkSec: 20, startPct: 96.6667, stepSec: 0.5, stopSec: 6, out _);
        Assert.True(est.Estimate.Valid);
        var before = est.Epoch;
        // New target at full HP: frac jumps up by ~0.2.
        var after = est.Update(6500, hasTarget: true, targetHpValid: true, targetHpBand: 14);
        Assert.False(after.Valid);
        Assert.True(est.Epoch > before);
    }

    [Fact]
    public void Heal_Jump_Up_Resets_To_Invalid()
    {
        var est = FeedDecline(ttkSec: 20, startPct: 96.6667, stepSec: 0.5, stopSec: 6, out var lastFrac);
        var before = est.Epoch;
        // Heal of ~+0.15 frac.
        var healedFrac = lastFrac + 0.15;
        var after = est.Update(6500, hasTarget: true, targetHpValid: true, targetHpBand: Band(healedFrac));
        Assert.False(after.Valid);
        Assert.True(est.Epoch > before);
    }

    [Fact]
    public void No_Target_Resets_To_Invalid()
    {
        var est = FeedDecline(ttkSec: 20, startPct: 96.6667, stepSec: 0.5, stopSec: 6, out _);
        Assert.True(est.Estimate.Valid);
        var after = est.Update(6500, hasTarget: false, targetHpValid: false, targetHpBand: 15);
        Assert.False(after.Valid);
        Assert.Equal(MaxTtk, after.TtkSec);
    }

    [Fact]
    public void Unknown_For_Over_Ten_Seconds_Resets_To_Invalid()
    {
        var est = FeedDecline(ttkSec: 20, startPct: 96.6667, stepSec: 0.5, stopSec: 6, out _);
        var before = est.Epoch;
        // Unknown band 15 for 11 s (0.5 s steps).
        for (var t = 6.5; t <= 17.5 + 1e-9; t += 0.5)
            est.Update((long)Math.Round(t * 1000.0), hasTarget: true, targetHpValid: false, targetHpBand: 15);
        Assert.False(est.Estimate.Valid);
        Assert.True(est.Epoch > before);
    }

    [Fact]
    public void Short_Unknown_Keeps_Rate_But_Reports_Invalid()
    {
        var est = FeedDecline(ttkSec: 20, startPct: 96.6667, stepSec: 0.5, stopSec: 6, out _);
        var before = est.Epoch;
        // A 1 s secret flicker must not reset the learned rate.
        est.Update(6500, hasTarget: true, targetHpValid: false, targetHpBand: 15);
        est.Update(7000, hasTarget: true, targetHpValid: false, targetHpBand: 15);
        Assert.False(est.Estimate.Valid);
        Assert.Equal(before, est.Epoch);
    }

    [Fact]
    public void Coarse_Band_Staircase_Still_Converges()
    {
        // 30 s true TTK, sampled only every 1 s: bands step in coarse 6.7% chunks.
        var est = FeedDecline(ttkSec: 30, startPct: 96.6667, stepSec: 1.0, stopSec: 12, out var lastFrac);
        var e = est.Estimate;
        Assert.True(e.Valid);
        var trueRemaining = lastFrac / (1.0 / 30.0);
        Assert.InRange(e.TtkSec, trueRemaining * 0.7, trueRemaining * 1.3);
    }

    [Fact]
    public void One_Tick_Noise_Is_Ignored()
    {
        var est = FeedDecline(ttkSec: 20, startPct: 96.6667, stepSec: 0.5, stopSec: 6, out var lastFrac);
        var before = est.Estimate;
        // A single tick with a tiny heal must not change the estimate.
        est.Update(6500, hasTarget: true, targetHpValid: true, targetHpBand: Band(lastFrac + 0.01));
        Assert.True(est.Estimate.Valid);
        Assert.Equal(before.TtkSec, est.Estimate.TtkSec, 3);
    }

    [Fact]
    public void Trickle_Decline_Clamps_To_300()
    {
        // 0.002 frac/s is below the 0.004 noise floor: never fed, stays invalid/300.
        var est = new TtkEstimator();
        TtkEstimate e = default;
        for (var t = 0.0; t <= 5.0 + 1e-9; t += 0.5)
        {
            var frac = 0.9 - 0.002 * t;
            e = est.Update((long)Math.Round(t * 1000.0), hasTarget: true, targetHpValid: true, targetHpBand: Band(frac));
        }
        Assert.False(e.Valid);
        Assert.Equal(MaxTtk, e.TtkSec);
    }

    [Fact]
    public void Same_Series_Yields_Identical_Output()
    {
        var a = FeedDecline(ttkSec: 20, startPct: 96.6667, stepSec: 0.5, stopSec: 8, out _).Estimate;
        var b = FeedDecline(ttkSec: 20, startPct: 96.6667, stepSec: 0.5, stopSec: 8, out _).Estimate;
        Assert.Equal(a, b);
    }

    [Fact]
    public void Band_Roundtrips_From_Decoded_Percent()
    {
        for (var band = 0; band <= 14; band++)
        {
            var pct = (int)Math.Round(band * 100.0 / 15.0);
            Assert.Equal(band, TtkEstimator.BandFromPercent(pct));
        }
        Assert.Equal(-1, TtkEstimator.BandFromPercent(-1));
    }

    [Fact]
    public void Reset_Clears_State()
    {
        var est = FeedDecline(ttkSec: 20, startPct: 96.6667, stepSec: 0.5, stopSec: 6, out _);
        Assert.True(est.Estimate.Valid);
        est.Reset();
        Assert.False(est.Estimate.Valid);
        Assert.Equal(MaxTtk, est.Estimate.TtkSec);
    }
}
