using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Stream 3 — DPS parity + restrict-only presets + cache/audit/design support.
/// New-file suite; each class targets one Stream 3 task.
/// </summary>
public class Stream3PresetTests
{
    private const long Now = 10_000;
    private static readonly bool[] AllSlots = [true, true, true, true, true, true, true, true];

    private static CombatContext Ctx(bool ttkValid = false, double ttkSec = 300, string? cls = null, string? spec = null)
    {
        var range = new TriState[PixelProtocol.SlotCount];
        range[(int)Slot.Offensive] = TriState.Yes;
        return new CombatContext
        {
            HpValid = true,
            HpPct = 90,
            Cast = PlayerCastState.None,
            TargetCasting = TriState.No,
            SlotRange = range,
            SlotBuffActive = new TriState[PixelProtocol.SlotCount],
            ContextValid = true,
            Class = cls,
            Spec = spec,
            TtkValid = ttkValid,
            TtkSec = ttkSec,
        };
    }

    private static PolicyDecision Evaluate(
        Slot slot, int spellId, CombatContext ctx, PolicyOptions options) =>
        PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = slot,
            SpellId = spellId,
            Context = ctx,
            Options = options,
            Memory = new PolicyMemory(),
            NowMs = Now,
            InCombat = true,
            HasTarget = true,
        }, AbilityCatalog.Default);

    [Fact]
    public void Burst_Preset_Holds_A_Major_Offensive_Until_A_Boss_Ttk_Is_Measurable()
    {
        // Fury Recklessness (1719, MajorOffensive) is a curated gap-fill; the
        // Burst preset holds it while the TTK is invalid.
        var held = Evaluate(Slot.Offensive, 1719, Ctx(ttkValid: false),
            new PolicyOptions { Preset = RotationPreset.Burst });
        Assert.Equal(PolicyVerdict.Hold, held.Verdict);
        Assert.Contains("burst preset", held.Reason);

        // A valid boss TTK releases it (no other gate holds this in combat).
        var released = Evaluate(Slot.Offensive, 1719, Ctx(ttkValid: true, ttkSec: 40),
            new PolicyOptions { Preset = RotationPreset.Burst });
        Assert.Equal(PolicyVerdict.Use, released.Verdict);
    }

    [Fact]
    public void Full_Preset_Is_The_Default_And_Does_Not_Hold()
    {
        var decision = Evaluate(Slot.Offensive, 1719, Ctx(ttkValid: false), PolicyOptions.Standard);
        Assert.Equal(PolicyVerdict.Use, decision.Verdict);
    }

    [Fact]
    public void Aoe_Preset_Conserves_Single_Target_Only_Offensives()
    {
        // Rogue Deathmark (360194) is curated SingleTargetOnly.
        var held = Evaluate(Slot.Offensive, 360194, Ctx(),
            new PolicyOptions { TargetPreset = TargetPreset.Aoe });
        Assert.Equal(PolicyVerdict.Hold, held.Verdict);
        Assert.Contains("AoE preset", held.Reason);

        var released = Evaluate(Slot.Offensive, 360194, Ctx(), PolicyOptions.Standard);
        Assert.NotEqual(PolicyVerdict.Hold, released.Verdict);
    }

    [Fact]
    public void AppSettings_RoundTrips_The_Rotation_Presets()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mdb-rotation-{Guid.NewGuid():N}.ini");
        try
        {
            File.WriteAllText(path, "[Rotation]\nMode=Burst\nTargets=Aoe\n");
            var loaded = AppSettings.Load(path);
            Assert.Equal(RotationPreset.Burst, loaded.ModePreset);
            Assert.Equal(TargetPreset.Aoe, loaded.TargetMode);

            loaded.Save();
            var again = AppSettings.Load(path);
            Assert.Equal(RotationPreset.Burst, again.ModePreset);
            Assert.Equal(TargetPreset.Aoe, again.TargetMode);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Presets_Default_To_Current_Behaviour()
    {
        var settings = AppSettings.Load(Path.Combine(Path.GetTempPath(), $"mdb-none-{Guid.NewGuid():N}.ini"));
        Assert.Equal(RotationPreset.Full, settings.ModePreset);
        Assert.Equal(TargetPreset.SingleTarget, settings.TargetMode);

        var options = PolicyOptions.FromSettings(settings);
        Assert.Equal(RotationPreset.Full, options.Preset);
        Assert.Equal(TargetPreset.SingleTarget, options.TargetPreset);
    }
}

/// <summary>Stream 3 §3.2 — explicit offensive gap-fill source parity.</summary>
public class Stream3OffensiveParityTests
{
    private const int Recklessness = 1719;

    private static BridgeFrame FuryFrame()
    {
        var builder = new TestV5Frame()
            .Vitals(100)
            .Cast(PlayerCastState.None)
            .Target(inMelee: true, casting: false, interruptible: false)
            .Ranges((Slot.Offensive, TriState.Yes))
            .ClassSpec("WARRIOR", 2)
            .Slot(Slot.Offensive, 0x52)
            .SpellId(Slot.Offensive, Recklessness);
        var frame = PixelProtocol.Decode(builder.Build(1, inCombat: true));
        Assert.NotNull(frame);
        return frame!;
    }

    [Fact]
    public void FromFrame_Projects_The_Explicit_Offensive_GapFill_Bit()
    {
        var combat = CombatContext.FromFrame(FuryFrame());
        Assert.True(combat.OffensiveDerivedGapFill);
        Assert.True(combat.IsOffensiveGapFill(Recklessness, AbilityCatalog.Default));
    }

    [Fact]
    public void Telemetry_RoundTrips_The_Offensive_GapFill_Bit()
    {
        var policy = new TelemetryPolicy { Reason = "x", OffensiveDerivedGapFill = true };
        var eventWith = new TelemetryEvent { Kind = TelemetryKind.Tick, Policy = policy };
        var json = Encoding.UTF8.GetString(TelemetryJson.Serialize(eventWith));
        Assert.Contains("\"osrc\":true", json);

        var loaded = TelemetryJson.Deserialize(json);
        Assert.NotNull(loaded?.Policy);
        Assert.True(loaded!.Policy!.OffensiveDerivedGapFill);

        // False is omitted (old line shape preserved).
        var offJson = Encoding.UTF8.GetString(TelemetryJson.Serialize(
            new TelemetryEvent { Kind = TelemetryKind.Tick, Policy = new TelemetryPolicy { Reason = "y" } }));
        Assert.DoesNotContain("osrc", offJson);
    }

    [Fact]
    public void Replay_Reproduces_Offensive_GapFill_Verdicts_With_Zero_Mismatches()
    {
        var events = new List<TelemetryEvent>
        {
            TelemetryEvent.Session(0, "3.3.0", PixelProtocol.SupportedVersion, 10000, true, "start",
                AbilityCatalog.CatalogVersion),
        };
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();
        tracker.Update(FuryFrame(), 1000);
        var combat = CombatContext.FromFrame(FuryFrame());
        var plan = scheduler.Advance(new ScheduleInput
        {
            Frame = FuryFrame(),
            Candidates = tracker.Snapshot([true, true, true, true, true, true, true, true]),
            NowMs = 1000,
            MinKeyIntervalMs = 120,
            StaleAfterMs = 1500,
            HeartbeatTimeoutMs = 500,
            RepeatSuppressMs = 900,
            Context = combat,
            Options = PolicyOptions.Standard,
            Catalog = AbilityCatalog.Default,
            CollectPolicyVerdicts = true,
        });
        events.Add(TelemetryEvent.Tick(1000, FuryFrame(), DecodeFault.None, null, null, true,
            tracker.Snapshot([true, true, true, true, true, true, true, true]), "gap-fill", true,
            TelemetryEvent.BuildPolicy(plan, true, combat, PolicyOptions.Standard)));

        var result = ReplayRunner.Run(events);
        Assert.Equal(0, result.Mismatches);
        Assert.Equal(0, result.VerdictMismatches);
    }
}

/// <summary>Stream 3 §3.4 — read-only suggested-vs-cast audit.</summary>
public class Stream3CastAuditTests
{
    private static TelemetryEvent Send(long tMs, Slot slot, int spellId) =>
        TelemetryEvent.Sent(tMs, "spell", slot, new KeyStroke(0x52, false, false, false), 120, spellId);

    private static TelemetryEvent Tick(long tMs, Slot selected, int spellId, string reason) => new()
    {
        Kind = TelemetryKind.Tick,
        TMs = tMs,
        Policy = new TelemetryPolicy
        {
            Selected = selected,
            Reason = "scheduler",
            Verdicts =
            [
                new TelemetryVerdict { Slot = selected, SpellId = spellId, Verdict = "Use", Reason = reason },
            ],
        },
    };

    [Fact]
    public void Pairs_A_Send_With_The_Plan_Head_It_Justified()
    {
        // Live ordering: the send event is written before the tick that explains it.
        var report = CastAudit.Build(
        [
            Send(990, Slot.Offensive, 1719),
            Tick(1000, Slot.Offensive, 1719, "offensive candidate; no conflict observed"),
        ]);

        Assert.Equal(1, report.Suggestions);
        Assert.Equal(1, report.Casts);
        Assert.Single(report.Rows);
        Assert.True(report.Rows[0].Cast);
        Assert.Equal(10, report.Rows[0].LatencyMs);
    }

    [Fact]
    public void Reports_A_Head_That_No_Send_Followed_As_Not_Cast()
    {
        var report = CastAudit.Build([Tick(1000, Slot.Defensive, 871, "MaxDps defensive gate fired")]);
        Assert.Equal(1, report.Suggestions);
        Assert.Equal(0, report.Casts);
        Assert.Equal(1, report.Misses);
        Assert.False(report.Rows[0].Cast);
        Assert.Equal("not cast", report.Rows[0].Status);
    }
}

/// <summary>Stream 3 §3.4 — spell icon cache TTL + catalog-regeneration invalidation.</summary>
public class Stream3SpellIconTtlTests
{
    private static byte[] TinyJpeg()
    {
        using var bitmap = new Bitmap(4, 4);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Jpeg);
        return stream.ToArray();
    }

    private static string NewTempDir() =>
        Path.Combine(Path.GetTempPath(), "mdb-icons-ttl-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void An_Expired_Entry_Is_Evicted_And_Reloaded_From_Disk()
    {
        var dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(dir);
            var now = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
            var cache = new SpellIconCache(dir, handler: null, slugProvider: null, utcNow: () => now);
            cache.Ttl = TimeSpan.FromMinutes(1);
            File.WriteAllBytes(Path.Combine(dir, "5277.jpg"), TinyJpeg());

            Assert.NotNull(cache.TryGet(5277));
            now = now.AddMinutes(2);
            Assert.NotNull(cache.TryGet(5277));   // TTL elapsed: re-read from disk, still valid
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Invalidate_Drops_Memory_So_The_Next_Read_Rechecks_Disk()
    {
        var dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(dir);
            var cache = new SpellIconCache(dir);
            File.WriteAllBytes(Path.Combine(dir, "5277.jpg"), TinyJpeg());

            Assert.NotNull(cache.TryGet(5277));
            File.Delete(Path.Combine(dir, "5277.jpg"));
            // Still served from memory until invalidated.
            Assert.NotNull(cache.TryGet(5277));
            cache.Invalidate();
            Assert.Null(cache.TryGet(5277));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}

/// <summary>Stream 1/3 — Solo band sliders pass through the existing Solo options.</summary>
public class Stream3SoloBandEditorTests
{
    [Fact]
    public void ApplyTo_Writes_The_Validated_Bands_Into_Settings()
    {
        var editor = new SoloBandEditor();
        var settings = new AppSettings
        {
            SoloMinorHpPct = 80,
            SoloMajorHpPct = 55,
            SoloImmunityHpPct = 35,
        };
        editor.LoadFrom(settings);
        var (minor, major, immunity) = editor.ApplyTo(settings);

        Assert.Equal((80, 55, 35), (minor, major, immunity));
        Assert.Equal(80, settings.SoloMinorHpPct);
        Assert.Equal(55, settings.SoloMajorHpPct);
        Assert.Equal(35, settings.SoloImmunityHpPct);
    }

    [Fact]
    public void Slider_Ranges_Match_The_Spec()
    {
        var editor = new SoloBandEditor();
        var (minor, major, immunity) = editor.SlidersForTest;
        Assert.Equal(40, minor.Minimum);
        Assert.Equal(99, minor.Maximum);
        Assert.Equal(20, major.Minimum);
        Assert.Equal(90, major.Maximum);
        Assert.Equal(5, immunity.Minimum);
        Assert.Equal(60, immunity.Maximum);
    }
}
