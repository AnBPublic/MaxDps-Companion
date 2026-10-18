namespace MaxDpsCompanion;

// Wave B (4.1 "Paragon"): the engine's STATUS surface lives ONCE, here.
//
// EngineStatus / LiveActionSnapshot were declared verbatim at the top of BOTH
// RotationEngine.cs files (the Windows engine and Core's IPlatform engine,
// which Wave C will merge). They are plain portable records - not engine
// implementation - so they are single-sourced in Core and both engines expose
// this one identity. That keeps the ShadowedTypeGate allow-list at exactly
// RotationEngine instead of quietly growing a second shadowed type.

/// <summary>One status update from the engine (status line + diagnostics).</summary>
internal readonly record struct EngineStatus(
    string Message,
    bool BridgeVisible,
    BridgeState State,
    string SlotSummary,
    string LastKeySent,
    string RawSample,
    string Decision);

/// <summary>
/// Immutable, secret-safe snapshot of one action (or the current plan head)
/// for the UI: name, reason, provider and a human-readable "why" list.
/// Frozen v2.7 contract - the UI codes against these exact members.
/// </summary>
public sealed record LiveActionSnapshot(string Action, string Reason, string Provider, IReadOnlyList<string> Why);
