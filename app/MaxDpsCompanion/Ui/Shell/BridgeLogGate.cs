namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// 4.4.0 "Wayfinder": quiet bridge log. Alt-tabbing pauses detection (the
/// engine's foreground gate), so "no bridge sample" is EXPECTED while the game
/// is in the background and must never be logged. This gate is the single
/// arbiter of the one warning that IS worth a line:
///
///  * never established (bridge never seen this run) → warn once after 5 s of
///    foreground-without-frame, measured from engine start;
///  * established then lost → warn once per outage, 5 s after the outage began;
///  * recovery re-arms the next outage;
///  * background time never accrues and never warns;
///  * stopping the engine resets the whole run state.
///
/// Pure and clock-injected (<c>long nowMs</c>) so the timing is unit-testable;
/// the shell passes <see cref="System.Environment.TickCount64"/>. No clock is
/// read here.
/// </summary>
internal sealed class BridgeLogGate
{
    /// <summary>Continuous foreground-without-frame before the one warning.</summary>
    public const long WarnAfterMs = 5000;

    private bool _running;
    private bool _everSeen;
    private bool _warned;
    // Foreground clock references; null while no measurement is in progress.
    private long? _runStartMs;
    private long? _outageStartMs;

    /// <summary>True once the bridge decoded a frame this run (diagnostics/tests).</summary>
    public bool EverSeen => _everSeen;

    /// <summary>Drops run/seen/warned/outage state (a new engine run).</summary>
    public void Reset()
    {
        _running = false;
        _everSeen = false;
        _warned = false;
        _runStartMs = null;
        _outageStartMs = null;
    }

    /// <summary>
    /// Observes one UI tick. Returns true EXACTLY once per outage/run when the
    /// bridge has been invisible in the foreground for 5 s; every other
    /// transition returns false.
    /// </summary>
    public bool Observe(bool bridgeVisible, bool running, bool foreground, long nowMs)
    {
        if (!running)
        {
            // Stop (or not started): a later Start is a brand-new run.
            Reset();
            return false;
        }

        if (!_running)
        {
            _running = true;
            _runStartMs = nowMs;
            _outageStartMs = null;
            _warned = false;
        }

        if (!foreground)
        {
            // Background is a pause, not an outage: no clock accrues and nothing
            // warns. The never-seen clock restarts from foreground return so a
            // long alt-tab cannot burn the 5 s threshold invisibly.
            _outageStartMs = null;
            if (!_everSeen) _runStartMs = nowMs;
            return false;
        }

        if (bridgeVisible)
        {
            _everSeen = true;
            _warned = false;
            _outageStartMs = null;
            return false;
        }

        if (_warned) return false;

        if (_everSeen)
        {
            _outageStartMs ??= nowMs;
            if (nowMs - _outageStartMs.Value < WarnAfterMs) return false;
        }
        else
        {
            _runStartMs ??= nowMs;
            if (nowMs - _runStartMs.Value < WarnAfterMs) return false;
        }

        _warned = true;
        return true;
    }
}
