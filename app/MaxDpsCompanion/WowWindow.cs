using System.Diagnostics;

namespace MaxDpsCompanion;

/// <summary>Locates the game window and the screen position of its client area origin.</summary>
internal sealed class WowWindow
{
    private IntPtr _handle = IntPtr.Zero;
    private string _processName = "";

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

    /// <summary>
    /// Finds the game window by process name, caching the handle until it dies.
    /// The name is matched case-insensitively and without the .exe suffix, so
    /// "Wow", "WowClassic" and "Legion" all work as written in settings.ini.
    /// </summary>
    public bool Refresh(string processName)
    {
        if (IsValid && string.Equals(processName, _processName, StringComparison.OrdinalIgnoreCase))
            return true;

        _handle = IntPtr.Zero;
        _processName = processName;

        var wanted = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (!string.Equals(process.ProcessName, wanted, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (process.MainWindowHandle == IntPtr.Zero)
                    continue;

                _handle = process.MainWindowHandle;
                return true;
            }
        }

        return false;
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
