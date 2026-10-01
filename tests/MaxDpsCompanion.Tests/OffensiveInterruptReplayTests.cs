using System.Text;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Canonical offensive / interrupt replay acceptance. The recording is produced
/// by the REAL pipeline (v6 wire frame -> decode -> tracker -> scheduler with
/// options/catalog -> telemetry record), the checked-in fixture is the same
/// sequence serialized, and the replay must re-derive every policy verdict with
/// zero mismatches.
///
/// Covers the interrupt matrix (live interruptible cast = Use + scheduled/sent;
/// not-interruptible = Skip) and the offensive-cooldown rule (a vendor
/// offensive with no conflict = Use), plus one main-rotation press.
/// </summary>
public class OffensiveInterruptReplayTests
{
    private const int MainSpell = 12294;      // Mortal Strike (identity only)
    private const int Pummel = 6552;          // Warrior interrupt
    private const int Recklessness = 1719;    // Warrior vendor offensive cooldown

    private static readonly KeyStroke KeyE = new(0x45, false, false, false);
    private static readonly KeyStroke KeyX = new(0x58, false, false, false);
    private static readonly KeyStroke KeyR = new(0x52, false, false, false);
    private static readonly bool[] AllSlots = [true, true, true, true, true, true, true, true];

    /// <summary>
    /// A v6 Warrior (Fury) frame carrying the requested main / interrupt /
    /// offensive candidates and the target cast sensor state.
    /// </summary>
    private static BridgeFrame Frame(
        int heartbeat,
        bool main,
        bool interrupt,
        bool offensive,
        bool targetCasting,
        bool interruptible)
    {
        var builder = new TestV5Frame()
            .Vitals(100)
            .Cast(PlayerCastState.None)
            .Target(inMelee: true, casting: targetCasting, interruptible: interruptible)
            .Ranges(
                (Slot.Main, TriState.Yes),
                (Slot.Interrupt, TriState.Yes),
                (Slot.Offensive, TriState.Yes))
            .ClassSpec("WARRIOR", 2);   // Fury
        if (main) builder.Slot(Slot.Main, KeyE.VirtualKey).SpellId(Slot.Main, MainSpell);
        if (interrupt) builder.Slot(Slot.Interrupt, KeyX.VirtualKey).SpellId(Slot.Interrupt, Pummel);
        if (offensive) builder.Slot(Slot.Offensive, KeyR.VirtualKey).SpellId(Slot.Offensive, Recklessness);

        var frame = PixelProtocol.Decode(builder.Build(heartbeat));
        Assert.NotNull(frame);
        return frame!;
    }

    /// <summary>
    /// The canonical sequence, built through the live pipeline exactly as the
    /// engine would record it (a send event precedes its own tick event; the
    /// scheduler's NoteSent runs after the plan was computed).
    /// </summary>
    private static List<TelemetryEvent> CanonicalRecording()
    {
        var events = new List<TelemetryEvent>
        {
            TelemetryEvent.Session(0, "2.6.0", PixelProtocol.SupportedVersion, 10000, true, "start",
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

        // 1 @1000: a live interruptible cast + main + offensive. The interrupt
        //   is off the GCD and ranks first: Use and sent.
        Send(Tick(1000, Frame(1, main: true, interrupt: true, offensive: true,
                targetCasting: true, interruptible: true), "live interruptible cast"),
            1000, Slot.Interrupt, KeyX, Pummel);

        // 2 @1300: the cast is NOT interruptible (v5 veto) -> Skip; the main
        //   rotation (higher rank than the offensive) is sent.
        Send(Tick(1300, Frame(2, main: true, interrupt: true, offensive: true,
                targetCasting: true, interruptible: false), "cast not interruptible"),
            1300, Slot.Main, KeyE, MainSpell);

        // 3 @1600: the interruptable cast ended; only the vendor offensive
        //   cooldown remains -> Use and sent.
        Send(Tick(1600, Frame(3, main: false, interrupt: false, offensive: true,
                targetCasting: false, interruptible: false), "offensive cooldown"),
            1600, Slot.Offensive, KeyR, Recklessness);

        return events;
    }

    private static string SourceFixturePath() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "tests", "MaxDpsCompanion.Tests", "fixtures",
            "offensive-interrupt-warrior.jsonl"));

    private static string OutputFixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "offensive-interrupt-warrior.jsonl");

    /// <summary>
    /// Fixture regeneration (opt-in, no behavioural change): set
    /// <c>MDB_REGENERATE_FIXTURES=1</c> and run
    /// <c>dotnet test --filter Regenerate_Fixtures_When_Requested</c> to rewrite
    /// the checked-in source fixture with the exact output of the real recording
    /// builder. Returns immediately when the env var is absent.
    /// </summary>
    [Fact]
    public void Regenerate_Fixtures_When_Requested()
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

        Assert.Equal(3, result.Ticks);
        Assert.Equal(3, result.Sends);
        Assert.Equal(0, result.Errors);
        Assert.Equal(0, result.Mismatches);
        Assert.True(result.Verdicts >= 7, $"expected the recorded verdicts to be recomputed, got {result.Verdicts}");
        Assert.Equal(0, result.VerdictMismatches);
        Assert.Contains("interrupt (Dedicated)", result.Report);
        Assert.Contains("not interruptible", result.Report);
        Assert.Contains("0 mismatch(es)", result.Report);
    }

    [Fact]
    public void Offensive_Interrupt_Fixture_Replays_With_Zero_Verdict_Mismatches()
    {
        var output = OutputFixturePath();
        if (!File.Exists(output))
        {
            // Bootstrap: serialize the canonical recording once. From then on
            // the checked-in file is the evidence and this test validates it.
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
        Assert.True(result.Verdicts >= 7, $"fixture should carry offensive/interrupt verdicts, found {result.Verdicts}");
        Assert.Equal(0, result.VerdictMismatches);
        Assert.Contains("interrupt (Dedicated)", result.Report);
        Assert.Contains("0 mismatch(es)", result.Report);
    }
}
