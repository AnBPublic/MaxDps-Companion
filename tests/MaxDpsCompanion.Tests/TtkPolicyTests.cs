using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// A2/A4: the TTK gate thresholds, tier defaults and the schema parse. The
/// estimator tests own the measurement; these tests pin the decision constants
/// and the per-rule behaviour.
/// </summary>
public class TtkPolicyTests
{
    private static AbilityCatalog Catalog => AbilityCatalog.Default;

    /// <summary>A minimal offensive definition; ctor args are the record's required order.</summary>
    private static AbilityDefinition Offensive(
        OffensiveUsage usage = OffensiveUsage.MajorBurst,
        double? minTtkSec = null,
        int cooldownMs = 120_000,
        int durationMs = 0,
        int? executeBelowPct = null,
        bool executeFavored = false) =>
        new(
            SpellId: 900001,
            Name: "Test Burst",
            Category: AbilityCategory.Offensive,
            Purpose: AbilityPurpose.MajorOffensive,
            Tier: DefensiveTier.None,
            Gcd: GcdKind.OnGcd,
            Range: RangeKind.Melee,
            Cast: CastKind.Instant,
            CooldownMs: cooldownMs,
            DurationMs: durationMs,
            HealPctMaxHp: 0,
            RequiresEnemyCast: false,
            TargetRange: RangeRequirement.Any,
            RequiresTarget: true,
            UseBelowHpPct: null,
            HoldAboveHpPct: null,
            HoldWhenBuffActive: false,
            NeverAutomatic: false,
            ConflictGroup: null,
            Priority: 0,
            Unknown: UnknownPolicy.Use,
            Classes: [],
            Specs: [],
            Note: null,
            Source: null)
        {
            OffensiveUsage = usage,
            MinTtkSec = minTtkSec,
            ExecuteBelowPct = executeBelowPct,
            ExecuteFavored = executeFavored,
        };

    private static CombatContext Combat(bool ttkValid, double ttkSec, bool targetHpValid = false, int targetHpPct = 0) =>
        new()
        {
            TtkValid = ttkValid,
            TtkSec = ttkSec,
            TargetHpValid = targetHpValid,
            TargetHpPct = targetHpPct,
        };

    // ----- A2: schema parse -----

    [Fact]
    public void ParseTtk_Present_Reads_All_Three_Fields()
    {
        var o = new AbilityCatalog.AbilityOverride
        {
            MinTtkSec = 15.5,
            ExecuteBelowPct = 20,
            ExecuteFavored = true,
        };
        var parsed = AbilityCatalog.ParseTtkCuration(o);
        Assert.Equal(15.5, parsed.MinTtkSec);
        Assert.Equal(20, parsed.ExecuteBelowPct);
        Assert.True(parsed.ExecuteFavored);
    }

    [Fact]
    public void ParseTtk_Absent_Yields_Defaults()
    {
        var none = AbilityCatalog.ParseTtkCuration(null);
        Assert.Null(none.MinTtkSec);
        Assert.Null(none.ExecuteBelowPct);
        Assert.False(none.ExecuteFavored);

        var empty = AbilityCatalog.ParseTtkCuration(new AbilityCatalog.AbilityOverride());
        Assert.Null(empty.MinTtkSec);
        Assert.Null(empty.ExecuteBelowPct);
        Assert.False(empty.ExecuteFavored);
    }

    [Theory]
    [InlineData((int)OffensiveUsage.MajorBurst, 12.0)]
    [InlineData((int)OffensiveUsage.Transformation, 20.0)]
    [InlineData((int)OffensiveUsage.Summon, 20.0)]
    [InlineData((int)OffensiveUsage.WindowDriven, 10.0)]
    [InlineData((int)OffensiveUsage.ShortCooldown, 5.0)]
    [InlineData((int)OffensiveUsage.ProcDriven, 5.0)]
    [InlineData((int)OffensiveUsage.AoeOnly, 5.0)]
    [InlineData((int)OffensiveUsage.SingleTargetOnly, 5.0)]
    [InlineData((int)OffensiveUsage.ResourceDriven, 5.0)]
    [InlineData((int)OffensiveUsage.Execute, 5.0)]
    [InlineData((int)OffensiveUsage.DefensiveOffensiveHybrid, 0.0)]
    [InlineData((int)OffensiveUsage.Unknown, 10.0)]
    public void Default_MinTtk_By_Usage(int usage, double expected)
    {
        Assert.Equal(expected, TtkPolicy.DefaultMinTtkSec((OffensiveUsage)usage));
    }

    [Fact]
    public void Curated_MinTtk_Wins_Over_Default()
    {
        var ability = Offensive(OffensiveUsage.MajorBurst, minTtkSec: 25);
        Assert.Equal(25, TtkPolicy.MinTtkSec(ability));
    }

    [Fact]
    public void Loaded_Offensive_Without_Curation_Uses_Default_And_Execute_Off()
    {
        // Avatar 107574 is a curated MajorBurst with no TTK fields yet (T-B owns curation).
        var avatar = Catalog.TryGet(107574);
        if (avatar is null) return; // catalog drift: covered by its own tests
        if (avatar.MinTtkSec is null)
            Assert.Equal(TtkPolicy.DefaultMinTtkSec(avatar.OffensiveUsage), TtkPolicy.MinTtkSec(avatar));
        Assert.False(avatar.ExecuteFavored);
    }

    // ----- A4: gate constants -----

    [Fact]
    public void Waste_Guard_Holds_Only_On_Valid_Short_Ttk()
    {
        var ability = Offensive(OffensiveUsage.MajorBurst); // default 12 s
        Assert.True(TtkPolicy.WasteGuardHolds(ability, Combat(ttkValid: true, ttkSec: 5)));
        Assert.False(TtkPolicy.WasteGuardHolds(ability, Combat(ttkValid: true, ttkSec: 20)));
        // Unknown TTK never holds (fail open).
        Assert.False(TtkPolicy.WasteGuardHolds(ability, Combat(ttkValid: false, ttkSec: 5)));
    }

    [Fact]
    public void Two_Uses_Requires_Two_Cooldowns_Plus_Duration()
    {
        var ability = Offensive(OffensiveUsage.MajorBurst, cooldownMs: 120_000, durationMs: 10_000);
        // 2*120 + 10 = 250 s.
        Assert.True(TtkPolicy.TwoUsesAvailable(ability, Combat(ttkValid: true, ttkSec: 260)));
        Assert.False(TtkPolicy.TwoUsesAvailable(ability, Combat(ttkValid: true, ttkSec: 240)));
        // Absent cooldown => rule skipped.
        var noCd = Offensive(OffensiveUsage.MajorBurst, cooldownMs: 0);
        Assert.False(TtkPolicy.TwoUsesAvailable(noCd, Combat(ttkValid: true, ttkSec: 300)));
    }

    [Fact]
    public void Execute_Range_Requires_Favored_And_Valid_Target_Hp()
    {
        var ability = Offensive(executeBelowPct: 20, executeFavored: true);
        Assert.True(TtkPolicy.ExecuteRange(ability, Combat(ttkValid: false, ttkSec: 10, targetHpValid: true, targetHpPct: 15)));
        Assert.False(TtkPolicy.ExecuteRange(ability, Combat(ttkValid: true, ttkSec: 10, targetHpValid: true, targetHpPct: 25)));
        Assert.False(TtkPolicy.ExecuteRange(ability, Combat(ttkValid: true, ttkSec: 10, targetHpValid: false, targetHpPct: 10)));
        var inert = Offensive(executeBelowPct: 20, executeFavored: false);
        Assert.False(TtkPolicy.ExecuteRange(inert, Combat(ttkValid: true, ttkSec: 10, targetHpValid: true, targetHpPct: 10)));
    }

    [Fact]
    public void Dying_Target_Holds_Only_Solo_NonEmergency_And_Short()
    {
        Assert.True(TtkPolicy.DyingTargetHolds(Combat(ttkValid: true, ttkSec: 4), soloEnabled: true, emergency: false));
        Assert.False(TtkPolicy.DyingTargetHolds(Combat(ttkValid: true, ttkSec: 4), soloEnabled: true, emergency: true));
        Assert.False(TtkPolicy.DyingTargetHolds(Combat(ttkValid: true, ttkSec: 4), soloEnabled: false, emergency: false));
        Assert.False(TtkPolicy.DyingTargetHolds(Combat(ttkValid: true, ttkSec: 9), soloEnabled: true, emergency: false));
        Assert.False(TtkPolicy.DyingTargetHolds(Combat(ttkValid: false, ttkSec: 4), soloEnabled: true, emergency: false));
    }

    // ----- A4: provider-level rule scenarios -----

    private static AbilityDefinition WithGroup(AbilityDefinition ability, string group) => ability with { ConflictGroup = group };

    private static ProviderInput PInput(
        AbilityDefinition ability,
        CombatContext ctx,
        PolicyMemory? memory = null,
        bool solo = false,
        TriState range = TriState.Yes,
        bool inCombat = true,
        bool hasTarget = true) =>
        new()
        {
            Input = new PolicyInput
            {
                Slot = ability.Category == AbilityCategory.Defensive ? Slot.Defensive : Slot.Offensive,
                SpellId = ability.SpellId,
                Context = ctx,
                Options = new PolicyOptions { SoloEnabled = solo },
                Memory = memory ?? new PolicyMemory(),
                NowMs = 1000,
                InCombat = inCombat,
                HasTarget = hasTarget,
            },
            Ability = ability,
            Catalog = Catalog,
            Range = range,
        };

    [Fact]
    public void Offensive_Waste_Guard_Holds_Both_Sources()
    {
        var ability = WithGroup(Offensive(OffensiveUsage.MajorBurst), "Burst");
        var shortTtk = Combat(ttkValid: true, ttkSec: 5);
        var held = CandidateProviders.Offensive.Evaluate(PInput(ability, shortTtk));
        Assert.Equal(PolicyVerdict.Hold, held.Verdict);
        Assert.Contains("saving", held.Reason);

        // Unknown TTK passes the rule (fail open).
        var unknown = CandidateProviders.Offensive.Evaluate(PInput(ability, Combat(ttkValid: false, ttkSec: 5)));
        Assert.Equal(PolicyVerdict.Use, unknown.Verdict);
    }

    [Fact]
    public void Offensive_Two_Uses_Bypasses_Active_Pair()
    {
        var ability = WithGroup(Offensive(OffensiveUsage.MajorBurst, cooldownMs: 120_000, durationMs: 10_000), "Burst");
        var memory = new PolicyMemory();
        memory.NoteUse(WithGroup(Offensive(OffensiveUsage.MajorBurst), "Burst"), 500); // pair active

        // 240 s is short of 2*120+10; pairing holds.
        var held = CandidateProviders.Offensive.Evaluate(PInput(ability, Combat(ttkValid: true, ttkSec: 240), memory));
        Assert.Equal(PolicyVerdict.Hold, held.Verdict);

        // 260 s fits a second full use: the pairing hold is bypassed and it fires.
        var fired = CandidateProviders.Offensive.Evaluate(PInput(ability, Combat(ttkValid: true, ttkSec: 260), memory));
        Assert.Equal(PolicyVerdict.Use, fired.Verdict);
    }

    [Fact]
    public void Offensive_Execute_Bypasses_Active_Pair()
    {
        var ability = WithGroup(
            Offensive(OffensiveUsage.MajorBurst, cooldownMs: 120_000, executeBelowPct: 20, executeFavored: true),
            "Burst");
        var memory = new PolicyMemory();
        memory.NoteUse(WithGroup(Offensive(OffensiveUsage.MajorBurst), "Burst"), 500);

        var fired = CandidateProviders.Offensive.Evaluate(
            PInput(ability, Combat(ttkValid: false, ttkSec: 300, targetHpValid: true, targetHpPct: 15), memory));
        Assert.Equal(PolicyVerdict.Use, fired.Verdict);

        var notFavored = WithGroup(
            Offensive(OffensiveUsage.MajorBurst, cooldownMs: 120_000, executeBelowPct: 20, executeFavored: false),
            "Burst");
        var held = CandidateProviders.Offensive.Evaluate(
            PInput(notFavored, Combat(ttkValid: false, ttkSec: 300, targetHpValid: true, targetHpPct: 15), memory));
        Assert.Equal(PolicyVerdict.Hold, held.Verdict);
    }

    private static AbilityDefinition Defensive(int? useBelowHp = null) =>
        new(
            SpellId: 900002,
            Name: "Test Wall",
            Category: AbilityCategory.Defensive,
            Purpose: AbilityPurpose.DefensiveMajor,
            Tier: DefensiveTier.Major,
            Gcd: GcdKind.OnGcd,
            Range: RangeKind.SelfOnly,
            Cast: CastKind.Instant,
            CooldownMs: 120_000,
            DurationMs: 8000,
            HealPctMaxHp: 0,
            RequiresEnemyCast: false,
            TargetRange: RangeRequirement.Any,
            RequiresTarget: false,
            UseBelowHpPct: useBelowHp,
            HoldAboveHpPct: null,
            HoldWhenBuffActive: false,
            NeverAutomatic: false,
            ConflictGroup: "Def.Major",
            Priority: 0,
            Unknown: UnknownPolicy.Use,
            Classes: [],
            Specs: [],
            Note: null,
            Source: null);

    private static CombatContext DefCombat(bool ttkValid, double ttkSec, bool hpValid = false, int hp = 0) =>
        new()
        {
            TtkValid = ttkValid,
            TtkSec = ttkSec,
            HpValid = hpValid,
            HpPct = hp,
            HpPctUpper = hp,
            DefensiveUrgency = DefensiveUrgency.Red,
        };

    [Fact]
    public void Solo_Defensive_Holds_When_Target_Dies_Imminently()
    {
        var ability = Defensive();
        var held = CandidateProviders.Defensive.Evaluate(
            PInput(ability, DefCombat(ttkValid: true, ttkSec: 4), solo: true));
        Assert.Equal(PolicyVerdict.Hold, held.Verdict);
        Assert.Contains("saving", held.Reason);
    }

    [Fact]
    public void Solo_Defensive_Emergency_Overrides_The_Ttk_Hold()
    {
        var ability = Defensive();
        var decision = CandidateProviders.Defensive.Evaluate(
            PInput(ability, DefCombat(ttkValid: true, ttkSec: 4, hpValid: true, hp: 20), solo: true));
        Assert.Equal(PolicyVerdict.Use, decision.Verdict);
        Assert.True(decision.Emergency);
    }

    [Fact]
    public void NonSolo_Defensive_Ignores_The_Ttk_Hold()
    {
        var ability = Defensive();
        var decision = CandidateProviders.Defensive.Evaluate(
            PInput(ability, DefCombat(ttkValid: true, ttkSec: 4), solo: false));
        Assert.Equal(PolicyVerdict.Use, decision.Verdict);
    }

    [Fact]
    public void Unknown_Ttk_Defensive_Is_Unaffected()
    {
        var ability = Defensive();
        var decision = CandidateProviders.Defensive.Evaluate(
            PInput(ability, DefCombat(ttkValid: false, ttkSec: 4), solo: true));
        Assert.Equal(PolicyVerdict.Use, decision.Verdict);
    }
}
