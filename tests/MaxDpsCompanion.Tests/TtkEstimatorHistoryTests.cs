using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// v3.7 adaptive real-data TTK history (fake clock). Covers the kill filter, the
/// bounded rolling window (cap + age prune + idle clear), the nearest-rank
/// pessimistic quantile, the fail-open floor below MinKills, the live/history
/// blend weights, and the history-only provisional path.
/// </summary>
public class TtkEstimatorHistoryTests
{
    private static readonly TtkOptions Defaults = TtkOptions.Default;

    private static void Feed(TtkEstimator est, long ms, int band, bool inCombat = true) =>
        est.Update(ms, hasTarget: true, targetHpValid: true, targetHpBand: band, inCombat: inCombat);

    private static void Depart(TtkEstimator est, long ms, bool inCombat = true) =>
        est.Update(ms, hasTarget: false, targetHpValid: false, targetHpBand: -1, inCombat: inCombat);

    /// <summary>Drives one full target lifetime and a departure: band 14 -> band 2 over <paramref name="lifeSec"/>.</summary>
    private static void AddKill(TtkEstimator est, long startMs, double lifeSec)
    {
        Feed(est, startMs, 14);
        var end = startMs + (long)(lifeSec * 1000);
        Feed(est, end, 2);
        Depart(est, end + 100);
    }

    // ----- kill filter (RecordDeparture) -----

    [Fact]
    public void Kill_Filter_Drops_Short_Low_Delta_And_High_End_Fraction()
    {
        // life < 2 s -> dropped.
        var shortLife = new TtkEstimator(Defaults);
        Feed(shortLife, 0, 14);
        Feed(shortLife, 1000, 2);
        Depart(shortLife, 1100);
        Assert.Equal(0, shortLife.History.Count);

        // HP loss < 0.15 -> dropped (spawn-at-20% mobs).
        var tinyDelta = new TtkEstimator(Defaults);
        Feed(tinyDelta, 0, 14);
        Feed(tinyDelta, 2500, 13);
        Depart(tinyDelta, 2600);
        Assert.Equal(0, tinyDelta.History.Count);

        // departure above 0.25 frac -> not a kill -> dropped.
        var notDead = new TtkEstimator(Defaults);
        Feed(notDead, 0, 14);
        Feed(notDead, 2500, 5); // frac 0.3667 > 0.25
        Depart(notDead, 2600);
        Assert.Equal(0, notDead.History.Count);

        // A real kill (life >= 2 s, loss >= 0.15, end <= 0.25) is recorded.
        var real = new TtkEstimator(Defaults);
        Feed(real, 0, 14);
        Feed(real, 2500, 2); // frac 0.1667
        Depart(real, 2600);
        Assert.Equal(1, real.History.Count);
    }

    // ----- window cap / prune / clear (KillHistory is the unit) -----

    [Fact]
    public void Window_Cap_Evicts_Oldest_And_Prunes_By_Age()
    {
        var capped = new KillHistory(8, 240_000);
        for (var i = 0; i <= 8; i++) // 9 kills -> the 9th evicts the 1st
            capped.Add(new KillRecord(i * 1000, 2.0 + 0.5 * i, 0.9, 0.1));
        Assert.Equal(8, capped.Count);
        // Lives 2.5..6.0 survive; even count -> mean of the two middles (4.0, 4.5).
        Assert.Equal(4.25, capped.MedianLifeSec(), 3);

        // Add() prunes against the incoming record's timestamp.
        var pruned = new KillHistory(8, 240_000);
        pruned.Add(new KillRecord(0, 3, 0.9, 0.1));
        pruned.Add(new KillRecord(300_000, 3, 0.9, 0.1)); // 300 s > 240 s -> first dropped
        Assert.Equal(1, pruned.Count);

        // Prune(now) drops records older than now-maxAgeMs.
        var boundary = new KillHistory(8, 240_000);
        boundary.Add(new KillRecord(0, 3, 0.9, 0.1));
        boundary.Add(new KillRecord(1000, 3, 0.9, 0.1));
        boundary.Prune(241_000); // first is 241 s old -> dropped, second is exactly 240 s -> kept
        Assert.Equal(1, boundary.Count);

        // Empty window queries are inert.
        var empty = new KillHistory(8, 240_000);
        Assert.Equal(0, empty.RateQuantile(75));
        Assert.Equal(0, empty.MedianLifeSec());
    }

    [Fact]
    public void Window_Clears_After_90s_No_Target_Out_Of_Combat()
    {
        var est = new TtkEstimator(Defaults);
        AddKill(est, 0, 2.5);
        AddKill(est, 3000, 2.5);
        AddKill(est, 6000, 2.5);
        Assert.Equal(3, est.History.Count);

        // A no-target tick in combat does not arm the idle clear.
        Depart(est, 9000, inCombat: true);
        Assert.Equal(3, est.History.Count);

        // 90 s of no target + out of combat clears the window.
        Depart(est, 10_000, inCombat: false);
        Depart(est, 100_001, inCombat: false);
        Assert.Equal(0, est.History.Count);
    }

    // ----- nearest-rank pessimistic quantile -----

    [Fact]
    public void RateQuantile_Is_NearestRank_And_Pessimistic()
    {
        var h = new KillHistory(8, 240_000);
        h.Add(new KillRecord(0, 5, 0.9, 0.4));    // 0.10 frac/s
        h.Add(new KillRecord(1000, 4, 0.9, 0.4)); // 0.125
        h.Add(new KillRecord(2000, 2, 0.9, 0.4)); // 0.25
        h.Add(new KillRecord(3000, 1, 0.9, 0.4)); // 0.50

        // rank = ceil(q/100 * n); p75 of 4 -> rank 3 -> 0.25; p50 -> rank 2 -> 0.125.
        Assert.Equal(0.25, h.RateQuantile(75), 6);
        Assert.Equal(0.125, h.RateQuantile(50), 6);
        // p95 -> rank 4 -> the fastest observed rate, still a real sample.
        Assert.Equal(0.50, h.RateQuantile(95), 6);
    }

    // ----- below MinKills is fail-open and byte-identical to pre-history -----

    [Fact]
    public void Below_MinKills_Is_Not_Binding_And_Matches_PreHistory()
    {
        var withHistory = new TtkEstimator(Defaults); // MinKills = 3
        var noHistory = new TtkEstimator(new TtkOptions(History: false));

        void FeedBoth(long ms, int band) { Feed(withHistory, ms, band); Feed(noHistory, ms, band); }
        void DepartBoth(long ms) { Depart(withHistory, ms); Depart(noHistory, ms); }

        // Two kills (below the 3-kill floor) then a fresh target with a decline.
        FeedBoth(0, 14); FeedBoth(2500, 2); DepartBoth(2600);
        FeedBoth(3000, 14); FeedBoth(5500, 2); DepartBoth(5600);
        Assert.Equal(2, withHistory.History.Count);

        FeedBoth(6000, 14);
        FeedBoth(7000, 13);
        FeedBoth(8500, 12);

        Assert.False(withHistory.Estimate.HistBinding);
        // The whole per-tick estimate is identical to the history-off estimator.
        Assert.Equal(noHistory.Estimate, withHistory.Estimate);
    }

    [Fact]
    public void History_Off_Is_A_NoOp()
    {
        var off = new TtkEstimator(new TtkOptions(History: false));
        AddKill(off, 0, 2.5);
        AddKill(off, 3000, 2.5);
        AddKill(off, 6000, 2.5);
        Feed(off, 9000, 14);
        Feed(off, 10_000, 13);
        Assert.Equal(0, off.History.Count);
        Assert.False(off.Estimate.HistBinding);
        Assert.Equal(0, off.Estimate.HistTtkSec);
    }

    // ----- blend weights -----

    [Fact]
    public void Blend_Weights_History_At_25s_And_Live_At_85s()
    {
        var est = new TtkEstimator(Defaults);
        // Three fast kills: rate = 0.8 lost / 2.1 s life ~= 0.381 frac/s.
        AddKill(est, 0, 2.0);
        AddKill(est, 3000, 2.0);
        AddKill(est, 6000, 2.0);
        Assert.True(est.History.Count >= 3);

        // A new, much slower target: 1 band (0.0667 frac) per 1500 ms.
        const long t0 = 10_000;
        Feed(est, t0, 14);
        Feed(est, t0 + 1000, 13);
        Feed(est, t0 + 2500, 12); // span exactly 2.5 s -> w = 0
        var at25 = est.Estimate;
        Assert.True(at25.Valid);
        Assert.True(at25.HistBinding);
        Assert.False(at25.HistProvisional);
        // w=0 => rateEff = rPq (the fast history rate), so the blended TTK is
        // shorter than the slow live estimate.
        Assert.True(at25.HistTtkSec < at25.TtkSec,
            $"expected history to shorten {at25.HistTtkSec:0.###} < {at25.TtkSec:0.###}");

        Feed(est, t0 + 4000, 11);
        Feed(est, t0 + 5500, 10);
        Feed(est, t0 + 7000, 9);
        Feed(est, t0 + 8500, 8); // span exactly 8.5 s -> w = 1
        var at85 = est.Estimate;
        Assert.True(at85.Valid);
        Assert.True(at85.HistBinding);
        // w=1 => rateEff = live ewma, so the blended TTK equals the live TTK.
        Assert.Equal(at85.TtkSec, at85.HistTtkSec, 6);
    }

    // ----- history-only provisional path -----

    [Fact]
    public void Invalid_Live_With_Binding_Window_Marks_HistProvisional()
    {
        var est = new TtkEstimator(Defaults);
        AddKill(est, 0, 2.0);
        AddKill(est, 3000, 2.0);
        AddKill(est, 6000, 2.0);

        // Fresh target, a single early decline: no trusted live rate yet.
        Feed(est, 10_000, 14);
        Feed(est, 11_200, 12);
        var e = est.Estimate;

        Assert.False(e.Valid);
        Assert.True(e.HistBinding);
        Assert.True(e.HistProvisional);
        Assert.True(e.HistTtkSec > 0);
        Assert.Equal(3, e.HistKills);
    }

    // ----- heal-jump veto (must-fix: an upward jump is not a kill) -----

    [Fact]
    public void Heal_Jump_Is_Not_Recorded_As_A_Kill()
    {
        var est = new TtkEstimator(Defaults);
        AddKill(est, 0, 2.5);
        AddKill(est, 3000, 2.5);
        AddKill(est, 6000, 2.5);
        Assert.Equal(3, est.History.Count);

        // New target: high first sight, real decline to low HP, then a big heal
        // (>0.12 up). The pre-heal state looks exactly like a kill, so without
        // the heal-jump veto this would append a 4th record.
        Feed(est, 9000, 14);
        Feed(est, 10_500, 8);
        Feed(est, 12_000, 2);   // pre-heal low, life 3 s
        Feed(est, 12_100, 10);  // jump up 0.533 -> departure, NOT a kill
        Assert.Equal(3, est.History.Count);
    }

    // ----- flat / first-tick departures add no history -----

    [Fact]
    public void Flat_And_First_Tick_Departures_Add_No_History()
    {
        var est = new TtkEstimator(Defaults);

        // First (and only) tick of a low target, then it vanishes: no decline.
        Feed(est, 0, 2);
        Depart(est, 200);
        Assert.Equal(0, est.History.Count);

        // A target that never declines and departs at high HP is not a kill.
        Feed(est, 1000, 14);
        Feed(est, 4000, 14);
        Depart(est, 4100);
        Assert.Equal(0, est.History.Count);
    }

    // ----- estimator-level MaxAge prune -----

    [Fact]
    public void Estimator_Prunes_History_By_MaxAge()
    {
        // MaxAgeSec = 5 s, so the two oldest kills fall out of the window.
        var est = new TtkEstimator(new TtkOptions(
            History: true, Kills: 8, MinKills: 3, MaxAgeSec: 5, Quantile: 75, DurFactor: 0.5));
        AddKill(est, 0, 2.5);      // departure at 2.6 s
        AddKill(est, 1000, 2.5);   // departure at 3.6 s
        AddKill(est, 2000, 2.5);   // departure at 4.6 s
        Assert.Equal(3, est.History.Count);

        // A composed tick at 9 s prunes everything older than 4 s.
        Feed(est, 8000, 14);
        Feed(est, 9000, 13);
        Assert.Equal(1, est.History.Count);
    }

    // ----- median life + dur-factor propagation -----

    [Fact]
    public void Binding_Window_Exposes_MedianLife_And_DurFactor()
    {
        var est = new TtkEstimator(new TtkOptions(
            History: true, Kills: 8, MinKills: 3, MaxAgeSec: 240, Quantile: 75, DurFactor: 0.25));
        AddKill(est, 0, 2.5);
        AddKill(est, 3000, 2.5);
        AddKill(est, 6000, 2.5);
        Feed(est, 9000, 14);
        Feed(est, 10_200, 12);

        var e = est.Estimate;
        Assert.True(e.HistBinding);
        // The record's LifeSec spans first sight to departure: 2.5 s decline + 0.1 s.
        Assert.Equal(2.6, e.HistMedianLifeSec, 3);
        Assert.Equal(0.25, e.NeedDurFactor, 6);

        // WithTtk copies the dur-factor into the policy context.
        var ctx = new CombatContext().WithTtk(e);
        Assert.Equal(0.25, ctx.TtkHistDurFactor, 6);
    }
}
