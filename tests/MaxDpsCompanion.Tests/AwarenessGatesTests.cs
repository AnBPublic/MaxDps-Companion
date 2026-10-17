using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// AWARENESS stream: the catalog-driven melee-position gate and the
/// cast-kind-aware execution-safety hold. Both live in
/// <see cref="ExecutionSafety"/> / <see cref="PolicyEvaluator.Known"/> and
/// read only the existing wire observables (<see cref="CombatContext.Cast"/>,
/// <see cref="CombatContext.TargetInMelee"/>, <see cref="CombatContext.SlotRange"/>).
///
/// The synthetic catalog makes the matrix deterministic: every branch keys on
/// <see cref="AbilityDefinition.Range"/> / <see cref="AbilityDefinition.TargetRange"/> /
/// <see cref="AbilityDefinition.Cast"/> / <see cref="AbilityDefinition.Gcd"/>
/// (no per-class or per-spell list), so every class/talent entry inherits it.
/// </summary>
public class AwarenessGatesTests
{
    private const long Now = 10_000;

    private const string Vendor = """{ "gamePatch": "12.1", "entries": [] }""";

    private const string CuratedJson = """
        { "gamePatch": "12.1", "interface": 120100, "verified": "2026-09-29",
          "abilities": [
            {"id":920001,"name":"Melee Strike","category":"Offensive","purpose":"MajorOffensive","range":"Melee","gcd":"OnGcd","cast":"Instant","status":"ResearchBacked"},
            {"id":920002,"name":"Ranged Bolt","category":"Offensive","purpose":"MajorOffensive","range":"Ranged","gcd":"OnGcd","cast":"Instant","status":"ResearchBacked"},
            {"id":920003,"name":"Gap Closer","category":"Mobility","purpose":"GapCloser","range":"Melee","targetRange":"OutOfMelee","gcd":"OffGcd","cast":"Instant","status":"ResearchBacked"},
            {"id":920004,"name":"Instant OffGcd","category":"Defensive","purpose":"DefensiveMajor","tier":"Major","range":"SelfOnly","gcd":"OffGcd","cast":"Instant","status":"ResearchBacked"},
            {"id":920005,"name":"Instant OnGcd","category":"Offensive","purpose":"MajorOffensive","range":"Ranged","gcd":"OnGcd","cast":"Instant","status":"ResearchBacked"},
            {"id":920006,"name":"Hard Cast","category":"Offensive","purpose":"MajorOffensive","range":"Ranged","gcd":"OnGcd","cast":"CastTime","status":"ResearchBacked"},
            {"id":920007,"name":"Channel","category":"Offensive","purpose":"MajorOffensive","range":"Ranged","gcd":"OnGcd","cast":"Channel","status":"ResearchBacked"},
            {"id":920008,"name":"Hard Cast OffGcd","category":"Offensive","purpose":"MajorOffensive","range":"Ranged","gcd":"OffGcd","cast":"CastTime","status":"ResearchBacked"},
            {"id":920009,"name":"Melee RangeRequirement","category":"Offensive","purpose":"MajorOffensive","range":"Ranged","targetRange":"InMelee","gcd":"OnGcd","cast":"Instant","status":"ResearchBacked"}
          ] }
        """;

    private static AbilityCatalog Catalog { get; } = AbilityCatalog.Load(Vendor, CuratedJson);

    // ---- fixtures ---------------------------------------------------------

    private static CombatContext Context(
        TriState targetInMelee = TriState.Unknown,
        PlayerCastState cast = PlayerCastState.None,
        TriState[]? range = null) => new()
    {
        HpValid = false,
        Cast = cast,
        TargetCasting = TriState.No,
        TargetCastInterruptible = TriState.Unknown,
        TargetInMelee = targetInMelee,
        SlotRange = range ?? AllRange(TriState.Yes),
        SlotBuffActive = new TriState[PixelProtocol.SlotCount],
        ContextValid = true,
    };

    private static TriState[] AllRange(TriState state)
    {
        var range = new TriState[PixelProtocol.SlotCount];
        for (var i = 0; i < range.Length; i++) range[i] = state;
        return range;
    }

    private static TriState[] Range(params (Slot Slot, TriState State)[] entries)
    {
        var range = new TriState[PixelProtocol.SlotCount];
        foreach (var (slot, state) in entries) range[(int)slot] = state;
        return range;
    }

    private static PolicyDecision Evaluate(Slot slot, int spellId, CombatContext ctx) =>
        PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = slot,
            SpellId = spellId,
            Context = ctx,
            Options = PolicyOptions.Standard,
            Memory = new PolicyMemory(),
            NowMs = Now,
            InCombat = true,
            HasTarget = true,
        }, Catalog);

    // ---- 1. melee-position gate (melee in / out / unknown) ----------------

    [Fact]
    public void Melee_Ability_Out_Of_Melee_Is_Unavailable()
    {
        var result = Evaluate(Slot.Offensive, 920001,
            Context(targetInMelee: TriState.No, range: Range((Slot.Offensive, TriState.Unknown))));
        Assert.Equal(PolicyVerdict.Unavailable, result.Verdict);
        Assert.Equal(ExecutionSafety.MeleeRangeReason, result.Reason);
        Assert.Contains(ExecutionSafety.MeleeRangeReason, result.Evidence);
        Assert.Equal("Offensive", result.Provider);
    }

    [Fact]
    public void TargetRange_InMelee_Out_Of_Melee_Is_Unavailable()
    {
        // The gate is driven by TargetRange too, not only RangeKind.
        var result = Evaluate(Slot.Offensive, 920009,
            Context(targetInMelee: TriState.No, range: Range((Slot.Offensive, TriState.No))));
        Assert.Equal(PolicyVerdict.Unavailable, result.Verdict);
        Assert.Equal(ExecutionSafety.MeleeRangeReason, result.Reason);
    }

    [Fact]
    public void Positive_Range_Probe_Wins_Over_The_Generic_Melee_Estimate()
    {
        // The bridge's IsSpellInRange for THIS slot says Yes: the spell is
        // confirmed in range, so the generic melee estimate must not override.
        var result = Evaluate(Slot.Offensive, 920001,
            Context(targetInMelee: TriState.No, range: Range((Slot.Offensive, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.DoesNotContain(ExecutionSafety.MeleeRangeReason, result.Reason);
    }

    [Fact]
    public void Melee_Ability_In_Melee_Fails_Open()
    {
        var result = Evaluate(Slot.Offensive, 920001,
            Context(targetInMelee: TriState.Yes, range: Range((Slot.Offensive, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.DoesNotContain(ExecutionSafety.MeleeRangeReason, result.Reason);
    }

    [Fact]
    public void Melee_Ability_Unknown_Melee_State_Fails_Open()
    {
        // Unknown must never invent a hold from a missing reading.
        var result = Evaluate(Slot.Offensive, 920001,
            Context(targetInMelee: TriState.Unknown, range: Range((Slot.Offensive, TriState.Unknown))));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.DoesNotContain(ExecutionSafety.MeleeRangeReason, result.Reason);
    }

    [Fact]
    public void Ranged_Ability_Ignores_Melee_State()
    {
        // Same context as the failing melee case: only RangeKind separates them.
        var result = Evaluate(Slot.Offensive, 920002,
            Context(targetInMelee: TriState.No, range: Range((Slot.Offensive, TriState.Unknown))));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.DoesNotContain(ExecutionSafety.MeleeRangeReason, result.Reason);
    }

    [Fact]
    public void Gap_Closer_Is_Not_Melee_Gated()
    {
        // A gap closer is *used* when the target is out of melee; the Mobility
        // provider owns the decision and must keep receiving it.
        var result = Evaluate(Slot.Mobility, 920003,
            Context(targetInMelee: TriState.No, range: Range((Slot.Mobility, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.DoesNotContain(ExecutionSafety.MeleeRangeReason, result.Reason);
    }

    // ---- 2. cast-kind-aware execution safety ------------------------------

    [Theory]
    [InlineData(2)] // Casting
    [InlineData(3)] // Channeling
    public void Instant_OffGcd_Verified_Is_Never_Held(int castCode)
    {
        Assert.Null(ExecutionSafety.CastHoldReason(Slot.Defensive, 920004, (PlayerCastState)castCode, Catalog));
    }

    [Theory]
    [InlineData(2, ExecutionSafety.CastReason)] // Casting
    [InlineData(3, ExecutionSafety.ChannelReason)] // Channeling
    public void Instant_OnGcd_Still_Holds_Because_It_Rides_The_Gcd(int castCode, string expected)
    {
        Assert.Equal(expected, ExecutionSafety.CastHoldReason(Slot.Offensive, 920005, (PlayerCastState)castCode, Catalog));
    }

    [Theory]
    [InlineData(2, ExecutionSafety.CastReason)] // Casting
    [InlineData(3, ExecutionSafety.ChannelReason)] // Channeling
    public void CastTime_Always_Holds(int castCode, string expected)
    {
        Assert.Equal(expected, ExecutionSafety.CastHoldReason(Slot.Offensive, 920006, (PlayerCastState)castCode, Catalog));
    }

    [Theory]
    [InlineData(2, ExecutionSafety.CastReason)] // Casting
    [InlineData(3, ExecutionSafety.ChannelReason)] // Channeling
    public void Channel_Always_Holds(int castCode, string expected)
    {
        Assert.Equal(expected, ExecutionSafety.CastHoldReason(Slot.Offensive, 920007, (PlayerCastState)castCode, Catalog));
    }

    [Fact]
    public void CastTime_Is_Not_Exempt_Even_When_OffGcd()
    {
        // You cannot start a cast/channel while one is already running.
        Assert.Equal(ExecutionSafety.ChannelReason,
            ExecutionSafety.CastHoldReason(Slot.Offensive, 920008, PlayerCastState.Channeling, Catalog));
    }

    [Fact]
    public void Gap_Closer_Is_Never_Exempt_From_The_Cast_Hold()
    {
        Assert.Equal(ExecutionSafety.CastReason,
            ExecutionSafety.CastHoldReason(Slot.Mobility, 920003, PlayerCastState.Casting, Catalog));
    }

    [Fact]
    public void Not_Casting_Yields_No_Hold()
    {
        Assert.Null(ExecutionSafety.CastHoldReason(Slot.Offensive, 920005, PlayerCastState.None, Catalog));
        Assert.Null(ExecutionSafety.CastHoldReason(Slot.Offensive, 920005, PlayerCastState.Unknown, Catalog));
    }

    [Fact]
    public void Interrupt_And_Item_Slots_Are_Exempt_By_Design()
    {
        Assert.Null(ExecutionSafety.CastHoldReason(Slot.Interrupt, 0, PlayerCastState.Channeling, Catalog));
        Assert.Null(ExecutionSafety.CastHoldReason(Slot.Consumable, 0, PlayerCastState.Casting, Catalog));
        Assert.Null(ExecutionSafety.CastHoldReason(Slot.Trinket, 0, PlayerCastState.Casting, Catalog));
    }

    [Fact]
    public void Unknown_Ability_Fails_Closed_To_The_Conservative_Hold()
    {
        // No catalog knowledge: preserve the pre-intelligence Generic behaviour.
        Assert.Equal(ExecutionSafety.CastReason,
            ExecutionSafety.CastHoldReason(Slot.Main, 999_999, PlayerCastState.Casting, Catalog));
    }

    // ---- 3. LoS is deliberately absent (documented, not implemented) ------

    [Fact]
    public void No_Line_Of_Sight_Gate_Is_Invented()
    {
        // Line of sight is not observable in Midnight, so no gate may exist
        // and the context must carry no LoS property.
        Assert.DoesNotContain(typeof(CombatContext).GetProperties(),
            p => p.Name.Contains("LineOfSight", StringComparison.OrdinalIgnoreCase));

        // A positional fail still reports only the observable reason.
        var result = Evaluate(Slot.Offensive, 920001,
            Context(targetInMelee: TriState.No, range: Range((Slot.Offensive, TriState.Unknown))));
        Assert.False(result.Reason.Contains("line of sight", StringComparison.OrdinalIgnoreCase), result.Reason);
        foreach (var fact in result.Evidence)
            Assert.False(fact.Contains("line of sight", StringComparison.OrdinalIgnoreCase), fact);
    }
}
