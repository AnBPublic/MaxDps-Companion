using MaxDpsCompanion.Core.Knowledge;
using MaxDpsCompanion.Core.Scheduler;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Feature 4 (policy half): interrupt insurance. Drives the REAL
/// <see cref="InterruptInsurance"/> against the embedded knowledge base, the
/// real curated CC registry and the real <see cref="CrowdControlVetoes"/> veto,
/// so every fact is an end-to-end predicate check (no stubs inventing
/// eligibility).
///
/// Ext5 age quantisation: the wire carries 250 ms buckets, so a cast age is only
/// ever 0, 250, 500, ... 3250 ms. 999 ms is therefore not representable; the
/// exact threshold boundary is proven at the 1000 ms reading plus "threshold one
/// millisecond above the reading", which is the same <c>&gt;=</c> comparison a
/// 999 ms cast against a 1000 ms floor makes.
/// </summary>
[Collection("GlobalStaticState")]
public class InterruptInsuranceTests
{
    private const long Now = 100_000;

    /// <summary>WARRIOR/Arms Storm Bolt: curated AutoEligible stun, 4 s base.</summary>
    private const int WarriorStormBolt = 107570;

    /// <summary>PRIEST/Shadow Silence (15487): the Silence-kind fallback row.</summary>
    private const int PriestSilence = 15487;

    /// <summary>A real class kick (Pummel) - never a curated CC row.</summary>
    private const int WarriorPummel = 6552;

    private static readonly KeyStroke KeyF = new(0x46, false, false, false);

    /// <summary>
    /// The caller-owned kick memory. KickMemory is per-scheduler instance state
    /// (4.6.0 review M2), so a policy test owns a local one instead of touching
    /// a process-wide static.
    /// </summary>
    private readonly KickMemory _kick = new();

    public InterruptInsuranceTests()
    {
        // Insurance is automatic crowd control, so the companion CC opt-in is
        // one of its gates (review m1). Every Fire test needs it ON; the gate's
        // own test turns it off explicitly.
        CrowdControlGate.Configure(true);
        CrowdControlVetoes.Memory = new CrowdControlDiminishing();
        CrowdControlVetoes.ClearStunIneffective();
        _kick.Reset();
    }

    private static AbilityCatalog Catalog => AbilityCatalog.Default;

    private static PolicyOptions Options(bool enabled, int minAgeMs = 1000) => new()
    {
        InsuranceEnabled = enabled,
        InsuranceMinCastAgeMs = minAgeMs,
    };

    /// <summary>Ext5 defaults to the ideal case: casting, interruptible, rank 3.</summary>
    private static Ext5Block Block(
        int castAgeBucket = 5, // bucket 5 => CastAgeMinMs 1000
        int spellId = WarriorStormBolt,
        KeyStroke? key = null) => new(
        Rank: 3,
        Delve: 2,
        Interruptible: 2,
        CastKind: 2,
        CastAgeBucket: castAgeBucket,
        CcKey: key ?? KeyF,
        CcSpellId: spellId,
        GcdBucket: 1);

    private static CombatContext Context(
        Ext5Block? ext5,
        string? className = "WARRIOR",
        string? spec = "Arms",
        TriState casting = TriState.Yes,
        TriState interruptible = TriState.Unknown,
        bool contextValid = true) => new()
    {
        Class = className,
        Spec = spec,
        Cast = PlayerCastState.None,
        TargetCasting = casting,
        TargetCastInterruptible = interruptible,
        TargetInMelee = TriState.Unknown,
        SlotRange = new TriState[PixelProtocol.SlotCount],
        SlotBuffActive = new TriState[PixelProtocol.SlotCount],
        Ext5 = ext5,
        ContextValid = contextValid,
    };

    private static InsuranceDecision Evaluate(
        CombatContext ctx,
        bool insuranceEnabled = true,
        int minAgeMs = 1000,
        bool inCombat = true,
        bool hasTarget = true,
        bool candidatePresent = false,
        bool suppressed = false,
        PolicyMemory? memory = null) =>
        InterruptInsurance.Evaluate(
            ctx, Options(insuranceEnabled, minAgeMs), Catalog,
            memory ?? new PolicyMemory(), inCombat, hasTarget, Now,
            candidatePresent, suppressed);

    // ---- 1. OFF is inert ------------------------------------------------

    [Fact]
    public void Off_Returns_None_And_Records_Nothing()
    {
        var memory = new CrowdControlDiminishing();
        CrowdControlVetoes.Memory = memory;
        CrowdControlVetoes.NoteStunIneffective(); // a veto WOULD fire if the predicate ran

        var decision = Evaluate(
            Context(Block()), insuranceEnabled: false, candidatePresent: true);

        Assert.Equal(InsuranceDecision.None, decision);
        Assert.False(decision.Fire);
        Assert.Equal(0, decision.SpellId);
        Assert.Equal(0, decision.BaseDurationMs);
        Assert.Equal(CcDrCategory.Unknown, decision.Dr);
        Assert.Equal("", decision.Reason); // no branch ran => no telemetry reason
        // Zero side effects: no DR application, no kick-memory touch.
        Assert.Equal(0, memory.Evaluate(CcDrCategory.Stun, Now, 4000).Applications);
        Assert.Equal(long.MinValue, _kick.FailedUntilMs);
    }

    [Fact]
    public void Off_Short_Circuits_Before_Every_Other_Branch()
    {
        // Garbage frame, out of combat, no target, no Ext5: still exactly None,
        // which proves the toggle is the single first return.
        var decision = Evaluate(
            Context(ext5: null, casting: TriState.Unknown, contextValid: false),
            insuranceEnabled: false, inCombat: false, hasTarget: false);

        Assert.Equal(InsuranceDecision.None, decision);
        Assert.Equal("", decision.Reason);
    }

    // ---- 2. the companion CC opt-in is a hard gate (review m1) -----------

    [Fact]
    public void Companion_Cc_Off_Does_Not_Fire_And_Names_The_Gate()
    {
        CrowdControlGate.Configure(false);
        try
        {
            var decision = Evaluate(Context(Block()), candidatePresent: false);

            Assert.False(decision.Fire);
            Assert.Equal("insurance: companion CC is off", decision.Reason);
        }
        finally
        {
            CrowdControlGate.Configure(true);
        }
    }

    [Fact]
    public void Companion_Cc_Off_Is_Checked_After_The_Insurance_Toggle()
    {
        // The insurance toggle keeps the FIRST return: with both off the answer
        // is the inert None (no telemetry reason), never the CC-gate deny - so
        // InsuranceEnabled=0 remains byte-identical to 4.5.8.
        CrowdControlGate.Configure(false);
        try
        {
            var decision = Evaluate(
                Context(Block()), insuranceEnabled: false, candidatePresent: false);

            Assert.Equal(InsuranceDecision.None, decision);
            Assert.Equal("", decision.Reason);
        }
        finally
        {
            CrowdControlGate.Configure(true);
        }
    }

    [Fact]
    public void Companion_Cc_Off_Refuses_Before_Any_Other_Branch()
    {
        // Garbage frame + CC off: the gate's reason wins, proving it is an
        // early deny rather than a late one hidden behind other refusals.
        CrowdControlGate.Configure(false);
        try
        {
            var decision = Evaluate(
                Context(ext5: null, contextValid: false), inCombat: false, hasTarget: false);

            Assert.False(decision.Fire);
            Assert.Equal("insurance: companion CC is off", decision.Reason);
        }
        finally
        {
            CrowdControlGate.Configure(true);
        }
    }

    // ---- 7. timing ladder -----------------------------------------------

    [Fact]
    public void Kick_Present_Young_Cast_Does_Not_Fire()
    {
        var decision = Evaluate(Context(Block(castAgeBucket: 3)), candidatePresent: true); // 500 ms

        Assert.False(decision.Fire);
        Assert.Contains("cast is 500 ms old", decision.Reason);
        Assert.Contains("1000 ms", decision.Reason);
    }

    [Fact]
    public void Kick_Present_At_Threshold_Fires()
    {
        var decision = Evaluate(Context(Block(castAgeBucket: 5)), candidatePresent: true); // 1000 ms

        Assert.True(decision.Fire);
        Assert.Equal(WarriorStormBolt, decision.SpellId);
        Assert.Equal(KeyF, decision.Stroke);
        Assert.Equal(CcDrCategory.Stun, decision.Dr);
        Assert.Equal(4_000, decision.BaseDurationMs);
        Assert.Contains("cast age 1000 ms", decision.Reason);
    }

    [Fact]
    public void Kick_Present_Above_Threshold_Fires()
    {
        var decision = Evaluate(
            Context(Block(castAgeBucket: 9)), candidatePresent: true); // 2000 ms

        Assert.True(decision.Fire);
        Assert.Contains("cast age 2000 ms", decision.Reason);
    }

    [Fact]
    public void Kick_Absent_Fires_Immediately_Even_On_A_Young_Cast()
    {
        var decision = Evaluate(Context(Block(castAgeBucket: 1)), candidatePresent: false); // 0 ms

        Assert.True(decision.Fire);
        Assert.Contains("no interrupt candidate", decision.Reason);
    }

    [Fact]
    public void Kick_Suppressed_Fires_Even_On_A_Young_Cast()
    {
        var decision = Evaluate(
            Context(Block(castAgeBucket: 1)), candidatePresent: true, suppressed: true);

        Assert.True(decision.Fire);
        Assert.Contains("suppressed", decision.Reason);
    }

    [Fact]
    public void Unknown_Cast_Age_Blocks_Only_The_Age_Path()
    {
        var unknownAge = Block(castAgeBucket: 0);
        Assert.Equal(-1, unknownAge.CastAgeMinMs);

        var blocked = Evaluate(Context(unknownAge), candidatePresent: true);
        Assert.False(blocked.Fire);
        Assert.Contains("cast age is unknown", blocked.Reason);

        var kickAbsent = Evaluate(Context(unknownAge), candidatePresent: false);
        Assert.True(kickAbsent.Fire);

        var suppressed = Evaluate(Context(unknownAge), candidatePresent: true, suppressed: true);
        Assert.True(suppressed.Fire);
    }

    [Fact]
    public void Age_Exactly_At_The_Threshold_Fires()
    {
        // 1000 ms reading vs a 1000 ms floor: inclusive boundary.
        var decision = Evaluate(
            Context(Block(castAgeBucket: 5)), minAgeMs: 1000, candidatePresent: true);

        Assert.True(decision.Fire);
    }

    [Fact]
    public void Age_One_Millisecond_Below_The_Threshold_Does_Not_Fire()
    {
        // 1000 ms reading vs a 1001 ms floor == a 999 ms cast vs a 1000 ms
        // floor: the "< threshold" side of the same comparison.
        var decision = Evaluate(
            Context(Block(castAgeBucket: 5)), minAgeMs: 1001, candidatePresent: true);

        Assert.False(decision.Fire);
        Assert.Contains("1000 ms", decision.Reason);
        Assert.Contains("1001 ms", decision.Reason);
    }

    [Fact]
    public void Age_Threshold_At_The_Bucket_Floor_Fires()
    {
        // 750 ms reading vs a 750 ms floor fires; 751 ms does not.
        Assert.True(Evaluate(
            Context(Block(castAgeBucket: 4)), minAgeMs: 750, candidatePresent: true).Fire);
        Assert.False(Evaluate(
            Context(Block(castAgeBucket: 4)), minAgeMs: 751, candidatePresent: true).Fire);
    }

    // ---- 2-6. structural gates ------------------------------------------

    [Fact]
    public void Out_Of_Combat_Does_Not_Fire()
    {
        var decision = Evaluate(Context(Block()), inCombat: false);

        Assert.False(decision.Fire);
        Assert.Contains("not in combat", decision.Reason);
    }

    [Fact]
    public void No_Target_Does_Not_Fire()
    {
        var decision = Evaluate(Context(Block()), hasTarget: false);

        Assert.False(decision.Fire);
        Assert.Contains("no target", decision.Reason);
    }

    [Fact]
    public void Invalid_Context_Does_Not_Fire()
    {
        var decision = Evaluate(Context(Block(), contextValid: false));

        Assert.False(decision.Fire);
        Assert.Contains("context invalid", decision.Reason);
    }

    [Theory]
    [InlineData(0)] // TriState.Unknown
    [InlineData(2)] // TriState.No
    public void No_Confirmed_Target_Cast_Does_Not_Fire(int castingState)
    {
        var decision = Evaluate(Context(Block(), casting: (TriState)castingState));

        Assert.False(decision.Fire);
        Assert.Contains("no confirmed target cast", decision.Reason);
    }

    [Fact]
    public void Non_Interruptible_Cast_Does_Not_Fire()
    {
        var decision = Evaluate(Context(Block(), interruptible: TriState.No));

        Assert.False(decision.Fire);
        Assert.Contains("not interruptible", decision.Reason);
    }

    [Fact]
    public void Unknown_Interruptible_Fails_Open()
    {
        var decision = Evaluate(
            Context(Block(), interruptible: TriState.Unknown), candidatePresent: false);

        Assert.True(decision.Fire);
    }

    [Fact]
    public void Null_Ext5_Does_Not_Fire()
    {
        var decision = Evaluate(Context(ext5: null), candidatePresent: false);

        Assert.False(decision.Fire);
        Assert.Contains("no Ext5 block", decision.Reason);
    }

    [Fact]
    public void Invalid_Key_Does_Not_Fire()
    {
        var decision = Evaluate(
            Context(Block(key: new KeyStroke(0, false, false, false))), // VirtualKey 0 = invalid
            candidatePresent: false);

        Assert.False(decision.Fire);
        Assert.Contains("not usable", decision.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_Spell_Id_Does_Not_Fire(int spellId)
    {
        var decision = Evaluate(Context(Block(spellId: spellId)), candidatePresent: false);

        Assert.False(decision.Fire);
        Assert.Contains("not usable", decision.Reason);
    }

    // ---- Ext5 <-> core cross-checks (review m4) --------------------------

    [Fact]
    public void Ext5_Cast_Kind_None_Does_Not_Fire()
    {
        // The core triple says "casting", the Ext5 duplicate says "no cast"
        // (cast kind 1): the two wire sources disagree, so the press fails
        // closed instead of trusting whichever one is stale.
        var decision = Evaluate(
            Context(Block() with { CastKind = 1 }), candidatePresent: false);

        Assert.False(decision.Fire);
        Assert.Contains("cast kind none", decision.Reason);
    }

    [Fact]
    public void Ext5_Not_Interruptible_Does_Not_Fire_Even_When_The_Core_Is_Unknown()
    {
        var decision = Evaluate(
            Context(Block() with { Interruptible = 1 }, interruptible: TriState.Unknown),
            candidatePresent: false);

        Assert.False(decision.Fire);
        Assert.Contains("Ext5 reports the target cast is not interruptible", decision.Reason);
    }

    [Fact]
    public void Ext5_Interruptible_Yes_And_Cast_Kind_Cast_Stay_Eligible()
    {
        // The consistent frame (the block the bridge writes for a live cast)
        // still fires: the cross-check only refuses contradictions.
        var decision = Evaluate(Context(Block()), candidatePresent: false);

        Assert.True(decision.Fire);
    }

    [Theory]
    [InlineData(0)]   // unknown
    [InlineData(1)]   // no
    [InlineData(2)]   // yes
    public void Delve_Is_Telemetry_Only(int delve)
    {
        // Ext5.Delve is documented as telemetry-only (docs/PROTOCOL.md): no
        // decision rule reads it, so the same cast fires for every value.
        var decision = Evaluate(
            Context(Block() with { Delve = delve }), candidatePresent: false);

        Assert.True(decision.Fire);
    }

    [Fact]
    public void Crowd_Control_Press_Predicate_Separates_The_Kick_From_Cc()
    {
        // Review M1: the scheduler keys the kick-failure memory and the DR
        // ladder on this predicate, so a CC id can never be mistaken for a kick.
        Assert.True(InterruptInsurance.IsCrowdControlPress(WarriorStormBolt));
        Assert.True(InterruptInsurance.IsCrowdControlPress(PriestSilence));
        Assert.False(InterruptInsurance.IsCrowdControlPress(WarriorPummel));
        Assert.False(InterruptInsurance.IsCrowdControlPress(0));
        Assert.False(InterruptInsurance.IsCrowdControlPress(-1));
    }

    // ---- 8. curated eligibility + vetoes ---------------------------------

    [Fact]
    public void Class_Without_A_Curated_Row_For_That_Spell_Does_Not_Fire()
    {
        // Kidney Shot 408 is rogue-only; a warrior frame must never press it.
        var decision = Evaluate(
            Context(Block(spellId: 408)), candidatePresent: false);

        Assert.False(decision.Fire);
        Assert.Contains("not a curated crowd-control row", decision.Reason);
    }

    [Fact]
    public void Non_Interrupt_Fallback_Row_Does_Not_Fire()
    {
        // Shockwave 46968 is curated for warrior but manual-only (AoE stun): the
        // insurance path may not promote it.
        var decision = Evaluate(
            Context(Block(spellId: 46968)), candidatePresent: false);

        Assert.False(decision.Fire);
        Assert.Contains("not an interrupt-fallback row", decision.Reason);
    }

    [Fact]
    public void Dr_Immune_Target_Does_Not_Fire_And_Echoes_The_Veto()
    {
        var memory = new CrowdControlDiminishing();
        CrowdControlVetoes.Memory = memory;
        memory.NoteUse(CcDrCategory.Stun, Now, 4_000);
        memory.NoteUse(CcDrCategory.Stun, Now, 4_000);

        var decision = Evaluate(Context(Block()), candidatePresent: false);

        Assert.False(decision.Fire);
        Assert.Contains("immune", decision.Reason);
        Assert.Contains("DR 2/", decision.Reason);
    }

    [Fact]
    public void StunIneffective_Latch_Does_Not_Fire()
    {
        CrowdControlVetoes.NoteStunIneffective();

        var decision = Evaluate(Context(Block()), candidatePresent: false);

        Assert.False(decision.Fire);
        Assert.Contains("ineffective", decision.Reason);
    }

    [Fact]
    public void Silence_Row_Fires_With_Its_Own_Dr_Category()
    {
        var decision = Evaluate(
            Context(Block(spellId: PriestSilence), className: "PRIEST", spec: "Shadow"),
            candidatePresent: false);

        Assert.True(decision.Fire);
        Assert.Equal(PriestSilence, decision.SpellId);
        Assert.Equal(CcDrCategory.Silence, decision.Dr);
        Assert.Equal(4_000, decision.BaseDurationMs);
    }

    [Fact]
    public void Fire_Returns_The_Ext5_Stroke_Including_Modifiers()
    {
        var shiftR = new KeyStroke(0x52, Shift: true, Ctrl: false, Alt: false);

        var decision = Evaluate(Context(Block(key: shiftR)), candidatePresent: false);

        Assert.True(decision.Fire);
        Assert.Equal(shiftR, decision.Stroke);
        Assert.Equal(WarriorStormBolt, decision.SpellId);
    }

    // ---- side-effect contract -------------------------------------------

    [Fact]
    public void Evaluate_Never_Records_NoteUsed_Or_Touches_KickMemory()
    {
        var memory = new CrowdControlDiminishing();
        CrowdControlVetoes.Memory = memory;

        var decision = Evaluate(Context(Block()), candidatePresent: false);

        Assert.True(decision.Fire);
        // The scheduler records after a real send; a decision alone must not
        // advance the DR ladder.
        Assert.Equal(0, memory.Evaluate(CcDrCategory.Stun, Now, 4_000).Applications);
        Assert.Equal(long.MinValue, _kick.FailedUntilMs);
    }

    [Fact]
    public void Evaluate_Does_Not_Consult_KickMemory()
    {
        // A suppression window 1.5 s long is owned by the scheduler: passing
        // interruptSuppressed=false must keep the young-cast hold even though
        // KickMemory would still be inside its window for this timestamp.
        _kick.NoteFailure(Now);

        var decision = Evaluate(
            Context(Block(castAgeBucket: 2)), candidatePresent: true, suppressed: false);

        Assert.False(decision.Fire);
        Assert.True(_kick.Failed(Now)); // untouched by the predicate
    }

    // ---- telemetry: every refusing branch explains itself ----------------

    [Fact]
    public void Every_Refusing_Branch_Returns_A_Non_Empty_Reason()
    {
        var memory = new CrowdControlDiminishing();
        CrowdControlVetoes.Memory = memory;
        memory.NoteUse(CcDrCategory.Stun, Now, 4_000);
        memory.NoteUse(CcDrCategory.Stun, Now, 4_000);

        InsuranceDecision[] refusals =
        [
            Evaluate(Context(Block()), inCombat: false),
            Evaluate(Context(Block()), hasTarget: false),
            Evaluate(Context(Block(), contextValid: false)),
            Evaluate(Context(Block(), casting: TriState.Unknown)),
            Evaluate(Context(Block(), interruptible: TriState.No)),
            Evaluate(Context(ext5: null)),
            Evaluate(Context(Block(key: new KeyStroke(0, false, false, false)))),
            Evaluate(Context(Block(spellId: 0))),
            Evaluate(Context(Block() with { CastKind = 1 })),                         // Ext5: no cast
            Evaluate(Context(Block() with { Interruptible = 1 })),                   // Ext5: not interruptible
            Evaluate(Context(Block(castAgeBucket: 1)), candidatePresent: true),      // young
            Evaluate(Context(Block(castAgeBucket: 0)), candidatePresent: true),      // unknown age
            Evaluate(Context(Block(spellId: 408))),                                  // not curated
            Evaluate(Context(Block(spellId: 46968))),                                // not fallback
            Evaluate(Context(Block())),                                              // DR immune
        ];

        Assert.All(refusals, decision =>
        {
            Assert.False(decision.Fire);
            Assert.False(string.IsNullOrWhiteSpace(decision.Reason));
        });
        // The predicate's own refusals are namespaced...
        Assert.All(refusals[..^1], decision => Assert.StartsWith("insurance:", decision.Reason));
        // ...and a veto is echoed verbatim, so its own wording survives.
        Assert.StartsWith("target immune", refusals[^1].Reason);
    }

    // =====================================================================
    // Scheduler integration (4.6.0 review).
    //
    // The insurance path records its DR application on the process-wide
    // CrowdControlVetoes.Memory and reads the shared StunIneffective latch, so
    // these tests live in the GlobalStaticState collection with the rest of the
    // CC suite. They used to sit in ActionSchedulerPolicyTests behind a blanket
    // [Collection] fence added to 23 scheduler classes: the fence serialised the
    // symptoms of a STATIC KickMemory. That memory is now per-scheduler instance
    // state (review M2) and the fence is gone; only the classes that really
    // touch the CC statics keep it.
    // =====================================================================

    private static readonly KeyStroke KeyE = new(0x45, false, false, false);
    private static readonly KeyStroke KeyR = new(0x52, false, false, false);
    private static readonly KeyStroke KeyG = new(0x47, false, false, false);

    private static KeyStroke SlotStroke(Slot slot) => new((byte)(0x41 + (int)slot), false, false, false);

    private static BridgeFrame Frame(
        (Slot Slot, KeyStroke? Stroke)[] slots,
        int heartbeat = 1,
        bool inCombat = true,
        bool onGcd = false,
        bool hasTarget = true,
        Ext5Block? ext5 = null)
    {
        var array = new KeyStroke?[PixelProtocol.SlotCount];
        foreach (var (slot, stroke) in slots) array[(int)slot] = stroke;
        var flags = 0;
        if (inCombat) flags |= PixelProtocol.StatusFlagInCombat;
        if (onGcd) flags |= PixelProtocol.StatusFlagOnGcd;
        if (hasTarget) flags |= PixelProtocol.StatusFlagHasTarget;
        return new BridgeFrame
        {
            State = BridgeState.Active,
            Heartbeat = heartbeat,
            Version = PixelProtocol.SupportedVersion,
            StatusFlags = flags,
            Slots = array,
            Ext5 = ext5,
        };
    }

    private static ActionCandidate Cand(Slot slot, KeyStroke? stroke = null, int spellId = 0) =>
        new(slot, stroke ?? SlotStroke(slot), Enabled: true, Actionable: true,
            FirstSeenMs: 0, LastChangedMs: 0, LastPressedMs: 0, EverPressed: false,
            SpellId: spellId);

    private static ScheduleInput Input(
        long now, BridgeFrame frame, CombatContext context, PolicyOptions? options,
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
        // Fully qualified: a bare `Catalog` here is easy to mis-read as the
        // catalog and is the kind of silent null that hides a GCD-path
        // regression (an unknown identity falls back to the legacy bypass).
        Catalog = options is null ? null : AbilityCatalog.Default,
    };

    /// <summary>Insurance only: landing and the opener stay OFF.</summary>
    private static CadenceOptions InsuranceCadence => new(
        InsuranceEnabled: true, InsuranceMinCastAgeMs: 250,
        LandingAuto: false, OpenerRelease: false, OpenerWindowActive: false);

    private static PolicyOptions InsuranceSettings => new()
    {
        InsuranceEnabled = true,
        InsuranceMinCastAgeMs = 250,
    };

    /// <summary>Ext5 with a live interruptible cast and a usable CC entry.</summary>
    private static Ext5Block CastBlock(int castAgeBucket = 5, int spellId = WarriorStormBolt) => new(
        Rank: 3, Delve: 2, Interruptible: 2, CastKind: 2,
        CastAgeBucket: castAgeBucket, CcKey: KeyG, CcSpellId: spellId, GcdBucket: 1);

    private static ActionScheduler InsuranceScheduler()
    {
        var scheduler = new ActionScheduler();
        scheduler.Configure(InsuranceCadence);
        return scheduler;
    }

    /// <summary>A clean DR ladder + latch (the constructor already reset both).</summary>
    private static CrowdControlDiminishing FreshVetoes()
    {
        var memory = new CrowdControlDiminishing();
        CrowdControlVetoes.Memory = memory;
        CrowdControlVetoes.ClearStunIneffective();
        return memory;
    }

    [Fact]
    public void Insurance_Fires_The_Ext5_Cc_On_A_Live_Cast_Without_A_Kick()
    {
        var memory = FreshVetoes();
        var scheduler = InsuranceScheduler();

        // No slot-6 candidate at all: the kick cannot cover this cast.
        var plan = scheduler.Advance(Input(1000,
            Frame([(Slot.Main, KeyE)], ext5: CastBlock()), Context(CastBlock()), InsuranceSettings,
            Cand(Slot.Main, KeyE)));

        Assert.Equal(Slot.Interrupt, plan.Selected);
        Assert.Equal(ScheduleReason.InterruptUrgency, plan.Reason);
        var action = plan.Actions[0];
        Assert.Equal(KeyG, action.Stroke);
        Assert.Equal(WarriorStormBolt, action.SpellId);
        // Review M1: Storm Bolt is an ON-GCD ability (class-spells -> Main
        // category -> GcdKind.OnGcd), so the interrupt slot no longer exempts it
        // from the GCD gate. The action is emitted here only because no GCD is
        // live; its BypassGcd flag must tell the truth.
        Assert.False(action.BypassGcd);
        var timing = scheduler.LastTiming;
        Assert.NotNull(timing);
        Assert.Equal(SchedulerTiming.KindInsurance, timing!.Value.Kind);
        Assert.Equal(WarriorStormBolt, timing.Value.CandidateSpell);

        // The DR ladder is written only AFTER the stroke really went out.
        Assert.Equal(0, memory.Evaluate(CcDrCategory.Stun, 1000, 4000).Applications);
        scheduler.NoteSent(1000, Slot.Interrupt, KeyG, WarriorStormBolt);
        Assert.Equal(1, memory.Evaluate(CcDrCategory.Stun, 1000, 4000).Applications);

        // A second send of the same insurance press cannot double-record.
        scheduler.NoteSent(1010, Slot.Interrupt, KeyG, WarriorStormBolt);
        Assert.Equal(1, memory.Evaluate(CcDrCategory.Stun, 1010, 4000).Applications);
    }

    [Fact]
    public void Insurance_Off_Equals_Todays_Plan()
    {
        var memory = FreshVetoes();
        var frame = Frame([(Slot.Main, KeyE)], ext5: CastBlock());
        var context = Context(CastBlock());

        // (a) stock config: Options without the toggle.
        var plain = new ActionScheduler().Advance(Input(
            1000, frame, context, PolicyOptions.Standard, Cand(Slot.Main, KeyE)));
        Assert.Equal(Slot.Main, plain.Selected);
        Assert.DoesNotContain(plain.Actions, a => a.Slot == Slot.Interrupt);

        // (b) the OPTION is on but the scheduler switch is off: still today.
        var switchedOff = new ActionScheduler();
        switchedOff.Configure(CadenceOptions.Off);
        var off = switchedOff.Advance(Input(
            1000, frame, context, InsuranceSettings, Cand(Slot.Main, KeyE)));
        Assert.Equal(Slot.Main, off.Selected);
        Assert.DoesNotContain(off.Actions, a => a.Slot == Slot.Interrupt);
        Assert.Null(switchedOff.LastTiming);

        // (c) the SWITCH is on but the option is off: still today.
        var optionOff = InsuranceScheduler().Advance(Input(
            1000, frame, context, PolicyOptions.Standard, Cand(Slot.Main, KeyE)));
        Assert.Equal(Slot.Main, optionOff.Selected);
        Assert.DoesNotContain(optionOff.Actions, a => a.Slot == Slot.Interrupt);

        // No DR application was recorded by any OFF path.
        Assert.Equal(0, memory.Evaluate(CcDrCategory.Stun, 1000, 4000).Applications);
    }

    [Fact]
    public void Insurance_Waits_For_A_Cast_Age_When_The_Kick_Is_Live()
    {
        FreshVetoes();
        var scheduler = InsuranceScheduler();

        // A live slot-6 kick candidate (KeyR) so the insurance must wait for a
        // cast old enough to be worth covering (min 250 ms; bucket 1 = 0 ms).
        var young = scheduler.Advance(Input(1000,
            Frame([(Slot.Main, KeyE), (Slot.Interrupt, KeyR)], ext5: CastBlock(castAgeBucket: 1)),
            Context(CastBlock(castAgeBucket: 1)), InsuranceSettings,
            Cand(Slot.Main, KeyE), Cand(Slot.Interrupt, KeyR, WarriorPummel)));
        Assert.DoesNotContain(young.Actions, a => a.SpellId == WarriorStormBolt);
        Assert.Null(scheduler.LastTiming);

        // 250 ms old (bucket 2) reaches the threshold: the insurance fires.
        var old = scheduler.Advance(Input(1300,
            Frame([(Slot.Main, KeyE), (Slot.Interrupt, KeyR)], heartbeat: 2,
                ext5: CastBlock(castAgeBucket: 2)),
            Context(CastBlock(castAgeBucket: 2)), InsuranceSettings,
            Cand(Slot.Main, KeyE), Cand(Slot.Interrupt, KeyR, WarriorPummel)));
        Assert.Contains(old.Actions, a => a.SpellId == WarriorStormBolt);
    }

    [Fact]
    public void Insurance_Latches_One_Press_Per_Cast()
    {
        FreshVetoes();
        var scheduler = InsuranceScheduler();

        var fired = scheduler.Advance(Input(1000,
            Frame([(Slot.Main, KeyE)], ext5: CastBlock(castAgeBucket: 5)),
            Context(CastBlock(castAgeBucket: 5)), InsuranceSettings, Cand(Slot.Main, KeyE)));
        Assert.Equal(Slot.Interrupt, fired.Selected);
        scheduler.NoteSent(1000, Slot.Interrupt, KeyG, WarriorStormBolt);

        // The SAME cast keeps running (age grew): no second press.
        var latched = scheduler.Advance(Input(1100,
            Frame([(Slot.Main, KeyE)], heartbeat: 2, ext5: CastBlock(castAgeBucket: 6)),
            Context(CastBlock(castAgeBucket: 6)), InsuranceSettings, Cand(Slot.Main, KeyE)));
        Assert.DoesNotContain(latched.Actions, a => a.SpellId == WarriorStormBolt);

        // A NEW cast (the age bucket restarted lower) may be covered again.
        var recast = scheduler.Advance(Input(1500,
            Frame([(Slot.Main, KeyE)], heartbeat: 3, ext5: CastBlock(castAgeBucket: 2)),
            Context(CastBlock(castAgeBucket: 2)), InsuranceSettings, Cand(Slot.Main, KeyE)));
        Assert.Contains(recast.Actions, a => a.SpellId == WarriorStormBolt);
    }

    [Fact]
    public void Insurance_Ties_Last_So_A_Live_Kick_Is_Still_The_First_Answer()
    {
        FreshVetoes();
        var scheduler = InsuranceScheduler();

        // Both answers are eligible: the kick (KeyR) and the insured CC (KeyG).
        var context = Context(CastBlock()).WithSlotRange((int)Slot.Interrupt, TriState.Yes);
        var plan = scheduler.Advance(Input(1000,
            Frame([(Slot.Main, KeyE), (Slot.Interrupt, KeyR)], ext5: CastBlock()), context,
            InsuranceSettings, Cand(Slot.Main, KeyE), Cand(Slot.Interrupt, KeyR, WarriorPummel)));

        // The kick keeps the Interrupt rank tie: the CC is only the fallback.
        Assert.Equal(Slot.Interrupt, plan.Selected);
        Assert.Equal(KeyR, plan.Actions[0].Stroke);
        Assert.Equal(WarriorPummel, plan.Actions[0].SpellId);
        Assert.Contains(plan.Actions, a => a.SpellId == WarriorStormBolt);
    }

    // ---- review M1: the GCD exemption is per SPELL, not per slot ---------

    [Fact]
    public void Insurance_Gcd_Riding_Cc_Is_Held_During_A_Gcd_And_Fires_On_The_Falling_Edge()
    {
        var memory = FreshVetoes();
        var scheduler = InsuranceScheduler();

        // (1) Inside a live GCD the insured CC is a GCD-RIDING press: it must
        // wait for the GCD to fall exactly like any other GCD stroke. This used
        // to be sent mid-GCD (the interrupt slot bypassed unconditionally) where
        // the client dropped it while the DR ladder advanced and the per-cast
        // latch suppressed the retry.
        var held = scheduler.Advance(Input(1000,
            Frame([(Slot.Main, KeyE)], onGcd: true, ext5: CastBlock()),
            Context(CastBlock()), InsuranceSettings, Cand(Slot.Main, KeyE)));

        Assert.Equal(ScheduleReason.GcdHold, held.Reason);
        Assert.Empty(held.Actions);
        Assert.Null(scheduler.LastTiming);              // no press is reported

        // (2) Nothing was armed either: a send note for that stroke (a press the
        // plan never made) must not advance the DR ladder.
        scheduler.NoteSent(1010, Slot.Interrupt, KeyG, WarriorStormBolt);
        Assert.Equal(0, memory.Evaluate(CcDrCategory.Stun, 1010, 4000).Applications);

        // (3) The falling edge: the same cast is still live and the GCD is over,
        // so the press goes out - the hold cost one GCD, not the whole cast.
        var fired = scheduler.Advance(Input(1500,
            Frame([(Slot.Main, KeyE)], heartbeat: 2, ext5: CastBlock(castAgeBucket: 6)),
            Context(CastBlock(castAgeBucket: 6)), InsuranceSettings, Cand(Slot.Main, KeyE)));

        Assert.Equal(Slot.Interrupt, fired.Selected);
        Assert.Equal(KeyG, fired.Actions[0].Stroke);
        Assert.Equal(WarriorStormBolt, fired.Actions[0].SpellId);
        Assert.Equal(SchedulerTiming.KindInsurance, scheduler.LastTiming!.Value.Kind);
    }

    [Fact]
    public void Interrupt_Bypass_Is_Per_Spell_Not_Per_Slot()
    {
        FreshVetoes();

        // A catalogued kick (Pummel: Interrupt category = off-GCD by design)
        // keeps the exemption ...
        var kick = new ActionScheduler().Advance(Input(1000,
            Frame([(Slot.Interrupt, KeyR)], onGcd: true), Context(CastBlock()),
            PolicyOptions.Standard, Cand(Slot.Interrupt, KeyR, WarriorPummel)));
        Assert.Equal(Slot.Interrupt, kick.Selected);
        Assert.True(kick.Actions[0].BypassGcd);

        // ... while an ON-GCD crowd control on the same slot does not.
        var cc = new ActionScheduler().Advance(Input(1000,
            Frame([(Slot.Interrupt, KeyG)], onGcd: true), Context(CastBlock()),
            PolicyOptions.Standard, Cand(Slot.Interrupt, KeyG, WarriorStormBolt)));
        Assert.Equal(ScheduleReason.GcdHold, cc.Reason);
        Assert.Empty(cc.Actions);
    }

    // ---- review m2: plan-time arming -> emission-time commit -------------

    [Fact]
    public void Insurance_Commits_Nothing_When_The_Press_Is_Held()
    {
        var memory = FreshVetoes();
        var scheduler = InsuranceScheduler();

        // The CC press is planned (the predicate fires) but the GCD gate holds
        // it. Nothing may be armed: no timing record, no per-cast latch, and no
        // DR write waiting for a send that never happened.
        var held = scheduler.Advance(Input(1000,
            Frame([(Slot.Main, KeyE)], onGcd: true, ext5: CastBlock()),
            Context(CastBlock()), InsuranceSettings, Cand(Slot.Main, KeyE)));
        Assert.Empty(held.Actions);
        Assert.Null(scheduler.LastTiming);

        scheduler.NoteSent(1000, Slot.Interrupt, KeyG, WarriorStormBolt);
        Assert.Equal(0, memory.Evaluate(CcDrCategory.Stun, 1000, 4000).Applications);

        // The latch was never armed either, so the SAME cast (same age bucket,
        // inside the 600 ms grace) still fires once the gate opens. (The probe
        // send above needs 120 ms of key-interval to clear first.)
        var fired = scheduler.Advance(Input(1200,
            Frame([(Slot.Main, KeyE)], heartbeat: 2, ext5: CastBlock()),
            Context(CastBlock()), InsuranceSettings, Cand(Slot.Main, KeyE)));
        Assert.Contains(fired.Actions, a => a.SpellId == WarriorStormBolt);
    }

    // ---- review M2: KickMemory is scheduler-owned ------------------------

    [Fact]
    public void KickMemory_Is_Per_Scheduler_Instance()
    {
        var first = new ActionScheduler();
        var second = new ActionScheduler();

        first.NoteFailure(Slot.Interrupt, KeyF, 1000, WarriorPummel);

        Assert.True(first.Kick.Failed(1000));
        Assert.Equal(1000 + KickMemory.FailedWindowMs, first.Kick.FailedUntilMs);
        // A second scheduler (a replay, another engine, a test) sees nothing.
        Assert.False(second.Kick.Failed(1000));
        Assert.Equal(long.MinValue, second.Kick.FailedUntilMs);

        // ... and resetting the second cannot clear the first one's ladder.
        second.Reset();
        Assert.True(first.Kick.Failed(1000));
    }

    [Fact]
    public void Failing_Cc_Never_Feeds_The_Kick_Memory_Or_The_Kick_Retry_State()
    {
        FreshVetoes();
        var scheduler = InsuranceScheduler();

        // Exhaust the CC press's send budget: five sends inside AttemptWindowMs.
        for (var i = 0; i < ActionScheduler.MaxAttemptsPerWindow; i++)
            scheduler.NoteSent(1000 + i, Slot.Interrupt, KeyG, WarriorStormBolt);

        // The next tick retires the over-budget CC stroke through the single
        // failure funnel - with the CC's spell id, never the kick's.
        var plan = scheduler.Advance(Input(1010,
            Frame([(Slot.Main, KeyE)], ext5: CastBlock()),
            Context(CastBlock()), InsuranceSettings, Cand(Slot.Main, KeyE)));

        Assert.DoesNotContain(plan.Actions, a => a.SpellId == WarriorStormBolt);
        // The funnel ran with the CC's own key (the CC stroke is suppressed) ...
        Assert.True(scheduler.FailedUntilFor(Slot.Interrupt, KeyG, WarriorStormBolt) > 1010);
        // ... and a failing CC says nothing about the kick: the ladder is clear.
        Assert.False(scheduler.Kick.Failed(1010));
        Assert.Equal(long.MinValue, scheduler.Kick.FailedUntilMs);

        // The wire kick itself still records (the funnel is not disabled).
        scheduler.NoteFailure(Slot.Interrupt, KeyF, 2000, WarriorPummel);
        Assert.True(scheduler.Kick.Failed(2000));
    }
}
