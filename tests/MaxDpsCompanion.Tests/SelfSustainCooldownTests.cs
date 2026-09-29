using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// R2 (v3/r2-sustain-cd): cooldown/reset-aware Solo self-sustain.
///
/// The bridge only encodes a self-heal when it is off cooldown, so the
/// companion never sees a ready-but-cooling-down heal as a pressable stroke.
/// These tests pin the companion's half of the contract:
///  * when HP is in the sustain window and no heal is ready, the plan/telemetry
///    explains the wait (distinct reason, not generic "no candidate");
///  * a heal that becomes ready again — including after a dynamic reset, with
///    unchanged slot content — is offered immediately, never stale-demoted
///    behind the main rotation;
///  * a transient failure suppresses the heal for at most 1.5 s with no
///    escalating backoff, and it never demotes a pending heal;
///  * a permanent (policy OFF) exclusion still never uses.
/// </summary>
public class SelfSustainCooldownTests
{
    private const int ImpendingVictory = 202168;
    private static readonly KeyStroke KeyE = new(0x45, false, false, false);
    private static readonly KeyStroke KeyH = new(0x48, false, false, false);
    private static readonly bool[] AllSlots = [true, true, true, true, true, true, true, true];

    private static PolicyOptions Solo => new() { SoloEnabled = true };

    private static BridgeFrame DecodeFrame(
        int heartbeat,
        int hp,
        bool hasSelfHeal,
        bool onGcd = false)
    {
        var builder = new TestV5Frame()
            .Vitals(hp)
            .Cast(PlayerCastState.None)
            .Target(inMelee: true, casting: false, interruptible: false)
            .Ranges((Slot.Main, TriState.Yes), (Slot.SelfHeal, TriState.Yes))
            .ClassSpec("WARRIOR", 1)
            .Slot(Slot.Main, KeyE.VirtualKey)
            .SpellId(Slot.Main, 12294);
        if (hasSelfHeal) builder.Slot(Slot.SelfHeal, KeyH.VirtualKey).SpellId(Slot.SelfHeal, ImpendingVictory);

        var frame = PixelProtocol.Decode(builder.Build(heartbeat, onGcd: onGcd));
        Assert.NotNull(frame);
        return frame!;
    }

    /// <summary>Frame with no sendable candidate at all (only context).</summary>
    private static BridgeFrame ContextOnlyFrame(int heartbeat, int hp)
    {
        var frame = PixelProtocol.Decode(new TestV5Frame()
            .Vitals(hp)
            .Cast(PlayerCastState.None)
            .Target(inMelee: true, casting: false, interruptible: false)
            .ClassSpec("WARRIOR", 1)
            .Build(heartbeat));
        Assert.NotNull(frame);
        return frame!;
    }

    private static SchedulePlan Advance(
        ActionScheduler scheduler,
        CandidateTracker tracker,
        BridgeFrame frame,
        PolicyOptions? options,
        long now)
    {
        tracker.Update(frame, now);
        return scheduler.Advance(new ScheduleInput
        {
            Frame = frame,
            Candidates = tracker.Snapshot(AllSlots),
            NowMs = now,
            MinKeyIntervalMs = 120,
            StaleAfterMs = 1500,
            HeartbeatTimeoutMs = 500,
            RepeatSuppressMs = 900,
            Context = CombatContext.FromFrame(frame),
            Options = options,
            Catalog = AbilityCatalog.Default,
            CollectPolicyVerdicts = true,
        });
    }

    // ---- 1. cooldown wait explains itself, then fires without delay --------

    [Fact]
    public void No_Ready_Heal_In_Sustain_Window_Holds_With_Distinct_Reason()
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();

        var plan = Advance(scheduler, tracker, ContextOnlyFrame(1, 50), Solo, now: 1000);

        Assert.Null(plan.Selected);
        Assert.Equal(ScheduleReason.SelfHealCoolingDown, plan.Reason);
        Assert.True(plan.SelfHealCoolingDown);
        Assert.Equal(SelfSustainCandidateProvider.CooldownWaitReason, plan.PolicyDetail);
        Assert.DoesNotContain(plan.Verdicts, v => v.Slot == Slot.SelfHeal);
    }

    [Fact]
    public void Heal_Returning_Next_Tick_Is_Used_Immediately()
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();

        var wait = Advance(scheduler, tracker, ContextOnlyFrame(1, 50), Solo, now: 1000);
        Assert.Equal(ScheduleReason.SelfHealCoolingDown, wait.Reason);

        var ready = Advance(scheduler, tracker, DecodeFrame(2, 50, hasSelfHeal: true), Solo, now: 1050);
        Assert.Equal(Slot.SelfHeal, ready.Selected);
        Assert.Equal(ScheduleReason.SelfSustain, ready.Reason);
        Assert.False(ready.SelfHealCoolingDown);
    }

    [Fact]
    public void Healthy_Or_Out_Of_Window_Is_Not_A_Cooldown_Wait()
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();

        var plan = Advance(scheduler, tracker, ContextOnlyFrame(1, 90), Solo, now: 1000);

        Assert.Equal(ScheduleReason.NoCandidate, plan.Reason);
        Assert.False(plan.SelfHealCoolingDown);
    }

    // ---- 2. reset: unchanged slot content still fires ----------------------

    [Fact]
    public void Reset_MidWindow_Unchanged_Slot_Is_Not_Stale_Demoted()
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();

        // Tick 1: heal fires and the engine sends it; the GCD then confirms it.
        var first = Advance(scheduler, tracker, DecodeFrame(1, 50, hasSelfHeal: true), Solo, now: 1000);
        Assert.Equal(Slot.SelfHeal, first.Selected);
        scheduler.NoteSent(1000, Slot.SelfHeal, KeyH, ImpendingVictory);

        var gcd = Advance(scheduler, tracker, DecodeFrame(2, 55, hasSelfHeal: true, onGcd: true), Solo, now: 1050);
        Assert.Equal(ScheduleReason.GcdHold, gcd.Reason);

        // Tick 3: well past the stale window, the SAME spell is still suggested
        // with unchanged slot content (a dynamic reset made it ready again).
        // The heal must fire itself, not be demoted behind fresh Main.
        var reset = Advance(scheduler, tracker, DecodeFrame(3, 50, hasSelfHeal: true), Solo, now: 4000);
        Assert.Equal(Slot.SelfHeal, reset.Selected);
        Assert.Equal(Slot.SelfHeal, reset.Actions[0].Slot);
        Assert.Contains(reset.Actions, a => a.Slot == Slot.Main);   // main never removed
    }

    // ---- 3. transient failure retries within 1.5 s, no escalation ----------

    [Fact]
    public void Transient_Failure_Suppression_Is_Capped_At_1500ms()
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();

        var first = Advance(scheduler, tracker, DecodeFrame(1, 50, hasSelfHeal: true), Solo, now: 1000);
        Assert.Equal(Slot.SelfHeal, first.Selected);
        scheduler.NoteSent(1000, Slot.SelfHeal, KeyH, ImpendingVictory);

        // Tick 2: no GCD appeared inside RejectDetectMs -> the press failed and
        // the heal is suppressed for exactly the base reject window.
        var failed = Advance(scheduler, tracker, DecodeFrame(3, 50, hasSelfHeal: true), Solo, now: 1700);
        Assert.Equal(1, scheduler.RejectionsDetected);
        Assert.Equal(Slot.Main, failed.Selected);
        Assert.DoesNotContain(failed.Actions, a => a.Slot == Slot.SelfHeal);
        Assert.True(failed.Suppressed >= 1);

        // Still inside the 1.5 s cap: no retry yet.
        var inside = Advance(scheduler, tracker, DecodeFrame(4, 50, hasSelfHeal: true), Solo, now: 3150);
        Assert.NotEqual(Slot.SelfHeal, inside.Selected);

        // Past 1700 + 1500: the heal is re-polled and fires again.
        var retry = Advance(scheduler, tracker, DecodeFrame(5, 50, hasSelfHeal: true), Solo, now: 3250);
        Assert.Equal(Slot.SelfHeal, retry.Selected);
    }

    // ---- 4. permanent exclusion still never uses ---------------------------

    [Fact]
    public void Policy_Off_Heal_Is_Never_Used()
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();
        var off = new PolicyOptions
        {
            SoloEnabled = true,
            Abilities = AbilityPolicy.FromIds(on: null, off: ImpendingVictory.ToString()),
        };

        var plan = Advance(scheduler, tracker, DecodeFrame(1, 50, hasSelfHeal: true), off, now: 1000);

        Assert.Equal(Slot.Main, plan.Selected);
        var verdict = Assert.Single(plan.Verdicts, v => v.Slot == Slot.SelfHeal);
        Assert.Equal(PolicyVerdict.Skip, verdict.Verdict);
        Assert.Contains("user policy disabled", verdict.Reason);
    }

    // ---- 5. optional curated resetHint is parsed and informational ---------

    [Fact]
    public void Curated_ResetHint_Parses_And_Is_Informational()
    {
        const string vendor = """{ "entries": [] }""";
        const string curated = """
            { "gamePatch": "12.1", "interface": 120100, "verified": "2026-09-28",
              "abilities": [
                { "id": 900100, "name": "Reset Fixture", "category": "SelfHeal", "purpose": "SelfHeal",
                  "gcd": "OnGcd", "range": "SelfOnly", "healPct": 20, "resetHint": "resets on kill",
                  "source": "fixture" } ] }
            """;
        var catalog = AbilityCatalog.Load(vendor, curated);
        var ability = catalog.TryGet(900100)!;
        Assert.Equal("resets on kill", ability.ResetHint);

        // The hint must not change the verdict: the same HP gate decides.
        var decision = PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = Slot.SelfHeal,
            SpellId = 900100,
            Context = new CombatContext
            {
                HpValid = true,
                HpPct = 50,
                ContextValid = true,
                SlotRange = new TriState[PixelProtocol.SlotCount],
                SlotBuffActive = new TriState[PixelProtocol.SlotCount],
            },
            Options = Solo,
            Memory = new PolicyMemory(),
            NowMs = 1000,
            InCombat = true,
            HasTarget = true,
        }, catalog);
        Assert.Equal(PolicyVerdict.Use, decision.Verdict);
    }
}
