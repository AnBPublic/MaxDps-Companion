using System.Text;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// v3.7 adaptive-history decision layer: the pure <see cref="TtkPolicy.NeedAdaptive"/>
/// formula, the T1 <c>ttk-hist</c> branch (hold vs use, invalid-live restriction,
/// carve-outs) and the consumable/trinket Burst release. Also pins the
/// [TimeToKill] history setting parse/clamp and the replay hk/hs reproduction.
/// </summary>
public class TtkPolicyHistoryTests
{
    // ----- ability builders (mirrors TtkPolicyTests) -----

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

    private static AbilityDefinition Consumable() =>
        new(
            SpellId: 900003,
            Name: "Test Potion",
            Category: AbilityCategory.Consumable,
            Purpose: AbilityPurpose.Consumable,
            Tier: DefensiveTier.None,
            Gcd: GcdKind.OffGcd,
            Range: RangeKind.SelfOnly,
            Cast: CastKind.Instant,
            CooldownMs: 60_000,
            DurationMs: 0,
            HealPctMaxHp: 0,
            RequiresEnemyCast: false,
            TargetRange: RangeRequirement.Any,
            RequiresTarget: false,
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
            Source: null);

    /// <summary>A context with a binding history window (5 kills) and a chosen history TTK.</summary>
    private static CombatContext HistCombat(
        double histTtkSec,
        bool ttkValid = false,
        double ttkSec = 300,
        bool provisional = false,
        double ageSec = 3,
        bool latch = false,
        int kills = 5,
        bool targetHpValid = false,
        int targetHpPct = 0,
        double targetHpFrac = 0,
        bool binding = true) =>
        new()
        {
            TtkValid = ttkValid,
            TtkSec = ttkSec,
            TtkProvisional = provisional,
            TargetAgeSec = ageSec,
            FastPackLatch = latch,
            TargetHpValid = targetHpValid,
            TargetHpPct = targetHpPct,
            TargetHpFrac = targetHpFrac,
            TtkHistBinding = binding,
            TtkHistRate = 0.1,
            TtkHistSec = histTtkSec,
            TtkHistProvisional = binding && !ttkValid,
            TtkHistKills = kills,
            TtkHistDurFactor = 0.5,
        };

    // ----- NeedAdaptive -----

    [Fact]
    public void NeedAdaptive_Is_Max_Of_Base_And_Capped_Duration_Fraction()
    {
        // Spec examples.
        Assert.Equal(12.5, TtkPolicy.NeedAdaptive(10, 25, 0.5), 6);
        Assert.Equal(20.0, TtkPolicy.NeedAdaptive(10, 60, 0.5), 6);
        // The 20 s cap binds; the ability's own minimum wins when higher.
        Assert.Equal(10.0, TtkPolicy.NeedAdaptive(10, 20, 0.5), 6);
        Assert.Equal(15.0, TtkPolicy.NeedAdaptive(15, 20, 0.5), 6);
        // A zero factor disables the adaptive raise (keeps the base need).
        Assert.Equal(15.0, TtkPolicy.NeedAdaptive(15, 200, 0.0), 6);
    }

    // ----- guard: 20 s active duration, history 6 s vs 14 s -----

    [Fact]
    public void Guard_20s_Active_Duration_Holds_At_6_And_Uses_At_14()
    {
        // WindowDriven base need 10 s, so NeedAdaptive(10,20,0.5) = 10 s.
        var ability = Offensive(OffensiveUsage.WindowDriven);
        Assert.Equal(10.0, TtkPolicy.MinTtkSec(ability));

        Assert.True(TtkPolicy.WasteGuardHolds(ability, HistCombat(6), activeDurSec: 20, durFactor: 0.5));
        Assert.False(TtkPolicy.WasteGuardHolds(ability, HistCombat(14), activeDurSec: 20, durFactor: 0.5));
        Assert.False(TtkPolicy.WasteGuardHolds(ability, HistCombat(10), activeDurSec: 20, durFactor: 0.5));
    }

    // ----- carve-outs -----

    [Fact]
    public void Guard_Carve_Outs_Bypass_The_History_Branch()
    {
        // Execute-favored in execute range with TtkSec >= 3 bypasses the guard.
        var execute = Offensive(OffensiveUsage.WindowDriven, executeBelowPct: 30, executeFavored: true);
        var executeCtx = HistCombat(1, targetHpValid: true, targetHpPct: 20);
        Assert.True(TtkPolicy.ExecuteWasteBypass(execute, executeCtx));
        Assert.False(TtkPolicy.HistoryWasteGuardHolds(execute, executeCtx, 20, 0.5));
        Assert.False(TtkPolicy.WasteGuardHolds(execute, executeCtx, 20, 0.5));

        // Curated killSecure on a dying, long-lived target bypasses it too.
        var killSecure = Offensive(OffensiveUsage.WindowDriven, killSecure: true);
        var dying = HistCombat(1, ttkValid: true, ttkSec: 12, ageSec: 25,
            targetHpValid: true, targetHpPct: 30, targetHpFrac: 0.30);
        Assert.True(TtkPolicy.KillSecureBypass(killSecure, dying));
        Assert.False(TtkPolicy.HistoryWasteGuardHolds(killSecure, dying, 20, 0.5));
        Assert.False(TtkPolicy.WasteGuardHolds(killSecure, dying, 20, 0.5));
    }

    // ----- invalid-live restricts the history hold to provisional-eligible -----

    [Fact]
    public void Invalid_Live_Restricts_History_Hold_To_Provisional_Eligible()
    {
        var major = Offensive(OffensiveUsage.WindowDriven);   // eligible
        var minor = Offensive(OffensiveUsage.ShortCooldown);  // not eligible (base 5 s)

        var invalidLive = HistCombat(1);
        Assert.True(TtkPolicy.HistoryWasteGuardHolds(major, invalidLive, 20, 0.5));
        Assert.False(TtkPolicy.HistoryWasteGuardHolds(minor, invalidLive, 20, 0.5));

        // A valid live estimate applies the branch to every T1 class.
        var validLive = HistCombat(1, ttkValid: true, ttkSec: 300);
        Assert.True(TtkPolicy.HistoryWasteGuardHolds(minor, validLive, 20, 0.5));

        // Not binding -> the branch never fires (fail open).
        Assert.False(TtkPolicy.HistoryWasteGuardHolds(major, HistCombat(1, binding: false), 20, 0.5));
    }

    // ----- consumable/trinket Burst history rule -----

    private static ProviderInput PInput(AbilityDefinition ability, CombatContext ctx, PolicyOptions options) =>
        new()
        {
            Input = new PolicyInput
            {
                Slot = Slot.Consumable,
                SpellId = ability.SpellId,
                Context = ctx,
                Options = options,
                Memory = new PolicyMemory(),
                NowMs = 1000,
                InCombat = true,
                HasTarget = true,
            },
            Ability = ability,
            Catalog = AbilityCatalog.Default,
            Range = TriState.Yes,
        };

    [Fact]
    public void Burst_Consumable_History_Releases_A_Long_Fight_And_Holds_A_Short_One()
    {
        var potion = Consumable();
        var burst = new PolicyOptions { Preset = RotationPreset.Burst };

        // Invalid live + binding history below 5 s -> held (ttk-hist).
        var shortHistory = TtkPolicyHistoryTests_Provider(potion, HistCombat(3), burst);
        Assert.Equal(PolicyVerdict.Hold, shortHistory.Verdict);
        Assert.Contains("ttk-hist", shortHistory.Reason);

        // Invalid live + binding history above 5 s -> the burst is released.
        var longHistory = TtkPolicyHistoryTests_Provider(potion, HistCombat(40), burst);
        Assert.Equal(PolicyVerdict.Use, longHistory.Verdict);

        // No binding window -> the pre-v3.7 hold is preserved.
        var noHistory = TtkPolicyHistoryTests_Provider(potion, HistCombat(3, binding: false), burst);
        Assert.Equal(PolicyVerdict.Hold, noHistory.Verdict);
        Assert.Contains("burst preset", noHistory.Reason);
    }

    private static PolicyDecision TtkPolicyHistoryTests_Provider(
        AbilityDefinition ability, CombatContext ctx, PolicyOptions options) =>
        CandidateProviders.MaxDpsRotation.Evaluate(PInput(ability, ctx, options));

    // ----- settings parse + clamp -----

    [Fact]
    public void History_Settings_Parse_And_Clamp()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mdb-ttkhist-{Guid.NewGuid():N}.ini");
        try
        {
            File.WriteAllText(path,
                "[TimeToKill]\nHistory=0\nHistoryKills=99\nHistoryMinKills=1\n" +
                "HistoryMaxAgeSec=10\nHistoryQuantile=10\nHistoryDurFactor=5\n");
            var s = AppSettings.Load(path);
            Assert.False(s.TimeToKillHistory);
            Assert.Equal(20, s.TimeToKillHistoryKills);
            Assert.Equal(2, s.TimeToKillHistoryMinKills);
            Assert.Equal(30, s.TimeToKillHistoryMaxAgeSec);
            Assert.Equal(50, s.TimeToKillHistoryQuantile);
            Assert.Equal(1.0, s.TimeToKillHistoryDurFactor, 6);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }

        var defaults = new AppSettings();
        Assert.True(defaults.TimeToKillHistory);
        Assert.Equal(8, defaults.TimeToKillHistoryKills);
        Assert.Equal(3, defaults.TimeToKillHistoryMinKills);
        Assert.Equal(240, defaults.TimeToKillHistoryMaxAgeSec);
        Assert.Equal(75, defaults.TimeToKillHistoryQuantile);
        Assert.Equal(0.5, defaults.TimeToKillHistoryDurFactor, 6);
    }

    // ----- replay: hk/hs reproduce -----

    private const int Avatar = 107574;
    private static readonly KeyStroke KeyR = new(0x52, false, false, false);
    private static readonly bool[] AllSlots = [true, true, true, true, true, true, true, true];

    private static BridgeFrame Frame(int heartbeat, int targetBand, bool hasTarget)
    {
        var frame = PixelProtocol.Decode(new TestV5Frame()
            .Vitals(100)
            .Cast(PlayerCastState.None)
            .Target(inMelee: true, casting: false, interruptible: false)
            .TargetHp(targetBand)
            .Ranges((Slot.Offensive, TriState.Yes))
            .ClassSpec("WARRIOR", 2)
            .Slot(Slot.Offensive, KeyR.VirtualKey)
            .SpellId(Slot.Offensive, Avatar)
            .Build(heartbeat, inCombat: true, hasTarget: hasTarget));
        Assert.NotNull(frame);
        return frame!;
    }

    [Fact]
    public void Replay_Reproduces_The_Adaptive_History_Fields()
    {
        var events = new List<TelemetryEvent>
        {
            TelemetryEvent.Session(0, "3.7.0", PixelProtocol.SupportedVersion, 10000, true, "start",
                AbilityCatalog.CatalogVersion),
        };
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();
        var estimator = new TtkEstimator();
        var heartbeat = 0;
        var now = 0L;

        void Tick(int band, bool hasTarget, string note)
        {
            var frame = Frame(++heartbeat, band, hasTarget);
            tracker.Update(frame, now);
            var estimate = estimator.Update(now, frame.HasTarget, frame.TargetHpPct >= 0,
                TtkEstimator.BandFromPercent(frame.TargetHpPct), frame.InCombat);
            var combat = CombatContext.FromFrame(frame).WithTtk(estimate);
            var options = new PolicyOptions();
            var plan = scheduler.Advance(new ScheduleInput
            {
                Frame = frame,
                OutOfCombatPermitted = true,
                Candidates = tracker.Snapshot(AllSlots),
                NowMs = now,
                MinKeyIntervalMs = 120,
                StaleAfterMs = 1500,
                HeartbeatTimeoutMs = 500,
                RepeatSuppressMs = 900,
                Context = combat,
                Options = options,
                Catalog = AbilityCatalog.Default,
                CollectPolicyVerdicts = true,
            });
            events.Add(TelemetryEvent.Tick(now, frame, DecodeFault.None, null, null, true,
                tracker.Snapshot(AllSlots), note, true,
                TelemetryEvent.BuildPolicy(plan, true, combat, options), ttkFeedMs: now));
        }

        // Three fast trash kills (binds the window), then a history-only pull.
        for (var kill = 0; kill < 3; kill++)
        {
            Tick(14, true, $"mob {kill} spawn");
            now += 2000; Tick(2, true, $"mob {kill} low");
            now += 100; Tick(2, false, $"mob {kill} dead");
            now += 900;
        }
        Tick(14, true, "pull");
        now += 1200; Tick(12, true, "pull decline - history-only");

        var result = ReplayRunner.Run(events);

        Assert.Equal(0, result.Mismatches);
        Assert.Equal(0, result.VerdictMismatches);
        Assert.Equal(0, result.HistMismatches);
        Assert.Contains("adaptive-history reconstruction 0 mismatch(es)", result.Report);
    }

    // ----- WithSlotRange must carry the v3.7 history fields -----

    [Fact]
    public void WithSlotRange_Preserves_History_Fields()
    {
        var ctx = HistCombat(7, ttkValid: false).WithSlotRange(0, TriState.Yes);

        Assert.Equal(TriState.Yes, ctx.SlotRange[0]);
        Assert.True(ctx.TtkHistBinding);
        Assert.Equal(7, ctx.TtkHistSec);
        Assert.Equal(0.1, ctx.TtkHistRate, 6);
        Assert.Equal(5, ctx.TtkHistKills);
        Assert.Equal(0.5, ctx.TtkHistDurFactor, 6);
        Assert.True(ctx.TtkHistProvisional);
    }

    // ----- a zero cooldown/duration keeps the base need (activeDur = 0) -----

    [Fact]
    public void Zero_Cooldown_Active_Duration_Keeps_Base_Need()
    {
        Assert.Equal(10.0, TtkPolicy.NeedAdaptive(10, 0, 0.5), 6);

        var ability = Offensive(OffensiveUsage.WindowDriven, cooldownMs: 0, durationMs: 0);
        var activeDurSec = (TtkPolicy.TwoUsesFactor * (double)ability.CooldownMs + ability.DurationMs) / 1000.0;
        Assert.Equal(0.0, activeDurSec, 6);
        // base need stays 10 s: 9 s holds, exactly 10 s uses.
        Assert.True(TtkPolicy.WasteGuardHolds(ability, HistCombat(9), activeDurSec, 0.5));
        Assert.False(TtkPolicy.WasteGuardHolds(ability, HistCombat(10), activeDurSec, 0.5));
    }

    // ----- telemetry export: hr/hprov beside hk/hs -----

    [Fact]
    public void Telemetry_Exports_HistRate_And_HistProvisional()
    {
        var ctx = HistCombat(6);
        var policy = TelemetryEvent.BuildPolicy(
            SchedulePlan.Hold(ScheduleReason.NoCandidate), fresh: true, ctx, new PolicyOptions());

        Assert.Equal(5, policy.HistKills);
        Assert.Equal(0.1, policy.HistRate!.Value, 4);
        Assert.Equal(6.0, policy.HistTtkSec!.Value, 4);
        Assert.True(policy.HistProvisional);
    }

    // ----- custom [TimeToKill] tuning is recorded AND replayed -----

    [Fact]
    public void Custom_History_Tuning_Is_Recorded_And_Replayed()
    {
        // MinKills=2 binds after two kills: a defaults-only replay (MinKills=3)
        // would rebuild a non-binding window and false-mismatch the recorded hk.
        var ttkOptions = new TtkOptions(
            History: true, Kills: 8, MinKills: 2, MaxAgeSec: 240, Quantile: 75, DurFactor: 0.25);
        var options = new PolicyOptions { TtkHistoryMinKills = 2, TtkHistoryDurFactor = 0.25 };
        var events = HistoryRecording(options, ttkOptions, kills: 2);

        var parsed = events
            .Select(e => TelemetryJson.Deserialize(Encoding.UTF8.GetString(TelemetryJson.Serialize(e)))!)
            .ToList();
        Assert.Contains(parsed, e => e.Policy?.Options?.TtkHistoryMinKills == 2);
        Assert.Contains(parsed, e => e.Policy?.Options?.TtkHistoryDurFactor == 0.25);

        var result = ReplayRunner.Run(parsed);
        Assert.Equal(0, result.VerdictMismatches);
        Assert.Equal(0, result.HistMismatches);
        Assert.Contains("adaptive-history reconstruction 0 mismatch(es)", result.Report);
    }

    /// <summary>Builds a recording from the real pipeline with explicit history tuning.</summary>
    private static List<TelemetryEvent> HistoryRecording(PolicyOptions options, TtkOptions ttkOptions, int kills)
    {
        var events = new List<TelemetryEvent>
        {
            TelemetryEvent.Session(0, "3.7.0", PixelProtocol.SupportedVersion, 10000, true, "start",
                AbilityCatalog.CatalogVersion),
        };
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();
        var estimator = new TtkEstimator(ttkOptions);
        var heartbeat = 0;
        var now = 0L;

        void Tick(int band, bool hasTarget, string note)
        {
            var frame = Frame(++heartbeat, band, hasTarget);
            tracker.Update(frame, now);
            var estimate = estimator.Update(now, frame.HasTarget, frame.TargetHpPct >= 0,
                TtkEstimator.BandFromPercent(frame.TargetHpPct), frame.InCombat);
            var combat = CombatContext.FromFrame(frame).WithTtk(estimate, options.TtkHistoryDurFactor);
            var plan = scheduler.Advance(new ScheduleInput
            {
                Frame = frame,
                OutOfCombatPermitted = true,
                Candidates = tracker.Snapshot(AllSlots),
                NowMs = now,
                MinKeyIntervalMs = 120,
                StaleAfterMs = 1500,
                HeartbeatTimeoutMs = 500,
                RepeatSuppressMs = 900,
                Context = combat,
                Options = options,
                Catalog = AbilityCatalog.Default,
                CollectPolicyVerdicts = true,
            });
            events.Add(TelemetryEvent.Tick(now, frame, DecodeFault.None, null, null, true,
                tracker.Snapshot(AllSlots), note, true,
                TelemetryEvent.BuildPolicy(plan, true, combat, options), ttkFeedMs: now));
        }

        for (var kill = 0; kill < kills; kill++)
        {
            Tick(14, true, $"mob {kill} spawn");
            now += 2000; Tick(2, true, $"mob {kill} low");
            now += 100; Tick(2, false, $"mob {kill} dead");
            now += 900;
        }
        Tick(14, true, "pull");
        now += 1200; Tick(12, true, "pull decline - history-only");
        return events;
    }
}
