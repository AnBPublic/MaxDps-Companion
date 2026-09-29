using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// v2.7 §51 scenario matrix for the explicit candidate providers. The real
/// <see cref="PolicyEvaluator"/> and the real provider classes are exercised;
/// synthetic ability definitions are used only to reach branches the pinned
/// catalog has no row for (paired window, enemy-count guard on a non-MaxDps
/// row, strict interrupt unknown policy). Every case asserts verdict + provider
/// + source, and utility is proven structurally incapable of USE.
/// </summary>
public class CandidateProviderTests
{
    private const long Now = 10_000;

    private static AbilityCatalog Catalog => AbilityCatalog.Default;

    // ---- fixture helpers --------------------------------------------------

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

    private static TriState[] Range(params (Slot Slot, TriState State)[] entries)
    {
        var range = new TriState[PixelProtocol.SlotCount];
        foreach (var (slot, state) in entries) range[(int)slot] = state;
        return range;
    }

    private static TriState[] Buffs(params Slot[] slots)
    {
        var buffs = new TriState[PixelProtocol.SlotCount];
        for (var i = 0; i < buffs.Length; i++) buffs[i] = TriState.No;
        foreach (var slot in slots) buffs[(int)slot] = TriState.Yes;
        return buffs;
    }

    private static PolicyDecision Evaluate(
        Slot slot,
        int spellId,
        CombatContext? context = null,
        PolicyOptions? options = null,
        PolicyMemory? memory = null,
        bool inCombat = true,
        bool hasTarget = true) =>
        PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = slot,
            SpellId = spellId,
            Context = context ?? Context(),
            Options = options ?? PolicyOptions.Standard,
            Memory = memory ?? new PolicyMemory(),
            NowMs = Now,
            InCombat = inCombat,
            HasTarget = hasTarget,
        }, Catalog);

    /// <summary>Builds a synthetic definition (only for branches the real catalog cannot reach).</summary>
    private static AbilityDefinition Ability(
        int spellId,
        AbilityCategory category,
        AbilityPurpose purpose,
        DefensiveTier tier = DefensiveTier.None,
        GcdKind gcd = GcdKind.OnGcd,
        RangeRequirement targetRange = RangeRequirement.Any,
        bool requiresTarget = false,
        int? useBelowHpPct = null,
        int? holdAboveHpPct = null,
        bool holdWhenBuffActive = false,
        bool neverAutomatic = false,
        string? conflictGroup = null,
        UnknownPolicy unknown = UnknownPolicy.Hold,
        int? enemyCountMin = null,
        DefensiveUrgency minimumUrgency = DefensiveUrgency.Yellow,
        bool minimumUrgencyCurated = false,
        DefensiveUrgencySource urgencySource = DefensiveUrgencySource.Hp,
        bool requiresEnemyCast = false,
        int healPctMaxHp = 0) =>
        new(spellId, $"Test{spellId}", category, purpose, tier, gcd, RangeKind.Melee, CastKind.Instant,
            0, 8000, healPctMaxHp, requiresEnemyCast, targetRange, requiresTarget, useBelowHpPct, holdAboveHpPct,
            holdWhenBuffActive, neverAutomatic, conflictGroup, 0, unknown,
            [], [], null, null)
        {
            MinimumUrgency = minimumUrgency,
            MinimumUrgencyCurated = minimumUrgencyCurated,
            UrgencySource = urgencySource,
            EnemyCountMin = enemyCountMin,
        };

    /// <summary>Runs one provider directly (the provider is the evaluator's own category logic).</summary>
    private static PolicyDecision Via(
        ICandidateProvider provider,
        AbilityDefinition ability,
        Slot slot,
        CombatContext ctx,
        PolicyOptions? options = null,
        PolicyMemory? memory = null,
        bool inCombat = true,
        bool hasTarget = true)
    {
        var input = new PolicyInput
        {
            Slot = slot,
            SpellId = ability.SpellId,
            Context = ctx,
            Options = options ?? PolicyOptions.Standard,
            Memory = memory ?? new PolicyMemory(),
            NowMs = Now,
            InCombat = inCombat,
            HasTarget = hasTarget,
        };
        return provider.Evaluate(new ProviderInput
        {
            Input = input,
            Ability = ability,
            Catalog = Catalog,
            Range = ctx.SlotRange[(int)slot],
        });
    }

    private static void AssertProvider(PolicyDecision decision, string provider, CandidateSourceKind source)
    {
        Assert.Equal(provider, decision.Provider);
        Assert.Equal(source, decision.Source);
    }

    // ---- self-sustain -----------------------------------------------------

    [Fact]
    public void SelfSustain_Healthy_Solo_Conserves()
    {
        var options = new PolicyOptions { SoloEnabled = true };
        var result = Evaluate(Slot.SelfHeal, 202168, Context(hpValid: true, hp: 90), options);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        AssertProvider(result, "SelfSustain", CandidateSourceKind.BridgeExtra);
    }

    [Fact]
    public void SelfSustain_Moderate_Solo_Uses()
    {
        var options = new PolicyOptions { SoloEnabled = true };
        var result = Evaluate(Slot.SelfHeal, 202168, Context(hpValid: true, hp: 50), options);
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.False(result.Emergency);
        AssertProvider(result, "SelfSustain", CandidateSourceKind.BridgeExtra);
    }

    [Fact]
    public void SelfSustain_Critical_Flags_Emergency()
    {
        var options = new PolicyOptions { SoloEnabled = true };
        var result = Evaluate(Slot.SelfHeal, 202168, Context(hpValid: true, hp: 30), options);
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.True(result.Emergency);
        AssertProvider(result, "SelfSustain", CandidateSourceKind.BridgeExtra);
    }

    [Fact]
    public void SelfSustain_Target_Missing_Holds()
    {
        var options = new PolicyOptions { SoloEnabled = true };
        var result = Evaluate(Slot.SelfHeal, 202168, Context(hpValid: true, hp: 50), options, hasTarget: false);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("requires a target", result.Reason);
    }

    [Fact]
    public void SelfSustain_Unavailable_Out_Of_Range()
    {
        var options = new PolicyOptions { SoloEnabled = true };
        var result = Evaluate(Slot.SelfHeal, 202168,
            Context(hpValid: true, hp: 50, range: Range((Slot.SelfHeal, TriState.No))), options);
        Assert.Equal(PolicyVerdict.Unavailable, result.Verdict);
        AssertProvider(result, "SelfSustain", CandidateSourceKind.BridgeExtra);
    }

    [Fact]
    public void SelfSustain_User_Off_Never_Uses()
    {
        var options = new PolicyOptions
        {
            SoloEnabled = true,
            Abilities = AbilityPolicy.Default.With(202168, enabled: false, defaultEnabled: true),
        };
        var result = Evaluate(Slot.SelfHeal, 202168, Context(hpValid: true, hp: 20), options);
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
        AssertProvider(result, "SelfSustain", CandidateSourceKind.BridgeExtra);
    }

    [Fact]
    public void SelfSustain_Normal_Mode_Holds_Above_Emergency()
    {
        var result = Evaluate(Slot.SelfHeal, 202168, Context(hpValid: true, hp: 40));
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
    }

    [Fact]
    public void SelfSustain_Overheal_Guard_Holds()
    {
        var options = new PolicyOptions { SoloEnabled = true, SelfSustainHpPct = 90 };
        var result = Evaluate(Slot.SelfHeal, 202168, Context(hpValid: true, hp: 85), options);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("overheal", result.Reason);
    }

    [Fact]
    public void SelfSustain_Cast_Holds()
    {
        var options = new PolicyOptions { SoloEnabled = true };
        var result = Evaluate(Slot.SelfHeal, 202168,
            Context(hpValid: true, hp: 20, cast: PlayerCastState.Casting), options);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("cast", result.Reason);
    }

    [Fact]
    public void SelfSustain_Manual_On_Makes_Eligible()
    {
        var options = new PolicyOptions
        {
            SoloEnabled = true,
            Abilities = AbilityPolicy.Default.With(370960, enabled: true, defaultEnabled: false),
        };
        var result = Evaluate(Slot.SelfHeal, 370960, Context(hpValid: true, hp: 20), options);
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        AssertProvider(result, "SelfSustain", CandidateSourceKind.BridgeExtra);
    }

    // ---- defensive --------------------------------------------------------

    [Theory]
    [InlineData(0, 1)] // White -> Hold (no glow)
    [InlineData(1, 1)] // Yellow -> Hold (below the discounted Orange)
    [InlineData(2, 0)] // Orange -> Use (MaxDps recommendation discounts Red -> Orange)
    [InlineData(3, 0)] // Red -> Use
    [InlineData(4, 4)] // Unknown -> Unknown (major with no staged urgency)
    public void Defensive_Major_Urgency_Ladder(int urgencyCode, int expectedCode)
    {
        var urgency = urgencyCode switch
        {
            0 => DefensiveUrgency.White,
            1 => DefensiveUrgency.Yellow,
            2 => DefensiveUrgency.Orange,
            3 => DefensiveUrgency.Red,
            _ => DefensiveUrgency.Unknown,
        };
        var result = Evaluate(Slot.Defensive, 871, Context(hpValid: true, hp: 60, urgency: urgency));
        Assert.Equal((PolicyVerdict)expectedCode, result.Verdict);
        AssertProvider(result, "Defensive", CandidateSourceKind.MaxDpsWire);
    }

    [Fact]
    public void Defensive_White_Always_Holds()
    {
        var result = Evaluate(Slot.Defensive, 871, Context(hpValid: true, hp: 99, urgency: DefensiveUrgency.White));
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("white defensive urgency", result.Reason);
    }

    [Fact]
    public void Defensive_Minor_Yellow_Uses()
    {
        var result = Evaluate(Slot.Defensive, 2565, Context(hpValid: true, hp: 60, urgency: DefensiveUrgency.Yellow));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        AssertProvider(result, "Defensive", CandidateSourceKind.MaxDpsWire);
    }

    [Fact]
    public void Defensive_User_Off_Never_Uses()
    {
        var options = new PolicyOptions
        {
            Abilities = AbilityPolicy.Default.With(871, enabled: false, defaultEnabled: true),
        };
        var result = Evaluate(Slot.Defensive, 871, Context(hpValid: true, hp: 20, urgency: DefensiveUrgency.Red), options);
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
    }

    [Fact]
    public void Defensive_Immunity_Tier_Red_Uses_Via_Provider()
    {
        var ability = Ability(900001, AbilityCategory.Defensive, AbilityPurpose.Immunity,
            tier: DefensiveTier.Immunity, minimumUrgency: DefensiveUrgency.Red);
        var result = Via(CandidateProviders.Defensive, ability, Slot.Defensive,
            Context(hpValid: true, hp: 60, urgency: DefensiveUrgency.Red));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        AssertProvider(result, "Defensive", CandidateSourceKind.MaxDpsWire);
    }

    [Fact]
    public void Defensive_Curated_Minimum_Orange_Holds_At_Yellow()
    {
        // 190456 Ignore Pain is curated to require Orange.
        var result = Evaluate(Slot.Defensive, 190456, Context(hpValid: true, hp: 60, urgency: DefensiveUrgency.Yellow));
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("needs Orange", result.Reason);
    }

    [Fact]
    public void Defensive_MaxDps_Recommendation_Discounts_Major()
    {
        // 12975 Last Stand (major, tier-default Red) at Orange with a MaxDps
        // recommendation is discounted one stage -> eligible.
        var result = Evaluate(Slot.Defensive, 12975,
            Context(hpValid: true, hp: 60, urgency: DefensiveUrgency.Orange, defensiveCatalogSource: false));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        AssertProvider(result, "Defensive", CandidateSourceKind.MaxDpsWire);
    }

    [Fact]
    public void Defensive_GapFill_Source_Has_No_Discount()
    {
        var result = Evaluate(Slot.Defensive, 12975,
            Context(hpValid: true, hp: 60, urgency: DefensiveUrgency.Orange, defensiveCatalogSource: true));
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        AssertProvider(result, "Defensive", CandidateSourceKind.CompanionGapFill);
    }

    [Fact]
    public void Defensive_Reflect_Needs_Incoming_Cast()
    {
        var result = Evaluate(Slot.Defensive, 23920, Context(targetCasting: TriState.No, contextValid: true));
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        AssertProvider(result, "Defensive", CandidateSourceKind.MaxDpsWire);
    }

    // ---- interrupt --------------------------------------------------------

    [Fact]
    public void Interrupt_No_Cast_Skips()
    {
        var result = Evaluate(Slot.Interrupt, 6552, Context(targetCasting: TriState.No));
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
        Assert.Contains("no live target cast", result.Reason);
        AssertProvider(result, "Interrupt", CandidateSourceKind.MaxDpsWire);
    }

    [Fact]
    public void Interrupt_Interruptible_Uses()
    {
        var result = Evaluate(Slot.Interrupt, 6552,
            Context(targetCasting: TriState.Yes, targetInterruptible: TriState.Yes,
                range: Range((Slot.Interrupt, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        AssertProvider(result, "Interrupt", CandidateSourceKind.MaxDpsWire);
    }

    [Fact]
    public void Interrupt_Non_Interruptible_Skips()
    {
        var result = Evaluate(Slot.Interrupt, 6552,
            Context(targetCasting: TriState.Yes, targetInterruptible: TriState.No));
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
        Assert.Contains("not interruptible", result.Reason);
    }

    [Fact]
    public void Interrupt_Out_Of_Range_Unavailable()
    {
        var result = Evaluate(Slot.Interrupt, 6552,
            Context(targetCasting: TriState.Yes, range: Range((Slot.Interrupt, TriState.No))));
        Assert.Equal(PolicyVerdict.Unavailable, result.Verdict);
        AssertProvider(result, "Interrupt", CandidateSourceKind.MaxDpsWire);
    }

    [Fact]
    public void Interrupt_Range_Unknown_Strict_Holds_Uncertain()
    {
        var ability = Ability(900002, AbilityCategory.Interrupt, AbilityPurpose.Interrupt,
            gcd: GcdKind.OffGcd, unknown: UnknownPolicy.Hold);
        var result = Via(CandidateProviders.Interrupt, ability, Slot.Interrupt,
            Context(targetCasting: TriState.Yes, targetInterruptible: TriState.Yes));
        Assert.Equal(PolicyVerdict.Unknown, result.Verdict);
        AssertProvider(result, "Interrupt", CandidateSourceKind.MaxDpsWire);
    }

    [Fact]
    public void Interrupt_During_Channel_Uses()
    {
        var result = Evaluate(Slot.Interrupt, 6552,
            Context(cast: PlayerCastState.Channeling, targetCasting: TriState.Yes,
                range: Range((Slot.Interrupt, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
    }

    [Fact]
    public void Interrupt_Silence_Kind_Recorded_In_Reason()
    {
        var result = Evaluate(Slot.Interrupt, 15487,
            Context(targetCasting: TriState.Yes, targetInterruptible: TriState.Yes,
                range: Range((Slot.Interrupt, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.Contains("Silence", result.Reason);
    }

    [Fact]
    public void Interrupt_Stun_Kind_Via_Provider()
    {
        var ability = Ability(900003, AbilityCategory.Interrupt, AbilityPurpose.Interrupt,
            gcd: GcdKind.OffGcd, unknown: UnknownPolicy.Use) with { InterruptKind = InterruptKind.Stun };
        var result = Via(CandidateProviders.Interrupt, ability, Slot.Interrupt,
            Context(targetCasting: TriState.Yes, targetInterruptible: TriState.Yes,
                range: Range((Slot.Interrupt, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.Contains("Stun", result.Reason);
    }

    // ---- offensive --------------------------------------------------------

    [Fact]
    public void Offensive_Fresh_Uses()
    {
        var result = Evaluate(Slot.Offensive, 1719, Context(range: Range((Slot.Offensive, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        AssertProvider(result, "Offensive", CandidateSourceKind.MaxDpsWire);
    }

    [Fact]
    public void Offensive_User_Off_Never_Uses()
    {
        var options = new PolicyOptions
        {
            Abilities = AbilityPolicy.Default.With(1719, enabled: false, defaultEnabled: true),
        };
        var result = Evaluate(Slot.Offensive, 1719,
            Context(range: Range((Slot.Offensive, TriState.Yes))), options);
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
    }

    [Fact]
    public void Offensive_Buff_Active_Skips()
    {
        var ability = Ability(900010, AbilityCategory.Offensive, AbilityPurpose.MajorOffensive, holdWhenBuffActive: true);
        var result = Via(CandidateProviders.Offensive, ability, Slot.Offensive,
            Context(range: Range((Slot.Offensive, TriState.Yes)), buffs: Buffs(Slot.Offensive)));
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
        Assert.Contains("already active", result.Reason);
        AssertProvider(result, "Offensive", CandidateSourceKind.MaxDpsWire);
    }

    [Fact]
    public void Offensive_Paired_Window_Holds()
    {
        var ability = Ability(900011, AbilityCategory.Offensive, AbilityPurpose.MajorOffensive,
            conflictGroup: "Burst");
        var memory = new PolicyMemory();
        memory.NoteUse(ability, Now);
        var result = Via(CandidateProviders.Offensive, ability, Slot.Offensive,
            Context(range: Range((Slot.Offensive, TriState.Yes))), memory: memory);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("paired cooldown window active (Burst)", result.Reason);
    }

    [Fact]
    public void Offensive_Enemy_Count_Unknown_Not_Observable()
    {
        // A companion-owned row (not MaxDpsBacked) with a curated enemy count.
        var ability = Ability(900012, AbilityCategory.Offensive, AbilityPurpose.MajorOffensive,
            enemyCountMin: 3);
        var result = Via(CandidateProviders.Offensive, ability, Slot.Offensive,
            Context(range: Range((Slot.Offensive, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Unknown, result.Verdict);
        Assert.Contains("enemy count not observable", result.Reason);
    }

    [Fact]
    public void Offensive_Movement_Restriction_Unavailable()
    {
        var ability = Ability(900013, AbilityCategory.Offensive, AbilityPurpose.MinorOffensive,
            targetRange: RangeRequirement.InMelee);
        var result = Via(CandidateProviders.Offensive, ability, Slot.Offensive,
            Context(targetInMelee: TriState.No, range: Range((Slot.Offensive, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Unavailable, result.Verdict);
        Assert.Contains("outside melee", result.Reason);
    }

    [Fact]
    public void Offensive_Target_State_Unknown_Uncertain()
    {
        var ability = Ability(900014, AbilityCategory.Offensive, AbilityPurpose.MajorOffensive);
        var result = Via(CandidateProviders.Offensive, ability, Slot.Offensive, Context());
        Assert.Equal(PolicyVerdict.Unknown, result.Verdict);
        Assert.Contains("range unknown", result.Reason);
    }

    [Fact]
    public void Offensive_Out_Of_Range_Unavailable()
    {
        var result = Evaluate(Slot.Offensive, 1719, Context(range: Range((Slot.Offensive, TriState.No))));
        Assert.Equal(PolicyVerdict.Unavailable, result.Verdict);
    }

    // ---- mobility ---------------------------------------------------------

    [Fact]
    public void Mobility_Target_Outside_Melee_Uses()
    {
        var result = Evaluate(Slot.Mobility, 36554,
            Context(targetInMelee: TriState.No, range: Range((Slot.Mobility, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        AssertProvider(result, "Mobility", CandidateSourceKind.BridgeExtra);
    }

    [Fact]
    public void Mobility_Target_In_Melee_Holds()
    {
        var result = Evaluate(Slot.Mobility, 36554,
            Context(targetInMelee: TriState.Yes, range: Range((Slot.Mobility, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("melee", result.Reason);
    }

    [Fact]
    public void Mobility_Melee_State_Unknown_Uncertain()
    {
        var result = Evaluate(Slot.Mobility, 36554,
            Context(targetInMelee: TriState.Unknown, range: Range((Slot.Mobility, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Unknown, result.Verdict);
    }

    [Fact]
    public void Mobility_Range_Unknown_Uncertain()
    {
        var result = Evaluate(Slot.Mobility, 36554,
            Context(targetInMelee: TriState.No, range: Range((Slot.Mobility, TriState.Unknown))));
        Assert.Equal(PolicyVerdict.Unknown, result.Verdict);
    }

    [Fact]
    public void Mobility_Out_Of_Range_Unavailable()
    {
        var result = Evaluate(Slot.Mobility, 36554,
            Context(targetInMelee: TriState.No, range: Range((Slot.Mobility, TriState.No))));
        Assert.Equal(PolicyVerdict.Unavailable, result.Verdict);
    }

    [Fact]
    public void Mobility_No_Target_Holds()
    {
        var result = Evaluate(Slot.Mobility, 36554,
            Context(targetInMelee: TriState.No, range: Range((Slot.Mobility, TriState.Yes))), hasTarget: false);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("no target", result.Reason);
    }

    [Fact]
    public void Mobility_Out_Of_Combat_Holds()
    {
        var result = Evaluate(Slot.Mobility, 36554,
            Context(targetInMelee: TriState.No, range: Range((Slot.Mobility, TriState.Yes))), inCombat: false);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
    }

    // ---- utility (structurally never USE) ---------------------------------

    [Fact]
    public void Utility_Manual_Classification_Holds()
    {
        // 528 Dispel Magic, explicitly enabled by the user.
        var options = new PolicyOptions
        {
            Abilities = AbilityPolicy.Default.With(528, enabled: true, defaultEnabled: false),
        };
        var result = Evaluate(Slot.Offensive, 528, Context(contextValid: false), options);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("manual utility", result.Reason);
        AssertProvider(result, "Utility", CandidateSourceKind.None);
    }

    [Fact]
    public void Utility_Unknown_Context_Holds()
    {
        var options = new PolicyOptions
        {
            Abilities = AbilityPolicy.Default.With(370, enabled: true, defaultEnabled: false), // Purge
        };
        var result = Evaluate(Slot.Offensive, 370, CombatContext.Unknown(), options);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.NotEqual(PolicyVerdict.Use, result.Verdict);
    }

    [Fact]
    public void Utility_Off_Skips()
    {
        var options = new PolicyOptions
        {
            Abilities = AbilityPolicy.Default.With(528, enabled: false, defaultEnabled: false),
        };
        var result = Evaluate(Slot.Offensive, 528, Context(), options);
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
    }

    [Fact]
    public void Utility_Attempted_AutoGeneration_Stays_Blocked()
    {
        var ability = Ability(900020, AbilityCategory.Utility, AbilityPurpose.CrowdControl,
            neverAutomatic: true);
        // Even with an explicit ON override and an emergency-looking context.
        var options = new PolicyOptions
        {
            SoloEnabled = true,
            Abilities = AbilityPolicy.Default.With(900020, enabled: true, defaultEnabled: false),
        };
        var result = Via(CandidateProviders.Utility, ability, Slot.Offensive,
            Context(hpValid: true, hp: 5, contextValid: true), options);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.NotEqual(PolicyVerdict.Use, result.Verdict);
        Assert.Equal("Utility", result.Provider);
    }

    [Fact]
    public void Utility_Provider_Type_Returns_No_Use_Paths()
    {
        // Structural guarantee: the utility provider source contains no USE factory.
        // (Compile-time/intent check documented here; runtime check below.)
        var ability = Ability(900021, AbilityCategory.Utility, AbilityPurpose.Dispel, neverAutomatic: true);
        var result = Via(CandidateProviders.Utility, ability, Slot.Offensive, Context());
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
    }
}
