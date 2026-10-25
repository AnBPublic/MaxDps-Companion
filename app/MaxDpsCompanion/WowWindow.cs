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

    private readonly Func<long> _clock;
    private readonly Func<string, IntPtr> _findMainWindow;

    private IntPtr _handle = IntPtr.Zero;
    private string _processName = "";
    private long _lastVerifyMs;
    // 4.1 A2.7: when no game window resolves, Process.GetProcesses() used to be
    // re-enumerated on EVERY tick (33 ms) while waiting for the game — the most
    // expensive thing the idle path did. Sweeps are throttled to the same 1 s
    // cadence as the handle re-verify; a configured-name change bypasses it.
    private long _lastMissMs = long.MinValue;

    public WowWindow() : this(() => Environment.TickCount64, FindMainWindow) { }

    /// <summary>Test seam: injected clock + process-name → main-window lookup.</summary>
    internal WowWindow(Func<long> clock, Func<string, IntPtr> findMainWindow)
    {
        _clock = clock;
        _findMainWindow = findMainWindow;
    }

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
        _lastMissMs = long.MinValue;
    }

    /// <summary>
    /// Finds the game window by process name. The name is matched
    /// case-insensitively and without the .exe suffix, so "Wow", "WowClassic"
    /// and "Legion" all work as written in settings. The cached handle is
    /// trusted only while it is STILL the process's live main window: after a
    /// relog / character switch / display-mode change the game can recreate
    /// (or replace) its window, leaving our old handle alive but wrong. That
    /// stale-but-valid handle was the "stops detecting the strip until
    /// restart" bug — the periodic identity check re-resolves within a second.
    /// </summary>
    public bool Refresh(string processName)
    {
        var wanted = Normalise(processName);
        var nameChanged = !string.Equals(processName, _processName, StringComparison.OrdinalIgnoreCase);

        if (_handle != IntPtr.Zero
            && IsValid
            && !nameChanged)
        {
            var now = _clock();
            if (now - _lastVerifyMs < VerifyIntervalMs) return true;
            _lastVerifyMs = now;
            if (StillCurrentMainWindow(wanted)) return true;
            // Fall through: the cached handle is no longer the game's main
            // window (recreated / re-owned). Re-resolve below.
        }

        _handle = IntPtr.Zero;
        _processName = processName;
        if (nameChanged) _lastMissMs = long.MinValue;

        var nowMs = _clock();
        if (_lastMissMs != long.MinValue && nowMs - _lastMissMs < VerifyIntervalMs) return false;
        _lastMissMs = nowMs;

        var found = _findMainWindow(wanted);
        if (found == IntPtr.Zero) return false;

        _handle = found;
        _lastVerifyMs = nowMs;
        _lastMissMs = long.MinValue;
        return true;
    }

    private static string Normalise(string processName) =>
        processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;

    // ----- 4.4.0 "Wayfinder": process presence + restart helpers -----

    /// <summary>
    /// True while any process with this name exists (exact
    /// <see cref="Process.GetProcessesByName(string)"/> match after the ".exe"
    /// suffix is normalised away). Never throws and disposes every Process it
    /// enumerates. Used only to drive the transport labels (Open Game /
    /// Game Running); it does not attach to a window.
    /// </summary>
    internal static bool IsProcessRunning(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        var wanted = Normalise(processName.Trim());
        if (wanted.Length == 0) return false;

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(wanted);
        }
        catch (Exception)
        {
            // Invalid name / enumeration refused: treat as "not running".
            return false;
        }

        var found = false;
        foreach (var process in processes)
        {
            using (process) found = true;
        }
        return found;
    }

    /// <summary>
    /// 4.4.0 "Wayfinder": kills the running game (Restart Game). Defensive by
    /// construction — a wrong settings.ini name must never take down the
    /// companion or another user's session:
    ///  * empty/whitespace is refused;
    ///  * the companion's own PID / process name is refused;
    ///  * only processes in OUR session are considered (SessionId is read in a
    ///    try/catch; when it cannot be proven equal the target is skipped);
    ///  * <c>Kill(entireProcessTree: true)</c>, expected races caught, bounded
    ///    wait, and the remaining count is reported.
    /// </summary>
    internal static (int Killed, bool Ok, string Message) KillGameProcesses(string processName, int waitMs)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return (0, false, "refusing to kill: ProcessName is empty");

        var wanted = Normalise(processName.Trim());
        if (wanted.Length == 0)
            return (0, false, "refusing to kill: ProcessName is empty");
        if (waitMs < 0) waitMs = 0;

        int ownPid;
        string ownName;
        int? ownSession;
        using (var self = Process.GetCurrentProcess())
        {
            ownPid = self.Id;
            ownName = self.ProcessName;
            ownSession = TrySessionId(self);
        }

        if (string.Equals(wanted, ownName, StringComparison.OrdinalIgnoreCase))
            return (0, false, $"refusing to kill: '{wanted}' is the companion itself");

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(wanted);
        }
        catch (Exception ex)
        {
            return (0, false, $"could not enumerate '{wanted}': {ex.Message}");
        }

        var targets = new List<Process>();
        var skipped = 0;
        foreach (var process in processes)
        {
            var keep = false;
            try
            {
                // Never our own process, even if the name matched.
                if (process.Id == ownPid) continue;
                // Same session only: another user's client is not ours to kill.
                var targetSession = TrySessionId(process);
                if (ownSession is null || targetSession is null
                    || targetSession.Value != ownSession.Value)
                {
                    skipped++;
                    continue;
                }
                targets.Add(process);
                keep = true;
            }
            catch (Exception)
            {
                skipped++;
            }
            finally
            {
                if (!keep) process.Dispose();
            }
        }

        if (targets.Count == 0 && processes.Length == 0)
            return (0, false, $"no running process named '{wanted}'");

        var killed = 0;
        string? lastError = null;
        foreach (var target in targets)
        {
            try
            {
                target.Kill(entireProcessTree: true);
                killed++;
            }
            catch (Exception ex)
            {
                // Access denied / already exited: expected races, keep going.
                lastError = ex.Message;
            }
        }

        // Bounded wait for the tree to actually disappear, then count what is
        // still alive (checked BEFORE the Process handle is disposed).
        var deadline = Environment.TickCount64 + waitMs;
        var remaining = 0;
        foreach (var target in targets)
        {
            try
            {
                while (!target.HasExited && Environment.TickCount64 < deadline)
                    Thread.Sleep(50);
                if (!target.HasExited) remaining++;
            }
            catch (Exception)
            {
                // HasExited can throw for a process we no longer own.
            }
            finally
            {
                target.Dispose();
            }
        }

        if (killed == 0)
        {
            // Nothing was killed. The common benign race is the user closing
            // the game between the presence poll and this click: if the name is
            // gone now, Restart may still relaunch it instead of aborting with
            // a misleading "could not close" dialog.
            if (remaining == 0 && !IsProcessRunning(wanted))
                return (0, true, $"'{wanted}' was already closed");
            return (0, false, Describe());
        }
        if (remaining == 0) return (killed, true, $"closed '{wanted}'");
        // A killed tree that still reports a live process is a partial close:
        // report it so the caller does not claim a clean restart.
        if (!IsProcessRunning(wanted)) return (killed, true, $"closed '{wanted}'");
        return (killed, false, Describe());

        string Describe()
        {
            if (remaining > 0) return $"'{wanted}' is still running after {waitMs} ms - close it manually";
            if (skipped > 0) return $"no killable '{wanted}' process in this session ({skipped} skipped)";
            if (lastError is { Length: > 0 }) return $"could not close '{wanted}': {lastError}";
            return $"no running process named '{wanted}'";
        }
    }

    /// <summary>SessionId without throwing (access denied / exited processes).</summary>
    private static int? TrySessionId(Process process)
    {
        try
        {
            return process.SessionId;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Default lookup: enumerate processes for the wanted name's main window.</summary>
    private static IntPtr FindMainWindow(string wanted)
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (!string.Equals(process.ProcessName, wanted, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (process.MainWindowHandle == IntPtr.Zero)
                    continue;
                return process.MainWindowHandle;
            }
        }
        return IntPtr.Zero;
    }

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
