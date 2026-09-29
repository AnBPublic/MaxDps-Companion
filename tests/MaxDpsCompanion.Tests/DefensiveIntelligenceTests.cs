using System.Drawing;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// v2.3 defensive intelligence: the MaxDps urgency block over the wire, the
/// tier/urgency policy matrix, the per-ability user policy (OFF = never,
/// ON = eligible) and the scheduler consuming both. Urgency values always
/// mirror what the bridge would stage from the vendor glow curve, so the
/// contexts here are internally consistent (HP and urgency agree unless the
/// test itself overrides the emergency threshold).
/// </summary>
public class DefensiveIntelligenceTests
{
    private const long Now = 10_000;
    private static AbilityCatalog Catalog => AbilityCatalog.Default;

    private static BridgeFrame Decode(TestV5Frame frame, int heartbeat = 1, int version = 0) =>
        PixelProtocol.Decode(frame.Build(heartbeat, version: version))
        ?? throw new InvalidOperationException("v6 frame did not decode");

    private static CombatContext Context(
        bool hpValid = true,
        int hp = 100,
        DefensiveUrgency urgency = DefensiveUrgency.White,
        DefensiveUrgency staggerUrgency = DefensiveUrgency.Unknown,
        bool catalogSource = false,
        PlayerCastState cast = PlayerCastState.None,
        TriState targetCasting = TriState.No,
        TriState[]? range = null) => new()
    {
        HpValid = hpValid,
        HpPct = hp,
        Cast = cast,
        TargetCasting = targetCasting,
        TargetCastInterruptible = TriState.Unknown,
        TargetInMelee = TriState.Unknown,
        SlotRange = range ?? new TriState[PixelProtocol.SlotCount],
        SlotBuffActive = new TriState[PixelProtocol.SlotCount],
        DefensiveUrgency = urgency,
        StaggerUrgency = staggerUrgency,
        DefensiveCatalogSource = catalogSource,
        ContextValid = true,
    };

    private static PolicyDecision Evaluate(
        Slot slot,
        int spellId,
        CombatContext context,
        PolicyOptions? options = null,
        PolicyMemory? memory = null,
        bool hasTarget = true) =>
        PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = slot,
            SpellId = spellId,
            Context = context,
            Options = options ?? PolicyOptions.Standard,
            Memory = memory ?? new PolicyMemory(),
            NowMs = Now,
            InCombat = true,
            HasTarget = hasTarget,
        }, Catalog);

    private static PolicyOptions Solo => new() { SoloEnabled = true };

    private static TriState[] Range(params (Slot Slot, TriState State)[] entries)
    {
        var range = new TriState[PixelProtocol.SlotCount];
        foreach (var (slot, state) in entries) range[(int)slot] = state;
        return range;
    }

    // ---- protocol decode ---------------------------------------------------

    [Fact]
    public void Additive_Urgency_Block_Is_Read_From_The_Version_5_Wire()
    {
        // The shipped addon keeps the v5 version nibble and writes the urgency
        // block into the reserved nibbles: a v2.3 companion reads it, and a
        // pre-2.3 companion exe still decodes the same frame (it ignores the
        // reserved nibbles and its extension checksum already covered them).
        var frame = Decode(new TestV5Frame()
            .Slot(Slot.Defensive, 0x52)
            .SpellId(Slot.Defensive, 871)
            .Vitals(45)
            .Urgency(DefensiveUrgency.Orange, DefensiveUrgency.Red, catalogSource: true));

        Assert.Equal(5, frame.Version);
        Assert.Equal(DefensiveUrgency.Orange, frame.DefensiveUrgency);
        Assert.Equal(DefensiveUrgency.Red, frame.StaggerUrgency);
        Assert.True(frame.DefensiveCatalogSource);
        Assert.True(frame.ContextValid);

        var context = CombatContext.FromFrame(frame);
        Assert.Equal(DefensiveUrgency.Orange, context.DefensiveUrgency);
        Assert.Equal(DefensiveUrgency.Red, context.StaggerUrgency);
        Assert.False(context.MaxDpsDefensiveRecommendation);
    }

    [Fact]
    public void Explicit_Version_6_Nibble_Is_Accepted()
    {
        // Forward compatibility: an encoder that ever bumps the version nibble
        // to 6 uses the same 35-cell layout and decodes identically.
        var frame = Decode(new TestV5Frame()
            .Slot(Slot.Defensive, 0x52)
            .SpellId(Slot.Defensive, 871)
            .Vitals(45)
            .Urgency(DefensiveUrgency.Orange, DefensiveUrgency.Red, catalogSource: true), version: 6);

        Assert.Equal(6, frame.Version);
        Assert.Equal(DefensiveUrgency.Orange, frame.DefensiveUrgency);
        Assert.Equal(DefensiveUrgency.Red, frame.StaggerUrgency);
        Assert.True(frame.DefensiveCatalogSource);
    }

    [Fact]
    public void Legacy_Encoder_Reserved_Nibbles_Read_Unknown()
    {
        // A pre-2.3 addon never writes the urgency nibbles (they are reserved
        // and stay 0). The companion must decode the frame and treat urgency
        // as UNKNOWN — never White, never Red.
        var frame = Decode(new TestV5Frame()
            .Slot(Slot.Defensive, 0x52)
            .SpellId(Slot.Defensive, 871)
            .Vitals(20));

        Assert.Equal(5, frame.Version);
        Assert.Equal(DefensiveUrgency.Unknown, frame.DefensiveUrgency);
        Assert.Equal(DefensiveUrgency.Unknown, frame.StaggerUrgency);
        Assert.False(frame.DefensiveCatalogSource);
        Assert.True(frame.ContextValid);
    }

    [Fact]
    public void Reserved_Urgency_Nibbles_Decode_Unknown()
    {
        var frame = Decode(new TestV5Frame()
            .Vitals(20)
            .Urgency((DefensiveUrgency)15, (DefensiveUrgency)9));

        Assert.Equal(DefensiveUrgency.Unknown, frame.DefensiveUrgency);
        Assert.Equal(DefensiveUrgency.Unknown, frame.StaggerUrgency);
    }

    [Fact]
    public void Urgency_Block_Is_Covered_By_The_Extension_Checksum()
    {
        // Flip a cell 31 G nibble without recomputing the checksum: the frame
        // must be rejected as torn (the decoder sums cells 11-33).
        var clean = new TestV5Frame().Vitals(20).Urgency(DefensiveUrgency.Red).Build(1);
        Assert.NotNull(PixelProtocol.Decode(clean));

        var tampered = new TestV5Frame().Vitals(20).Urgency(DefensiveUrgency.White).Build(1);
        // Copy the checksum + commit from the clean frame so only the urgency
        // nibble differs.
        tampered[PixelProtocol.ExtensionCellIndex] = clean[PixelProtocol.ExtensionCellIndex];
        tampered[PixelProtocol.RangeCellIndex2] = Color.FromArgb(0, 1 * 17, 0);   // White nibble, stale checksum
        Assert.Null(PixelProtocol.Decode(tampered));
    }

    // ---- policy matrix: white / yellow / orange / red / unknown -------------

    [Fact]
    public void White_Holds_Even_A_MaxDps_Recommended_Minor()
    {
        // Full health: MaxDps stages no glow at all (curve alpha 0). The
        // hard invariant: no automatic defensive use.
        var result = Evaluate(Slot.Defensive, 1966, Context(hp: 100, urgency: DefensiveUrgency.White));
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("white", result.Reason);
    }

    [Fact]
    public void Yellow_Allows_A_Recommended_Short_Defensive()
    {
        var result = Evaluate(Slot.Defensive, 1966, Context(hp: 80, urgency: DefensiveUrgency.Yellow));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.Contains("short-CD mitigation", result.Reason);
    }

    [Fact]
    public void Yellow_Holds_A_Major()
    {
        var result = Evaluate(Slot.Defensive, 871, Context(hp: 80, urgency: DefensiveUrgency.Yellow));
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("major mitigation", result.Reason);
    }

    [Fact]
    public void Orange_Holds_A_Catalog_Major_But_Allows_A_MaxDps_Recommended_One()
    {
        // Companion gap-fill (catalog source): the major is not MaxDps's own
        // recommendation, so Orange is too early.
        var gapFill = Evaluate(Slot.Defensive, 871,
            Context(hp: 45, urgency: DefensiveUrgency.Orange, catalogSource: true));
        Assert.Equal(PolicyVerdict.Hold, gapFill.Verdict);
        Assert.Contains("needs Red", gapFill.Reason);

        // MaxDps recommends the exact ability: the requirement drops one stage
        // (Red -> Orange) but never below Yellow.
        var recommended = Evaluate(Slot.Defensive, 871,
            Context(hp: 45, urgency: DefensiveUrgency.Orange, catalogSource: false));
        Assert.Equal(PolicyVerdict.Use, recommended.Verdict);
    }

    [Fact]
    public void Red_Allows_A_Major_Without_MaxDps_Recommendation()
    {
        // Isolate the urgency gate from the emergency-HP override (default
        // 35%): with a lowered emergency threshold, HP 25 + Red still uses a
        // catalog gap-fill major on the strength of the urgency alone.
        var options = new PolicyOptions { EmergencyHpPct = 20 };
        var result = Evaluate(Slot.Defensive, 871,
            Context(hp: 25, urgency: DefensiveUrgency.Red, catalogSource: true), options);
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.False(result.Emergency);
        Assert.Contains("catalog defensive gap-fill", result.Reason);
    }

    [Fact]
    public void Emergency_Hp_Overrides_The_Urgency_Gate()
    {
        // Documented survival override: at/below the emergency threshold the
        // HP rule wins even when the stage is only Orange.
        var result = Evaluate(Slot.Defensive, 871,
            Context(hp: 32, urgency: DefensiveUrgency.Orange, catalogSource: true));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.True(result.Emergency);
    }

    [Fact]
    public void Unknown_Urgency_Holds_A_Major_And_Keeps_A_Minor()
    {
        // v2.6: unknown urgency on a MAJOR is Unknown (the context is unknown),
        // while a short-CD minor keeps the legacy gates and still uses.
        var major = Evaluate(Slot.Defensive, 871, Context(hpValid: false, urgency: DefensiveUrgency.Unknown));
        Assert.Equal(PolicyVerdict.Unknown, major.Verdict);
        Assert.Contains("urgency unknown", major.Reason);

        var minor = Evaluate(Slot.Defensive, 1966, Context(hpValid: false, urgency: DefensiveUrgency.Unknown));
        Assert.Equal(PolicyVerdict.Use, minor.Verdict);
    }

    [Fact]
    public void Stagger_Urgency_Overrides_Hp_For_Purifying_Brew()
    {
        // Purifying Brew (119582) is MaxDps's one stagger-coloured defensive:
        // full HP (White) but heavy stagger (Red) must still use.
        var result = Evaluate(Slot.Defensive, 119582,
            Context(hp: 100, urgency: DefensiveUrgency.White, staggerUrgency: DefensiveUrgency.Red));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
    }

    [Fact]
    public void Stagger_Unknown_Falls_Back_To_The_Hp_Curve()
    {
        // The vendor itself falls back to UnitHealthPercent when stagger is
        // unreadable; at full HP that is White -> hold.
        var result = Evaluate(Slot.Defensive, 119582,
            Context(hp: 100, urgency: DefensiveUrgency.White, staggerUrgency: DefensiveUrgency.Unknown));
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("white", result.Reason);
    }

    [Fact]
    public void Ignore_Pain_Curated_Orange_Is_Not_Discounted_By_Recommendation()
    {
        // The curated minimum is the deliberate opportunity-cost decision
        // (rage spend), so a live recommendation must not lower it.
        var yellow = Evaluate(Slot.Defensive, 190456, Context(hp: 70, urgency: DefensiveUrgency.Yellow));
        Assert.Equal(PolicyVerdict.Hold, yellow.Verdict);

        var orange = Evaluate(Slot.Defensive, 190456, Context(hp: 45, urgency: DefensiveUrgency.Orange));
        Assert.Equal(PolicyVerdict.Use, orange.Verdict);
    }

    [Fact]
    public void Ignore_Pain_Emergency_Still_Overrides()
    {
        var result = Evaluate(Slot.Defensive, 190456, Context(hp: 30, urgency: DefensiveUrgency.Red));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.True(result.Emergency);
    }

    // ---- user ability policy ----------------------------------------------

    [Fact]
    public void User_Off_Blocks_Red_Emergency_Solo_And_Recommendation()
    {
        // OFF is an absolute automatic-use prohibition.
        var options = new PolicyOptions
        {
            SoloEnabled = true,
            Abilities = AbilityPolicy.Default.With(871, enabled: false, defaultEnabled: true),
        };
        var result = Evaluate(Slot.Defensive, 871,
            Context(hp: 20, urgency: DefensiveUrgency.Red), options);
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
        Assert.Equal("user policy disabled", result.Reason);
    }

    [Fact]
    public void Vanish_Default_Off_Never_Fires()
    {
        // Rogue Vanish: manual by design. Even at Red urgency in Solo it skips.
        var result = Evaluate(Slot.Defensive, 1856,
            Context(hp: 20, urgency: DefensiveUrgency.Red), Solo);
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
        Assert.Contains("manual by design", result.Reason);
    }

    [Fact]
    public void Vanish_Explicit_Off_Matches_The_Curated_Default()
    {
        // Turning a default-OFF ability OFF is a no-op (it stays never-auto);
        // the override never downgrades it to an implicit ON.
        var policy = AbilityPolicy.Default.With(1856, enabled: false, defaultEnabled: false);
        Assert.Equal(AbilityPolicy.Default.HasOverrides, policy.HasOverrides);
        var options = new PolicyOptions { SoloEnabled = true, Abilities = policy };
        var result = Evaluate(Slot.Defensive, 1856,
            Context(hp: 20, urgency: DefensiveUrgency.Red), options);
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
        Assert.Contains("manual by design", result.Reason);
    }

    [Fact]
    public void Vanish_Explicit_On_Is_Emergency_Only()
    {
        var options = new PolicyOptions
        {
            SoloEnabled = true,
            Abilities = AbilityPolicy.Default.With(1856, enabled: true, defaultEnabled: false),
        };
        // Yellow (routine damage): still held despite the explicit ON.
        var routine = Evaluate(Slot.Defensive, 1856, Context(hp: 80, urgency: DefensiveUrgency.Yellow), options);
        Assert.Equal(PolicyVerdict.Hold, routine.Verdict);

        // Red / emergency: the user's override makes the escape eligible.
        var panic = Evaluate(Slot.Defensive, 1856, Context(hp: 20, urgency: DefensiveUrgency.Red), options);
        Assert.Equal(PolicyVerdict.Use, panic.Verdict);
    }

    [Fact]
    public void User_On_Does_Not_Mean_Spam_White_Still_Holds()
    {
        var options = new PolicyOptions
        {
            Abilities = AbilityPolicy.Default.With(1966, enabled: true, defaultEnabled: true),
        };
        var result = Evaluate(Slot.Defensive, 1966, Context(hp: 100, urgency: DefensiveUrgency.White), options);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
    }

    [Fact]
    public void User_Off_Blocks_A_Self_Heal_In_Solo()
    {
        var options = new PolicyOptions
        {
            SoloEnabled = true,
            Abilities = AbilityPolicy.Default.With(202168, enabled: false, defaultEnabled: true),
        };
        var result = Evaluate(Slot.SelfHeal, 202168, Context(hp: 40, urgency: DefensiveUrgency.Orange), options);
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
        Assert.Equal("user policy disabled", result.Reason);
    }

    [Fact]
    public void Solo_Emergency_Picks_One_Survival_Action_And_Keeps_The_Heal_Eligible()
    {
        // v2.3 + v2.2 unified survival: at emergency HP with a major defensive
        // AND a self-heal both eligible, the scheduler selects exactly one
        // action (the emergency-ranked defensive), and the heal stays eligible
        // for the next tick instead of both firing at once.
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();
        var frame = Decode(new TestV5Frame()
            .Slot(Slot.Defensive, 0x52)
            .Slot(Slot.SelfHeal, 0x48)
            .SpellId(Slot.Defensive, 871)
            .SpellId(Slot.SelfHeal, 202168)
            .Vitals(30)
            .Urgency(DefensiveUrgency.Red)
            .Ranges((Slot.Defensive, TriState.Yes), (Slot.SelfHeal, TriState.Yes)));
        var options = new PolicyOptions { SoloEnabled = true };

        var plan = Advance(scheduler, frame, tracker, now: 1000, options);
        Assert.Equal(Slot.Defensive, plan.Selected);
        Assert.Equal(ScheduleReason.EmergencySurvival, plan.Reason);
        Assert.Contains(plan.Verdicts, v => v.Slot == Slot.SelfHeal && v.Verdict == PolicyVerdict.Use);
        Assert.Contains(plan.Verdicts, v => v.Slot == Slot.Defensive && v.Verdict == PolicyVerdict.Use);
    }

    // ---- scheduler integration over a real v6 frame ------------------------

    private static readonly bool[] AllEnabled = [true, true, true, true, true, true, true, true];

    private static SchedulePlan Advance(
        ActionScheduler scheduler,
        BridgeFrame frame,
        CandidateTracker tracker,
        long now,
        PolicyOptions? options = null)
    {
        tracker.Update(frame, now);
        return scheduler.Advance(new ScheduleInput
        {
            Frame = frame,
            Candidates = tracker.Snapshot(AllEnabled),
            NowMs = now,
            MinKeyIntervalMs = 120,
            StaleAfterMs = 1500,
            HeartbeatTimeoutMs = 500,
            RepeatSuppressMs = 900,
            Context = CombatContext.FromFrame(frame),
            Options = options ?? PolicyOptions.Standard,
            Catalog = Catalog,
            CollectPolicyVerdicts = true,
        });
    }

    [Fact]
    public void Scheduler_Selects_Defensive_From_Decoded_Red_Urgency()
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();
        var frame = Decode(new TestV5Frame()
            .Slot(Slot.Defensive, 0x52)
            .SpellId(Slot.Defensive, 871)
            .Vitals(25)
            .Urgency(DefensiveUrgency.Red)
            .Ranges((Slot.Defensive, TriState.Yes)));

        var plan = Advance(scheduler, frame, tracker, now: 1000);
        Assert.Equal(Slot.Defensive, plan.Selected);
        Assert.Equal(ScheduleReason.EmergencySurvival, plan.Reason);
        Assert.Contains(plan.Actions, a => a.SpellId == 871);
    }

    [Fact]
    public void Scheduler_Holds_White_Urgency_And_Keeps_The_Main_Rotation()
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();
        var frame = Decode(new TestV5Frame()
            .Slot(Slot.Main, 0x45)
            .Slot(Slot.Defensive, 0x52)
            .SpellId(Slot.Main, 78)
            .SpellId(Slot.Defensive, 871)
            .Vitals(100)
            .Urgency(DefensiveUrgency.White));

        var plan = Advance(scheduler, frame, tracker, now: 1000);
        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Equal(ScheduleReason.MainRotation, plan.Reason);
        Assert.DoesNotContain(plan.Actions, a => a.Slot == Slot.Defensive);
        Assert.Contains("white", plan.PolicyDetail);
    }

    [Fact]
    public void Scheduler_Orange_GapFill_Major_Is_Held_But_Minor_Fires()
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();
        // MaxDps named no bound defensive; the bridge gap-filled a MAJOR at
        // Orange (source bit set): the policy must hold it.
        var frame = Decode(new TestV5Frame()
            .Slot(Slot.Defensive, 0x52)
            .SpellId(Slot.Defensive, 871)
            .Vitals(45)
            .Urgency(DefensiveUrgency.Orange, catalogSource: true));

        var plan = Advance(scheduler, frame, tracker, now: 1000);
        Assert.Null(plan.Selected);
        Assert.Equal(ScheduleReason.PolicyHold, plan.Reason);
        Assert.Contains("Orange", plan.PolicyDetail);
    }

    [Fact]
    public void Scheduler_User_Off_Is_Reported_As_A_Skip()
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();
        var frame = Decode(new TestV5Frame()
            .Slot(Slot.Defensive, 0x52)
            .SpellId(Slot.Defensive, 871)
            .Vitals(25)
            .Urgency(DefensiveUrgency.Red));
        var options = new PolicyOptions
        {
            Abilities = AbilityPolicy.Default.With(871, enabled: false, defaultEnabled: true),
        };

        var plan = Advance(scheduler, frame, tracker, now: 1000, options);
        Assert.Null(plan.Selected);
        Assert.Equal(ScheduleReason.PolicySkip, plan.Reason);
        Assert.Contains("user policy disabled", plan.PolicyDetail);
    }

    [Fact]
    public void Failed_Defensive_Press_Does_Not_Lock_The_Scheduler()
    {
        // Ignore Pain rides the GCD, so a press that never starts a GCD is a
        // failed action: it must be suppressed with an escalating backoff
        // while the main rotation continues to be serviced.
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();
        BridgeFrame Build(int heartbeat) => Decode(new TestV5Frame()
            .Slot(Slot.Main, 0x45)
            .Slot(Slot.Defensive, 0x52)
            .SpellId(Slot.Main, 78)
            .SpellId(Slot.Defensive, 190456)
            .Vitals(25)
            .Urgency(DefensiveUrgency.Red), heartbeat);
        var frame = Build(1);

        var first = Advance(scheduler, frame, tracker, now: 1000);
        Assert.Equal(Slot.Defensive, first.Selected);
        scheduler.NoteSent(1000, Slot.Defensive, new KeyStroke(0x52, false, false, false), 190456);

        // 700 ms later (fresh heartbeat, so the link stays up): no GCD ever
        // appeared and the candidate is unchanged — the press is detected as
        // failed, the stroke is suppressed and the main takes the tick.
        var second = Advance(scheduler, Build(2), tracker, now: 1700);
        Assert.True(scheduler.RejectionsDetected >= 1);
        Assert.Equal(Slot.Main, second.Selected);
        Assert.Equal(ScheduleReason.MainRotation, second.Reason);
    }
}
