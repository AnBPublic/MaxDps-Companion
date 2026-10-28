// Cadence 4.6.0: GCD-perfect delivery ("landing"), the opener-release window
// and the adaptive-polling window. Everything here is PURE: every timestamp
// comes from the caller's NowMs (the engine clock), never from a wall clock, so
// --replay and the unit tests reproduce the exact same decisions.
//
// Why a planner at all: the strip is a 30 Hz sampling of the game, so its
// on-GCD flag falls up to one poll + one capture late. Waiting for the flag to
// fall therefore adds that latency to every GCD-riding press. Ext5 (cell 53)
// and the observed GCD edges let the scheduler PREDICT the end of the current
// GCD, and the client's own spell-queue window (~400 ms) accepts a press sent
// slightly before the GCD ends and casts it the moment the GCD falls. The
// pre-fire is therefore a queued press, not an early one.
//
// Safety shape (the reason every constant here is bounded):
//  * the pre-fire only exists while the bridge reports an ACTIVE GCD, so the
//    press can never start a pull, a wasted off-GCD cast or a double GCD;
//  * the learned lead is clamped to 10..150 ms - far inside the client's
//    ~400 ms spell-queue window - so an early pre-fire is queued, not lost;
//  * at most ONE pre-fire per GCD (a per-GCD latch, not a queue), and the
//    latch expires after one estimated GCD + the reject window so a missed
//    flag edge can never silence the planner forever;
//  * a pre-fire that starts no GCD is an ordinary press: it enters the
//    existing RejectDetectMs (600 ms) failure window unchanged;
//  * Landing=Off never reaches this code (the scheduler checks the toggle), so
//    the 4.5.8 order and pacing stay byte-identical.
//
// MinKeyIntervalMs contract (unchanged): the pre-fire is a SCHEDULED stroke, so
// it bypasses the min key interval exactly like the interrupt/Main fast paths.
// Every UNSCHEDULED stroke is still gated by MinKeyIntervalMs exactly as today
// (see ActionScheduler.MinIntervalApplies).

namespace MaxDpsCompanion;

/// <summary>The landing planner's three states (plan §3: Idle -> Armed -> Fired).</summary>
internal enum LandingState
{
    /// <summary>No candidate is armed for this GCD.</summary>
    Idle = 0,

    /// <summary>A candidate identity is armed and waiting for the predicted GCD edge.</summary>
    Armed = 1,

    /// <summary>
    /// This GCD's single pre-fire has been committed. Set only by
    /// <see cref="LandingPlanner.CommitFire"/>, i.e. after the scheduler really
    /// emitted the stroke - never by <see cref="LandingPlanner.Plan"/> itself.
    /// </summary>
    Fired = 2,
}

/// <summary>What the planner wants the scheduler to do with the GCD-riding head.</summary>
internal enum LandingAction
{
    /// <summary>Nothing to say: the planner is inert this tick (returns today's behaviour).</summary>
    None = 0,

    /// <summary>Arm and wait: the predicted GCD edge is still ahead of the lead.</summary>
    Hold = 1,

    /// <summary>Fire the armed head now (one per GCD).</summary>
    Fire = 2,

    /// <summary>The armed identity or verdict changed between Arm and Fire: re-plan.</summary>
    Abort = 3,
}

/// <summary>
/// The identity + verdict signature the planner arms. A change in ANY field
/// between Arm and Fire aborts the pre-fire: a stroke the planner did not arm
/// (a different slot, key, spell, verdict or urgency) is never pre-fired.
/// </summary>
internal readonly record struct LandingCandidate(
    Slot Slot,
    KeyStroke Stroke,
    int SpellId,
    PolicyVerdict Verdict,
    ScheduleReason Reason,
    bool Emergency)
{
    /// <summary>Full signature equality (identity + verdict + urgency + emergency flag).</summary>
    public bool SameAs(LandingCandidate other) =>
        Slot == other.Slot
        && Stroke == other.Stroke
        && SpellId == other.SpellId
        && Verdict == other.Verdict
        && Reason == other.Reason
        && Emergency == other.Emergency;

    /// <summary>Physical identity equality only (used to name the abort in telemetry).</summary>
    public bool SameIdentity(LandingCandidate other) =>
        Slot == other.Slot && Stroke == other.Stroke && SpellId == other.SpellId;
}

/// <summary>
/// One landing decision. <see cref="PredictedGcdEndMs"/> and <see cref="LeadMs"/>
/// ride telemetry (<c>Timing</c>) so a pre-fire can be audited after the fact.
/// </summary>
internal readonly record struct LandingDecision(
    LandingAction Action,
    string Reason,
    long PredictedGcdEndMs,
    int LeadMs,
    int CandidateSpell)
{
    /// <summary>The inert value: no state change, no telemetry.</summary>
    public static readonly LandingDecision None = new(LandingAction.None, "", 0, 0, 0);

    /// <summary>True when the scheduler may emit the armed stroke this tick.</summary>
    public bool Firing => Action == LandingAction.Fire;
}

/// <summary>
/// 4.6.0 "Cadence" GCD-perfect delivery. Pure state machine + two EWMAs, driven
/// entirely by (onGcd, Ext5, NowMs) - see the file header for the contract.
/// </summary>
internal sealed class LandingPlanner
{
    // ---- estimator / lead tuning (plan §3; every value is pinned by tests) --

    /// <summary>Seed for the estimated GCD length (ms).</summary>
    internal const int SeedGcdMs = 1500;

    /// <summary>Lower clamp for the estimated GCD length (ms).</summary>
    internal const int MinGcdMs = 750;

    /// <summary>Upper clamp for the estimated GCD length (ms).</summary>
    internal const int MaxGcdMs = 2000;

    /// <summary>Seed for the send-to-observed-edge latency (ms).</summary>
    internal const int SeedLatencyMs = 40;

    /// <summary>Lower clamp for the learned lead (ms).</summary>
    internal const int MinLatencyMs = 10;

    /// <summary>Upper clamp for the learned lead (ms).</summary>
    internal const int MaxLatencyMs = 150;

    /// <summary>EWMA smoothing used by both estimators (alpha; deterministic).</summary>
    internal const double Alpha = 0.2;

    /// <summary>
    /// Longest send-to-edge gap still credited as OUR latency sample. A wider
    /// gap means the edge came from some other GCD (a failed pre-fire), so the
    /// sample is discarded instead of poisoning the lead.
    /// </summary>
    internal const int MaxLatencySampleMs = 500;

    /// <summary>
    /// How long a landed insurance/landing decision is remembered after a send
    /// before the per-cast / per-GCD latch is allowed to expire on its own.
    /// Mirrors the scheduler's RejectDetectMs: a press that produced nothing is
    /// judged there.
    /// </summary>
    internal const int LatchGraceMs = 600;

    /// <summary>
    /// Sentinel for "no observed GCD start". Deliberately NOT 0: the engine
    /// clock starts at 0, so a real GCD can legitimately start at ms 0.
    /// </summary>
    internal const long NoGcd = long.MinValue;

    private bool _lastOnGcd;
    private long _gcdStartMs = NoGcd;
    private long _gcdEndMs;
    private double _gcdEstimateMs = SeedGcdMs;
    private double _latencyMs = SeedLatencyMs;
    private LandingState _state = LandingState.Idle;
    private LandingCandidate? _armed;
    private long _firedForGcdStartMs = NoGcd;
    private long _firedAtMs;
    private bool _awaitingLatency;
    private long _pendingSendAtMs;

    /// <summary>Current state (Idle/Armed/Fired) - diagnostics + tests.</summary>
    public LandingState State => _state;

    /// <summary>Estimated GCD length (ms, clamped 750..2000).</summary>
    public int EstimatedGcdMs => (int)Math.Round(_gcdEstimateMs);

    /// <summary>Learned lead (ms, clamped 10..150).</summary>
    public int LatencyMs => (int)Math.Round(_latencyMs);

    /// <summary>The last predicted GCD end (0 = never predicted) - adaptive polling reads it.</summary>
    public long PredictedGcdEndMs => _gcdEndMs;

    /// <summary>Observed start of the current GCD, or <see cref="NoGcd"/> when none was seen.</summary>
    internal long ObservedGcdStartMs => _gcdStartMs;

    /// <summary>Drops all history (engine Start / scheduler Reset).</summary>
    public void Reset()
    {
        _lastOnGcd = false;
        _gcdStartMs = NoGcd;
        _gcdEndMs = 0;
        _gcdEstimateMs = SeedGcdMs;
        _latencyMs = SeedLatencyMs;
        _state = LandingState.Idle;
        _armed = null;
        _firedForGcdStartMs = NoGcd;
        _firedAtMs = 0;
        _awaitingLatency = false;
        _pendingSendAtMs = 0;
    }

    /// <summary>
    /// Feeds one decoded frame's on-GCD flag. Rising/falling edges update the
    /// GCD-length estimator; a rising edge that follows a pre-fire samples the
    /// learned lead. Call once per tick, before <see cref="PredictEnd"/>.
    /// </summary>
    public void Observe(bool onGcd, long nowMs)
    {
        if (onGcd && !_lastOnGcd)
        {
            // A GCD started. If we just pre-fired, this edge IS the latency
            // sample (send -> the strip actually showing the GCD).
            if (_awaitingLatency)
            {
                var sample = nowMs - _pendingSendAtMs;
                if (sample is > 0 and <= MaxLatencySampleMs) SampleLatency(sample);
                _awaitingLatency = false;
            }
            _gcdStartMs = nowMs;
            _gcdEndMs = nowMs + (long)Math.Round(_gcdEstimateMs);
            _firedForGcdStartMs = NoGcd;
            _state = LandingState.Idle;
            _armed = null;
        }
        else if (!onGcd && _lastOnGcd)
        {
            // The GCD ended: the on-GCD window is a GCD-length sample.
            if (_gcdStartMs != NoGcd && nowMs > _gcdStartMs) SampleGcd(nowMs - _gcdStartMs);
            _gcdEndMs = nowMs;
            _gcdStartMs = NoGcd;
            _state = LandingState.Idle;
            _armed = null;
        }
        else if (onGcd && _gcdStartMs == NoGcd)
        {
            // First observation of an already-running GCD (engine/session start):
            // start the clock here rather than inventing a start time.
            _gcdStartMs = nowMs;
            _gcdEndMs = nowMs + (long)Math.Round(_gcdEstimateMs);
        }
        _lastOnGcd = onGcd;
    }

    /// <summary>
    /// Predicts when the current GCD ends, from NowMs.
    ///
    /// Live Ext5 wins when the addon actually measured a GCD (bucket &gt; 1):
    /// the bucket is a 100 ms lower bound, so the prediction is the bucket's
    /// UPPER edge (the midpoint snapped up to the edge). Pressing at the end of
    /// the bucket is safe for any true value inside it; the learned lead then
    /// compensates for the transport delay. This is deviation D5: the bucket
    /// carries a 0-100 ms LATE bias (a true 0 ms reading is predicted as 100 ms
    /// remaining), chosen over the plan's midpoint because a late pre-fire is
    /// queued and lands on the GCD while an early one can be dropped. Bucket 1
    /// ("not on GCD") = the GCD ends now. Bucket 0 / no Ext5 = the estimator
    /// (last observed rising edge + the EWMA GCD length).
    /// </summary>
    /// <returns>The predicted end in engine ms, or 0 when nothing is known.</returns>
    public long PredictEnd(Ext5Block? ext5, long nowMs)
    {
        if (ext5 is { } block)
        {
            if (block.GcdBucket == 1) return nowMs;
            if (block.GcdBucket > 1)
                return nowMs + block.GcdRemainMinMs + PixelProtocol.Ext5GcdStepMs;
        }
        if (_gcdStartMs != NoGcd) return _gcdStartMs + (long)Math.Round(_gcdEstimateMs);
        return 0;
    }

    /// <summary>
    /// One plan step for the GCD-riding head candidate.
    ///
    /// <see cref="LandingAction.Fire"/> is an INTENT, not a press (4.6.0
    /// review m3): the planner does not set the Fired latch, the one-per-GCD
    /// deadline or the latency sample here. The scheduler calls
    /// <see cref="CommitFire"/> only after the stroke actually reached the
    /// plan's action list, so a pre-fire that a send gate holds stays re-plannable
    /// and never fabricates a latency sample.
    /// </summary>
    /// <param name="head">The highest-ranked candidate that rides the GCD (null = none).</param>
    /// <param name="onGcd">The bridge's own GCD flag for this tick.</param>
    /// <param name="ext5">The decoded Ext5 block (null on a pre-Ext5 addon).</param>
    /// <param name="nowMs">Engine clock.</param>
    /// <param name="enabled">False (Landing=Off) makes this a pure no-op.</param>
    public LandingDecision Plan(
        LandingCandidate? head, bool onGcd, Ext5Block? ext5, long nowMs, bool enabled)
    {
        if (!enabled) return LandingDecision.None;

        // Outside a live GCD there is nothing to pre-fire: the ordinary path is
        // already free to send. The armed state is dropped (a new GCD re-arms).
        if (!onGcd)
        {
            _armed = null;
            _state = LandingState.Idle;
            return LandingDecision.None;
        }

        var end = PredictEnd(ext5, nowMs);
        var lead = LatencyMs;
        var fireAt = end - lead;

        if (head is not { } candidate)
        {
            _armed = null;
            _state = LandingState.Idle;
            return LandingDecision.None;
        }

        // One pre-fire per GCD. The latch is keyed on the observed GCD start so
        // a new edge re-arms it; a missed edge cannot latch forever because the
        // deadline below expires it.
        if (_firedForGcdStartMs != NoGcd && _firedForGcdStartMs == _gcdStartMs)
        {
            if (nowMs - _firedAtMs <= (long)Math.Round(_gcdEstimateMs) + LatchGraceMs)
                return new LandingDecision(LandingAction.Hold,
                    "landing: already pre-fired this GCD", end, lead, candidate.SpellId);
            _firedForGcdStartMs = NoGcd;
            _state = LandingState.Idle;
        }

        // Identity / verdict change between Arm and Fire: never press a stroke
        // the planner did not arm. Re-plan from Idle with the new signature.
        if (_armed is { } armed && !armed.SameAs(candidate))
        {
            _armed = candidate;
            _state = LandingState.Armed;
            return new LandingDecision(LandingAction.Abort,
                armed.SameIdentity(candidate)
                    ? "landing: verdict changed before the GCD edge"
                    : "landing: candidate changed before the GCD edge",
                end, lead, candidate.SpellId);
        }

        if (nowMs < fireAt)
        {
            _armed = candidate;
            _state = LandingState.Armed;
            return new LandingDecision(LandingAction.Hold,
                "landing: armed before the predicted GCD edge", end, lead, candidate.SpellId);
        }

        _armed = candidate;
        // The intent is armed, NOT fired: the Fired latch is spent by
        // CommitFire once the scheduler emitted this exact stroke.
        _state = LandingState.Armed;
        return new LandingDecision(LandingAction.Fire,
            "landing: pre-fire at the predicted GCD edge", end, lead, candidate.SpellId);
    }

    /// <summary>
    /// Commits a <see cref="LandingAction.Fire"/> returned by <see cref="Plan"/>
    /// once the scheduler actually emitted that exact stroke. Sets the Fired
    /// latch (one pre-fire per GCD) and arms the send-to-edge latency sample.
    /// Idempotent per GCD: the latch makes the next <see cref="Plan"/> answer
    /// "already pre-fired".
    /// </summary>
    internal void CommitFire(long nowMs)
    {
        _state = LandingState.Fired;
        _firedForGcdStartMs = _gcdStartMs;
        _firedAtMs = nowMs;
        _awaitingLatency = true;
        _pendingSendAtMs = nowMs;
    }

    private void SampleGcd(long sampleMs)
    {
        // Clamp AFTER the EWMA so one pathological window (a held flag, a
        // cancelled GCD, a frozen link) can never move the estimate outside the
        // approved band, while ordinary windows still converge normally.
        _gcdEstimateMs = Math.Clamp(
            Alpha * sampleMs + (1 - Alpha) * _gcdEstimateMs, MinGcdMs, MaxGcdMs);
    }

    private void SampleLatency(long sampleMs)
    {
        _latencyMs = Math.Clamp(
            Alpha * sampleMs + (1 - Alpha) * _latencyMs, MinLatencyMs, MaxLatencyMs);
    }
}

/// <summary>
/// 4.6.0 "Cadence" opener release: a rising edge on InCombat whose decoded Ext5
/// rank is BOSS-GRADE (6 worldboss / 7 elite-skull) opens a 2500 ms window in
/// which the scheduler pre-ranks one ready Offensive major ahead of the first
/// Main. Every other rank (elite, rare, rareelite, normal, trivial, UNKNOWN)
/// never opens it, so trash keeps today's order exactly.
///
/// The detector needs to have seen the out-of-combat frame first: a session that
/// starts mid-fight cannot know the pull happened, which is the conservative
/// outcome (no window, today's behaviour).
/// </summary>
internal sealed class OpenerWindow
{
    /// <summary>How long the released opener stays open after a boss-grade pull.</summary>
    internal const int WindowMs = 2500;

    private bool _hasFrame;
    private bool _lastInCombat;
    private long _untilMs;

    /// <summary>True on the frame that OPENED the window (telemetry: one event per pull).</summary>
    public bool OpenedThisFrame { get; private set; }

    /// <summary>Feeds one decoded frame. Returns true while the window is open.</summary>
    public bool Observe(bool inCombat, Ext5Block? ext5, bool enabled, long nowMs)
    {
        OpenedThisFrame = false;
        var rising = _hasFrame && inCombat && !_lastInCombat;
        _hasFrame = true;
        _lastInCombat = inCombat;

        if (!enabled || !inCombat)
        {
            _untilMs = 0;
            return false;
        }
        if (rising && ext5 is { BossGrade: true })
        {
            _untilMs = nowMs + WindowMs;
            OpenedThisFrame = true;
        }
        return Active(nowMs);
    }

    /// <summary>True while the 2500 ms boss window is still open.</summary>
    public bool Active(long nowMs) => _untilMs > 0 && nowMs < _untilMs;

    /// <summary>Window end in engine ms (0 = closed) - diagnostics/tests.</summary>
    internal long UntilMs => _untilMs;

    /// <summary>Drops all history (engine Start).</summary>
    public void Reset()
    {
        _hasFrame = false;
        _lastInCombat = false;
        _untilMs = 0;
        OpenedThisFrame = false;
    }
}

/// <summary>
/// 4.6.0 "Cadence" adaptive polling: burst the poll cadence only inside the
/// predicted GCD-edge window. Pure so both engines (Core and Windows) can share
/// the exact predicate and the tests can pin it.
///
/// Everything else stays authoritative: the caller applies this to the wait it
/// already computed (idle pacing included) and the duty-cycle guard still runs
/// afterwards, so a slow capture path can never be made faster than it can
/// sustain. When the prediction is unknown (0) the configured cadence is used.
/// </summary>
internal static class CadencePacing
{
    /// <summary>The burst cadence while the GCD edge is imminent (ms).</summary>
    internal const int BurstMs = 16;

    /// <summary>
    /// How long BEFORE the predicted GCD edge the burst starts (ms). Named for
    /// its use: "now" is this far ahead of the edge, i.e.
    /// <c>predictedEnd - nowMs</c> = +300.
    /// </summary>
    internal const int BeforeEdgeMs = 300;

    /// <summary>
    /// How long AFTER the predicted GCD edge the burst continues (ms). The
    /// flag/capture can land on either side of the edge, so the window is
    /// asymmetric: "now" is this far past the edge (<c>predictedEnd - nowMs</c>
    /// = -50).
    /// </summary>
    internal const int AfterEdgeMs = 50;

    /// <summary>True when NowMs sits inside the predicted GCD-edge window.</summary>
    internal static bool NearGcdEdge(long predictedGcdEndMs, long nowMs)
    {
        if (predictedGcdEndMs <= 0) return false;
        var delta = predictedGcdEndMs - nowMs;
        return delta >= -AfterEdgeMs && delta <= BeforeEdgeMs;
    }

    /// <summary>
    /// The wait to use for the next poll: the burst cadence inside the window,
    /// the caller's configured wait everywhere else. Idle pacing wins: with no
    /// target there is no GCD worth bursting for.
    /// </summary>
    internal static int AdaptiveWait(int wait, bool adaptive, bool idle, long predictedGcdEndMs, long nowMs) =>
        adaptive && !idle && NearGcdEdge(predictedGcdEndMs, nowMs)
            ? Math.Min(wait, BurstMs)
            : wait;
}

/// <summary>
/// The 4.6.0 feature switches the scheduler needs. Default = <see cref="Off"/>
/// for every field, so an unconfigured scheduler (every pre-4.6 caller and every
/// test that does not opt in) is byte-identical to 4.5.8.
/// </summary>
internal readonly record struct CadenceOptions(
    bool InsuranceEnabled,
    int InsuranceMinCastAgeMs,
    bool LandingAuto,
    bool OpenerRelease,
    bool OpenerWindowActive)
{
    /// <summary>Every 4.6.0 feature off (the 4.5.8 contract).</summary>
    public static readonly CadenceOptions Off = new(false, 1000, false, false, false);

    /// <summary>True when the opener pre-rank may reorder this tick.</summary>
    public bool OpenerPromotes => OpenerRelease && OpenerWindowActive;
}

/// <summary>
/// One timing decision for telemetry (<c>TelemetryPolicy.Timing</c>). Exactly
/// one is recorded per tick, in this precedence order: insurance, landing
/// pre-fire, landing abort, opener. Null on every other tick, so pre-4.6
/// recordings and Landing=Off replays stay byte-identical.
/// </summary>
internal readonly record struct SchedulerTiming(
    string Kind,
    string Reason,
    long PredictedGcdEndMs,
    int LeadMs,
    int CandidateSpell)
{
    /// <summary>Kind for an interrupt-insurance press.</summary>
    public const string KindInsurance = "insurance";

    /// <summary>Kind for a landed pre-fire.</summary>
    public const string KindLandPrefire = "LandPrefire";

    /// <summary>Kind for an aborted landing (identity/verdict changed).</summary>
    public const string KindLandAbort = "LandAbort";

    /// <summary>Kind for the opener-release window edge.</summary>
    public const string KindOpener = "opener";
}
