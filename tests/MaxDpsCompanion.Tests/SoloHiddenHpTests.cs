using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// v3.0.0 C5 scenarios: when the game hides plain HP, the Ext2 curve drives
/// Solo self-sustain. The canonical hidden-HP recording (Warrior Arms, curve
/// band 6 = 40%, Solo ON) exercises the real wire -> decode -> tracker ->
/// scheduler -> telemetry pipeline and the checked-in fixture drives the real
/// <c>--replay</c>. A catalog-wide property test pins the same gates for every
/// class/spec.
/// </summary>
public class SoloHiddenHpTests
{
    private static readonly KeyStroke KeyE = new(0x45, false, false, false);
    private static readonly KeyStroke KeyH = new(0x48, false, false, false);
    private static readonly KeyStroke KeyG = new(0x47, false, false, false);
    private static readonly bool[] AllSlots = [true, true, true, true, true, true, true, true];

    private static PolicyOptions Solo => new() { SoloEnabled = true };

    private static BridgeFrame Frame(
        string className, int specId, int? plainHp, int curveBand,
        bool hasSelfHeal, int selfHealSpell, int alternateSpell = 0,
        TriState selfHealRange = TriState.Yes, IReadOnlyList<int>? extras = null)
    {
        var b = new TestV5Frame()
            .Cast(PlayerCastState.None)
            .Target(inMelee: true, casting: false, interruptible: false)
            .Ranges((Slot.Main, TriState.Yes), (Slot.SelfHeal, selfHealRange))
            .ClassSpec(className, specId);
        if (plainHp is { } hp) b.Vitals(hp);
        if (curveBand >= 0) b.Ext2(hpCurveActive: true, curveBand: curveBand);
        else if (alternateSpell > 0) b.Ext2(hpCurveActive: false, curveBand: 0);
        if (hasSelfHeal)
        {
            b.Slot(Slot.Main, KeyE.VirtualKey).SpellId(Slot.Main, 12294);
            b.Slot(Slot.SelfHeal, KeyH.VirtualKey).SpellId(Slot.SelfHeal, selfHealSpell);
        }
        if (alternateSpell > 0) b.SelfHeal2(KeyG, alternateSpell, selfHealRange);
        var cells = curveBand >= 0 || alternateSpell > 0 ? b.BuildExt2(heartbeat: 7) : b.Build(heartbeat: 7);
        return PixelProtocol.Decode(cells)!;
    }

    private static SchedulePlan Advance(
        BridgeFrame frame, PolicyOptions? options, ActionScheduler? scheduler = null,
        CandidateTracker? tracker = null, long now = 1000)
    {
        scheduler ??= new ActionScheduler();
        tracker ??= new CandidateTracker();
        tracker.Update(frame, now);
        return scheduler.Advance(new ScheduleInput
        {
            Frame = frame,
            OutOfCombatPermitted = true,
            Candidates = tracker.Snapshot(AllSlots),
            NowMs = now,
            MinKeyIntervalMs = 120,
            StaleAfterMs = 1500,
            HeartbeatTimeoutMs = 500,
            RepeatSuppressMs = 900,
            Context = CombatContext.FromFrame(frame),
            Options = options,
            Catalog = AbilityCatalog.Default,
            CollectPolicyVerdicts = true,
        });
    }

    // ---- named scenarios -------------------------------------------------

    [Fact]
    public void Warrior_HiddenHp_Curve40_Solo_Uses_ImpendingVictory()
    {
        // hidden plain HP + curve band 6 (40%) + Solo ON: the companion still
        // has a usable HP source and fires the self-sustain.
        var plan = Advance(Frame("WARRIOR", 1, plainHp: null, curveBand: 6,
            hasSelfHeal: true, selfHealSpell: 202168), Solo);

        Assert.Equal(Slot.SelfHeal, plan.Selected);
        Assert.Equal(202168, plan.Actions[0].SpellId);
        Assert.Equal(KeyH, plan.Actions[0].Stroke);
        var verdict = Assert.Single(plan.Verdicts, v => v.Slot == Slot.SelfHeal);
        Assert.Equal(PolicyVerdict.Use, verdict.Verdict);
        Assert.Contains("~40% (curve)", verdict.Reason);
    }

    [Fact]
    public void Warrior_HiddenHp_BarVariant_34428_Uses_VictoryRush()
    {
        // The player's bar holds Victory Rush instead of Impending Victory: the
        // bridge encodes 34428 and the companion fires that variant.
        var plan = Advance(Frame("WARRIOR", 1, plainHp: null, curveBand: 6,
            hasSelfHeal: true, selfHealSpell: 34428), Solo);

        Assert.Equal(Slot.SelfHeal, plan.Selected);
        Assert.Equal(34428, plan.Actions[0].SpellId);
    }

    [Fact]
    public void HiddenHp_NinetyPercent_Does_Not_Sustain()
    {
        var plan = Advance(Frame("WARRIOR", 1, plainHp: null, curveBand: 14, // 93%
            hasSelfHeal: true, selfHealSpell: 202168), Solo);

        // The main rotation still fires; self-sustain must not.
        Assert.DoesNotContain(plan.Actions, a => a.Slot == Slot.SelfHeal);
    }

    // ---- catalog-wide property gate --------------------------------------

    [Fact]
    public void Every_Spec_SelfHeal_Gate_Behaves_At_Curve_40_And_93()
    {
        foreach (var className in AbilityCatalog.ClassOrder.Skip(1))
        {
            foreach (var spec in AbilityCatalog.SpecOrder[className].Skip(1))
            {
                var heals = AbilityCatalog.Default.Extras(className, spec, AbilityCategory.SelfHeal);
                if (heals.Length == 0) continue; // documented nones
                var specId = AbilityCatalog.SpecId(className, spec);

                // First entry without an HP ceiling is the expected sustain.
                var first = heals.Select(id => AbilityCatalog.Default.TryGet(id)!).First(a => a.UseBelowHpPct is null);

                var at40 = Advance(Frame(className, specId, plainHp: null, curveBand: 6,
                    hasSelfHeal: true, selfHealSpell: first.SpellId), Solo);
                Assert.True(at40.Selected == Slot.SelfHeal && at40.Actions[0].SpellId == first.SpellId,
                    $"{className}/{spec}: curve 40% should Use {first.SpellId} ({first.Name})");

                var at93 = Advance(Frame(className, specId, plainHp: null, curveBand: 14,
                    hasSelfHeal: true, selfHealSpell: first.SpellId), Solo);
                Assert.DoesNotContain(at93.Actions, a => a.Slot == Slot.SelfHeal);

                var soloOff = Advance(Frame(className, specId, plainHp: 50, curveBand: -1,
                    hasSelfHeal: true, selfHealSpell: first.SpellId), new PolicyOptions { SoloEnabled = false });
                Assert.DoesNotContain(soloOff.Actions, a => a.Slot == Slot.SelfHeal);

                var policyOff = Advance(Frame(className, specId, plainHp: 50, curveBand: -1,
                    hasSelfHeal: true, selfHealSpell: first.SpellId), options: null);
                Assert.DoesNotContain(policyOff.Actions, a => a.Slot == Slot.SelfHeal);
            }
        }
    }

    // ---- canonical fixture ----------------------------------------------

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

        // Tick 1 @1000: hidden HP, curve 40%, Solo ON -> Impending Victory.
        var tick1 = Tick(1000, Frame("WARRIOR", 1, null, 6, true, 202168), Solo, "sending");
        scheduler.NoteSent(1000, Slot.SelfHeal, KeyH, 202168);
        events.Add(TelemetryEvent.Sent(1000, "spell", Slot.SelfHeal, KeyH, 0, 202168));
        events.Add(tick1);

        // Tick 2 @1050: the heal started a GCD.
        events.Add(Tick(1050, Frame("WARRIOR", 1, null, 6, true, 202168), Solo, "waiting for GCD"));

        // Tick 3 @1400: bar variant Victory Rush, hidden HP still at 40%.
        var tick3 = Tick(1400, Frame("WARRIOR", 1, null, 6, true, 34428), Solo, "sending");
        scheduler.NoteSent(1400, Slot.SelfHeal, KeyH, 34428);
        events.Add(TelemetryEvent.Sent(1400, "spell", Slot.SelfHeal, KeyH, 300, 34428));
        events.Add(tick3);

        // Tick 4 @1600: Solo OFF -> hold.
        events.Add(Tick(1600, Frame("WARRIOR", 1, null, 6, true, 202168), new PolicyOptions { SoloEnabled = false }, "holding"));

        // Tick 5 @1700: 93% -> conserve.
        events.Add(Tick(1700, Frame("WARRIOR", 1, null, 14, true, 202168), Solo, "conserving"));

        return events;
    }

    private const string FixtureName = "solo-hidden-hp-warrior.jsonl";

    private static string SourceFixturePath() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "tests", "MaxDpsCompanion.Tests", "fixtures", FixtureName));

    private static string OutputFixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", FixtureName);

    [Fact]
    public void Regenerate_Fixture_When_Requested()
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
        Assert.Equal(2, result.Sends);
        Assert.Equal(0, result.Mismatches);
        Assert.True(result.Verdicts >= 4, $"verdicts={result.Verdicts} mismatches={result.VerdictMismatches}");
        Assert.Equal(0, result.VerdictMismatches);
        Assert.Contains("(curve)", result.Report);
    }

    [Fact]
    public void Hidden_Hp_Fixture_Replays_With_Zero_Verdict_Mismatches()
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
            File.Copy(source, output, overwrite: true);
        }

        var (events, badLines) = TelemetryReader.Read(output);
        var result = ReplayRunner.Run(events, badLines, source: output);

        Assert.Equal(0, badLines);
        Assert.Equal(0, result.Errors);
        Assert.Equal(0, result.Mismatches);
        Assert.True(result.Verdicts >= 4);
        Assert.Equal(0, result.VerdictMismatches);
        Assert.Contains("(curve)", result.Report);
    }
}
