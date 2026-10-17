namespace MaxDpsCompanion;

/// <summary>Per-slot OS gate outcome, shared by the scheduled and legacy send loops.</summary>
internal enum SlotVerdict
{
    /// <summary>All gates passed — deliver the stroke.</summary>
    Send,

    /// <summary>Bound to a true movement key (WASD/Space/arrows): never sent.</summary>
    SkipMovementBound,

    /// <summary>The key is physically held; a synthetic KEYUP would read as a release.</summary>
    SkipPhysicalHold,

    /// <summary>Keyboard slot blocked by the foreground gate: skip it, try lower ranks.</summary>
    SkipFocusKeyboard,

    /// <summary>Mouse slot needs the game focused (no per-window mouse route).</summary>
    SkipFocusMouse,
}

/// <summary>
/// 4.1 A2.6: the per-slot OS gates both send loops run before a stroke, kept in
/// one pure place so the "focus-blocked keyboard slot must not starve lower
/// ranks" rule is unit-tested. Before 4.1 the keyboard branch returned from the
/// whole loop while the mouse branch skipped one slot.
/// </summary>
internal static class SlotGate
{
    /// <param name="physicallyDown">Test seam; the live call uses GetAsyncKeyState.</param>
    public static SlotVerdict Evaluate(
        KeyStroke stroke,
        bool foreground,
        bool isGameWindow,
        bool allowBackgroundKeys,
        Func<byte, bool>? physicallyDown = null)
    {
        if (MovementGuard.IsMovementStroke(stroke)) return SlotVerdict.SkipMovementBound;

        var down = physicallyDown ?? MovementGuard.IsPhysicallyDown;
        if (down(stroke.VirtualKey)) return SlotVerdict.SkipPhysicalHold;

        if (KeySender.IsMouse(stroke.VirtualKey))
            return foreground && isGameWindow ? SlotVerdict.Send : SlotVerdict.SkipFocusMouse;

        return allowBackgroundKeys || foreground ? SlotVerdict.Send : SlotVerdict.SkipFocusKeyboard;
    }
}
