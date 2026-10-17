using System.Runtime.InteropServices;

namespace MaxDpsCompanion;

/// <summary>
/// Replays a keystroke into one window only: keyboard input is posted as
/// WM_KEYDOWN/WM_KEYUP to the attached game window by default, so it can never
/// leak into whatever happens to be focused (browser, chat, ...). The game does
/// not need focus and neither window needs to be in front.
///
/// 4.1 A2.2: a stroke carrying Ctrl/Alt/Shift can instead be routed through
/// SendInput ([Window] ModifierInput=Auto/SendInput — see
/// <see cref="ModifierInputMode"/> and <see cref="ChordPlanner"/>); the default
/// stays Post, i.e. today's exact delivery.
///
/// Mouse buttons and the wheel have no per-window message equivalent and still
/// go through SendInput to the focused window — the foreground gate in the
/// engine stays mandatory for those.
/// </summary>
internal sealed class KeySender
{
    internal const byte VkShift = 0x10;
    internal const byte VkControl = 0x11;
    internal const byte VkMenu = 0x12;

    /// <summary>Process-wide sender (the engine's seam). Tests build their own with a fake backend.</summary>
    public static KeySender Default { get; } = new();

    private readonly KeyDelivery _delivery;

    public KeySender(KeyDelivery? delivery = null) => _delivery = delivery ?? new KeyDelivery();

    /// <summary>
    /// Sends a keyboard stroke to <paramref name="target"/>. Returns a failure
    /// outcome when the handle is dead or belongs to another process (nothing
    /// is sent), or a held outcome with the visible reason when the chord was
    /// deferred. <paramref name="mode"/> + <paramref name="gameForeground"/>
    /// are the [Window] ModifierInput seam (A2.2 f).
    /// </summary>
    public SendOutcome SendToWindow(
        IntPtr target,
        uint expectedProcessId,
        KeyStroke stroke,
        int holdMilliseconds,
        ModifierInputMode mode,
        bool gameForeground) =>
        _delivery.SendToWindow(target, expectedProcessId, stroke, holdMilliseconds, mode, gameForeground);

    /// <summary>True while our own synthetic press of <paramref name="virtualKey"/> is settling.</summary>
    public bool IsOwnInjection(byte virtualKey) => _delivery.IsOwnInjection(virtualKey);

    /// <summary>Key-up Ctrl/Shift/Alt only while our own injection is outstanding (A2.2 e).</summary>
    public void PanicRelease() => _delivery.PanicRelease();

    /// <summary>Sends a stroke (keyboard or mouse) to the focused window; false when SendInput was blocked.</summary>
    public bool SendFocused(KeyStroke stroke, int holdMilliseconds) => _delivery.SendFocused(stroke, holdMilliseconds);

    // Mouse codes as encoded by the addon. Buttons reuse their real virtual-key
    // codes, which a keyboard can never produce; the wheel borrows two values
    // Windows leaves undefined.
    private const byte VkLeftButton = 0x01;
    private const byte VkRightButton = 0x02;
    private const byte VkMiddleButton = 0x04;
    private const byte VkXButton1 = 0x05;
    private const byte VkXButton2 = 0x06;
    private const byte VkWheelUp = 0x07;
    private const byte VkWheelDown = 0x0B;

    /// <summary>
    /// VKs whose lParam carries the extended-key bit (bit 24).
    ///
    /// VK_CONTROL / VK_MENU (0x11/0x12) are deliberately NOT extended: the wire
    /// only carries the generic VK, MapVirtualKey resolves it to the LEFT scan
    /// code (0x1D/0x38), and marking it extended would silently turn a
    /// left-Ctrl binding into right-Ctrl. VK_RETURN (0x0D) is left alone for
    /// the same reason: there is no separate numpad-Enter VK, so the encoding
    /// cannot distinguish main Enter from numpad Enter, and an extended flag
    /// would turn every Enter into the numpad key.
    ///
    /// The right-hand modifier VKs (0xA3 right Ctrl, 0xA5 right Alt) ARE
    /// unambiguous when they arrive (a future addon / a direct config can send
    /// them), so they are marked extended "where the VK allows".
    /// </summary>
    private static readonly HashSet<byte> ExtendedKeys =
    [
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, // page/home/end/arrows
        0x2D, 0x2E,                                     // insert, delete
        0x6F,                                           // numpad divide
        0x90,                                           // num lock
        0xA3,                                           // right Ctrl
        0xA5,                                           // right Alt
    ];

    internal static bool IsExtendedKey(byte virtualKey) => ExtendedKeys.Contains(virtualKey);

    /// <summary>
    /// Pure lParam composition. Bit 0 = repeat count 1, bits 16-23 = scan code,
    /// bit 24 = extended key, bits 30/31 = previous state / transition on key-up.
    /// The uint cast is required: bit 31 makes the int negative, and
    /// <c>(nint)(int)</c> sign-extends on x64 (the pre-4.1 bug: every key-up
    /// lParam became 0xFFFFFFFF80000001 instead of 0x0000000080000001).
    /// </summary>
    internal static nint ComposeLParam(uint scanCode, bool extended, bool up)
    {
        var value = 1u | ((scanCode & 0xFFu) << 16);
        if (extended) value |= 1u << 24;
        if (up) value |= (1u << 30) | (1u << 31);
        return unchecked((nint)value);
    }

    /// <summary>lParam for one key event of <paramref name="virtualKey"/>.</summary>
    internal static nint BuildLParam(byte virtualKey, bool up) =>
        ComposeLParam(Native.MapVirtualKey(virtualKey, Native.MAPVK_VK_TO_VSC), IsExtendedKey(virtualKey), up);

    public static bool IsMouse(byte virtualKey) => virtualKey switch
    {
        VkLeftButton or VkRightButton or VkMiddleButton or VkXButton1 or VkXButton2
            or VkWheelUp or VkWheelDown => true,
        _ => false,
    };

    /// <summary>Human-readable name incl. mouse buttons (MB5 etc. — the
    /// Interact-on-mouse case). KeyNames.Describe lacks mouse codes.</summary>
    public static string DescribeStroke(KeyStroke stroke)
    {
        var prefix = (stroke.Ctrl ? "Ctrl+" : "") + (stroke.Alt ? "Alt+" : "") + (stroke.Shift ? "Shift+" : "");
        return prefix + (IsMouse(stroke.VirtualKey) ? MouseName(stroke.VirtualKey) : KeyNames.Describe(stroke.VirtualKey));
    }

    private static string MouseName(byte vk) => vk switch
    {
        VkLeftButton => "Mouse1",
        VkRightButton => "Mouse2",
        VkMiddleButton => "Mouse3",
        VkXButton1 => "Mouse4",
        VkXButton2 => "Mouse5",
        VkWheelUp => "WheelUp",
        VkWheelDown => "WheelDown",
        _ => $"VK{vk:X2}",
    };

    /// <summary>Focused send (keyboard or mouse) through the process-wide sender.</summary>
    public static bool Send(KeyStroke stroke, int holdMilliseconds) => Default.SendFocused(stroke, holdMilliseconds);

    /// <summary>
    /// Adds the mouse half of the input. A wheel tick is a single event with no
    /// release, so it only contributes to the "down" batch.
    /// </summary>
    internal static void AppendMouse(byte virtualKey, List<Native.INPUT> down, List<Native.INPUT> up)
    {
        switch (virtualKey)
        {
            case VkLeftButton:
                down.Add(MouseInput(Native.MOUSEEVENTF_LEFTDOWN));
                up.Insert(0, MouseInput(Native.MOUSEEVENTF_LEFTUP));
                break;
            case VkRightButton:
                down.Add(MouseInput(Native.MOUSEEVENTF_RIGHTDOWN));
                up.Insert(0, MouseInput(Native.MOUSEEVENTF_RIGHTUP));
                break;
            case VkMiddleButton:
                down.Add(MouseInput(Native.MOUSEEVENTF_MIDDLEDOWN));
                up.Insert(0, MouseInput(Native.MOUSEEVENTF_MIDDLEUP));
                break;
            case VkXButton1:
                down.Add(MouseInput(Native.MOUSEEVENTF_XDOWN, Native.XBUTTON1));
                up.Insert(0, MouseInput(Native.MOUSEEVENTF_XUP, Native.XBUTTON1));
                break;
            case VkXButton2:
                down.Add(MouseInput(Native.MOUSEEVENTF_XDOWN, Native.XBUTTON2));
                up.Insert(0, MouseInput(Native.MOUSEEVENTF_XUP, Native.XBUTTON2));
                break;
            case VkWheelUp:
                down.Add(MouseInput(Native.MOUSEEVENTF_WHEEL, unchecked((uint)Native.WHEEL_DELTA)));
                break;
            case VkWheelDown:
                down.Add(MouseInput(Native.MOUSEEVENTF_WHEEL, unchecked((uint)(-Native.WHEEL_DELTA))));
                break;
        }
    }

    private static Native.INPUT MouseInput(uint flags, uint mouseData = 0) => new()
    {
        type = Native.INPUT_MOUSE,
        mi = new Native.MOUSEINPUT
        {
            dx = 0,
            dy = 0,
            mouseData = mouseData,
            dwFlags = flags,
            time = 0,
            dwExtraInfo = IntPtr.Zero,
        },
    };

    /// <summary>Keyboard event (scancode-based, extended flag where the VK is unambiguous).</summary>
    internal static Native.INPUT KeyInput(byte virtualKey, bool keyUp)
    {
        var flags = Native.KEYEVENTF_SCANCODE;
        if (keyUp) flags |= Native.KEYEVENTF_KEYUP;
        if (IsExtendedKey(virtualKey)) flags |= Native.KEYEVENTF_EXTENDEDKEY;

        return new Native.INPUT
        {
            type = Native.INPUT_KEYBOARD,
            ki = new Native.KEYBDINPUT
            {
                wVk = 0,
                wScan = (ushort)Native.MapVirtualKey(virtualKey, Native.MAPVK_VK_TO_VSC),
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        };
    }
}
