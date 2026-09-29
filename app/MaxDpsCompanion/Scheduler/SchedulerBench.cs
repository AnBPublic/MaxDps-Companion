using System.Security.Cryptography;
using System.Text;

namespace MaxDpsCompanion;

/// <summary>
/// Deterministic measurement harness for <see cref="ActionScheduler"/>
/// (--bench-scheduler). Runs a scripted 900-tick synthetic rotation — main
/// spam, cooldowns, interrupts mid-GCD, a defensive emergency that goes stale,
/// an active cast and an active channel (v2.1 execution-safety holds), a 2 s
/// heartbeat freeze (link loss), a protocol v1 no-GCD frame, target loss and
/// idle — on a fake 33 ms clock, and reports press counts, interval
/// statistics, hold reasons and a plan hash. Pure: same ticks in, same
/// numbers out. No game, no wall clock, no screen.
/// </summary>
internal static class SchedulerBench
{
    private const int TickMs = 33;
    private const int PhaseTicks = 900;

    /// <summary>
    /// Recorded baseline for the p95 send interval (ms) at the default 27,000
    /// ticks on the pinned script (2026-09-29, Stream 2). Deterministic: the
    /// bench is pure, so any change here is a real behaviour/ordering change.
    /// Stream 2 fails if p95 regresses by more than <see cref="BaselineTolerance"/>.
    /// </summary>
    internal const int BaselineP95Ms = 1782;

    /// <summary>Allowed p95 regression before the bench reports FAIL.</summary>
    internal const double BaselineTolerance = 0.10;

    private static readonly KeyStroke KeyE = new(0x45, false, false, false);
    private static readonly KeyStroke KeyR = new(0x52, false, false, false);
    private static readonly KeyStroke KeyF = new(0x46, false, false, false);
    private static readonly KeyStroke KeyX = new(0x58, false, false, false);
    private static readonly KeyStroke Key2 = new(0x32, false, false, false);
    private static readonly bool[] AllEnabled = [true, true, true, true, true, true];

    public static string[] Run(int ticks)
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();
        var holds = new Dictionary<ScheduleReason, int>();
        var selections = new Dictionary<ScheduleReason, int>();
        var sendsBySlot = new Dictionary<Slot, int>();
        var hashInput = new StringBuilder(ticks * 24);

        var sends = 0;
        var suppressed = 0;
        var demotions = 0;
        var intervals = new List<long>();
        long lastSendAt = -1;

        for (var tick = 0; tick < ticks; tick++)
        {
            var now = (long)tick * TickMs;
            var frame = Script(tick);
            tracker.Update(frame, now);
            var plan = scheduler.Advance(new ScheduleInput
            {
                Frame = frame,
                Candidates = tracker.Snapshot(AllEnabled),
                NowMs = now,
                MinKeyIntervalMs = 120,
                StaleAfterMs = 1500,
                HeartbeatTimeoutMs = 500,
                RepeatSuppressMs = 900,
                // Intelligence off (options null) with the real cast state:
                // measures the hard execution-safety gate on the legacy path.
                Context = new CombatContext { Cast = frame.Cast, ContextValid = true },
            });

            if (plan.HasSelection && plan.Actions.Length > 0)
            {
                var action = plan.Actions[0];
                scheduler.NoteSent(now, action.Slot, action.Stroke);
                tracker.NotePressed(action.Slot, now);
                sends++;
                sendsBySlot[action.Slot] = sendsBySlot.GetValueOrDefault(action.Slot) + 1;
                if (lastSendAt >= 0) intervals.Add(now - lastSendAt);
                lastSendAt = now;
                selections[plan.Reason] = selections.GetValueOrDefault(plan.Reason) + 1;
            }
            else
            {
                holds[plan.Reason] = holds.GetValueOrDefault(plan.Reason) + 1;
            }
            suppressed += plan.Suppressed;
            if (plan.StaleDemoted) demotions++;
            hashInput.Append(tick).Append('|').Append((int)plan.Reason).Append('|')
                .Append(plan.Selected is { } s ? (int)s : -1).Append(';');
        }

        intervals.Sort();
        var seconds = ticks * (double)TickMs / 1000;
        var p95 = Stat(intervals, 0.95);
        var p95Ceiling = (long)Math.Ceiling(BaselineP95Ms * (1 + BaselineTolerance));
        var baselineOk = p95 <= p95Ceiling;
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(hashInput.ToString())))[..16].ToLowerInvariant();

        return
        [
            $"scenario: {PhaseTicks}-tick script (main spam, GCD, interrupt, defensive stale, cast/channel holds, link freeze, v1, target loss, idle) repeated",
            $"ticks={ticks} simulated={seconds:F1}s sends={sends} ({(seconds > 0 ? sends / seconds : 0):F2}/s)",
            $"interval ms: min={Stat(intervals, 0)} median={Stat(intervals, 0.50)} p95={p95} max={Stat(intervals, 1)}",
            $"sends by slot: {BySlot(sendsBySlot)}",
            $"holds by reason: {Histogram(holds)}",
            $"selections: {Histogram(selections)}",
            $"suppressed candidates: {suppressed}",
            $"stale demotion ticks: {demotions}",
            $"plan sha256={hash}",
            $"baseline: p95 {p95}ms vs recorded {BaselineP95Ms}ms, ceiling +{BaselineTolerance:P0} = {p95Ceiling}ms -> {(baselineOk ? "PASS" : "FAIL")}",
        ];
    }

    /// <summary>
    /// Scripted frame for one tick. Phase = tick % 900; heartbeat changes every
    /// 10 ticks (330 ms) except during the [420,480) freeze.
    /// </summary>
    private static BridgeFrame Script(int tick)
    {
        var phase = tick % PhaseTicks;
        var heartbeat = phase is >= 420 and < 480
            ? 1 + (420 / 10) % 15
            : 1 + tick / 10 % 15;
        var version = phase is >= 480 and < 540
            ? PixelProtocol.SupportedVersionV1
            : PixelProtocol.SupportedVersion;
        var onGcd = (phase is >= 60 and < 120 || phase is >= 180 and < 300) && tick % 20 < 10;
        var hasTarget = phase is < 540 or >= 660;
        var state = phase is >= 600 and < 660 ? BridgeState.NeedTarget : BridgeState.Active;
        // v2.1: a hard cast then a channel with a live main suggestion — the
        // execution-safety gate must hold instead of sending.
        var cast = phase is >= 300 and < 315 ? PlayerCastState.Casting
            : phase is >= 315 and < 330 ? PlayerCastState.Channeling
            : PlayerCastState.None;

        KeyStroke? main = null, offensive = null, defensive = null, interrupt = null;
        switch (phase)
        {
            case < 60:
                main = KeyE;
                break;
            case < 120:
                main = KeyE;
                offensive = Key2;
                break;
            case < 180:
                main = KeyR;
                break;
            case < 240:
                main = KeyE;
                offensive = Key2;
                break;
            case < 300:
                main = KeyE;
                interrupt = KeyF;
                break;
            case < 330:
                main = KeyE;
                defensive = KeyX;
                break;
            case < 480:
                main = KeyR;
                defensive = KeyX;
                break;
            case < 540:
                main = KeyE;
                break;
            case < 600:
                main = KeyE;
                break;
        }

        var slots = new KeyStroke?[PixelProtocol.SlotCount];
        slots[(int)Slot.Main] = main;
        slots[(int)Slot.Offensive] = offensive;
        slots[(int)Slot.Defensive] = defensive;
        slots[(int)Slot.Interrupt] = interrupt;

        var flags = 0;
        if (phase is < 540 or >= 660) flags |= PixelProtocol.StatusFlagInCombat;
        if (onGcd) flags |= PixelProtocol.StatusFlagOnGcd;
        if (hasTarget) flags |= PixelProtocol.StatusFlagHasTarget;

        return new BridgeFrame
        {
            State = state,
            Heartbeat = heartbeat,
            Version = version,
            StatusFlags = flags,
            Slots = slots,
            Cast = cast,
        };
    }

    private static long Stat(List<long> sorted, double quantile)
    {
        if (sorted.Count == 0) return 0;
        var index = (int)Math.Round(quantile * (sorted.Count - 1));
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    private static string BySlot(Dictionary<Slot, int> counts) =>
        string.Join(" ", counts.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"));

    private static string Histogram<T>(Dictionary<T, int> counts) where T : struct, Enum =>
        string.Join(" ", counts.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key}={pair.Value}"));
}
