using System.Runtime.InteropServices;

namespace MaxDpsCompanion;

/// <summary>
/// Replays a keystroke into one window only: keyboard input is posted as
/// WM_KEYDOWN/WM_KEYUP to the attached game window, so it can never leak into
/// whatever happens to be focused (browser, chat, ...). The game does not
/// need focus and neither window needs to be in front.
/// Mouse buttons and the wheel have no per-window message equivalent and still
/// go through SendInput to the focused window — the foreground gate in the
/// engine stays mandatory for those.
/// </summary>
internal static class KeySender
{
    private const byte VkShift = 0x10;
    private const byte VkControl = 0x11;
    private const byte VkMenu = 0x12;

    /// <summary>
    /// Sends a keyboard stroke to <paramref name="target"/>. Returns false and
    /// sends nothing when the handle is dead or belongs to another process.
    /// </summary>
    public static bool SendToWindow(IntPtr target, uint expectedProcessId, KeyStroke stroke, int holdMilliseconds)
    {
        if (target == IntPtr.Zero || !Native.IsWindow(target)) return false;
        Native.GetWindowThreadProcessId(target, out var pid);
        if (expectedProcessId != 0 && pid != expectedProcessId) return false;

        var keys = new List<(byte Vk, bool Alt)>();
        if (stroke.Ctrl) keys.Add((VkControl, false));
        if (stroke.Alt) keys.Add((VkMenu, true));
        if (stroke.Shift) keys.Add((VkShift, false));
        keys.Add((stroke.VirtualKey, stroke.Alt));

        foreach (var (vk, alt) in keys)
        {
            var scan = (IntPtr)Native.MapVirtualKey(vk, Native.MAPVK_VK_TO_VSC);
            // lParam: repeat=1, scancode in bits 16-23, extended flag as needed.
            var lParam = (IntPtr)(1 | ((int)scan << 16) | (ExtendedKeys.Contains(vk) ? 1 << 24 : 0));
            var down = stroke.Alt || alt ? Native.WM_SYSKEYDOWN : Native.WM_KEYDOWN;
            Native.PostMessage(target, down, (IntPtr)vk, lParam);
        }

        if (holdMilliseconds > 0) Thread.Sleep(holdMilliseconds);

        for (var i = keys.Count - 1; i >= 0; i--)
        {
            var (vk, alt) = keys[i];
            var scan = (IntPtr)Native.MapVirtualKey(vk, Native.MAPVK_VK_TO_VSC);
            var lParam = (IntPtr)(1 | ((int)scan << 16) | (ExtendedKeys.Contains(vk) ? 1 << 24 : 0) | (1 << 30) | (1 << 31));
            var up = stroke.Alt || alt ? Native.WM_SYSKEYUP : Native.WM_KEYUP;
            Native.PostMessage(target, up, (IntPtr)vk, lParam);
        }

        return true;
    }

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

    private static readonly HashSet<byte> ExtendedKeys =
    [
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, // page/home/end/arrows
        0x2D, 0x2E,                                     // insert, delete
        0x6F,                                           // numpad divide
        0x90,                                           // num lock
    ];

    public static bool IsMouse(byte virtualKey) => virtualKey switch
    {
        VkLeftButton or VkRightButton or VkMiddleButton or VkXButton1 or VkXButton2
            or VkWheelUp or VkWheelDown => true,
        _ => false,
    };

    public static void Send(KeyStroke stroke, int holdMilliseconds)
    {
        var down = new List<Native.INPUT>(4);
        var up = new List<Native.INPUT>(4);

        // Modifiers are always keyboard, even when the bound input is a mouse button.
        if (stroke.Ctrl) { down.Add(Key(VkControl, false)); up.Insert(0, Key(VkControl, true)); }
        if (stroke.Alt) { down.Add(Key(VkMenu, false)); up.Insert(0, Key(VkMenu, true)); }
        if (stroke.Shift) { down.Add(Key(VkShift, false)); up.Insert(0, Key(VkShift, true)); }

        if (IsMouse(stroke.VirtualKey))
        {
            AppendMouse(stroke.VirtualKey, down, up);
        }
        else
        {
            down.Add(Key(stroke.VirtualKey, false));
            up.Insert(0, Key(stroke.VirtualKey, true));
        }

        Native.SendInput((uint)down.Count, down.ToArray(), Marshal.SizeOf<Native.INPUT>());
        if (holdMilliseconds > 0) Thread.Sleep(holdMilliseconds);
        if (up.Count > 0) Native.SendInput((uint)up.Count, up.ToArray(), Marshal.SizeOf<Native.INPUT>());
    }

    /// <summary>
    /// Adds the mouse half of the input. A wheel tick is a single event with no
    /// release, so it only contributes to the "down" batch.
    /// </summary>
    private static void AppendMouse(byte virtualKey, List<Native.INPUT> down, List<Native.INPUT> up)
    {
        switch (virtualKey)
        {
            case VkLeftButton:
                down.Add(Mouse(Native.MOUSEEVENTF_LEFTDOWN));
                up.Insert(0, Mouse(Native.MOUSEEVENTF_LEFTUP));
                break;
            case VkRightButton:
                down.Add(Mouse(Native.MOUSEEVENTF_RIGHTDOWN));
                up.Insert(0, Mouse(Native.MOUSEEVENTF_RIGHTUP));
                break;
            case VkMiddleButton:
                down.Add(Mouse(Native.MOUSEEVENTF_MIDDLEDOWN));
                up.Insert(0, Mouse(Native.MOUSEEVENTF_MIDDLEUP));
                break;
            case VkXButton1:
                down.Add(Mouse(Native.MOUSEEVENTF_XDOWN, Native.XBUTTON1));
                up.Insert(0, Mouse(Native.MOUSEEVENTF_XUP, Native.XBUTTON1));
                break;
            case VkXButton2:
                down.Add(Mouse(Native.MOUSEEVENTF_XDOWN, Native.XBUTTON2));
                up.Insert(0, Mouse(Native.MOUSEEVENTF_XUP, Native.XBUTTON2));
                break;
            case VkWheelUp:
                down.Add(Mouse(Native.MOUSEEVENTF_WHEEL, unchecked((uint)Native.WHEEL_DELTA)));
                break;
            case VkWheelDown:
                down.Add(Mouse(Native.MOUSEEVENTF_WHEEL, unchecked((uint)(-Native.WHEEL_DELTA))));
                break;
        }
    }

    private static Native.INPUT Mouse(uint flags, uint mouseData = 0) => new()
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

    private static Native.INPUT Key(byte virtualKey, bool keyUp)
    {
        var flags = Native.KEYEVENTF_SCANCODE;
        if (keyUp) flags |= Native.KEYEVENTF_KEYUP;
        if (ExtendedKeys.Contains(virtualKey)) flags |= Native.KEYEVENTF_EXTENDEDKEY;

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
