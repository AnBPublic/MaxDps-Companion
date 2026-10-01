namespace MaxDpsCompanion;

/// <summary>
/// The estimator's per-tick output. <see cref="TargetHpFrac"/> is always the
/// last observed fraction (0 when nothing has been observed); <see cref="TtkSec"/>
/// is only meaningful when <see cref="Valid"/> is true, but is clamped to
/// 0..<see cref="TtkEstimator.MaxTtkSec"/> even when invalid (a trickle reads as
/// "long", never as an unbounded number).
///
/// v3.6 additions (all optional, defaults keep existing callers compiling):
/// <see cref="Provisional"/> is an early rate that is not yet trusted,
/// <see cref="AgeSec"/> is the lifetime of the current target (0 with no target),
/// and <see cref="FastPackLatch"/> is a rolling warning that the last two targets
/// died fast (a trash pack).
/// </summary>
internal readonly record struct TtkEstimate(
    bool Valid,
    double TtkSec,
    double TargetHpFrac,
    bool Provisional = false,
    double AgeSec = 0,
    bool FastPackLatch = false)
{
    /// <summary>The fail-safe "no estimate" value: invalid, clamped long, no flags.</summary>
    public static readonly TtkEstimate Invalid = new(false, TtkEstimator.MaxTtkSec, 0);

    /// <summary>Rounded seconds for reasons/telemetry (one decimal).</summary>
    public double RoundedSec => Math.Round(TtkSec, 1);
}

/// <summary>
/// Per-target time-to-kill estimator (v3.6.0). Pure and fake-clock: every input
/// is a decoded protocol field (<c>nowMs</c>, <c>hasTarget</c>,
/// <c>targetHpValid</c>, <c>targetHpBand</c>, <c>inCombat</c>); the class reads
/// no clock, no I/O and no game value.
///
/// Base contract (§3.1 of the TTK plan):
///  * <c>frac = (band + 0.5) / 15.0</c> — the midpoint of the coarse wire band.
///  * RESET (epoch++, clear EWMA) when: no target; HP unknown continuously for
///    more than 10 s; or <c>frac</c> jumps UP by more than 0.12 (a new target or
///    a big heal — the learned rate no longer applies).
///  * FEED on decline only: <c>inst = (prevFrac - frac) / dtSec</c>; below
///    0.004 frac/s it is noise/heal and is ignored for the EWMA (the previous
///    fraction still advances), otherwise <c>ewma = a·inst + (1-a)·ewma</c> with
///    <c>a = 1 - exp(-dt / 3.0)</c>.
///  * VALID when at least 2 fed samples, an observation span of at least 2.5 s,
///    and <c>ewma &gt;= 0.004</c>; else invalid.
///  * <c>TtkSec = clamp(frac / ewma, 0, 300)</c>; below the minimum rate this is
///    300 ("long").
///
/// v3.6 provisional + latch (§2 of the approved spec):
///  * FAST-DROP provisional: within the first 2.5 s, a band fall of at least 3
///    over at least 0.5 s yields <c>Provisional=true</c> and
///    <c>TtkSec=frac/(dropFrac/dt)</c>. Valid stays false until the normal rule.
///  * LOW-FIRST-SIGHT provisional: first observation with band&lt;=3 while in
///    combat seeds <c>Provisional=true, TtkSec=frac*15</c>.
///  * FAST-PACK latch: a "kill" is a departure (no target, or an upward jump
///    &gt; 0.12) whose previous target lived &lt;=12 s and ended at frac&lt;=0.25.
///    Two kills within 20 s set the latch for 20 s; it clears early when the
///    current target reaches AgeSec&gt;=12 with frac&gt;0.5, or a valid
///    TtkSec&gt;=30. The latch survives target swaps and the reset paths.
///
/// Unknown HP for a short flicker marks the output stale (invalid, provisional
/// cleared) but does NOT reset the learned rate; a sustained unknown past 10 s
/// resets. No-target/reset paths also clear provisional but keep the latch.
/// </summary>
internal sealed class TtkEstimator
{
    /// <summary>Hard ceiling for a TTK estimate; also the invalid/trickle value.</summary>
    public const double MaxTtkSec = 300.0;

    /// <summary>Minimum believable decline rate (frac/s); below it is noise/heal.</summary>
    public const double MinRate = 0.004;

    /// <summary>An upward fraction jump beyond this resets the learned rate.</summary>
    public const double JumpResetFrac = 0.12;

    /// <summary>Minimum observation span before an estimate is trusted.</summary>
    public const double MinSpanSec = 2.5;

    /// <summary>Continuous unknown HP beyond this many seconds resets the estimate.</summary>
    public const double UnknownResetSec = 10.0;

    /// <summary>Minimum number of fed decline samples before an estimate is trusted.</summary>
    public const int MinSamples = 2;

    /// <summary>Fast-drop provisional: minimum band fall (>=~20%).</summary>
    public const int ProvisionalMinDropBands = 3;

    /// <summary>Fast-drop provisional: minimum span the fall is measured over.</summary>
    public const double ProvisionalMinSpanSec = 0.5;

    /// <summary>Low-first-sight provisional: highest band that seeds an estimate.</summary>
    public const int LowFirstSightBand = 3;

    /// <summary>Fast-pack latch: a kill target must have lived at most this long.</summary>
    public const double KillMaxLifeSec = 12.0;

    /// <summary>Fast-pack latch: a kill target must have ended at or below this frac.</summary>
    public const double KillMaxLastFrac = 0.25;

    /// <summary>Fast-pack latch: kills needed inside <see cref="LatchWindowSec"/>.</summary>
    public const int LatchKills = 2;

    /// <summary>Fast-pack latch: window (seconds) in which kills are counted.</summary>
    public const double LatchWindowSec = 20.0;

    /// <summary>Fast-pack latch: how long the latch is held once set.</summary>
    public const double LatchHoldSec = 20.0;

    /// <summary>EWMA time constant (seconds) for the decline-rate smoother.</summary>
    private const double RateTauSec = 3.0;

    /// <summary>Fast-drop provisional is only armed within this many seconds of first sight.</summary>
    private const double FastDropMaxAgeSec = 2.5;

    private long _prevMs;
    private double _prevFrac;
    private bool _havePrev;

    // Anchor of the last fed decline sample. The rate is measured across the
    // whole flat interval, not tick-to-tick: the wire band changes in coarse
    // 6.7% steps, so a tick-by-tick rate would over-estimate whenever the band
    // happened to step on that exact tick.
    private long _feedMs;
    private double _feedFrac;

    private double _ewma;
    private int _samples;
    private long _firstObsMs = long.MinValue;

    private long _unknownSinceMs = long.MinValue;
    private int _epoch;

    // Fast-drop reference sample: the fraction at least ProvisionalMinSpanSec old.
    private long _refMs = long.MinValue;
    private double _refFrac;

    // Low-first-sight seed survives ticks until the target is trusted or replaced.
    private bool _lowSeed;

    // Fast-pack latch: kill timestamps inside the rolling window, and the hold deadline.
    private readonly Queue<long> _killTimes = new();
    private long _latchUntilMs = long.MinValue;

    // Last fed clock, so the latch property can report expiry without needing a
    // fresh Update (the estimator owns no real clock).
    private long _nowMs = long.MinValue;

    private TtkEstimate _estimate = TtkEstimate.Invalid;

    /// <summary>The last computed output (never null; invalid until a real decline).</summary>
    public TtkEstimate Estimate => _estimate;

    /// <summary>Learning epoch: increments on every reset, so callers can observe re-learns.</summary>
    public int Epoch => _epoch;

    /// <summary>
    /// True while the fast-pack latch is set (survives target swaps and resets).
    /// Clock-aware: a hold whose deadline has already passed reports false even
    /// before the next <see cref="Update"/> clears it.
    /// </summary>
    public bool FastPackLatch =>
        _latchUntilMs != long.MinValue && _nowMs < _latchUntilMs;

    /// <summary>Clears all learned target state (session start / kill-switch off). The latch is kept.</summary>
    public void Reset()
    {
        ClearTargetState();
        _prevMs = 0;
        _prevFrac = 0;
        _havePrev = false;
        _epoch++;
        _estimate = new TtkEstimate(false, MaxTtkSec, 0, false, 0, FastPackLatch);
    }

    /// <summary>
    /// Feeds one tick and returns the current estimate. Callers invoke this once
    /// per decoded frame, before the policy runs; a frame with no target or no
    /// valid target HP degrades to invalid (and eventually resets).
    /// <paramref name="inCombat"/> gates only the low-first-sight seed; it defaults
    /// to true so existing callers keep their behaviour.
    /// </summary>
    public TtkEstimate Update(long nowMs, bool hasTarget, bool targetHpValid, int targetHpBand, bool inCombat = true)
    {
        _nowMs = nowMs;

        // No target: nothing to estimate. A kill (fast short-lived target) is
        // recorded first; a later target starts a fresh epoch.
        if (!hasTarget)
        {
            RecordDeparture(nowMs);
            ClearTargetState();
            _havePrev = false;
            _prevMs = nowMs;
            _prevFrac = 0;
            _epoch++;
            _estimate = ApplyLatch(new TtkEstimate(false, MaxTtkSec, 0), nowMs);
            return _estimate;
        }

        // HP unknown: keep the learned rate for a short flicker, mark stale, and
        // reset once the gap exceeds the sustained-unknown window.
        if (!targetHpValid || targetHpBand is < 0 or > 14)
        {
            if (_unknownSinceMs == long.MinValue) _unknownSinceMs = nowMs;
            if ((nowMs - _unknownSinceMs) / 1000.0 > UnknownResetSec)
            {
                ClearTargetState();
                _havePrev = false;
                _prevMs = nowMs;
                _epoch++;
                _estimate = ApplyLatch(new TtkEstimate(false, MaxTtkSec, 0), nowMs);
                return _estimate;
            }
            _lowSeed = false;
            _estimate = ApplyLatch(
                new TtkEstimate(false, _estimate.TtkSec, _prevFrac, false, AgeSec(nowMs)), nowMs);
            return _estimate;
        }
        _unknownSinceMs = long.MinValue;

        var frac = (targetHpBand + 0.5) / 15.0;

        if (!_havePrev)
        {
            _havePrev = true;
            _prevFrac = frac;
            _prevMs = nowMs;
            _feedFrac = frac;
            _feedMs = nowMs;
            _firstObsMs = nowMs;
            _refMs = nowMs;
            _refFrac = frac;
            _lowSeed = targetHpBand <= LowFirstSightBand && inCombat;
            var ttk = _lowSeed ? frac * 15.0 : MaxTtkSec;
            _estimate = ApplyLatch(new TtkEstimate(false, ttk, frac, _lowSeed, 0), nowMs);
            return _estimate;
        }

        var dtMs = nowMs - _prevMs;
        if (dtMs <= 0) return _estimate; // duplicate/same-instant tick: no new information

        // A jump UP (new target or big heal) invalidates the learned rate. The
        // departing target is scored for the fast-pack latch before we reset.
        if (frac - _prevFrac > JumpResetFrac)
        {
            RecordDeparture(nowMs);
            ClearTargetState();
            _havePrev = true;
            _prevFrac = frac;
            _prevMs = nowMs;
            _feedFrac = frac;
            _feedMs = nowMs;
            _firstObsMs = nowMs;
            _refMs = nowMs;
            _refFrac = frac;
            _epoch++;
            _lowSeed = targetHpBand <= LowFirstSightBand && inCombat;
            var ttk = _lowSeed ? frac * 15.0 : MaxTtkSec;
            _estimate = ApplyLatch(new TtkEstimate(false, ttk, frac, _lowSeed, 0), nowMs);
            return _estimate;
        }

        _prevFrac = frac;
        _prevMs = nowMs;

        // Rate is measured from the last FED anchor, so flat ticks (band did not
        // move yet) accumulate time instead of biasing the rate upward.
        var feedDtSec = (nowMs - _feedMs) / 1000.0;
        var inst = feedDtSec > 0 ? (_feedFrac - frac) / feedDtSec : 0;
        if (inst >= MinRate)
        {
            var alpha = 1.0 - Math.Exp(-feedDtSec / RateTauSec);
            _ewma = _samples == 0 ? inst : alpha * inst + (1.0 - alpha) * _ewma;
            _samples++;
            _feedFrac = frac;
            _feedMs = nowMs;
            _estimate = Compose(nowMs, frac);
            return _estimate;
        }

        // Noise/heal: not a decline. A heal re-anchors at the new level so the
        // next real decline is measured from there; a flat tick keeps the anchor
        // (its elapsed time is part of the next measured decline). The previous
        // estimate is kept verbatim except for the exposed current fraction/age.
        if (frac > _feedFrac)
        {
            _feedFrac = frac;
            _feedMs = nowMs;
            _refMs = nowMs;
            _refFrac = frac;
        }
        if (_samples == 0 && _lowSeed)
        {
            _estimate = ApplyLatch(
                new TtkEstimate(false, frac * 15.0, frac, true, AgeSec(nowMs)), nowMs);
        }
        else
        {
            _estimate = ApplyLatch(
                _estimate with { TargetHpFrac = frac, AgeSec = AgeSec(nowMs) }, nowMs);
        }
        return _estimate;
    }

    /// <summary>
    /// Builds the trusted-or-provisional estimate for the fed-decline path.
    /// Precedence: a valid estimate beats any provisional; then fast-drop; then
    /// the low-first-sight seed; else an invalid clamped-long output.
    /// </summary>
    private TtkEstimate Compose(long nowMs, double frac)
    {
        var spanSec = AgeSec(nowMs);
        var valid = _samples >= MinSamples && spanSec >= MinSpanSec && _ewma >= MinRate;

        if (valid)
        {
            _lowSeed = false;
            _refMs = nowMs;
            _refFrac = frac;
            var ttk = Math.Clamp(frac / _ewma, 0.0, MaxTtkSec);
            return ApplyLatch(new TtkEstimate(true, ttk, frac, false, spanSec), nowMs);
        }

        var dropSpanSec = _refMs == long.MinValue ? 0 : (nowMs - _refMs) / 1000.0;
        var fastDrop = false;
        var fastTtk = MaxTtkSec;
        if (spanSec <= FastDropMaxAgeSec && dropSpanSec >= ProvisionalMinSpanSec)
        {
            var dropFrac = _refFrac - frac;
            if (dropFrac * 15.0 >= ProvisionalMinDropBands && dropFrac > 0)
            {
                fastDrop = true;
                fastTtk = frac / (dropFrac / dropSpanSec);
            }
        }
        if (dropSpanSec >= ProvisionalMinSpanSec)
        {
            _refMs = nowMs;
            _refFrac = frac;
        }

        if (fastDrop)
        {
            _lowSeed = false;
            return ApplyLatch(new TtkEstimate(false, fastTtk, frac, true, spanSec), nowMs);
        }
        if (_lowSeed)
        {
            return ApplyLatch(new TtkEstimate(false, frac * 15.0, frac, true, spanSec), nowMs);
        }
        return ApplyLatch(new TtkEstimate(false, MaxTtkSec, frac, false, spanSec), nowMs);
    }

    /// <summary>Seconds since the first observation of the current target (0 when none).</summary>
    private double AgeSec(long nowMs)
        => _firstObsMs == long.MinValue ? 0 : (nowMs - _firstObsMs) / 1000.0;

    /// <summary>Projects the current latch state onto an estimate, applying the early clears.</summary>
    private TtkEstimate ApplyLatch(TtkEstimate estimate, long nowMs)
    {
        var latch = nowMs < _latchUntilMs;
        if (latch && estimate.Valid && estimate.TtkSec >= 30.0) latch = false;
        if (latch && estimate.AgeSec >= KillMaxLifeSec && estimate.TargetHpFrac > 0.5) latch = false;
        if (!latch)
        {
            // Drop the stale kill window only on a real release (expiry or an
            // early clear). Clearing on every non-latched tick would wipe the
            // window before two kills could ever accumulate. Without this a
            // single later fast kill could re-roll LatchKills and re-latch.
            if (_latchUntilMs != long.MinValue) _killTimes.Clear();
            _latchUntilMs = long.MinValue;
        }
        return latch == estimate.FastPackLatch ? estimate : estimate with { FastPackLatch = latch };
    }

    /// <summary>
    /// Scores the current target as a fast-pack "kill" if it departed after a
    /// short life at low HP, then rolls the latch window forward.
    /// </summary>
    private void RecordDeparture(long nowMs)
    {
        if (!_havePrev || _firstObsMs == long.MinValue) return;
        var lifeSec = (nowMs - _firstObsMs) / 1000.0;
        if (lifeSec > KillMaxLifeSec) return;
        if (_prevFrac > KillMaxLastFrac) return;

        _killTimes.Enqueue(nowMs);
        while (_killTimes.Count > 0 && nowMs - _killTimes.Peek() > (long)(LatchWindowSec * 1000.0))
            _killTimes.Dequeue();
        if (_killTimes.Count >= LatchKills)
            _latchUntilMs = nowMs + (long)(LatchHoldSec * 1000.0);
    }

    /// <summary>Clears learned target state and provisional flags; the latch and its window survive.</summary>
    private void ClearTargetState()
    {
        ClearLearning();
        _lowSeed = false;
        _refMs = long.MinValue;
        _refFrac = 0;
    }

    private void ClearLearning()
    {
        _ewma = 0;
        _samples = 0;
        _firstObsMs = long.MinValue;
        _unknownSinceMs = long.MinValue;
        _feedFrac = 0;
        _feedMs = 0;
    }

    /// <summary>
    /// Reconstructs the wire HP band from a decoded target-HP percent (the frame
    /// only exposes the rounded percent, not the raw band). The band→percent→band
    /// round-trip is exact for all 0..14 bands; -1 (unknown) stays -1.
    /// </summary>
    public static int BandFromPercent(int targetHpPct)
    {
        if (targetHpPct < 0) return -1;
        return Math.Clamp((int)Math.Round(targetHpPct * 15.0 / 100.0), 0, 14);
    }
}
