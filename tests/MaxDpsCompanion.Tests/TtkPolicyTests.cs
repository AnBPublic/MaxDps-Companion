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
        bool executeFavored = false,
        bool killSecure = false) =>
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
            KillSecure = killSecure,
        };

    private static CombatContext Combat(
        bool ttkValid,
        double ttkSec,
        bool targetHpValid = false,
        int targetHpPct = 0,
        bool provisional = false,
        double targetHpFrac = 0,
        double ageSec = 0,
        bool latch = false,
        bool histBinding = false,
        double histSec = 0) =>
        new()
        {
            TtkValid = ttkValid,
            TtkSec = ttkSec,
            TargetHpValid = targetHpValid,
            TargetHpPct = targetHpPct,
            TtkProvisional = provisional,
            TargetHpFrac = targetHpFrac,
            TargetAgeSec = ageSec,
            FastPackLatch = latch,
            TtkHistBinding = histBinding,
            TtkHistSec = histSec,
        };

    // ----- A2: schema parse -----

    [Fact]
    public void ParseTtk_Present_Reads_All_Four_Fields()
    {
        var o = new AbilityCatalog.AbilityOverride
        {
            MinTtkSec = 15.5,
            ExecuteBelowPct = 20,
            ExecuteFavored = true,
            KillSecure = true,
        };
        var parsed = AbilityCatalog.ParseTtkCuration(o);
        Assert.Equal(15.5, parsed.MinTtkSec);
        Assert.Equal(20, parsed.ExecuteBelowPct);
        Assert.True(parsed.ExecuteFavored);
        Assert.True(parsed.KillSecure);
    }

    [Fact]
    public void ParseTtk_Absent_Yields_Defaults()
    {
        var none = AbilityCatalog.ParseTtkCuration(null);
        Assert.Null(none.MinTtkSec);
        Assert.Null(none.ExecuteBelowPct);
        Assert.False(none.ExecuteFavored);
        Assert.False(none.KillSecure);

        var empty = AbilityCatalog.ParseTtkCuration(new AbilityCatalog.AbilityOverride());
        Assert.Null(empty.MinTtkSec);
        Assert.Null(empty.ExecuteBelowPct);
        Assert.False(empty.ExecuteFavored);
        Assert.False(empty.KillSecure);
    }

    [Theory]
    [InlineData((int)OffensiveUsage.MajorBurst, 15.0)]
    [InlineData((int)OffensiveUsage.Transformation, 20.0)]
    [InlineData((int)OffensiveUsage.Summon, 20.0)]
    [InlineData((int)OffensiveUsage.WindowDriven, 10.0)]
    [InlineData((int)OffensiveUsage.ShortCooldown, 5.0)]
    [InlineData((int)OffensiveUsage.ProcDriven, 5.0)]
    [InlineData((int)OffensiveUsage.AoeOnly, 0.0)]
    [InlineData((int)OffensiveUsage.SingleTargetOnly, 5.0)]
    [InlineData((int)OffensiveUsage.ResourceDriven, 5.0)]
    [InlineData((int)OffensiveUsage.Execute, 3.0)]
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
        var ability = Offensive(OffensiveUsage.MajorBurst); // default 15 s
        Assert.True(TtkPolicy.WasteGuardHolds(ability, Combat(ttkValid: true, ttkSec: 5)));
        Assert.False(TtkPolicy.WasteGuardHolds(ability, Combat(ttkValid: true, ttkSec: 20)));
        // Unknown TTK never holds (fail open).
        Assert.False(TtkPolicy.WasteGuardHolds(ability, Combat(ttkValid: false, ttkSec: 5)));
    }

    [Fact]
    public void Provisional_Waste_Guard_Holds_Majors_Only()
    {
        var major = Offensive(OffensiveUsage.MajorBurst);
        Assert.True(TtkPolicy.WasteGuardHolds(major, Combat(ttkValid: false, ttkSec: 5, provisional: true)));

        // A non-major is never held on a provisional rate, even far below its threshold.
        var minor = Offensive(OffensiveUsage.ShortCooldown);
        Assert.Equal(5.0, TtkPolicy.MinTtkSec(minor));
        Assert.False(TtkPolicy.WasteGuardHolds(minor, Combat(ttkValid: false, ttkSec: 1, provisional: true)));
        Assert.False(TtkPolicy.WasteGuardHolds(minor, Combat(ttkValid: false, ttkSec: 1, provisional: false)));
    }

    [Fact]
    public void Grace_Hold_Needs_Latch_And_Young_Target()
    {
        var major = Offensive(OffensiveUsage.MajorBurst);

        Assert.True(TtkPolicy.GraceHoldHolds(major,
            Combat(ttkValid: false, ttkSec: 300, latch: true, ageSec: 3)));
        // No latch -> documented fail-open, no hold.
        Assert.False(TtkPolicy.GraceHoldHolds(major,
            Combat(ttkValid: false, ttkSec: 300, latch: false, ageSec: 3)));
        // The grace window closes at 4 s.
        Assert.False(TtkPolicy.GraceHoldHolds(major,
            Combat(ttkValid: false, ttkSec: 300, latch: true, ageSec: 5)));
        // A provisional estimate is "known"; the grace hold must not fire.
        Assert.False(TtkPolicy.GraceHoldHolds(major,
            Combat(ttkValid: false, ttkSec: 300, provisional: true, latch: true, ageSec: 1)));
        // ConserveMajors applies the hold to any unknown-TTK major without a latch.
        Assert.True(TtkPolicy.GraceHoldHolds(major,
            Combat(ttkValid: false, ttkSec: 300, latch: false, ageSec: 9), TtkFallback.ConserveMajors));
        // Never a minor.
        Assert.False(TtkPolicy.GraceHoldHolds(Offensive(OffensiveUsage.ShortCooldown),
            Combat(ttkValid: false, ttkSec: 300, latch: true, ageSec: 1)));
    }

    [Fact]
    public void Kill_Secure_Bypasses_The_Waste_Guard_When_Dying_And_Old()
    {
        var major = Offensive(OffensiveUsage.MajorBurst);
        var dyingBoss = Combat(ttkValid: true, ttkSec: 8, targetHpFrac: 0.3, ageSec: 25);

        Assert.True(TtkPolicy.KillSecureBypass(major, dyingBoss));
        Assert.False(TtkPolicy.WasteGuardHolds(major, dyingBoss));

        // Same short TTK but a young target: not the long-fight-ending case.
        var young = Combat(ttkValid: true, ttkSec: 8, targetHpFrac: 0.3, ageSec: 5);
        Assert.False(TtkPolicy.KillSecureBypass(major, young));
        Assert.True(TtkPolicy.WasteGuardHolds(major, young));

        // Curated killSecure:true promotes a non-major usage.
        var curated = Offensive(OffensiveUsage.ShortCooldown, killSecure: true);
        Assert.True(TtkPolicy.KillSecureBypass(curated, dyingBoss));
    }

    [Fact]
    public void Execute_CarveOut_Bypasses_Above_Three_Seconds()
    {
        var execute = Offensive(executeBelowPct: 30, executeFavored: true);
        var inRangeShortTtk = Combat(ttkValid: true, ttkSec: 2, targetHpValid: true, targetHpPct: 30);

        // The execute-range rule fires regardless of the (short) estimate...
        Assert.True(TtkPolicy.ExecuteRange(execute, inRangeShortTtk));
        // ...but the waste-guard carve-out needs TTK >= 3 s, so at 2 s it still holds.
        Assert.False(TtkPolicy.ExecuteWasteBypass(execute, inRangeShortTtk));
        Assert.True(TtkPolicy.WasteGuardHolds(execute, inRangeShortTtk));

        // Unknown TTK fires (documented fail-open).
        var unknown = Combat(ttkValid: false, ttkSec: 300, targetHpValid: true, targetHpPct: 30);
        Assert.True(TtkPolicy.ExecuteWasteBypass(execute, unknown));
        Assert.False(TtkPolicy.WasteGuardHolds(execute, unknown));

        // At exactly 3 s the carve-out fires.
        Assert.True(TtkPolicy.ExecuteWasteBypass(execute,
            Combat(ttkValid: true, ttkSec: 3, targetHpValid: true, targetHpPct: 30)));
    }

    [Fact]
    public void AoeOnly_And_Hybrid_Are_Never_Gated()
    {
        Assert.Equal(0.0, TtkPolicy.DefaultMinTtkSec(OffensiveUsage.AoeOnly));
        Assert.Equal(0.0, TtkPolicy.DefaultMinTtkSec(OffensiveUsage.DefensiveOffensiveHybrid));
        Assert.False(TtkPolicy.WasteGuardHolds(
            Offensive(OffensiveUsage.AoeOnly), Combat(ttkValid: true, ttkSec: 1)));
        Assert.False(TtkPolicy.WasteGuardHolds(
            Offensive(OffensiveUsage.DefensiveOffensiveHybrid), Combat(ttkValid: true, ttkSec: 1)));
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

    private static AbilityDefinition Defensive(DefensiveTier tier = DefensiveTier.Major, int? useBelowHp = null) =>
        new(
            SpellId: 900002,
            Name: "Test Wall",
            Category: AbilityCategory.Defensive,
            Purpose: tier == DefensiveTier.Immunity ? AbilityPurpose.Immunity
                : tier == DefensiveTier.Minor ? AbilityPurpose.DefensiveMinor
                : AbilityPurpose.DefensiveMajor,
            Tier: tier,
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

    private static CombatContext DefCombat(
        bool ttkValid,
        double ttkSec,
        bool hpValid = false,
        int hp = 0,
        DefensiveUrgency urgency = DefensiveUrgency.Red,
        bool latch = false) =>
        new()
        {
            TtkValid = ttkValid,
            TtkSec = ttkSec,
            HpValid = hpValid,
            HpPct = hp,
            HpPctUpper = hp,
            DefensiveUrgency = urgency,
            FastPackLatch = latch,
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

    // ----- v3.6 per-tier / scope defensive table + carve-outs -----

    [Fact]
    public void Solo_Defensive_Tier_Table_Uses_Per_Tier_Windows()
    {
        // Minor: held below 6 s.
        Assert.True(TtkPolicy.DyingTargetHolds(Defensive(DefensiveTier.Minor), DefCombat(true, 5), true, false));
        Assert.False(TtkPolicy.DyingTargetHolds(Defensive(DefensiveTier.Minor), DefCombat(true, 7), true, false));
        // Major: held below 10 s.
        Assert.True(TtkPolicy.DyingTargetHolds(Defensive(DefensiveTier.Major), DefCombat(true, 9), true, false));
        Assert.False(TtkPolicy.DyingTargetHolds(Defensive(DefensiveTier.Major), DefCombat(true, 11), true, false));
        // Immunity: held below 15 s.
        Assert.True(TtkPolicy.DyingTargetHolds(Defensive(DefensiveTier.Immunity), DefCombat(true, 14), true, false));
        Assert.False(TtkPolicy.DyingTargetHolds(Defensive(DefensiveTier.Immunity), DefCombat(true, 16), true, false));
    }

    [Fact]
    public void Group_Defensive_Gates_Minor_Only_And_Needs_The_Latch()
    {
        var minor = Defensive(DefensiveTier.Minor);
        // Valid, nearly dead, latched, urgency below Orange -> hold.
        Assert.True(TtkPolicy.DyingTargetHolds(minor,
            DefCombat(true, 3, urgency: DefensiveUrgency.Yellow, latch: true), false, false));
        // No latch -> never held.
        Assert.False(TtkPolicy.DyingTargetHolds(minor,
            DefCombat(true, 3, urgency: DefensiveUrgency.Yellow, latch: false), false, false));
        // Orange or worse -> never held (the tank may be dying to other mobs).
        Assert.False(TtkPolicy.DyingTargetHolds(minor,
            DefCombat(true, 3, urgency: DefensiveUrgency.Orange, latch: true), false, false));
        // No valid rate -> never held.
        Assert.False(TtkPolicy.DyingTargetHolds(minor,
            DefCombat(false, 3, urgency: DefensiveUrgency.Yellow, latch: true), false, false));
        // Majors and immunities are never group-gated.
        Assert.False(TtkPolicy.DyingTargetHolds(Defensive(DefensiveTier.Major),
            DefCombat(true, 1, urgency: DefensiveUrgency.Yellow, latch: true), false, false));
        Assert.False(TtkPolicy.DyingTargetHolds(Defensive(DefensiveTier.Immunity),
            DefCombat(true, 1, urgency: DefensiveUrgency.Yellow, latch: true), false, false));
    }

    [Fact]
    public void Solo_Ladder_Band_Carve_Out_Never_Conserves_Needed_Mitigation()
    {
        // Major inside its HP band (<=50%) is needed now.
        Assert.False(TtkPolicy.DyingTargetHolds(Defensive(DefensiveTier.Major),
            DefCombat(true, 8, hpValid: true, hp: 40), true, false));
        // Immunity inside its band (<=30%).
        Assert.False(TtkPolicy.DyingTargetHolds(Defensive(DefensiveTier.Immunity),
            DefCombat(true, 12, hpValid: true, hp: 20), true, false));
        // Above the band the hold still applies.
        Assert.True(TtkPolicy.DyingTargetHolds(Defensive(DefensiveTier.Major),
            DefCombat(true, 8, hpValid: true, hp: 80), true, false));
    }

    // ----- v3.8 warmup hold + buff-aware need -----

    [Fact]
    public void BuffNeed_Uses_Half_Of_The_Buff_Duration_Capped_At_Twenty()
    {
        // MajorBurst base need is 15 s; a 20 s buff needs max(15, 10) = 15 s.
        var dur20 = Offensive(durationMs: 20_000);
        // A 40 s buff halves to 20 s, which is also the hard cap.
        var dur40 = Offensive(durationMs: 40_000);
        // A zero duration falls back to the base need.
        var dur0 = Offensive(durationMs: 0);

        Assert.Equal(15.0, TtkPolicy.BuffNeed(dur20, 0.5), 6);
        Assert.Equal(20.0, TtkPolicy.BuffNeed(dur40, 0.5), 6);
        Assert.Equal(15.0, TtkPolicy.BuffNeed(dur0, 0.5), 6);
        Assert.Equal(TtkPolicy.MinTtkSec(dur0), TtkPolicy.BuffNeed(dur0, 0.5), 6);
    }

    [Fact]
    public void Warmup_Holds_A_Young_Unknown_Major_Until_Estimable()
    {
        var major = Offensive(OffensiveUsage.MajorBurst);

        // 2 s trash, seen 1 s, unknown: hold (warming up TTK).
        Assert.True(TtkPolicy.WarmupHoldHolds(major, Combat(ttkValid: false, ttkSec: 300, ageSec: 1), 3.0, 0.5));
        // The window closes at 3 s.
        Assert.False(TtkPolicy.WarmupHoldHolds(major, Combat(ttkValid: false, ttkSec: 300, ageSec: 3.5), 3.0, 0.5));
        // A valid estimate releases it (20 s boss with 30 s TTK).
        Assert.False(TtkPolicy.WarmupHoldHolds(major, Combat(ttkValid: true, ttkSec: 30, ageSec: 1), 3.0, 0.5));
        // A provisional estimate is "known" and releases it too.
        Assert.False(TtkPolicy.WarmupHoldHolds(major, Combat(ttkValid: false, ttkSec: 300, ageSec: 1, provisional: true), 3.0, 0.5));
        // WarmupSec = 0 is the legacy fail-open.
        Assert.False(TtkPolicy.WarmupHoldHolds(major, Combat(ttkValid: false, ttkSec: 300, ageSec: 1), 0, 0.5));
    }

    [Fact]
    public void Warmup_Never_Holds_Execute_Aoe_Minors_Or_KillSecure()
    {
        var young = Combat(ttkValid: false, ttkSec: 300, ageSec: 1);
        Assert.False(TtkPolicy.WarmupHoldHolds(Offensive(OffensiveUsage.Execute), young, 3.0, 0.5));
        Assert.False(TtkPolicy.WarmupHoldHolds(Offensive(OffensiveUsage.AoeOnly), young, 3.0, 0.5));
        Assert.False(TtkPolicy.WarmupHoldHolds(Offensive(OffensiveUsage.ShortCooldown), young, 3.0, 0.5));
        Assert.False(TtkPolicy.WarmupHoldHolds(Offensive(killSecure: true), young, 3.0, 0.5));
        Assert.False(TtkPolicy.WarmupHoldHolds(Offensive(minTtkSec: 0), young, 3.0, 0.5));
    }

    [Fact]
    public void Warmup_Releases_When_A_Binding_History_Already_Covers_The_Buff()
    {
        var major = Offensive(OffensiveUsage.MajorBurst, durationMs: 20_000); // BuffNeed 15 s
        // History predicts a 20 s fight -> >= BuffNeed, no warmup hold.
        Assert.False(TtkPolicy.WarmupHoldHolds(major,
            Combat(ttkValid: false, ttkSec: 300, ageSec: 1, histBinding: true, histSec: 20), 3.0, 0.5));
        // History predicts only 5 s -> still holds (unknown live, short history).
        Assert.True(TtkPolicy.WarmupHoldHolds(major,
            Combat(ttkValid: false, ttkSec: 300, ageSec: 1, histBinding: true, histSec: 5), 3.0, 0.5));
    }

    [Fact]
    public void Warmup_Provider_Wiring_Holds_A_Fresh_Trash_Major_And_Uses_A_Boss()
    {
        var ability = WithGroup(Offensive(OffensiveUsage.MajorBurst), "Burst");
        var options = new PolicyOptions { TtkWarmupSec = 3.0 };

        var held = CandidateProviders.Offensive.Evaluate(new ProviderInput
        {
            Input = new PolicyInput
            {
                Slot = Slot.Offensive,
                SpellId = ability.SpellId,
                Context = Combat(ttkValid: false, ttkSec: 300, ageSec: 1),
                Options = options,
                Memory = new PolicyMemory(),
                NowMs = 1000,
                InCombat = true,
                HasTarget = true,
            },
            Ability = ability,
            Catalog = Catalog,
            Range = TriState.Yes,
        });
        Assert.Equal(PolicyVerdict.Hold, held.Verdict);
        Assert.Equal(TtkPolicy.WarmupHoldReason, held.Reason);

        // A valid long boss estimate falls through to Use.
        var fired = CandidateProviders.Offensive.Evaluate(new ProviderInput
        {
            Input = new PolicyInput
            {
                Slot = Slot.Offensive,
                SpellId = ability.SpellId,
                Context = Combat(ttkValid: true, ttkSec: 30, ageSec: 1),
                Options = options,
                Memory = new PolicyMemory(),
                NowMs = 1000,
                InCombat = true,
                HasTarget = true,
            },
            Ability = ability,
            Catalog = Catalog,
            Range = TriState.Yes,
        });
        Assert.Equal(PolicyVerdict.Use, fired.Verdict);
    }

    // ----- [TimeToKill] Fallback end-to-end wiring (spec §4) -----

    [Fact]
    public void Fallback_Threads_From_Settings_Through_PolicyOptions()
    {
        var settings = new AppSettings { TimeToKillFallback = TtkFallback.ConserveMajors };
        Assert.Equal(TtkFallback.ConserveMajors, PolicyOptions.FromSettings(settings).TimeToKillFallback);
        // Default stays the documented fail-open.
        Assert.Equal(TtkFallback.FailOpen, PolicyOptions.Standard.TimeToKillFallback);
        Assert.Equal(TtkFallback.FailOpen, new AppSettings().TimeToKillFallback);
    }

    /// <summary>Unknown-TTK, no latch, in-range offensive context (band 0, AgeSec 9).</summary>
    private static CombatContext UnknownTtkContext()
    {
        var range = new TriState[PixelProtocol.SlotCount];
        range[(int)Slot.Offensive] = TriState.Yes;
        return new CombatContext
        {
            TtkValid = false,
            TtkSec = 300,
            TargetAgeSec = 9,
            FastPackLatch = false,
            SlotRange = range,
        };
    }

    /// <summary>Avatar (107574) is a real Warrior Fury MajorBurst row in the pinned catalog.</summary>
    private static PolicyDecision EvaluateAvatarThroughEvaluator(TtkFallback fallback) =>
        PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = Slot.Offensive,
            SpellId = 107574,
            Context = UnknownTtkContext(),
            Options = new PolicyOptions { TimeToKillFallback = fallback },
            Memory = new PolicyMemory(),
            NowMs = 1000,
            InCombat = true,
            HasTarget = true,
        }, Catalog);

    [Fact]
    public void ConserveMajors_Holds_Unknown_Ttk_Major_EndToEnd_While_FailOpen_Does_Not()
    {
        // The evaluator must hand the setting to ProviderInput.Fallback; with the
        // default FailOpen this unknown-TTK major keeps its pre-TTK verdict...
        var failOpen = EvaluateAvatarThroughEvaluator(TtkFallback.FailOpen);
        Assert.Equal(PolicyVerdict.Use, failOpen.Verdict);

        // ...while ConserveMajors applies the grace hold to any unknown major
        // with no latch needed.
        var conserve = EvaluateAvatarThroughEvaluator(TtkFallback.ConserveMajors);
        Assert.Equal(PolicyVerdict.Hold, conserve.Verdict);
        Assert.Equal(TtkPolicy.GraceHoldReason, conserve.Reason);
    }
}
