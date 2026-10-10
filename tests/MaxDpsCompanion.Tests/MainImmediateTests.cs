using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// T4 (spec <c>docs/plans/2026-10-04-main-immediate.md</c>): the WHITE core
/// rotation (Main slot) executes immediately when the game allows it. These
/// tests pin the scheduler half of the contract — a stale Main is never
/// demoted behind a fresh Offensive, Main bypasses the min key interval for any
/// DIFFERENT press (only an identical repeat is held), a failed Main press
/// arms no <c>_failedUntil</c> suppression, and the GCD-observed send resets
/// the no-op attempt cap. The game-forbidden holds (GCD, cast, range, melee,
/// no-target) and the non-Main backoff ladder stay exactly as they were.
///
/// The decision-layer half (Main never stale-demoted, no confidence penalty)
/// is pinned in <c>DecisionEngineTests</c>/<c>TelemetryReplayTests</c>.
/// </summary>
public class MainImmediateTests
{
    private static readonly KeyStroke KeyE = new(0x45, false, false, false);
    private static readonly KeyStroke KeyR = new(0x52, false, false, false);
    private static readonly KeyStroke KeyF = new(0x46, false, false, false);

    private const int MeleeSpellId = 920001;

    // Synthetic catalog: one ResearchBacked melee On-GCD ability. Real Main
    // rotation spells are uncatalogued, so a catalogued melee identity is the
    // only way to exercise the central melee gate on the Main slot.
    private const string VendorJson = """{ "gamePatch": "12.1", "entries": [] }""";
    private const string MeleeJson = """
        { "gamePatch": "12.1", "interface": 120100, "verified": "2026-10-04",
          "abilities": [
            {"id":920001,"name":"Melee Strike","category":"Offensive","purpose":"MajorOffensive","range":"Melee","gcd":"OnGcd","cast":"Instant","status":"ResearchBacked"}
          ] }
        """;

    private static AbilityCatalog MeleeCatalog { get; } = AbilityCatalog.Load(VendorJson, MeleeJson);

    private static BridgeFrame Frame(
        (Slot Slot, KeyStroke? Stroke)[] slots,
        int heartbeat = 1,
        bool inCombat = true,
        bool onGcd = false,
        bool hasTarget = true,
        BridgeState state = BridgeState.Active)
    {
        var array = new KeyStroke?[PixelProtocol.SlotCount];
        foreach (var (slot, stroke) in slots) array[(int)slot] = stroke;
        var flags = 0;
        if (inCombat) flags |= PixelProtocol.StatusFlagInCombat;
        if (onGcd) flags |= PixelProtocol.StatusFlagOnGcd;
        if (hasTarget) flags |= PixelProtocol.StatusFlagHasTarget;
        return new BridgeFrame
        {
            State = state,
            Heartbeat = heartbeat,
            Version = PixelProtocol.SupportedVersion,
            StatusFlags = flags,
            Slots = array,
        };
    }

    private static ActionCandidate Candidate(
        Slot slot,
        KeyStroke? stroke = null,
        int spellId = 0,
        bool enabled = true,
        bool actionable = true,
        long firstSeenMs = 0,
        long lastChangedMs = 0,
        bool everPressed = false) =>
        new(slot, stroke ?? SlotStroke(slot), enabled, actionable, firstSeenMs, lastChangedMs, 0, everPressed, spellId);

    private static KeyStroke SlotStroke(Slot slot) => new((byte)(0x41 + (int)slot), false, false, false);

    private static CombatContext Context(
        bool hpValid = false,
        int hp = 100,
        TriState targetCasting = TriState.No,
        TriState targetInMelee = TriState.Unknown,
        TriState[]? range = null,
        PlayerCastState cast = PlayerCastState.None) => new()
    {
        HpValid = hpValid,
        HpPct = hp,
        Cast = cast,
        TargetCasting = targetCasting,
        TargetInMelee = targetInMelee,
        SlotRange = range ?? new TriState[PixelProtocol.SlotCount],
        SlotBuffActive = new TriState[PixelProtocol.SlotCount],
        ContextValid = true,
    };

    private static TriState[] Range(params (Slot Slot, TriState State)[] entries)
    {
        var range = new TriState[PixelProtocol.SlotCount];
        foreach (var (slot, state) in entries) range[(int)slot] = state;
        return range;
    }

    /// <summary>Policy-off input: MaxDps's own gate is the only filter.</summary>
    private static ScheduleInput Input(long now, BridgeFrame? frame, params ActionCandidate[] candidates) => new()
    {
        Frame = frame,
        OutOfCombatPermitted = true,
        Candidates = candidates,
        NowMs = now,
        MinKeyIntervalMs = 120,
        StaleAfterMs = 1500,
        HeartbeatTimeoutMs = 500,
        RepeatSuppressMs = 900,
        Context = null,
        Options = null,
        Catalog = null,
    };

    /// <summary>Policy-on input with an explicit context/catalog.</summary>
    private static ScheduleInput Input(
        long now,
        BridgeFrame? frame,
        CombatContext? context,
        PolicyOptions? options,
        AbilityCatalog? catalog,
        params ActionCandidate[] candidates) => new()
    {
        Frame = frame,
        OutOfCombatPermitted = true,
        Candidates = candidates,
        NowMs = now,
        MinKeyIntervalMs = 120,
        StaleAfterMs = 1500,
        HeartbeatTimeoutMs = 500,
        RepeatSuppressMs = 900,
        Context = context,
        Options = options,
        Catalog = catalog,
    };

    // ---- stale exemption --------------------------------------------------

    [Fact]
    public void Stale_Main_Stays_Ahead_Of_Fresh_Offensive()
    {
        var scheduler = new ActionScheduler();
        var plan = scheduler.Advance(Input(
            10_000, Frame([(Slot.Main, KeyE)]),
            Context(), PolicyOptions.Standard, AbilityCatalog.Default,
            Candidate(Slot.Main, KeyE, everPressed: true, lastChangedMs: 0),
            Candidate(Slot.Offensive, KeyR, firstSeenMs: 9_000, lastChangedMs: 9_000)));

        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Equal(ScheduleReason.MainRotation, plan.Reason);
        Assert.False(plan.StaleDemoted);
        Assert.Equal([Slot.Main, Slot.Offensive], plan.Actions.Select(a => a.Slot).ToArray());
    }

    // ---- min-interval bypass (different press only) -----------------------

    [Fact]
    public void Main_Bypasses_MinInterval_After_A_Different_Slot_Send()
    {
        var scheduler = new ActionScheduler();

        // A different-slot send starts the interval clock.
        Assert.Equal(Slot.Offensive, scheduler.Advance(Input(
            0, Frame([(Slot.Offensive, KeyR)]), Candidate(Slot.Offensive, KeyR))).Selected);
        scheduler.NoteSent(0, Slot.Offensive, KeyR, 0);

        // 50 ms later (inside the 120 ms interval) the Main rotation executes
        // immediately: only an identical repeat is paced.
        var plan = scheduler.Advance(Input(
            50, Frame([(Slot.Main, KeyE), (Slot.Offensive, KeyR)], heartbeat: 2),
            Candidate(Slot.Main, KeyE), Candidate(Slot.Offensive, KeyR)));

        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Equal(ScheduleReason.MainRotation, plan.Reason);
    }

    [Fact]
    public void Main_Bypasses_MinInterval_After_A_Different_Main_Spell_Send()
    {
        var scheduler = new ActionScheduler();
        var frame = Frame([(Slot.Main, KeyE), (Slot.Main, KeyF)]);
        var e = Candidate(Slot.Main, KeyE, spellId: 100);
        var f = Candidate(Slot.Main, KeyF, spellId: 200);

        Assert.Equal(Slot.Main, scheduler.Advance(Input(0, frame, e, f)).Selected);
        scheduler.NoteSent(0, Slot.Main, KeyE, 100);

        // The rotation moved to a different Main identity inside the interval:
        // the changed press lands now, only the identical KeyE repeat is held.
        var plan = scheduler.Advance(Input(50, Frame([(Slot.Main, KeyE), (Slot.Main, KeyF)], heartbeat: 2), e, f));

        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Equal(KeyF, plan.Actions[0].Stroke);
        Assert.Equal(200, plan.Actions[0].SpellId);
    }

    [Fact]
    public void Identical_Main_Repeat_Is_Held_Until_MinKeyIntervalMs()
    {
        var scheduler = new ActionScheduler();
        var e = Candidate(Slot.Main, KeyE, spellId: 42);

        Assert.Equal(Slot.Main, scheduler.Advance(Input(0, Frame([(Slot.Main, KeyE)], heartbeat: 1), e)).Selected);
        scheduler.NoteSent(0, Slot.Main, KeyE, 42);

        var held = scheduler.Advance(Input(119, Frame([(Slot.Main, KeyE)], heartbeat: 2), e));
        Assert.Null(held.Selected);
        Assert.Equal(ScheduleReason.MinInterval, held.Reason);

        var due = scheduler.Advance(Input(120, Frame([(Slot.Main, KeyE)], heartbeat: 3), e));
        Assert.Equal(Slot.Main, due.Selected);
    }

    // ---- failure handling -------------------------------------------------

    [Fact]
    public void Failed_Main_Press_Sets_No_FailedUntil_And_Represses()
    {
        var scheduler = new ActionScheduler();
        var e = Candidate(Slot.Main, KeyE);

        Assert.Equal(Slot.Main, scheduler.Advance(Input(0, Frame([(Slot.Main, KeyE)], heartbeat: 1), e)).Selected);
        scheduler.NoteSent(0, Slot.Main, KeyE, 0);

        // The press never produced a GCD: the failure is counted for telemetry
        // but NO suppression window is armed for Main — it re-presses at once.
        var plan = scheduler.Advance(Input(700, Frame([(Slot.Main, KeyE)], heartbeat: 2), e));

        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Equal(1, scheduler.RejectionsDetected);
        Assert.Equal(0, scheduler.FailedUntilFor(Slot.Main, KeyE));

        // Direct NoteFailure is likewise a no-op for Main suppression.
        scheduler.NoteFailure(Slot.Main, KeyE, 800);
        Assert.Equal(0, scheduler.FailedUntilFor(Slot.Main, KeyE));
    }

    [Fact]
    public void Gcd_Observed_Main_Send_Resets_The_NoOp_Attempt_Cap()
    {
        // Control: three identical Main sends without a GCD trip the cap.
        var control = new ActionScheduler();
        control.NoteSent(0, Slot.Main, KeyE, 7);
        control.NoteSent(10, Slot.Main, KeyE, 7);
        control.NoteSent(20, Slot.Main, KeyE, 7);
        var capped = control.Advance(Input(
            100, Frame([(Slot.Main, KeyE)], heartbeat: 2), Candidate(Slot.Main, KeyE, spellId: 7)));
        Assert.Equal(ScheduleReason.RetryBackoff, capped.Reason);
        Assert.Null(capped.Selected);

        // A GCD observed after a Main press proves it landed: the NEXT Main send
        // drops the streak, so the same number of later presses never caps.
        var scheduler = new ActionScheduler();
        scheduler.NoteSent(0, Slot.Main, KeyE, 7);
        scheduler.Advance(Input(
            30, Frame([(Slot.Main, KeyE)], heartbeat: 2, onGcd: true), Candidate(Slot.Main, KeyE, spellId: 7)));
        scheduler.NoteSent(200, Slot.Main, KeyE, 7);   // sees SawGcd -> resets the count
        scheduler.NoteSent(250, Slot.Main, KeyE, 7);
        scheduler.NoteSent(280, Slot.Main, KeyE, 7);

        var plan = scheduler.Advance(Input(
            410, Frame([(Slot.Main, KeyE)], heartbeat: 3), Candidate(Slot.Main, KeyE, spellId: 7)));
        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Equal(ScheduleReason.MainRotation, plan.Reason);
    }

    [Fact]
    public void NonMain_Backoff_Ladder_Is_Unchanged()
    {
        var s = new ActionScheduler();

        s.NoteFailure(Slot.Offensive, KeyR, 1600);
        Assert.Equal(3100, s.FailedUntilFor(Slot.Offensive, KeyR));   // 1.5 s
        s.NoteFailure(Slot.Offensive, KeyR, 2000);
        Assert.Equal(5000, s.FailedUntilFor(Slot.Offensive, KeyR));   // 3 s
        s.NoteFailure(Slot.Offensive, KeyR, 2400);
        Assert.Equal(8400, s.FailedUntilFor(Slot.Offensive, KeyR));   // 6 s
        s.NoteFailure(Slot.Offensive, KeyR, 2800);
        Assert.Equal(12_800, s.FailedUntilFor(Slot.Offensive, KeyR)); // 10 s
    }

    // ---- game-forbidden holds still apply to Main ------------------------

    [Fact]
    public void Main_Is_Still_Held_By_Gcd()
    {
        var plan = new ActionScheduler().Advance(Input(
            0, Frame([(Slot.Main, KeyE)], heartbeat: 1, onGcd: true), Candidate(Slot.Main, KeyE)));

        Assert.Null(plan.Selected);
        Assert.Equal(ScheduleReason.GcdHold, plan.Reason);
    }

    [Fact]
    public void Main_Is_Still_Held_By_Cast()
    {
        var plan = new ActionScheduler().Advance(Input(
            0, Frame([(Slot.Main, KeyE)], heartbeat: 1),
            Context(cast: PlayerCastState.Casting), PolicyOptions.Standard, AbilityCatalog.Default,
            Candidate(Slot.Main, KeyE)));

        Assert.Null(plan.Selected);
        Assert.Equal(ScheduleReason.CastHold, plan.Reason);
    }

    [Fact]
    public void Main_Is_Still_Held_By_Range()
    {
        var plan = new ActionScheduler().Advance(Input(
            0, Frame([(Slot.Main, KeyE)], heartbeat: 1),
            Context(range: Range((Slot.Main, TriState.No))), PolicyOptions.Standard, AbilityCatalog.Default,
            Candidate(Slot.Main, KeyE)));

        Assert.Null(plan.Selected);
        Assert.Equal(ScheduleReason.PolicySkip, plan.Reason);
    }

    [Fact]
    public void Main_Is_Still_Held_By_Melee_Out_Of_Reach()
    {
        var plan = new ActionScheduler().Advance(Input(
            0, Frame([(Slot.Main, KeyE)], heartbeat: 1),
            Context(targetInMelee: TriState.No), PolicyOptions.Standard, MeleeCatalog,
            Candidate(Slot.Main, KeyE, spellId: MeleeSpellId)));

        Assert.Null(plan.Selected);
        Assert.Equal(ScheduleReason.PolicySkip, plan.Reason);
        Assert.Contains(ExecutionSafety.MeleeRangeReason, plan.PolicyDetail);
    }

    [Fact]
    public void Main_Is_Still_Held_By_NoTarget()
    {
        var plan = new ActionScheduler().Advance(Input(
            0, Frame([(Slot.Main, KeyE)], heartbeat: 1, hasTarget: false), Candidate(Slot.Main, KeyE)));

        Assert.Null(plan.Selected);
        Assert.Equal(ScheduleReason.NoTarget, plan.Reason);
    }

    // ---- fallback (intelligence off) is unchanged ------------------------

    [Fact]
    public void Fallback_Main_Order_Is_Identical()
    {
        var expected = new[] { Slot.Main, Slot.Offensive, Slot.Interrupt, Slot.Defensive, Slot.Consumable, Slot.Trinket };
        Assert.Equal(expected, DecisionEngine.FallbackOrder);
        Assert.Equal(expected, DecisionResult.Fallback().Order);

        var plan = new ActionScheduler().Advance(Input(
            0, Frame([(Slot.Main, KeyE), (Slot.Offensive, KeyR)]),
            Candidate(Slot.Main, KeyE), Candidate(Slot.Offensive, KeyR)));

        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Equal(ScheduleReason.MainRotation, plan.Reason);
    }

    // ---- C4 pin: Main never degrades on TTK / burst / pair / waste / history
    //
    // Spec docs/plans/2026-10-04-main-immediate.md C4 requires the Main branches
    // in PolicyEvaluator.cs (Generic Main, registry, exec/melee) and
    // CandidateProviders.cs (MaxDpsRotation Main identity) to be untouched. The
    // TTK-aware holds live ONLY on the Offensive / Consumable / Trinket paths.
    // The context below is deliberately hostile: a valid 0.5 s TTK, a pending
    // 0.5 s trash-history window, a 0.2 s young target, the fast-pack latch,
    // Burst + AoE presets, the ConserveMajors fallback, warmup on, an active
    // paired offensive window and an active on-use trinket lockout. The control
    // proves the fixture is genuinely hostile; Main must still be executable.

    private const string HostileJson = """
        { "gamePatch": "12.1", "interface": 120100, "verified": "2026-10-04",
          "abilities": [
            {"id":930001,"name":"Main Rotation","category":"Main","purpose":"Rotational","range":"Ranged","gcd":"OnGcd","cast":"Instant","status":"ResearchBacked"},
            {"id":930002,"name":"Rotational Strike","category":"Main","purpose":"Rotational","range":"Melee","gcd":"OnGcd","cast":"Instant","status":"ResearchBacked"},
            {"id":930003,"name":"Burst Window","category":"Offensive","purpose":"MajorOffensive","range":"Ranged","gcd":"OnGcd","cast":"Instant","status":"ResearchBacked","group":"Burst","cdMs":120000,"durMs":20000}
          ] }
        """;

    private static AbilityCatalog HostileCatalog { get; } = AbilityCatalog.Load(VendorJson, HostileJson);

    /// <summary>Every worst-case TTK/history observable the offensive guards read.</summary>
    private static CombatContext HostileTtkContext()
    {
        var range = new TriState[PixelProtocol.SlotCount];
        for (var i = 0; i < range.Length; i++) range[i] = TriState.Yes;
        return new CombatContext
        {
            HpValid = true,
            HpPct = 20,
            Cast = PlayerCastState.None,
            TargetCasting = TriState.No,
            TargetCastInterruptible = TriState.No,
            TargetInMelee = TriState.Yes,
            TargetHpValid = true,
            TargetHpPct = 5,
            TtkValid = true,
            TtkSec = 0.5,
            TtkProvisional = true,
            TargetAgeSec = 0.2,
            FastPackLatch = true,
            TtkHistBinding = true,
            TtkHistRate = 1.0,
            TtkHistSec = 0.5,
            TtkHistProvisional = true,
            TtkHistKills = 8,
            TtkHistDurFactor = 0.5,
            SlotRange = range,
            SlotBuffActive = new TriState[PixelProtocol.SlotCount],
            ContextValid = true,
        };
    }

    private static PolicyOptions HostileOptions { get; } = new()
    {
        Preset = RotationPreset.Burst,
        TargetPreset = TargetPreset.Aoe,
        TimeToKillFallback = TtkFallback.ConserveMajors,
        TtkWarmupSec = 3.0,
        TtkHistory = true,
        TtkHistoryQuantile = 75,
    };

    private static PolicyMemory HostileMemory()
    {
        var memory = new PolicyMemory();
        // An active paired offensive window plus the shared on-use trinket lockout.
        memory.NoteUse(HostileCatalog.TryGet(930003)!, 10_000);
        memory.NoteTrinketUse(10_000);
        return memory;
    }

    private static PolicyDecision EvaluateHostile(Slot slot, int spellId, PolicyMemory memory) =>
        PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = slot,
            SpellId = spellId,
            Context = HostileTtkContext(),
            Options = HostileOptions,
            Memory = memory,
            NowMs = 10_000,
            InCombat = true,
            HasTarget = true,
        }, HostileCatalog);

    [Fact]
    public void Hostile_Ttk_Context_Actually_Holds_A_Major_Offensive()
    {
        // Control: proves the fixture really does trigger the TTK/pair holds, so
        // the Main pins below are not vacuous.
        var control = EvaluateHostile(Slot.Offensive, 930003, HostileMemory());

        Assert.Equal(PolicyVerdict.Hold, control.Verdict);
        Assert.NotEqual(PolicyVerdict.Use, control.Verdict);
    }

    [Fact]
    public void Catalogued_Main_Never_Held_By_Hostile_Ttk_Burst_Pair_Waste_History()
    {
        var decision = EvaluateHostile(Slot.Main, 930001, HostileMemory());

        Assert.Equal(PolicyVerdict.Use, decision.Verdict);
        Assert.Equal("MaxDpsRotation", decision.Provider);
        Assert.Equal("MaxDps main candidate; actionable", decision.Reason);
    }

    [Fact]
    public void Rotational_Main_Never_Held_By_Hostile_Ttk_Burst_Pair_Waste_History()
    {
        // Purpose Rotational (not Category Main) routes to the same Main branch.
        var decision = EvaluateHostile(Slot.Main, 930002, HostileMemory());

        Assert.Equal(PolicyVerdict.Use, decision.Verdict);
        Assert.Equal("MaxDpsRotation", decision.Provider);
    }

    [Fact]
    public void Uncatalogued_Main_Never_Held_By_Hostile_Ttk_Burst_Pair_Waste_History()
    {
        // The real Main rotation is uncatalogued: the generic branch must also
        // stay executable under the same hostile context.
        var decision = EvaluateHostile(Slot.Main, 0, HostileMemory());

        Assert.Equal(PolicyVerdict.Use, decision.Verdict);
        Assert.Equal("Generic", decision.Provider);
        Assert.Equal("MaxDps main candidate; actionable", decision.Reason);
    }
}
