namespace MaxDpsCompanion;

/// <summary>
/// [Window] ModifierInput (4.1 A2.2): how a stroke that CARRIES Ctrl/Alt/Shift
/// reaches the game.
///
///  * <see cref="Post"/> — today's behaviour: WM_KEYDOWN/WM_KEYUP posted to
///    the game window only. Default, so every existing install keeps the
///    exact delivery it has now (zero regression).
///  * <see cref="Auto"/> — a modifier chord goes through SendInput while the
///    game is the foreground window, and falls back to Post (with a visible
///    status note) in the background.
///  * <see cref="SendInput"/> — a modifier chord is forced through SendInput
///    while the game is foreground and held with a note when it is not; it
///    never falls back to Post and never sends global input to another
///    window.
///
/// A stroke WITHOUT modifiers always keeps the per-window Post route in every
/// mode: [Window] ModifierInput governs modifier delivery only, so the
/// companion never starts synthesising global input it does not need.
/// </summary>
internal enum ModifierInputMode
{
    Post = 0,
    Auto = 1,
    SendInput = 2,
}

internal static class ModifierInputSetting
{
    /// <summary>Parses the setting; anything unrecognised keeps today's Post.</summary>
    public static ModifierInputMode Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "auto" => ModifierInputMode.Auto,
        "sendinput" or "send-input" or "input" => ModifierInputMode.SendInput,
        _ => ModifierInputMode.Post,
    };

    /// <summary>Canonical spelling written back to settings.ini.</summary>
    public static string Format(ModifierInputMode mode) => mode switch
    {
        ModifierInputMode.Auto => "Auto",
        ModifierInputMode.SendInput => "SendInput",
        _ => "Post",
    };
}
