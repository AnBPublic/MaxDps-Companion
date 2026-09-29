using System.Text;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Replay determinism contract: feeding a recorded decision context back into
/// <see cref="DecisionEngine.Evaluate"/> must reproduce the recorded result
/// exactly (zero mismatches), the report must be byte-stable across runs, and
/// legacy recordings must expose the evaluator's "would-select" without
/// claiming a defect. Rejected candidates and GCD/stale holds are rendered
/// from the same rules the evaluator applies.
/// </summary>
public class TelemetryReplayTests : IDisposable
{
    private readonly List<string> _temp = [];

    public void Dispose()
    {
        foreach (var path in _temp)
            if (File.Exists(path)) File.Delete(path);
    }

    private static readonly KeyStroke StrokeE = new(0x45, false, false, false);
    private static readonly KeyStroke StrokeTwo = new(0x32, false, false, false);
    private static readonly KeyStroke StrokeT = new(0x54, false, false, false);

    private static ActionCandidate Candidate(
        Slot slot,
        KeyStroke stroke,
        bool enabled = true,
        bool actionable = true,
        long firstSeenMs = 0,
        long lastChangedMs = 0,
        bool everPressed = false,
        int spellId = 0) =>
        new(slot, stroke, enabled, actionable, firstSeenMs, lastChangedMs, LastPressedMs: 0, everPressed, spellId);

    private static DecisionContext Context(
        long nowMs,
        bool onGcd = false,
        long staleAfterMs = 1500,
        params ActionCandidate[] candidates) => new()
    {
        InCombat = true,
        OnGcd = onGcd,
        HasTarget = true,
        State = BridgeState.Active,
        NowMs = nowMs,
        StaleAfterMs = staleAfterMs,
        Candidates = candidates,
    };

    private static TelemetryEvent TickEvent(DecisionContext ctx, DecisionResult decision, bool intelligence = true, string note = "sending")
    {
        var slots = new KeyStroke?[PixelProtocol.SlotCount];
        foreach (var candidate in ctx.Candidates) slots[(int)candidate.Slot] = candidate.Stroke;
        var status = 0;
        if (ctx.InCombat) status |= PixelProtocol.StatusFlagInCombat;
        if (ctx.OnGcd) status |= PixelProtocol.StatusFlagOnGcd;
        if (ctx.HasTarget) status |= PixelProtocol.StatusFlagHasTarget;
        var frame = new BridgeFrame
        {
            State = ctx.State,
            Heartbeat = (int)(ctx.NowMs % 16),
            Version = PixelProtocol.SupportedVersion,
            StatusFlags = status,
            Slots = slots,
        };
        return TelemetryEvent.Tick(ctx.NowMs, frame, DecodeFault.None, ctx, decision, intelligence, ctx.Candidates, note, true);
    }

    private string TempPath(string suffix = ".jsonl")
    {
        var path = Path.Combine(Path.GetTempPath(), $"mdb-tel-replay-{Guid.NewGuid():N}{suffix}");
        _temp.Add(path);
        return path;
    }

    [Fact]
    public void Intelligence_Tick_Recomputes_Exactly()
    {
        var ctx = Context(10_000, candidates: [Candidate(Slot.Main, StrokeE), Candidate(Slot.Offensive, StrokeTwo)]);
        var decision = DecisionEngine.Evaluate(ctx);

        var result = ReplayRunner.Run([TickEvent(ctx, decision)]);

        Assert.Equal(1, result.Ticks);
        Assert.Equal(1, result.Decisions);
        Assert.Equal(0, result.Mismatches);
        Assert.Contains("decision: Main -> MainRotation (70%)", result.Report);
        Assert.Contains("recorded: Main -> MainRotation (70%)  OK", result.Report);
    }

    [Fact]
    public void Replay_Is_Deterministic()
    {
        var fresh = Context(10_000, candidates: [Candidate(Slot.Main, StrokeE), Candidate(Slot.Offensive, StrokeTwo)]);
        var stale = Context(12_000, candidates:
        [
            Candidate(Slot.Main, StrokeE, firstSeenMs: 0, lastChangedMs: 0, everPressed: true),
            Candidate(Slot.Offensive, StrokeTwo, firstSeenMs: 11_000, lastChangedMs: 11_000),
        ]);
        var events = new[]
        {
            TickEvent(fresh, DecisionEngine.Evaluate(fresh)),
            TelemetryEvent.Sent(10_010, "spell", Slot.Main, StrokeE, 0),
            TickEvent(stale, DecisionEngine.Evaluate(stale)),
        };

        var first = ReplayRunner.Run(events);
        var second = ReplayRunner.Run(events);

        Assert.Equal(0, first.Mismatches);
        Assert.Equal(first.Report, second.Report);
    }

    [Fact]
    public void Legacy_Tick_Reports_WouldSelect_And_Fallback_Matches()
    {
        var ctx = Context(10_000, candidates: [Candidate(Slot.Main, StrokeE), Candidate(Slot.Offensive, StrokeTwo)]);

        var result = ReplayRunner.Run([TickEvent(ctx, DecisionResult.Fallback(), intelligence: false)]);

        Assert.Equal(0, result.Mismatches);
        Assert.Contains("legacy priority (evaluator off)", result.Report);
        Assert.Contains("wouldsel: Main -> MainRotation (70%)", result.Report);
    }

    [Fact]
    public void Tampered_Recording_Reports_Mismatch()
    {
        var ctx = Context(10_000, candidates: [Candidate(Slot.Main, StrokeE)]);
        var evt = TickEvent(ctx, DecisionEngine.Evaluate(ctx));
        var tampered = evt with { Decision = evt.Decision! with { Confidence = 1 } };

        var result = ReplayRunner.Run([tampered]);

        Assert.Equal(1, result.Mismatches);
        Assert.Contains("MISMATCH", result.Report);
    }

    [Fact]
    public void Tampered_Legacy_Order_Reports_Mismatch()
    {
        var ctx = Context(10_000, candidates: [Candidate(Slot.Main, StrokeE)]);
        var evt = TickEvent(ctx, DecisionResult.Fallback(), intelligence: false);
        var tampered = evt with { Decision = evt.Decision! with { Order = [Slot.Trinket] } };

        var result = ReplayRunner.Run([tampered]);

        Assert.Equal(1, result.Mismatches);
    }

    [Fact]
    public void Gcd_Tick_Recomputes_And_Shows_Hold()
    {
        var ctx = Context(10_000, onGcd: true, candidates: [Candidate(Slot.Main, StrokeE)]);
        var decision = DecisionEngine.Evaluate(ctx);
        Assert.Equal(DecisionReason.GcdHold, decision.Reason); // sanity: the recording is honest

        var result = ReplayRunner.Run([TickEvent(ctx, decision)]);

        Assert.Equal(0, result.Mismatches);
        Assert.Contains("held    : GCD active", result.Report);
    }

    [Fact]
    public void Rejected_Candidates_List_Disabled_And_Lower_Rank()
    {
        var ctx = Context(10_000, candidates:
        [
            Candidate(Slot.Main, StrokeE),
            Candidate(Slot.Offensive, StrokeTwo),
            Candidate(Slot.Trinket, StrokeT, enabled: false),
        ]);

        var result = ReplayRunner.Run([TickEvent(ctx, DecisionEngine.Evaluate(ctx))]);

        Assert.Contains("rejected:", result.Report);
        Assert.Contains("Offensive (lower rank #2)", result.Report);
        Assert.Contains("Trinket (slot disabled)", result.Report);
    }

    [Fact]
    public void Stale_Demotion_Is_Reported_And_Recomputed()
    {
        var ctx = Context(10_000, candidates:
        [
            Candidate(Slot.Main, StrokeE, lastChangedMs: 0, everPressed: true),
            Candidate(Slot.Offensive, StrokeTwo, firstSeenMs: 9000, lastChangedMs: 9000),
        ]);
        var decision = DecisionEngine.Evaluate(ctx);
        Assert.Equal(Slot.Offensive, decision.Selected); // sanity: stale main demoted

        var result = ReplayRunner.Run([TickEvent(ctx, decision)]);

        Assert.Equal(0, result.Mismatches);
        Assert.Contains("Main (lower rank #2, stale)", result.Report);
        Assert.Contains("stale   : a pressed candidate was demoted", result.Report);
    }

    [Fact]
    public void EndToEnd_Recorder_Export_Reader_Replay()
    {
        var ctx1 = Context(10_000, candidates: [Candidate(Slot.Main, StrokeE)]);
        var ctx2 = Context(10_200, candidates: [Candidate(Slot.Main, StrokeE, lastChangedMs: 0, everPressed: true)]);
        var recorder = new TelemetryRecorder(256);
        recorder.Append(TickEvent(ctx1, DecisionEngine.Evaluate(ctx1)));
        recorder.Append(TelemetryEvent.Sent(10_010, "spell", Slot.Main, StrokeE, 0));
        recorder.Append(TickEvent(ctx2, DecisionEngine.Evaluate(ctx2)));

        var path = TempPath();
        recorder.Export(path);
        var (events, badLines) = TelemetryReader.Read(path);
        var result = ReplayRunner.Run(events, badLines);

        Assert.Equal(0, badLines);
        Assert.Equal(2, result.Ticks);
        Assert.Equal(1, result.Sends);
        Assert.Equal(0, result.Mismatches);
    }

    [Fact]
    public void Corrupt_Line_Does_Not_Abort_Replay()
    {
        var ctx = Context(10_000, candidates: [Candidate(Slot.Main, StrokeE)]);
        var valid = Encoding.UTF8.GetString(TelemetryJson.Serialize(TickEvent(ctx, DecisionEngine.Evaluate(ctx))));
        var path = TempPath();
        File.WriteAllLines(path, ["{ not json", valid, "{\"fmt\":1}"]);
        _temp.Add(path + ".replay.txt");

        var (events, badLines) = TelemetryReader.Read(path);
        var result = ReplayRunner.Run(events, badLines);

        Assert.Equal(2, badLines);
        Assert.Equal(1, result.Ticks);
        Assert.Equal(0, result.Mismatches);
        Assert.Contains("bad 2", result.Report);
    }

    [Fact]
    public void Sample_Fixture_Replays_With_Zero_Mismatches()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "sample-session.jsonl");

        var (events, badLines) = TelemetryReader.Read(path);
        var result = ReplayRunner.Run(events, badLines, source: path);

        Assert.True(events.Count >= 4, $"fixture should carry a full session, found {events.Count}");
        Assert.Equal(0, badLines);
        Assert.Equal(0, result.Errors);
        Assert.Equal(0, result.Mismatches);
        Assert.Contains("MainRotation", result.Report);
        Assert.Contains("OffensiveCooldown", result.Report);
    }

    [Fact]
    public void RunFile_Writes_Report_Next_To_Input()
    {
        var source = TempPath();
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "sample-session.jsonl"), source);

        var result = ReplayRunner.RunFile(source);

        Assert.Equal(source + ".replay.txt", result.ReportPath);
        Assert.True(File.Exists(result.ReportPath));
        Assert.Equal(result.Report, File.ReadAllText(result.ReportPath));
        _temp.Add(result.ReportPath);
    }

    // ---- policy verdict replay (v2.1) ------------------------------------

    private static BridgeFrame PolicyFrame(KeyStroke? main = null, KeyStroke? defensive = null, int heartbeat = 1)
    {
        var slots = new KeyStroke?[PixelProtocol.SlotCount];
        slots[(int)Slot.Main] = main;
        slots[(int)Slot.Defensive] = defensive;
        return new BridgeFrame
        {
            State = BridgeState.Active,
            Heartbeat = heartbeat,
            Version = PixelProtocol.SupportedVersion,
            StatusFlags = PixelProtocol.StatusFlagInCombat | PixelProtocol.StatusFlagHasTarget,
            Slots = slots,
        };
    }

    private static CombatContext PolicyCombat(
        int hp = 80,
        PlayerCastState cast = PlayerCastState.None,
        TriState targetCasting = TriState.No,
        TriState targetInMelee = TriState.Unknown,
        params (Slot Slot, TriState State)[] range)
    {
        var ranges = new TriState[PixelProtocol.SlotCount];
        foreach (var (slot, state) in range) ranges[(int)slot] = state;
        return new CombatContext
        {
            HpValid = true,
            HpPct = hp,
            Cast = cast,
            TargetCasting = targetCasting,
            TargetInMelee = targetInMelee,
            SlotRange = ranges,
            SlotBuffActive = new TriState[PixelProtocol.SlotCount],
            ContextValid = true,
        };
    }

    private static TelemetryEvent PolicyTick(
        ActionScheduler scheduler, long nowMs, BridgeFrame frame, CombatContext combat,
        PolicyOptions options, ActionCandidate[] candidates, string note = "holding")
    {
        var plan = scheduler.Advance(new ScheduleInput
        {
            Frame = frame,
            Candidates = candidates,
            NowMs = nowMs,
            MinKeyIntervalMs = 120,
            StaleAfterMs = 1500,
            HeartbeatTimeoutMs = 500,
            RepeatSuppressMs = 900,
            Context = combat,
            Options = options,
            Catalog = AbilityCatalog.Default,
            CollectPolicyVerdicts = true,
        });
        return TelemetryEvent.Tick(nowMs, frame, DecodeFault.None, null, null, true, candidates, note, true,
            TelemetryEvent.BuildPolicy(plan, true, combat, options));
    }

    [Fact]
    public void Policy_Verdicts_Recompute_With_Zero_Mismatches()
    {
        var scheduler = new ActionScheduler();
        var options = PolicyOptions.Standard;
        var candidates = new[]
        {
            Candidate(Slot.Defensive, StrokeTwo, spellId: 23920),
            Candidate(Slot.Main, StrokeE),
        };

        // Tick 1: Spell Reflection with no observed target cast -> HOLD.
        var events = new List<TelemetryEvent>
        {
            PolicyTick(scheduler, 1000, PolicyFrame(StrokeE, StrokeTwo, 1),
                PolicyCombat(cast: PlayerCastState.Casting), options, candidates),
            // Tick 2: cast finished, a target cast is live -> USE (+ main held
            // only if the cast state says so; here it is none).
            PolicyTick(scheduler, 1400, PolicyFrame(StrokeE, StrokeTwo, 2),
                PolicyCombat(targetCasting: TriState.Yes), options, candidates),
        };

        var result = ReplayRunner.Run(events);

        Assert.Equal(0, result.VerdictMismatches);
        Assert.True(result.Verdicts >= 4, $"expected the recorded verdicts to be recomputed, got {result.Verdicts}");
        Assert.Contains("0 mismatch(es)", result.Report);
    }

    [Fact]
    public void Tampered_Policy_Verdict_Reports_Mismatch()
    {
        var scheduler = new ActionScheduler();
        var options = PolicyOptions.Standard;
        var candidates = new[] { Candidate(Slot.Defensive, StrokeTwo, spellId: 23920) };

        var evt = PolicyTick(scheduler, 1000, PolicyFrame(defensive: StrokeTwo),
            PolicyCombat(targetCasting: TriState.Yes), options, candidates);
        var tampered = evt with
        {
            Policy = evt.Policy! with
            {
                Verdicts = [evt.Policy!.Verdicts![0] with { Verdict = "Skip", Reason = "tampered" }],
            },
        };

        var result = ReplayRunner.Run([tampered]);

        Assert.Equal(1, result.VerdictMismatches);
        Assert.Contains("VERDICT MISMATCH", result.Report);
    }

    [Fact]
    public void Policy_Replay_Rebuilds_Trinket_Lockout_From_Sends()
    {
        var scheduler = new ActionScheduler();
        var options = PolicyOptions.Standard;
        var candidates = new[] { Candidate(Slot.Trinket, StrokeT) };

        var tick1 = PolicyTick(scheduler, 1000, PolicyFrame(heartbeat: 1), PolicyCombat(), options, candidates, "sending");
        scheduler.NoteSent(1000, Slot.Trinket, StrokeT);   // the live press
        // LIVE EVENT ORDER: the engine appends the send BEFORE the tick event
        // of the same tick (TrySendOne runs before Report). The replay must
        // still evaluate tick1 against pre-send memory and apply the send
        // afterwards; the shared 20 s lockout then blocks tick2's trinket.
        var send1 = TelemetryEvent.Sent(1000, "spell", Slot.Trinket, StrokeT, 0);
        var tick2 = PolicyTick(scheduler, 2000, PolicyFrame(heartbeat: 2), PolicyCombat(),
            options, candidates, "holding");

        var result = ReplayRunner.Run([send1, tick1, tick2]);

        Assert.Equal(0, result.VerdictMismatches);
        Assert.Contains("trinket", result.Report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Policy_Replay_Uses_Live_Send_Before_Tick_Order()
    {
        var scheduler = new ActionScheduler();
        var options = PolicyOptions.Standard;

        // Tick 1: Shield Wall is sent (send event written before the tick).
        var wall = new[] { Candidate(Slot.Defensive, StrokeTwo, spellId: 871) };
        var tick1 = PolicyTick(scheduler, 1000, PolicyFrame(defensive: StrokeTwo), PolicyCombat(),
            options, wall, "sending");
        scheduler.NoteSent(1000, Slot.Defensive, StrokeTwo, 871);
        var send1 = TelemetryEvent.Sent(1000, "spell", Slot.Defensive, StrokeTwo, 0, 871);

        // Tick 2: a minor defensive is suggested; the overlap hold is driven
        // ONLY by the buffered send — tick1 itself must not have seen it.
        var minor = new[] { Candidate(Slot.Defensive, StrokeE, spellId: 2565) };
        var tick2 = PolicyTick(scheduler, 1400, PolicyFrame(defensive: StrokeE, heartbeat: 2), PolicyCombat(),
            options, minor, "holding");

        var result = ReplayRunner.Run([send1, tick1, tick2]);

        Assert.Equal(0, result.VerdictMismatches);
        Assert.Equal(2, result.Verdicts);   // one defensive candidate per tick
    }
}
