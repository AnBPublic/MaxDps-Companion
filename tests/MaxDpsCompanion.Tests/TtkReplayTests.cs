using System.Text;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// A5: canonical TTK replay acceptance (v3.2.0). The recording is produced by the
/// REAL pipeline (v5 wire frame -&gt; decode -&gt; tracker -&gt; TTK estimator feed -&gt;
/// scheduler with the estimate attached -&gt; telemetry record). Replay must
/// reconstruct the estimator from the recorded target-HP series and re-derive
/// every policy verdict with zero mismatches.
///
/// Covers: Avatar now fires on the fast trash target (the T1 waste guard that
/// once held it was removed in 3.7.9), a long-lived target where the same burst
/// fires, and a Solo defensive held by the T4 dying-target rule. Execute-range
/// bypass needs a curated <c>executeFavored</c> row (workstream T-B) and is
/// covered by <see cref="TtkPolicyTests"/> at the rule level.
/// </summary>
public class TtkReplayTests
{
    private const int Avatar = 107574;      // Warrior MajorBurst, default min TTK 15 s
    private const int ShieldWall = 871;     // Warrior defensive
    private static readonly KeyStroke KeyR = new(0x52, false, false, false);
    private static readonly KeyStroke KeyF = new(0x46, false, false, false);
    private static readonly bool[] AllSlots = [true, true, true, true, true, true, true, true];

    private static BridgeFrame Frame(int heartbeat, int targetBand, bool withDefensive)
    {
        var builder = new TestV5Frame()
            .Vitals(100)
            .Cast(PlayerCastState.None)
            .Target(inMelee: true, casting: false, interruptible: false)
            .TargetHp(targetBand)
            .Ranges((Slot.Offensive, TriState.Yes), (Slot.Defensive, TriState.Yes))
            .ClassSpec("WARRIOR", 2)   // Fury
            .Slot(Slot.Offensive, KeyR.VirtualKey)
            .SpellId(Slot.Offensive, Avatar);
        if (withDefensive)
            builder.Slot(Slot.Defensive, KeyF.VirtualKey).SpellId(Slot.Defensive, ShieldWall);
        var frame = PixelProtocol.Decode(builder.Build(heartbeat, inCombat: true));
        Assert.NotNull(frame);
        return frame!;
    }

    private static List<TelemetryEvent> CanonicalRecording()
    {
        var events = new List<TelemetryEvent>
        {
            TelemetryEvent.Session(0, "3.2.0", PixelProtocol.SupportedVersion, 10000, true, "start",
                AbilityCatalog.CatalogVersion),
        };
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();
        var estimator = new TtkEstimator();
        var heartbeat = 0;

        TelemetryEvent Tick(long now, int band, bool withDefensive, bool solo, string note)
        {
            var frame = Frame(++heartbeat, band, withDefensive);
            tracker.Update(frame, now);
            var estimate = estimator.Update(now, frame.HasTarget, frame.TargetHpPct >= 0,
                TtkEstimator.BandFromPercent(frame.TargetHpPct));
            var combat = CombatContext.FromFrame(frame).WithTtk(estimate);
            // v3.8: TtkWarmupSec is recorded so replay reproduces the ttkw
            // field; the warmup hold itself was removed in 3.7.9.
            var options = new PolicyOptions { SoloEnabled = solo, TtkWarmupSec = TtkPolicy.DefaultWarmupSec };
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
            return TelemetryEvent.Tick(now, frame, DecodeFault.None, null, null, true,
                tracker.Snapshot(AllSlots), note, true,
                TelemetryEvent.BuildPolicy(plan, true, combat, options), ttkFeedMs: now);
        }

        // Phase 1 — trash pack: target HP collapses fast. v3.7.9 removed the T1
        // waste guard, so Avatar fires; in Solo, the defensive is still held by
        // T4.
        events.Add(Tick(0, 14, withDefensive: true, solo: true, "trash t=0"));
        events.Add(Tick(500, 13, true, true, "trash t=500"));
        events.Add(Tick(1000, 11, true, true, "trash t=1000"));
        events.Add(Tick(1500, 9, true, true, "trash t=1500"));
        events.Add(Tick(2000, 7, true, true, "trash t=2000"));
        events.Add(Tick(2500, 5, true, true, "trash t=2500 - hold expected"));

        // Phase 2 — boss: a fresh full-HP target (frac jump resets learning),
        // then a slow decline, so the same burst fires.
        events.Add(Tick(3000, 14, withDefensive: false, solo: false, "boss t=3000 (reset)"));
        events.Add(Tick(4000, 14, false, false, "boss t=4000"));
        events.Add(Tick(5000, 14, false, false, "boss t=5000"));
        events.Add(Tick(6000, 13, false, false, "boss t=6000"));
        events.Add(Tick(7000, 13, false, false, "boss t=7000"));
        events.Add(Tick(8000, 12, false, false, "boss t=8000 - fire expected"));

        return events;
    }

    private static string SourceFixturePath() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "tests", "MaxDpsCompanion.Tests", "fixtures",
            "ttk-warrior-burst.jsonl"));

    private static string OutputFixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "ttk-warrior-burst.jsonl");

    [Fact]
    public void Regenerate_Fixture_When_Requested()
    {
        if (Environment.GetEnvironmentVariable("MDB_REGENERATE_FIXTURES") != "1") return;
        WriteFixture(SourceFixturePath());
    }

    [Fact]
    public void Canonical_InMemory_Recording_Recomputes_Every_Verdict()
    {
        var result = ReplayRunner.Run(CanonicalRecording());

        Assert.Equal(12, result.Ticks);
        Assert.Equal(0, result.Errors);
        Assert.Equal(0, result.Mismatches);
        Assert.Equal(0, result.VerdictMismatches);
        Assert.Contains("0 mismatch(es)", result.Report);
    }

    [Fact]
    public void Ttk_Fixture_Replays_With_Zero_Mismatches()
    {
        var output = OutputFixturePath();
        if (!File.Exists(output))
        {
            WriteFixture(SourceFixturePath());
            File.Copy(SourceFixturePath(), output, overwrite: true);
        }

        var (events, badLines) = TelemetryReader.Read(output);
        var result = ReplayRunner.Run(events, badLines, source: output);

        Assert.Equal(0, badLines);
        Assert.Equal(0, result.Errors);
        Assert.Equal(0, result.Mismatches);
        Assert.Equal(0, result.VerdictMismatches);
        Assert.Contains("0 mismatch(es)", result.Report);
    }

    private static void WriteFixture(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false));
        foreach (var evt in CanonicalRecording())
            writer.WriteLine(Encoding.UTF8.GetString(TelemetryJson.Serialize(evt)));
    }

    // ----- v3.6 trash-pack replay fixture (6 mobs, each <5 s) -----

    private static string TrashPackSourceFixturePath() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "tests", "MaxDpsCompanion.Tests", "fixtures",
            "ttk-trash-pack.jsonl"));

    private static string TrashPackOutputFixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "ttk-trash-pack.jsonl");

    [Fact]
    public void Trash_Pack_Fixture_Replays_With_Zero_Mismatches()
    {
        var output = TrashPackOutputFixturePath();
        if (!File.Exists(output))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.Copy(TrashPackSourceFixturePath(), output, overwrite: true);
        }

        var (events, badLines) = TelemetryReader.Read(output);
        var result = ReplayRunner.Run(events, badLines, source: output);

        Assert.Equal(0, badLines);
        Assert.Equal(0, result.Errors);
        Assert.Equal(0, result.Mismatches);
        Assert.Equal(0, result.VerdictMismatches);
        Assert.Equal(24, result.Ticks);
    }
}
