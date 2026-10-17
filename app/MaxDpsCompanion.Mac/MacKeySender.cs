using System.Drawing;
using System.Runtime.InteropServices;

namespace MaxDpsCompanion.Mac;

// IKeySender for the Mac host.
//
// KEYBOARD = per-process delivery (CGEventPostToPid), the macOS counterpart of
// the Windows PostMessage-to-HWND route: the stroke goes into the game's own
// event stream, so it needs no focus and it can never leak into whatever the
// user has in front. (The previous port posted every stroke to the GLOBAL HID
// tap: with the game unfocused that typed into the frontmost app, and it
// depended on focus for everything.)
//
// MOUSE / WHEEL keep the global HID route: click coordinates are screen
// positions and there is no per-process equivalent that the game reads
// correctly, so the engine's foreground gate stays mandatory for them
// (identical to the Windows hybrid).
//
// Fail-closed: an unmapped virtual key or an unknown mouse code is refused,
// never guessed. A KEYBOARD stroke is NEVER posted to the global HID tap: with
// no target pid the stroke is dropped (and reported), because a global key
// event goes to whatever app is frontmost. Only mouse/wheel strokes use the
// global route, and those are re-gated on the game being frontmost right
// before the event.
internal sealed class MacKeySender : IKeySender
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    private const int kCGHIDEventTap = 0;

    // CGEventType (CGEventTypes.h)
    private const int kCGEventLeftMouseDown = 1;    // +1 = up
    private const int kCGEventRightMouseDown = 3;   // +1 = up
    private const int kCGEventOtherMouseDown = 25;  // +1 = up (middle / X1 / X2)
    private const int kCGEventScrollWheel = 22;

    // CGEventFlags (CGEventTypes.h)
    private const ulong kCGEventFlagMaskShift = 0x00020002;
    private const ulong kCGEventFlagMaskControl = 0x00040001;
    private const ulong kCGEventFlagMaskAlternate = 0x00080020;

    [StructLayout(LayoutKind.Sequential)]
    private struct CgPoint
    {
        public double X, Y;
    }

    [DllImport(CoreGraphics, EntryPoint = "CGEventCreateKeyboardEvent")]
    private static extern IntPtr CGEventCreateKeyboardEvent(
        IntPtr source, ushort keyCode,
        [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [DllImport(CoreGraphics, EntryPoint = "CGEventSetFlags")]
    private static extern void CGEventSetFlags(IntPtr ev, ulong flags);

    [DllImport(CoreGraphics, EntryPoint = "CGEventPost")]
    private static extern void CGEventPost(int tap, IntPtr ev);

    [DllImport(CoreGraphics, EntryPoint = "CGEventPostToPid")]
    private static extern void CGEventPostToPid(int pid, IntPtr ev);

    [DllImport(CoreGraphics, EntryPoint = "CGEventCreateMouseEvent")]
    private static extern IntPtr CGEventCreateMouseEvent(
        IntPtr source, int mouseType, CgPoint mouseCursorPosition, int mouseButton);

    [DllImport(CoreGraphics, EntryPoint = "CGEventCreate")]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport(CoreGraphics, EntryPoint = "CGEventGetLocation")]
    private static extern CgPoint CGEventGetLocation(IntPtr ev);

    [DllImport(CoreGraphics, EntryPoint = "CGEventCreateScrollWheelEvent")]
    private static extern IntPtr CGEventCreateScrollWheelEvent1(
        IntPtr source, int units, uint wheelCount, int wheel1);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", EntryPoint = "CFRelease")]
    private static extern void CFRelease(IntPtr obj);

    /// <summary>Game pid for per-process keyboard delivery (set by the host).</summary>
    public int TargetPid { get; set; }

    public bool IsMouse(byte virtualKey) => Keys.IsMouse(virtualKey);

    public string DescribeStroke(Keys.KeyRef stroke) => Keys.Describe(stroke);

    public bool SendToWindow(IntPtr target, uint expectedProcessId, Keys.KeyRef stroke, int holdMilliseconds)
    {
        if (IsMouse(stroke.VirtualKey))
        {
            // Mouse strokes are HID-only: the engine gates them on focus and
            // SendMouse re-checks the frontmost pid at send time.
            SendMouse(stroke, Flags(stroke), holdMilliseconds);
            return true;
        }
        if (expectedProcessId == 0)
        {
            // No process identity -> no per-process route -> fail closed.
            // Never fall back to the global tap for a keyboard stroke.
            NoteNoTarget(stroke);
            return false;
        }
        return SendKeyboardToPid((int)expectedProcessId, stroke, holdMilliseconds);
    }

    public void Send(Keys.KeyRef stroke, int holdMilliseconds)
    {
        if (IsMouse(stroke.VirtualKey))
        {
            SendMouse(stroke, Flags(stroke), holdMilliseconds);
            return;
        }
        // Keyboard: per-process only (the host keeps TargetPid current). With
        // no pid the stroke is dropped — IKeySender.Send cannot report a
        // failure, so the drop is reported once per VK.
        if (TargetPid <= 0)
        {
            NoteNoTarget(stroke);
            return;
        }
        SendKeyboardToPid(TargetPid, stroke, holdMilliseconds);
    }

    /// <summary>Raised once per VK when a keyboard stroke had no target pid.</summary>
    internal static event Action<string>? Dropped;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<byte, byte> DropReported = new();

    private static void NoteNoTarget(Keys.KeyRef stroke)
    {
        if (!DropReported.TryAdd(stroke.VirtualKey, 0)) return;
        try
        {
            Dropped?.Invoke(
                $"keyboard stroke VK 0x{stroke.VirtualKey:X2} has no target pid: dropped (fail-closed)");
        }
        catch { /* logging must never throw */ }
    }

    private static ulong Flags(Keys.KeyRef stroke)
    {
        ulong f = 0;
        if (stroke.Shift) f |= kCGEventFlagMaskShift;
        if (stroke.Ctrl) f |= kCGEventFlagMaskControl;
        if (stroke.Alt) f |= kCGEventFlagMaskAlternate;
        return f;
    }

    /// <summary>Per-process keyboard stroke: target needs neither focus nor a window handle.</summary>
    private bool SendKeyboardToPid(int pid, Keys.KeyRef stroke, int holdMilliseconds)
    {
        if (pid <= 0) { NoteNoTarget(stroke); return false; }
        var code = MacVkMap.Map(stroke.VirtualKey);
        if (code is null)
        {
            MacVkMap.NoteUnmapped(stroke.VirtualKey); // fail-closed, reported once
            return false;
        }
        var flags = Flags(stroke);
        var ok = Post(pid, code.Value, down: true, flags);
        if (!ok) return false;
        if (holdMilliseconds > 0) Thread.Sleep(holdMilliseconds);
        Post(pid, code.Value, down: false, flags);
        return true;
    }

    /// <summary>
    /// Keyboard event delivery. pid MUST be > 0: keyboard events are only ever
    /// posted per-process (CGEventPostToPid). The global tap is not reachable
    /// from here by construction, so a missing pid can never type into the
    /// frontmost app.
    /// </summary>
    private static bool Post(int pid, ushort code, bool down, ulong flags)
    {
        if (pid <= 0) return false;                  // fail-closed
        var ev = CGEventCreateKeyboardEvent(IntPtr.Zero, code, down);
        if (ev == IntPtr.Zero) return false;
        try
        {
            if (flags != 0) CGEventSetFlags(ev, flags);
            CGEventPostToPid(pid, ev);
            return true;
        }
        finally { CFRelease(ev); }
    }

    private void SendMouse(Keys.KeyRef stroke, ulong flags, int holdMilliseconds)
    {
        // Mouse events are global (no per-process route the game reads), so the
        // focus requirement is re-checked HERE, at send time: the engine's
        // cached foreground flag can be up to 250 ms stale, and a click that
        // lands in another app is worse than a skipped action.
        if (TargetPid > 0 && !MacCaptureStream.GameIsFrontmost(TargetPid)) return;

        switch (stroke.Vk)
        {
            case 0x07: Scroll(1); return;   // WheelUp
            case 0x0B: Scroll(-1); return;  // WheelDown
        }
        int type, button;
        switch (stroke.Vk)
        {
            case 0x01: type = kCGEventLeftMouseDown; button = 0; break;
            case 0x02: type = kCGEventRightMouseDown; button = 1; break;
            case 0x04: type = kCGEventOtherMouseDown; button = 2; break;  // middle
            case 0x05: type = kCGEventOtherMouseDown; button = 3; break;  // X1 (MB4)
            case 0x06: type = kCGEventOtherMouseDown; button = 4; break;  // X2 (MB5)
            default: return; // fail-closed
        }

        // A CGEvents mouse event carries its own cursor location; posting at
        // (0,0) would move the pointer into the screen corner and click there.
        var at = CurrentCursor();

        var modDowns = new List<IntPtr>();
        try
        {
            if (stroke.Shift) modDowns.Add(FlagEvent(0x38, true));
            if (stroke.Ctrl) modDowns.Add(FlagEvent(0x3B, true));
            if (stroke.Alt) modDowns.Add(FlagEvent(0x3A, true));
            foreach (var e in modDowns) if (e != IntPtr.Zero) CGEventPost(kCGHIDEventTap, e);

            var down = CGEventCreateMouseEvent(IntPtr.Zero, type, at, button);
            if (down != IntPtr.Zero)
            {
                try { CGEventPost(kCGHIDEventTap, down); }
                finally { CFRelease(down); }
            }
            if (holdMilliseconds > 0) Thread.Sleep(holdMilliseconds);
            var up = CGEventCreateMouseEvent(IntPtr.Zero, type + 1, at, button);
            if (up != IntPtr.Zero)
            {
                try { CGEventPost(kCGHIDEventTap, up); }
                finally { CFRelease(up); }
            }
        }
        finally
        {
            foreach (var e in modDowns) if (e != IntPtr.Zero) CFRelease(e);
            if (stroke.Shift) PostFlag(0x38, false);
            if (stroke.Ctrl) PostFlag(0x3B, false);
            if (stroke.Alt) PostFlag(0x3A, false);
        }
    }

    /// <summary>Current cursor position (the click must land where the user's pointer is).</summary>
    private static CgPoint CurrentCursor()
    {
        var probe = CGEventCreate(IntPtr.Zero);
        if (probe == IntPtr.Zero) return new CgPoint { X = 0, Y = 0 };
        try { return CGEventGetLocation(probe); }
        finally { CFRelease(probe); }
    }

    private static IntPtr FlagEvent(ushort code, bool down) =>
        CGEventCreateKeyboardEvent(IntPtr.Zero, code, down);

    private static void PostFlag(ushort code, bool down)
    {
        var e = CGEventCreateKeyboardEvent(IntPtr.Zero, code, down);
        if (e == IntPtr.Zero) return;
        try { CGEventPost(kCGHIDEventTap, e); }
        finally { CFRelease(e); }
    }

    private static void Scroll(int lines)
    {
        // units=1 (line), wheelCount=1
        var e = CGEventCreateScrollWheelEvent1(IntPtr.Zero, 1, 1, lines);
        if (e == IntPtr.Zero) return;
        try { CGEventPost(kCGHIDEventTap, e); }
        finally { CFRelease(e); }
    }
}
