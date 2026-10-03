using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Deterministic scheduler contract. Every time value is passed in explicitly
/// (fake clock) so each scenario is a pure sequence of ticks: A-&gt;A-&gt;A
/// repeats, A-&gt;B transitions, interrupt/defensive urgency, GCD edges, stale
/// frames, heartbeat loss, protocol mismatch, target loss, the minimum key
/// interval and repeated-unavailable suppression.
/// </summary>
public class ActionSchedulerTests
{
    private static readonly KeyStroke KeyE = new(0x45, false, false, false);
    private static readonly KeyStroke KeyR = new(0x52, false, false, false);
    private static readonly KeyStroke KeyF = new(0x46, false, false, false);
    private static readonly KeyStroke KeyX = new(0x58, false, false, false);
    private static readonly KeyStroke Key2 = new(0x32, false, false, false);
    private static readonly KeyStroke KeyW = new(0x57, false, false, false);

    private static KeyStroke SlotStroke(Slot slot) => new((byte)(0x41 + (int)slot), false, false, false);

    private static BridgeFrame Frame(
        (Slot Slot, KeyStroke? Stroke)[] slots,
        int heartbeat = 1,
        int version = PixelProtocol.SupportedVersion,
        BridgeState state = BridgeState.Active,
        bool inCombat = true,
        bool onGcd = false,
        bool hasTarget = true)
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
            Version = version,
            StatusFlags = flags,
            Slots = array,
        };
    }

    private static ActionCandidate Candidate(
        Slot slot,
        KeyStroke? stroke = null,
        bool enabled = true,
        bool actionable = true,
        long firstSeenMs = 0,
        long lastChangedMs = 0,
        bool everPressed = false) =>
        new(slot, stroke ?? SlotStroke(slot), enabled, actionable, firstSeenMs, lastChangedMs,
            LastPressedMs: 0, everPressed);

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
    };

    // ---------- link / protocol / frame freshness ----------

    [Fact]
    public void Stale_Frame_Null_Holds()
    {
        var plan = new ActionScheduler().Advance(Input(1000, null, Candidate(Slot.Main)));

        Assert.Equal(ScheduleReason.StaleFrame, plan.Reason);
        Assert.Empty(plan.Actions);
        Assert.Null(plan.Selected);
    }

    [Fact]
    public void Protocol_Version_Mismatch_Holds()
    {
        var frame = Frame([(Slot.Main, KeyE)], version: 2);

        var plan = new ActionScheduler().Advance(Input(0, frame, Candidate(Slot.Main, KeyE)));

        Assert.Equal(ScheduleReason.ProtocolMismatch, plan.Reason);
        Assert.Empty(plan.Actions);
    }

    [Fact]
    public void Frozen_Heartbeat_Is_Link_Loss_And_Recovers_On_Change()
    {
        var scheduler = new ActionScheduler();
        var frozen = Frame([(Slot.Main, KeyE)], heartbeat: 7);
        var candidate = Candidate(Slot.Main, KeyE);

        var healthy = scheduler.Advance(Input(0, frozen, candidate));
        Assert.Equal(Slot.Main, healthy.Selected);

        var stale = scheduler.Advance(Input(500, frozen, candidate));
        Assert.Equal(ScheduleReason.LinkLost, stale.Reason);
        Assert.Empty(stale.Actions);

        var resumed = Frame([(Slot.Main, KeyE)], heartbeat: 8);
        var recovered = scheduler.Advance(Input(533, resumed, candidate));
        Assert.Equal(Slot.Main, recovered.Selected);
    }

    [Fact]
    public void Link_Lost_Never_Fires_Across_A_Long_Frozen_Window()
    {
        var scheduler = new ActionScheduler();
        var candidate = Candidate(Slot.Main, KeyE);
        scheduler.Advance(Input(0, Frame([(Slot.Main, KeyE)], heartbeat: 3), candidate));

        var frozen = Frame([(Slot.Main, KeyE)], heartbeat: 3);
        for (var t = 500; t <= 5000; t += 33)
        {
            var plan = scheduler.Advance(Input(t, frozen, candidate));
            Assert.Equal(ScheduleReason.LinkLost, plan.Reason);
            Assert.False(plan.HasSelection);
        }
    }

    // ---------- repeated suggestions / transitions ----------

    [Fact]
    public void A_To_A_To_A_Presses_Once_Per_Minimum_Interval()
    {
        var scheduler = new ActionScheduler();
        var frame = Frame([(Slot.Main, KeyE)]);
        var candidate = Candidate(Slot.Main, KeyE);

        var first = scheduler.Advance(Input(0, frame, candidate));
        Assert.Equal(Slot.Main, first.Selected);
        scheduler.NoteSent(0, Slot.Main, KeyE);

        var early = scheduler.Advance(Input(33, frame, candidate));
        Assert.Null(early.Selected);
        Assert.Equal(ScheduleReason.MinInterval, early.Reason);

        var due = scheduler.Advance(Input(120, frame, candidate));
        Assert.Equal(Slot.Main, due.Selected);
        scheduler.NoteSent(120, Slot.Main, KeyE);

        var third = scheduler.Advance(Input(240, frame, candidate));
        Assert.Equal(Slot.Main, third.Selected);
    }

    [Fact]
    public void A_To_B_Transition_Selects_The_New_Stroke()
    {
        var scheduler = new ActionScheduler();
        scheduler.Advance(Input(0, Frame([(Slot.Main, KeyE)]), Candidate(Slot.Main, KeyE)));
        scheduler.NoteSent(0, Slot.Main, KeyE);

        var frame = Frame([(Slot.Main, KeyR)], heartbeat: 2);
        var plan = scheduler.Advance(Input(150, frame, Candidate(Slot.Main, KeyR)));

        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Equal(KeyR, plan.Actions[0].Stroke);
        Assert.Equal(ScheduleReason.MainRotation, plan.Reason);
    }

    [Fact]
    public void Order_Is_Independent_Of_Candidate_Input_Order()
    {
        var trinket = new KeyStroke(0x54, false, false, false);   // T
        var consumable = new KeyStroke(0x59, false, false, false); // Y
        var frame = Frame([(Slot.Main, KeyE), (Slot.Offensive, Key2), (Slot.Defensive, KeyX), (Slot.Interrupt, KeyF)]);
        var plan = new ActionScheduler().Advance(Input(0, frame,
            Candidate(Slot.Trinket, trinket),
            Candidate(Slot.Main, KeyE),
            Candidate(Slot.Interrupt, KeyF),
            Candidate(Slot.Defensive, KeyX),
            Candidate(Slot.Offensive, Key2),
            Candidate(Slot.Consumable, consumable)));

        Assert.Equal(
            [Slot.Interrupt, Slot.Defensive, Slot.Main, Slot.Offensive, Slot.Consumable, Slot.Trinket],
            plan.Actions.Select(a => a.Slot).ToArray());
    }

    [Fact]
    public void Duplicate_Stroke_Collapses_To_Highest_Rank()
    {
        var frame = Frame([(Slot.Main, KeyE), (Slot.Defensive, KeyE)]);
        var plan = new ActionScheduler().Advance(Input(0, frame,
            Candidate(Slot.Main, KeyE), Candidate(Slot.Defensive, KeyE)));

        Assert.Equal([Slot.Defensive], plan.Actions.Select(a => a.Slot).ToArray());
        Assert.Equal(KeyE, plan.Actions[0].Stroke);
    }

    [Fact]
    public void Disabled_Candidate_Is_Never_Selected()
    {
        var frame = Frame([(Slot.Main, KeyE)]);
        var plan = new ActionScheduler().Advance(Input(0, frame, Candidate(Slot.Main, KeyE, enabled: false)));

        Assert.Equal(ScheduleReason.NoCandidate, plan.Reason);
        Assert.Empty(plan.Actions);
    }

    // ---------- interrupt / defensive urgency ----------

    [Fact]
    public void Interrupt_Preempts_Main_And_Bypasses_Gcd_And_Minimum_Interval()
    {
        var scheduler = new ActionScheduler();
        scheduler.NoteSent(0, Slot.Main, KeyE); // a DPS key went out 10 ms ago

        var frame = Frame([(Slot.Main, KeyE), (Slot.Interrupt, KeyF)], onGcd: true);
        var plan = scheduler.Advance(Input(10, frame,
            Candidate(Slot.Main, KeyE), Candidate(Slot.Interrupt, KeyF)));

        Assert.Equal(Slot.Interrupt, plan.Selected);
        Assert.Equal(ScheduleReason.InterruptUrgency, plan.Reason);
        Assert.True(plan.Actions[0].BypassGcd);
    }

    [Fact]
    public void Repeated_Identical_Interrupt_Stroke_Respects_The_Minimum_Interval()
    {
        var scheduler = new ActionScheduler();
        var interrupt = Candidate(Slot.Interrupt, KeyF);

        var first = scheduler.Advance(Input(0, Frame([(Slot.Interrupt, KeyF)]), interrupt));
        Assert.Equal(Slot.Interrupt, first.Selected);
        scheduler.NoteSent(0, Slot.Interrupt, KeyF);

        var repeated = scheduler.Advance(Input(10, Frame([(Slot.Interrupt, KeyF)], heartbeat: 2), interrupt));
        Assert.Null(repeated.Selected);
        Assert.Equal(ScheduleReason.MinInterval, repeated.Reason);

        var due = scheduler.Advance(Input(120, Frame([(Slot.Interrupt, KeyF)], heartbeat: 3), interrupt));
        Assert.Equal(Slot.Interrupt, due.Selected);
    }

    [Fact]
    public void Gcd_Holds_The_Rotation_And_Releases_On_The_Falling_Edge()
    {
        var scheduler = new ActionScheduler();
        var candidate = Candidate(Slot.Main, KeyE);

        var held = scheduler.Advance(Input(0, Frame([(Slot.Main, KeyE)], onGcd: true), candidate));
        Assert.Equal(ScheduleReason.GcdHold, held.Reason);
        Assert.Empty(held.Actions);

        var fired = scheduler.Advance(Input(33, Frame([(Slot.Main, KeyE)], heartbeat: 2), candidate));
        Assert.Equal(Slot.Main, fired.Selected);
    }

    [Fact]
    public void Defensive_Emergency_Preempts_Main()
    {
        var frame = Frame([(Slot.Main, KeyE), (Slot.Defensive, KeyX)]);
        var plan = new ActionScheduler().Advance(Input(0, frame,
            Candidate(Slot.Main, KeyE), Candidate(Slot.Defensive, KeyX)));

        Assert.Equal(Slot.Defensive, plan.Selected);
        Assert.Equal(ScheduleReason.DefensiveUrgency, plan.Reason);
    }

    [Fact]
    public void Stale_Defensive_Does_Not_Block_Fresh_Main()
    {
        var frame = Frame([(Slot.Main, KeyE), (Slot.Defensive, KeyX)]);
        var plan = new ActionScheduler().Advance(Input(10_000, frame,
            Candidate(Slot.Defensive, KeyX, everPressed: true, lastChangedMs: 0),
            Candidate(Slot.Main, KeyE)));

        Assert.Equal(Slot.Main, plan.Selected);
        Assert.True(plan.StaleDemoted);
        // Never removed: the stale defensive stays behind the fresh main.
        Assert.Equal([Slot.Main, Slot.Defensive], plan.Actions.Select(a => a.Slot).ToArray());
    }

    [Fact]
    public void All_Stale_Candidates_Keep_Order_Without_Deadlock()
    {
        var frame = Frame([(Slot.Main, KeyE), (Slot.Defensive, KeyX)]);
        var plan = new ActionScheduler().Advance(Input(10_000, frame,
            Candidate(Slot.Defensive, KeyX, everPressed: true, lastChangedMs: 0),
            Candidate(Slot.Main, KeyE, everPressed: true, lastChangedMs: 0)));

        Assert.Equal([Slot.Defensive, Slot.Main], plan.Actions.Select(a => a.Slot).ToArray());
        Assert.False(plan.StaleDemoted);
        Assert.True(plan.Confidence <= 40); // 70 base - 40 stale penalty
    }

    // ---------- target / paused state ----------

    [Fact]
    public void Target_Lost_Holds_And_Recovers_Without_Stale_History()
    {
        var scheduler = new ActionScheduler();
        var candidate = Candidate(Slot.Main, KeyE);

        var lost = scheduler.Advance(Input(0, Frame([(Slot.Main, KeyE)], hasTarget: false), candidate));
        Assert.Equal(ScheduleReason.NoTarget, lost.Reason);
        Assert.Empty(lost.Actions);

        var back = scheduler.Advance(Input(100, Frame([(Slot.Main, KeyE)], heartbeat: 2), candidate));
        Assert.Equal(Slot.Main, back.Selected);
    }

    [Fact]
    public void Paused_Frame_Holds()
    {
        var frame = Frame([(Slot.Main, KeyE)], state: BridgeState.Paused);
        var plan = new ActionScheduler().Advance(Input(0, frame, Candidate(Slot.Main, KeyE)));

        Assert.Equal(ScheduleReason.Paused, plan.Reason);
        Assert.Empty(plan.Actions);
    }

    [Fact]
    public void Target_Regain_Clears_Unavailable_Suppression()
    {
        var scheduler = new ActionScheduler();
        var candidate = Candidate(Slot.Main, KeyE);
        scheduler.NoteAttempt(0, Slot.Main, KeyE, AttemptOutcome.FocusRequired);

        var lost = Frame([(Slot.Main, KeyE)], heartbeat: 2, hasTarget: false);
        scheduler.Advance(Input(33, lost, candidate));

        var back = Frame([(Slot.Main, KeyE)], heartbeat: 3);
        var plan = scheduler.Advance(Input(66, back, candidate));

        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Equal(0, plan.Suppressed);
    }

    // ---------- unavailable / repeated-unavailable suppression ----------

    [Fact]
    public void Repeated_Unavailable_Action_Falls_Through_And_Recovers_After_The_Window()
    {
        var scheduler = new ActionScheduler();
        var frame = Frame([(Slot.Main, KeyE), (Slot.Offensive, Key2)]);
        var main = Candidate(Slot.Main, KeyE);
        var offensive = Candidate(Slot.Offensive, Key2);

        // Main was rejected by an engine OS gate on the previous tick.
        scheduler.NoteAttempt(0, Slot.Main, KeyE, AttemptOutcome.PhysicalHold);

        var fallback = scheduler.Advance(Input(33, frame, main, offensive));
        Assert.Equal(Slot.Offensive, fallback.Selected);
        Assert.Equal(1, fallback.Suppressed);

        var later = Frame([(Slot.Main, KeyE), (Slot.Offensive, Key2)], heartbeat: 2);
        var retried = scheduler.Advance(Input(501, later, main, offensive));
        Assert.Equal(Slot.Main, retried.Selected);
        Assert.Equal(0, retried.Suppressed);
    }

    [Fact]
    public void Attempt_History_Is_Recorded_For_Diagnostics()
    {
        var scheduler = new ActionScheduler();
        scheduler.NoteAttempt(42, Slot.Defensive, KeyW, AttemptOutcome.MovementBound);

        var attempt = scheduler.LastAttempt;
        Assert.NotNull(attempt);
        Assert.Equal(Slot.Defensive, attempt.Value.Slot);
        Assert.Equal(KeyW, attempt.Value.Stroke);
        Assert.Equal(42, attempt.Value.AtMs);
        Assert.Equal(AttemptOutcome.MovementBound, attempt.Value.Outcome);

        scheduler.NoteSent(50, Slot.Main, KeyE);
        Assert.Equal((Slot.Main, KeyE, 50L), scheduler.LastSent);
    }

    // ---------- GCD-unavailable (protocol v1) repeats ----------

    [Fact]
    public void V1_NoGcd_Frames_Suppress_Identical_Repeat_Until_The_Repeat_Window()
    {
        var scheduler = new ActionScheduler();
        var candidate = Candidate(Slot.Main, KeyE);
        var first = scheduler.Advance(Input(0,
            Frame([(Slot.Main, KeyE)], version: PixelProtocol.SupportedVersionV1), candidate));
        Assert.Equal(Slot.Main, first.Selected);
        scheduler.NoteSent(0, Slot.Main, KeyE);

        var held = scheduler.Advance(Input(150,
            Frame([(Slot.Main, KeyE)], heartbeat: 2, version: PixelProtocol.SupportedVersionV1), candidate));
        Assert.Null(held.Selected);
        Assert.Equal(ScheduleReason.RepeatSuppressed, held.Reason);

        // Keep the link alive across the window, then the repeat is allowed.
        scheduler.Advance(Input(600,
            Frame([(Slot.Main, KeyE)], heartbeat: 3, version: PixelProtocol.SupportedVersionV1), candidate));
        var allowed = scheduler.Advance(Input(900,
            Frame([(Slot.Main, KeyE)], heartbeat: 4, version: PixelProtocol.SupportedVersionV1), candidate));
        Assert.Equal(Slot.Main, allowed.Selected);
    }

    [Fact]
    public void V4_Frames_Do_Not_Repeat_Suppress_Once_The_Gcd_Is_Clear()
    {
        var scheduler = new ActionScheduler();
        var candidate = Candidate(Slot.Main, KeyE);
        scheduler.Advance(Input(0, Frame([(Slot.Main, KeyE)]), candidate));
        scheduler.NoteSent(0, Slot.Main, KeyE);

        // 120 ms later, no GCD reported: a legitimate re-press of the same key.
        var plan = scheduler.Advance(Input(120, Frame([(Slot.Main, KeyE)], heartbeat: 2), candidate));

        Assert.Equal(Slot.Main, plan.Selected);
    }

    // ---------- movement binding metadata ----------

    [Fact]
    public void Movement_Bound_Head_Is_Still_Planned_But_Lowers_Confidence()
    {
        var frame = Frame([(Slot.Main, KeyW)]);
        var plan = new ActionScheduler().Advance(Input(0, frame, Candidate(Slot.Main, KeyW, actionable: false)));

        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Equal(50, plan.Confidence); // 70 base - 20 not-actionable
    }
}
