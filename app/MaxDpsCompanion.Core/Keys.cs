namespace MaxDpsCompanion;

// Portable single-sourced key table for MaxDps-Companion (Core P0a).
// Merges MovementGuard.KeyNames + MovementGuard.TryParseKey/TryParseHotkey +
// KeySender mouse/extended/Describe logic + KeyNames.Describe into ONE
// canonical map, so C# and Keymap.lua agree byte-for-byte.
//
// NO WinForms, NO P/Invoke, NO System.Drawing: must compile standalone in
// MaxDpsCompanion.Core (net8.0, any OS). No PostMessage/send calls here.
//
// P0b NOTE: KeyStroke lives in PixelProtocol.cs (protected there) and is NOT
// referenced here so this file compiles standalone in Core P0a. Parsing and
// Describe take/return the minimal local KeyRef (byte Vk + Shift/Ctrl/Alt);
// P0b wires the real KeyStroke type (same shape) to these methods.
//
// INVARIANTS (preserved from MovementGuard/KeySender/KeyNames — do not weaken):
// - MB5/Interact-on-mouse parses ("MB5", "Alt+MB5"): the Sep-2026 outage was
//   InteractKey "MB5"/"Alt+MB5" failing to parse -> silent dead fallback.
// - Win split: game strokes REJECT Win loudly (no PostMessage/SendInput
//   encoding exists); pause-box hotkeys ALLOW Win (RegisterHotKey-only,
//   MOD_WIN 0x0008).
// - Exact movement set: WASD/Space/arrows only (0x57/0x41/0x53/0x44, 0x20,
//   0x25-0x28). Nothing else may enter MovementKeys.
// - LastParseError is NEVER silent: every false return sets it.
internal static partial class Keys
{
    // Minimal standalone stroke. Same shape as PixelProtocol.KeyStroke;
    // P0b replaces these KeyRef touchpoints with the real KeyStroke type.
    // Layout-identical on purpose: implicit conversion both ways below, so
    // call sites read naturally during the Core port.
    internal readonly record struct KeyRef(byte Vk, bool Shift, bool Ctrl, bool Alt)
    {
        public static implicit operator KeyRef(KeyStroke s) => new(s.VirtualKey, s.Shift, s.Ctrl, s.Alt);

        /// <summary>Back to the wire stroke type (same layout).</summary>
        public KeyStroke ToKeyStroke() => new(VirtualKey, Shift, Ctrl, Alt);

        /// <summary>Alias matching KeyStroke.VirtualKey so engine code reads unchanged.</summary>
        public byte VirtualKey => Vk;

        /// <summary>Human-readable name, mirroring KeyStroke.Describe().</summary>
        public string Describe() => Keys.Describe(this);
    }

    // Routing for the send path: keyboard via PostMessage/SendInput, mouse
    // buttons via mouse input, wheel as unheld/unblocked clicks.
    internal enum KeyRoute
    {
        Keyboard,
        MouseButton,
        MouseWheel,
    }

    // RegisterHotKey modifier bits (Win32 MOD_*). Shared by TryParseHotkey.
    internal const uint ModAlt = 0x0001;
    internal const uint ModControl = 0x0002;
    internal const uint ModShift = 0x0004;
    internal const uint ModWin = 0x0008;

    // Mouse codes as encoded by the addon (from KeySender). Buttons reuse
    // their real virtual-key codes, which a keyboard can never produce; the
    // wheel borrows two values Windows leaves undefined.
    private const byte VkLeftButton = 0x01;
    private const byte VkRightButton = 0x02;
    private const byte VkMiddleButton = 0x04;
    private const byte VkXButton1 = 0x05;
    private const byte VkXButton2 = 0x06;
    private const byte VkWheelUp = 0x07;
    private const byte VkWheelDown = 0x0B;

    // EXACT movement set: WASD + Space (jump) + arrows. Kept byte-identical
    // to MovementGuard.MovementKeys.
    private static readonly HashSet<byte> MovementKeys =
    [
        0x57, 0x41, 0x53, 0x44, // W A S D
        0x20,                   // Space (jump)
        0x25, 0x26, 0x27, 0x28, // Left Up Right Down
    ];

    // Keys needing KEYEVENTF_EXTENDEDKEY on SendInput (from KeySender).
    private static readonly HashSet<byte> ExtendedKeySet =
    [
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, // page/home/end/arrows
        0x2D, 0x2E,                                     // insert, delete
        0x6F,                                           // numpad divide
        0x90,                                           // num lock
    ];

    // v1.3.3 full-coverage key table. ONE canonical map for every name the
    // user can type (TargetKey/InteractKey/Pause hotkey) — mirrors
    // Keymap.lua's VK table so C# and Lua agree byte-for-byte. WinForms
    // Keys CANNOT be the authority: it lacks mouse buttons entirely and
    // misspells OEM/layout tokens — the Sep-2026 MB5 outage (InteractKey
    // "MB5"/"Alt+MB5" failed to parse → silent dead fallback). Alias rows
    // marked *: WoW spelling → same VK. Wheel names parse (engine treats
    // them as unheld/unblocked) so they never silently die either.
    //
    // MB3 NOTE: C# keeps the MB3/LMB/RMB/MMB/M3/LBRACE aliases as-is (same
    // VKs as the mouse section below). Keymap.lua currently spells only
    // MB1/MB2/MB4/MB5 (+MOUSE1-5/BUTTON1-5) and has NO MB3/M3 row — adding a
    // Lua MB3 alias is a SEPARATE addon change, NOT made here.
    //
    // NUMLOCK* NOTE: the 15 NUMLOCK-prefixed spellings below (divide,
    // multiply, minus, plus, decimal, 0-9) come from Keymap.lua's Sep-2026
    // audit rows, which the C# table was missing; added here so both sides
    // resolve them to 0x6F/0x6A/0x6D/0x6B/0x6E/0x60-0x69.
    private static readonly Dictionary<string, byte> Names = new(StringComparer.OrdinalIgnoreCase)
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
        ["backspace"] = 0x08, ["tab"] = 0x09, ["enter"] = 0x0D, ["return"] = 0x0D,
        ["escape"] = 0x1B, ["esc"] = 0x1B, ["space"] = 0x20, ["spacebar"] = 0x20,
        ["capslock"] = 0x14, ["caps"] = 0x14,
        // Navigation
        ["pageup"] = 0x21, ["pgup"] = 0x21, ["pagedown"] = 0x22, ["pgdn"] = 0x22,
        ["pagedn"] = 0x22, ["end"] = 0x23, ["home"] = 0x24,
        ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28,
        ["insert"] = 0x2D, ["ins"] = 0x2D, ["delete"] = 0x2E, ["del"] = 0x2E,
        // Lock / system keys (bindable in WoW's keybind UI)
        ["numlock"] = 0x90, ["scrolllock"] = 0x91, ["scroll"] = 0x91,
        ["printscreen"] = 0x2C, ["prtsc"] = 0x2C, ["prtscr"] = 0x2C,
        ["pause"] = 0x13, ["break"] = 0x13, ["clear"] = 0x0C,
        // OEM punctuation US (+ WoW alias spellings)
        ["-"] = 0xBD, ["minus"] = 0xBD, ["="] = 0xBB, ["equals"] = 0xBB,
        ["["] = 0xDB, ["lbrace"] = 0xDB, ["lbr"] = 0xDB, ["lbracket"] = 0xDB,
        ["]"] = 0xDD, ["rbrace"] = 0xDD, ["rbr"] = 0xDD, ["rbracket"] = 0xDD,
        ["\\"] = 0xDC, ["backslash"] = 0xDC,
        [";"] = 0xBA, ["semicolon"] = 0xBA,
        ["'"] = 0xDE, ["quote"] = 0xDE, ["apostrophe"] = 0xDE,
        [","] = 0xBC, ["comma"] = 0xBC, ["."] = 0xBE, ["period"] = 0xBE,
        ["/"] = 0xBF, ["slash"] = 0xBF,
        ["`"] = 0xC0, ["tilde"] = 0xC0, ["grave"] = 0xC0,
        // Numpad (digits + operators + NumLock spellings)
        ["numpad0"] = 0x60, ["numpad1"] = 0x61, ["numpad2"] = 0x62,
        ["numpad3"] = 0x63, ["numpad4"] = 0x64, ["numpad5"] = 0x65,
        ["numpad6"] = 0x66, ["numpad7"] = 0x67, ["numpad8"] = 0x68,
        ["numpad9"] = 0x69, ["num0"] = 0x60, ["num1"] = 0x61,
        ["num2"] = 0x62, ["num3"] = 0x63, ["num4"] = 0x64,
        ["num5"] = 0x65, ["num6"] = 0x66, ["num7"] = 0x67,
        ["num8"] = 0x68, ["num9"] = 0x69,
        ["n0"] = 0x60, ["n1"] = 0x61, ["n2"] = 0x62, ["n3"] = 0x63,
        ["n4"] = 0x64, ["n5"] = 0x65, ["n6"] = 0x66, ["n7"] = 0x67,
        ["n8"] = 0x68, ["n9"] = 0x69,
        ["numpaddivide"] = 0x6F, ["numpadmultiply"] = 0x6A,
        ["numpadminus"] = 0x6D, ["numpadplus"] = 0x6B, ["numpaddecimal"] = 0x6E,
        // NEW (from Keymap.lua): NumLock-dependent spellings WoW can emit.
        ["numlockdivide"] = 0x6F, ["numlockmultiply"] = 0x6A,
        ["numlockminus"] = 0x6D, ["numlockplus"] = 0x6B,
        ["numlockdecimal"] = 0x6E,
        ["numlock0"] = 0x60, ["numlock1"] = 0x61, ["numlock2"] = 0x62,
        ["numlock3"] = 0x63, ["numlock4"] = 0x64, ["numlock5"] = 0x65,
        ["numlock6"] = 0x66, ["numlock7"] = 0x67, ["numlock8"] = 0x68,
        ["numlock9"] = 0x69,
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
    /// Parses a game-bound stroke ("F", "MB5", "Alt+MB5", "Ctrl+Shift+F9").
    /// Grammar mirrors MovementGuard.TryParseKey byte-for-byte. Win is
    /// REJECTED LOUDLY: it is a valid RegisterHotKey modifier but has no
    /// PostMessage/SendInput encoding — fail loudly, not silently.
    /// </summary>
    public static bool TryParseGameStroke(string text, out KeyRef stroke)
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
        var lookup = keyToken.Trim();
        if (!Names.TryGetValue(lookup, out var vk))
        {
            LastParseError = $"unknown key '{keyToken}' in '{text}' (see README key table)";
            return false;
        }
        stroke = new KeyRef(vk, shift, ctrl, alt);
        return true;
    }

    /// <summary>Parses a key name. Same grammar as <see cref="TryParseGameStroke"/>.</summary>
    public static bool TryParseKey(string text, out KeyRef stroke) =>
        TryParseGameStroke(text, out stroke);

    /// <summary>Parses a TargetKey-style name. Same grammar as <see cref="TryParseGameStroke"/>.</summary>
    public static bool TryParseTargetKey(string text, out KeyRef stroke) =>
        TryParseGameStroke(text, out stroke);

    /// <summary>Parses an InteractKey name ("F", "MB5", "Alt+MB5"). Same grammar as <see cref="TryParseGameStroke"/>.</summary>
    public static bool TryParseInteractKey(string text, out KeyRef stroke) =>
        TryParseGameStroke(text, out stroke);

    /// <summary>
    /// Parses a RegisterHotKey-style hotkey ("Pause", "Ctrl+Shift+F9") into
    /// Win32 modifiers + VK. Shares <see cref="Names"/> so the pause box
    /// accepts everything Target/Interact accept (plus Win, which is
    /// RegisterHotKey-only). Returns false + LastParseError on failure.
    /// </summary>
    public static bool TryParseHotkey(string text, out uint modifiers, out byte vk)
    {
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
        var lookup = keyToken.Trim();
        if (!Names.TryGetValue(lookup, out var code))
        {
            LastParseError = $"unknown key '{keyToken}' in '{text}'";
            return false;
        }
        if (shift) modifiers |= ModShift;
        if (ctrl) modifiers |= ModControl;
        if (alt) modifiers |= ModAlt;
        if (win) modifiers |= ModWin;
        vk = code;
        return true;
    }

    /// <summary>
    /// Human-readable name incl. modifiers and mouse buttons (MB5 etc. — the
    /// Interact-on-mouse case). Merges KeySender.DescribeStroke (Ctrl+Alt+
    /// Shift+ prefix order + mouse names) with KeyNames.Describe (VK names).
    /// </summary>
    public static string Describe(KeyRef stroke)
    {
        var prefix = (stroke.Ctrl ? "Ctrl+" : "") + (stroke.Alt ? "Alt+" : "") + (stroke.Shift ? "Shift+" : "");
        return prefix + DescribeVk(stroke.Vk);
    }

    /// <summary>
    /// Human-readable name for a virtual-key code, for the status display
    /// only. Merges KeyNames.Describe with KeySender.MouseName (identical
    /// mouse strings), plus FIXES vs KeyNames.Describe: CLEAR, PAUSE,
    /// CAPSLOCK, PRINTSCREEN, NUMLOCK, SCROLLLOCK are named instead of
    /// falling through to VKxx.
    /// </summary>
    public static string DescribeVk(byte vk) => vk switch
    {
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x60 and <= 0x69 => "Num" + (vk - 0x60),
        >= 0x70 and <= 0x87 => "F" + (vk - 0x70 + 1),
        // Mouse: real VK codes for the buttons, two undefined values for the wheel.
        0x01 => "Mouse1",
        0x02 => "Mouse2",
        0x04 => "Mouse3",
        0x05 => "Mouse4",
        0x06 => "Mouse5",
        0x07 => "WheelUp",
        0x0B => "WheelDown",
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0C => "Clear",
        0x0D => "Enter",
        0x13 => "Pause",
        0x14 => "CapsLock",
        0x1B => "Escape",
        0x20 => "Space",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x2C => "PrintScreen",
        0x2D => "Insert",
        0x2E => "Delete",
        0x6A => "Num*",
        0x6B => "Num+",
        0x6D => "Num-",
        0x6E => "Num.",
        0x6F => "Num/",
        0x90 => "NumLock",
        0x91 => "ScrollLock",
        0xBA => ";",
        0xBB => "=",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        _ => $"VK{vk:X2}",
    };

    /// <summary>True for mouse codes: buttons 1-5 + the two wheel codes.</summary>
    public static bool IsMouse(byte vk) => vk switch
    {
        VkLeftButton or VkRightButton or VkMiddleButton or VkXButton1 or VkXButton2
            or VkWheelUp or VkWheelDown => true,
        _ => false,
    };

    /// <summary>True for the exact movement set (WASD/Space/arrows only).</summary>
    public static bool IsMovementKey(byte vk) => MovementKeys.Contains(vk);

    /// <summary>True when a TargetKey/InteractKey stroke is a movement key ("W", "Shift+Up", ...).</summary>
    public static bool IsMovementStroke(KeyRef stroke) => IsMovementKey(stroke.Vk);

    /// <summary>True for keys needing KEYEVENTF_EXTENDEDKEY on SendInput.</summary>
    public static bool IsExtendedKey(byte vk) => ExtendedKeySet.Contains(vk);

    /// <summary>Routes a VK to the send path: wheel, mouse button, or keyboard.</summary>
    public static KeyRoute Route(byte vk) => vk switch
    {
        VkWheelUp or VkWheelDown => KeyRoute.MouseWheel,
        VkLeftButton or VkRightButton or VkMiddleButton or VkXButton1 or VkXButton2 => KeyRoute.MouseButton,
        _ => KeyRoute.Keyboard,
    };

    /// <summary>
    /// Maps a Windows VK to the platform send code. Win-identity today
    /// (return vk); the Mac override maps here later — callers must go
    /// through this instead of using the VK raw.
    /// </summary>
    public static byte MapToPlatform(byte vk) => vk;
}

internal static partial class Keys
{
    /// <summary>
    /// Pure combat-aware auto-target gate, kept static for testability.
    /// Verbatim from MovementGuard (Windows file stays as-is): with combatOnly
    /// on, NeedTarget only auto-targets shortly after the last Active frame;
    /// a long stretch without Active reads as out of combat and holds.
    /// </summary>
    public static bool ShouldAutoTarget(
        BridgeState state,
        bool autoTargetEnabled,
        bool combatOnly,
        bool mirrorOutOfCombatSet,
        bool mirrorAutoTargetSet,
        long nowMs,
        long lastActiveMs,
        long combatIdleWindowMs = 5000)
    {
        if (!autoTargetEnabled || state != BridgeState.NeedTarget) return false;
        if (!mirrorAutoTargetSet) return false;
        if (!combatOnly) return mirrorOutOfCombatSet;
        if (lastActiveMs == long.MinValue) return false;
        return nowMs - lastActiveMs <= combatIdleWindowMs;
    }

    /// <summary>
    /// Pure combat-aware auto-interact gate, mirroring ShouldAutoTarget.
    /// Verbatim from MovementGuard (Windows file stays as-is).
    /// </summary>
    public static bool ShouldAutoInteract(
        BridgeState state,
        bool interactEnabled,
        bool combatOnly,
        bool mirrorOutOfCombatSet,
        bool mirrorAutoInteractSet,
        long nowMs,
        long lastActiveMs,
        long combatIdleWindowMs = 5000)
    {
        if (!interactEnabled || state != BridgeState.NeedInteract) return false;
        if (!mirrorAutoInteractSet) return false;
        if (!combatOnly) return mirrorOutOfCombatSet;
        if (lastActiveMs == long.MinValue) return false;
        return nowMs - lastActiveMs <= combatIdleWindowMs;
    }

    /// <summary>
    /// Human-readable name incl. mouse buttons. Mirrors KeySender.DescribeStroke
    /// (Windows file stays as-is): Ctrl+/Alt+/Shift+ prefix + mouse names.
    /// </summary>
    public static string DescribeStroke(KeyRef stroke) => Describe(stroke);
}
