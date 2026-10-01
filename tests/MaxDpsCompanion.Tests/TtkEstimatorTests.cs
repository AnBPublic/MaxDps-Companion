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

    // ----- v3.6 provisional rate + fast-pack latch (§2 of the TTK spec) -----

    [Fact]
    public void Fast_Drop_Sets_Provisional_Rate()
    {
        // Band 14 -> 10 in 1.0 s: a >=3-band fall over >=0.5 s is provisional.
        var est = new TtkEstimator();
        est.Update(0, hasTarget: true, targetHpValid: true, targetHpBand: 14);
        var e = est.Update(1000, hasTarget: true, targetHpValid: true, targetHpBand: 10);

        Assert.False(e.Valid);
        Assert.True(e.Provisional);
        // frac=0.7, drop=0.2667 over 1.0 s -> 0.7 / 0.2667 ~= 2.63 s.
        Assert.InRange(e.TtkSec, 2.4, 2.8);
        Assert.False(e.FastPackLatch);
    }

    [Fact]
    public void Low_First_Sight_In_Combat_Seeds_Provisional()
    {
        var est = new TtkEstimator();
        var e = est.Update(0, hasTarget: true, targetHpValid: true, targetHpBand: 2, inCombat: true);

        Assert.False(e.Valid);
        Assert.True(e.Provisional);
        // Seed rate 1/15 per second -> frac * 15 = band + 0.5 ~= 2.5 s.
        Assert.InRange(e.TtkSec, 2.0, 2.6);
    }

    [Fact]
    public void Low_First_Sight_Out_Of_Combat_Does_Not_Seed()
    {
        var est = new TtkEstimator();
        var e = est.Update(0, hasTarget: true, targetHpValid: true, targetHpBand: 2, inCombat: false);
        Assert.False(e.Valid);
        Assert.False(e.Provisional);
    }

    [Fact]
    public void Two_Fast_Kills_Set_The_Fast_Pack_Latch()
    {
        var est = new TtkEstimator();
        est.Update(0, hasTarget: true, targetHpValid: true, targetHpBand: 3);
        est.Update(1000, hasTarget: false, targetHpValid: false, targetHpBand: 15);   // kill 1
        est.Update(1500, hasTarget: true, targetHpValid: true, targetHpBand: 3);
        est.Update(2500, hasTarget: false, targetHpValid: false, targetHpBand: 15);   // kill 2 -> latch

        Assert.True(est.FastPackLatch);
        // The latch survives the target swap and is exposed on the estimate.
        var e = est.Update(3000, hasTarget: true, targetHpValid: true, targetHpBand: 14);
        Assert.True(e.FastPackLatch);
    }

    [Fact]
    public void Latch_Clears_On_A_Long_High_Hp_Target()
    {
        var est = new TtkEstimator();
        est.Update(0, true, true, 3);
        est.Update(1000, false, false, 15);
        est.Update(1500, true, true, 3);
        est.Update(2500, false, false, 15);
        Assert.True(est.FastPackLatch);

        // Same target, high HP, alive past AgeSec>=12 -> the pack was not trash.
        est.Update(3000, true, true, 14);
        for (var t = 4000L; t <= 16000; t += 1000)
            est.Update(t, true, true, 14);

        Assert.True(est.Estimate.TargetHpFrac > 0.5);
        Assert.False(est.FastPackLatch);
    }

    [Fact]
    public void Latch_Clears_On_A_Valid_Long_Ttk()
    {
        var est = new TtkEstimator();
        est.Update(0, true, true, 3);
        est.Update(1000, false, false, 15);
        est.Update(1500, true, true, 3);
        est.Update(2500, false, false, 15);
        Assert.True(est.FastPackLatch);

        // A fresh target declining slowly enough to become valid with TTK >= 30 s.
        est.Update(2600, true, true, 14);
        est.Update(5100, true, true, 13);
        est.Update(7600, true, true, 12);

        Assert.True(est.Estimate.Valid);
        Assert.True(est.Estimate.TtkSec >= 30);
        Assert.False(est.Estimate.FastPackLatch);
    }

    [Fact]
    public void Early_Latch_Clear_Drops_The_Kill_Window()
    {
        var est = new TtkEstimator();
        est.Update(0, true, true, 3);
        est.Update(1000, false, false, 15);   // kill 1
        est.Update(1500, true, true, 3);
        est.Update(2500, false, false, 15);   // kill 2 -> latch
        Assert.True(est.FastPackLatch);

        // A long high-HP target clears the latch early (age >= 12 s, frac > 0.5).
        est.Update(3000, true, true, 14);
        for (var t = 4000L; t <= 16000; t += 1000)
            est.Update(t, true, true, 14);
        Assert.False(est.FastPackLatch);

        // One later fast kill must NOT re-latch: the stale kill window is gone.
        est.Update(16500, false, false, 15);
        est.Update(17000, true, true, 3);
        var after = est.Update(17500, false, false, 15);
        Assert.False(after.FastPackLatch);
        Assert.False(est.FastPackLatch);
    }

    [Fact]
    public void Provisional_And_Latch_Series_Is_Deterministic()
    {
        static TtkEstimate Run()
        {
            var est = new TtkEstimator();
            est.Update(0, true, true, 14);
            est.Update(1000, true, true, 10);
            est.Update(1500, false, false, 15);
            est.Update(2000, true, true, 3);
            est.Update(3000, false, false, 15);
            return est.Estimate;
        }

        Assert.Equal(Run(), Run());
    }
}
