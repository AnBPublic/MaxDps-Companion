using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Canonical Solo / self-sustain replay acceptance. The recording is produced
/// by the REAL pipeline (wire frame -> decode -> tracker -> scheduler ->
/// telemetry record), the checked-in fixture is the same sequence serialized,
/// and the replay must re-derive every policy verdict with zero mismatches.
///
/// The fixture file drives the `--replay=&lt;file&gt;` CLI diagnostic; this test
/// bootstraps it once (writing it next to the other fixtures) if it is absent,
/// then always validates the file that is used as evidence.
/// </summary>
public class SelfSustainReplayTests
{
    private const int ImpendingVictory = 202168;
    private static readonly KeyStroke KeyE = new(0x45, false, false, false);
    private static readonly KeyStroke KeyH = new(0x48, false, false, false);
    private static readonly bool[] AllSlots = [true, true, true, true, true, true, true, true];

    private static PolicyOptions Solo => new() { SoloEnabled = true };

    private static BridgeFrame Frame(int heartbeat, int hp, bool hasSelfHeal, bool hasTarget = true, bool onGcd = false)
    {
        var builder = new TestV5Frame()
            .Vitals(hp)
            .Cast(PlayerCastState.None)
            .Target(inMelee: true, casting: false, interruptible: false)
            .Ranges((Slot.Main, TriState.Yes), (Slot.SelfHeal, TriState.Yes))
            .ClassSpec("WARRIOR", 1)
            .Slot(Slot.Main, KeyE.VirtualKey)
            .SpellId(Slot.Main, 12294);
        if (hasSelfHeal) builder.Slot(Slot.SelfHeal, KeyH.VirtualKey).SpellId(Slot.SelfHeal, ImpendingVictory);

        var frame = PixelProtocol.Decode(builder.Build(heartbeat, onGcd: onGcd, hasTarget: hasTarget));
        Assert.NotNull(frame);
        return frame!;
    }

    /// <summary>
    /// The canonical sequence, built through the live pipeline exactly as the
    /// engine would record it (send events precede their tick event).
    /// </summary>
    private static List<TelemetryEvent> CanonicalRecording()
    {
        var events = new List<TelemetryEvent>
        {
            TelemetryEvent.Session(0, "2.2.0", PixelProtocol.SupportedVersion, 10000, true, "start",
                AbilityCatalog.CatalogVersion),
        };
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();

        TelemetryEvent Tick(long now, BridgeFrame frame, PolicyOptions options, string note)
        {
            tracker.Update(frame, now);
            var combat = CombatContext.FromFrame(frame);
            var plan = scheduler.Advance(new ScheduleInput
            {
                Frame = frame,
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
                TelemetryEvent.BuildPolicy(plan, true, combat, options));
        }

        // Tick 1 @1000: Warrior at 50% HP, Solo ON, Impending Victory ready.
        // The live engine writes the send BEFORE the tick record.
        var tick1 = Tick(1000, Frame(1, 50, hasSelfHeal: true), Solo, "sending");
        scheduler.NoteSent(1000, Slot.SelfHeal, KeyH, ImpendingVictory);
        events.Add(TelemetryEvent.Sent(1000, "spell", Slot.SelfHeal, KeyH, 0, ImpendingVictory));
        events.Add(tick1);

        // Tick 2 @1050: the heal started a GCD; everything waits.
        events.Add(Tick(1050, Frame(2, 55, hasSelfHeal: true, onGcd: true), Solo, "waiting for GCD"));

        // Tick 3 @1400: the heal is on cooldown (slot empty) and HP recovered;
        // the normal rotation resumes.
        events.Add(Tick(1400, Frame(3, 80, hasSelfHeal: false), Solo, "sending"));

        // Tick 4 @1600: low HP again, but Solo is OFF — no independent press.
        events.Add(Tick(1600, Frame(4, 45, hasSelfHeal: true), new PolicyOptions { SoloEnabled = false }, "holding"));

        // Tick 5 @1700: Solo ON at emergency HP — the heal is an emergency
        // survival action.
        events.Add(Tick(1700, Frame(5, 30, hasSelfHeal: true), Solo, "sending"));

        return events;
    }

    private static string SourceFixturePath() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "tests", "MaxDpsCompanion.Tests", "fixtures",
            "solo-warrior-selfheal.jsonl"));

    private static string OutputFixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "solo-warrior-selfheal.jsonl");

    /// <summary>
    /// Fixture regeneration (opt-in, no behavioural change): set
    /// <c>MDB_REGENERATE_FIXTURES=1</c> and run
    /// <c>dotnet test --filter Regenerate_Fixtures_When_Requested</c> to rewrite
    /// the checked-in source fixture with the exact output of the real recording
    /// builder. Returns immediately when the env var is absent, so a normal test
    /// run never touches the repo.
    /// </summary>
    [Fact]
    public void Regenerate_Fixtures_When_Requested()
    {
        if (Environment.GetEnvironmentVariable("MDB_REGENERATE_FIXTURES") != "1") return;
        var source = SourceFixturePath();
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        using var writer = new StreamWriter(source, append: false, new System.Text.UTF8Encoding(false));
        foreach (var evt in CanonicalRecording())
            writer.WriteLine(System.Text.Encoding.UTF8.GetString(TelemetryJson.Serialize(evt)));
    }

    [Fact]
    public void Canonical_InMemory_Recording_Recomputes_Every_Verdict()
    {
        var result = ReplayRunner.Run(CanonicalRecording());

        Assert.Equal(5, result.Ticks);
        Assert.Equal(1, result.Sends);
        Assert.Equal(0, result.Mismatches);
        Assert.True(result.Verdicts >= 8, $"expected the recorded self-sustain verdicts to be recomputed, got {result.Verdicts}");
        Assert.Equal(0, result.VerdictMismatches);
        Assert.Contains("SelfSustain", result.Report);
        Assert.Contains("0 mismatch(es)", result.Report);
    }

    [Fact]
    public void Solo_SelfSustain_Fixture_Replays_With_Zero_Verdict_Mismatches()
    {
        var output = OutputFixturePath();
        if (!File.Exists(output))
        {
            // Bootstrap: serialize the canonical recording once. From then on
            // the checked-in file is the evidence and this test validates it.
            var source = SourceFixturePath();
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            using (var writer = new StreamWriter(source, append: false, new System.Text.UTF8Encoding(false)))
            {
                foreach (var evt in CanonicalRecording())
                    writer.WriteLine(System.Text.Encoding.UTF8.GetString(TelemetryJson.Serialize(evt)));
            }
            File.Copy(source, output, overwrite: true);
        }

        var (events, badLines) = TelemetryReader.Read(output);
        var result = ReplayRunner.Run(events, badLines, source: output);

        Assert.Equal(0, badLines);
        Assert.Equal(0, result.Errors);
        Assert.Equal(0, result.Mismatches);
        Assert.True(result.Verdicts >= 8, $"fixture should carry self-sustain verdicts, found {result.Verdicts}");
        Assert.Equal(0, result.VerdictMismatches);
        Assert.Contains("SelfSustain", result.Report);
        Assert.Contains("solo: HP 50%", result.Report);
        Assert.Contains("solo mode off", result.Report);
    }
}
