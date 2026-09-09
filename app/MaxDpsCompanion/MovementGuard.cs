namespace MaxDpsCompanion;

/// <summary>
/// Guards against the helper injecting character-movement input.
///
/// Sticky-movement root causes handled here:
///  1. A rotation spell bound to a movement key (WASD / Space / arrows) would
///     be tapped every tick, which looks exactly like "keeps moving after I
///     release the key". Such slots are never sent (see RotationEngine).
///  2. A TargetKey/InteractKey set to a movement key would inject movement on
///     every need-target / need-interact frame. Movement keys are rejected.
///  3. A synthetic KEYUP for a physically-held key tells WoW the key was
///     released even though the finger is still down. Slots whose key is
///     currently held are skipped (exact-VK collision only, so kiting while
///     DPSing with non-held spell keys keeps working).
///  4. CombatOnly previously existed in settings/UI but was never read, so
///     auto-target could fire out of combat too. ShouldAutoTarget /
///     ShouldAutoInteract make it functional without widening the protocol:
///     the engine remembers the last Active frame and treats a long stretch
///     without one as out of combat. (The companion-side gate; the addon-side
///     gate is the T button mode plus UnitAffectingCombat in Bridge.lua.)
/// </summary>
internal static class MovementGuard
{
    // WASD + Space + arrow keys. Q/E are deliberately excluded: they strafe by
    // default but are also common spell binds, so they are covered by the
    // physical-hold collision skip instead of a blanket block.
    private static readonly HashSet<byte> MovementKeys =
    [
        0x57, 0x41, 0x53, 0x44, // W A S D
        0x20,                   // Space (jump)
        0x25, 0x26, 0x27, 0x28, // Left Up Right Down
    ];

    public static bool IsMovementKey(byte virtualKey) => MovementKeys.Contains(virtualKey);

    public static bool IsMovementStroke(KeyStroke stroke) => IsMovementKey(stroke.VirtualKey);

    /// <summary>Parses a TargetKey-style name ("Tab", "Shift+F8") into a stroke.</summary>
    public static bool TryParseTargetKey(string text, out KeyStroke stroke)
    {
        stroke = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var shift = false;
        var ctrl = false;
        var alt = false;
        var key = text.Trim();
        foreach (var part in key.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "shift": shift = true; continue;
                case "ctrl":
                case "control": ctrl = true; continue;
                case "alt": alt = true; continue;
            }
            if (!Enum.TryParse<System.Windows.Forms.Keys>(part, true, out var parsed)) return false;
            stroke = new KeyStroke((byte)parsed, shift, ctrl, alt);
            return true;
        }
        return false;
    }

    /// <summary>Parses an InteractKey name ("F", "Shift+F") into a stroke. Same grammar as TargetKey.</summary>
    public static bool TryParseInteractKey(string text, out KeyStroke stroke) =>
        TryParseTargetKey(text, out stroke);

    /// <summary>True when a TargetKey/InteractKey name resolves to a movement key ("W", "Shift+Up", ...).</summary>
    public static bool IsMovementKeyName(string text) =>
        TryParseTargetKey(text, out var stroke) && IsMovementStroke(stroke);

    /// <summary>True while the physical key is currently held down.</summary>
    public static bool IsPhysicallyDown(byte virtualKey)
    {
        try
        {
            return (Native.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Pure combat-aware auto-target gate, kept static for testability.
    /// With combatOnly on, NeedTarget only auto-targets shortly after the
    /// last Active frame (in-combat target dropout); a long stretch without
    /// Active reads as out of combat and holds. Pass long.MinValue for
    /// lastActiveMs when no Active frame has ever been seen (fresh start out
    /// of combat -&gt; hold). Idle never fires: the T button is OFF or a target
    /// is present, so there is nothing to ask for.
    /// </summary>
    public static bool ShouldAutoTarget(
        BridgeState state,
        bool autoTargetEnabled,
        bool combatOnly,
        long nowMs,
        long lastActiveMs,
        long combatIdleWindowMs = 5000)
    {
        if (!autoTargetEnabled || state != BridgeState.NeedTarget) return false;
        if (!combatOnly) return true;
        if (lastActiveMs == long.MinValue) return false;
        return nowMs - lastActiveMs <= combatIdleWindowMs;
    }

    /// <summary>
    /// Pure combat-aware auto-interact gate, mirroring ShouldAutoTarget.
    /// With combatOnly on, NeedInteract only auto-interacts shortly after the
    /// last Active frame; a long stretch without Active reads as out of
    /// combat and holds. Pass long.MinValue for lastActiveMs when no Active
    /// frame has ever been seen (fresh start out of combat -&gt; hold).
    /// </summary>
    public static bool ShouldAutoInteract(
        BridgeState state,
        bool interactEnabled,
        bool combatOnly,
        long nowMs,
        long lastActiveMs,
        long combatIdleWindowMs = 5000)
    {
        if (!interactEnabled || state != BridgeState.NeedInteract) return false;
        if (!combatOnly) return true;
        if (lastActiveMs == long.MinValue) return false;
        return nowMs - lastActiveMs <= combatIdleWindowMs;
    }
}
