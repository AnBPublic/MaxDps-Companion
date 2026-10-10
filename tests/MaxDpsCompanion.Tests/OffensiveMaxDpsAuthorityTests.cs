using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// v3.7.9 MaxDps-authority contract: a MaxDps-offered Main/Offensive candidate
/// is USED unless a game-truth/structural gate applies. The companion's TTK
/// conservation cluster (T1 waste guard, grace/warmup holds, adaptive history,
/// enemy-count / range Uncertain holds) was removed, so Avatar 107574 and
/// Ravager 228920 fire on any target — estimable or not. Only a confirmed
/// range / melee failure vetoes, and the opt-in Burst preset still holds a major
/// until a valid boss TTK exists.
/// </summary>
public class OffensiveMaxDpsAuthorityTests
{
    private static AbilityCatalog Catalog => AbilityCatalog.Default;
    private const long Now = 1000;

    private static AbilityDefinition Ability(int id)
    {
        var a = Catalog.TryGet(id);
        Assert.NotNull(a);
        return a!;
    }

    private static ProviderInput PInput(
        AbilityDefinition ability,
        CombatContext ctx,
        PolicyOptions? options = null,
        TriState range = TriState.Yes,
        bool inCombat = true) =>
        new()
        {
            Input = new PolicyInput
            {
                Slot = Slot.Offensive,
                SpellId = ability.SpellId,
                Context = ctx,
                Options = options ?? new PolicyOptions(),
                Memory = new PolicyMemory(),
                NowMs = Now,
                InCombat = inCombat,
                HasTarget = true,
            },
            Ability = ability,
            Catalog = Catalog,
            Range = range,
        };

    private static CombatContext Ctx(
        bool ttkValid,
        double ttkSec,
        bool provisional = false,
        bool histBinding = false,
        double histSec = 0,
        bool latch = false,
        double ageSec = 0) =>
        new()
        {
            TtkValid = ttkValid,
            TtkSec = ttkSec,
            TtkProvisional = provisional,
            TtkHistBinding = histBinding,
            TtkHistSec = histSec,
            FastPackLatch = latch,
            TargetAgeSec = ageSec,
        };

    public static IEnumerable<object[]> Majors =>
        [new object[] { 107574 }, new object[] { 228920 }];

    [Theory]
    [MemberData(nameof(Majors))]
    public void MaxDps_Major_On_Valid_Short_Ttk_Is_Used(int id)
    {
        var d = CandidateProviders.Offensive.Evaluate(PInput(Ability(id), Ctx(ttkValid: true, ttkSec: 5)));
        Assert.Equal(PolicyVerdict.Use, d.Verdict);
    }

    [Theory]
    [MemberData(nameof(Majors))]
    public void MaxDps_Major_Is_Used_Across_Provisional_History_FastPack_And_Warmup(int id)
    {
        var ability = Ability(id);

        // A provisional (short) live rate would previously hold the major.
        Assert.Equal(PolicyVerdict.Use,
            CandidateProviders.Offensive.Evaluate(PInput(ability, Ctx(false, 5, provisional: true))).Verdict);

        // A short trash-learned history window would previously hold it.
        Assert.Equal(PolicyVerdict.Use,
            CandidateProviders.Offensive.Evaluate(PInput(ability, Ctx(false, 300, histBinding: true, histSec: 4))).Verdict);

        // A fast-pack latch on a young target with no estimate would previously hold it.
        Assert.Equal(PolicyVerdict.Use,
            CandidateProviders.Offensive.Evaluate(PInput(ability, Ctx(false, 300, latch: true, ageSec: 1))).Verdict);

        // The warmup window (young unknown target) would previously hold it.
        Assert.Equal(PolicyVerdict.Use,
            CandidateProviders.Offensive.Evaluate(PInput(ability, Ctx(false, 300, ageSec: 1),
                new PolicyOptions { TtkWarmupSec = 3.0 })).Verdict);
    }

    [Fact]
    public void Range_Unknown_With_NonUse_UnknownPolicy_Is_Used()
    {
        // A synthetic major whose UnknownPolicy would previously have produced
        // an Uncertain hold on an unknown range probe.
        var ability = Synthetic(UnknownPolicy.Hold);
        var d = CandidateProviders.Offensive.Evaluate(PInput(ability, Ctx(false, 300), range: TriState.Unknown));
        Assert.Equal(PolicyVerdict.Use, d.Verdict);
    }

    [Fact]
    public void Range_No_Is_Unavailable()
    {
        var d = CandidateProviders.Offensive.Evaluate(PInput(Ability(107574), Ctx(true, 60), range: TriState.No));
        Assert.Equal(PolicyVerdict.Unavailable, d.Verdict);
    }

    [Fact]
    public void Burst_Preset_Still_Holds_A_Major_On_Invalid_Ttk()
    {
        var d = CandidateProviders.Offensive.Evaluate(
            PInput(Ability(107574), Ctx(false, 300), new PolicyOptions { Preset = RotationPreset.Burst }));
        Assert.Equal(PolicyVerdict.Hold, d.Verdict);
        Assert.Contains("burst preset", d.Reason);
    }

    private static AbilityDefinition Synthetic(UnknownPolicy unknown) =>
        new(
            SpellId: 900777,
            Name: "Synthetic Major",
            Category: AbilityCategory.Offensive,
            Purpose: AbilityPurpose.MajorOffensive,
            Tier: DefensiveTier.None,
            Gcd: GcdKind.OnGcd,
            Range: RangeKind.Melee,
            Cast: CastKind.Instant,
            CooldownMs: 120_000,
            DurationMs: 0,
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
            Unknown: unknown,
            Classes: [],
            Specs: [],
            Note: null,
            Source: null)
        {
            OffensiveUsage = OffensiveUsage.MajorBurst,
        };
}
