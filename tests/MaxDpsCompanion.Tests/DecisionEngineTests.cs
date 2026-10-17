using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Deterministic decision-layer contract. These tests pin the fallback path to
/// the exact legacy priority order (the "intelligence disabled" behaviour must
/// remain byte-identical to the pre-v1.4.0 engine) and cover the enabled rules:
/// interrupt/defensive urgency, duplicate collapse, stale demotion and the GCD
/// temporary state.
/// </summary>
public class DecisionEngineTests
{
    private const long Now = 10_000;
    private const long StaleAfter = 1_500;

    private static KeyStroke Key(byte vk) => new(vk, false, false, false);

    /// <summary>Default strokes differ per slot so tests never collide by accident.</summary>
    private static ActionCandidate Candidate(
        Slot slot,
        KeyStroke? stroke = null,
        bool enabled = true,
        bool actionable = true,
        long firstSeenMs = 0,
        long lastChangedMs = 0,
        bool everPressed = false) =>
        new(slot, stroke ?? Key((byte)(0x41 + (int)slot)), enabled, actionable,
            firstSeenMs, lastChangedMs, LastPressedMs: 0, everPressed);

    private static DecisionContext Context(bool onGcd, params ActionCandidate[] candidates) => new()
    {
        InCombat = true,
        OnGcd = onGcd,
        HasTarget = true,
        State = BridgeState.Active,
        NowMs = Now,
        StaleAfterMs = StaleAfter,
        Candidates = candidates,
    };

    private static DecisionContext Context(params ActionCandidate[] candidates) => Context(false, candidates);

    // ---------- fallback: legacy behaviour unchanged ----------

    [Fact]
    public void Fallback_Is_Exact_Legacy_Priority_Order()
    {
        var fallback = DecisionResult.Fallback();

        Assert.Equal(
            [Slot.Main, Slot.Offensive, Slot.Interrupt, Slot.Defensive, Slot.Consumable, Slot.Trinket],
            fallback.Order);
        Assert.Equal(DecisionReason.Fallback, fallback.Reason);
        Assert.Null(fallback.Selected);
        Assert.False(fallback.DemotedStale);
    }

    [Fact]
    public void EnabledOrder_Inserts_Companion_Slots_Without_Reordering_Fallback()
    {
        // The two companion-only slots (SelfHeal, Mobility) are reachable on
        // the legacy path too — otherwise Solo mode would silently lose its
        // self-sustain whenever [Scheduler] Enabled=0. The six MaxDps slots
        // keep their original relative order; SelfHeal sits before Main so a
        // policy-USE heal can preempt damage, Mobility after Defensive.
        Assert.Equal(
            [Slot.Interrupt, Slot.SelfHeal, Slot.Main, Slot.Defensive, Slot.Mobility,
             Slot.Offensive, Slot.Consumable, Slot.Trinket],
            DecisionEngine.EnabledOrder);

        // The pre-v2.2 enabled-order contract for the six MaxDps slots is
        // preserved: Interrupt < Main < Defensive < Offensive < Consumable <
        // Trinket positions remain ascending — insertion only.
        var priorEnabled = new[]
        {
            Slot.Interrupt, Slot.Main, Slot.Defensive, Slot.Offensive, Slot.Consumable, Slot.Trinket,
        };
        var positions = priorEnabled.Select(slot => Array.IndexOf(DecisionEngine.EnabledOrder, slot)).ToArray();
        Assert.Equal(positions.OrderBy(position => position), positions);
    }

    [Fact]
    public void Evaluate_SelfHeal_Outranks_Main_When_Enabled()
    {
        var result = DecisionEngine.Evaluate(Context(Candidate(Slot.Main), Candidate(Slot.SelfHeal)));

        Assert.Equal([Slot.SelfHeal, Slot.Main], result.Order);
        Assert.Equal(DecisionReason.SelfSustain, result.Reason);
        Assert.Equal(75, result.Confidence);
    }

    [Fact]
    public void Evaluate_Mobility_Outranks_Offensive()
    {
        var result = DecisionEngine.Evaluate(Context(Candidate(Slot.Offensive), Candidate(Slot.Mobility)));

        Assert.Equal([Slot.Mobility, Slot.Offensive], result.Order);
        Assert.Equal(DecisionReason.MobilityUplift, result.Reason);
    }

    // ---------- no candidates / validity ----------

    [Fact]
    public void Evaluate_NoCandidates_Returns_NoCandidate()
    {
        var result = DecisionEngine.Evaluate(Context());

        Assert.Empty(result.Order);
        Assert.Null(result.Selected);
        Assert.Equal(DecisionReason.NoCandidate, result.Reason);
        Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public void Evaluate_Excludes_Disabled_Slots()
    {
        var result = DecisionEngine.Evaluate(Context(Candidate(Slot.Main, enabled: false)));

        Assert.Empty(result.Order);
        Assert.Equal(DecisionReason.NoCandidate, result.Reason);
    }

    // ---------- interrupt / defensive urgency ----------

    [Fact]
    public void Evaluate_Interrupt_Preempts_Main()
    {
        var result = DecisionEngine.Evaluate(Context(Candidate(Slot.Main), Candidate(Slot.Interrupt)));

        Assert.Equal(Slot.Interrupt, result.Order[0]);
        Assert.Equal(Slot.Main, result.Order[1]);
        Assert.Equal(DecisionReason.InterruptUrgency, result.Reason);
        Assert.True(result.Confidence >= 90);
    }

    [Fact]
    public void Evaluate_Defensive_Preempts_Offensive()
    {
        var result = DecisionEngine.Evaluate(Context(Candidate(Slot.Offensive), Candidate(Slot.Defensive)));

        Assert.Equal([Slot.Defensive, Slot.Offensive], result.Order);
        Assert.Equal(DecisionReason.DefensiveUrgency, result.Reason);
    }

    [Fact]
    public void Evaluate_EnabledOrder_Is_Independent_Of_Input_Order()
    {
        var result = DecisionEngine.Evaluate(Context(
            Candidate(Slot.Trinket),
            Candidate(Slot.Consumable),
            Candidate(Slot.Offensive),
            Candidate(Slot.Defensive),
            Candidate(Slot.Main),
            Candidate(Slot.Interrupt)));

        Assert.Equal(
            [Slot.Interrupt, Slot.Main, Slot.Defensive, Slot.Offensive, Slot.Consumable, Slot.Trinket],
            result.Order);
    }

    // ---------- duplicate / conflicting suggestions ----------

    [Fact]
    public void Evaluate_Collapses_Duplicate_Strokes_Keeping_Highest_Rank()
    {
        var stroke = Key(0x52); // 'R' bound to two category slots
        var result = DecisionEngine.Evaluate(Context(
            Candidate(Slot.Main, stroke),
            Candidate(Slot.Offensive, stroke)));

        Assert.Equal([Slot.Main], result.Order);
        Assert.Equal(DecisionReason.MainRotation, result.Reason);
    }

    [Fact]
    public void Evaluate_Collapses_Duplicate_Interrupt_Onto_Interrupt()
    {
        var stroke = Key(0x52);
        var result = DecisionEngine.Evaluate(Context(
            Candidate(Slot.Main, stroke),
            Candidate(Slot.Interrupt, stroke)));

        Assert.Equal([Slot.Interrupt], result.Order);
        Assert.Equal(DecisionReason.InterruptUrgency, result.Reason);
    }

    // ---------- stale suggestion detection ----------

    [Fact]
    public void Evaluate_Demotes_Stale_Candidate_Behind_Fresh()
    {
        // T4 (main-immediate): Main is exempt from stale demotion, so the
        // generic demotion contract is pinned on a non-Main stale candidate.
        var result = DecisionEngine.Evaluate(Context(
            Candidate(Slot.Defensive, everPressed: true, lastChangedMs: 0),
            Candidate(Slot.Offensive)));

        Assert.Equal([Slot.Offensive, Slot.Defensive], result.Order);
        Assert.True(result.DemotedStale);
        Assert.Equal(DecisionReason.OffensiveCooldown, result.Reason);
    }

    [Fact]
    public void Evaluate_All_Stale_Keeps_Order_And_Does_Not_Deadlock()
    {
        var result = DecisionEngine.Evaluate(Context(
            Candidate(Slot.Defensive, everPressed: true, lastChangedMs: 0),
            Candidate(Slot.Offensive, everPressed: true, lastChangedMs: 0)));

        Assert.Equal([Slot.Defensive, Slot.Offensive], result.Order);
        Assert.False(result.DemotedStale);
        Assert.Equal(DecisionReason.DefensiveUrgency, result.Reason);
        Assert.True(result.Confidence <= 40); // 80 base - 40 stale penalty
    }

    [Fact]
    public void Evaluate_Unchanged_But_Never_Pressed_Is_Not_Stale()
    {
        var result = DecisionEngine.Evaluate(Context(
            Candidate(Slot.Main, everPressed: false, lastChangedMs: 0),
            Candidate(Slot.Offensive)));

        Assert.Equal(Slot.Main, result.Order[0]);
        Assert.False(result.DemotedStale);
    }

    [Theory]
    [InlineData(StaleAfter, true)]      // unchanged for exactly the whole window
    [InlineData(StaleAfter - 1, false)] // one ms short: fresh
    public void Stale_Threshold_Is_Inclusive(long changedAgo, bool expectedStale)
    {
        var candidate = Candidate(Slot.Main, everPressed: true, lastChangedMs: Now - changedAgo);

        Assert.Equal(expectedStale, candidate.IsStale(Now, StaleAfter));
        if (expectedStale) Assert.Equal(StaleAfter, candidate.UnchangedMs(Now));
    }

    // ---------- GCD temporary state ----------

    [Fact]
    public void Evaluate_OnGcd_Reports_GcdHold_For_NonInterrupt()
    {
        var result = DecisionEngine.Evaluate(Context(onGcd: true, Candidate(Slot.Main)));

        Assert.Equal(DecisionReason.GcdHold, result.Reason);
        Assert.True(result.Confidence <= 20);
        // The order is unchanged; the existing send loop enforces the skip.
        Assert.Equal(Slot.Main, result.Order[0]);
    }

    [Fact]
    public void Evaluate_OnGcd_Keeps_Interrupt_Urgent()
    {
        var result = DecisionEngine.Evaluate(Context(
            onGcd: true,
            Candidate(Slot.Main),
            Candidate(Slot.Interrupt)));

        Assert.Equal(DecisionReason.InterruptUrgency, result.Reason);
        Assert.Equal(Slot.Interrupt, result.Order[0]);
    }

    // ---------- actionability metadata ----------

    [Fact]
    public void Evaluate_MovementBound_Head_Lowers_Confidence()
    {
        var result = DecisionEngine.Evaluate(Context(Candidate(Slot.Main, actionable: false)));

        Assert.Equal(DecisionReason.MainRotation, result.Reason);
        Assert.Equal(50, result.Confidence); // 70 base - 20 not-actionable
    }
}
