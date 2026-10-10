using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// T6: routing + scheduler deltas. Covers the v3.5 movement/CC routing
/// (<see cref="CandidateProviders.For"/>), the last-seen candidate set with its
/// rotation TTL (<see cref="CandidateTracker"/>), and the scheduler's GCD
/// bypass / (slot, stroke, spellId) backoff / transition clearing
/// (<see cref="ActionScheduler"/>).
/// </summary>
public class T6RoutingSchedulerTests
{
    private static KeyStroke KeyE => new(0x45, false, false, false);
    private static KeyStroke KeyR => new(0x52, false, false, false);
    private static KeyStroke KeyF => new(0x46, false, false, false);

    private static readonly bool[] AllEnabled = [true, true, true, true, true, true, true, true];

    private static AbilityCatalog Catalog => AbilityCatalog.Default;

    private static AbilityDefinition Known(int spellId) => Catalog.TryGet(spellId)!;

    /// <summary>A synthetic definition for the flag cases the pinned catalog cannot express.</summary>
    private static AbilityDefinition Synthetic(
        AbilityPurpose purpose, bool emergencyEscape = false, bool neverAutomatic = false) =>
        new(900100, "Synthetic", AbilityCategory.Mobility, purpose, DefensiveTier.None, GcdKind.OnGcd,
            RangeKind.Melee, CastKind.Instant, 0, 0, 0, false, RangeRequirement.Any, false, null, null,
            false, neverAutomatic, null, 0, UnknownPolicy.Hold, [], [], null, null)
        {
            EmergencyEscape = emergencyEscape,
        };

    // ---- routing ----------------------------------------------------------

    [Fact]
    public void GapCloser_Routes_To_Mobility()
    {
        Assert.Same(CandidateProviders.Mobility, CandidateProviders.For(Slot.Mobility, Known(100))); // Charge
    }

    [Fact]
    public void Movement_Routes_To_Mobility()
    {
        Assert.Same(CandidateProviders.Mobility, CandidateProviders.For(Slot.Mobility, Known(1850))); // Dash
    }

    [Fact]
    public void Plain_Escape_Routes_To_Mobility()
    {
        // An auto-flagged escape (no emergencyEscape, not manual-by-design) is a
        // target-reaching movement tool.
        var ability = Synthetic(AbilityPurpose.Escape);
        Assert.Same(CandidateProviders.Mobility, CandidateProviders.For(Slot.Mobility, ability));
    }

    [Fact]
    public void Emergency_Escape_Flag_Routes_To_Defensive()
    {
        var ability = Synthetic(AbilityPurpose.Escape, emergencyEscape: true);
        Assert.Same(CandidateProviders.Defensive, CandidateProviders.For(Slot.Defensive, ability));
    }

    [Fact]
    public void Manual_Escape_Stays_On_Defensive()
    {
        // Vanish is a curated manual-by-design escape: an emergency button, not
        // a gap closer (pinned by RegistryDecisionScenarioTests).
        Assert.Same(CandidateProviders.Defensive, CandidateProviders.For(Slot.Defensive, Known(1856)));
    }

    [Fact]
    public void Cc_On_The_Reused_Interrupt_Slot_Routes_To_CrowdControl()
    {
        Assert.Same(CandidateProviders.CrowdControl, CandidateProviders.For(Slot.Interrupt, Known(853)));
    }

    [Fact]
    public void Cc_On_A_NonInterrupt_Slot_Stays_On_Utility()
    {
        Assert.Same(CandidateProviders.Utility, CandidateProviders.For(Slot.Offensive, Known(853)));
    }

    [Fact]
    public void Survival_And_Offensive_Routing_Is_Unchanged()
    {
        Assert.Same(CandidateProviders.Defensive, CandidateProviders.For(Slot.Defensive, Known(871))); // Shield Wall
        Assert.Same(CandidateProviders.Offensive, CandidateProviders.For(Slot.Offensive, Known(228920))); // Ravager
    }

    // ---- last-seen set / TTL ---------------------------------------------

    private static BridgeFrame Frame(params (Slot Slot, KeyStroke Stroke, int SpellId)[] slots)
    {
        var strokes = new KeyStroke?[PixelProtocol.SlotCount];
        var spellIds = new int[PixelProtocol.SlotCount];
        foreach (var (slot, stroke, spellId) in slots)
        {
            strokes[(int)slot] = stroke;
            spellIds[(int)slot] = spellId;
        }
        return new BridgeFrame
        {
            State = BridgeState.Active,
            Heartbeat = 1,
            Version = PixelProtocol.SupportedVersion,
            StatusFlags = PixelProtocol.StatusFlagHasTarget,
            Slots = strokes,
            SpellIds = spellIds,
        };
    }

    [Fact]
    public void Default_Ttl_Is_One_And_A_Half_Rotations()
    {
        Assert.Equal(1000, CandidateTracker.DefaultTtlMs); // TtlMsFor(33)=max(1000,99) floor
    }

    [Fact]
    public void Ttl_Snapshot_Keeps_A_Rotated_Sibling_Then_Drops_It()
    {
        var tracker = new CandidateTracker();
        tracker.Update(Frame((Slot.Mobility, KeyE, 100)), 1000);
        tracker.Update(Frame((Slot.Mobility, KeyR, 200)), 1100); // the bridge rotated the slot

        // Legacy snapshot: only the frame's present candidate.
        var present = tracker.Snapshot(AllEnabled);
        Assert.Equal(200, Assert.Single(present).SpellId);

        // TTL snapshot: both identities (same slot, different spell id) while
        // inside the window.
        var window = tracker.Snapshot(AllEnabled, 1200, CandidateTracker.DefaultTtlMs);
        Assert.Equal(2, window.Length);
        Assert.Contains(window, c => c.SpellId == 100);
        Assert.Contains(window, c => c.SpellId == 200);

        // Beyond the TTL the rotated-away candidate is gone (never pressed).
        var expired = tracker.Snapshot(AllEnabled, 1000 + CandidateTracker.DefaultTtlMs + 1, CandidateTracker.DefaultTtlMs);
        Assert.Equal(200, Assert.Single(expired).SpellId);
    }

    [Fact]
    public void Ttl_Is_Keyed_By_Slot_And_SpellId()
    {
        var tracker = new CandidateTracker();
        tracker.Update(Frame((Slot.Main, KeyE, 111), (Slot.Offensive, KeyR, 222)), 1000);
        tracker.Update(Frame((Slot.Main, KeyF, 333)), 1100);

        var snapshot = tracker.Snapshot(AllEnabled, 1150, CandidateTracker.DefaultTtlMs);
        Assert.Equal(3, snapshot.Length);
        Assert.Contains(snapshot, c => c.Slot == Slot.Main && c.SpellId == 111);
        Assert.Contains(snapshot, c => c.Slot == Slot.Main && c.SpellId == 333);
        Assert.Contains(snapshot, c => c.Slot == Slot.Offensive && c.SpellId == 222);
    }

    [Fact]
    public void NotePressed_Marks_The_Exact_Spell_Identity()
    {
        var tracker = new CandidateTracker();
        tracker.Update(Frame((Slot.Mobility, KeyE, 100)), 1000);
        tracker.Update(Frame((Slot.Mobility, KeyR, 200)), 1100);
        tracker.NotePressed(Slot.Mobility, 1200, spellId: 100);

        var snapshot = tracker.Snapshot(AllEnabled, 1250, CandidateTracker.DefaultTtlMs);
        Assert.True(snapshot.Single(c => c.SpellId == 100).EverPressed);
        Assert.False(snapshot.Single(c => c.SpellId == 200).EverPressed);
    }

    // ---- scheduler: GCD bypass, scoped backoff, transitions ----------------

    private static BridgeFrame SFrame(params (Slot Slot, KeyStroke Stroke)[] slots) => BuildFrame(
        slots, PixelProtocol.StatusFlagInCombat | PixelProtocol.StatusFlagHasTarget | PixelProtocol.StatusFlagOnGcd);

    private static BridgeFrame PlainFrame(params (Slot Slot, KeyStroke Stroke)[] slots) => BuildFrame(
        slots, PixelProtocol.StatusFlagInCombat | PixelProtocol.StatusFlagHasTarget);

    private static BridgeFrame NoTargetFrame(params (Slot Slot, KeyStroke Stroke)[] slots) => BuildFrame(
        slots, PixelProtocol.StatusFlagInCombat);

    private static BridgeFrame OutOfCombatFrame(params (Slot Slot, KeyStroke Stroke)[] slots) => BuildFrame(
        slots, PixelProtocol.StatusFlagHasTarget);

    private static BridgeFrame BuildFrame((Slot Slot, KeyStroke Stroke)[] slots, int statusFlags)
    {
        var array = new KeyStroke?[PixelProtocol.SlotCount];
        foreach (var (slot, stroke) in slots) array[(int)slot] = stroke;
        return new BridgeFrame
        {
            State = BridgeState.Active,
            Heartbeat = 1,
            Version = PixelProtocol.SupportedVersion,
            StatusFlags = statusFlags,
            Slots = array,
        };
    }

    private static CombatContext Context(int hp = 100, bool hpValid = true, DefensiveUrgency urgency = DefensiveUrgency.Red) =>
        new()
        {
            HpValid = hpValid,
            HpPct = hp,
            TargetCasting = TriState.No,
            TargetInMelee = TriState.Unknown,
            SlotRange = new TriState[PixelProtocol.SlotCount],
            SlotBuffActive = new TriState[PixelProtocol.SlotCount],
            ContextValid = true,
            DefensiveUrgency = urgency,
        };

    private static ActionCandidate Candidate(Slot slot, KeyStroke stroke, int spellId, long firstSeen = 0, long lastChanged = 0) =>
        new(slot, stroke, true, true, firstSeen, lastChanged, 0, false, spellId);

    private static ScheduleInput Input(
        long now, BridgeFrame frame, CombatContext context, PolicyOptions? options, params ActionCandidate[] candidates) =>
        new()
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
            Catalog = options is null ? null : Catalog,
        };

    [Fact]
    public void Gcd_Bypass_Extends_To_Emergency_Verdicts()
    {
        var scheduler = new ActionScheduler();
        var frame = SFrame((Slot.Defensive, KeyR)); // OnGcd
        var plan = scheduler.Advance(Input(1000, frame, Context(hp: 20), PolicyOptions.Standard,
            Candidate(Slot.Defensive, KeyR, 871)));

        Assert.Equal(Slot.Defensive, plan.Selected);
        Assert.Equal(ScheduleReason.EmergencySurvival, plan.Reason);
    }

    [Fact]
    public void Gcd_Bypass_Extends_To_Registry_OffGcd()
    {
        var scheduler = new ActionScheduler();
        var frame = SFrame((Slot.Defensive, KeyR)); // OnGcd
        var plan = scheduler.Advance(Input(1000, frame, Context(hp: 60, urgency: DefensiveUrgency.Red), PolicyOptions.Standard,
            Candidate(Slot.Defensive, KeyR, 871)));

        Assert.Equal(Slot.Defensive, plan.Selected);
        Assert.Equal(ScheduleReason.DefensiveUrgency, plan.Reason);
    }

    [Fact]
    public void Gcd_Still_Holds_An_OnGcd_Unknown_Candidate()
    {
        var scheduler = new ActionScheduler();
        var frame = SFrame((Slot.Main, KeyE)); // OnGcd
        var plan = scheduler.Advance(Input(1000, frame, Context(), PolicyOptions.Standard,
            Candidate(Slot.Main, KeyE, spellId: 0)));

        Assert.Null(plan.Selected);
        Assert.Equal(ScheduleReason.GcdHold, plan.Reason);
    }

    [Fact]
    public void Backoff_Is_Keyed_By_SpellId_So_A_Sibling_Is_Not_Suppressed()
    {
        var scheduler = new ActionScheduler();
        for (var i = 0; i < ActionScheduler.MaxAttemptsPerWindow; i++)
            scheduler.NoteSent(1000 + i * 5, Slot.Main, KeyE, spellId: 111);

        // The failed identity backs off ...
        var backedOff = scheduler.Advance(Input(1200, PlainFrame((Slot.Main, KeyE)), Context(), null,
            Candidate(Slot.Main, KeyE, 111, firstSeen: 1000, lastChanged: 1000)));
        Assert.Equal(ScheduleReason.RetryBackoff, backedOff.Reason);

        // ... a sibling rotation candidate on the same stroke/spell slot does not.
        var sibling = scheduler.Advance(Input(1200, PlainFrame((Slot.Main, KeyE)), Context(), null,
            Candidate(Slot.Main, KeyE, 222, firstSeen: 1000, lastChanged: 1000)));
        Assert.Equal(Slot.Main, sibling.Selected);
    }

    [Fact]
    public void Combat_Transition_Clears_Os_Gate_Suppression()
    {
        var scheduler = new ActionScheduler();
        var candidate = Candidate(Slot.Main, KeyE, 0, firstSeen: 1000, lastChanged: 1000);
        scheduler.Advance(Input(1000, PlainFrame((Slot.Main, KeyE)), Context(), null, candidate));

        scheduler.NoteAttempt(1000, Slot.Main, KeyE, AttemptOutcome.MovementBound, 0);
        var blocked = scheduler.Advance(Input(1050, PlainFrame((Slot.Main, KeyE)), Context(), null, candidate));
        Assert.Equal(ScheduleReason.Unavailable, blocked.Reason);

        // inCombat true -> false is a transition: suppression cleared.
        var outOfCombat = OutOfCombatFrame((Slot.Main, KeyE));
        var freed = scheduler.Advance(Input(1100, outOfCombat, Context(), null, candidate));
        Assert.Equal(Slot.Main, freed.Selected);
    }

    [Fact]
    public void Target_Change_Clears_Os_Gate_Suppression()
    {
        var scheduler = new ActionScheduler();
        var candidate = Candidate(Slot.Main, KeyE, 0, firstSeen: 1000, lastChanged: 1000);
        scheduler.Advance(Input(1000, PlainFrame((Slot.Main, KeyE)), Context(), null, candidate));

        scheduler.NoteAttempt(1000, Slot.Main, KeyE, AttemptOutcome.MovementBound, 0);
        var blocked = scheduler.Advance(Input(1050, PlainFrame((Slot.Main, KeyE)), Context(), null, candidate));
        Assert.Equal(ScheduleReason.Unavailable, blocked.Reason);

        // Target lost (a change) clears suppression; the next target frame fires.
        scheduler.Advance(Input(1080, NoTargetFrame((Slot.Main, KeyE)), Context(), null, candidate));
        var freed = scheduler.Advance(Input(1120, PlainFrame((Slot.Main, KeyE)), Context(), null, candidate));
        Assert.Equal(Slot.Main, freed.Selected);
    }

    // ---- scheduler: multi-candidate choice (rotation) ----------------------

    [Fact]
    public void Rotated_Slot_Chooses_The_Use_Verdict_Over_A_Held_Sibling()
    {
        var scheduler = new ActionScheduler();
        // Two recent candidates for the Main slot: the known offensive is
        // user-disabled (Skip), the unknown main uses (Use). The scheduler must
        // evaluate both and select the Use.
        var options = new PolicyOptions
        {
            Abilities = AbilityPolicy.Default.With(228920, enabled: false, defaultEnabled: true),
        };
        var plan = scheduler.Advance(Input(1000, PlainFrame((Slot.Main, KeyE), (Slot.Main, KeyF)), Context(), options,
            Candidate(Slot.Main, KeyE, 228920, firstSeen: 900, lastChanged: 900),
            Candidate(Slot.Main, KeyF, spellId: 0, firstSeen: 900, lastChanged: 900)));

        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Equal(KeyF, plan.Actions[0].Stroke);
    }
}
