using System.Text;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Canonical v2.3 defensive-intelligence replay acceptance. The recording is
/// produced by the REAL pipeline (v6 wire frame -> decode -> tracker ->
/// scheduler with options/catalog -> telemetry record), the checked-in fixture
/// is the same sequence serialized, and the replay must re-derive every
/// defensive policy verdict with zero mismatches.
///
/// It also pins the legacy contract: a policy record whose "du" field is
/// absent predates v2.3 urgency gating, so its verdicts are NOT recomputed
/// (0 verdicts, 0 mismatches) and the report says "legacy pre-v2.3".
/// </summary>
public class DefensiveReplayTests
{
    private static readonly KeyStroke KeyE = new(0x45, false, false, false);
    private static readonly KeyStroke KeyR = new(0x52, false, false, false);
    private static readonly bool[] AllSlots = [true, true, true, true, true, true, true, true];

    /// <summary>
    /// A v6 frame carrying the main rotation plus one Defensive candidate with
    /// the requested MaxDps urgency / catalog-source flag. When
    /// <paramref name="hpValid"/> is false the vitals cell keeps its UNKNOWN
    /// default (the decode yields HpPct = -1).
    /// </summary>
    private static BridgeFrame Frame(
        int heartbeat,
        int hp,
        int defensiveSpell,
        DefensiveUrgency urgency,
        bool catalogSource = false,
        bool onGcd = false,
        bool hpValid = true,
        bool hasTarget = true)
    {
        var builder = new TestV5Frame()
            .Slot(Slot.Main, KeyE.VirtualKey)
            .SpellId(Slot.Main, 12294)
            .Urgency(urgency, catalogSource: catalogSource);
        if (defensiveSpell > 0)
            builder.Slot(Slot.Defensive, KeyR.VirtualKey).SpellId(Slot.Defensive, defensiveSpell);
        if (hpValid) builder.Vitals(hp);

        var frame = PixelProtocol.Decode(builder.Build(heartbeat, onGcd: onGcd, hasTarget: hasTarget));
        Assert.NotNull(frame);
        return frame!;
    }

    private static List<TelemetryEvent> CanonicalRecording() => CanonicalRecording(out _);

    /// <summary>
    /// The canonical sequence, built through the live pipeline exactly as the
    /// engine would record it (a send event precedes its own tick event; the
    /// scheduler's NoteSent runs after the plan was computed).
    /// </summary>
    private static List<TelemetryEvent> CanonicalRecording(out ActionScheduler scheduler)
    {
        var events = new List<TelemetryEvent>
        {
            TelemetryEvent.Session(0, "2.3.0", PixelProtocol.SupportedVersion, 10000, true, "start",
                AbilityCatalog.CatalogVersion),
        };
        var sched = new ActionScheduler();
        var tracker = new CandidateTracker();

        TelemetryEvent Tick(long now, BridgeFrame frame, PolicyOptions options, string note)
        {
            tracker.Update(frame, now);
            var combat = CombatContext.FromFrame(frame);
            var plan = sched.Advance(new ScheduleInput
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

        // The tick is computed BEFORE the send so the recorded verdicts reflect
        // the pre-send policy memory; the send event still precedes the tick in
        // the list, matching ReplayRunner's ordering contract.
        void Send(TelemetryEvent tick, long now, Slot slot, KeyStroke key, int spell)
        {
            sched.NoteSent(now, slot, key, spell);
            events.Add(TelemetryEvent.Sent(now, "spell", slot, key, 0, spell));
            events.Add(tick);
        }

        // 1 @1000: full HP (White, MaxDps renders no glow) — the major holds,
        //   the main rotation fires.
        events.Add(Tick(1000, Frame(1, 100, 871, DefensiveUrgency.White), PolicyOptions.Standard, "white urgency"));

        // 2 @1200: Orange + a MaxDps-recommended short defensive (Feint) — use.
        Send(Tick(1200, Frame(2, 45, 1966, DefensiveUrgency.Orange), PolicyOptions.Standard, "using short defensive"),
            1200, Slot.Defensive, KeyR, 1966);

        // 3 @1500: Orange + a catalog gap-filled major (Shield Wall, no MaxDps
        //   recommendation) — hold until Red.
        events.Add(Tick(1500, Frame(3, 45, 871, DefensiveUrgency.Orange, catalogSource: true),
            PolicyOptions.Standard, "holding catalog major"));

        // 4 @2000: emergency HP + Red — the catalog major fires regardless.
        Send(Tick(2000, Frame(4, 30, 871, DefensiveUrgency.Red, catalogSource: true),
                PolicyOptions.Standard, "emergency major"),
            2000, Slot.Defensive, KeyR, 871);

        // 5 @2500: the user turned Shield Wall OFF — absolute skip.
        var off = new PolicyOptions
        {
            Abilities = AbilityPolicy.Default.With(871, enabled: false, defaultEnabled: true),
        };
        events.Add(Tick(2500, Frame(5, 30, 871, DefensiveUrgency.Red), off, "user disabled"));

        // 6 @3000: no HP reading (Unknown urgency) — a major still holds.
        events.Add(Tick(3000, Frame(6, 0, 871, DefensiveUrgency.Unknown, hpValid: false),
            PolicyOptions.Standard, "urgency unknown"));

        // 7 @3500: emergency + Red — Ignore Pain (on-GCD) fires ...
        Send(Tick(3500, Frame(7, 30, 190456, DefensiveUrgency.Red), PolicyOptions.Standard, "emergency absorb"),
            3500, Slot.Defensive, KeyR, 190456);

        // 8 @4100: no GCD ever appeared — the press failed (rejected) and the
        //   main rotation takes the tick.
        events.Add(Tick(4100, Frame(8, 30, 190456, DefensiveUrgency.Red), PolicyOptions.Standard, "failed press"));

        scheduler = sched;
        return events;
    }

    private static string SourceFixturePath() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "tests", "MaxDpsCompanion.Tests", "fixtures",
            "defensive-warrior-urgency.jsonl"));

    private static string OutputFixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "defensive-warrior-urgency.jsonl");

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
        using var writer = new StreamWriter(source, append: false, new UTF8Encoding(false));
        foreach (var evt in CanonicalRecording())
            writer.WriteLine(Encoding.UTF8.GetString(TelemetryJson.Serialize(evt)));
    }

    /// <summary>
    /// Drops the v2.3 policy keys, modelling a pre-v2.3 recording. Nulling the
    /// record fields makes the serializer OMIT them, exactly as an old build
    /// would have written the line.
    /// </summary>
    private static TelemetryEvent StripV23PolicyFields(TelemetryEvent evt) =>
        evt.Policy is { } policy
            ? evt with
            {
                Policy = policy with
                {
                    DefensiveUrgency = null,
                    StaggerUrgency = null,
                    DefensiveCatalogSource = null,
                    Options = policy.Options is { } opts
                        ? opts with { AbilitiesOn = null, AbilitiesOff = null }
                        : null,
                },
            }
            : evt;

    [Fact]
    public void Canonical_InMemory_Recording_Recomputes_Every_Verdict()
    {
        var result = ReplayRunner.Run(CanonicalRecording());

        Assert.Equal(8, result.Ticks);
        Assert.Equal(3, result.Sends);
        Assert.Equal(0, result.Errors);
        Assert.Equal(0, result.Mismatches);
        Assert.True(result.Verdicts >= 8, $"expected the recorded defensive verdicts to be recomputed, got {result.Verdicts}");
        Assert.Equal(0, result.VerdictMismatches);
        Assert.Contains("0 mismatch(es)", result.Report);
    }

    [Fact]
    public void Canonical_Recording_Rejects_A_Failed_Defensive_Press()
    {
        _ = CanonicalRecording(out var scheduler);

        Assert.True(scheduler.RejectionsDetected >= 1,
            "the on-GCD Ignore Pain press never produced a GCD and must be counted as a failed action");
    }

    [Fact]
    public void Replay_Of_A_Legacy_Policy_Record_Skips_Verdicts()
    {
        var canonical = CanonicalRecording();
        Assert.Contains(canonical, e => e.Policy?.DefensiveUrgency == "Red");
        Assert.Contains(canonical, e => e.Policy?.DefensiveCatalogSource == true);
        Assert.Contains(canonical, e => e.Policy?.Options?.AbilitiesOff == "871");

        // Serialize -> strip the v2.3 keys -> deserialize: the result has the
        // exact shape of a pre-v2.3 recording.
        var legacy = canonical
            .Select(StripV23PolicyFields)
            .Select(evt => TelemetryJson.Deserialize(Encoding.UTF8.GetString(TelemetryJson.Serialize(evt)))!)
            .ToList();

        Assert.All(legacy.Where(e => e.Policy is { Verdicts.Length: > 0 }), e =>
        {
            Assert.Null(e.Policy!.DefensiveUrgency);
            Assert.Null(e.Policy.StaggerUrgency);
            Assert.Null(e.Policy.DefensiveCatalogSource);
            Assert.Null(e.Policy.Options!.AbilitiesOn);
            Assert.Null(e.Policy.Options.AbilitiesOff);
        });

        var result = ReplayRunner.Run(legacy);

        Assert.Equal(0, result.Verdicts);
        Assert.Equal(0, result.VerdictMismatches);
        Assert.Contains("legacy pre-v2.3", result.Report);
    }

    [Fact]
    public void Defensive_Fixture_Replays_With_Zero_Verdict_Mismatches()
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
        Assert.True(result.Verdicts >= 8, $"fixture should carry defensive verdicts, found {result.Verdicts}");
        Assert.Equal(0, result.VerdictMismatches);
        Assert.Contains("0 mismatch(es)", result.Report);
    }
}
