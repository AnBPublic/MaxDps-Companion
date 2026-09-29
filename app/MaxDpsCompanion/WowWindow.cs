using System.Diagnostics;

namespace MaxDpsCompanion;

/// <summary>Locates the game window and the screen position of its client area origin.</summary>
internal sealed class WowWindow
{
    // How often the cached handle is re-validated against the process's
    // CURRENT main window. The old cache trusted a handle for as long as
    // IsWindow() stayed true, so a handle that survived a window recreation
    // (relog, character switch, display-mode change) as a hidden/lingering
    // top-level window was kept forever — the "companion stops detecting the
    // strip until restart" bug. Cheap (~1/s) and self-healing.
    private const long VerifyIntervalMs = 1000;

    private IntPtr _handle = IntPtr.Zero;
    private string _processName = "";
    private long _lastVerifyMs;

    public IntPtr Handle => _handle;
    public bool IsValid => _handle != IntPtr.Zero && Native.IsWindow(_handle);
    public bool IsForeground => IsValid && Native.GetForegroundWindow() == _handle;

    /// <summary>Owning process id of the attached game window, for send-target checks.</summary>
    public uint ProcessId
    {
        get
        {
            if (!IsValid) return 0;
            Native.GetWindowThreadProcessId(_handle, out var pid);
            return pid;
        }
    }

    /// <summary>True while <paramref name="candidate"/> is our attached game window.</summary>
    public bool IsGameWindow(IntPtr candidate) =>
        IsValid && candidate == _handle;

    /// <summary>Drops the cached handle so the next Refresh re-resolves from scratch.</summary>
    public void Reset()
    {
        _handle = IntPtr.Zero;
        _lastVerifyMs = 0;
    }

    /// <summary>
    /// Finds the game window by process name. The name is matched
    /// case-insensitively and without the .exe suffix, so "Wow", "WowClassic"
    /// and "Legion" all work as written in settings.ini. The cached handle is
    /// trusted only while it is STILL the process's live main window: after a
    /// relog / character switch / display-mode change the game can recreate
    /// (or replace) its window, leaving our old handle alive but wrong. That
    /// stale-but-valid handle was the "stops detecting the strip until
    /// restart" bug — the periodic identity check re-resolves within a second.
    /// </summary>
    public bool Refresh(string processName)
    {
        var wanted = Normalise(processName);

        if (_handle != IntPtr.Zero
            && IsValid
            && string.Equals(processName, _processName, StringComparison.OrdinalIgnoreCase))
        {
            var now = Environment.TickCount64;
            if (now - _lastVerifyMs < VerifyIntervalMs) return true;
            _lastVerifyMs = now;
            if (StillCurrentMainWindow(wanted)) return true;
            // Fall through: the cached handle is no longer the game's main
            // window (recreated / re-owned). Re-resolve below.
        }

        _handle = IntPtr.Zero;
        _processName = processName;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (!string.Equals(process.ProcessName, wanted, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (process.MainWindowHandle == IntPtr.Zero)
                    continue;

                _handle = process.MainWindowHandle;
                _lastVerifyMs = Environment.TickCount64;
                return true;
            }
        }

        return false;
    }

    private static string Normalise(string processName) =>
        processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;

    /// <summary>
    /// True while the cached handle belongs to the wanted process AND Windows
    /// still reports it as that process's main window. A non-zero main window
    /// that differs means the game recreated its window and the cache is
    /// stale. A zero main window (e.g. mid-load) keeps the cached handle.
    /// </summary>
    private bool StillCurrentMainWindow(string wanted)
    {
        try
        {
            if (Native.GetWindowThreadProcessId(_handle, out var pid) == 0 || pid == 0) return false;
            using var process = Process.GetProcessById((int)pid);
            if (!string.Equals(process.ProcessName, wanted, StringComparison.OrdinalIgnoreCase)) return false;
            var main = process.MainWindowHandle;
            return main == IntPtr.Zero || main == _handle;
        }
        catch
        {
            // Process exited / access denied: treat as stale and re-resolve.
            return false;
        }
    }

    /// <summary>
    /// Screen coordinates of the client area's top-left corner, which is where
    /// WoW anchors UIParent's TOPLEFT and therefore where the pixel block starts.
    /// </summary>
    public bool TryGetClientOrigin(out Point origin, out Size clientSize)
    {
        origin = Point.Empty;
        clientSize = Size.Empty;

        if (!IsValid) return false;
        if (!Native.GetClientRect(_handle, out var rect)) return false;

        var point = new Native.POINT { X = 0, Y = 0 };
        if (!Native.ClientToScreen(_handle, ref point)) return false;

        origin = new Point(point.X, point.Y);
        clientSize = new Size(rect.Right - rect.Left, rect.Bottom - rect.Top);
        return clientSize.Width > 0 && clientSize.Height > 0;
    }
}
