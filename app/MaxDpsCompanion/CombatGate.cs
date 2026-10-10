namespace MaxDpsCompanion;

/// <summary>
/// Fail-closed out-of-combat gate shared by the engine, the scheduler and the
/// auto-target / auto-interact movement guards (2026-09-30 OOC hold fix,
/// extended for the ADDON-WINS authority flip).
///
/// Authority: the in-game overlay owns the Ext3 mirror bits. The companion is
/// permissive (the user sets everything ON to mean "let the overlay drive") and
/// only ever RESTRICTS: <see cref="AppSettings.CombatOnly"/> = true is a hard
/// kill-switch that holds all out-of-combat automation regardless of the mirror.
/// With <c>CombatOnly</c> = false the mirror OOC bit is authoritative:
/// out-of-combat automation is permitted only when the bridge echoed it AND the
/// frame is Active with a target — EXCEPT the NeedTarget / NeedInteract asks,
/// which are exactly the auto-target / auto-interact states and therefore carry
/// no Active/target requirement (the per-toggle mirror bits are checked in
/// <see cref="MovementGuard"/>). An absent / invalid Ext3 block (old 40-cell
/// addon, unsynced session) therefore fails closed. An in-combat frame always
/// passes.
///
/// This is a pure predicate so the engine, the scheduler and the movement
/// guards can never disagree, and so the whole matrix is unit-testable without
/// a game window.
/// </summary>
internal static class CombatGate
{
    /// <summary>True when the Ext3 toggle mirror is present AND its OOC bit is set.</summary>
    public static bool MirrorOutOfCombatSet(bool mirrorValid, int mirrorMask) =>
        mirrorValid && (mirrorMask & (1 << ToggleSync.BitOutOfCombat)) != 0;

    /// <summary>True when the Ext3 mirror is present AND its AutoTarget bit is set.</summary>
    public static bool MirrorAutoTargetSet(bool mirrorValid, int mirrorMask) =>
        mirrorValid && (mirrorMask & (1 << ToggleSync.BitAutoTarget)) != 0;

    /// <summary>True when the Ext3 mirror is present AND its AutoInteract bit is set.</summary>
    public static bool MirrorAutoInteractSet(bool mirrorValid, int mirrorMask) =>
        mirrorValid && (mirrorMask & (1 << ToggleSync.BitAutoInteract)) != 0;

    /// <summary>
    /// The engine/scheduler fail-closed OOC predicate. <paramref name="inCombat"/>
    /// always passes. Out of combat passes when the companion kill-switch is off
    /// (<c>CombatOnly=false</c>) and the mirror OOC bit is set, AND either the
    /// frame is Active carrying a target (rotation), or the state is
    /// NeedTarget / NeedInteract (the auto-target / auto-interact asks, which by
    /// construction have no target yet — 2026-09-30 review follow-up, so the
    /// overlay's AutoTarget/AutoInteract toggles are not dead out of combat).
    /// The per-toggle mirror bits are checked separately in
    /// <see cref="MovementGuard"/>.
    /// </summary>
    public static bool OutOfCombatPermitted(
        bool inCombat,
        bool combatOnly,
        bool mirrorOutOfCombatSet,
        BridgeState state,
        bool hasTarget) =>
        inCombat
        || (state is BridgeState.NeedTarget or BridgeState.NeedInteract
            ? MovementOutOfCombatPermitted(combatOnly, mirrorOutOfCombatSet)
            : !combatOnly
                && mirrorOutOfCombatSet
                && state == BridgeState.Active
                && hasTarget);

    /// <summary>
    /// The auto-target / auto-interact OOC permission: no Active/target
    /// requirement, because the state itself is NeedTarget / NeedInteract. The
    /// per-toggle mirror bits (AutoTarget / AutoInteract) are checked separately
    /// in <see cref="MovementGuard"/>.
    /// </summary>
    public static bool MovementOutOfCombatPermitted(bool combatOnly, bool mirrorOutOfCombatSet) =>
        !combatOnly && mirrorOutOfCombatSet;
}
