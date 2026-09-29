namespace MaxDpsCompanion;

/// <summary>
/// Why the scheduler selected an action or held the rotation. Selection
/// reasons mirror the policy/decision urgency names; hold reasons name the
/// exact gate that blocked the tick (diagnostics + deterministic tests).
/// </summary>
internal enum ScheduleReason
{
    // Hold reasons.
    LinkLost = 0,        // heartbeat frozen past HeartbeatTimeoutMs
    StaleFrame,          // no decoded frame for this tick
    ProtocolMismatch,    // frame version is neither N nor N-1
    Paused,              // bridge reports pause (calibrate / /mdb off)
    NoTarget,            // no attackable target; nothing may fire
    NoCandidate,         // no enabled slot carries a suggestion
    GcdHold,             // only GCD-riding candidates, GCD active
    MinInterval,         // a key was sent less than MinKeyIntervalMs ago
    RepeatSuppressed,    // same stroke re-suggested, GCD state unavailable
    Unavailable,         // candidate rejected by a local gate recently
    PolicyHold,          // situational policy says HOLD (condition may clear)
    PolicySkip,          // situational policy says SKIP (out of range / redundant)
    Rejected,            // a recent press never started a GCD (failed cast)
    RetryBackoff,        // same stroke sent MaxAttempts within the window
    CastHold,            // player is casting a non-instant action
    ChannelHold,         // player is channeling and the action would clip it

    // Selection reasons (urgency of the head action).
    InterruptUrgency,
    EmergencySurvival,
    DefensiveUrgency,
    SelfSustain,
    MainRotation,
    MobilityUplift,
    OffensiveCooldown,
    Consumable,
    Trinket,

    // Hold reasons added after the selection block: APPEND ONLY so the numeric
    // value of every existing reason (and the --bench-scheduler plan hash,
    // which hashes (int)Reason) never moves.
    SelfHealCoolingDown, // HP in the sustain window, no self-heal is ready yet
}

/// <summary>Why an attempt could not be turned into a send (engine OS gates).</summary>
internal enum AttemptOutcome
{
    Sent = 0,
    MovementBound,
    PhysicalHold,
    FocusRequired,
    WindowLost,
}

/// <summary>
/// Everything the scheduler may observe for one tick. All inputs are plain
/// values produced by the pixel protocol, the candidate tracker, the
/// knowledge base and settings — no new game state.
/// </summary>
internal sealed class ScheduleInput
{
    /// <summary>Decoded frame; null models a tick with no fresh decode (stale).</summary>
    public required BridgeFrame? Frame { get; init; }
    public required ActionCandidate[] Candidates { get; init; }
    public required long NowMs { get; init; }
    public int MinKeyIntervalMs { get; init; } = 120;
    public int StaleAfterMs { get; init; } = 1500;
    public int HeartbeatTimeoutMs { get; init; } = 500;
    public int RepeatSuppressMs { get; init; } = 900;

    /// <summary>Combat context (protocol v5). Null = all-unknown context.</summary>
    public CombatContext? Context { get; init; }

    /// <summary>Situational policy options. Null = intelligence disabled (no knowledge filtering).</summary>
    public PolicyOptions? Options { get; init; }

    /// <summary>Ability knowledge base. Null = intelligence disabled.</summary>
    public AbilityCatalog? Catalog { get; init; }

    /// <summary>Collect per-candidate policy verdicts for telemetry (costs one array when set).</summary>
    public bool CollectPolicyVerdicts { get; init; }
}

/// <summary>One recorded situational decision (telemetry/explainability).</summary>
internal readonly record struct PolicyVerdictEntry(Slot Slot, int SpellId, PolicyVerdict Verdict, string Reason)
{
    /// <summary>Name of the provider that produced the verdict (v2.7).</summary>
    public string Provider { get; init; } = "";

    /// <summary>Where the candidate came from (v2.7).</summary>
    public CandidateSourceKind Source { get; init; } = CandidateSourceKind.None;

    /// <summary>Human-readable "why" facts (v2.7).</summary>
    public IReadOnlyList<string> Evidence { get; init; } = [];

    /// <summary>
    /// Ext2 (v3.0.0): this verdict is for the SelfHeal2 alternate (the primary
    /// SelfHeal candidate did not Use). The replay applies <see cref="Range"/>
    /// to the SelfHeal slot when recomputing it.
    /// </summary>
    public bool Alternate { get; init; }

    /// <summary>Ext2: the SelfHeal2 range tri-state this alternate verdict was evaluated with.</summary>
    public TriState Range { get; init; } = TriState.Unknown;

    /// <summary>
    /// R2 (sustain-cd): this SelfHeal verdict is waiting for the ability's
    /// cooldown (HP in the sustain window, no ready self-heal candidate).
    /// Telemetry-only; never changes the verdict.
    /// </summary>
    public bool CdWait { get; init; }

    /// <summary>R2: when this SelfHeal stroke was last tried (attempt/send); 0 = never.</summary>
    public long LastTriedMs { get; init; }

    /// <summary>
    /// R2: optional curated "resets on kill"-style hint for the ability, or
    /// null. Informational only — it is never read by any decision rule.
    /// </summary>
    public string? ResetHint { get; init; }
}

/// <summary>One pressable action in scheduler rank order.</summary>
internal readonly record struct ScheduledAction(
    Slot Slot,
    KeyStroke Stroke,
    int SpellId,
    ScheduleReason Reason,
    bool BypassGcd)
{
    /// <summary>Provider that produced the plan verdict (v2.7; empty on the intelligence-off path).</summary>
    public string Provider { get; init; } = "";

    /// <summary>Human-readable "why" facts carried from the plan's policy verdict (v2.7).</summary>
    public IReadOnlyList<string> Evidence { get; init; } = [];
}

/// <summary>
/// The scheduler's deterministic output: the ordered actions the engine may
/// attempt (existing per-slot OS gates stay authoritative), the head's
/// reason/confidence and diagnostics (stale demotion, suppressed counts,
/// policy detail).
/// </summary>
internal readonly record struct SchedulePlan(
    ScheduledAction[] Actions,
    Slot? Selected,
    ScheduleReason Reason,
    int Confidence,
    bool StaleDemoted,
    int Suppressed,
    int PolicyHeld,
    int PolicySkipped,
    string? PolicyDetail)
{
    /// <summary>Per-candidate policy verdicts; empty unless collection was requested.</summary>
    public PolicyVerdictEntry[] Verdicts { get; init; } = [];

    /// <summary>
    /// R2 (sustain-cd): true when the tick was in the SelfHeal cooldown wait —
    /// HP is inside the sustain window and no self-heal candidate is ready.
    /// Additive diagnostic; the action list is unchanged.
    /// </summary>
    public bool SelfHealCoolingDown { get; init; }

    /// <summary>R2: last time a SelfHeal stroke was attempted/sent (0 = never).</summary>
    public long SelfHealLastTriedMs { get; init; }

    public static SchedulePlan Hold(ScheduleReason reason, int suppressed = 0, int policyHeld = 0, int policySkipped = 0, string? detail = null) =>
        new([], null, reason, 0, false, suppressed, policyHeld, policySkipped, detail);

    public bool HasSelection => Selected is not null;
}
