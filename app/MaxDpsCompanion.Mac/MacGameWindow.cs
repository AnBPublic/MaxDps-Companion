using System.Drawing;

namespace MaxDpsCompanion.Mac;

// IGameWindow over CGWindowList metadata (window identity + bounds are a
// cheap metadata query, no capture permission needed) plus the frontmost-app
// check from the native layer.
//
// CLIENT ORIGIN: on macOS the WoW window is borderless — bounds == client
// area — so the strip's global origin is `window origin + strip offset`.
// Everything the capture layer does is WINDOW-RELATIVE, so the same offsets
// apply on any display at any scale (screen-agnostic by construction).
//
// FOREGROUND: the engine gates mouse strokes (and keyboard strokes when
// background keys are disabled) on this. The old port returned false
// unconditionally, which silently disabled EVERY send — the "won't execute
// actions" failure mode. It now asks the window server which app is
// frontmost.
internal sealed class MacGameWindow : IGameWindow
{
    private int? _pid;
    private string _processName = "";
    private long _lastVerifyMs;
    private long _lastFrontmostCheckMs;
    private bool _frontmost;
    private const long VerifyIntervalMs = 1000;
    private const long FrontmostIntervalMs = 250;

    public IntPtr Handle => _pid is { } pid ? new IntPtr(pid) : IntPtr.Zero;
    public bool IsValid => _pid is not null;

    public bool IsForeground
    {
        get
        {
            if (_pid is not { } pid) return false;
            // Cached for 250 ms: the engine asks on every gated send and the
            // window-server query is not free.
            var now = Environment.TickCount64;
            if (now - _lastFrontmostCheckMs < FrontmostIntervalMs) return _frontmost;
            _lastFrontmostCheckMs = now;
            _frontmost = MacCaptureStream.GameIsFrontmost(pid);
            return _frontmost;
        }
    }

    public uint ProcessId => _pid is { } pid ? (uint)pid : 0;

    public bool IsGameWindow(IntPtr candidate) => IsValid && candidate == Handle;

    public void Reset()
    {
        _pid = null;
        _lastVerifyMs = 0;
        _lastFrontmostCheckMs = 0;
        _frontmost = false;
    }

    public bool Refresh(string processName)
    {
        if (_pid is not null
            && string.Equals(processName, _processName, StringComparison.OrdinalIgnoreCase))
        {
            var now = Environment.TickCount64;
            if (now - _lastVerifyMs < VerifyIntervalMs) return true;
            _lastVerifyMs = now;
            if (NativeMac.WowPidRaw() == _pid) return true;
        }

        _pid = null;
        _processName = processName;
        var found = NativeMac.WowPidRaw();
        if (found is null) return false;
        _pid = found;
        _lastVerifyMs = Environment.TickCount64;
        return true;
    }

    public bool TryGetClientOrigin(out Point origin, out Size clientSize)
    {
        origin = Point.Empty;
        clientSize = Size.Empty;
        if (!IsValid) return false;
        if (NativeMac.WowBoundsRaw() is not { } b) return false;
        origin = new Point((int)Math.Round(b.X), (int)Math.Round(b.Y));
        clientSize = new Size((int)Math.Round(b.W), (int)Math.Round(b.H));
        return clientSize.Width > 0 && clientSize.Height > 0;
    }
}
