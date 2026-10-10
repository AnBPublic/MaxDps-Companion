using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// R2 (v3/r2-sustain-cd) replay acceptance: the cooldown/reset self-sustain
/// sequence is produced by the REAL pipeline (wire frame -> decode -> tracker
/// -> scheduler -> telemetry record), checked in as
/// <c>solo-cooldown-reset-warrior.jsonl</c>, and must replay with 0 mismatches.
///
/// The sequence deliberately covers: a heal that fires, a tick where it is on
/// cooldown (distinct "waiting" hold, no verdict), the same spell coming back
/// ready (a dynamic reset), a GCD confirmation, and the same unchanged slot
/// firing long after the stale window.
/// </summary>
public class SoloCooldownResetReplayTests
{
    private const int ImpendingVictory = 202168;
    private static readonly KeyStroke KeyE = new(0x45, false, false, false);
    private static readonly KeyStroke KeyH = new(0x48, false, false, false);
    private static readonly bool[] AllSlots = [true, true, true, true, true, true, true, true];

    private static PolicyOptions Solo => new() { SoloEnabled = true };

    private static BridgeFrame Frame(int heartbeat, int hp, bool hasSelfHeal, bool onGcd = false)
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

        var frame = PixelProtocol.Decode(builder.Build(heartbeat, onGcd: onGcd));
        Assert.NotNull(frame);
        return frame!;
    }

    /// <summary>A candidate-less frame: only the context (HP + class/spec).</summary>
    private static BridgeFrame ContextOnly(int heartbeat, int hp)
    {
        var frame = PixelProtocol.Decode(new TestV5Frame()
            .Vitals(hp)
            .Cast(PlayerCastState.None)
            .Target(inMelee: true, casting: false, interruptible: false)
            .ClassSpec("WARRIOR", 1)
            .Build(heartbeat));
        Assert.NotNull(frame);
        return frame!;
    }

    /// <summary>The canonical sequence through the live pipeline (send before its tick).</summary>
    private static List<TelemetryEvent> CanonicalRecording()
    {
        var events = new List<TelemetryEvent>
        {
            TelemetryEvent.Session(0, "3.0.0", PixelProtocol.SupportedVersion, 10000, true, "start",
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
                TelemetryEvent.BuildPolicy(plan, true, combat, options));
        }

        // @1000: 50% HP, Solo ON, Impending Victory ready -> Use + send.
        var t1 = Tick(1000, Frame(1, 50, hasSelfHeal: true), Solo, "sending");
        scheduler.NoteSent(1000, Slot.SelfHeal, KeyH, ImpendingVictory);
        events.Add(TelemetryEvent.Sent(1000, "spell", Slot.SelfHeal, KeyH, 0, ImpendingVictory));
        events.Add(t1);

        // @1200: the heal is on cooldown (no candidate) and HP is still in the
        // sustain window -> distinct cooldown-wait hold, no verdict, no send.
        events.Add(Tick(1200, ContextOnly(2, 50), Solo, "waiting for self-heal cooldown"));

        // @1300: a reset makes the same heal ready again -> fires immediately.
        var t3 = Tick(1300, Frame(3, 50, hasSelfHeal: true), Solo, "sending");
        scheduler.NoteSent(1300, Slot.SelfHeal, KeyH, ImpendingVictory);
        events.Add(TelemetryEvent.Sent(1300, "spell", Slot.SelfHeal, KeyH, 0, ImpendingVictory));
        events.Add(t3);

        // @1350: the heal's GCD appears and confirms the press.
        events.Add(Tick(1350, Frame(4, 55, hasSelfHeal: true, onGcd: true), Solo, "waiting for GCD"));

        // @4000: the SAME spell is still suggested with unchanged slot content,
        // far past the stale window; it must fire again (no stale demotion).
        var t5 = Tick(4000, Frame(5, 50, hasSelfHeal: true), Solo, "sending");
        scheduler.NoteSent(4000, Slot.SelfHeal, KeyH, ImpendingVictory);
        events.Add(TelemetryEvent.Sent(4000, "spell", Slot.SelfHeal, KeyH, 0, ImpendingVictory));
        events.Add(t5);

        return events;
    }

    private static string SourceFixturePath() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "tests", "MaxDpsCompanion.Tests", "fixtures",
            "solo-cooldown-reset-warrior.jsonl"));

    private static string OutputFixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "solo-cooldown-reset-warrior.jsonl");

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
    public void Canonical_Cooldown_Reset_Recording_Recomputes_Every_Verdict()
    {
        var result = ReplayRunner.Run(CanonicalRecording());

        Assert.Equal(5, result.Ticks);
        Assert.Equal(3, result.Sends);
        Assert.Equal(0, result.Mismatches);
        Assert.True(result.Verdicts >= 3, $"expected replayed self-heal verdicts, got {result.Verdicts}");
        Assert.Equal(0, result.VerdictMismatches);
        Assert.Contains("SelfSustain", result.Report);
    }

    [Fact]
    public void Cooldown_Reset_Fixture_Replays_With_Zero_Verdict_Mismatches()
    {
        var output = OutputFixturePath();
        if (!File.Exists(output))
        {
            var source = SourceFixturePath();
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            using (var writer = new StreamWriter(source, append: false, new System.Text.UTF8Encoding(false)))
            {
                foreach (var evt in CanonicalRecording())
                    writer.WriteLine(System.Text.Encoding.UTF8.GetString(TelemetryJson.Serialize(evt)));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.Copy(source, output, overwrite: true);
        }

        var (events, badLines) = TelemetryReader.Read(output);
        var result = ReplayRunner.Run(events, badLines, source: output);

        Assert.Equal(0, badLines);
        Assert.Equal(0, result.Errors);
        Assert.Equal(0, result.Mismatches);
        Assert.True(result.Verdicts >= 3, $"fixture should carry self-heal verdicts, found {result.Verdicts}");
        Assert.Equal(0, result.VerdictMismatches);
        Assert.Contains("SelfSustain", result.Report);
    }
}
