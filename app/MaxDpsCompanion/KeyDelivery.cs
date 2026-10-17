using System.Runtime.InteropServices;

namespace MaxDpsCompanion;

/// <summary>
/// Every Win32 call the key sender makes, behind one injectable seam so the
/// delivery logic (Post vs SendInput routing, chord press/release bookkeeping
/// and the failure paths) is unit-tested without a game window.
/// </summary>
internal interface IKeyDeliveryBackend
{
    bool IsWindow(IntPtr hwnd);
    uint GetWindowProcessId(IntPtr hwnd);
    IntPtr GetForegroundWindow();
    bool PostMessage(IntPtr hwnd, uint message, nint wParam, nint lParam);
    uint SendInput(Native.INPUT[] inputs, int size);
    uint MapVirtualKey(uint code);
    bool IsDown(byte virtualKey);
    void Sleep(int milliseconds);
}

/// <summary>Live backend: the exact pairs of Win32 calls the app already uses.</summary>
internal sealed class NativeKeyDeliveryBackend : IKeyDeliveryBackend
{
    public bool IsWindow(IntPtr hwnd) => Native.IsWindow(hwnd);

    public uint GetWindowProcessId(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    public IntPtr GetForegroundWindow() => Native.GetForegroundWindow();

    public bool PostMessage(IntPtr hwnd, uint message, nint wParam, nint lParam) =>
        Native.PostMessage(hwnd, message, (IntPtr)wParam, (IntPtr)lParam);

    public uint SendInput(Native.INPUT[] inputs, int size) =>
        Native.SendInput((uint)inputs.Length, inputs, size);

    public uint MapVirtualKey(uint code) => Native.MapVirtualKey(code, Native.MAPVK_VK_TO_VSC);

    public bool IsDown(byte virtualKey)
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

    public void Sleep(int milliseconds) => Thread.Sleep(milliseconds);
}

/// <summary>
/// One delivery attempt: <see cref="Sent"/> false means nothing reached the
/// game. <see cref="Note"/> is the visible status line reason — a deliberate
/// hold (background chord, held modifier, chord needs focus) or, when
/// <see cref="Sent"/> is true, the informational background-chord note. Null
/// with <see cref="Sent"/> false is the engine's WindowLost outcome.
/// </summary>
internal readonly record struct SendOutcome(bool Sent, string? Note)
{
    public static SendOutcome Ok => new(true, null);

    /// <summary>Sent, with an informational status line note.</summary>
    public static SendOutcome OkWithNote(string note) => new(true, note);

    /// <summary>Delivery failed for a window/handle reason (window lost, UIPI).</summary>
    public static SendOutcome Lost => new(false, null);

    /// <summary>Deliberate hold with a status line note (never a send failure).</summary>
    public static SendOutcome Held(string note) => new(false, note);
}

/// <summary>
/// 4.1 A2.1/A2.2: one keystroke into one window.
///
/// Keyboard input is still posted as WM_KEYDOWN/WM_KEYUP to the attached game
/// window by default, so it can never leak into whatever happens to be
/// focused. A stroke carrying Ctrl/Alt/Shift can be routed through SendInput
/// instead ([Window] ModifierInput=Auto/SendInput) with the safety rules from
/// the audit remediation plan:
///
///  a. never press/release a modifier the user physically holds;
///  b. a held modifier the stroke does not contain defers the chord;
///  c. the foreground is re-checked immediately before SendInput, up events
///     are always sent in <c>finally</c>, and the SendInput return count is
///     checked (UIPI / elevated game returns 0);
///  d. every VK we inject is recorded as an "own injection" window
///     (send start .. end + 30 ms) so the physical-key scan never re-queues
///     our own synthetic press;
///  e. <see cref="PanicRelease"/> key-ups Ctrl/Shift/Alt only while our own
///     injection is still outstanding (a failed release batch).
/// </summary>
internal sealed class KeyDelivery
{
    /// <summary>How long after our own release a VK still counts as ours.</summary>
    internal const int OwnInjectionGraceMs = 30;

    private const int BitCtrl = 1;
    private const int BitShift = 2;
    private const int BitAlt = 4;

    private readonly IKeyDeliveryBackend _backend;
    private readonly Func<long> _nowMs;
    private readonly object _gate = new();

    // Per-VK own-injection window (index = VK). -1 = no window recorded.
    private readonly long[] _ownFrom = new long[256];
    private readonly long[] _ownUntil = new long[256];

    // Modifier keys our own SendInput batch pressed and did not see released.
    private int _ownModifiersDown;

    public KeyDelivery(IKeyDeliveryBackend? backend = null, Func<long>? nowMs = null)
    {
        _backend = backend ?? new NativeKeyDeliveryBackend();
        _nowMs = nowMs ?? (() => Environment.TickCount64);
        Array.Fill(_ownFrom, -1L);
        Array.Fill(_ownUntil, -1L);
    }

    /// <summary>True while our own injection of <paramref name="virtualKey"/> may still be settling.</summary>
    public bool IsOwnInjection(byte virtualKey)
    {
        lock (_gate)
        {
            var now = _nowMs();
            var until = _ownUntil[virtualKey];
            return until >= 0 && now >= _ownFrom[virtualKey] && now <= until;
        }
    }

    /// <summary>True when a modifier we pressed was never confirmed released.</summary>
    public bool HasOutstandingModifiers
    {
        get { lock (_gate) return _ownModifiersDown != 0; }
    }

    /// <summary>
    /// Sends a keyboard stroke to <paramref name="target"/>.
    /// <paramref name="mode"/> and <paramref name="gameForeground"/> are the
    /// [Window] ModifierInput seam (A2.2 f); the foreground is re-checked
    /// inside the SendInput branch before any global input is synthesised.
    /// </summary>
    public SendOutcome SendToWindow(
        IntPtr target,
        uint expectedProcessId,
        KeyStroke stroke,
        int holdMilliseconds,
        ModifierInputMode mode,
        bool gameForeground)
    {
        if (target == IntPtr.Zero || !_backend.IsWindow(target)) return SendOutcome.Lost;
        var pid = _backend.GetWindowProcessId(target);
        if (expectedProcessId != 0 && pid != expectedProcessId) return SendOutcome.Lost;

        if (KeySender.IsMouse(stroke.VirtualKey)) return SendOutcome.Lost; // mouse has no per-window route

        lock (_gate)
        {
            var plan = ChordPlanner.Decide(
                mode, stroke, gameForeground,
                shiftDown: _backend.IsDown(KeySender.VkShift),
                ctrlDown: _backend.IsDown(KeySender.VkControl),
                altDown: _backend.IsDown(KeySender.VkMenu));

            switch (plan.Route)
            {
                case ChordRoute.Defer:
                    return SendOutcome.Held(plan.Note ?? "chord deferred");

                case ChordRoute.SendInput:
                {
                    // (c) authoritative re-check immediately before the global send.
                    if (!_backend.IsWindow(target) || _backend.GetForegroundWindow() != target)
                    {
                        return mode == ModifierInputMode.Auto
                            ? PostStroke(target, stroke, holdMilliseconds,
                                "background chord: posted (modifiers may not register)")
                            : SendOutcome.Held(
                                $"chord {stroke.Describe()} needs the game focused (ModifierInput=SendInput)");
                    }

                    return SendChordViaInput(target, stroke, holdMilliseconds, plan);
                }

                default:
                    return PostStroke(target, stroke, holdMilliseconds, null);
            }
        }
    }

    /// <summary>
    /// Posted stroke (A2.1): every PostMessage result is checked, the up path
    /// runs in <c>finally</c> for exactly the keys whose down was accepted, and
    /// any failed down/up reports the window as lost.
    /// </summary>
    private SendOutcome PostStroke(IntPtr target, KeyStroke stroke, int holdMilliseconds, string? note)
    {
        var keys = BuildPostKeys(stroke);
        var pressed = new List<(byte Vk, bool Alt)>(keys.Count);
        var ok = true;
        try
        {
            foreach (var (vk, alt) in keys)
            {
                var down = stroke.Alt || alt ? Native.WM_SYSKEYDOWN : Native.WM_KEYDOWN;
                if (!_backend.PostMessage(target, down, vk, KeySender.BuildLParam(vk, up: false)))
                {
                    ok = false;
                    break;
                }
                pressed.Add((vk, alt));
            }
            if (ok && holdMilliseconds > 0) _backend.Sleep(holdMilliseconds);
        }
        finally
        {
            for (var i = pressed.Count - 1; i >= 0; i--)
            {
                var (vk, alt) = pressed[i];
                var up = stroke.Alt || alt ? Native.WM_SYSKEYUP : Native.WM_KEYUP;
                if (!_backend.PostMessage(target, up, vk, KeySender.BuildLParam(vk, up: true))) ok = false;
            }
        }

        if (!ok) return SendOutcome.Lost;
        return note is null ? SendOutcome.Ok : SendOutcome.OkWithNote(note);
    }

    /// <summary>
    /// The key order the Post path has always used: Ctrl, Alt, Shift, base key.
    /// The Alt flag marks the modifier that needs WM_SYS* messages.
    /// </summary>
    internal static List<(byte Vk, bool Alt)> BuildPostKeys(in KeyStroke stroke)
    {
        var keys = new List<(byte Vk, bool Alt)>(4);
        if (stroke.Ctrl) keys.Add((KeySender.VkControl, false));
        if (stroke.Alt) keys.Add((KeySender.VkMenu, true));
        if (stroke.Shift) keys.Add((KeySender.VkShift, false));
        keys.Add((stroke.VirtualKey, stroke.Alt));
        return keys;
    }

    /// <summary>
    /// Global chord delivery (A2.2 a/c/d/e). Up events always run; the down
    /// batch is only pressed for modifiers the user is not holding.
    /// </summary>
    private SendOutcome SendChordViaInput(IntPtr target, KeyStroke stroke, int holdMilliseconds, in ChordPlan plan)
    {
        var modBits = (plan.PressCtrl ? BitCtrl : 0) | (plan.PressShift ? BitShift : 0) | (plan.PressAlt ? BitAlt : 0);
        var pressedVks = new List<byte>(4);
        var start = _nowMs();
        var ok = true;
        try
        {
            var down = new List<Native.INPUT>(4);
            if (plan.PressCtrl) { down.Add(KeySender.KeyInput(KeySender.VkControl, keyUp: false)); pressedVks.Add(KeySender.VkControl); }
            if (plan.PressAlt) { down.Add(KeySender.KeyInput(KeySender.VkMenu, keyUp: false)); pressedVks.Add(KeySender.VkMenu); }
            if (plan.PressShift) { down.Add(KeySender.KeyInput(KeySender.VkShift, keyUp: false)); pressedVks.Add(KeySender.VkShift); }
            down.Add(KeySender.KeyInput(stroke.VirtualKey, keyUp: false));
            pressedVks.Add(stroke.VirtualKey);

            var sentDown = _backend.SendInput(down.ToArray(), Marshal.SizeOf<Native.INPUT>());
            if (sentDown > 0) _ownModifiersDown |= modBits;   // (e): remember what may be stuck down
            if (sentDown != down.Count) ok = false;           // (c): UIPI / elevated game returns 0
            else if (holdMilliseconds > 0) _backend.Sleep(holdMilliseconds);
        }
        finally
        {
            // Release base first, then our own modifiers in reverse press order.
            var up = new List<Native.INPUT>(4) { KeySender.KeyInput(stroke.VirtualKey, keyUp: true) };
            if (plan.PressShift) up.Add(KeySender.KeyInput(KeySender.VkShift, keyUp: true));
            if (plan.PressAlt) up.Add(KeySender.KeyInput(KeySender.VkMenu, keyUp: true));
            if (plan.PressCtrl) up.Add(KeySender.KeyInput(KeySender.VkControl, keyUp: true));

            var released = _backend.SendInput(up.ToArray(), Marshal.SizeOf<Native.INPUT>());
            if (released == up.Count) _ownModifiersDown &= ~modBits;
            else ok = false;

            // (d) own-injection window for the physical-key scan.
            var end = _nowMs() + OwnInjectionGraceMs;
            foreach (var vk in pressedVks) { _ownFrom[vk] = start; _ownUntil[vk] = end; }
        }

        return ok ? SendOutcome.Ok : SendOutcome.Lost;
    }

    /// <summary>
    /// (e) Panic release: key-up Ctrl/Shift/Alt ONLY while our own injection is
    /// outstanding (a chord whose release batch was not confirmed).
    /// </summary>
    public void PanicRelease()
    {
        lock (_gate)
        {
            if (_ownModifiersDown == 0) return;
            var up = new List<Native.INPUT>(3);
            if ((_ownModifiersDown & BitCtrl) != 0) up.Add(KeySender.KeyInput(KeySender.VkControl, keyUp: true));
            if ((_ownModifiersDown & BitShift) != 0) up.Add(KeySender.KeyInput(KeySender.VkShift, keyUp: true));
            if ((_ownModifiersDown & BitAlt) != 0) up.Add(KeySender.KeyInput(KeySender.VkMenu, keyUp: true));
            _backend.SendInput(up.ToArray(), Marshal.SizeOf<Native.INPUT>());
            _ownModifiersDown = 0;
        }
    }

    /// <summary>
    /// Focused-window send (mouse buttons / wheel, and any stroke the per-window
    /// route cannot express). Returns false when SendInput inserted fewer
    /// events than requested (UIPI / elevated game).
    /// </summary>
    public bool SendFocused(KeyStroke stroke, int holdMilliseconds)
    {
        var down = new List<Native.INPUT>(4);
        var up = new List<Native.INPUT>(4);

        // Modifiers are always keyboard, even when the bound input is a mouse button.
        if (stroke.Ctrl) { down.Add(KeySender.KeyInput(KeySender.VkControl, keyUp: false)); up.Insert(0, KeySender.KeyInput(KeySender.VkControl, keyUp: true)); }
        if (stroke.Alt) { down.Add(KeySender.KeyInput(KeySender.VkMenu, keyUp: false)); up.Insert(0, KeySender.KeyInput(KeySender.VkMenu, keyUp: true)); }
        if (stroke.Shift) { down.Add(KeySender.KeyInput(KeySender.VkShift, keyUp: false)); up.Insert(0, KeySender.KeyInput(KeySender.VkShift, keyUp: true)); }

        if (KeySender.IsMouse(stroke.VirtualKey)) KeySender.AppendMouse(stroke.VirtualKey, down, up);
        else
        {
            down.Add(KeySender.KeyInput(stroke.VirtualKey, keyUp: false));
            up.Insert(0, KeySender.KeyInput(stroke.VirtualKey, keyUp: true));
        }

        var sentDown = _backend.SendInput(down.ToArray(), Marshal.SizeOf<Native.INPUT>());
        var ok = sentDown == down.Count;
        if (holdMilliseconds > 0) _backend.Sleep(holdMilliseconds);
        var sentUp = up.Count == 0 ? 0u : _backend.SendInput(up.ToArray(), Marshal.SizeOf<Native.INPUT>());
        return ok && sentUp == up.Count;
    }
}
