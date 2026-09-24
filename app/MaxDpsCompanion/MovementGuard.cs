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

    // v1.3.3 full-coverage key table. ONE canonical map for every name the
    // user can type (TargetKey/InteractKey/Pause hotkey) — mirrors
    // Keymap.lua's VK table so C# and Lua agree byte-for-byte. WinForms
    // Keys CANNOT be the authority: it lacks mouse buttons entirely and
    // misspells OEM/layout tokens — the Sep-2026 MB5 outage (InteractKey
    // "MB5"/"Alt+MB5" failed to parse → silent dead fallback). Alias rows
    // marked *: WoW spelling → same VK. Wheel names parse (engine treats
    // them as unheld/unblocked) so they never silently die either.
    private static readonly Dictionary<string, byte> KeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Letters + digits (layout-independent VKs)
        ["a"] = 0x41, ["b"] = 0x42, ["c"] = 0x43, ["d"] = 0x44, ["e"] = 0x45,
        ["f"] = 0x46, ["g"] = 0x47, ["h"] = 0x48, ["i"] = 0x49, ["j"] = 0x4A,
        ["k"] = 0x4B, ["l"] = 0x4C, ["m"] = 0x4D, ["n"] = 0x4E, ["o"] = 0x4F,
        ["p"] = 0x50, ["q"] = 0x51, ["r"] = 0x52, ["s"] = 0x53, ["t"] = 0x54,
        ["u"] = 0x55, ["v"] = 0x56, ["w"] = 0x57, ["x"] = 0x58, ["y"] = 0x59,
        ["z"] = 0x5A,
        ["0"] = 0x30, ["1"] = 0x31, ["2"] = 0x32, ["3"] = 0x33, ["4"] = 0x34,
        ["5"] = 0x35, ["6"] = 0x36, ["7"] = 0x37, ["8"] = 0x38, ["9"] = 0x39,
        // Function keys
        ["f1"] = 0x70, ["f2"] = 0x71, ["f3"] = 0x72, ["f4"] = 0x73,
        ["f5"] = 0x74, ["f6"] = 0x75, ["f7"] = 0x76, ["f8"] = 0x77,
        ["f9"] = 0x78, ["f10"] = 0x79, ["f11"] = 0x7A, ["f12"] = 0x7B,
        ["f13"] = 0x7C, ["f14"] = 0x7D, ["f15"] = 0x7E, ["f16"] = 0x7F,
        ["f17"] = 0x80, ["f18"] = 0x81, ["f19"] = 0x82, ["f20"] = 0x83,
        ["f21"] = 0x84, ["f22"] = 0x85, ["f23"] = 0x86, ["f24"] = 0x87,
        // Editing / whitespace (+ WoW aliases *)
        ["backspace"] = 0x08, ["tab"] = 0x09, ["enter"] = 0x0D, ["return*"] = 0x0D,
        ["escape"] = 0x1B, ["esc*"] = 0x1B, ["space"] = 0x20, ["spacebar*"] = 0x20,
        ["capslock"] = 0x14, ["caps*"] = 0x14,
        // Navigation (+ aliases *)
        ["pageup"] = 0x21, ["pgup*"] = 0x21, ["pagedown"] = 0x22, ["pgdn*"] = 0x22,
        ["pagedn*"] = 0x22, ["end"] = 0x23, ["home"] = 0x24,
        ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28,
        ["insert"] = 0x2D, ["ins*"] = 0x2D, ["delete"] = 0x2E, ["del*"] = 0x2E,
        // Lock / system keys (bindable in WoW's keybind UI)
        ["numlock"] = 0x90, ["scrolllock"] = 0x91, ["scroll*"] = 0x91,
        ["printscreen"] = 0x2C, ["prtsc*"] = 0x2C, ["prtscr*"] = 0x2C,
        ["pause"] = 0x13, ["break*"] = 0x13, ["clear"] = 0x0C,
        // OEM punctuation US (+ WoW alias spellings *)
        ["-"] = 0xBD, ["minus*"] = 0xBD, ["="] = 0xBB, ["equals*"] = 0xBB,
        ["["] = 0xDB, ["lbrace*"] = 0xDB, ["lbr*"] = 0xDB, ["lbracket*"] = 0xDB,
        ["]"] = 0xDD, ["rbrace*"] = 0xDD, ["rbr*"] = 0xDD, ["rbracket*"] = 0xDD,
        ["\\"] = 0xDC, ["backslash*"] = 0xDC,
        [";"] = 0xBA, ["semicolon*"] = 0xBA,
        ["'"] = 0xDE, ["quote*"] = 0xDE, ["apostrophe*"] = 0xDE,
        [","] = 0xBC, ["comma*"] = 0xBC, ["."] = 0xBE, ["period*"] = 0xBE,
        ["/"] = 0xBF, ["slash*"] = 0xBF,
        ["`"] = 0xC0, ["tilde*"] = 0xC0, ["grave*"] = 0xC0,
        // Numpad (digits + operators + NumLock spellings)
        ["numpad0"] = 0x60, ["numpad1"] = 0x61, ["numpad2"] = 0x62,
        ["numpad3"] = 0x63, ["numpad4"] = 0x64, ["numpad5"] = 0x65,
        ["numpad6"] = 0x66, ["numpad7"] = 0x67, ["numpad8"] = 0x68,
        ["numpad9"] = 0x69, ["num0*"] = 0x60, ["num1*"] = 0x61,
        ["num2*"] = 0x62, ["num3*"] = 0x63, ["num4*"] = 0x64,
        ["num5*"] = 0x65, ["num6*"] = 0x66, ["num7*"] = 0x67,
        ["num8*"] = 0x68, ["num9*"] = 0x69,
        ["n0*"] = 0x60, ["n1*"] = 0x61, ["n2*"] = 0x62, ["n3*"] = 0x63,
        ["n4*"] = 0x64, ["n5*"] = 0x65, ["n6*"] = 0x66, ["n7*"] = 0x67,
        ["n8*"] = 0x68, ["n9*"] = 0x69,
        ["numpaddivide"] = 0x6F, ["numpadmultiply"] = 0x6A,
        ["numpadminus"] = 0x6D, ["numpadplus"] = 0x6B, ["numpaddecimal"] = 0x6E,
        // Mouse buttons 1-5 + wheel (same VKs as Keymap.lua; 6+ have no
        // Windows VK and CANNOT parse — documented, never silent: caller
        // surfaces LastParseError).
        ["mouse1"] = 0x01, ["leftbutton"] = 0x01, ["lmb"] = 0x01, ["button1"] = 0x01,
        ["mb1"] = 0x01,
        ["mouse2"] = 0x02, ["rightbutton"] = 0x02, ["rmb"] = 0x02, ["button2"] = 0x02,
        ["mb2"] = 0x02,
        ["mouse3"] = 0x04, ["middlebutton"] = 0x04, ["mmb"] = 0x04, ["m3"] = 0x04,
        ["button3"] = 0x04, ["mb3"] = 0x04,
        ["mouse4"] = 0x05, ["xbutton1"] = 0x05, ["mb4"] = 0x05, ["button4"] = 0x05,
        ["mouse5"] = 0x06, ["xbutton2"] = 0x06, ["mb5"] = 0x06, ["button5"] = 0x06,
        ["wheelup"] = 0x07, ["mwheelup"] = 0x07, ["mwu"] = 0x07,
        ["mousewheelup"] = 0x07,
        ["wheeldown"] = 0x0B, ["mwheeldown"] = 0x0B, ["mwd"] = 0x0B,
        ["mousewheeldown"] = 0x0B,
    };

    /// <summary>Last parse failure (miss-transparency: never swallow).</summary>
    public static string? LastParseError { get; private set; }

    /// <summary>
    /// Parses a key name ("Tab", "Shift+F8", "MB5", "Alt+MB5", "Ctrl+Shift+MB4",
    /// "NumPad7", "OEMMinus"...) into a stroke. Grammar: optional Shift/Ctrl/
    /// Alt/Win prefixes in ANY order (plus-separated, case-insensitive, extra
    /// whitespace tolerated), then exactly one key token from <see cref="KeyNames"/>.
    /// Modifiers combine with mouse AND keyboard alike (Alt+MB5 valid).
    /// Returns false + sets <see cref="LastParseError"/> on any failure.
    /// </summary>
    public static bool TryParseKey(string text, out KeyStroke stroke)
    {
        stroke = default;
        LastParseError = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            LastParseError = "empty key name";
            return false;
        }

        var shift = false;
        var ctrl = false;
        var alt = false;
        var win = false;
        string? keyToken = null;
        foreach (var part in text.Trim().Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "shift": shift = true; continue;
                case "ctrl":
                case "control": ctrl = true; continue;
                case "alt": alt = true; continue;
                case "win":
                case "windows":
                case "meta":
                case "cmd": win = true; continue;
            }
            if (keyToken is not null)
            {
                LastParseError = $"two key tokens: '{keyToken}' + '{part}' (one key per bind)";
                return false;
            }
            keyToken = part;
        }
        if (keyToken is null)
        {
            LastParseError = $"modifiers only, no key: '{text}'";
            return false;
        }
        if (win)
        {
            // Win is a valid RegisterHotKey modifier; for game strokes it has
            // no PostMessage/SendInput encoding — fail loudly, not silently.
            LastParseError = $"Win modifier not sendable to game: '{text}' (use Shift/Ctrl/Alt)";
            return false;
        }
        var lookup = keyToken.Trim().TrimEnd('*');
        if (!KeyNames.TryGetValue(lookup, out var vk))
        {
            LastParseError = $"unknown key '{keyToken}' in '{text}' (see README key table)";
            return false;
        }
        stroke = new KeyStroke(vk, shift, ctrl, alt);
        return true;
    }

    /// <summary>Parses a TargetKey-style name. Same grammar as <see cref="TryParseKey"/>.</summary>
    public static bool TryParseTargetKey(string text, out KeyStroke stroke) =>
        TryParseKey(text, out stroke);

    /// <summary>Parses an InteractKey name ("F", "MB5", "Alt+MB5"). Same grammar as <see cref="TryParseKey"/>.</summary>
    public static bool TryParseInteractKey(string text, out KeyStroke stroke) =>
        TryParseKey(text, out stroke);

    /// <summary>True when a TargetKey/InteractKey name resolves to a movement key ("W", "Shift+Up", ...).</summary>
    public static bool IsMovementKeyName(string text) =>
        TryParseKey(text, out var stroke) && IsMovementStroke(stroke);

    /// <summary>
    /// Parses a RegisterHotKey-style hotkey ("Pause", "Ctrl+Shift+F9") into
    /// Win32 modifiers + VK. Shares <see cref="KeyNames"/> so the pause box
    /// accepts everything Target/Interact accept (plus Win, which is
    /// RegisterHotKey-only). Returns false + LastParseError on failure.
    /// </summary>
    public static bool TryParseHotkey(string text, out uint modifiers, out byte vk)
    {
        const uint modAlt = 0x0001, modControl = 0x0002, modShift = 0x0004, modWin = 0x0008;
        modifiers = 0;
        vk = 0;
        LastParseError = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            LastParseError = "empty hotkey";
            return false;
        }
        var shift = false;
        var ctrl = false;
        var alt = false;
        var win = false;
        string? keyToken = null;
        foreach (var part in text.Trim().Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "shift": shift = true; continue;
                case "ctrl":
                case "control": ctrl = true; continue;
                case "alt": alt = true; continue;
                case "win":
                case "windows":
                case "meta":
                case "cmd": win = true; continue;
            }
            if (keyToken is not null)
            {
                LastParseError = $"two key tokens: '{keyToken}' + '{part}'";
                return false;
            }
            keyToken = part;
        }
        if (keyToken is null)
        {
            LastParseError = $"modifiers only, no key: '{text}'";
            return false;
        }
        var lookup = keyToken.Trim().TrimEnd('*');
        if (!KeyNames.TryGetValue(lookup, out var code))
        {
            LastParseError = $"unknown key '{keyToken}' in '{text}'";
            return false;
        }
        if (shift) modifiers |= modShift;
        if (ctrl) modifiers |= modControl;
        if (alt) modifiers |= modAlt;
        if (win) modifiers |= modWin;
        vk = code;
        return true;
    }

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
