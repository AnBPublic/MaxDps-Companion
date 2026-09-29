namespace MaxDpsCompanion;

/// <summary>
/// One normalized action candidate: a decoded slot suggestion joined with the
/// observation state the deterministic layers need. Since protocol v5 the
/// candidate also carries the ability's spell id (0 = unknown identity), which
/// is what the knowledge base keys on; the <see cref="KeyStroke"/> remains the
/// physical identity used for duplicate collapse and sends.
/// </summary>
internal readonly record struct ActionCandidate(
    Slot Slot,
    KeyStroke Stroke,
    bool Enabled,
    bool Actionable,
    long FirstSeenMs,
    long LastChangedMs,
    long LastPressedMs,
    bool EverPressed,
    int SpellId = 0)
{
    /// <summary>How long this stroke has been continuously suggested.</summary>
    public long AgeMs(long nowMs) => nowMs - FirstSeenMs;

    /// <summary>How long the stroke has been unchanged (drives staleness).</summary>
    public long UnchangedMs(long nowMs) => nowMs - LastChangedMs;

    /// <summary>
    /// Stale = unchanged for the whole window AND already acted on since it
    /// last changed. The "already acted on" half is essential: an idle filler
    /// suggested for minutes is not stale until a press has been spent on it.
    /// </summary>
    public bool IsStale(long nowMs, long staleAfterMs) =>
        EverPressed && UnchangedMs(nowMs) >= staleAfterMs;
}

/// <summary>Why the decision layer selected its head candidate (diagnostics + tests).</summary>
internal enum DecisionReason
{
    Fallback = 0,       // intelligence disabled: the legacy priority order
    NoCandidate,        // no enabled slot carries a suggestion
    GcdHold,            // candidates exist but none may ride the active GCD
    InterruptUrgency,   // off-GCD kick, time-critical
    DefensiveUrgency,   // reactive defensive, MaxDps HP-gated upstream
    SelfSustain,        // solo self-heal, policy HP-gated (companion-only slot)
    MainRotation,       // core rotation
    MobilityUplift,     // companion-only movement/gap-closer, policy-gated
    OffensiveCooldown,
    Consumable,
    Trinket,
}

/// <summary>
/// Everything the decision layer may observe for one tick. Built from the
/// decoded frame (combat/GCD/target/state), the candidate tracker snapshot,
/// the slot toggles and the clock. The evaluator is a pure function of this
/// context — no I/O, no randomness, no internal clock reads.
/// </summary>
internal sealed class DecisionContext
{
    public required bool InCombat { get; init; }
    public required bool OnGcd { get; init; }
    public required bool HasTarget { get; init; }
    public required BridgeState State { get; init; }
    public required long NowMs { get; init; }
    public required long StaleAfterMs { get; init; }
    public required ActionCandidate[] Candidates { get; init; }
}

/// <summary>
/// Decision output: the send order and the head's reason/confidence. The order
/// is authoritative for <c>RotationEngine.TrySendOne</c>; the metadata is for
/// status/diagnostics and tests only.
/// </summary>
internal readonly record struct DecisionResult(
    Slot[] Order,
    Slot? Selected,
    DecisionReason Reason,
    int Confidence,
    bool DemotedStale)
{
    /// <summary>
    /// Legacy behaviour: intelligence disabled. The exact pre-intelligence
    /// priority array, so the send loop is byte-identical to before.
    /// </summary>
    public static DecisionResult Fallback() =>
        new(DecisionEngine.FallbackOrder, null, DecisionReason.Fallback, 100, false);

    public bool HasSelection => Selected is not null;
}
