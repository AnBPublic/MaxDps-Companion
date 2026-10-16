// Mac engine host: owns the Core RotationEngine (self-pacing tick loop on a
// background thread) with the Mac adapters, plus the lifecycle of the native
// ScreenCaptureKit stream.
//
// CAPTURE LIFECYCLE (performance contract)
//   * The stream exists only while the engine runs: Stop() tears it down, so
//     an idle companion costs nothing (no capture session, no timers).
//   * A supervisor thread (1 s cadence, sleeping - not a poll loop) keeps the
//     stream pointed at the live game window: it starts it on Start, restarts
//     it after a stream error or a game restart, and keeps the key sender's
//     target pid current.
//   * The engine tick itself never touches the capture APIs: it reads the
//     newest frame (seqlock) or falls back to the legacy one-shot when the
//     native backend is unavailable.
//
// StatusChanged surfaces EngineStatus for the UI; CaptureStatus adds the
// backend line (probe/UI only).

namespace MaxDpsCompanion.Mac;

internal sealed class MacEngineHost : IDisposable
{
    private readonly AppSettings _settings;
    private readonly RotationEngine _engine;
    private readonly MacPermissions _permissions = new();
    private readonly MacGameWindow _window = new();
    private readonly MacKeySender _keys = new();

    private MacCaptureStream? _stream;
    private MacScreenSampler? _sampler;
    private MacBlockScanner? _scanner;
    private Thread? _supervisor;
    private volatile bool _running;

    public event Action<EngineStatus>? StatusChanged;

    public MacEngineHost(AppSettings settings, ColorProfile? colorProfile = null)
    {
        _settings = settings;
        _sampler = new MacScreenSampler(null, settings.Color);
        _scanner = new MacBlockScanner(null, colorProfile ?? settings.Color);
        _engine = new RotationEngine(
            settings,
            window: _window,
            sampler: _sampler,
            keys: _keys,
            physical: new MacPhysicalKeys(),
            scanner: _scanner,
            clock: new MacClock());
        _engine.StatusChanged += s => StatusChanged?.Invoke(s);
    }

    public RotationEngine Engine => _engine;
    public bool IsRunning => _engine.IsRunning;

    /// <summary>Capture backend description for the UI/probe (never in the tick path).</summary>
    public string CaptureStatus
    {
        get
        {
            if (!MdcNative.AvailableSafe) return "legacy CGWindowList (native SCK library missing)";
            if (MdcNative.Authorized() != 1) return "ScreenCaptureKit (Screen Recording NOT granted)";
            var stream = _stream;
            if (stream is not { Running: true }) return "ScreenCaptureKit (stream idle)";
            var (rx, ry, rw, rh) = stream.Region;
            var (pw, ph) = stream.RegionPixels;
            var stats = stream.Stats();
            return $"ScreenCaptureKit region={rw:F0}x{rh:F0}pt@{rx:F0},{ry:F0} px={pw}x{ph} fps={stats.Fps} frames={stats.Delivered}";
        }
    }

    /// <summary>Checks TCC gates; prompts for Screen Recording once when needed.
    /// Accessibility is required for sends - warn, don't block Start.</summary>
    public bool EnsurePermissions(out string message)
    {
        if (!_permissions.CheckCapturePermission())
        {
            MacPermissions.RequestCapture();
            message = "Screen Recording required: grant to MaxDPS Companion in System Settings > Privacy & Security, then Start again.";
            return false;
        }
        if (!_permissions.CheckInputPermission())
        {
            message = "running without Accessibility: rotation holds on sends until granted (System Settings > Privacy & Security > Accessibility).";
            return true;
        }
        message = "permissions OK";
        return true;
    }

    public void Start()
    {
        _running = true;
        EnsureStream();
        StartSupervisor();
        _engine.Start();
    }

    public void Stop()
    {
        _running = false;
        try { _engine.Stop(); } catch { /* stop is best effort */ }
        _stream?.Stop();
        if (_sampler is not null) _sampler.Stream = null;
        if (_scanner is not null) _scanner.Stream = null;
    }

    /// <summary>
    /// Starts (or restarts) the native stream against the current game window
    /// and hands it to the (stable) adapters. Called on Start and by the
    /// supervisor; cheap and idempotent while the stream is healthy.
    /// </summary>
    public bool EnsureStream()
    {
        if (!_running) return false;
        if (!MdcNative.AvailableSafe) return false;
        if (!_window.Refresh(_settings.ProcessName))
        {
            _stream?.Stop();
            _stream = null;
            return false;
        }
        var pid = (int)_window.ProcessId;
        _keys.TargetPid = pid;

        var stream = _stream;
        var needsRestart = stream is null
            || !stream.Running
            || stream.StreamDead
            || !stream.GeometryMatchesWindow();

        if (needsRestart)
        {
            stream?.Stop();
            var fresh = new MacCaptureStream(pid, fps: 60);
            if (!fresh.Start())
            {
                fresh.Dispose();
                _stream = null;
                if (_sampler is not null) _sampler.Stream = null;
                if (_scanner is not null) _scanner.Stream = null;
                return false;
            }
            _stream = fresh;
            if (_sampler is not null) _sampler.Stream = fresh;
            if (_scanner is not null) _scanner.Stream = fresh;
        }
        return true;
    }

    private void StartSupervisor()
    {
        if (_supervisor is { IsAlive: true }) return;
        _supervisor = new Thread(() =>
        {
            while (_running)
            {
                try
                {
                    EnsureStream();
                }
                catch { /* never let the supervisor kill the app */ }
                Thread.Sleep(1000);
            }
        })
        {
            IsBackground = true,
            Name = "mac-capture-supervisor",
            Priority = ThreadPriority.BelowNormal,
        };
        _supervisor.Start();
    }

    public void Dispose()
    {
        Stop();
        _stream?.Dispose();
        _stream = null;
        _engine.Dispose();
    }
}
