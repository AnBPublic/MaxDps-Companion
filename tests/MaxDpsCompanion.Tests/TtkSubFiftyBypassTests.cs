using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 2026-10-04 sub-50s TTK bypass (<c>docs/plans/2026-10-04-sub50s-ttk-bypass.md</c>),
/// widened 2026-10-06 to <c>&lt;= 60 s</c> for Divine Toll
/// (<c>docs/plans/2026-10-06-divine-toll.md</c>): a companion-gated ability
/// whose curated cooldown is 60 s or shorter is a rotational button, never a
/// major, so the Burst preset must not conserve it. Covers the warrior
/// <c>cdMs=45000</c> backfill (Colossus Smash 167105, Warbreaker 262161,
/// Demolish 436358, Odyn's Fury 385059, Shield Charge 385952, Demoralizing
/// Shout 1160), a non-warrior sub-50s row (Essence Break 258860, 40 s), the
/// 60 s boundary (Divine Toll 375576) and the fail-closed cases: 60 001 ms /
/// 90 s majors stay non-bypassed, <c>CooldownMs == 0</c> is never bypassed, and
/// a 60 s <see cref="OffensiveUsage.Summon"/> (Summon Demonic Tyrant 265187) is
/// explicitly opted out. Since 3.7.9 the remaining TTK conservation guards are
/// gone, so the provider-level tests assert every row fires.
/// </summary>
public class TtkSubFiftyBypassTests
{
    private static AbilityCatalog Catalog => AbilityCatalog.Default;

    private static AbilityDefinition Ability(int id)
    {
        var ability = Catalog.TryGet(id);
        Assert.NotNull(ability);
        return ability!;
    }

    /// <summary>Young target + short binding trash history + fast-pack latch.</summary>
    private static CombatContext Ctx(
        bool ttkValid = false,
        double ttkSec = 300,
        double ageSec = 2,
        double histTtkSec = 6.0,
        bool histBinding = true,
        bool latch = true,
        double targetHpFrac = 0.5) =>
        new()
        {
            TtkValid = ttkValid,
            TtkSec = ttkSec,
            TargetAgeSec = ageSec,
            FastPackLatch = latch,
            TargetHpValid = true,
            TargetHpFrac = targetHpFrac,
            TargetHpPct = (int)(targetHpFrac * 100),
            TtkHistBinding = histBinding,
            TtkHistRate = 0.1,
            TtkHistSec = histTtkSec,
            TtkHistProvisional = histBinding && !ttkValid,
            TtkHistKills = 6,
            TtkHistDurFactor = 0.5,
        };

    private static AbilityDefinition Synthesized(int cooldownMs) =>
        new(
            SpellId: 990001,
            Name: "Synthetic Burst",
            Category: AbilityCategory.Offensive,
            Purpose: AbilityPurpose.MajorOffensive,
            Tier: DefensiveTier.None,
            Gcd: GcdKind.OnGcd,
            Range: RangeKind.Melee,
            Cast: CastKind.Instant,
            CooldownMs: cooldownMs,
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
            Unknown: UnknownPolicy.Use,
            Classes: [],
            Specs: [],
            Note: null,
            Source: null)
        {
            OffensiveUsage = OffensiveUsage.MajorBurst,
        };

    private static ProviderInput OffensiveInput(
        AbilityDefinition ability, CombatContext ctx, PolicyOptions options) =>
        new()
        {
            Input = new PolicyInput
            {
                Slot = Slot.Offensive,
                SpellId = ability.SpellId,
                Context = ctx,
                Options = options,
                Memory = new PolicyMemory(),
                NowMs = 1000,
                InCombat = true,
                HasTarget = true,
            },
            Ability = ability,
            Catalog = Catalog,
            Range = TriState.Yes,
        };

    // ----- predicate contract ------------------------------------------------

    [Fact]
    public void SubFiftyBypass_True_For_Sub50_False_For_90s_And_Zero()
    {
        // Warrior <50s backfill.
        foreach (var id in new[] { 167105, 262161, 436358, 385059, 385952, 1160 })
        {
            var a = Ability(id);
            Assert.Equal(45_000, a.CooldownMs);
            Assert.True(TtkPolicy.SubFiftyBypass(a));
        }

        // A curated non-warrior <50s row.
        Assert.Equal(40_000, Ability(258860).CooldownMs);
        Assert.True(TtkPolicy.SubFiftyBypass(Ability(258860)));

        // 60s boundary: the curated Divine Toll real row (60000 ms) bypasses.
        var divineToll = Ability(375576);
        Assert.Equal(60_000, divineToll.CooldownMs);
        Assert.True(TtkPolicy.SubFiftyBypass(divineToll));

        // Just above the boundary stays gated (fail-closed).
        Assert.False(TtkPolicy.SubFiftyBypass(Synthesized(60_001)));
        Assert.False(TtkPolicy.SubFiftyBypass(Synthesized(90_000)));

        // 90s majors stay gated.
        Assert.False(TtkPolicy.SubFiftyBypass(Ability(1719)));
        Assert.False(TtkPolicy.SubFiftyBypass(Ability(107574)));

        // Unknown cooldown is never bypassed (fail-closed).
        Assert.False(TtkPolicy.SubFiftyBypass(Synthesized(0)));
    }

    // ----- 60s Summon opt-out: a true 60s summon stays TTK-gated ------------

    [Fact]
    public void SubFiftyBypass_False_For_60s_Summon_OptOut()
    {
        var tyrant = Ability(265187);
        Assert.Equal(60_000, tyrant.CooldownMs);
        Assert.Equal(OffensiveUsage.Summon, tyrant.OffensiveUsage);
        Assert.False(TtkPolicy.SubFiftyBypass(tyrant));
    }

    // ----- Divine Toll 375576: the Burst preset still short-circuits ---------

    [Fact]
    public void DivineToll_60s_ShortCooldown_Not_Held_By_The_Burst_Preset()
    {
        var dt = Ability(375576);
        Assert.Equal(AbilityCategory.Offensive, dt.Category);
        Assert.Equal(OffensiveUsage.ShortCooldown, dt.OffensiveUsage);
        Assert.True(TtkPolicy.SubFiftyBypass(dt));

        // Burst preset: an invalid boss TTK would hold a major; the 60 s button
        // still fires because SubFiftyBypass short-circuits the preset.
        var burst = new PolicyOptions { Preset = RotationPreset.Burst };
        var ctx = Ctx(ttkValid: false, ageSec: 60, histBinding: false, latch: false);
        var verdict = CandidateProviders.Offensive.Evaluate(OffensiveInput(dt, ctx, burst));
        Assert.NotEqual(PolicyVerdict.Hold, verdict.Verdict);
        Assert.DoesNotContain("burst preset", verdict.Reason);
    }

    // ----- Divine Toll: an unknown range probe must not hold the burst ------

    [Fact]
    public void DivineToll_UnknownRange_Is_Not_Held_Or_Uncertain()
    {
        var dt = Ability(375576);

        // The curated row inherits the Offensive default (Use): an unknown
        // range probe must not turn a flagged short-CD burst into an Uncertain
        // hold that empties the whole plan.
        Assert.Equal(UnknownPolicy.Use, dt.Unknown);

        var input = new ProviderInput
        {
            Input = new PolicyInput
            {
                Slot = Slot.Offensive,
                SpellId = dt.SpellId,
                Context = Ctx(ttkValid: false, ageSec: 60, histBinding: false, latch: false),
                Options = new PolicyOptions(),
                Memory = new PolicyMemory(),
                NowMs = 1000,
                InCombat = true,
                HasTarget = true,
            },
            Ability = dt,
            Catalog = Catalog,
            Range = TriState.Unknown,
        };

        var verdict = CandidateProviders.Offensive.Evaluate(input);
        Assert.NotEqual(PolicyVerdict.Hold, verdict.Verdict);
        // PolicyDecision.Uncertain() renders as PolicyVerdict.Unknown.
        Assert.NotEqual(PolicyVerdict.Unknown, verdict.Verdict);
        Assert.DoesNotContain("range unknown", verdict.Evidence);
    }

    // ----- Warbreaker 262161: a sub-50 s rotational button is never conserved --

    [Fact]
    public void Warbreaker_Sub50_Is_Not_Conserved()
    {
        var war = Ability(262161);
        Assert.True(TtkPolicy.SubFiftyBypass(war));
        Assert.True(TtkPolicy.SubFiftyBypass(Ability(262161)));

        // A valid-but-short live TTK no longer holds the rotational button.
        var shortLive = CandidateProviders.Offensive.Evaluate(
            OffensiveInput(war, Ctx(ttkValid: true, ttkSec: 2), new PolicyOptions()));
        Assert.Equal(PolicyVerdict.Use, shortLive.Verdict);

        // Young trash with no estimate is also a Use.
        var youngTrash = CandidateProviders.Offensive.Evaluate(
            OffensiveInput(war, Ctx(ttkValid: false, ageSec: 2), new PolicyOptions()));
        Assert.Equal(PolicyVerdict.Use, youngTrash.Verdict);
    }

    // ----- non-warrior sub-50s row: Essence Break 258860 ---------------------

    [Fact]
    public void Non_Warrior_Sub50_EssenceBreak_Is_Not_Conserved()
    {
        var eb = Ability(258860);
        Assert.Equal(AbilityCategory.Offensive, eb.Category);
        Assert.True(TtkPolicy.SubFiftyBypass(eb));

        var shortLive = CandidateProviders.Offensive.Evaluate(
            OffensiveInput(eb, Ctx(ttkValid: true, ttkSec: 2), new PolicyOptions()));
        Assert.Equal(PolicyVerdict.Use, shortLive.Verdict);
    }

    // ----- burst preset: bypasses sub-50s, still holds 90s ------------------

    [Fact]
    public void Burst_Preset_Does_Not_Hold_Sub50_But_Holds_90s_Majors()
    {
        var burst = new PolicyOptions { Preset = RotationPreset.Burst };
        // Invalid TTK, aged target, no history/latch: only the preset would hold.
        var ctx = Ctx(ttkValid: false, ageSec: 60, histBinding: false, latch: false);

        var war = CandidateProviders.Offensive.Evaluate(OffensiveInput(Ability(262161), ctx, burst));
        Assert.NotEqual(PolicyVerdict.Hold, war.Verdict);
        Assert.DoesNotContain("burst preset", war.Reason);

        var eb = CandidateProviders.Offensive.Evaluate(OffensiveInput(Ability(258860), ctx, burst));
        Assert.NotEqual(PolicyVerdict.Hold, eb.Verdict);
        Assert.DoesNotContain("burst preset", eb.Reason);

        var avatar = CandidateProviders.Offensive.Evaluate(OffensiveInput(Ability(107574), ctx, burst));
        Assert.Equal(PolicyVerdict.Hold, avatar.Verdict);
        Assert.Contains("burst preset", avatar.Reason);
    }

    // ----- 90s majors are used on a short live TTK (no conservation) --------

    [Fact]
    public void Ninety_Second_Majors_Are_Used_On_A_Short_Live_Ttk()
    {
        foreach (var id in new[] { 1719, 107574 })
        {
            var major = Ability(id);
            Assert.False(TtkPolicy.SubFiftyBypass(major));
            var ctx = Ctx(ttkValid: true, ttkSec: 5, ageSec: 10);
            var d = CandidateProviders.Offensive.Evaluate(OffensiveInput(major, ctx, new PolicyOptions()));
            Assert.Equal(PolicyVerdict.Use, d.Verdict);
        }
    }

    // ----- CooldownMs == 0 is no longer held either -------------------------

    [Fact]
    public void Zero_Cooldown_Major_Is_Used()
    {
        var zero = Synthesized(0);
        Assert.False(TtkPolicy.SubFiftyBypass(zero));

        var d = CandidateProviders.Offensive.Evaluate(
            OffensiveInput(zero, Ctx(ttkValid: true, ttkSec: 2), new PolicyOptions()));
        Assert.Equal(PolicyVerdict.Use, d.Verdict);
    }
}
