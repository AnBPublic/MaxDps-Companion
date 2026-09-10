using System.Diagnostics;

namespace MaxDpsCompanion;

internal readonly record struct EngineStatus(
    string Message,
    bool BridgeVisible,
    BridgeState State,
    string SlotSummary,
    string LastKeySent,
    string RawSample);

/// <summary>
/// Reads the addon's pixel block on a background thread and replays the encoded
/// input. One press at a time, never faster than MinKeyIntervalMs. Keyboard
/// input is posted to the attached game window only (never the focused
/// window), so rotation keeps working while both windows sit in the
/// background; mouse input has no per-window route and keeps the foreground
/// gate. The game must stay visible (not minimized) or sampling goes blind.
/// </summary>
internal sealed class RotationEngine : IDisposable
{
    // Interrupts and defensives outrank the main rotation; consumables last.
    private static readonly Slot[] Priority =
        [Slot.Interrupt, Slot.Defensive, Slot.Cooldown, Slot.Main, Slot.Consumable];

    /// <summary>How long to keep missing the block before sweeping the screen for it again.</summary>
    private const long RelocateIntervalMs = 2000;

    private readonly AppSettings _settings;
    private readonly WowWindow _window = new();
    private readonly ScreenSampler _sampler = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly long[] _lastSlotPress = new long[PixelProtocol.SlotCount];

    private Thread? _thread;
    private volatile bool _running;
    private long _lastAnyPress;
    private long _lastLocateAttempt = long.MinValue;
    private long _lastActiveMs = long.MinValue;
    // Last companion->addon proof of life. Sent as the SILENT '/mdb alive'
    // chat command (no output, Escape-closed): no focus steal beyond the
    // unavoidable SetForegroundWindow round-trip, no keystroke as itself.
    // Without this the bridge would reset the strip mid-session 25 s after
    // the last /mdb command.
    private long _lastAliveMs = long.MinValue;
    private const long AliveIntervalMs = 10000;
    private int _rotation;
    private string _lastKeySent = "-";
    private string? _holdNote;
    private bool _targetBlocked;
    private bool _interactBlocked;

    public RotationEngine(AppSettings settings) => _settings = settings;

    /// <summary>Suspends input sending without tearing down the reader, for the pause hotkey.</summary>
    public volatile bool Paused;

    public event Action<EngineStatus>? StatusChanged;

    /// <summary>Raised when the sweep finds the block somewhere other than the configured offset.</summary>
    public event Action<BlockLocation>? LocationChanged;

    public bool IsRunning => _running;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _lastLocateAttempt = long.MinValue;
        _lastAliveMs = long.MinValue;
        _thread = new Thread(Loop) { IsBackground = true, Name = "MaxDpsCompanion.Engine" };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(1000);
        _thread = null;
    }

    /// <summary>
    /// One-shot sweep for the block, usable while the engine is stopped. Uses its
    /// own window handle so it never races the engine thread.
    /// </summary>
    public static BlockLocation? Locate(AppSettings settings, out string message) =>
        Locate(settings, settings.Color, out message);

    /// <summary>Profile-aware sweep used by recalibration and the Learn flow.</summary>
    public static BlockLocation? Locate(AppSettings settings, ColorProfile? profile, out string message)
    {
        var window = new WowWindow();

        if (!window.Refresh(settings.ProcessName))
        {
            message = $"no process named '{settings.ProcessName}'";
            return null;
        }

        if (!window.TryGetClientOrigin(out var origin, out var size))
        {
            message = "game window has no client area";
            return null;
        }

        var found = BlockLocator.Locate(origin, size, profile);
        message = found is { } location
            ? $"found at {location.OffsetX},{location.OffsetY} ({location.CellSize} px cells)"
            : $"not found in the {size.Width}x{size.Height} client area at {origin.X},{origin.Y}";
        return found;
    }

    private void Loop()
    {
        // Sleep-first pacing: a steady 50ms cadence without drift pile-up.
        // Tick work is ~1ms (8x1 BitBlt + decode), so the loop costs ~2%
        // of one core and never spins.
        var next = Environment.TickCount64;
        while (_running)
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                Report($"error: {ex.Message}", false, BridgeState.Idle, "-", "-");
            }

            next += Math.Max(10, _settings.PollIntervalMs);
            var wait = (int)(next - Environment.TickCount64);
            if (wait > 0) Thread.Sleep(wait);
            else next = Environment.TickCount64;
        }
    }

    private void Tick()
    {
        if (!_window.Refresh(_settings.ProcessName))
        {
            Report($"waiting for '{_settings.ProcessName}'", false, BridgeState.Idle, "-", "-");
            return;
        }

        if (!_window.TryGetClientOrigin(out var origin, out var clientSize))
        {
            Report("game window has no client area", false, BridgeState.Idle, "-", "-");
            return;
        }

        var block = new Point(origin.X + _settings.OffsetX, origin.Y + _settings.OffsetY);
        var cells = _sampler.Sample(block, _settings.CellSize);
        var frame = PixelProtocol.Decode(cells, _settings.Color);

        if (frame is null)
        {
            // A visible calibrate pattern is not a failure: say so plainly so
            // a stuck `/mdb calibrate on` reads as what it is instead of a
            // capture problem. Check the pattern first — it needs no profile.
            if (ColorLearner.Classify(cells, _settings.Color) >= 0)
            {
                Report("calibrate pattern visible (/mdb calibrate off to resume)", true, BridgeState.Paused, "-", Describe(cells));
                return;
            }
            var message = Relocate(origin, clientSize)
                ? "block found, re-aligned"
                : "no pixel block - client must be windowed or borderless";
            Report(message, false, BridgeState.Idle, "-", Describe(cells));
            return;
        }

        var summary = Summarise(frame);

        if (frame.State == BridgeState.Active)
        {
            _lastActiveMs = _clock.ElapsedMilliseconds;
        }

        if (frame.State == BridgeState.Paused)
        {
            Report("addon bridge paused (/mdb on)", true, frame.State, summary, "-");
            return;
        }

        if (Paused)
        {
            Report("paused", true, frame.State, summary, "-");
            return;
        }

        // Process lock: notify but never send while the attached window is
        // gone or has been re-owned by another process since Refresh.
        if (!_window.IsValid)
        {
            Report("game window lost", true, frame.State, summary, "-");
            return;
        }
        var gameHandle = _window.Handle;
        var gamePid = _window.ProcessId;
        // Re-resolve the PID at send time: the handle could have been
        // recycled between Refresh and now.
        Native.GetWindowThreadProcessId(gameHandle, out gamePid);

        // Proof of life for the bridge auto-cal watchdog, every 10 s, via
        // the silent chat path (Escape-closed, no output). The round-trip
        // briefly focuses the game; that is the same cost as calibration
        // and is unavoidable — WoW Lua cannot see window messages.
        var tickNow = _clock.ElapsedMilliseconds;
        if (tickNow - _lastAliveMs >= AliveIntervalMs)
        {
            _lastAliveMs = tickNow;
            try { ChatCommander.SendChatCommandSilent(_window, "mdb alive", settleMs: 100); } catch { }
        }

        if (frame.State != BridgeState.Active)
        {
            // Auto-target fallback: the addon asks for a target explicitly
            // (state 3 = T mode ON with nothing usable) and the kill-switch
            // is on — press the user's own TargetKey (default Tab, a
            // hardware-equivalent keypress). Idle (mode OFF or target
            // present) never fires. Never fires while paused or unfocused.
            // A movement TargetKey is refused, never sent.
            // Auto-interact fallback mirrors it: state 4 asks for the
            // InteractKey (default F).
            _holdNote = null;
            AuditBindings(frame);
            if (MovementGuard.ShouldAutoTarget(
                    frame.State, _settings.AutoTargetEnabled, _settings.CombatOnly,
                    _clock.ElapsedMilliseconds, _lastActiveMs))
            {
                _targetBlocked = false;
                if (TrySendTargetKey(gameHandle, gamePid)) Report("targeting", true, frame.State, summary, _lastKeySent);
                else if (_targetBlocked)
                {
                    // Movement target key: surface both facts in one status line.
                    var note = _holdNote is { } blocked
                        ? $"target key is a movement key; {blocked}"
                        : "target key is a movement key";
                    Report(note, true, frame.State, summary, "-");
                }
                else if (_holdNote is { } note)
                {
                    // Binding audit surfaced a spell on a movement key: say so
                    // and hold, instead of a generic "no suggestion".
                    Report(note, true, frame.State, summary, "-");
                }
                else
                {
                    Report("no suggestion", true, frame.State, summary, "-");
                }
            }
            else if (MovementGuard.ShouldAutoInteract(
                    frame.State, _settings.InteractEnabled, _settings.CombatOnly,
                    _clock.ElapsedMilliseconds, _lastActiveMs))
            {
                _interactBlocked = false;
                if (TrySendInteractKey(gameHandle, gamePid)) Report("interacting", true, frame.State, summary, _lastKeySent);
                else if (_interactBlocked)
                {
                    var note = _holdNote is { } blocked
                        ? $"interact key is a movement key; {blocked}"
                        : "interact key is a movement key";
                    Report(note, true, frame.State, summary, "-");
                }
                else if (_holdNote is { } interactNote)
                {
                    Report(interactNote, true, frame.State, summary, "-");
                }
                else
                {
                    Report("no suggestion", true, frame.State, summary, "-");
                }
            }
            else if (_holdNote is { } note)
            {
                // Stale binding-audit note re-checked above: say so and
                // hold, instead of a generic "no suggestion".
                Report(note, true, frame.State, summary, "-");
            }
            else
            {
                Report("no suggestion", true, frame.State, summary, "-");
            }
            return;
        }

        _holdNote = null;
        var sent = TrySendOne(frame, gameHandle, gamePid);
        if (!sent && _holdNote is null) AuditBindings(frame);
        Report(sent ? "sending" : (_holdNote ?? "holding"), true, frame.State, summary, "-");
    }

    /// <summary>Sweeps the client area for the block, at most once every RelocateIntervalMs.</summary>
    private bool Relocate(Point origin, Size clientSize)
    {
        var now = _clock.ElapsedMilliseconds;
        if (now - _lastLocateAttempt < RelocateIntervalMs) return false;
        _lastLocateAttempt = now;

        if (BlockLocator.Locate(origin, clientSize, _settings.Color) is not { } location) return false;

        _settings.OffsetX = location.OffsetX;
        _settings.OffsetY = location.OffsetY;
        _settings.CellSize = location.CellSize;
        LocationChanged?.Invoke(location);
        return true;
    }

    private bool TrySendOne(BridgeFrame frame, IntPtr gameHandle, uint gamePid)
    {
        var now = _clock.ElapsedMilliseconds;
        var gap = Math.Max(1, _settings.MinKeyIntervalMs);

        if (now - _lastAnyPress < gap) return false;

        KeyStroke? held = null;
        for (var offset = 0; offset < Priority.Length; offset++)
        {
            var slot = Priority[(_rotation + offset) % Priority.Length];
            var index = (int)slot;

            if (!_settings.SlotEnabled[index]) continue;
            if (frame[slot] is not { } stroke) continue;
            if (now - _lastSlotPress[index] < gap) continue;

            // Binding audit: a spell on WASD/Space/arrows is never sent —
            // replaying it would drive movement every tick.
            if (MovementGuard.IsMovementStroke(stroke))
            {
                _holdNote = $"slot {SlotName(slot)} is bound to {stroke.Describe()}";
                continue;
            }

            // Collision skip: a synthetic KEYUP for a physically-held key
            // reads as a real release in WoW. Exact-VK match only, so kiting
            // while DPSing with non-held spell keys keeps working.
            if (MovementGuard.IsPhysicallyDown(stroke.VirtualKey))
            {
                held = stroke;
                continue;
            }

            if (KeySender.IsMouse(stroke.VirtualKey))
            {
                // No per-window message equivalent exists: mouse input goes to
                // the focused window, so it always needs the foreground gate.
                if (!_window.IsForeground || !_window.IsGameWindow(gameHandle))
                {
                    _holdNote = $"slot {SlotName(slot)} needs focus (mouse input)";
                    continue;
                }
                KeySender.Send(stroke, _settings.KeyPressMs);
            }
            else
            {
                // Keyboard slots: background-safe when allowed; otherwise the
                // foreground gate applies to keys too.
                if (!_settings.AllowBackgroundKeys && !_window.IsForeground)
                {
                    _holdNote = "game not focused";
                    return false;
                }
                if (!KeySender.SendToWindow(gameHandle, gamePid, stroke, _settings.KeyPressMs))
                {
                    _holdNote = "game window lost";
                    return false;
                }
            }

            _lastSlotPress[index] = now;
            _lastAnyPress = now;
            _rotation = (_rotation + offset + 1) % Priority.Length;
            _lastKeySent = $"{SlotName(slot)}: {stroke.Describe()}";
            return true;
        }

        if (held is { } heldStroke)
            _holdNote = $"holding {heldStroke.Describe()} (you are holding it)";

        return false;
    }

    /// <summary>Sends the configured TargetKey, never a movement key.</summary>
    private bool TrySendTargetKey(IntPtr gameHandle, uint gamePid)
    {
        var now = _clock.ElapsedMilliseconds;
        var gap = Math.Max(1, _settings.MinKeyIntervalMs);
        if (now - _lastAnyPress < gap) return false;
        if (!MovementGuard.TryParseTargetKey(_settings.TargetKey, out var stroke)) return false;

        // A TargetKey of W/A/S/D/Space/arrows would inject movement on every
        // need-target frame. Hold and let the status line say why.
        _targetBlocked = MovementGuard.IsMovementStroke(stroke);
        if (_targetBlocked) return false;
        if (MovementGuard.IsPhysicallyDown(stroke.VirtualKey))
        {
            _holdNote = $"holding {stroke.Describe()} (you are holding it)";
            return false;
        }

        if (KeySender.IsMouse(stroke.VirtualKey))
        {
            if (!_window.IsForeground || !_window.IsGameWindow(gameHandle)) return false;
            KeySender.Send(stroke, _settings.KeyPressMs);
        }
        else
        {
            if (!_settings.AllowBackgroundKeys && !_window.IsForeground) return false;
            if (!KeySender.SendToWindow(gameHandle, gamePid, stroke, _settings.KeyPressMs))
                return false;
        }
        _lastAnyPress = now;
        _lastKeySent = $"Target: {stroke.Describe()}";
        return true;
    }

    /// <summary>Sends the configured InteractKey, never a movement key.</summary>
    private bool TrySendInteractKey(IntPtr gameHandle, uint gamePid)
    {
        var now = _clock.ElapsedMilliseconds;
        var gap = Math.Max(1, _settings.MinKeyIntervalMs);
        if (now - _lastAnyPress < gap) return false;
        if (!MovementGuard.TryParseInteractKey(_settings.InteractKey, out var stroke)) return false;

        // An InteractKey of W/A/S/D/Space/arrows would inject movement on
        // every need-interact frame. Hold and let the status line say why.
        _interactBlocked = MovementGuard.IsMovementStroke(stroke);
        if (_interactBlocked) return false;
        if (MovementGuard.IsPhysicallyDown(stroke.VirtualKey))
        {
            _holdNote = $"holding {stroke.Describe()} (you are holding it)";
            return false;
        }

        if (KeySender.IsMouse(stroke.VirtualKey))
        {
            if (!_window.IsForeground || !_window.IsGameWindow(gameHandle)) return false;
            KeySender.Send(stroke, _settings.KeyPressMs);
        }
        else
        {
            if (!_settings.AllowBackgroundKeys && !_window.IsForeground) return false;
            if (!KeySender.SendToWindow(gameHandle, gamePid, stroke, _settings.KeyPressMs))
                return false;
        }
        _lastAnyPress = now;
        _lastKeySent = $"Interact: {stroke.Describe()}";
        return true;
    }

    private void AuditBindings(BridgeFrame frame)
    {
        for (var i = 0; i < PixelProtocol.SlotCount; i++)
        {
            if (frame.Slots[i] is not { } stroke) continue;
            if (!MovementGuard.IsMovementStroke(stroke)) continue;
            _holdNote =
                $"slot {SlotName((Slot)i)} is bound to {stroke.Describe()} - move it off WASD/Space/arrows";
            return;
        }
    }

    private static string Describe(Color[] cells) =>
        string.Join(" ", cells.Select(c => $"{c.R:X2}{c.G:X2}{c.B:X2}"));

    private static string Summarise(BridgeFrame frame)
    {
        var parts = new List<string>(PixelProtocol.SlotCount);
        for (var i = 0; i < PixelProtocol.SlotCount; i++)
        {
            var slot = (Slot)i;
            parts.Add($"{SlotName(slot)}={(frame[slot] is { } stroke ? stroke.Describe() : "-")}");
        }
        return string.Join("  ", parts);
    }

    private static string SlotName(Slot slot) => slot switch
    {
        Slot.Main => "Main",
        Slot.Cooldown => "CD",
        Slot.Interrupt => "Int",
        Slot.Defensive => "Def",
        Slot.Consumable => "Cons",
        _ => slot.ToString(),
    };

    private void Report(string message, bool visible, BridgeState state, string summary, string raw) =>
        StatusChanged?.Invoke(new EngineStatus(message, visible, state, summary, _lastKeySent, raw));

    public void Dispose()
    {
        Stop();
        _sampler.Dispose();
    }
}
