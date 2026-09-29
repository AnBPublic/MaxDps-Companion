using System.Globalization;
using System.Text;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Telemetry format v1 contract: every event kind round-trips through JSONL,
/// null sections are omitted (bounded ring size), the line shape is pinned so
/// a future change cannot silently break old exports, and the reader tolerates
/// corrupt lines instead of aborting a replay.
/// </summary>
public class TelemetrySerializationTests
{
    private static readonly KeyStroke StrokeE = new(0x45, false, false, false);
    private static readonly KeyStroke StrokeTwo = new(0x32, false, false, false);
    private static readonly KeyStroke StrokeF = new(0x46, false, false, false);

    private static BridgeFrame Frame(params (Slot Slot, KeyStroke? Stroke)[] slots)
    {
        var array = new KeyStroke?[PixelProtocol.SlotCount];
        foreach (var (slot, stroke) in slots) array[(int)slot] = stroke;
        return new BridgeFrame
        {
            State = BridgeState.Active,
            Heartbeat = 7,
            Version = PixelProtocol.SupportedVersion,
            StatusFlags = PixelProtocol.StatusFlagInCombat | PixelProtocol.StatusFlagHasTarget,
            Slots = array,
        };
    }

    private static ActionCandidate Candidate(
        Slot slot,
        KeyStroke? stroke,
        bool enabled = true,
        bool actionable = true,
        long firstSeenMs = 0,
        long lastChangedMs = 0,
        bool everPressed = false) =>
        new(slot, stroke ?? StrokeE, enabled, actionable, firstSeenMs, lastChangedMs, LastPressedMs: 0, everPressed);

    private static DecisionContext Context(long nowMs, params ActionCandidate[] candidates) => new()
    {
        InCombat = true,
        OnGcd = false,
        HasTarget = true,
        State = BridgeState.Active,
        NowMs = nowMs,
        StaleAfterMs = 1500,
        Candidates = candidates,
    };

    private static TelemetryEvent SampleTick()
    {
        var candidates = new[]
        {
            Candidate(Slot.Main, StrokeE, firstSeenMs: 8000, lastChangedMs: 9000, everPressed: true),
            Candidate(Slot.Offensive, StrokeTwo, firstSeenMs: 9000, lastChangedMs: 9500),
            Candidate(Slot.Trinket, StrokeF, enabled: false),
        };
        var context = Context(10_000, candidates);
        var decision = DecisionEngine.Evaluate(context);
        var frame = Frame((Slot.Main, StrokeE), (Slot.Offensive, StrokeTwo), (Slot.Trinket, StrokeF));
        return TelemetryEvent.Tick(10_000, frame, DecodeFault.None, context, decision, true, candidates, "sending", true);
    }

    private static string Json(TelemetryEvent evt) => Encoding.UTF8.GetString(TelemetryJson.Serialize(evt));

    private static byte[] Reserialized(string line) => TelemetryJson.Serialize(TelemetryJson.Deserialize(line)!);

    [Fact]
    public void Tick_RoundTrips_Every_Field()
    {
        var original = SampleTick();

        var loaded = TelemetryJson.Deserialize(Json(original));

        Assert.NotNull(loaded);
        Assert.Equal(original.Kind, loaded!.Kind);
        Assert.Equal(original.TMs, loaded.TMs);
        Assert.Equal(original.Proto, loaded.Proto);
        Assert.Equal(original.State, loaded.State);
        Assert.Equal(original.Heartbeat, loaded.Heartbeat);
        Assert.Equal(original.InCombat, loaded.InCombat);
        Assert.Equal(original.OnGcd, loaded.OnGcd);
        Assert.Equal(original.HasTarget, loaded.HasTarget);
        Assert.Equal(original.Link, loaded.Link);
        Assert.Equal(original.Note, loaded.Note);
        Assert.Equal(original.StaleAfterMs, loaded.StaleAfterMs);
        Assert.Equal(original.Slots!.Length, loaded.Slots!.Length);
        Assert.Equal(StrokeE, loaded.Slots![(int)Slot.Main]!.ToKeyStroke());
        Assert.Null(loaded.Slots[(int)Slot.Defensive]);
        Assert.Equal(3, loaded.Candidates!.Length);
        Assert.True(loaded.Candidates[0].EverPressed);
        Assert.False(loaded.Candidates[2].Enabled);
        Assert.Equal(DecisionReason.MainRotation, loaded.Decision!.Reason);
        Assert.Equal(Slot.Main, loaded.Decision.Selected);
        Assert.Equal(70, loaded.Decision.Confidence);
        Assert.Equal(new[] { Slot.Main, Slot.Offensive }, loaded.Decision.Order);
    }

    [Fact]
    public void Tick_Byte_Round_Trip_Is_Stable()
    {
        var line = Json(SampleTick());

        Assert.Equal(Encoding.UTF8.GetBytes(line), Reserialized(line));
    }

    [Fact]
    public void Sent_Line_Pins_Format_V1()
    {
        var evt = new TelemetryEvent
        {
            Kind = TelemetryKind.Send,
            Seq = 7,
            TMs = 1000,
            Utc = "2026-09-27T18:00:00.0000000Z",
            Send = new TelemetrySend
            {
                What = "spell",
                Slot = Slot.Main,
                Key = new TelemetryStroke { VirtualKey = 0x45 },
                IntervalMs = 121,
            },
        };

        Assert.Equal(
            "{\"fmt\":1,\"kind\":\"send\",\"seq\":7,\"tMs\":1000,\"utc\":\"2026-09-27T18:00:00.0000000Z\",\"send\":{\"what\":\"spell\",\"slot\":\"Main\",\"key\":{\"vk\":69,\"sh\":false,\"ct\":false,\"alt\":false},\"intervalMs\":121}}",
            Json(evt));
    }

    [Fact]
    public void Session_And_Link_Round_Trip()
    {
        var session = TelemetryEvent.Session(0, "1.5.0", PixelProtocol.SupportedVersion, 10_000, recording: true);
        var sessionLine = Json(session);
        Assert.Contains("\"kind\":\"session\"", sessionLine);
        Assert.Equal("start", TelemetryJson.Deserialize(sessionLine)!.Note);

        // v2.1: the catalog revision is stamped so replay can warn on skew.
        var stamped = TelemetryEvent.Session(0, "2.1.0", PixelProtocol.SupportedVersion, 10_000,
            recording: true, catalogVersion: 2);
        Assert.Equal(2, TelemetryJson.Deserialize(Json(stamped))!.CatalogVersion);

        var link = TelemetryEvent.LinkEvent(50_000, false, "no pixel block", DecodeFault.Checksum);
        var linkLine = Json(link);
        var loaded = TelemetryJson.Deserialize(linkLine)!;
        Assert.False(loaded.Link);
        Assert.Equal("Checksum", loaded.Fault);
        Assert.Equal("no pixel block", loaded.Note);
    }

    [Fact]
    public void Null_Sections_Are_Omitted()
    {
        var json = Json(new TelemetryEvent { Kind = TelemetryKind.Tick, TMs = 5 });

        Assert.DoesNotContain("\"proto\"", json);
        Assert.DoesNotContain("\"slots\"", json);
        Assert.DoesNotContain("\"cands\"", json);
        Assert.DoesNotContain("\"dec\"", json);
        Assert.DoesNotContain("\"send\"", json);
        Assert.DoesNotContain("null", json);
        Assert.EndsWith("}", json);
    }

    [Fact]
    public void Serialization_Is_Culture_Invariant()
    {
        var evt = SampleTick();
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var german = TelemetryJson.Serialize(evt);
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var invariant = TelemetryJson.Serialize(evt);

            Assert.Equal(invariant, german);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Policy_Record_RoundTrips_The_Explainability_Context()
    {
        var range = new TriState[PixelProtocol.SlotCount];
        range[(int)Slot.Main] = TriState.Yes;
        range[(int)Slot.Defensive] = TriState.No;
        var buffs = new TriState[PixelProtocol.SlotCount];
        buffs[(int)Slot.Defensive] = TriState.Yes;
        var combat = new CombatContext
        {
            HpValid = true,
            HpPct = 55,
            Cast = PlayerCastState.Casting,
            TargetCasting = TriState.Yes,
            TargetCastInterruptible = TriState.No,
            TargetInMelee = TriState.Yes,
            SlotRange = range,
            SlotBuffActive = buffs,
            ContextValid = true,
        };
        var plan = SchedulePlan.Hold(ScheduleReason.CastHold, 0, 1, 0, "player cast in progress") with
        {
            Verdicts = [new PolicyVerdictEntry(Slot.Main, 185358, PolicyVerdict.Hold, "player cast in progress")],
        };
        var options = new PolicyOptions { SoloEnabled = true };

        var line = Json(new TelemetryEvent
        {
            Kind = TelemetryKind.Tick,
            TMs = 10,
            Policy = TelemetryEvent.BuildPolicy(plan, true, combat, options),
        });
        var loaded = TelemetryJson.Deserialize(line)!.Policy!;

        Assert.Equal("CastHold", loaded.Reason);
        Assert.Equal("player cast in progress", loaded.Detail);
        Assert.Equal("Casting", loaded.Cast);
        Assert.Equal("Yes", loaded.TargetCast);
        Assert.Equal("No", loaded.TargetInterruptible);
        Assert.True(loaded.ContextValid);
        Assert.Equal("IUOUUUUU", loaded.Range);
        Assert.Equal("00100000", loaded.Buffs);
        var recorded = loaded.Options!;
        Assert.True(recorded.Solo);
        Assert.Equal(35, recorded.EmergencyHpPct);
        var verdicts = loaded.Verdicts!;
        Assert.Single(verdicts);
        Assert.Equal(185358, verdicts[0].SpellId);
        Assert.Equal("Hold", verdicts[0].Verdict);
    }

    [Fact]
    public void Send_Spell_Id_Is_Omitted_When_Unknown_And_Present_When_Known()
    {
        Assert.DoesNotContain("\"sp\"", Json(TelemetryEvent.Sent(1, "spell", Slot.Main, StrokeE, 0)));
        var known = Json(TelemetryEvent.Sent(1, "spell", Slot.Main, StrokeE, 0, 185358));
        Assert.Contains("\"sp\":185358", known);
    }

    [Fact]
    public void Reader_Counts_Corrupt_And_Foreign_Lines()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mdb-tel-reader-{Guid.NewGuid():N}.jsonl");
        try
        {
            File.WriteAllLines(path,
            [
                Json(new TelemetryEvent { Kind = TelemetryKind.Tick, TMs = 1, Seq = 1 }),
                "this is not json",
                "",
                "{\"fmt\":2,\"kind\":\"tick\"}", // wrong format version
                "{\"fmt\":1}",                   // missing kind
                Json(new TelemetryEvent { Kind = TelemetryKind.Tick, TMs = 2, Seq = 2 }),
            ]);

            var (events, badLines) = TelemetryReader.Read(path);

            Assert.Equal(2, events.Count);
            Assert.Equal(3, badLines);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ----- v2.3.0 defensive urgency + per-ability overrides ----------------

    private static TelemetryEvent SampleV23PolicyEvent()
    {
        var combat = new CombatContext
        {
            HpValid = true,
            HpPct = 30,
            ContextValid = true,
            DefensiveUrgency = DefensiveUrgency.Red,
            StaggerUrgency = DefensiveUrgency.Orange,
            DefensiveCatalogSource = true,
        };
        var options = new PolicyOptions
        {
            Abilities = AbilityPolicy.Default
                .With(871, enabled: false, defaultEnabled: true)
                .With(1856, enabled: true, defaultEnabled: false),
        };
        var plan = SchedulePlan.Hold(ScheduleReason.PolicyHold, 0, 0, 0, "mixed verdicts") with
        {
            Verdicts =
            [
                new PolicyVerdictEntry(Slot.Defensive, 871, PolicyVerdict.Skip, "user policy disabled"),
                new PolicyVerdictEntry(Slot.Main, 12294, PolicyVerdict.Use, "MaxDps main candidate; actionable"),
            ],
        };
        return new TelemetryEvent
        {
            Kind = TelemetryKind.Tick,
            TMs = 10,
            Policy = TelemetryEvent.BuildPolicy(plan, true, combat, options),
        };
    }

    [Fact]
    public void Policy_Record_Serializes_V23_Urgency_Source_And_Overrides()
    {
        var json = Json(SampleV23PolicyEvent());

        Assert.Contains("\"du\":\"Red\"", json);
        Assert.Contains("\"dsu\":\"Orange\"", json);
        Assert.Contains("\"dsrc\":true", json);
        Assert.Contains("\"on\":\"1856\"", json);
        Assert.Contains("\"off\":\"871\"", json);

        var loaded = TelemetryJson.Deserialize(json)!.Policy!;
        Assert.Equal("Red", loaded.DefensiveUrgency);
        Assert.Equal("Orange", loaded.StaggerUrgency);
        Assert.Equal(true, loaded.DefensiveCatalogSource);
        Assert.Equal("1856", loaded.Options!.AbilitiesOn);
        Assert.Equal("871", loaded.Options.AbilitiesOff);
        Assert.Equal(2, loaded.Verdicts!.Length);
    }

    [Fact]
    public void Policy_V23_Record_Re_Serializes_Byte_Equal()
    {
        var line = Json(SampleV23PolicyEvent());

        Assert.Equal(Encoding.UTF8.GetBytes(line), Reserialized(line));
    }

    [Fact]
    public void Policy_Record_Omits_Ability_Overrides_When_None()
    {
        var combat = new CombatContext { HpValid = true, HpPct = 80, ContextValid = true };
        var plan = SchedulePlan.Hold(ScheduleReason.MainRotation, 0, 0, 0, "no overrides");

        var json = Json(new TelemetryEvent
        {
            Kind = TelemetryKind.Tick,
            TMs = 10,
            Policy = TelemetryEvent.BuildPolicy(plan, true, combat, new PolicyOptions()),
        });

        Assert.DoesNotContain("\"on\":", json);
        Assert.DoesNotContain("\"off\":", json);
        var loaded = TelemetryJson.Deserialize(json)!.Policy!;
        Assert.Null(loaded.Options!.AbilitiesOn);
        Assert.Null(loaded.Options.AbilitiesOff);
    }

    [Fact]
    public void Policy_Record_Emits_Unknown_Urgency_When_A_Combat_Context_Is_Passed()
    {
        // A v4/v5/degraded context is still a combat object: urgency is
        // recorded as "Unknown", never omitted, so replay can tell a v2.3
        // record apart from a legacy pre-v2.3 one (which has no "du").
        var combat = new CombatContext
        {
            ContextValid = false,
            DefensiveUrgency = DefensiveUrgency.Unknown,
            StaggerUrgency = DefensiveUrgency.Unknown,
        };

        var json = Json(new TelemetryEvent
        {
            Kind = TelemetryKind.Tick,
            TMs = 10,
            Policy = TelemetryEvent.BuildPolicy(
                SchedulePlan.Hold(ScheduleReason.NoCandidate, 0, 0, 0, "unknown"), false, combat, new PolicyOptions()),
        });

        Assert.Contains("\"du\":\"Unknown\"", json);
        Assert.Contains("\"dsu\":\"Unknown\"", json);
        Assert.DoesNotContain("\"dsrc\"", json);   // false => omitted
        Assert.Contains("\"hpKnown\":false", json);
    }

    [Fact]
    public void Policy_Record_Omits_Urgency_When_No_Combat_Context()
    {
        var json = Json(new TelemetryEvent
        {
            Kind = TelemetryKind.Tick,
            TMs = 10,
            Policy = TelemetryEvent.BuildPolicy(
                SchedulePlan.Hold(ScheduleReason.StaleFrame, 0, 0, 0, "no context"), false, null, null),
        });

        Assert.DoesNotContain("\"du\"", json);
        Assert.DoesNotContain("\"dsu\"", json);
        Assert.DoesNotContain("\"dsrc\"", json);
        Assert.DoesNotContain("\"opts\"", json);
    }
}
