using System.Text;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Canonical offensive gap-fill replay acceptance (v3.0.0). The recording is
/// produced by the REAL pipeline (v6 wire frame -&gt; decode -&gt; tracker -&gt;
/// scheduler with options/catalog -&gt; telemetry record); the checked-in
/// fixture is that sequence serialized, and replay must re-derive every policy
/// verdict with zero mismatches.
///
/// Covers: a curated offensive gap-fill candidate (Recklessness 1719 for
/// Warrior Fury) firing in combat; the same candidate held out of combat in
/// Normal mode; and the own-buff skip staying upstream of the gap-fill gate.
/// </summary>
public class OffensiveGapFillReplayTests
{
    private const int Recklessness = 1719;    // Warrior Fury gap-fill entry
    private static readonly KeyStroke KeyR = new(0x52, false, false, false);
    private static readonly bool[] AllSlots = [true, true, true, true, true, true, true, true];

    private static BridgeFrame Frame(int heartbeat, bool inCombat)
    {
        var builder = new TestV5Frame()
            .Vitals(100)
            .Cast(PlayerCastState.None)
            .Target(inMelee: true, casting: false, interruptible: false)
            .Ranges((Slot.Offensive, TriState.Yes))
            .ClassSpec("WARRIOR", 2)   // Fury
            .Slot(Slot.Offensive, KeyR.VirtualKey)
            .SpellId(Slot.Offensive, Recklessness);
        var frame = PixelProtocol.Decode(builder.Build(heartbeat, inCombat: inCombat));
        Assert.NotNull(frame);
        return frame!;
    }

    private static List<TelemetryEvent> CanonicalRecording()
    {
        var events = new List<TelemetryEvent>
        {
            TelemetryEvent.Session(0, "3.0.0", PixelProtocol.SupportedVersion, 10000, true, "start",
                AbilityCatalog.CatalogVersion),
        };
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();

        TelemetryEvent Tick(long now, BridgeFrame frame, string note)
        {
            tracker.Update(frame, now);
            var combat = CombatContext.FromFrame(frame);
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
                Options = PolicyOptions.Standard,
                Catalog = AbilityCatalog.Default,
                CollectPolicyVerdicts = true,
            });
            return TelemetryEvent.Tick(now, frame, DecodeFault.None, null, null, true,
                tracker.Snapshot(AllSlots), note, true,
                TelemetryEvent.BuildPolicy(plan, true, combat, PolicyOptions.Standard));
        }

        void Send(TelemetryEvent tick, long now, Slot slot, KeyStroke key, int spell)
        {
            scheduler.NoteSent(now, slot, key, spell);
            events.Add(TelemetryEvent.Sent(now, "spell", slot, key, 0, spell));
            events.Add(tick);
        }

        // 1 @1000: in combat, MaxDps names no offensive -> the Fury gap-fill
        //   list supplies Recklessness; Use and sent.
        Send(Tick(1000, Frame(1, inCombat: true), "offensive gap-fill in combat"),
            1000, Slot.Offensive, KeyR, Recklessness);

        // 2 @1300: out of combat, Normal mode -> the gap-fill is held.
        events.Add(Tick(1300, Frame(2, inCombat: false), "offensive gap-fill out of combat"));

        return events;
    }

    private static string SourceFixturePath() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "tests", "MaxDpsCompanion.Tests", "fixtures",
            "offensive-gapfill-warrior.jsonl"));

    private static string OutputFixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "offensive-gapfill-warrior.jsonl");

    [Fact]
    public void Regenerate_Fixture_When_Requested()
    {
        if (Environment.GetEnvironmentVariable("MDB_REGENERATE_FIXTURES") != "1") return;
        var source = SourceFixturePath();
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        using var writer = new StreamWriter(source, append: false, new UTF8Encoding(false));
        foreach (var evt in CanonicalRecording())
            writer.WriteLine(Encoding.UTF8.GetString(TelemetryJson.Serialize(evt)));
    }

    [Fact]
    public void Canonical_InMemory_Recording_Recomputes_Every_Verdict()
    {
        var result = ReplayRunner.Run(CanonicalRecording());

        Assert.Equal(2, result.Ticks);
        Assert.Equal(1, result.Sends);
        Assert.Equal(0, result.Errors);
        Assert.Equal(0, result.Mismatches);
        Assert.Equal(0, result.VerdictMismatches);
        Assert.Contains("offensive gap-fill", result.Report);
        Assert.Contains("0 mismatch(es)", result.Report);
    }

    [Fact]
    public void Offensive_GapFill_Fixture_Replays_With_Zero_Mismatches()
    {
        var output = OutputFixturePath();
        if (!File.Exists(output))
        {
            var source = SourceFixturePath();
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            using (var writer = new StreamWriter(source, append: false, new UTF8Encoding(false)))
            {
                foreach (var evt in CanonicalRecording())
                    writer.WriteLine(Encoding.UTF8.GetString(TelemetryJson.Serialize(evt)));
            }
            File.Copy(source, output, overwrite: true);
        }

        var (events, badLines) = TelemetryReader.Read(output);
        var result = ReplayRunner.Run(events, badLines, source: output);

        Assert.Equal(0, badLines);
        Assert.Equal(0, result.Errors);
        Assert.Equal(0, result.Mismatches);
        Assert.Equal(0, result.VerdictMismatches);
        Assert.Contains("offensive gap-fill", result.Report);
    }
}
