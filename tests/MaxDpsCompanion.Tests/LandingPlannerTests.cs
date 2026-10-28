using MaxDpsCompanion.Core.Scheduler;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 4.6.0 "Cadence" GCD-landing contract: the planner state machine (Idle ->
/// Armed -> Fired), the one-pre-fire-per-GCD latch, the abort on an identity or
/// verdict change, the GCD-length and latency EWMAs (seeds + clamps + the
/// convergence the feature depends on), the Ext5 bucket prediction with its
/// estimator fallback, the opener-release window and the adaptive-poll predicate.
///
/// Every time value is a fake clock passed in explicitly, so each scenario is a
/// pure sequence of (onGcd, Ext5, NowMs) observations - the same shape the
/// scheduler feeds it in production and in --replay.
/// </summary>
public class LandingPlannerTests
{
    private static readonly KeyStroke KeyE = new(0x45, false, false, false);
    private static readonly KeyStroke KeyR = new(0x52, false, false, false);

    /// <summary>Ext5 block with a given GCD bucket (0 unknown, 1 not on GCD, 2..15 = 100 ms bands).</summary>
    private static Ext5Block Block(int gcdBucket = 15, int rank = 3) => new(
        Rank: rank,
        Delve: 2,
        Interruptible: 2,
        CastKind: 2,
        CastAgeBucket: 5,
        CcKey: new KeyStroke(0x46, false, false, false),
        CcSpellId: 107570,
        GcdBucket: gcdBucket);

    private static LandingCandidate Candidate(int spellId = 100, KeyStroke? stroke = null) => new(
        Slot.Main,
        stroke ?? KeyE,
        spellId,
        PolicyVerdict.Use,
        ScheduleReason.MainRotation,
        Emergency: false);

    // ---- state machine / one pre-fire per GCD -----------------------------

    [Fact]
    public void Prefires_Once_At_The_Predicted_Edge_And_Never_Twice()
    {
        var planner = new LandingPlanner();
        var head = Candidate();
        // Estimator path (bucket 0): the observed rising edge at 100 plus the
        // 1500 ms seed puts the predicted end at 1600 and the seed lead (40 ms)
        // at 1560. A stable prediction is what makes the edge testable at all.
        var unknownGcd = Block(0);

        planner.Observe(onGcd: false, 0);
        planner.Observe(onGcd: true, 100);

        var armed = planner.Plan(head, onGcd: true, unknownGcd, nowMs: 100, enabled: true);
        Assert.Equal(LandingAction.Hold, armed.Action);
        Assert.Equal(LandingState.Armed, planner.State);
        Assert.Equal(1600, armed.PredictedGcdEndMs);
        Assert.Equal(LandingPlanner.SeedLatencyMs, armed.LeadMs);

        // The live Ext5 bucket wins while it is usable: bucket 15 is the
        // saturated 1300..1400 ms band and the planner predicts its UPPER edge.
        var ext5Armed = planner.Plan(head, onGcd: true, Block(15), nowMs: 1100, enabled: true);
        Assert.Equal(LandingAction.Hold, ext5Armed.Action);
        Assert.Equal(2500, ext5Armed.PredictedGcdEndMs);   // 1100 + 1400

        var early = planner.Plan(head, onGcd: true, unknownGcd, nowMs: 1559, enabled: true);
        Assert.Equal(LandingAction.Hold, early.Action);

        // Plan returns the INTENT: no latch is set and no latency sample armed
        // until the scheduler reports the stroke really went out (CommitFire).
        var intent = planner.Plan(head, onGcd: true, unknownGcd, nowMs: 1560, enabled: true);
        Assert.Equal(LandingAction.Fire, intent.Action);
        Assert.Equal(LandingState.Armed, planner.State);
        planner.CommitFire(1560);
        Assert.Equal(LandingState.Fired, planner.State);

        // Same GCD, still suggested: held, never a second pre-fire.
        var again = planner.Plan(head, onGcd: true, unknownGcd, nowMs: 1570, enabled: true);
        Assert.Equal(LandingAction.Hold, again.Action);
        Assert.Contains("already pre-fired", again.Reason);

        // A NEW GCD (rising edge) re-arms exactly one more pre-fire.
        planner.Observe(onGcd: false, 1700);
        planner.Observe(onGcd: true, 2000);
        var second = planner.Plan(head, onGcd: true, unknownGcd, nowMs: 3480, enabled: true);
        Assert.Equal(LandingAction.Fire, second.Action);
    }

    [Fact]
    public void Over_Off_Is_A_Pure_NoOp()
    {
        var planner = new LandingPlanner();

        var decision = planner.Plan(Candidate(), onGcd: true, Block(15), nowMs: 1000, enabled: false);

        Assert.Equal(LandingDecision.None, decision);
        Assert.Equal(LandingState.Idle, planner.State);
        Assert.Equal(0, planner.PredictedGcdEndMs);
    }

    // ---- abort on identity / verdict change -------------------------------

    [Fact]
    public void Aborts_When_The_Candidate_Identity_Changes_Then_Replans()
    {
        var planner = new LandingPlanner();
        var first = Candidate();
        var second = Candidate(stroke: KeyR);
        var unknownGcd = Block(0);

        planner.Observe(onGcd: true, 100);
        Assert.Equal(LandingAction.Hold, planner.Plan(first, true, unknownGcd, 100, true).Action);

        var abort = planner.Plan(second, true, unknownGcd, 200, true);
        Assert.Equal(LandingAction.Abort, abort.Action);
        Assert.Contains("candidate changed", abort.Reason);
        Assert.Equal(LandingState.Armed, planner.State);         // re-planned from Idle

        // The new identity is armed and fires at the SAME predicted edge (1600).
        var stillEarly = planner.Plan(second, true, unknownGcd, 1559, true);
        Assert.Equal(LandingAction.Hold, stillEarly.Action);
        var fired = planner.Plan(second, true, unknownGcd, 1560, true);
        Assert.Equal(LandingAction.Fire, fired.Action);
        planner.CommitFire(1560);
        Assert.Equal(LandingState.Fired, planner.State);
    }

    [Fact]
    public void Aborts_When_Only_The_Verdict_Changes()
    {
        var planner = new LandingPlanner();
        var use = Candidate();
        var skip = use with { Verdict = PolicyVerdict.Skip };

        planner.Observe(onGcd: true, 100);
        Assert.Equal(LandingAction.Hold, planner.Plan(use, true, Block(0), 100, true).Action);

        var abort = planner.Plan(skip, true, Block(0), 200, true);
        Assert.Equal(LandingAction.Abort, abort.Action);
        Assert.Contains("verdict changed", abort.Reason);
    }

    // ---- estimator: convergence + clamps ----------------------------------

    [Fact]
    public void Gcd_Estimator_Converges_From_Observed_Windows()
    {
        var planner = new LandingPlanner();
        // Eight 1000 ms on-GCD windows on a 1500 ms cycle.
        long t = 0;
        for (var i = 0; i < 8; i++)
        {
            planner.Observe(true, t);
            planner.Observe(false, t + 1000);
            t += 1500;
        }

        // alpha 0.2 from the 1500 ms seed towards 1000 ms: monotone, inside the
        // clamps, and clearly below the seed.
        Assert.True(planner.EstimatedGcdMs < LandingPlanner.SeedGcdMs,
            $"expected < {LandingPlanner.SeedGcdMs}, got {planner.EstimatedGcdMs}");
        Assert.True(planner.EstimatedGcdMs > 1000,
            $"expected > 1000, got {planner.EstimatedGcdMs}");
        Assert.InRange(planner.EstimatedGcdMs, LandingPlanner.MinGcdMs, LandingPlanner.MaxGcdMs);
    }

    [Fact]
    public void Gcd_Estimator_Clamps_Long_And_Short_Windows()
    {
        var longPlanner = new LandingPlanner();
        long t = 0;
        for (var i = 0; i < 30; i++)
        {
            longPlanner.Observe(true, t);
            longPlanner.Observe(false, t + 3000);
            t += 4000;
        }
        Assert.Equal(LandingPlanner.MaxGcdMs, longPlanner.EstimatedGcdMs);

        var shortPlanner = new LandingPlanner();
        t = 0;
        for (var i = 0; i < 30; i++)
        {
            shortPlanner.Observe(true, t);
            shortPlanner.Observe(false, t + 400);
            t += 1000;
        }
        Assert.Equal(LandingPlanner.MinGcdMs, shortPlanner.EstimatedGcdMs);
    }

    [Fact]
    public void Latency_Ewma_Samples_After_A_Committed_PreFire_And_Clamps()
    {
        // An edge far outside the plausible window is NOT credited to our press:
        // the seed lead survives, so a failed pre-fire cannot poison the lead.
        var stale = new LandingPlanner();
        stale.Observe(true, 0);
        Assert.True(stale.Plan(Candidate(), true, Block(1), 0, true).Firing);
        stale.CommitFire(0);
        stale.Observe(false, 500);                // the GCD fell (a length sample, not a latency one)
        stale.Observe(true, 3000);                // 3000 ms after the send: discarded
        Assert.Equal(LandingPlanner.SeedLatencyMs, stale.LatencyMs);

        // A plausible edge updates the EWMA (alpha 0.2 from the 40 ms seed).
        var planner = new LandingPlanner();
        planner.Observe(true, 0);
        Assert.True(planner.Plan(Candidate(), true, Block(1), 0, true).Firing);
        planner.CommitFire(0);
        planner.Observe(false, 20);               // the GCD fell: the press worked
        planner.Observe(true, 100);               // 100 ms send -> observed GCD
        Assert.Equal((int)Math.Round(0.2 * 100 + 0.8 * LandingPlanner.SeedLatencyMs), planner.LatencyMs);
        Assert.InRange(planner.LatencyMs, LandingPlanner.MinLatencyMs, LandingPlanner.MaxLatencyMs);

        // Floor: repeated 2 ms edges pull the lead down to the 10 ms clamp.
        var fast = new LandingPlanner();
        for (var i = 0; i < 20; i++)
        {
            var t = i * 100L;
            fast.Observe(true, t);
            Assert.True(fast.Plan(Candidate(), true, Block(1), t, true).Firing);
            fast.CommitFire(t);
            fast.Observe(false, t + 1);
            fast.Observe(true, t + 2);
        }
        Assert.Equal(LandingPlanner.MinLatencyMs, fast.LatencyMs);
    }

    // ---- plan vs commit (review m3) ---------------------------------------

    [Fact]
    public void Fire_Is_An_Intent_And_Only_CommitFire_Latches_It()
    {
        var planner = new LandingPlanner();
        var head = Candidate();
        var unknownGcd = Block(0);

        planner.Observe(onGcd: true, 100);

        // At the predicted edge the plan says Fire - and changes no state.
        var fired = planner.Plan(head, true, unknownGcd, 1560, true);
        Assert.Equal(LandingAction.Fire, fired.Action);
        Assert.Equal(LandingState.Armed, planner.State);

        // A held press re-plans to the SAME intent instead of latching: the
        // scheduler never committed it.
        var again = planner.Plan(head, true, unknownGcd, 1570, true);
        Assert.Equal(LandingAction.Fire, again.Action);
        Assert.Equal(LandingState.Armed, planner.State);

        // Only the commit spends the GCD's single pre-fire.
        planner.CommitFire(1570);
        Assert.Equal(LandingState.Fired, planner.State);
        var latched = planner.Plan(head, true, unknownGcd, 1580, true);
        Assert.Equal(LandingAction.Hold, latched.Action);
        Assert.Contains("already pre-fired", latched.Reason);
    }

    [Fact]
    public void A_Held_PreFire_Never_Feeds_The_Latency_Ewma()
    {
        var planner = new LandingPlanner();
        planner.Observe(true, 0);

        // Plan says Fire but nothing commits (the scheduler's send gates held
        // the stroke): the observed rising edge is NOT a latency sample.
        Assert.True(planner.Plan(Candidate(), true, Block(1), 0, true).Firing);
        planner.Observe(false, 20);
        planner.Observe(true, 100);
        Assert.Equal(LandingPlanner.SeedLatencyMs, planner.LatencyMs);
    }

    // ---- prediction: Ext5 bucket + estimator fallback ----------------------

    [Fact]
    public void Ext5_Unknown_Falls_Back_To_The_Estimator()
    {
        var planner = new LandingPlanner();
        planner.Observe(true, 1000);

        // Bucket 0 = UNKNOWN: the observed rising edge (1000) + the 1500 ms seed.
        Assert.Equal(2500, planner.PredictEnd(Block(0), nowMs: 1100));
        // No Ext5 at all behaves identically (pre-Ext5 addon).
        Assert.Equal(2500, planner.PredictEnd(null, nowMs: 1100));
        // Bucket 1 = "not on GCD": the GCD ends now.
        Assert.Equal(1100, planner.PredictEnd(Block(1), nowMs: 1100));
        // Bucket 2 = 0..100 ms remaining: the bucket's upper edge (100).
        Assert.Equal(1200, planner.PredictEnd(Block(2), nowMs: 1100));
        // Bucket 15 saturates at 1300 ms minimum: upper edge 1400.
        Assert.Equal(2500, planner.PredictEnd(Block(15), nowMs: 1100));
    }

    [Fact]
    public void Predict_Without_Any_Observation_Is_Unknown()
    {
        var planner = new LandingPlanner();
        Assert.Equal(0, planner.PredictEnd(Block(0), nowMs: 500));
        Assert.Equal(0, planner.PredictEnd(null, nowMs: 500));
    }

    // ---- opener release window -------------------------------------------

    [Fact]
    public void Opener_Window_Opens_On_A_Boss_Grade_Pull_And_Expires()
    {
        var opener = new OpenerWindow();

        Assert.False(opener.Observe(inCombat: false, Block(rank: 6), enabled: true, 0));
        Assert.True(opener.Observe(inCombat: true, Block(rank: 6), enabled: true, 100));
        Assert.True(opener.OpenedThisFrame);
        Assert.True(opener.Active(100 + OpenerWindow.WindowMs - 1));
        Assert.False(opener.Active(100 + OpenerWindow.WindowMs));

        // Rank 7 (elite-skull) is the second boss grade.
        var eliteSkull = new OpenerWindow();
        eliteSkull.Observe(false, Block(rank: 7), true, 0);
        Assert.True(eliteSkull.Observe(true, Block(rank: 7), true, 100));
    }

    [Theory]
    [InlineData(0)]   // UNKNOWN
    [InlineData(1)]   // trivial
    [InlineData(2)]   // normal
    [InlineData(3)]   // elite
    [InlineData(4)]   // rare
    [InlineData(5)]   // rareelite
    public void Opener_Window_Never_Opens_Below_Boss_Grade(int rank)
    {
        var opener = new OpenerWindow();
        opener.Observe(inCombat: false, Block(rank: rank), enabled: true, 0);

        Assert.False(opener.Observe(inCombat: true, Block(rank: rank), enabled: true, 100));
        Assert.False(opener.OpenedThisFrame);
        Assert.False(opener.Active(200));
    }

    [Fact]
    public void Opener_Window_Is_Inert_When_Disabled_Or_Mid_Fight()
    {
        // Toggle off: a boss pull is today's behaviour.
        var off = new OpenerWindow();
        off.Observe(false, Block(rank: 6), enabled: false, 0);
        Assert.False(off.Observe(true, Block(rank: 6), enabled: false, 100));

        // A session that starts mid-fight never saw the pull: no window.
        var midFight = new OpenerWindow();
        Assert.False(midFight.Observe(true, Block(rank: 6), enabled: true, 100));
        Assert.False(midFight.OpenedThisFrame);

        // Combat ending closes the window immediately.
        var closed = new OpenerWindow();
        closed.Observe(false, Block(rank: 6), true, 0);
        Assert.True(closed.Observe(true, Block(rank: 6), true, 100));
        Assert.False(closed.Observe(false, null, true, 200));
        Assert.False(closed.Active(300));
    }

    // ---- adaptive polling predicate ---------------------------------------

    [Theory]
    [InlineData(-50, true)]    // exactly at the leading edge of the window
    [InlineData(0, true)]      // predicted end is now
    [InlineData(300, true)]    // exactly at the trailing edge
    [InlineData(-51, false)]   // too early
    [InlineData(301, false)]   // too late
    public void Adaptive_Wait_Bursts_Only_Inside_The_Gcd_Edge_Window(int deltaMs, bool bursts)
    {
        const int configured = 33;
        var wait = CadencePacing.AdaptiveWait(
            configured, adaptive: true, idle: false, predictedGcdEndMs: 10_000 + deltaMs, nowMs: 10_000);

        Assert.Equal(bursts ? CadencePacing.BurstMs : configured, wait);
    }

    [Fact]
    public void Adaptive_Wait_Keeps_Idle_Pacing_And_Off_Config_Authoritative()
    {
        // Idle pacing wins even inside the window: no target, no GCD to chase.
        Assert.Equal(132, CadencePacing.AdaptiveWait(132, adaptive: true, idle: true, 10_000, 9_900));
        // Toggle off: the configured wait is untouched.
        Assert.Equal(33, CadencePacing.AdaptiveWait(33, adaptive: false, idle: false, 10_000, 10_000));
        // Unknown prediction: nothing to burst for.
        Assert.Equal(33, CadencePacing.AdaptiveWait(33, adaptive: true, idle: false, 0, 10_000));
        // The burst never lengthens a wait that is already shorter.
        Assert.Equal(8, CadencePacing.AdaptiveWait(8, adaptive: true, idle: false, 10_000, 10_000));
    }

    // ---- KickMemory contract ---------------------------------------------

    [Fact]
    public void KickMemory_Holds_The_First_Suppression_Rung_And_Resets()
    {
        // Per-scheduler instance state (review M2): a local instance, no shared
        // static, so this test can never race another scheduler test.
        var kick = new KickMemory();

        Assert.False(kick.Failed(1000));
        kick.NoteFailure(1000);
        Assert.True(kick.Failed(1000));
        Assert.True(kick.Failed(1000 + (int)KickMemory.FailedWindowMs - 1));
        Assert.False(kick.Failed(1000 + (int)KickMemory.FailedWindowMs));
        Assert.Equal(1000 + KickMemory.FailedWindowMs, kick.FailedUntilMs);

        kick.Reset();
        Assert.False(kick.Failed(1001));
        Assert.Equal(long.MinValue, kick.FailedUntilMs);
    }
}
