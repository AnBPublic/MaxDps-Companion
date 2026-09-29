namespace MaxDpsCompanion;

/// <summary>
/// The estimator's per-tick output. <see cref="TargetHpFrac"/> is always the
/// last observed fraction (0 when nothing has been observed); <see cref="TtkSec"/>
/// is only meaningful when <see cref="Valid"/> is true, but is clamped to
/// 0..<see cref="TtkEstimator.MaxTtkSec"/> even when invalid (a trickle reads as
/// "long", never as an unbounded number).
/// </summary>
internal readonly record struct TtkEstimate(bool Valid, double TtkSec, double TargetHpFrac)
{
    /// <summary>The fail-safe "no estimate" value: invalid and clamped long.</summary>
    public static readonly TtkEstimate Invalid = new(false, TtkEstimator.MaxTtkSec, 0);

    /// <summary>Rounded seconds for reasons/telemetry (one decimal).</summary>
    public double RoundedSec => Math.Round(TtkSec, 1);
}

/// <summary>
/// Per-target time-to-kill estimator (v3.2.0). Pure and fake-clock: every input
/// is a decoded protocol field (<c>nowMs</c>, <c>hasTarget</c>,
/// <c>targetHpValid</c>, <c>targetHpBand</c>); the class reads no clock, no I/O
/// and no game value.
///
/// Contract (§3.1 of the TTK plan):
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
/// Unknown HP for a short flicker marks the output stale (invalid) but does NOT
/// reset the learned rate; a sustained unknown past 10 s resets. Unknown always
/// fails open upstream: every TTK gate is skipped when the estimate is invalid.
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

    /// <summary>EWMA time constant (seconds) for the decline-rate smoother.</summary>
    private const double RateTauSec = 3.0;

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

    private TtkEstimate _estimate = TtkEstimate.Invalid;

    /// <summary>The last computed output (never null; invalid until a real decline).</summary>
    public TtkEstimate Estimate => _estimate;

    /// <summary>Learning epoch: increments on every reset, so callers can observe re-learns.</summary>
    public int Epoch => _epoch;

    /// <summary>Clears all learned state (session start / kill-switch off).</summary>
    public void Reset()
    {
        ClearLearning();
        _prevMs = 0;
        _prevFrac = 0;
        _havePrev = false;
        _epoch++;
        _estimate = TtkEstimate.Invalid;
    }

    /// <summary>
    /// Feeds one tick and returns the current estimate. Callers invoke this once
    /// per decoded frame, before the policy runs; a frame with no target or no
    /// valid target HP degrades to invalid (and eventually resets).
    /// </summary>
    public TtkEstimate Update(long nowMs, bool hasTarget, bool targetHpValid, int targetHpBand)
    {
        // No target: nothing to estimate. A later target starts a fresh epoch.
        if (!hasTarget)
        {
            ClearLearning();
            _havePrev = false;
            _prevMs = nowMs;
            _prevFrac = 0;
            _epoch++;
            _estimate = TtkEstimate.Invalid;
            return _estimate;
        }

        // HP unknown: keep the learned rate for a short flicker, mark stale, and
        // reset once the gap exceeds the sustained-unknown window.
        if (!targetHpValid || targetHpBand is < 0 or > 14)
        {
            if (_unknownSinceMs == long.MinValue) _unknownSinceMs = nowMs;
            if ((nowMs - _unknownSinceMs) / 1000.0 > UnknownResetSec)
            {
                ClearLearning();
                _havePrev = false;
                _prevMs = nowMs;
                _epoch++;
                _estimate = TtkEstimate.Invalid;
                return _estimate;
            }
            _estimate = new TtkEstimate(false, _estimate.TtkSec, _prevFrac);
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
            _estimate = new TtkEstimate(false, MaxTtkSec, frac);
            return _estimate;
        }

        var dtMs = nowMs - _prevMs;
        if (dtMs <= 0) return _estimate; // duplicate/same-instant tick: no new information

        // A jump UP (new target or big heal) invalidates the learned rate.
        if (frac - _prevFrac > JumpResetFrac)
        {
            ClearLearning();
            _havePrev = true;
            _prevFrac = frac;
            _prevMs = nowMs;
            _feedFrac = frac;
            _feedMs = nowMs;
            _epoch++;
            _estimate = new TtkEstimate(false, MaxTtkSec, frac);
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
            _estimate = Compute(nowMs, frac);
            return _estimate;
        }

        // Noise/heal: not a decline. A heal re-anchors at the new level so the
        // next real decline is measured from there; a flat tick keeps the anchor
        // (its elapsed time is part of the next measured decline). The previous
        // estimate is kept verbatim except for the exposed current fraction.
        if (frac > _feedFrac)
        {
            _feedFrac = frac;
            _feedMs = nowMs;
        }
        _estimate = _estimate with { TargetHpFrac = frac };
        return _estimate;
    }

    private TtkEstimate Compute(long nowMs, double frac)
    {
        if (_samples == 0) return new TtkEstimate(false, MaxTtkSec, frac);
        var spanSec = _firstObsMs == long.MinValue ? 0 : (nowMs - _firstObsMs) / 1000.0;
        var valid = _samples >= MinSamples && spanSec >= MinSpanSec && _ewma >= MinRate;
        var ttk = valid ? Math.Clamp(frac / _ewma, 0.0, MaxTtkSec) : MaxTtkSec;
        return new TtkEstimate(valid, ttk, frac);
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
