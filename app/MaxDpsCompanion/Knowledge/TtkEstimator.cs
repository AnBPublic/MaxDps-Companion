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
///
/// v3.7 additions (all optional, defaults keep existing callers and "no history"
/// runs byte-identical): <see cref="HistBinding"/> is true only once the rolling
/// kill window holds at least the configured minimum, <see cref="HistKills"/> is
/// the live Count, <see cref="HistRate"/> is the pessimistic p-quantile burn rate
/// (frac/s, 0 when not binding), <see cref="HistTtkSec"/> is the history-blended
/// estimate clamped to 0..300 s (0 when not produced), <see cref="HistProvisional"/>
/// marks a history-only rate (no live estimate yet), and
/// <see cref="HistMedianLifeSec"/> is the window's median kill time.
/// <see cref="NeedDurFactor"/> is the configured adaptive-need duration factor
/// (0 when the history is not binding) for the policy's active-duration check.
/// </summary>
internal readonly record struct TtkEstimate(
    bool Valid,
    double TtkSec,
    double TargetHpFrac,
    bool Provisional = false,
    double AgeSec = 0,
    bool FastPackLatch = false,
    bool HistBinding = false,
    int HistKills = 0,
    double HistRate = 0,
    double HistTtkSec = 0,
    bool HistProvisional = false,
    double HistMedianLifeSec = 0,
    double NeedDurFactor = 0)
{
    /// <summary>The fail-safe "no estimate" value: invalid, clamped long, no flags.</summary>
    public static readonly TtkEstimate Invalid = new(false, TtkEstimator.MaxTtkSec, 0);

    /// <summary>Rounded seconds for reasons/telemetry (one decimal).</summary>
    public double RoundedSec => Math.Round(TtkSec, 1);
}

/// <summary>
/// Adaptive-history tuning (v3.7). Defaults are the approved spec values and are
/// the fail-open configuration: <see cref="History"/>=false reproduces the exact
/// pre-history estimator. All are clamped by the caller
/// (<see cref="AppSettings"/>), never here.
/// </summary>
internal readonly record struct TtkOptions(
    bool History = true,
    int Kills = 8,
    int MinKills = 3,
    int MaxAgeSec = 240,
    int Quantile = 75,
    double DurFactor = 0.5)
{
    /// <summary>
    /// The approved spec defaults. A record struct's <c>default</c>/<c>new()</c>
    /// zero-initialises (History=false, everything 0), so callers must use this
    /// when they mean "the documented default configuration".
    /// </summary>
    public static TtkOptions Default => new(
        History: true,
        Kills: 8,
        MinKills: 3,
        MaxAgeSec: 240,
        Quantile: 75,
        DurFactor: 0.5);
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
///
/// v3.7 adaptive history (§ of the approved spec): every valid kill (a departure
/// at frac&lt;=0.25 whose target lived &gt;=2 s and lost &gt;=0.15 frac) is appended to a
/// bounded rolling <see cref="KillHistory"/>. The history p-quantile burn rate
/// blends with the live EWMA — <c>w = clamp((spanSec-2.5)/6, 0, 1)</c> — so a
/// fresh pull starts at the trash-learned rate and the live rate takes over by
/// ~8.5 s. With no live estimate but a target, an in-combat frac and a binding
/// window, the history rate alone yields a provisional estimate. Window clears
/// after 90 s with no target and out of combat; <c>History=false</c> is a no-op.
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

    // ---- v3.7 adaptive history -------------------------------------------------

    /// <summary>A recorded kill's target must have lived at least this long.</summary>
    public const double HistoryValidMinLifeSec = 2.0;

    /// <summary>A recorded kill must have lost at least this fraction of HP.</summary>
    public const double HistoryValidMinDeltaFrac = 0.15;

    /// <summary>The history-only rate blends in over this span after the live minimum.</summary>
    private const double HistoryBlendSpanSec = 6.0;

    /// <summary>No target + out of combat for this long clears the kill window.</summary>
    public const double HistoryClearAfterSec = 90.0;

    private readonly TtkOptions _options;
    private readonly KillHistory _history;

    // First observed fraction of the current target (the kill record's StartFrac).
    private double _firstFrac;

    // When the no-target + out-of-combat condition began (long.MinValue = not counting).
    private long _noTargetSinceMs = long.MinValue;

    // Last-fed combat flag, so the history-only provisional path can require combat.
    private bool _inCombat = true;

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

    /// <summary>
    /// Creates an estimator. The parameterless form keeps the approved defaults
    /// (history ON, 8 kills / min 3 / 240 s / p75 / 0.5), so existing callers are
    /// unchanged; the engine passes the clamped <c>[TimeToKill]</c> values in.
    /// </summary>
    public TtkEstimator(TtkOptions? options = null)
    {
        _options = options ?? TtkOptions.Default;
        _history = new KillHistory(_options.Kills, _options.MaxAgeSec * 1000L);
    }

    /// <summary>The last computed output (never null; invalid until a real decline).</summary>
    public TtkEstimate Estimate => _estimate;

    /// <summary>The configured adaptive-history tuning (read-only; tests can pin defaults).</summary>
    public TtkOptions Options => _options;

    /// <summary>The rolling kill window (read-only seam for tests/telemetry).</summary>
    public KillHistory History => _history;

    /// <summary>Learning epoch: increments on every reset, so callers can observe re-learns.</summary>
    public int Epoch => _epoch;

    /// <summary>
    /// True while the fast-pack latch is set (survives target swaps and resets).
    /// Clock-aware: a hold whose deadline has already passed reports false even
    /// before the next <see cref="Update"/> clears it.
    /// </summary>
    public bool FastPackLatch =>
        _latchUntilMs != long.MinValue && _nowMs < _latchUntilMs;

    /// <summary>
    /// Clears all learned target state (session start / kill-switch off) AND the
    /// rolling kill window. The latch is kept. <see cref="RotationEngine.Start"/>
    /// calls this, so a Stop/Start must not let a previous session's trash
    /// history throttle a fresh pull (the safer opener).
    /// </summary>
    public void Reset()
    {
        ClearTargetState();
        _history.Clear();
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
        _inCombat = inCombat;

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

            // v3.7: drop the learned window once the player has been idle (no
            // target, out of combat) long enough that it no longer describes
            // the current pack.
            if (!inCombat)
            {
                if (_noTargetSinceMs == long.MinValue) _noTargetSinceMs = nowMs;
                else if (nowMs - _noTargetSinceMs >= (long)(HistoryClearAfterSec * 1000.0))
                    _history.Clear();
            }
            else
            {
                _noTargetSinceMs = long.MinValue;
            }

            _estimate = ApplyLatch(new TtkEstimate(false, MaxTtkSec, 0), nowMs);
            return _estimate;
        }
        _noTargetSinceMs = long.MinValue;

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
            _firstFrac = frac;
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
            // An upward jump is ambiguous: a new target OR a big heal. The
            // v3.6 fast-pack latch still scores it, but the v3.7 kill history
            // must not (a heal from low HP would otherwise be a false kill).
            RecordDeparture(nowMs, healJump: true);
            ClearTargetState();
            _havePrev = true;
            _prevFrac = frac;
            _prevMs = nowMs;
            _feedFrac = frac;
            _feedMs = nowMs;
            _firstObsMs = nowMs;
            _refMs = nowMs;
            _refFrac = frac;
            _firstFrac = frac;
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

        TtkEstimate estimate;
        if (valid)
        {
            _lowSeed = false;
            _refMs = nowMs;
            _refFrac = frac;
            var ttk = Math.Clamp(frac / _ewma, 0.0, MaxTtkSec);
            estimate = ApplyLatch(new TtkEstimate(true, ttk, frac, false, spanSec), nowMs);
        }
        else
        {
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
                estimate = ApplyLatch(new TtkEstimate(false, fastTtk, frac, true, spanSec), nowMs);
            }
            else if (_lowSeed)
            {
                estimate = ApplyLatch(new TtkEstimate(false, frac * 15.0, frac, true, spanSec), nowMs);
            }
            else
            {
                estimate = ApplyLatch(new TtkEstimate(false, MaxTtkSec, frac, false, spanSec), nowMs);
            }
        }

        return ApplyHistory(estimate, nowMs, frac, spanSec, valid);
    }

    /// <summary>
    /// v3.7 adaptive-history blend. When the rolling window is binding
    /// (History ON and Count &gt;= MinKills) the pessimistic p-quantile history
    /// rate <c>rPq</c> is merged with the live EWMA:
    /// <c>w = clamp((spanSec-2.5)/6, 0, 1)</c> and
    /// <c>rateEff = ewma + (1-w)·max(0, rPq-ewma)</c> on a valid live estimate
    /// (history lifts a too-slow live rate early; the live rate wins by ~8.5 s).
    /// With no live estimate but a target, in combat and a usable frac, the
    /// history rate alone produces a provisional estimate. Everything else is
    /// left byte-identical to a no-history run (fail-open).
    /// </summary>
    private TtkEstimate ApplyHistory(TtkEstimate estimate, long nowMs, double frac, double spanSec, bool liveValid)
    {
        if (!_options.History || frac <= 0) return estimate;

        _history.Prune(nowMs);
        if (_history.Count < _options.MinKills) return estimate;

        var rate = _history.RateQuantile(_options.Quantile);
        var medianLife = _history.MedianLifeSec();
        var kills = _history.Count;

        if (liveValid && _ewma > 0)
        {
            var w = Math.Clamp((spanSec - MinSpanSec) / HistoryBlendSpanSec, 0.0, 1.0);
            var rateEff = _ewma + (1.0 - w) * Math.Max(0.0, rate - _ewma);
            var ttk = Math.Clamp(frac / rateEff, 0.0, MaxTtkSec);
            return estimate with
            {
                HistBinding = true,
                HistKills = kills,
                HistRate = rate,
                HistTtkSec = ttk,
                HistProvisional = false,
                HistMedianLifeSec = medianLife,
                NeedDurFactor = _options.DurFactor,
            };
        }

        if (_inCombat && rate > 0)
        {
            var ttk = Math.Clamp(frac / rate, 0.0, MaxTtkSec);
            return estimate with
            {
                HistBinding = true,
                HistKills = kills,
                HistRate = rate,
                HistTtkSec = ttk,
                HistProvisional = true,
                HistMedianLifeSec = medianLife,
                NeedDurFactor = _options.DurFactor,
            };
        }

        // Binding but no usable value this tick (no live rate, out of combat, or
        // a zero rate): expose the window for telemetry, leave HistTtkSec at 0.
        return estimate with
        {
            HistBinding = true,
            HistKills = kills,
            HistRate = rate,
            HistMedianLifeSec = medianLife,
            NeedDurFactor = _options.DurFactor,
        };
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
    /// <paramref name="healJump"/> marks a departure caused by an upward HP jump
    /// (new target or big heal) — the v3.6 latch still scores it, but the v3.7
    /// kill history vetoes it so a heal is never recorded as a kill.
    /// </summary>
    private void RecordDeparture(long nowMs, bool healJump = false)
    {
        if (!_havePrev || _firstObsMs == long.MinValue) return;

        var lifeSec = (nowMs - _firstObsMs) / 1000.0;
        var lastFrac = _prevFrac;

        // Fast-pack latch (v3.6, unchanged): a short-lived low-HP target.
        if (lifeSec <= KillMaxLifeSec && lastFrac <= KillMaxLastFrac)
        {
            _killTimes.Enqueue(nowMs);
            while (_killTimes.Count > 0 && nowMs - _killTimes.Peek() > (long)(LatchWindowSec * 1000.0))
                _killTimes.Dequeue();
            if (_killTimes.Count >= LatchKills)
                _latchUntilMs = nowMs + (long)(LatchHoldSec * 1000.0);
        }

        // Adaptive history (v3.7): a kill is a low-HP departure. This method is
        // never reached from the sustained-unknown reset, so a target that
        // merely vanished under unknown HP is not counted. A valid record also
        // needs a real lifetime and a real HP loss (spawn-at-20% mobs dropped).
        if (!_options.History || healJump || lastFrac > KillMaxLastFrac) return;
        var delta = _firstFrac - lastFrac;
        if (delta <= 0 || lifeSec < HistoryValidMinLifeSec || delta < HistoryValidMinDeltaFrac) return;
        _history.Add(new KillRecord(nowMs, lifeSec, _firstFrac, lastFrac));
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
        _firstFrac = 0;
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
