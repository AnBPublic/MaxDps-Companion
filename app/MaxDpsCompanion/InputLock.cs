using System.Runtime.InteropServices;

namespace MaxDpsCompanion;

/// <summary>
/// Low-level keyboard lock: swallows the user's physical keystrokes while the
/// calibrate pattern runs, so stray typing can't corrupt the profile or leak
/// into game chat. Installed only for the sampling window and always removed
/// in a finally block. Synthetic SendInput from this process is unaffected
/// (LL hooks see injected input flagged and let it through).
/// </summary>
internal sealed class InputLock : IDisposable
{
    private IntPtr _hook = IntPtr.Zero;
    private Native.HookProc? _proc;
    private bool _disposed;

    /// <summary>Installs the lock. Must be called on a thread with a message loop (the UI thread).</summary>
    public InputLock Install()
    {
        if (_hook != IntPtr.Zero) return this;
        _proc = Hook;
        var module = Native.GetModuleHandle(null);
        _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, module, 0);
        return this;
    }

    private IntPtr Hook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

        var msg = (uint)wParam.ToInt64();
        var isKey = msg == Native.WM_KEYDOWN_MSG || msg == Native.WM_KEYUP_MSG
            || msg == Native.WM_SYSKEYDOWN_MSG || msg == Native.WM_SYSKEYUP_MSG;
        if (!isKey) return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

        // Swallow physical keystrokes only; injected input (LLKHF_INJECTED)
        // passes through so our own SendInput keeps working.
        const int LLKHF_INJECTED = 0x10;
        var flags = Marshal.ReadInt32(lParam, 8);
        if ((flags & LLKHF_INJECTED) != 0)
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

        return (IntPtr)1; // swallow
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hook != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _proc = null;
        GC.SuppressFinalize(this);
    }
}
