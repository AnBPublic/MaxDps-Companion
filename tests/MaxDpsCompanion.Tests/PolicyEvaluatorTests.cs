using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Situational policy: USE / HOLD / SKIP per candidate, with UNKNOWN-safe
/// fallbacks. Scenarios mirror the requested behaviour list: main stays
/// responsive, Spell Reflection needs an observed cast, Shadowstep needs a
/// target outside melee, defensives sequence, solo self-sustain is HP-gated.
/// </summary>
public class PolicyEvaluatorTests
{
    private const long Now = 10_000;

    private static AbilityCatalog Catalog => AbilityCatalog.Default;

    private static CombatContext Context(
        bool hpValid = false,
        int hp = 100,
        PlayerCastState cast = PlayerCastState.None,
        TriState targetCasting = TriState.No,
        TriState targetInMelee = TriState.Unknown,
        TriState[]? range = null,
        TriState[]? buffs = null,
        bool contextValid = true,
        TriState targetInterruptible = TriState.Unknown,
        DefensiveUrgency urgency = DefensiveUrgency.Unknown,
        DefensiveUrgency staggerUrgency = DefensiveUrgency.Unknown,
        bool defensiveCatalogSource = false) => new()
    {
        HpValid = hpValid,
        HpPct = hp,
        Cast = cast,
        TargetCasting = targetCasting,
        TargetCastInterruptible = targetInterruptible,
        TargetInMelee = targetInMelee,
        SlotRange = range ?? new TriState[PixelProtocol.SlotCount],
        SlotBuffActive = buffs ?? new TriState[PixelProtocol.SlotCount],
        ContextValid = contextValid,
        DefensiveUrgency = urgency,
        StaggerUrgency = staggerUrgency,
        DefensiveCatalogSource = defensiveCatalogSource,
    };

    private static PolicyDecision Evaluate(
        Slot slot,
        int spellId,
        CombatContext? context = null,
        PolicyOptions? options = null,
        PolicyMemory? memory = null,
        bool inCombat = true,
        bool hasTarget = true,
        long now = Now) =>
        PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = slot,
            SpellId = spellId,
            Context = context ?? Context(),
            Options = options ?? PolicyOptions.Standard,
            Memory = memory ?? new PolicyMemory(),
            NowMs = now,
            InCombat = inCombat,
            HasTarget = hasTarget,
        }, Catalog);

    private static TriState[] Range(params (Slot Slot, TriState State)[] entries)
    {
        var range = new TriState[PixelProtocol.SlotCount];
        foreach (var (slot, state) in entries) range[(int)slot] = state;
        return range;
    }

    private static TriState[] Buffs(params Slot[] slots)
    {
        // v2.7 tri-state: a fixture that declares buffs declares them known.
        var buffs = new TriState[PixelProtocol.SlotCount];
        for (var i = 0; i < buffs.Length; i++) buffs[i] = TriState.No;
        foreach (var slot in slots) buffs[(int)slot] = TriState.Yes;
        return buffs;
    }

    // ---- main rotation ----------------------------------------------------

    [Fact]
    public void Main_Out_Of_Range_Is_Skipped()
    {
        // v2.6: a CONFIRMED out-of-range is a structural exclusion ->
        // Unavailable (retry when the state changes), not a deliberate Skip.
        var result = Evaluate(Slot.Main, 0, Context(range: Range((Slot.Main, TriState.No))));
        Assert.Equal(PolicyVerdict.Unavailable, result.Verdict);
        Assert.Contains("out of range", result.Reason);
    }

    [Fact]
    public void Main_Unknown_Range_Fails_Open()
    {
        var result = Evaluate(Slot.Main, 0, Context(range: Range((Slot.Main, TriState.Unknown))));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
    }

    [Fact]
    public void Main_During_Cast_Holds()
    {
        var result = Evaluate(Slot.Main, 0, Context(cast: PlayerCastState.Casting));
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("cast", result.Reason);
    }

    [Fact]
    public void Main_During_Channel_Holds()
    {
        // v2.1 execution safety: a channel is never ours to clip, not even
        // with the main rotation. The next tick after the channel ends fires
        // the current suggestion (one poll of latency, zero wasted GCDs).
        var result = Evaluate(Slot.Main, 0, Context(cast: PlayerCastState.Channeling));
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("channel", result.Reason);
    }

    [Fact]
    public void Uncatalogued_Main_Identity_Still_Uses()
    {
        var result = Evaluate(Slot.Main, 999_999);
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
    }

    // ---- interrupts -------------------------------------------------------

    [Fact]
    public void Interrupt_Out_Of_Range_Skips()
    {
        var result = Evaluate(Slot.Interrupt, 6552,
            Context(targetCasting: TriState.Yes, range: Range((Slot.Interrupt, TriState.No))));
        Assert.Equal(PolicyVerdict.Unavailable, result.Verdict);
    }

    [Fact]
    public void Interrupt_In_Range_Uses()
    {
        var result = Evaluate(Slot.Interrupt, 6552,
            Context(targetCasting: TriState.Yes, targetInterruptible: TriState.Yes,
                range: Range((Slot.Interrupt, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
    }

    [Fact]
    public void Interrupt_Not_Interruptible_Cast_Skips()
    {
        // The v5 target sensor observed UNIT_SPELLCAST_NOT_INTERRUPTIBLE:
        // kicking now would burn the interrupt without a lockout.
        var result = Evaluate(Slot.Interrupt, 6552,
            Context(targetCasting: TriState.Yes, targetInterruptible: TriState.No));
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
        Assert.Contains("not interruptible", result.Reason);
    }

    [Fact]
    public void Interrupt_Stale_Cast_Skips()
    {
        // MaxDps still recommends a kick but the v5 sensors see no live cast:
        // the suggestion is stale (the cast already ended).
        var result = Evaluate(Slot.Interrupt, 6552, Context(targetCasting: TriState.No));
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
        Assert.Contains("no live target cast", result.Reason);
    }

    [Fact]
    public void Interrupt_Unknown_Context_Fails_Open()
    {
        // v4/v1 frame: no sensor block, so the MaxDps gate stays authoritative.
        var result = Evaluate(Slot.Interrupt, 6552, Context(targetCasting: TriState.No, contextValid: false));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
    }

    // ---- v2.1 cast / channel execution safety ----------------------------

    [Fact]
    public void OffGcd_Defensive_May_Fire_During_Cast()
    {
        // Shield Wall is curated off-GCD: it cannot disturb the cast.
        var result = Evaluate(Slot.Defensive, 871,
            Context(hpValid: true, hp: 25, cast: PlayerCastState.Casting, urgency: DefensiveUrgency.Red));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
    }

    [Fact]
    public void OnGcd_Defensive_Holds_During_Channel()
    {
        var result = Evaluate(Slot.Defensive, 2565, Context(cast: PlayerCastState.Channeling));
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("channel", result.Reason);
    }

    [Fact]
    public void Unverified_OffGcd_Defense_Holds_During_Channel()
    {
        // Enraged Regeneration is an uncurated defensive: its off-GCD default
        // is a guess, so under a channel it is conservatively held.
        var result = Evaluate(Slot.Defensive, 184364, Context(cast: PlayerCastState.Channeling));
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
    }

    [Fact]
    public void GapCloser_Holds_During_Cast_Even_Though_OffGcd()
    {
        // Shadowstep is off-GCD but MOVES the player, and movement cancels a
        // hard cast. It must wait for the cast to finish.
        var result = Evaluate(Slot.Mobility, 36554,
            Context(cast: PlayerCastState.Casting, targetInMelee: TriState.No,
                range: Range((Slot.Mobility, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("cast", result.Reason);
    }

    [Fact]
    public void Interrupt_May_Fire_During_Channel()
    {
        // Kicks are off-GCD by design and must land mid-cast/channel.
        var result = Evaluate(Slot.Interrupt, 6552,
            Context(cast: PlayerCastState.Channeling, targetCasting: TriState.Yes,
                range: Range((Slot.Interrupt, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
    }

    [Fact]
    public void Consumable_May_Fire_During_Cast()
    {
        // Item activations are off-GCD and cannot disturb the cast.
        var result = Evaluate(Slot.Consumable, 0, Context(cast: PlayerCastState.Casting));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
    }

    // ---- defensives / reflect --------------------------------------------

    [Fact]
    public void Spell_Reflection_Without_Incoming_Cast_Holds()
    {
        var result = Evaluate(Slot.Defensive, 23920, Context(targetCasting: TriState.No));
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("incoming cast", result.Reason);
    }

    [Fact]
    public void Spell_Reflection_With_Unknown_Cast_Holds()
    {
        // v2.6: the incoming-cast state is unknown, so the verdict is Unknown
        // (reason "incoming cast state unknown").
        var result = Evaluate(Slot.Defensive, 23920, Context(targetCasting: TriState.Unknown));
        Assert.Equal(PolicyVerdict.Unknown, result.Verdict);
        Assert.Contains("incoming cast state unknown", result.Reason);
    }

    [Fact]
    public void Spell_Reflection_With_Incoming_Cast_Uses()
    {
        var result = Evaluate(Slot.Defensive, 23920, Context(targetCasting: TriState.Yes));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
    }

    [Fact]
    public void Spell_Reflection_While_Already_Active_Is_Skipped()
    {
        var result = Evaluate(Slot.Defensive, 23920,
            Context(targetCasting: TriState.Yes, buffs: Buffs(Slot.Defensive)));
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
        Assert.Contains("already active", result.Reason);
    }

    [Fact]
    public void Defensive_Major_Without_Urgency_Holds()
    {
        // v2.3: UNKNOWN defensive urgency is never treated as Red. A major
        // (Shield Wall) must hold when the bridge could not stage the vendor
        // glow stage (stale v5 addon, secret HP, failed probe).
        var result = Evaluate(Slot.Defensive, 871);
        Assert.Equal(PolicyVerdict.Unknown, result.Verdict);
        Assert.Contains("urgency unknown", result.Reason);
    }

    [Fact]
    public void Defensive_Minor_Without_Urgency_Keeps_Legacy_Gates()
    {
        // A short-CD mitigation keeps the pre-urgency behaviour when the stage
        // is unknown (documented fallback): Spell Reflection with a live cast
        // still uses.
        var result = Evaluate(Slot.Defensive, 23920, Context(targetCasting: TriState.Yes));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
    }

    [Fact]
    public void Lay_On_Hands_Above_Threshold_Holds()
    {
        var result = Evaluate(Slot.Defensive, 633, Context(hpValid: true, hp: 80));
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("threshold", result.Reason);
    }

    [Fact]
    public void Lay_On_Hands_Low_Hp_Uses_Emergency()
    {
        var result = Evaluate(Slot.Defensive, 633, Context(hpValid: true, hp: 30));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.True(result.Emergency);
    }

    [Fact]
    public void Defensive_Overlap_Holds_While_Stronger_Is_Running()
    {
        var memory = new PolicyMemory();
        memory.NoteUse(Catalog.TryGet(871)!, Now);          // Shield Wall, 8 s window

        // Orange stage + MaxDps recommendation for Last Stand: the urgency
        // gate admits it, so the overlap rule is the one that holds.
        var result = Evaluate(Slot.Defensive, 12975,
            Context(hpValid: true, hp: 45, urgency: DefensiveUrgency.Orange), memory: memory, now: Now + 2000);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("already active", result.Reason);
    }

    [Fact]
    public void Defensive_Escalates_When_Hp_Drops()
    {
        var memory = new PolicyMemory();
        memory.NoteUse(Catalog.TryGet(871)!, Now);

        // Emergency HP overrides the overlap hold: stacking is justified.
        var result = Evaluate(Slot.Defensive, 12975, Context(hpValid: true, hp: 30), memory: memory, now: Now + 2000);
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.True(result.Emergency);
    }

    [Fact]
    public void Defensive_Overlap_Expires_With_Its_Window()
    {
        var memory = new PolicyMemory();
        memory.NoteUse(Catalog.TryGet(871)!, Now);          // 8 s coverage
        var result = Evaluate(Slot.Defensive, 12975,
            Context(hpValid: true, hp: 45, urgency: DefensiveUrgency.Orange), memory: memory, now: Now + 9000);
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
    }

    // ---- offensives -------------------------------------------------------

    [Fact]
    public void Offensive_During_Cast_Holds()
    {
        var result = Evaluate(Slot.Offensive, 1719, Context(cast: PlayerCastState.Casting));
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
    }

    [Fact]
    public void Offensive_During_Channel_Holds()
    {
        var result = Evaluate(Slot.Offensive, 1719, Context(cast: PlayerCastState.Channeling));
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
    }

    [Fact]
    public void Offensive_Out_Of_Range_Skips()
    {
        var result = Evaluate(Slot.Offensive, 1719, Context(range: Range((Slot.Offensive, TriState.No))));
        Assert.Equal(PolicyVerdict.Unavailable, result.Verdict);
    }

    [Fact]
    public void Offensive_Fresh_Uses()
    {
        var result = Evaluate(Slot.Offensive, 1719,
            Context(range: Range((Slot.Offensive, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
    }

    [Fact]
    public void Offensives_Stack_Unless_Curated_Into_A_Substitution_Group()
    {
        // Using Recklessness must NOT hold Avatar: stacking burst cooldowns is
        // the intended play. Only curated substitution groups (e.g. trinkets)
        // block a second use.
        var memory = new PolicyMemory();
        memory.NoteUse(Catalog.TryGet(1719)!, Now);
        var result = Evaluate(Slot.Offensive, 107574, Context(range: Range((Slot.Offensive, TriState.Yes))),
            memory: memory, now: Now + 1000);
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
    }

    // ---- mobility / gap closers ------------------------------------------

    [Fact]
    public void Shadowstep_While_Already_In_Melee_Holds()
    {
        var result = Evaluate(Slot.Mobility, 36554,
            Context(targetInMelee: TriState.Yes, range: Range((Slot.Mobility, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("melee", result.Reason);
    }

    [Fact]
    public void Shadowstep_With_Unknown_Melee_State_Holds()
    {
        // v2.6: an unknown required context is Unknown (retry), not Hold.
        var result = Evaluate(Slot.Mobility, 36554,
            Context(targetInMelee: TriState.Unknown, range: Range((Slot.Mobility, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Unknown, result.Verdict);
    }

    [Fact]
    public void Shadowstep_Out_Of_Melee_And_In_Range_Uses()
    {
        var result = Evaluate(Slot.Mobility, 36554,
            Context(targetInMelee: TriState.No, range: Range((Slot.Mobility, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
    }

    [Fact]
    public void Shadowstep_Out_Of_Ability_Range_Skips()
    {
        var result = Evaluate(Slot.Mobility, 36554,
            Context(targetInMelee: TriState.No, range: Range((Slot.Mobility, TriState.No))));
        Assert.Equal(PolicyVerdict.Unavailable, result.Verdict);
    }

    [Fact]
    public void Gap_Closer_Out_Of_Combat_Holds()
    {
        var result = Evaluate(Slot.Mobility, 36554,
            Context(targetInMelee: TriState.No, range: Range((Slot.Mobility, TriState.Yes))),
            inCombat: false);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
    }

    [Fact]
    public void Escape_Utility_Is_Never_Automatic()
    {
        // v2.3: the curated NeverAutomatic flag is the DEFAULT for the user
        // ability policy, so a manual-by-design escape skips (not merely holds)
        // unless the user explicitly enables it.
        var result = Evaluate(Slot.Mobility, 195072, Context(targetInMelee: TriState.No));
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
        Assert.Contains("manual by design", result.Reason);
    }

    [Fact]
    public void Uncatalogued_Mobility_Holds()
    {
        var result = Evaluate(Slot.Mobility, 0, Context(targetInMelee: TriState.No));
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
    }

    // ---- solo / self-sustain ---------------------------------------------

    [Fact]
    public void SelfHeal_With_Solo_Off_Holds()
    {
        var result = Evaluate(Slot.SelfHeal, 202168, Context(hpValid: true, hp: 40));
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("solo", result.Reason);
    }

    [Fact]
    public void Solo_SelfHeal_High_Hp_Conserves()
    {
        var options = new PolicyOptions { SoloEnabled = true };
        var result = Evaluate(Slot.SelfHeal, 202168, Context(hpValid: true, hp: 90), options);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("conserving", result.Reason);
    }

    [Fact]
    public void Solo_SelfHeal_Mid_Hp_Uses()
    {
        var options = new PolicyOptions { SoloEnabled = true };
        var result = Evaluate(Slot.SelfHeal, 202168, Context(hpValid: true, hp: 50), options);
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.False(result.Emergency);
    }

    [Fact]
    public void Solo_SelfHeal_Emergency_Flags_Emergency()
    {
        var options = new PolicyOptions { SoloEnabled = true };
        var result = Evaluate(Slot.SelfHeal, 202168, Context(hpValid: true, hp: 30), options);
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.True(result.Emergency);
    }

    [Fact]
    public void Solo_SelfHeal_Unknown_Hp_Conserves()
    {
        var options = new PolicyOptions { SoloEnabled = true };
        var result = Evaluate(Slot.SelfHeal, 202168, Context(hpValid: false), options);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
    }

    [Fact]
    public void Solo_Ability_Threshold_Conserves_Lay_On_Hands()
    {
        var options = new PolicyOptions { SoloEnabled = true };
        // Lay on Hands is curated useBelowHpPct=40: at 60% HP it is conserved
        // by its own threshold before any generic sustain rule runs.
        var result = Evaluate(Slot.SelfHeal, 633, Context(hpValid: true, hp: 60), options);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("ability threshold", result.Reason);
    }

    [Fact]
    public void Solo_Overheal_Guard_Holds_A_Mostly_Wasted_Heal()
    {
        // Impending Victory heals 30% max HP: with a widened sustain window a
        // heal that would be mostly overheal must stay conserved.
        var options = new PolicyOptions { SoloEnabled = true, SelfSustainHpPct = 90 };
        var result = Evaluate(Slot.SelfHeal, 202168, Context(hpValid: true, hp: 85), options);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("overheal", result.Reason);
    }

    [Fact]
    public void Solo_NeverAutomatic_SelfHeal_Stays_Manual()
    {
        // Emerald Communion is curated as a never-automatic self-heal: its
        // default is OFF, so even at emergency HP Solo skips it (unless the
        // user explicitly enables it in the ability policy).
        var options = new PolicyOptions { SoloEnabled = true };
        var result = Evaluate(Slot.SelfHeal, 370960, Context(hpValid: true, hp: 30), options);
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
        Assert.Contains("manual by design", result.Reason);
    }

    [Fact]
    public void User_On_Override_Makes_A_Manual_SelfHeal_Eligible()
    {
        // The user may explicitly override the curated default: Emerald
        // Communion then runs through the normal Solo gates (here: emergency).
        var options = new PolicyOptions
        {
            SoloEnabled = true,
            Abilities = AbilityPolicy.Default.With(370960, enabled: true, defaultEnabled: false),
        };
        var result = Evaluate(Slot.SelfHeal, 370960, Context(hpValid: true, hp: 30), options);
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.True(result.Emergency);
    }

    [Fact]
    public void Solo_Melee_SelfHeal_Out_Of_Range_Skips()
    {
        var options = new PolicyOptions { SoloEnabled = true };
        var result = Evaluate(Slot.SelfHeal, 202168,
            Context(hpValid: true, hp: 50, range: Range((Slot.SelfHeal, TriState.No))), options);
        Assert.Equal(PolicyVerdict.Unavailable, result.Verdict);
        Assert.Contains("out of ability range", result.Reason);
    }

    [Fact]
    public void Solo_SelfHeal_Requiring_A_Target_Holds_Without_One()
    {
        var options = new PolicyOptions { SoloEnabled = true };
        var result = Evaluate(Slot.SelfHeal, 202168, Context(hpValid: true, hp: 50), options, hasTarget: false);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("requires a target", result.Reason);
    }

    [Fact]
    public void Solo_Defensive_Still_Sequences()
    {
        // Solo must not turn every defensive into an instant press.
        var options = new PolicyOptions { SoloEnabled = true };
        var memory = new PolicyMemory();
        memory.NoteUse(Catalog.TryGet(871)!, Now);

        var result = Evaluate(Slot.Defensive, 12975, Context(hpValid: true, hp: 80), options, memory, now: Now + 1000);
        // v2.6: with an unknown defensive urgency a major reports Unknown
        // (the urgency gate runs before the sequencing rule).
        Assert.Equal(PolicyVerdict.Unknown, result.Verdict);
    }

    [Fact]
    public void Solo_SelfHeal_Conserved_While_Immunity_Runs()
    {
        // Divine Shield already prevents the damage; healing into it is waste.
        var options = new PolicyOptions { SoloEnabled = true };
        var memory = new PolicyMemory();
        memory.NoteUse(Catalog.TryGet(642)!, Now);   // Divine Shield, Immunity tier

        var result = Evaluate(Slot.SelfHeal, 202168, Context(hpValid: true, hp: 50), options, memory, now: Now + 1000);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("immunity", result.Reason);
    }

    [Fact]
    public void Solo_Emergency_Overrides_Immunity_Hold()
    {
        // At lethal-low HP the heal is prepared before the immunity expires.
        var options = new PolicyOptions { SoloEnabled = true };
        var memory = new PolicyMemory();
        memory.NoteUse(Catalog.TryGet(642)!, Now);

        var result = Evaluate(Slot.SelfHeal, 202168, Context(hpValid: true, hp: 30), options, memory, now: Now + 1000);
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.True(result.Emergency);
    }

    // ---- unknown context --------------------------------------------------

    [Fact]
    public void Fully_Unknown_Context_Fails_Open_For_MaxDps_Slots()
    {
        var unknown = CombatContext.Unknown();
        Assert.Equal(PolicyVerdict.Use, Evaluate(Slot.Main, 0, unknown).Verdict);
        Assert.Equal(PolicyVerdict.Use, Evaluate(Slot.Interrupt, 6552, unknown).Verdict);
        // v2.3: an unknown context carries UNKNOWN urgency, never Red — a
        // major defensive holds. Short-CD minors keep the legacy gates.
        Assert.Equal(PolicyVerdict.Unknown, Evaluate(Slot.Defensive, 871, unknown).Verdict);
        Assert.Equal(PolicyVerdict.Use, Evaluate(Slot.Defensive, 23920, Context(targetCasting: TriState.Yes, contextValid: false)).Verdict);
        Assert.Equal(PolicyVerdict.Use, Evaluate(Slot.Offensive, 1719, unknown).Verdict);
        Assert.Equal(PolicyVerdict.Use, Evaluate(Slot.Consumable, 0, unknown).Verdict);
        Assert.Equal(PolicyVerdict.Use, Evaluate(Slot.Trinket, 0, unknown).Verdict);
    }

    [Fact]
    public void Unknown_Context_Holds_Companion_Only_Slots()
    {
        var unknown = CombatContext.Unknown();
        // v2.6: a gap-closer whose melee state is unknown is Unknown; a
        // self-heal whose HP is unknown still Holds (conserving the heal).
        Assert.Equal(PolicyVerdict.Unknown, Evaluate(Slot.Mobility, 36554, unknown).Verdict);
        Assert.Equal(PolicyVerdict.Hold, Evaluate(Slot.SelfHeal, 202168, unknown).Verdict);
    }
}
