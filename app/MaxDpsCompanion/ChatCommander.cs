using System.Runtime.InteropServices;

namespace MaxDpsCompanion;

/// <summary>
/// One-click calibration driver: focuses the game window, types a slash
/// command into its chat box via clipboard paste (layout-independent — no
/// reliance on the '/' scancode), verifies the pattern actually appears on
/// screen, and restores the previous foreground window. Never touches
/// movement keys: only Enter, Ctrl+V, letters and Space.
/// Default command prefix is 'mdb' (e.g. /mdb calibrate on, /mdb status).
/// </summary>
internal static class ChatCommander
{
    private const byte VkReturn = 0x0D;
    private const byte VkControl = 0x11;
    private const byte VkV = 0x56;
    private const byte VkEscape = 0x1B;

    /// <summary>
    /// Sends a chat command silently: the chat box is opened, the command is
    /// pasted + Enter, then Escape closes any leftover edit box WITHOUT
    /// sending. Escape never types, so nothing the user typed can leak and
    /// no half-open box keeps swallowing keys.
    /// </summary>
    public static bool SendChatCommandSilent(WowWindow game, string command, int settleMs = 800)
    {
        if (!SendChatCommand(game, command, settleMs, closeKey: VkEscape)) return false;
        Thread.Sleep(100);
        return true;
    }

    /// <summary>
    /// Focuses <paramref name="game"/> (restoring it if minimised), pastes
    /// "/<paramref name="command"/>" into chat + Enter, waits
    /// <paramref name="settleMs"/>, then restores the previous foreground
    /// window. Returns false when the game window is unusable.
    ///
    /// Key discipline (WoW fires actions on key-DOWN, so a stuck chat key is
    /// worse than a missed one): ONLY scancode Enter (chat open / send) and
    /// Ctrl+V (paste) are ever emitted. Plain-text typing is never used — no
    /// letter, Space, Tab or V key is ever pressed as itself — so even if the
    /// chat box fails to open, no keybind (nameplates, bags, map, mount...)
    /// can fire as a side effect. If the chat box never opens, the paste
    /// lands nowhere and the trailing Enter is a harmless world-click-noop.
    /// </summary>
    public static bool SendChatCommand(WowWindow game, string command, int settleMs = 800)
        => SendChatCommand(game, command, settleMs, closeKey: VkReturn);

    private static bool SendChatCommand(WowWindow game, string command, int settleMs, byte closeKey)
    {
        if (!game.IsValid) return false;
        var previous = Native.GetForegroundWindow();

        if (Native.IsIconic(game.Handle))
            Native.ShowWindow(game.Handle, Native.SW_RESTORE);
        Native.SetForegroundWindow(game.Handle);
        Thread.Sleep(350);
        if (Native.GetForegroundWindow() != game.Handle) return false;

        // Enter opens the chat box first — typing into the world would eat keys.
        // No verification possible from outside, so assume nothing: the only
        // keys below are Enter and Ctrl+V, both inert in the world.
        PressKey(VkReturn);
        Thread.Sleep(300);
        PasteText("/" + command);
        Thread.Sleep(150);
        PressKey(VkReturn);
        Thread.Sleep(settleMs);

        // If the chat box never opened (focus race, cinematic, loading
        // screen), the paste went nowhere — but a half-open chat edit box
        // may still hold the text. closeKey sends-or-noops; Escape (silent
        // path) discards the draft, Enter (legacy path) sends it. Either
        // way it is still just one inert key, never a binding.
        PressKey(closeKey);
        Thread.Sleep(200);

        if (previous != IntPtr.Zero && previous != game.Handle && Native.IsWindow(previous))
            Native.SetForegroundWindow(previous);
        return true;
    }

    /// <summary>
    /// Pastes text through the clipboard so non-US layouts work. Restores the
    /// previous clipboard content afterwards.
    /// </summary>
    private static void PasteText(string text)
    {
        var saved = StealClipboard();
        try
        {
            SetClipboardText(text);
            // Ctrl+V
            KeyDown(VkControl);
            TapKey(VkV);
            KeyUp(VkControl);
        }
        finally
        {
            RestoreClipboard(saved);
        }
    }

    private static byte[]? StealClipboard()
    {
        try
        {
            if (!Native.OpenClipboard(IntPtr.Zero)) return null;
            try
            {
                var handle = Native.GetClipboardData(Native.CF_UNICODETEXT);
                if (handle == IntPtr.Zero) return null;
                var size = (int)Native.GlobalSize(handle);
                if (size <= 0) return null;
                var locked = Native.GlobalLock(handle);
                if (locked == IntPtr.Zero) return null;
                var bytes = new byte[size];
                Marshal.Copy(locked, bytes, 0, size);
                Native.GlobalUnlock(handle);
                return bytes;
            }
            finally
            {
                Native.CloseClipboard();
            }
        }
        catch { return null; }
    }

    private static void SetClipboardText(string text)
    {
        var bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
        var handle = Native.GlobalAlloc(Native.GMEM_MOVEABLE, (UIntPtr)bytes.Length);
        if (handle == IntPtr.Zero) return;
        var locked = Native.GlobalLock(handle);
        if (locked == IntPtr.Zero) return;
        Marshal.Copy(bytes, 0, locked, bytes.Length);
        Native.GlobalUnlock(handle);
        if (!Native.OpenClipboard(IntPtr.Zero)) return;
        try
        {
            Native.EmptyClipboard();
            Native.SetClipboardData(Native.CF_UNICODETEXT, handle);
            handle = IntPtr.Zero; // owned by the clipboard now
        }
        finally
        {
            Native.CloseClipboard();
        }
    }

    private static void RestoreClipboard(byte[]? saved)
    {
        if (saved is null) return;
        try
        {
            var handle = Native.GlobalAlloc(Native.GMEM_MOVEABLE, (UIntPtr)saved.Length);
            if (handle == IntPtr.Zero) return;
            var locked = Native.GlobalLock(handle);
            if (locked == IntPtr.Zero) return;
            Marshal.Copy(saved, 0, locked, saved.Length);
            Native.GlobalUnlock(handle);
            if (!Native.OpenClipboard(IntPtr.Zero)) return;
            try
            {
                Native.EmptyClipboard();
                Native.SetClipboardData(Native.CF_UNICODETEXT, handle);
            }
            finally
            {
                Native.CloseClipboard();
            }
        }
        catch { /* best effort */ }
    }

    private static void TapKey(byte virtualKey)
    {
        KeyDown(virtualKey);
        KeyUp(virtualKey);
    }

    private static void KeyDown(byte virtualKey) => SendScancode(virtualKey, keyUp: false);

    private static void KeyUp(byte virtualKey) => SendScancode(virtualKey, keyUp: true);

    private static void PressKey(byte virtualKey)
    {
        TapKey(virtualKey);
        Thread.Sleep(30);
    }

    private static void SendScancode(byte virtualKey, bool keyUp)
    {
        var scancode = (ushort)Native.MapVirtualKey(virtualKey, Native.MAPVK_VK_TO_VSC);
        var flags = Native.KEYEVENTF_SCANCODE;
        if (keyUp) flags |= Native.KEYEVENTF_KEYUP;
        var input = new Native.INPUT
        {
            type = Native.INPUT_KEYBOARD,
            ki = new Native.KEYBDINPUT
            {
                wVk = 0,
                wScan = scancode,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        };
        var size = Marshal.SizeOf<Native.INPUT>();
        Native.SendInput(1, [input], size);
        Thread.Sleep(30);
    }
}
