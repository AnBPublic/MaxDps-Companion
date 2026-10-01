using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// END-TO-END self-sustain acceptance: a real protocol-v5 wire frame (encoded
/// by <see cref="TestV5Frame"/>, decoded by <see cref="PixelProtocol"/>) flows
/// through the candidate tracker and the scheduler exactly as it does in the
/// engine, proving the Warrior case:
///
///   low HP + Solo ON + Impending Victory ready
///   -> candidate generated -> policy USE -> scheduler selects it -> (send)
///
/// and every negative case the feature must not break: healthy conservation,
/// cooldown/resource absence, out-of-range, Solo OFF, Intelligence OFF,
/// casting/channeling, an active GCD, and failure recovery.
/// The engine's PostMessage send itself is OS-gated and cannot be exercised
/// offline; the scheduler plan is the last deterministic stage before it and
/// the exact input to that send loop.
/// </summary>
public class SelfSustainEndToEndTests
{
    private const int MainSpell = 12294;          // Mortal Strike (identity only)
    private const int ImpendingVictory = 202168;  // Warrior solo self-heal
    private static readonly KeyStroke KeyE = new(0x45, false, false, false);
    private static readonly KeyStroke KeyH = new(0x48, false, false, false);

    /// <summary>Every slot enabled, as the default settings ship.</summary>
    private static readonly bool[] AllSlots = [true, true, true, true, true, true, true, true];

    private static PolicyOptions Solo => new() { SoloEnabled = true };

    private static BridgeFrame DecodeFrame(
        int heartbeat,
        int hp,
        TriState selfHealRange = TriState.Yes,
        bool hasSelfHeal = true,
        bool hasMain = true,
        bool hasTarget = true,
        bool onGcd = false,
        PlayerCastState cast = PlayerCastState.None)
    {
        var builder = new TestV5Frame()
            .Vitals(hp)
            .Cast(cast)
            .Target(inMelee: true, casting: false, interruptible: false)
            .Ranges((Slot.Main, TriState.Yes), (Slot.SelfHeal, selfHealRange))
            .ClassSpec("WARRIOR", 1);
        if (hasMain) builder.Slot(Slot.Main, KeyE.VirtualKey).SpellId(Slot.Main, MainSpell);
        if (hasSelfHeal) builder.Slot(Slot.SelfHeal, KeyH.VirtualKey).SpellId(Slot.SelfHeal, ImpendingVictory);

        var frame = PixelProtocol.Decode(builder.Build(heartbeat, onGcd: onGcd, hasTarget: hasTarget));
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
            OutOfCombatPermitted = true,
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

    private static PolicyVerdictEntry Verdict(SchedulePlan plan, Slot slot) =>
        Assert.Single(plan.Verdicts, entry => entry.Slot == slot);

    // ---- acceptance: low HP -> candidate -> USE -> scheduled -------------

    [Fact]
    public void Warrior_LowHp_Solo_ImpendingVictory_Reaches_SelfSustain_Plan()
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();

        var plan = Advance(scheduler, tracker, DecodeFrame(1, 50), Solo, now: 1000);

        Assert.Equal(Slot.SelfHeal, plan.Selected);
        Assert.Equal(ScheduleReason.SelfSustain, plan.Reason);
        Assert.Equal(75, plan.Confidence);
        var action = plan.Actions[0];
        Assert.Equal(Slot.SelfHeal, action.Slot);
        Assert.Equal(KeyH, action.Stroke);
        Assert.Equal(ImpendingVictory, action.SpellId);

        var verdict = Verdict(plan, Slot.SelfHeal);
        Assert.Equal(PolicyVerdict.Use, verdict.Verdict);
        Assert.Equal(ImpendingVictory, verdict.SpellId);
        Assert.Contains("below sustain", verdict.Reason);
        Assert.Contains("HP 50%", verdict.Reason);

        // The main rotation is present and waits for the next tick — the heal
        // preempts it, it never disappears.
        Assert.Contains(plan.Actions, a => a.Slot == Slot.Main);
    }

    [Fact]
    public void Warrior_LowHp_ImpendingVictory_Then_Gcd_Send_And_Return_To_Main()
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();

        // Tick 1: the heal is selected and the engine sends it.
        var first = Advance(scheduler, tracker, DecodeFrame(1, 50), Solo, now: 1000);
        Assert.Equal(Slot.SelfHeal, first.Selected);
        scheduler.NoteSent(1000, Slot.SelfHeal, KeyH, ImpendingVictory);

        // Tick 2: the GCD from the heal appears — the send is confirmed, no
        // rejection, and the GCD gate blocks the rest of the tick.
        var gcd = Advance(scheduler, tracker, DecodeFrame(2, 55, onGcd: true), Solo, now: 1050);
        Assert.Null(gcd.Selected);
        Assert.Equal(ScheduleReason.GcdHold, gcd.Reason);
        Assert.Equal(0, scheduler.RejectionsDetected);

        // Tick 3: the heal is on cooldown (slot empty) and HP recovered above
        // the sustain threshold — the normal MaxDps rotation resumes.
        var resumed = Advance(scheduler, tracker,
            DecodeFrame(3, 80, hasSelfHeal: false), Solo, now: 1400);
        Assert.Equal(Slot.Main, resumed.Selected);
        Assert.Equal(ScheduleReason.MainRotation, resumed.Reason);
    }

    [Fact]
    public void Warrior_Emergency_SelfHeal_Ranks_Above_Main()
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();

        var plan = Advance(scheduler, tracker, DecodeFrame(1, 20), Solo, now: 1000);

        Assert.Equal(Slot.SelfHeal, plan.Selected);
        Assert.Equal(ScheduleReason.EmergencySurvival, plan.Reason);
        var verdict = Verdict(plan, Slot.SelfHeal);
        Assert.True(verdict.Reason.Contains("emergency", StringComparison.OrdinalIgnoreCase));
    }

    // ---- negative cases ---------------------------------------------------

    [Theory]
    [InlineData(100)]
    [InlineData(95)]
    [InlineData(70)]
    public void Warrior_Healthy_Or_Above_Threshold_Conserves_The_Heal(int hp)
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();

        var plan = Advance(scheduler, tracker, DecodeFrame(1, hp), Solo, now: 1000);

        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Equal(ScheduleReason.MainRotation, plan.Reason);
        Assert.Equal(PolicyVerdict.Hold, Verdict(plan, Slot.SelfHeal).Verdict);
    }

    [Fact]
    public void Solo_Off_Produces_No_Independent_SelfSustain_Press()
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();

        var plan = Advance(scheduler, tracker, DecodeFrame(1, 50), new PolicyOptions { SoloEnabled = false }, now: 1000);

        Assert.Equal(Slot.Main, plan.Selected);
        var verdict = Verdict(plan, Slot.SelfHeal);
        Assert.Equal(PolicyVerdict.Hold, verdict.Verdict);
        Assert.Contains("solo mode off", verdict.Reason);
    }

    [Fact]
    public void Intelligence_Off_Excludes_The_Companion_Only_Slot_Entirely()
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();

        var plan = Advance(scheduler, tracker, DecodeFrame(1, 50), options: null, now: 1000);

        Assert.Equal(Slot.Main, plan.Selected);
        Assert.DoesNotContain(plan.Actions, a => a.Slot == Slot.SelfHeal);
        Assert.DoesNotContain(plan.Verdicts, v => v.Slot == Slot.SelfHeal);
    }

    [Fact]
    public void ImpendingVictory_Out_Of_Range_Is_Skipped_And_Main_Proceeds()
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();

        var plan = Advance(scheduler, tracker,
            DecodeFrame(1, 50, selfHealRange: TriState.No), Solo, now: 1000);

        Assert.Equal(Slot.Main, plan.Selected);
        var verdict = Verdict(plan, Slot.SelfHeal);
        // v2.6: a confirmed out-of-range self-heal is Unavailable (structural),
        // not a deliberate Skip.
        Assert.Equal(PolicyVerdict.Unavailable, verdict.Verdict);
        Assert.Contains("out of ability range", verdict.Reason);
    }

    [Fact]
    public void ImpendingVictory_On_Cooldown_Is_Absent_From_The_Candidate_Set()
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();

        var plan = Advance(scheduler, tracker,
            DecodeFrame(1, 50, hasSelfHeal: false), Solo, now: 1000);

        Assert.Equal(Slot.Main, plan.Selected);
        Assert.DoesNotContain(plan.Verdicts, v => v.Slot == Slot.SelfHeal);
    }

    [Fact]
    public void No_Target_Holds_Everything()
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();

        var plan = Advance(scheduler, tracker, DecodeFrame(1, 50, hasTarget: false), Solo, now: 1000);

        Assert.Null(plan.Selected);
        Assert.Equal(ScheduleReason.NoTarget, plan.Reason);
    }

    [Fact]
    public void Casting_Holds_The_SelfHeal_And_The_Main()
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();

        var plan = Advance(scheduler, tracker,
            DecodeFrame(1, 50, cast: PlayerCastState.Casting), Solo, now: 1000);

        Assert.Null(plan.Selected);
        Assert.Equal(ScheduleReason.CastHold, plan.Reason);
        Assert.Equal(PolicyVerdict.Hold, Verdict(plan, Slot.SelfHeal).Verdict);
    }

    // ---- failure recovery -------------------------------------------------

    [Fact]
    public void Failed_SelfHeal_Is_Bounded_And_The_Main_Rotation_Continues()
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();

        var first = Advance(scheduler, tracker, DecodeFrame(1, 50), Solo, now: 1000);
        Assert.Equal(Slot.SelfHeal, first.Selected);
        scheduler.NoteSent(1000, Slot.SelfHeal, KeyH, ImpendingVictory);

        // The press never started a GCD and the heal is still suggested: past
        // RejectDetectMs the scheduler records the failure and suppresses the
        // stroke; the fresh main is selected instead.
        var plan = Advance(scheduler, tracker, DecodeFrame(3, 50), Solo, now: 1700);

        Assert.Equal(1, scheduler.RejectionsDetected);
        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Equal(ScheduleReason.MainRotation, plan.Reason);

        // Bounded: well after the suppression the same heal may be attempted
        // again (retry decay), and the main never disappeared in between.
        var retry = Advance(scheduler, tracker, DecodeFrame(4, 50), Solo, now: 3500);
        Assert.Contains(retry.Actions, a => a.Slot == Slot.Main);
    }

    [Fact]
    public void SelfHeal_Does_Not_Block_A_Main_Only_Tick()
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();

        var plan = Advance(scheduler, tracker,
            DecodeFrame(1, 90, hasSelfHeal: false), Solo, now: 1000);

        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Single(plan.Actions);
    }
}
