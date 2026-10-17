// Windows-VK -> macOS CGKeyCode map, applied at send time.
// The wire protocol stays Windows VK; translation happens only in the Mac
// sender (CGEventPostToPid path). Fail-closed: Map returns null for mouse
// buttons and anything unmapped — the caller must drop the key, never guess
// or silently substitute.
// No dependency on Core Keys (owned by another stream); VK constants are
// re-declared here as plain bytes.
//
// COVERAGE: every VK the addon can put on the wire (addon/MaxDpsBridgeExp/
// Keymap.lua — letters, digits, F1-F24, numpad, OEM punctuation, navigation,
// lock keys, mouse) is listed below. The Mac side has a real CGKeyCode for all
// of them EXCEPT keys that do not exist on a macOS ANSI keyboard (PrintScreen,
// Pause/Break, NumLock, ScrollLock, F21-F24); those stay null on purpose and
// are reported once via NoteUnmapped so the miss is visible instead of silent.
//
// Keycodes are the Carbon HIToolbox constants (kVK_*), which are the values
// CGEventCreateKeyboardEvent takes for a US ANSI layout.

using System.Collections.Concurrent;

namespace MaxDpsCompanion.Mac;

internal static class MacVkMap
{
    // ---- Windows VK anchors (byte values, not Core Keys) ----
    private const byte VkBack = 0x08;
    private const byte VkTab = 0x09;
    private const byte VkReturn = 0x0D;
    private const byte VkShift = 0x10;
    private const byte VkControl = 0x11;
    private const byte VkMenu = 0x12; // Alt
    private const byte VkEscape = 0x1B;
    private const byte VkSpace = 0x20;
    private const byte VkPageUp = 0x21;
    private const byte VkPageDown = 0x22;
    private const byte VkEnd = 0x23;
    private const byte VkHome = 0x24;
    private const byte VkLeft = 0x25;
    private const byte VkUp = 0x26;
    private const byte VkRight = 0x27;
    private const byte VkDown = 0x28;
    private const byte VkInsert = 0x2D;
    private const byte VkDelete = 0x2E;
    private const byte VkLWin = 0x5B;
    private const byte VkRWin = 0x5C;

    // ---- CGKeyCode anchors (Carbon kVK_*) ----
    // Letters are ANSI positional (QWERTY assumption): e.g. VK_A -> kVK_ANSI_A.
    private const ushort CgA = 0x00;
    private const ushort CgS = 0x01;
    private const ushort CgD = 0x02;
    private const ushort CgF = 0x03;
    private const ushort CgH = 0x04;
    private const ushort CgG = 0x05;
    private const ushort CgZ = 0x06;
    private const ushort CgX = 0x07;
    private const ushort CgC = 0x08;
    private const ushort CgV = 0x09;
    private const ushort CgB = 0x0B;
    private const ushort CgQ = 0x0C;
    private const ushort CgW = 0x0D;
    private const ushort CgE = 0x0E;
    private const ushort CgR = 0x0F;
    private const ushort CgY = 0x10;
    private const ushort CgT = 0x11;
    private const ushort CgO = 0x1F;
    private const ushort CgU = 0x20;
    private const ushort CgI = 0x22;
    private const ushort CgP = 0x23;
    private const ushort CgL = 0x25;
    private const ushort CgJ = 0x26;
    private const ushort CgK = 0x28;
    private const ushort CgN = 0x2D;
    private const ushort CgM = 0x2E;

    // Digits are scrambled on ANSI: 1/2/3/4 -> 12/13/14/15, 5/6 -> 17/16,
    // 7/8/9/0 -> 1A/1C/19/1D.
    private const ushort Cg1 = 0x12;
    private const ushort Cg2 = 0x13;
    private const ushort Cg3 = 0x14;
    private const ushort Cg4 = 0x15;
    private const ushort Cg6 = 0x16;
    private const ushort Cg5 = 0x17;
    private const ushort Cg7 = 0x1A;
    private const ushort Cg8 = 0x1C;
    private const ushort Cg9 = 0x19;
    private const ushort Cg0 = 0x1D;

    // F1-F12 are scrambled: F1/F2/F3/F4 -> 7A/78/63/76,
    // F5/F6/F7/F8 -> 60/61/62/64, F9/F10/F11/F12 -> 65/6D/67/6F.
    private const ushort CgF1 = 0x7A;
    private const ushort CgF2 = 0x78;
    private const ushort CgF3 = 0x63;
    private const ushort CgF4 = 0x76;
    private const ushort CgF5 = 0x60;
    private const ushort CgF6 = 0x61;
    private const ushort CgF7 = 0x62;
    private const ushort CgF8 = 0x64;
    private const ushort CgF9 = 0x65;
    private const ushort CgF10 = 0x6D;
    private const ushort CgF11 = 0x67;
    private const ushort CgF12 = 0x6F;

    // F13-F20 (kVK_F13..kVK_F20).
    private const ushort CgF13 = 0x69;
    private const ushort CgF14 = 0x6B;
    private const ushort CgF15 = 0x71;
    private const ushort CgF16 = 0x6A;
    private const ushort CgF17 = 0x40;
    private const ushort CgF18 = 0x4F;
    private const ushort CgF19 = 0x50;
    private const ushort CgF20 = 0x5A;

    private const ushort CgSpace = 0x31;
    private const ushort CgEscape = 0x35;
    private const ushort CgTab = 0x30;
    private const ushort CgReturn = 0x24;
    private const ushort CgBack = 0x33;
    private const ushort CgLeft = 0x7B;
    private const ushort CgUp = 0x7E;
    private const ushort CgRight = 0x7C;
    private const ushort CgDown = 0x7D;

    // Navigation block (kVK_Home/End/PageUp/PageDown/Help/ForwardDelete).
    // kVK_Help is where a PC Insert key lands on an Apple keyboard.
    private const ushort CgHome = 0x73;
    private const ushort CgEnd = 0x77;
    private const ushort CgPageUp = 0x74;
    private const ushort CgPageDown = 0x79;
    private const ushort CgInsert = 0x72;        // kVK_Help
    private const ushort CgForwardDelete = 0x75; // kVK_ForwardDelete

    // OEM punctuation (ANSI positions).
    private const ushort CgSemicolon = 0x29;
    private const ushort CgEquals = 0x18;
    private const ushort CgComma = 0x2B;
    private const ushort CgMinus = 0x1B;
    private const ushort CgPeriod = 0x2F;
    private const ushort CgSlash = 0x2C;
    private const ushort CgGrave = 0x32;
    private const ushort CgLeftBracket = 0x21;
    private const ushort CgBackslash = 0x2A;
    private const ushort CgRightBracket = 0x1E;
    private const ushort CgQuote = 0x27;

    // Numeric keypad (kVK_ANSI_Keypad*). NumLock state is not modelled: the
    // keypad codes are positional, exactly like the Windows VK_NUMPAD0-9.
    private const ushort CgKeypad0 = 0x52;
    private const ushort CgKeypad1 = 0x53;
    private const ushort CgKeypad2 = 0x54;
    private const ushort CgKeypad3 = 0x55;
    private const ushort CgKeypad4 = 0x56;
    private const ushort CgKeypad5 = 0x57;
    private const ushort CgKeypad6 = 0x58;
    private const ushort CgKeypad7 = 0x59;
    private const ushort CgKeypad8 = 0x5B;
    private const ushort CgKeypad9 = 0x5C;
    private const ushort CgKeypadDecimal = 0x41;
    private const ushort CgKeypadMultiply = 0x43;
    private const ushort CgKeypadPlus = 0x45;
    private const ushort CgKeypadDivide = 0x4B;
    private const ushort CgKeypadMinus = 0x4E;

    // Modifiers: left/right distinction is lost (single CG code each).
    private const ushort CgShift = 0x38;
    private const ushort CgControl = 0x3B;
    private const ushort CgAlt = 0x3A;
    private const ushort CgCommand = 0x37; // Win key -> Command

    // ---- unmapped diagnostics (once per VK) ----

    private static readonly ConcurrentDictionary<byte, byte> Reported = new();

    /// <summary>
    /// Raised at most once per unmapped VK (UI/log sinks subscribe). The map
    /// itself stays pure; callers report through <see cref="NoteUnmapped"/>.
    /// </summary>
    internal static event Action<string>? Unmapped;

    /// <summary>
    /// Records a fail-closed miss exactly once per VK so a key the Mac cannot
    /// deliver is visible ("unmapped VK 44") instead of a silent no-op.
    /// </summary>
    internal static void NoteUnmapped(byte vk)
    {
        if (!Reported.TryAdd(vk, 0)) return;
        var line = $"unmapped VK {vk} (0x{vk:X2}): stroke skipped (fail-closed)";
        try { Unmapped?.Invoke(line); } catch { /* logging must never throw */ }
    }

    internal static ushort? Map(byte vk)
    {
        // Mouse buttons: never mappable to a key code (fail-closed).
        // 0x01 LBUTTON, 0x02 RBUTTON, 0x04 MIDDLE, 0x05 X1, 0x06 X2,
        // 0x07 undefined, 0x0B reserved.
        if (vk is 0x01 or 0x02 or 0x04 or 0x05 or 0x06 or 0x07 or 0x0B)
            return null;

        return vk switch
        {
            // Letters (ANSI positional).
            0x41 => CgA,
            0x42 => CgB,
            0x43 => CgC,
            0x44 => CgD,
            0x45 => CgE,
            0x46 => CgF,
            0x47 => CgG,
            0x48 => CgH,
            0x49 => CgI,
            0x4A => CgJ,
            0x4B => CgK,
            0x4C => CgL,
            0x4D => CgM,
            0x4E => CgN,
            0x4F => CgO,
            0x50 => CgP,
            0x51 => CgQ,
            0x52 => CgR,
            0x53 => CgS,
            0x54 => CgT,
            0x55 => CgU,
            0x56 => CgV,
            0x57 => CgW,
            0x58 => CgX,
            0x59 => CgY,
            0x5A => CgZ,
            // Digits (scrambled).
            0x31 => Cg1,
            0x32 => Cg2,
            0x33 => Cg3,
            0x34 => Cg4,
            0x35 => Cg5,
            0x36 => Cg6,
            0x37 => Cg7,
            0x38 => Cg8,
            0x39 => Cg9,
            0x30 => Cg0,
            // F1-F12 (scrambled).
            0x70 => CgF1,
            0x71 => CgF2,
            0x72 => CgF3,
            0x73 => CgF4,
            0x74 => CgF5,
            0x75 => CgF6,
            0x76 => CgF7,
            0x77 => CgF8,
            0x78 => CgF9,
            0x79 => CgF10,
            0x7A => CgF11,
            0x7B => CgF12,
            // F13-F20. F21-F24 (0x84-0x87): Carbon has no kVK constant for
            // them and no macOS keyboard emits them -> unmapped by design.
            0x7C => CgF13,
            0x7D => CgF14,
            0x7E => CgF15,
            0x7F => CgF16,
            0x80 => CgF17,
            0x81 => CgF18,
            0x82 => CgF19,
            0x83 => CgF20,
            // Numpad digits + operators (Keymap.lua NUMPAD0-9/DIVIDE/...).
            0x60 => CgKeypad0,
            0x61 => CgKeypad1,
            0x62 => CgKeypad2,
            0x63 => CgKeypad3,
            0x64 => CgKeypad4,
            0x65 => CgKeypad5,
            0x66 => CgKeypad6,
            0x67 => CgKeypad7,
            0x68 => CgKeypad8,
            0x69 => CgKeypad9,
            0x6A => CgKeypadMultiply,
            0x6B => CgKeypadPlus,
            0x6D => CgKeypadMinus,
            0x6E => CgKeypadDecimal,
            0x6F => CgKeypadDivide,
            // OEM punctuation (US layout; Keymap.lua - = [ ] \ ; ' , . / `).
            0xBA => CgSemicolon,
            0xBB => CgEquals,
            0xBC => CgComma,
            0xBD => CgMinus,
            0xBE => CgPeriod,
            0xBF => CgSlash,
            0xC0 => CgGrave,
            0xDB => CgLeftBracket,
            0xDC => CgBackslash,
            0xDD => CgRightBracket,
            0xDE => CgQuote,
            // Navigation.
            VkPageUp => CgPageUp,
            VkPageDown => CgPageDown,
            VkEnd => CgEnd,
            VkHome => CgHome,
            VkInsert => CgInsert,
            VkDelete => CgForwardDelete,
            // Controls.
            VkSpace => CgSpace,
            VkEscape => CgEscape,
            VkTab => CgTab,
            VkReturn => CgReturn,
            VkBack => CgBack,
            VkLeft => CgLeft,
            VkUp => CgUp,
            VkRight => CgRight,
            VkDown => CgDown,
            // Modifiers (left/right collapsed).
            VkShift => CgShift,
            VkControl => CgControl,
            VkMenu => CgAlt,
            VkLWin => CgCommand,
            VkRWin => CgCommand,
            // Anything else: unmapped -> null (fail-closed, never silent:
            // the sender reports it through NoteUnmapped).
            // Deliberately absent: VK_SNAPSHOT (0x2C), VK_PAUSE/BREAK (0x13),
            // VK_NUMLOCK (0x90), VK_SCROLL (0x91) — macOS has no equivalent
            // key, and substituting one would press a different key.
            _ => null,
        };
    }
}
