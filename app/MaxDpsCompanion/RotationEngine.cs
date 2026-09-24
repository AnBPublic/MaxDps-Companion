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
    // Send order: the MAIN rotation is the core functionality and goes
    // FIRST — a live main suggestion must never wait behind a situational
    // cooldown while the GCD sits idle (Sep-2026: main starved behind
    // offensive/defensive because interrupt/defensive outranked it and the
    // main cell was usually EMPTY — see Reader.GetMainSpellID note).
    // Interrupt still preempts via the round-robin rotation once live.
    private static readonly Slot[] Priority =
        [Slot.Main, Slot.Offensive, Slot.Interrupt, Slot.Defensive, Slot.Consumable, Slot.Trinket];

    /// <summary>How long to keep missing the block before sweeping the screen for it again.</summary>
    private const long RelocateIntervalMs = 2000;

    private readonly AppSettings _settings;
    private readonly WowWindow _window = new();
    private readonly ScreenSampler _sampler = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    // v1.3.3 perf model (Sep-2026 live test: stuck on R, stalled on SE):
    // ONE global press clock, not six per-slot clocks. A Paladin rotation
    // cycles 4-6 distinct spells inside one GCD window; per-slot gaps let
    // the engine machine-gun the SAME ready slot every poll (R,R,R…)
    // while the next suggestion (SE) waits behind MinKeyIntervalMs on a
    // slot that never becomes "due". Single global gap = strict WoW-GCD
    // pacing: one press per interval, always the CURRENT suggestion.
    // _lastAnyPress doubles as the GCD clock; per-slot stamps are kept
    // for diagnostics only (LastKey/readout), never gating.
    private readonly long[] _lastSlotPress = new long[PixelProtocol.SlotCount];

    private Thread? _thread;
    private volatile bool _running;
    private long _lastAnyPress;
    private long _lastLocateAttempt = long.MinValue;
    private long _lastActiveMs = long.MinValue;
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
        // v2 width first; a stale v1 addon renders 8 cells, so the 9th
        // captured cell is background and v2 Decode rejects by checksum.
        var cells = _sampler.Sample(block, _settings.CellSize);
        var frame = PixelProtocol.Decode(cells, _settings.Color);
        if (frame is null)
        {
            var old = _sampler.SampleV1(block, _settings.CellSize);
            frame = PixelProtocol.Decode(old, _settings.Color);
            if (frame is not null)
            {
                Report("addon is v1 - run install-addon.ps1 + /reload", true, frame.State, Summarise(frame), Describe(old));
                cells = old;
            }
        }

        if (frame is null)
        {
            // A visible calibrate pattern is not a failure: say so plainly so
            // a stuck `/mdb calibrate on` reads as what it is instead of a
            // capture problem. Check the pattern first — it needs no profile.
            // Probe both widths: a stale v1 pattern classifies only in the
            // 8-cell window (see CalibrateWorker.SampleBoth).
            if (ColorLearner.Classify(cells, _settings.Color) >= 0
                || ColorLearner.Classify(_sampler.SampleV1(block, _settings.CellSize), _settings.Color) >= 0)
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

        // v1.3.5/1.3.6 COMBAT GATE: "Out of combat" toggle OFF (CombatOnly)
        // ⇒ HARD PAUSE until the game reports combat. Nothing fires — no
        // rotation, no auto-target, no auto-interact. The toggle ON opts
        // out of this gate (out-of-combat attack allowed, subject to the
        // target gate below).
        if (_settings.CombatOnly && !frame.InCombat)
        {
            Report("holding (out of combat)", true, frame.State, summary, "-");
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

        // v1.3.6 TARGET GATE (user: "attack out of combat is ok, it just
        // shouldn't spam into empty space — wait until I target
        // something"). With out-of-combat allowed, still require a valid
        // attackable target: no target ⇒ hold. Auto-target (if enabled)
        // still asks for one; otherwise say we're waiting.
        if (!frame.HasTarget)
        {
            if (MovementGuard.ShouldAutoTarget(
                    BridgeState.NeedTarget, _settings.AutoTargetEnabled, _settings.CombatOnly,
                    _clock.ElapsedMilliseconds, _lastActiveMs))
            {
                _targetBlocked = false;
                if (TrySendTargetKey(gameHandle, gamePid)) Report("targeting", true, frame.State, summary, _lastKeySent);
                else Report(_holdNote ?? "waiting for target", true, frame.State, summary, "-");
            }
            else
            {
                Report("waiting for target", true, frame.State, summary, "-");
            }
            return;
        }

        if (frame.State != BridgeState.Active)
        {
            // v1.3.4 MELEE-STATE FIX: the bridge used to stamp NeedInteract
            // (4) for every in-melee frame, which suppressed the rotation
            // entirely (melee classes = never Active = never cast; only the
            // auto-interact key fired). The bridge now keeps a live
            // suggestion Active, but the client is belt-and-braces: if the
            // frame carries ANY sendable slot, the ROTATION wins here too,
            // and auto-target/interact only run when the frame is EMPTY.
            // A pending main suggestion must never be held behind an
            // interact press (Sep-2026: R recommended, F spammed).
            if (AnyEnabledSlot(frame))
            {
                _holdNote = null;
                var rotSent = TrySendOne(frame, gameHandle, gamePid);
                if (!rotSent && _holdNote is null) AuditBindings(frame);
                Report(rotSent ? "sending" : (_holdNote ?? "holding"), true, frame.State, summary, "-");
                return;
            }
            // Auto-target fallback: the addon asks for a target explicitly
            // (state 3 = T mode ON with nothing usable) and the kill-switch
            // is on — press the user's own TargetKey (default Tab, a
            // hardware-equivalent keypress). Idle (mode OFF or target
            // present) never fires. Never fires while paused or unfocused.
            // A movement TargetKey is refused, never sent.
            // Auto-interact fallback mirrors it: state 4 asks for the
            // InteractKey (MB5 / Alt+MB5).
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
                if (TrySendInteractKey(gameHandle, gamePid, frame)) Report("interacting", true, frame.State, summary, _lastKeySent);
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

        // MaxDps shows NO spell by design when it has nothing to recommend
        // (idle out of combat, resource pooling, proc waits — upstream
        // Flags stay empty and the overlay stays dark). An Active frame
        // with zero encodable slots is therefore CORRECT: hold, never
        // synthesize a press, and say so plainly. "holding (no suggestion
        // from MaxDps)" distinguishes upstream-idle from our own gates
        // (movement-key hold, focus hold, key-gap hold in TrySendOne).
        _holdNote = null;
        var sent = TrySendOne(frame, gameHandle, gamePid);
        if (!sent && _holdNote is null) AuditBindings(frame);
        var idleActive = !sent && _holdNote is null && !AnyEnabledSlot(frame);
        var gcdHold = !sent && _holdNote is null && !idleActive && frame.OnGcd;
        Report(sent ? "sending"
                : (_holdNote ?? (gcdHold ? "waiting for GCD"
                    : (idleActive ? "holding (no suggestion from MaxDps)" : "holding"))),
            true, frame.State, summary, "-");
    }

    /// <summary>
    /// True when MaxDps recommends nothing SENDABLE right now (every slot is
    /// empty OR its type is toggled off). Upstream-idle by design — not an
    /// error, not a link failure. v1.3.4: toggled-off types are SKIPPED,
    /// never waited on (user: "if one type is off the companion should never
    /// wait" — a frame whose only suggestion is a disabled type must read as
    /// idle, not hold the engine on a slot it will never send).
    /// </summary>
    private bool AnyEnabledSlot(BridgeFrame frame)
    {
        for (var i = 0; i < PixelProtocol.SlotCount; i++)
            if (_settings.SlotEnabled[i] && frame.Slots[i] is not null) return true;
        return false;
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

        // Global min-gap (cheap, one compare): never two presses faster
        // than MinKeyIntervalMs — protects against double-fire, not pacing.
        if (now - _lastAnyPress < gap) return false;

        KeyStroke? held = null;
        // v1.3.3: scan Priority in order EVERY tick (Main first). The frame
        // IS the order — no start-offset rotation (see send-site note).
        for (var offset = 0; offset < Priority.Length; offset++)
        {
            var slot = Priority[offset];
            var index = (int)slot;

            if (!_settings.SlotEnabled[index]) continue;
            if (frame[slot] is not { } stroke) continue;

            // v1.3.5 #2 GCD GATE: while the bridge reports an active GCD
            // (C_Spell isOnGCD, NeverSecret), nothing that rides the GCD
            // may fire — WoW would ignore it (the old spam). Interrupts
            // are OFF the GCD and BYPASS this so a kick still lands mid-GCD.
            // When the GCD ends the next tick presses the CURRENT
            // suggestion within one poll (≤50 ms) — inside the 10-100 ms
            // availability window, minimal latency, no wasted presses.
            if (frame.OnGcd && slot != Slot.Interrupt) continue;
            // v1.3.3: NO per-slot gap — every READY slot is sendable the
            // moment the GCD window opens, so R→SE transitions fire on the
            // next tick instead of stalling behind a per-slot timer.

            // Binding audit: a spell on true movement keys (WASD/Space/
            // arrows) is never sent — replaying it would drive movement
            // every tick. Q/E are NOT movement keys here (user's Paladin
            // binds: they strafe by default but are standard spell binds;
            // IsMovementStroke already excludes them — pinned by the
            // Sep-2026 stuck-rotation report where Q/E-class binds are
            // the whole rotation).
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

            _lastSlotPress[index] = now; // diagnostics only (see field note)
            _lastAnyPress = now;
            // v1.3.3: NO round-robin advance on send. The bridge re-encodes
            // the CURRENT suggestion every 50 ms; rotating the start offset
            // after each press made the engine SKIP the fresh suggestion and
            // re-fire a stale slot (the R,R,R machine-gun: R stayed valid
            // across ticks, rotation cycled past SE's slot). Always scan
            // from Priority[0] (Main first) — the frame IS the order.
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
        _lastKeySent = $"Target: {KeySender.DescribeStroke(stroke)}";
        return true;
    }

    /// <summary>
    /// Sends the configured InteractKey, never a movement key, and NEVER a
    /// key that collides with a live spell bind. v1.3.3 F-on-engage fix
    /// (Sep-2026 live test: companion pressed F on every engage although
    /// the user's Interact is MB5/Alt+MB5): F was the STALE DEFAULT
    /// InteractKey from dist\settings.ini — the user never typed MB5 into
    /// the box, so TryParse succeeded on "F" and the need-interact state
    /// (melee range on engage) fired it. Two guards now: (1) a parse
    /// failure surfaces LastParseError instead of silently holding; (2) an
    /// InteractKey whose stroke EQUALS any currently-encoded spell stroke
    /// is refused — interact must never shadow the rotation (if your
    /// interact key IS a spell key, the spell path owns it).
    /// </summary>
    private bool TrySendInteractKey(IntPtr gameHandle, uint gamePid, BridgeFrame frame)
    {
        var now = _clock.ElapsedMilliseconds;
        var gap = Math.Max(1, _settings.MinKeyIntervalMs);
        if (now - _lastAnyPress < gap) return false;
        if (!MovementGuard.TryParseInteractKey(_settings.InteractKey, out var stroke))
        {
            _holdNote = MovementGuard.LastParseError is { } err
                ? $"interact key unparseable: {err}"
                : "interact key unparseable";
            return false;
        }
        // Shadow guard: interact must never equal a live spell stroke
        // (same VK + same modifiers). Firing it would double-press the
        // spell path's key out of order (the F-on-engage symptom: F was
        // BOTH the stale interact default AND a live spell bind).
        for (var i = 0; i < PixelProtocol.SlotCount; i++)
        {
            if (frame.Slots[i] is not { } spell) continue;
            if (spell.VirtualKey == stroke.VirtualKey
                && spell.Shift == stroke.Shift
                && spell.Ctrl == stroke.Ctrl
                && spell.Alt == stroke.Alt)
            {
                _holdNote = $"interact key {KeySender.DescribeStroke(stroke)} shadows a spell bind — change it (MB5?)";
                return false;
            }
        }

        // An InteractKey of W/A/S/D/Space/arrows would inject movement on
        // every need-interact frame. Hold and let the status line say why.
        // Mouse buttons (user's case: MB5 / Alt+MB5) are NOT movement keys
        // — IsMovementStroke only matches WASD/Space/arrows — so they pass
        // here and take the SendInput branch below (mouse has no per-window
        // PostMessage route, hence the foreground gate; modifiers ride
        // along as keyboard input in the same batch).
        _interactBlocked = MovementGuard.IsMovementStroke(stroke);
        if (_interactBlocked) return false;
        // Physical-hold check applies to keyboard keys only: mouse buttons
        // report async state per-button and the user may legitimately hold
        // MB5 (push-to-talk / mount) while interacting — skipping would
        // deadlock the interact fallback whenever the button is in use.
        if (!KeySender.IsMouse(stroke.VirtualKey) && MovementGuard.IsPhysicallyDown(stroke.VirtualKey))
        {
            _holdNote = $"holding {KeySender.DescribeStroke(stroke)} (you are holding it)";
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
        _lastKeySent = $"Interact: {KeySender.DescribeStroke(stroke)}";
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
        Slot.Offensive => "Off",
        Slot.Defensive => "Def",
        Slot.Consumable => "Cons",
        Slot.Trinket => "Trin",
        Slot.Interrupt => "Int",
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
