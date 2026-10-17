using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 4.1 A2.1 + A2.2: lParam composition, the checked Post failure path, and the
/// modifier-chord routing decision (a-g) exercised through the injectable
/// poster/probe/clock seams. No game window, no global input: the fake backend
/// records every PostMessage/SendInput call.
/// </summary>
public class KeySenderTests
{
    private static readonly KeyStroke KeyF = new(0x46, false, false, false);

    private static KeyStroke CtrlShiftAlt(byte vk) => new(vk, Shift: true, Ctrl: true, Alt: true);

    private sealed class FakeBackend : IKeyDeliveryBackend
    {
        public IntPtr Target { get; init; } = new(0x1234);
        public uint Pid { get; init; } = 42;
        public IntPtr Foreground { get; set; }
        public bool WindowValid { get; set; } = true;
        public readonly Dictionary<byte, bool> PhysicallyDown = new();

        /// <summary>Zero-based PostMessage attempt index that returns false, or -1.</summary>
        public int FailPostAtIndex { get; set; } = -1;

        private int _postAttempts;

        /// <summary>A SendInput batch containing a KEYUP returns 0 (UIPI).</summary>
        public bool FailUpBatch { get; set; }

        /// <summary>Every SendInput returns 0.</summary>
        public bool FailAllInput { get; set; }

        public readonly List<(uint Msg, byte Vk, long LParam)> Posts = new();
        public readonly List<Native.INPUT[]> Inputs = new();
        public int SleptMs { get; private set; }

        public bool IsWindow(IntPtr hwnd) => WindowValid && hwnd == Target;

        public uint GetWindowProcessId(IntPtr hwnd) => hwnd == Target ? Pid : 0;

        public IntPtr GetForegroundWindow() => Foreground;

        public bool PostMessage(IntPtr hwnd, uint message, nint wParam, nint lParam)
        {
            var attempt = _postAttempts++;
            if (attempt == FailPostAtIndex) return false;
            Posts.Add((message, (byte)(int)wParam, (long)lParam));
            return true;
        }

        public uint SendInput(Native.INPUT[] inputs, int size)
        {
            Inputs.Add(inputs);
            if (FailAllInput) return 0;
            if (FailUpBatch && inputs.Any(i => (i.ki.dwFlags & Native.KEYEVENTF_KEYUP) != 0)) return 0;
            return (uint)inputs.Length;
        }

        public uint MapVirtualKey(uint code) => Native.MapVirtualKey(code, Native.MAPVK_VK_TO_VSC);

        public bool IsDown(byte virtualKey) => PhysicallyDown.GetValueOrDefault(virtualKey);

        public void Sleep(int milliseconds) => SleptMs += milliseconds;
    }

    private static KeySender Sender(FakeBackend backend, Func<long>? clock = null) =>
        new(new KeyDelivery(backend, clock));

    // ---------- A2.1: lParam composition ----------

    [Fact]
    public void ComposeLParam_KeyDown_Is_RepeatCount_Plus_Scancode()
    {
        Assert.Equal(0x001E0001L, (long)KeySender.ComposeLParam(0x1E, extended: false, up: false));
    }

    [Fact]
    public void ComposeLParam_KeyUp_Sets_Transition_Bits_And_Stays_Unsigned()
    {
        // The pre-4.1 bug: (nint)(int)0xC01E0001 sign-extended to
        // 0xFFFFFFFFC01E0001 on x64. The composed value must be the plain
        // 32-bit pattern, positive as a signed 64-bit integer.
        var lParam = KeySender.ComposeLParam(0x1E, extended: false, up: true);
        Assert.Equal(0x00000000C01E0001UL, (ulong)(long)lParam);
        Assert.True((long)lParam > 0);
    }

    [Fact]
    public void ComposeLParam_Extended_Sets_Bit_24()
    {
        var lParam = KeySender.ComposeLParam(0x4B, extended: true, up: true);
        Assert.Equal(0x00000000C14B0001UL, (ulong)(long)lParam);
        Assert.True((long)lParam > 0);
    }

    [Fact]
    public void BuildLParam_Uses_The_Real_Scancode_And_Is_Not_Sign_Extended()
    {
        var scan = Native.MapVirtualKey(0x25, Native.MAPVK_VK_TO_VSC); // Left arrow (extended)
        var up = KeySender.BuildLParam(0x25, up: true);
        Assert.Equal((long)scan << 16, (long)up & 0xFF0000);
        Assert.True(((long)up & (1 << 24)) != 0);
        Assert.True((long)up > 0);

        var down = KeySender.BuildLParam(0x41, up: false);
        Assert.Equal(1L, (long)down & 0xFFFF);
        Assert.Equal(0L, (long)down & (1L << 24));
    }

    [Fact]
    public void Extended_Key_Table_Excludes_Ambiguous_VKs()
    {
        Assert.True(KeySender.IsExtendedKey(0x25));   // Left
        Assert.True(KeySender.IsExtendedKey(0x2E));   // Delete
        Assert.True(KeySender.IsExtendedKey(0xA3));   // Right Ctrl
        Assert.True(KeySender.IsExtendedKey(0xA5));   // Right Alt
        // Generic modifiers and Return carry no side/numpad information on the
        // wire: never marked extended (that would flip left->right, Enter->numpad).
        Assert.False(KeySender.IsExtendedKey(0x11));
        Assert.False(KeySender.IsExtendedKey(0x12));
        Assert.False(KeySender.IsExtendedKey(0x0D));
    }

    // ---------- A2.1: checked Post path ----------

    [Fact]
    public void Post_Chord_Sends_Modifiers_Then_Key_And_Reverses_The_Ups()
    {
        var backend = new FakeBackend();
        var outcome = Sender(backend).SendToWindow(
            backend.Target, backend.Pid, CtrlShiftAlt(0x46), holdMilliseconds: 0,
            ModifierInputMode.Post, gameForeground: false);

        Assert.True(outcome.Sent);
        Assert.Null(outcome.Note);
        Assert.Equal(8, backend.Posts.Count);
        // Alt is part of the stroke, so EVERY message is a WM_SYS* message
        // (the pre-4.1 rule, preserved: stroke.Alt || this-key-is-Alt).
        Assert.Equal((Native.WM_SYSKEYDOWN, (byte)0x11), (backend.Posts[0].Msg, backend.Posts[0].Vk));   // Ctrl
        Assert.Equal((Native.WM_SYSKEYDOWN, (byte)0x12), (backend.Posts[1].Msg, backend.Posts[1].Vk));   // Alt
        Assert.Equal((Native.WM_SYSKEYDOWN, (byte)0x10), (backend.Posts[2].Msg, backend.Posts[2].Vk));   // Shift
        Assert.Equal((Native.WM_SYSKEYDOWN, (byte)0x46), (backend.Posts[3].Msg, backend.Posts[3].Vk));   // F
        Assert.Equal((Native.WM_SYSKEYUP, (byte)0x46), (backend.Posts[4].Msg, backend.Posts[4].Vk));
        Assert.Equal((Native.WM_SYSKEYUP, (byte)0x10), (backend.Posts[5].Msg, backend.Posts[5].Vk));
        Assert.Equal((Native.WM_SYSKEYUP, (byte)0x12), (backend.Posts[6].Msg, backend.Posts[6].Vk));
        Assert.Equal((Native.WM_SYSKEYUP, (byte)0x11), (backend.Posts[7].Msg, backend.Posts[7].Vk));
    }

    [Fact]
    public void Post_Down_Failure_Returns_Lost_And_Releases_Only_Accepted_Keys()
    {
        var backend = new FakeBackend { FailPostAtIndex = 1 };   // Alt down fails
        var outcome = Sender(backend).SendToWindow(
            backend.Target, backend.Pid, CtrlShiftAlt(0x46), 0, ModifierInputMode.Post, false);

        Assert.False(outcome.Sent);
        Assert.Null(outcome.Note);                               // window-lost, not a deliberate hold
        Assert.Equal(2, backend.Posts.Count);                    // Ctrl down + Ctrl up only
        Assert.Equal((Native.WM_SYSKEYDOWN, (byte)0x11), (backend.Posts[0].Msg, backend.Posts[0].Vk));
        Assert.Equal((Native.WM_SYSKEYUP, (byte)0x11), (backend.Posts[1].Msg, backend.Posts[1].Vk));
    }

    [Fact]
    public void Post_Up_Failure_Returns_Lost()
    {
        var backend = new FakeBackend { FailPostAtIndex = 1 };   // F down ok, F up fails
        var outcome = Sender(backend).SendToWindow(
            backend.Target, backend.Pid, KeyF, 0, ModifierInputMode.Post, false);

        Assert.False(outcome.Sent);
        Assert.Single(backend.Posts);                            // the accepted down is the only recorded post
    }

    [Fact]
    public void Dead_Window_Posts_Nothing()
    {
        var backend = new FakeBackend { WindowValid = false };
        var outcome = Sender(backend).SendToWindow(backend.Target, backend.Pid, KeyF, 0, ModifierInputMode.Post, true);

        Assert.False(outcome.Sent);
        Assert.Empty(backend.Posts);
    }

    [Fact]
    public void Foreign_Process_Posts_Nothing()
    {
        var backend = new FakeBackend();
        var outcome = Sender(backend).SendToWindow(backend.Target, expectedProcessId: 999, KeyF, 0, ModifierInputMode.Post, true);

        Assert.False(outcome.Sent);
        Assert.Empty(backend.Posts);
    }

    [Fact]
    public void Hold_Waits_On_The_Injected_Clock()
    {
        var backend = new FakeBackend();
        var outcome = Sender(backend).SendToWindow(backend.Target, backend.Pid, KeyF, 25, ModifierInputMode.Post, true);

        Assert.True(outcome.Sent);
        Assert.Equal(25, backend.SleptMs);
    }

    // ---------- A2.2: modifier routing decision (pure) ----------

    [Fact]
    public void Plain_Key_Always_Uses_The_Per_Window_Post_Route()
    {
        foreach (var mode in Enum.GetValues<ModifierInputMode>())
        {
            var plan = ChordPlanner.Decide(mode, KeyF, gameForeground: true, false, false, false);
            Assert.Equal(ChordRoute.Post, plan.Route);
            Assert.Null(plan.Note);
        }
    }

    [Fact]
    public void Post_Mode_Keeps_Today_Behaviour_For_Chords()
    {
        var plan = ChordPlanner.Decide(ModifierInputMode.Post, CtrlShiftAlt(0x46), true, true, true, true);
        Assert.Equal(ChordRoute.Post, plan.Route);
        Assert.Null(plan.Note);
    }

    [Fact]
    public void Auto_Foreground_Chord_Uses_SendInput()
    {
        var plan = ChordPlanner.Decide(ModifierInputMode.Auto, new KeyStroke(0x46, false, true, false), true, false, false, false);
        Assert.Equal(ChordRoute.SendInput, plan.Route);
        Assert.True(plan.PressCtrl);
        Assert.False(plan.PressShift);
        Assert.False(plan.PressAlt);
        Assert.Null(plan.Note);
    }

    [Fact]
    public void Auto_Background_Chord_Falls_Back_To_Post_With_A_Note()
    {
        var plan = ChordPlanner.Decide(ModifierInputMode.Auto, new KeyStroke(0x46, false, true, false), false, false, false, false);
        Assert.Equal(ChordRoute.Post, plan.Route);
        Assert.Contains("posted", plan.Note);
    }

    [Fact]
    public void SendInput_Background_Chord_Defers_Instead_Of_Leaking_Input()
    {
        var plan = ChordPlanner.Decide(ModifierInputMode.SendInput, new KeyStroke(0x46, false, true, false), false, false, false, false);
        Assert.Equal(ChordRoute.Defer, plan.Route);
        Assert.Contains("focused", plan.Note);
    }

    [Fact]
    public void An_Extra_Held_Modifier_Defers_The_Chord()
    {
        // Shift held + Ctrl+X: SendInput cannot clear Shift, so the game would
        // see Shift+Ctrl+X. Defer with a note naming the modifier.
        var plan = ChordPlanner.Decide(ModifierInputMode.SendInput, new KeyStroke(0x58, false, true, false), true, true, false, false);
        Assert.Equal(ChordRoute.Defer, plan.Route);
        Assert.Contains("Shift", plan.Note);
    }

    [Fact]
    public void A_Modifier_The_User_Already_Holds_Is_Not_Pressed_Or_Released()
    {
        var plan = ChordPlanner.Decide(ModifierInputMode.SendInput, new KeyStroke(0x46, false, true, false), true, false, true, false);
        Assert.Equal(ChordRoute.SendInput, plan.Route);
        Assert.False(plan.PressCtrl);   // already down: never ours to release
    }

    [Fact]
    public void Post_Mode_Does_Not_Query_Or_Defer_On_Held_Modifiers()
    {
        // The default path must stay byte-identical: no physical-modifier gate.
        var plan = ChordPlanner.Decide(ModifierInputMode.Post, new KeyStroke(0x58, false, true, false), false, true, false, false);
        Assert.Equal(ChordRoute.Post, plan.Route);
    }

    // ---------- A2.2: live SendInput chord behaviour ----------

    [Fact]
    public void SendInput_Chord_Presses_Only_Missing_Modifiers_And_Releases_Them()
    {
        var backend = new FakeBackend { Foreground = (IntPtr)0x1234 };
        backend.PhysicallyDown[0x10] = true;   // user holds Shift
        var stroke = new KeyStroke(0x46, Shift: true, Ctrl: true, Alt: false);

        var outcome = Sender(backend).SendToWindow(backend.Target, backend.Pid, stroke, 0,
            ModifierInputMode.SendInput, gameForeground: true);

        Assert.True(outcome.Sent);
        Assert.Empty(backend.Posts);
        Assert.Equal(2, backend.Inputs.Count);
        var down = backend.Inputs[0];
        var up = backend.Inputs[1];
        Assert.Equal(2, down.Length);   // Ctrl + F (Shift already held)
        Assert.Equal(2, up.Length);     // F + Ctrl (Shift never released)
        Assert.Equal((ushort)Native.MapVirtualKey(0x11, Native.MAPVK_VK_TO_VSC), down[0].ki.wScan);
        Assert.Equal(0u, down[0].ki.dwFlags & Native.KEYEVENTF_KEYUP);
        Assert.Equal((ushort)Native.MapVirtualKey(0x46, Native.MAPVK_VK_TO_VSC), up[0].ki.wScan);
        Assert.NotEqual(0u, up[0].ki.dwFlags & Native.KEYEVENTF_KEYUP);
    }

    [Fact]
    public void SendInput_Chord_Rechecks_The_Foreground_Before_Sending()
    {
        var backend = new FakeBackend { Foreground = (IntPtr)0x9999 };   // changed under us
        var outcome = Sender(backend).SendToWindow(backend.Target, backend.Pid,
            new KeyStroke(0x46, false, true, false), 0, ModifierInputMode.Auto, gameForeground: true);

        // Auto falls back to the per-window Post route and says so.
        Assert.True(outcome.Sent);
        Assert.Contains("background chord", outcome.Note);
        Assert.Empty(backend.Inputs);
        Assert.Equal(4, backend.Posts.Count);   // Ctrl down + F down, then F up + Ctrl up
    }

    [Fact]
    public void SendInput_Chord_Rechecks_The_Foreground_And_Holds_In_Explicit_Mode()
    {
        var backend = new FakeBackend { Foreground = IntPtr.Zero };
        var outcome = Sender(backend).SendToWindow(backend.Target, backend.Pid,
            new KeyStroke(0x46, false, true, false), 0, ModifierInputMode.SendInput, gameForeground: true);

        Assert.False(outcome.Sent);
        Assert.Contains("focused", outcome.Note);
        Assert.Empty(backend.Inputs);
        Assert.Empty(backend.Posts);
    }

    [Fact]
    public void SendInput_Failure_Returns_Lost_And_PanicRelease_Releases_Our_Modifiers()
    {
        var backend = new FakeBackend { Foreground = (IntPtr)0x1234, FailUpBatch = true };
        var sender = Sender(backend);

        var outcome = sender.SendToWindow(backend.Target, backend.Pid,
            new KeyStroke(0x46, false, true, false), 0, ModifierInputMode.SendInput, gameForeground: true);

        Assert.False(outcome.Sent);             // the release batch was refused
        Assert.Null(outcome.Note);              // a delivery failure, not a hold
        sender.PanicRelease();

        // down (Ctrl+F), refused up (F+Ctrl), panic up (Ctrl)
        Assert.Equal(3, backend.Inputs.Count);
        var panic = backend.Inputs[2];
        Assert.Single(panic);
        Assert.Equal((ushort)Native.MapVirtualKey(0x11, Native.MAPVK_VK_TO_VSC), panic[0].ki.wScan);
        Assert.NotEqual(0u, panic[0].ki.dwFlags & Native.KEYEVENTF_KEYUP);
    }

    [Fact]
    public void PanicRelease_Is_A_NoOp_When_Nothing_Is_Outstanding()
    {
        var backend = new FakeBackend();
        Sender(backend).PanicRelease();
        Assert.Empty(backend.Inputs);
    }

    [Fact]
    public void Own_Injection_Window_Covers_The_Settle_Grace_Only()
    {
        var backend = new FakeBackend { Foreground = (IntPtr)0x1234 };
        var now = 1000L;
        var sender = Sender(backend, () => now);

        var outcome = sender.SendToWindow(backend.Target, backend.Pid,
            new KeyStroke(0x46, false, true, false), 0, ModifierInputMode.SendInput, gameForeground: true);
        Assert.True(outcome.Sent);

        Assert.True(sender.IsOwnInjection(0x46));
        now += 30;
        Assert.True(sender.IsOwnInjection(0x46));     // still inside start..end+30
        now += 1;
        Assert.False(sender.IsOwnInjection(0x46));    // scan may queue again
    }

    [Fact]
    public void Post_Route_Records_No_Own_Injection()
    {
        var backend = new FakeBackend();
        var now = 500L;
        var sender = Sender(backend, () => now);

        var outcome = sender.SendToWindow(backend.Target, backend.Pid, KeyF, 0, ModifierInputMode.Post, true);
        Assert.True(outcome.Sent);
        Assert.False(sender.IsOwnInjection(0x46));
    }

    // ---------- A2.1/A2.2: focused send (mouse path) ----------

    [Fact]
    public void Focused_Send_Reports_Uipi_Block()
    {
        var backend = new FakeBackend { FailAllInput = true };
        Assert.False(Sender(backend).SendFocused(new KeyStroke(0x06, false, false, true), 0));
    }

    [Fact]
    public void Focused_Send_Ok_Inserts_Mouse_And_Modifier_Events()
    {
        var backend = new FakeBackend();
        Assert.True(Sender(backend).SendFocused(new KeyStroke(0x06, false, false, true), 0));
        Assert.Equal(2, backend.Inputs.Count);
        Assert.Equal(2, backend.Inputs[0].Length);   // Alt + XBUTTON2
        Assert.Equal(2, backend.Inputs[1].Length);
    }
}
