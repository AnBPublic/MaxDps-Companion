namespace MaxDpsCompanion;

/// <summary>How one stroke should be delivered.</summary>
internal enum ChordRoute
{
    /// <summary>WM_KEYDOWN/WM_KEYUP posted to the game window only (no focus needed).</summary>
    Post,

    /// <summary>Global SendInput while the game owns the foreground window.</summary>
    SendInput,

    /// <summary>Send nothing this tick; the status line explains why.</summary>
    Defer,
}

/// <summary>
/// The pure delivery decision for one stroke. Modifier presses are decided
/// here so the sender only ever presses/releases modifiers the user is NOT
/// already holding (<see cref="PressShift"/> etc. are false for those).
/// </summary>
internal readonly record struct ChordPlan(
    ChordRoute Route,
    bool PressShift,
    bool PressCtrl,
    bool PressAlt,
    string? Note)
{
    public static ChordPlan Post() => new(ChordRoute.Post, false, false, false, null);

    public static ChordPlan Defer(string note) => new(ChordRoute.Defer, false, false, false, note);
}

/// <summary>
/// 4.1 A2.2 (a)-(f): the modifier-chord routing decision, kept pure and
/// injectable so it is unit-tested without a game window.
///
/// The premise that WoW ignores posted modifiers is UNPROVEN in this repo, so
/// the proven Post path stays the default and the decision is only taken when
/// the user selects [Window] ModifierInput=Auto/SendInput.
/// </summary>
internal static class ChordPlanner
{
    /// <summary>True when the stroke needs at least one modifier key.</summary>
    public static bool IsChord(in KeyStroke stroke) => stroke.Shift || stroke.Ctrl || stroke.Alt;

    /// <summary>
    /// Decides the route for <paramref name="stroke"/>.
    /// <paramref name="shiftDown"/>/<paramref name="ctrlDown"/>/
    /// <paramref name="altDown"/> are the PHYSICAL modifier states at decision
    /// time (GetAsyncKeyState, high bit).
    /// </summary>
    public static ChordPlan Decide(
        ModifierInputMode mode,
        in KeyStroke stroke,
        bool gameForeground,
        bool shiftDown,
        bool ctrlDown,
        bool altDown)
    {
        // Not a chord: the per-window Post route is safe, focused or not, and
        // [Window] ModifierInput explicitly governs modifier input only.
        if (!IsChord(stroke)) return ChordPlan.Post();

        // Default: today's exact behaviour, and no physical-state queries.
        if (mode == ModifierInputMode.Post) return ChordPlan.Post();

        // (b) A physically held modifier the stroke does NOT contain cannot be
        // cleared by pressing/releasing our own modifiers: SendInput would
        // deliver a DIFFERENT chord (Shift held + Ctrl+X => Shift+Ctrl+X).
        // Defer with a visible note instead of guessing.
        if (MovementGuard.HeldModifierNotIn(stroke, shiftDown, ctrlDown, altDown) is { } extra)
            return ChordPlan.Defer(
                $"holding {extra} - {stroke.Describe()} deferred (it would change the chord)");

        // (c) SendInput is global: it lands in whatever window owns the
        // foreground. Never send blind, and never guess a fallback the user
        // did not ask for in the explicit SendInput mode.
        if (!gameForeground)
        {
            return mode == ModifierInputMode.Auto
                ? new ChordPlan(ChordRoute.Post, false, false, false,
                    $"background chord {stroke.Describe()}: posted (modifiers may not register)")
                : ChordPlan.Defer(
                    $"chord {stroke.Describe()} needs the game focused (ModifierInput=SendInput)");
        }

        // (a) Press/release ONLY the modifiers that are not already down; the
        // user's own held modifier is neither pressed nor released by us.
        return new ChordPlan(
            ChordRoute.SendInput,
            PressShift: stroke.Shift && !shiftDown,
            PressCtrl: stroke.Ctrl && !ctrlDown,
            PressAlt: stroke.Alt && !altDown,
            Note: null);
    }
}
