using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Scheduler + situational policy integration: USE/HOLD/SKIP filtering,
/// emergency ranking, companion-slot gating, and the failure-recovery state
/// machine (a press that never produces a GCD, repeated-stroke backoff).
/// </summary>
public class ActionSchedulerPolicyTests
{
    private static readonly KeyStroke KeyE = new(0x45, false, false, false);
    private static readonly KeyStroke KeyR = new(0x52, false, false, false);
    private static readonly KeyStroke KeyF = new(0x46, false, false, false);
    private static readonly KeyStroke Key1 = new(0x31, false, false, false);

    private static AbilityCatalog Catalog => AbilityCatalog.Default;

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

    private static ScheduleInput Input(
        long now,
        BridgeFrame? frame,
        CombatContext? context = null,
        PolicyOptions? options = null,
        params ActionCandidate[] candidates) => new()
    {
        Frame = frame,
        Candidates = candidates,
        NowMs = now,
        MinKeyIntervalMs = 120,
        StaleAfterMs = 1500,
        HeartbeatTimeoutMs = 500,
        RepeatSuppressMs = 900,
        Context = context,
        Options = options,
        Catalog = options is null ? null : Catalog,
    };

    private static CombatContext Context(
        bool hpValid = false,
        int hp = 100,
        TriState targetCasting = TriState.No,
        TriState targetInMelee = TriState.Unknown,
        TriState[]? range = null,
        TriState[]? buffs = null,
        PlayerCastState cast = PlayerCastState.None) => new()
    {
        HpValid = hpValid,
        HpPct = hp,
        Cast = cast,
        TargetCasting = targetCasting,
        TargetInMelee = targetInMelee,
        SlotRange = range ?? new TriState[PixelProtocol.SlotCount],
        SlotBuffActive = buffs ?? new TriState[PixelProtocol.SlotCount],
        ContextValid = true,
    };

    private static TriState[] Range(params (Slot Slot, TriState State)[] entries)
    {
        var range = new TriState[PixelProtocol.SlotCount];
        foreach (var (slot, state) in entries) range[(int)slot] = state;
        return range;
    }

    // ---- policy filtering and ranking ------------------------------------

    [Fact]
    public void Policy_Held_Defensive_Does_Not_Block_Main()
    {
        var scheduler = new ActionScheduler();
        var frame = Frame([(Slot.Main, KeyE), (Slot.Defensive, KeyR)]);
        var plan = scheduler.Advance(Input(
            1000, frame,
            Context(targetCasting: TriState.No),
            PolicyOptions.Standard,
            Candidate(Slot.Defensive, KeyR, spellId: 23920),   // Spell Reflection: no cast -> HOLD
            Candidate(Slot.Main, KeyE)));

        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Equal(ScheduleReason.MainRotation, plan.Reason);
        Assert.Equal(1, plan.PolicyHeld);
        Assert.Contains("incoming cast", plan.PolicyDetail);
    }

    [Fact]
    public void Emergency_Defensive_Outranks_Main()
    {
        var scheduler = new ActionScheduler();
        var frame = Frame([(Slot.Main, KeyE), (Slot.Defensive, KeyR)]);
        var plan = scheduler.Advance(Input(
            1000, frame,
            Context(hpValid: true, hp: 28),
            PolicyOptions.Standard,
            Candidate(Slot.Main, KeyE),
            Candidate(Slot.Defensive, KeyR, spellId: 871)));   // Shield Wall at 28%

        Assert.Equal(Slot.Defensive, plan.Selected);
        Assert.Equal(ScheduleReason.EmergencySurvival, plan.Reason);
        Assert.Equal(KeyR, plan.Actions[0].Stroke);
    }

    [Fact]
    public void Policy_Skip_Is_Reported_And_Falls_Through()
    {
        var scheduler = new ActionScheduler();
        var frame = Frame([(Slot.Main, KeyE), (Slot.Offensive, KeyR)]);
        var plan = scheduler.Advance(Input(
            1000, frame,
            Context(range: Range((Slot.Main, TriState.No), (Slot.Offensive, TriState.Yes))),
            PolicyOptions.Standard,
            Candidate(Slot.Main, KeyE),
            Candidate(Slot.Offensive, KeyR, spellId: 1719)));

        Assert.Equal(Slot.Offensive, plan.Selected);
        Assert.Equal(1, plan.PolicySkipped);
        Assert.Contains("out of range", plan.PolicyDetail);
    }

    [Fact]
    public void Mobility_Ranks_Below_A_Live_Main()
    {
        var scheduler = new ActionScheduler();
        var frame = Frame([(Slot.Main, KeyE), (Slot.Mobility, Key1)]);
        var plan = scheduler.Advance(Input(
            1000, frame,
            Context(targetInMelee: TriState.No, range: Range((Slot.Mobility, TriState.Yes))),
            PolicyOptions.Standard,
            Candidate(Slot.Main, KeyE),
            Candidate(Slot.Mobility, Key1, spellId: 36554)));

        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Equal(2, plan.Actions.Length);
    }

    [Fact]
    public void Mobility_Is_Selected_When_Main_Is_Out_Of_Range()
    {
        var scheduler = new ActionScheduler();
        var frame = Frame([(Slot.Main, KeyE), (Slot.Mobility, Key1)]);
        var plan = scheduler.Advance(Input(
            1000, frame,
            Context(targetInMelee: TriState.No,
                range: Range((Slot.Main, TriState.No), (Slot.Mobility, TriState.Yes))),
            PolicyOptions.Standard,
            Candidate(Slot.Main, KeyE),
            Candidate(Slot.Mobility, Key1, spellId: 36554)));

        Assert.Equal(Slot.Mobility, plan.Selected);
        Assert.Equal(ScheduleReason.MobilityUplift, plan.Reason);
        Assert.Equal(1, plan.PolicySkipped);
    }

    [Fact]
    public void Solo_SelfHeal_Ranks_Above_Main_Below_Emergency()
    {
        var scheduler = new ActionScheduler();
        var frame = Frame([(Slot.Main, KeyE), (Slot.SelfHeal, Key1)]);
        var solo = new PolicyOptions { SoloEnabled = true };
        var plan = scheduler.Advance(Input(
            1000, frame,
            Context(hpValid: true, hp: 50),
            solo,
            Candidate(Slot.Main, KeyE),
            Candidate(Slot.SelfHeal, Key1, spellId: 202168)));

        Assert.Equal(Slot.SelfHeal, plan.Selected);
        Assert.Equal(ScheduleReason.SelfSustain, plan.Reason);
    }

    [Fact]
    public void Companion_Slots_Are_Never_Scheduled_Without_Intelligence()
    {
        var scheduler = new ActionScheduler();
        var frame = Frame([(Slot.Main, KeyE), (Slot.Mobility, Key1), (Slot.SelfHeal, KeyF)]);
        var plan = scheduler.Advance(Input(
            1000, frame, context: null, options: null,
            Candidate(Slot.Main, KeyE),
            Candidate(Slot.Mobility, Key1, spellId: 36554),
            Candidate(Slot.SelfHeal, KeyF, spellId: 202168)));

        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Single(plan.Actions);
    }

    [Fact]
    public void Unified_Context_Fails_Open_On_The_Scheduler_Path()
    {
        var scheduler = new ActionScheduler();
        var frame = Frame([(Slot.Defensive, KeyR)]);
        var plan = scheduler.Advance(Input(
            1000, frame,
            CombatContext.Unknown(),
            PolicyOptions.Standard,
            Candidate(Slot.Defensive, KeyR, spellId: 23920)));  // reflection: unknown cast -> HOLD per policy

        // Spell Reflection explicitly holds on unknown casts (UnknownPolicy.Hold).
        Assert.Equal(ScheduleReason.PolicyHold, plan.Reason);
    }

    [Fact]
    public void Trinket_Lockout_Is_Enforced_From_Own_History()
    {
        var scheduler = new ActionScheduler();
        var memory = scheduler.Policy;

        var first = scheduler.Advance(Input(1000, Frame([(Slot.Trinket, KeyF)], heartbeat: 1),
            Context(), PolicyOptions.Standard, Candidate(Slot.Trinket, KeyF)));
        Assert.Equal(Slot.Trinket, first.Selected);
        scheduler.NoteSent(1000, Slot.Trinket, KeyF);

        var second = scheduler.Advance(Input(2000, Frame([(Slot.Trinket, KeyF)], heartbeat: 2),
            Context(), PolicyOptions.Standard, Candidate(Slot.Trinket, KeyF)));
        Assert.Null(second.Selected);
        Assert.Equal(1, second.PolicySkipped);

        // After the shared 20 s lockout the next trinket is allowed again.
        var third = scheduler.Advance(Input(25_000, Frame([(Slot.Trinket, KeyF)], heartbeat: 3),
            Context(), PolicyOptions.Standard,
            Candidate(Slot.Trinket, KeyF, firstSeenMs: 24_900, lastChangedMs: 24_900)));
        Assert.Equal(Slot.Trinket, third.Selected);
        Assert.NotNull(memory);
    }

    // ---- cast / channel execution safety (v2.1) --------------------------

    [Fact]
    public void Channeling_Holds_The_Main_Rotation_With_Policy_On()
    {
        var scheduler = new ActionScheduler();
        var frame = Frame([(Slot.Main, KeyE)]);
        var plan = scheduler.Advance(Input(
            1000, frame,
            Context(cast: PlayerCastState.Channeling),
            PolicyOptions.Standard,
            Candidate(Slot.Main, KeyE)));

        Assert.Null(plan.Selected);
        Assert.Equal(ScheduleReason.ChannelHold, plan.Reason);
        Assert.Equal(1, plan.PolicyHeld);
        Assert.Contains("channel", plan.PolicyDetail);
    }

    [Fact]
    public void Casting_Holds_The_Main_Rotation_With_Intelligence_Off()
    {
        // The hard execution-safety gate is not knowledge filtering: it runs
        // with intelligence off too (options null), using the same predicate.
        var scheduler = new ActionScheduler();
        var frame = Frame([(Slot.Main, KeyE)]);
        var plan = scheduler.Advance(Input(
            1000, frame,
            Context(cast: PlayerCastState.Casting),
            options: null,
            Candidate(Slot.Main, KeyE)));

        Assert.Null(plan.Selected);
        Assert.Equal(ScheduleReason.CastHold, plan.Reason);
    }

    [Fact]
    public void Casting_Still_Passes_An_Interrupt_With_Intelligence_Off()
    {
        // Interrupts are off-GCD by design: holding them behind the player's
        // own cast would defeat the kick.
        var scheduler = new ActionScheduler();
        var frame = Frame([(Slot.Main, KeyE), (Slot.Interrupt, KeyF)]);
        var plan = scheduler.Advance(Input(
            1000, frame,
            Context(cast: PlayerCastState.Casting),
            options: null,
            Candidate(Slot.Main, KeyE),
            Candidate(Slot.Interrupt, KeyF, spellId: 6552)));

        Assert.Equal(Slot.Interrupt, plan.Selected);
        Assert.Equal(ScheduleReason.InterruptUrgency, plan.Reason);
    }

    [Fact]
    public void Stale_Interrupt_Suggestion_Does_Not_Block_Main()
    {
        // MaxDps still surfaces a kick, but the v5 sensors see no live cast:
        // the kick is skipped and the main rotation proceeds in the same tick.
        var scheduler = new ActionScheduler();
        var frame = Frame([(Slot.Main, KeyE), (Slot.Interrupt, KeyF)]);
        var plan = scheduler.Advance(Input(
            1000, frame,
            Context(targetCasting: TriState.No, range: Range((Slot.Interrupt, TriState.Yes))),
            PolicyOptions.Standard,
            Candidate(Slot.Main, KeyE),
            Candidate(Slot.Interrupt, KeyF, spellId: 6552)));

        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Equal(1, plan.PolicySkipped);
        Assert.Contains("no live target cast", plan.PolicyDetail);
    }

    [Fact]
    public void Range_Change_Revalidates_Next_Tick()
    {
        var scheduler = new ActionScheduler();
        var frame = Frame([(Slot.Main, KeyE)]);
        var c = Candidate(Slot.Main, KeyE, firstSeenMs: 900, lastChangedMs: 900);

        var skipped = scheduler.Advance(Input(
            1000, frame, Context(range: Range((Slot.Main, TriState.No))), PolicyOptions.Standard, c));
        Assert.Null(skipped.Selected);
        Assert.Equal(ScheduleReason.PolicySkip, skipped.Reason);

        var resumed = scheduler.Advance(Input(
            1200, frame, Context(range: Range((Slot.Main, TriState.Yes))), PolicyOptions.Standard, c));
        Assert.Equal(Slot.Main, resumed.Selected);
    }

    [Fact]
    public void Failed_Situational_Press_Is_Demoted_Behind_A_Fresh_Main()
    {
        // R2 (sustain-cd): SelfHeal is now the documented exception (a ready
        // heal must never be demoted — see SelfSustainCooldownTests). This
        // contract still holds for every other situational slot; an emergency
        // Defensive (Shield Block, OnGcd) stands in for the generic case (an
        // emergency bypasses the active-tier sequencing block, so the pending
        // demotion is what moves it behind Main).
        var scheduler = new ActionScheduler();
        var slots = new (Slot Slot, KeyStroke? Stroke)[] { (Slot.Main, KeyE), (Slot.Defensive, KeyR) };

        var first = scheduler.Advance(Input(1000, Frame(slots, heartbeat: 1),
            Context(hpValid: true, hp: 20), PolicyOptions.Standard,
            Candidate(Slot.Defensive, KeyR, spellId: 2565)));
        Assert.Equal(Slot.Defensive, first.Selected);
        scheduler.NoteSent(1000, Slot.Defensive, KeyR, 2565);

        // The defensive press never started a GCD and is still suggested: it
        // must not shadow the fresh main while the failure is being confirmed.
        var plan = scheduler.Advance(Input(1200, Frame(slots, heartbeat: 2),
            Context(hpValid: true, hp: 20), PolicyOptions.Standard,
            Candidate(Slot.Defensive, KeyR, spellId: 2565),
            Candidate(Slot.Main, KeyE)));

        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Equal(Slot.Main, plan.Actions[0].Slot);
        Assert.Contains(plan.Actions, a => a.Slot == Slot.Defensive);   // never removed
    }

    [Fact]
    public void Pending_Main_Press_Is_Not_Demoted()
    {
        var scheduler = new ActionScheduler();
        var slots = new (Slot Slot, KeyStroke? Stroke)[] { (Slot.Main, KeyE), (Slot.Offensive, KeyR) };

        scheduler.Advance(Input(1000, Frame(slots, heartbeat: 1), Context(),
            PolicyOptions.Standard, Candidate(Slot.Main, KeyE)));
        scheduler.NoteSent(1000, Slot.Main, KeyE);

        // Re-pressing the same main key is the player's own spell-queue
        // behaviour; demoting it would fire a different key into a GCD that
        // may be starting. The main stays first.
        var plan = scheduler.Advance(Input(1200, Frame(slots, heartbeat: 2), Context(),
            PolicyOptions.Standard,
            Candidate(Slot.Main, KeyE),
            Candidate(Slot.Offensive, KeyR, spellId: 1719)));

        Assert.Equal(Slot.Main, plan.Selected);
    }

    // ---- failure recovery ------------------------------------------------

    [Fact]
    public void Failed_Press_Without_Gcd_Is_Suppressed()
    {
        var scheduler = new ActionScheduler();
        Assert.Equal(Slot.Main, scheduler.Advance(Input(
            1000, Frame([(Slot.Main, KeyE)], heartbeat: 1), Context(), PolicyOptions.Standard,
            Candidate(Slot.Main, KeyE))).Selected);
        scheduler.NoteSent(1000, Slot.Main, KeyE);

        // A pace tick, then one retry (a transient failure is allowed one).
        Assert.Null(scheduler.Advance(Input(
            1100, Frame([(Slot.Main, KeyE)], heartbeat: 2), Context(), PolicyOptions.Standard,
            Candidate(Slot.Main, KeyE))).Selected);
        Assert.Equal(Slot.Main, scheduler.Advance(Input(
            1500, Frame([(Slot.Main, KeyE)], heartbeat: 3), Context(), PolicyOptions.Standard,
            Candidate(Slot.Main, KeyE))).Selected);
        scheduler.NoteSent(1500, Slot.Main, KeyE);

        // The retry also never starts a GCD: suppress the stroke briefly
        // instead of hammering it.
        var plan = scheduler.Advance(Input(
            2200, Frame([(Slot.Main, KeyE)], heartbeat: 4), Context(), PolicyOptions.Standard,
            Candidate(Slot.Main, KeyE)));

        Assert.Null(plan.Selected);
        Assert.Equal(1, scheduler.RejectionsDetected);
        Assert.Equal(ScheduleReason.Unavailable, plan.Reason);
    }

    [Fact]
    public void Vanished_Candidate_Is_Not_A_Failure()
    {
        // A successful press advances the rotation: the slot goes empty while
        // the GCD flag has not been observed yet. That must NOT be recorded as
        // a silent failure (the comment promised "vanished or changed").
        var scheduler = new ActionScheduler();
        scheduler.Advance(Input(1000, Frame([(Slot.Main, KeyE)], heartbeat: 1), Context(),
            PolicyOptions.Standard, Candidate(Slot.Main, KeyE)));
        scheduler.NoteSent(1000, Slot.Main, KeyE);

        // The suggestion vanished for a while.
        scheduler.Advance(Input(1200, Frame([], heartbeat: 2), Context(), PolicyOptions.Standard));

        // Well past RejectDetectMs the stroke is suggested again: no failure.
        var plan = scheduler.Advance(Input(1700, Frame([(Slot.Main, KeyE)], heartbeat: 3), Context(),
            PolicyOptions.Standard, Candidate(Slot.Main, KeyE)));

        Assert.Equal(0, scheduler.RejectionsDetected);
        Assert.Equal(Slot.Main, plan.Selected);
    }

    [Fact]
    public void Confirmed_Gcd_Is_Not_A_Failure()
    {
        var scheduler = new ActionScheduler();
        scheduler.Advance(Input(1000, Frame([(Slot.Main, KeyE)], heartbeat: 1), Context(), PolicyOptions.Standard,
            Candidate(Slot.Main, KeyE)));
        scheduler.NoteSent(1000, Slot.Main, KeyE);

        // The GCD appears on the next decoded frame: the press worked.
        scheduler.Advance(Input(1050, Frame([(Slot.Main, KeyE)], heartbeat: 2, onGcd: true), Context(),
            PolicyOptions.Standard, Candidate(Slot.Main, KeyE)));
        scheduler.Advance(Input(1700, Frame([(Slot.Main, KeyE)], heartbeat: 3), Context(),
            PolicyOptions.Standard, Candidate(Slot.Main, KeyE)));

        Assert.Equal(0, scheduler.RejectionsDetected);
    }

    [Fact]
    public void Changed_Candidate_Cancels_Failure_Detection()
    {
        var scheduler = new ActionScheduler();
        scheduler.Advance(Input(1000, Frame([(Slot.Main, KeyE)], heartbeat: 1), Context(), PolicyOptions.Standard,
            Candidate(Slot.Main, KeyE)));
        scheduler.NoteSent(1000, Slot.Main, KeyE);

        // The rotation moved on to a different stroke: the press did something.
        scheduler.Advance(Input(1700,
            Frame([(Slot.Main, KeyR)], heartbeat: 2), Context(), PolicyOptions.Standard,
            Candidate(Slot.Main, KeyR)));
        Assert.Equal(0, scheduler.RejectionsDetected);
    }

    [Fact]
    public void OffGcd_Ability_Is_Not_Rejection_Checked()
    {
        var scheduler = new ActionScheduler();
        scheduler.Advance(Input(1000, Frame([(Slot.Defensive, KeyR)], heartbeat: 1), Context(),
            PolicyOptions.Standard, Candidate(Slot.Defensive, KeyR, spellId: 871)));
        scheduler.NoteSent(1000, Slot.Defensive, KeyR, 871);

        scheduler.Advance(Input(1700, Frame([(Slot.Defensive, KeyR)], heartbeat: 2), Context(),
            PolicyOptions.Standard, Candidate(Slot.Defensive, KeyR, spellId: 871)));
        Assert.Equal(0, scheduler.RejectionsDetected);
    }

    [Fact]
    public void Repeated_Stroke_Inside_Window_Backs_Off()
    {
        var scheduler = new ActionScheduler();

        for (var i = 0; i < ActionScheduler.MaxAttemptsPerWindow; i++)
            scheduler.NoteSent(1000 + i * 10, Slot.Main, KeyE);

        var plan = scheduler.Advance(Input(1100, Frame([(Slot.Main, KeyE)], heartbeat: 2),
            Context(), PolicyOptions.Standard, Candidate(Slot.Main, KeyE)));
        Assert.Null(plan.Selected);
        Assert.Equal(ScheduleReason.RetryBackoff, plan.Reason);

        // The retry after the backoff works (a real send starts a GCD) ...
        scheduler.NoteSent(1150, Slot.Main, KeyE);
        scheduler.Advance(Input(1200, Frame([(Slot.Main, KeyE)], heartbeat: 3, onGcd: true),
            Context(), PolicyOptions.Standard, Candidate(Slot.Main, KeyE)));

        // ... and once the backoff window expires the stroke is eligible again.
        var later = scheduler.Advance(Input(3000, Frame([(Slot.Main, KeyE)], heartbeat: 4),
            Context(), PolicyOptions.Standard,
            Candidate(Slot.Main, KeyE, firstSeenMs: 2900, lastChangedMs: 2900)));
        Assert.Equal(Slot.Main, later.Selected);
    }

    [Fact]
    public void Suppressed_Rejection_Does_Not_Block_Other_Ranks()
    {
        var scheduler = new ActionScheduler();
        var slots = new (Slot Slot, KeyStroke? Stroke)[] { (Slot.Main, KeyE), (Slot.Offensive, KeyR) };
        scheduler.Advance(Input(1000, Frame(slots, heartbeat: 1), Context(), PolicyOptions.Standard,
            Candidate(Slot.Main, KeyE), Candidate(Slot.Offensive, KeyR, spellId: 1719)));
        scheduler.NoteSent(1000, Slot.Main, KeyE);

        var plan = scheduler.Advance(Input(1700, Frame(slots, heartbeat: 2), Context(), PolicyOptions.Standard,
            Candidate(Slot.Main, KeyE), Candidate(Slot.Offensive, KeyR, spellId: 1719)));

        // Main is suppressed as failed; the fresh offensive still fires.
        Assert.Equal(Slot.Offensive, plan.Selected);
    }
}
