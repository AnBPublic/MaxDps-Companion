using System.Diagnostics;

namespace MaxDpsCompanion;

internal readonly record struct EngineStatus(
    string Message,
    bool BridgeVisible,
    BridgeState State,
    string SlotSummary,
    string LastKeySent,
    string RawSample,
    string Decision);

/// <summary>
/// Immutable, secret-safe snapshot of one action (or the current plan head)
/// for the UI: name, reason, provider and a human-readable "why" list.
/// Frozen v2.7 contract — the UI codes against these exact members.
/// </summary>
public sealed record LiveActionSnapshot(string Action, string Reason, string Provider, IReadOnlyList<string> Why);

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
    // Send order lives in the deterministic decision layer. With
    // IntelligenceEnabled OFF the order is DecisionEngine.FallbackOrder —
    // the exact legacy priority array: MAIN first, because a live main
    // suggestion must never wait behind a situational cooldown while the GCD
    // sits idle (Sep-2026: main starved behind offensive/defensive because
    // interrupt/defensive outranked it and the main cell was usually EMPTY —
    // see Reader.GetMainSpellID note). When ON the evaluator reorders
    // deterministically (interrupt urgency, defensive urgency, stale and
    // duplicate handling) but the per-slot gates below stay authoritative.

    /// <summary>
    /// How long to keep missing the block before sweeping the screen again.
    /// PERF (v1.3.9): 2s → 5s. A sweep copies the WHOLE client area and
    /// scans it pixel by pixel — by far the most expensive thing this
    /// process does. When the strip is merely hidden (login screen, alt-tab
    /// covered) that cost was paid twice a second for nothing; 5s keeps the
    /// self-heal while cutting the worst-case load by 60%.
    /// </summary>
    private const long RelocateIntervalMs = 5000;

    /// <summary>
    /// How long the strip may stay missing before the engine assumes the
    /// capture chain itself has gone stale (game window recreated on relog /
    /// character switch / display-mode change, or a frozen GDI screen DC) and
    /// forces a full re-attach. Without this, a stale-but-still-valid window
    /// handle kept sampling the wrong pixels until the app was restarted.
    /// </summary>
    private const long RecoveryIntervalMs = 2000;

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

    // Deterministic decision layer (v1.4.0): per-slot suggestion history +
    // the last evaluator result (diagnostics only). With IntelligenceEnabled
    // off the tracker still runs (cheap) but the evaluator is bypassed and
    // the legacy priority array drives the send loop.
    private readonly CandidateTracker _candidateTracker = new();
    private DecisionResult _lastDecision = DecisionResult.Fallback();

    // v1.6.0 deterministic action scheduler (default on; see Scheduler/).
    // Owns the send order and pacing: link loss (frozen heartbeat), interrupt
    // and defensive urgency, GCD + minimum key interval, stale demotion and
    // repeat suppression. [Scheduler] Enabled=0 falls back to the exact legacy
    // path (with [Intelligence] able to reorder it) — byte-identical.
    private readonly ActionScheduler _scheduler = new();
    private SchedulePlan _lastPlan = SchedulePlan.Hold(ScheduleReason.NoCandidate);

    // v2.0 ability knowledge + situational policy. The catalog is immutable;
    // options are rebuilt per tick from settings (cheap record).
    private readonly AbilityCatalog _catalog = AbilityCatalog.Default;

    // v1.5.0 local rotation telemetry (opt-in, default off; see Telemetry/).
    // Null = the zero-cost disabled path. MainForm attaches a recorder when
    // '[Telemetry] Enabled=1'; all capture happens on this engine thread.
    private volatile TelemetryRecorder? _telemetry;
    private BridgeFrame? _telemetryFrame;
    private DecodeFault _telemetryFault;
    private DecisionContext? _telemetryContext;
    private DecisionResult? _telemetryDecision;
    private CombatContext? _lastCombatContext;
    private bool _planFreshThisTick;

    // v2.7 UI snapshots (frozen names). Lock-guarded reference swaps; the
    // snapshot is an immutable record holding only plain strings.
    private readonly object _snapshotLock = new();
    private LiveActionSnapshot? _lastAction;
    private LiveActionSnapshot? _currentPlanHead;

    /// <summary>Last successful sent spell action (v2.7; null until the first send).</summary>
    public LiveActionSnapshot? LastAction
    {
        get { lock (_snapshotLock) return _lastAction; }
    }

    /// <summary>The current plan's top scheduled action (v2.7; null when the plan is empty).</summary>
    public LiveActionSnapshot? CurrentPlanHead
    {
        get { lock (_snapshotLock) return _currentPlanHead; }
    }

    private Thread? _thread;
    private volatile bool _running;
    private long _lastAnyPress;
    private long _lastLocateAttempt = long.MinValue;
    private long _lastActiveMs = long.MinValue;
    // Last tick that decoded a real frame; drives the stale-capture recovery
    // (see RecoveryIntervalMs). Set in Start() so a fresh Start always gets a
    // full re-attach first.
    private long _lastFrameMs;
    private string _lastKeySent = "-";
    private string? _holdNote;
    private volatile bool _idle = true;
    private bool _targetBlocked;
    private bool _interactBlocked;

    public RotationEngine(AppSettings settings) => _settings = settings;

    /// <summary>Suspends input sending without tearing down the reader, for the pause hotkey.</summary>
    public volatile bool Paused;

    /// <summary>
    /// The class/spec last decoded from the wire (v5 cell 33). UI-only: the
    /// Class skills screen preselects what the game reports. Null until a
    /// context-valid frame decodes; a reference read is safe from the UI thread.
    /// </summary>
    public bool TryGetLiveClass(out string? className)
    {
        className = _lastCombatContext?.Class;
        return className is not null;
    }

    public bool TryGetLiveSpec(out string? specName)
    {
        specName = _lastCombatContext?.Spec;
        return specName is not null;
    }

    /// <summary>Optional local telemetry sink. Null disables capture entirely.</summary>
    public TelemetryRecorder? Telemetry
    {
        get => _telemetry;
        set => _telemetry = value;
    }

    /// <summary>Monotonic engine clock; used for telemetry session events.</summary>
    public long ElapsedMs => _clock.ElapsedMilliseconds;

    public event Action<EngineStatus>? StatusChanged;

    /// <summary>Raised when the sweep finds the block somewhere other than the configured offset.</summary>
    public event Action<BlockLocation>? LocationChanged;

    public bool IsRunning => _running;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _lastLocateAttempt = long.MinValue;
        // Every Start re-attaches from scratch (window handle + GDI surface),
        // so toggling the companion off/on now heals a stale attachment —
        // previously only a full app restart re-resolved it (the user's
        // relog/character-change "stops detecting until restart" report).
        _lastFrameMs = _clock.ElapsedMilliseconds;
        // Fresh session: never inherit suggestion timestamps from a previous
        // Start (they would instantly read as stale).
        _candidateTracker.Reset();
        _lastDecision = DecisionResult.Fallback();
        _scheduler.Reset();
        _legacyPolicyMemory.Reset();
        _lastPlan = SchedulePlan.Hold(ScheduleReason.NoCandidate);
        _telemetryFrame = null;
        _telemetryFault = DecodeFault.None;
        _telemetryContext = null;
        _telemetryDecision = null;
        _lastCombatContext = null;
        _planFreshThisTick = false;
        lock (_snapshotLock) { _lastAction = null; _currentPlanHead = null; }
        if (_telemetry is { } telemetry)
        {
            telemetry.ResetLink();
            telemetry.Append(TelemetryEvent.Session(0, Native.AppVersion, PixelProtocol.SupportedVersion, telemetry.Capacity,
                recording: true, catalogVersion: AbilityCatalog.CatalogVersion));
        }
        RecoverAttachment();
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "MaxDpsCompanion.Engine",
            // Never compete with the game's render thread for CPU.
            Priority = ThreadPriority.BelowNormal,
        };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(1000);
        _thread = null;
        // After the join: the engine thread is the only Append writer, so the
        // session footer cannot race it.
        if (_telemetry is { } telemetry)
            telemetry.Append(TelemetryEvent.Session(_clock.ElapsedMilliseconds, Native.AppVersion, PixelProtocol.SupportedVersion, telemetry.Capacity,
                recording: false, catalogVersion: AbilityCatalog.CatalogVersion));
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
        // PERF (v1.3.9) piggybacked-on-the-game pacing:
        //  * A high-resolution WAITABLE TIMER paces the loop instead of
        //    Thread.Sleep. Sleep's default granularity is 15.6 ms, so a
        //    10-20 ms cadence would jitter by up to a full tick — exactly
        //    the kind of variance that shows up as inconsistent key timing.
        //    CreateWaitableTimerEx(HIGH_RESOLUTION) is per-call precision
        //    WITHOUT timeBeginPeriod (which raises the system-wide timer
        //    interrupt and costs the game CPU/power — measured and
        //    documented; avoided for that reason).
        //  * ADAPTIVE cadence: fast while there is something to act on,
        //    slow when idle (no game / no block / out of combat), so the
        //    companion costs ~0 CPU when nothing can happen.
        //  * BELOW-NORMAL thread priority: the game's render thread always
        //    wins the CPU; our sampling never competes with a frame.
        var timer = Native.CreateWaitableTimerEx(IntPtr.Zero, null,
            Native.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, Native.TIMER_ALL_ACCESS);
        try
        {
            while (_running)
            {
                var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    Tick();
                }
                catch (Exception ex)
                {
                    Report($"error: {ex.Message}", false, BridgeState.Idle);
                }
                var tickUs = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMicroseconds;

                var fast = _settings.PollIntervalMs;
                var idleMs = Math.Max(120, fast * 4);
                var wait = _idle ? idleMs : Math.Max(8, fast);
                // PERF (v1.3.9) duty-cycle guard: the screen read costs what
                // it costs (measured ~4.2 ms per BitBlt on this machine —
                // the whole tick). Cap our CPU share at ~10% by stretching
                // the cadence to 10x the measured tick cost, so a slow
                // capture path degrades latency gracefully instead of
                // stealing frames from the game.
                if (tickUs > 0)
                {
                    var dutyFloorMs = (int)Math.Ceiling(tickUs * 10 / 1000.0);
                    if (dutyFloorMs > wait && dutyFloorMs <= 250) wait = dutyFloorMs;
                }
                if (timer != IntPtr.Zero)
                {
                    var due = -(long)wait * 10_000;   // relative, 100 ns units
                    if (Native.SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                        Native.WaitForSingleObject(timer, (uint)wait + 2);
                    else
                        Thread.Sleep(wait);
                }
                else
                {
                    Thread.Sleep(wait);
                }
            }
        }
        finally
        {
            if (timer != IntPtr.Zero) Native.CloseHandle(timer);
        }
    }

    private void Tick()
    {
        // Per-tick telemetry state: a fresh frame/context replaces these; a
        // hold or decode error leaves them null/None.
        _telemetryFrame = null;
        _telemetryFault = DecodeFault.None;
        _telemetryContext = null;
        _telemetryDecision = null;
        _lastCombatContext = null;
        _planFreshThisTick = false;

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
        // v5 width first; a stale addon renders fewer cells (9 = v4, 8 = v1),
        // so the extra captured cells are background and v5 Decode rejects by
        // the version nibble. PERF: the fallback windows are trimmed out of
        // the SAME capture — no second BitBlt.
        var cells = _sampler.Sample(block, _settings.CellSize);
        var frame = PixelProtocol.Decode(cells, _settings.Color);
        if (frame is null)
        {
            var v4 = PixelProtocol.TrimToV4(cells);
            frame = PixelProtocol.Decode(v4, _settings.Color);
            if (frame is not null)
            {
                Report("addon is v4 - run install-addon.ps1 + /reload", true, frame.State, WantDiagnostics ? Summarise(frame) : "-", WantDiagnostics ? Describe(v4) : "-");
                cells = v4;
            }
        }
        if (frame is null)
        {
            var old = PixelProtocol.TrimToV1(cells);
            frame = PixelProtocol.Decode(old, _settings.Color);
            if (frame is not null)
            {
                Report("addon is v1 - run install-addon.ps1 + /reload", true, frame.State, WantDiagnostics ? Summarise(frame) : "-", WantDiagnostics ? Describe(old) : "-");
                cells = old;
            }
        }

        if (frame is null)
        {
            // v1.5.0 telemetry: classify the failure (magic / checksum /
            // commit / version / state) without recording any screen content.
            _telemetryFault = PixelProtocol.Diagnose(cells, _settings.Color);
            // A visible calibrate pattern is not a failure: say so plainly so
            // a stuck `/mdb calibrate on` reads as what it is instead of a
            // capture problem. Check the pattern FIRST — the ramp is a
            // healthy capture, so it must not trigger the stale-capture
            // recovery below. Probe both widths: a stale v1 pattern
            // classifies only in the 8-cell window (see CalibrateWorker.SampleBoth).
            if (ColorLearner.Classify(cells, _settings.Color) >= 0
                || ColorLearner.Classify(PixelProtocol.TrimToV4(cells), _settings.Color) >= 0
                || ColorLearner.Classify(PixelProtocol.TrimToV1(cells), _settings.Color) >= 0)
            {
                Report("calibrate pattern visible (/mdb calibrate off to resume)", true, BridgeState.Paused, "-", WantDiagnostics ? Describe(cells) : "-");
                return;
            }

            // Self-heal the capture chain after a sustained miss. Relocating
            // alone cannot fix a stale-but-still-valid game-window handle or
            // a frozen screen DC — both survive relog / character switches
            // and were only cleared on an app restart (the user's bug). Force
            // a full window + GDI re-attach, then re-evaluate on the next
            // tick with a clean origin.
            if (_clock.ElapsedMilliseconds - _lastFrameMs > RecoveryIntervalMs)
            {
                RecoverAttachment();
                _lastFrameMs = _clock.ElapsedMilliseconds;
                Report("re-attaching to the game window", false, BridgeState.Idle, "-", "-");
                _idle = true;
                return;
            }

            var message = Relocate(origin, clientSize)
                ? "block found, re-aligned"
                : "no pixel block - client must be windowed or borderless";
            Report(message, false, BridgeState.Idle, "-", WantDiagnostics ? Describe(cells) : "-");
            _idle = true;
            return;
        }

        // A real frame decoded: the capture chain is healthy, so the stale-
        // capture recovery clock is reset.
        _lastFrameMs = _clock.ElapsedMilliseconds;
        _telemetryFrame = frame;

        // Decision layer observation: record the decoded candidates once per
        // real frame (stroke, first-seen, last-changed, pressed-since-change).
        _candidateTracker.Update(frame, _lastFrameMs);

        // PERF (v1.3.9): summary/raw strings only when the UI wants them.
        var summary = WantDiagnostics ? Summarise(frame) : "-";

        // v1.6.0 LINK GATE (scheduler on): a frozen-but-decodable strip (the
        // addon stopped rendering; commit==heartbeat still passes checksum)
        // must never keep a rotation firing. Observe every frame so a later
        // heartbeat change heals the link; hold everything while lost.
        if (_settings.SchedulerEnabled)
        {
            _scheduler.Observe(frame, _lastFrameMs);
            if (_scheduler.IsLinkLost(_lastFrameMs, _settings.SchedulerHeartbeatTimeoutMs))
            {
                _lastPlan = SchedulePlan.Hold(ScheduleReason.LinkLost);
                Report("link lost (addon heartbeat frozen)", true, frame.State, summary, "-");
                _idle = true;
                return;
            }
        }

        // Adaptive cadence: idle unless there is a target-bearing frame.
        _idle = frame.State == BridgeState.Idle && !frame.HasTarget;

        if (frame.State == BridgeState.Active)
        {
            _lastActiveMs = _clock.ElapsedMilliseconds;
        }

        if (frame.State == BridgeState.Paused)
        {
            Report("addon bridge paused (/mdb on)", true, frame.State, summary);
            _idle = true;
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
            Report("holding (out of combat)", true, frame.State, summary);
            _idle = true;
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

    /// <summary>
    /// Drops the cached game-window handle and the captured GDI surface so
    /// the next tick re-resolves both from scratch. Cheap and idempotent;
    /// used on every Start and on a sustained decode miss (recovery).
    /// </summary>
    private void RecoverAttachment()
    {
        _window.Reset();
        _sampler.Reset();
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

    /// <summary>
    /// Builds the decision context from the decoded frame, the tracker
    /// snapshot and settings. All inputs are plain values already exposed by
    /// the pixel protocol or produced by this process — no new state is
    /// invented (no health, cooldowns or cast data exists here by design).
    /// </summary>
    private DecisionContext BuildContext(BridgeFrame frame, long nowMs) => new()
    {
        InCombat = frame.InCombat,
        OnGcd = frame.OnGcd,
        HasTarget = frame.HasTarget,
        State = frame.State,
        NowMs = nowMs,
        StaleAfterMs = Math.Max(250, _settings.IntelligenceStaleAfterMs),
        Candidates = _candidateTracker.Snapshot(_settings.SlotEnabled),
    };

    /// <summary>Ability display name when the identity is known, else the stroke/slot.</summary>
    private string ActionNameOf(int spellId, KeyStroke stroke) =>
        spellId > 0 && _catalog.TryGet(spellId) is { } ability ? ability.Name : stroke.Describe();

    private void SetPlanHead(ScheduledAction[] actions)
    {
        if (actions.Length == 0)
        {
            lock (_snapshotLock) _currentPlanHead = null;
            return;
        }
        var head = actions[0];
        var snapshot = new LiveActionSnapshot(
            ActionNameOf(head.SpellId, head.Stroke), head.Reason.ToString(), head.Provider, head.Evidence);
        lock (_snapshotLock) _currentPlanHead = snapshot;
    }

    private void SetLastAction(string action, string reason, string provider, IReadOnlyList<string> why)
    {
        var snapshot = new LiveActionSnapshot(action, reason, provider, why);
        lock (_snapshotLock) _lastAction = snapshot;
    }

    private bool TrySendOne(BridgeFrame frame, IntPtr gameHandle, uint gamePid)
    {
        var now = _clock.ElapsedMilliseconds;
        return _settings.SchedulerEnabled
            ? TrySendScheduled(frame, gameHandle, gamePid, now)
            : TrySendLegacy(frame, gameHandle, gamePid, now);
    }

    /// <summary>
    /// v1.6.0 scheduler path: the deterministic plan owns the order and the
    /// pacing gates; the per-slot OS gates below stay authoritative (movement
    /// bind, physically-held key, focus, background policy, window liveness).
    /// A rejected action is recorded via NoteAttempt so the scheduler retries
    /// the slot only after a short window, and lower ranks get their turn in
    /// the same tick.
    /// </summary>
    private bool TrySendScheduled(BridgeFrame frame, IntPtr gameHandle, uint gamePid, long now)
    {
        // Build the combat context once: the scheduler consumes it (hard
        // execution-safety gate even with intelligence off), the policy
        // consumes it, and the telemetry tick records it (explainability).
        var combat = CombatContext.FromFrame(frame);
        _lastCombatContext = combat;
        var plan = _scheduler.Advance(new ScheduleInput
        {
            Frame = frame,
            Candidates = _candidateTracker.Snapshot(_settings.SlotEnabled),
            NowMs = now,
            MinKeyIntervalMs = _settings.MinKeyIntervalMs,
            StaleAfterMs = Math.Max(250, _settings.IntelligenceStaleAfterMs),
            HeartbeatTimeoutMs = _settings.SchedulerHeartbeatTimeoutMs,
            RepeatSuppressMs = _settings.SchedulerRepeatSuppressMs,
            Context = combat,
            Options = _settings.IntelligenceEnabled ? PolicyOptions.FromSettings(_settings) : null,
            Catalog = _catalog,
            CollectPolicyVerdicts = _telemetry is not null,
        });
        _lastPlan = plan;
        _planFreshThisTick = true;
        SetPlanHead(plan.Actions);
        // The scheduler path's explainability record is the plan (`pol`); the
        // legacy decision fields must not carry over from an earlier legacy
        // tick, or a mid-session scheduler toggle would record a stale
        // decision against fresh candidates (and replay as a mismatch).
        _telemetryDecision = null;
        _telemetryContext = null;

        KeyStroke? held = null;
        foreach (var action in plan.Actions)
        {
            var index = (int)action.Slot;
            var stroke = action.Stroke;

            // Binding audit: a spell on true movement keys (WASD/Space/
            // arrows) is never sent — replaying it would drive movement
            // every tick (same rule as the legacy path).
            if (MovementGuard.IsMovementStroke(stroke))
            {
                _holdNote = $"slot {SlotName(action.Slot)} is bound to {stroke.Describe()}";
                _scheduler.NoteAttempt(now, action.Slot, stroke, AttemptOutcome.MovementBound);
                continue;
            }

            // Collision skip: a synthetic KEYUP for a physically-held key
            // reads as a real release in WoW. Exact-VK match only.
            if (MovementGuard.IsPhysicallyDown(stroke.VirtualKey))
            {
                held = stroke;
                _scheduler.NoteAttempt(now, action.Slot, stroke, AttemptOutcome.PhysicalHold);
                continue;
            }

            if (KeySender.IsMouse(stroke.VirtualKey))
            {
                // Mouse input has no per-window route: always needs focus.
                if (!_window.IsForeground || !_window.IsGameWindow(gameHandle))
                {
                    _holdNote = $"slot {SlotName(action.Slot)} needs focus (mouse input)";
                    _scheduler.NoteAttempt(now, action.Slot, stroke, AttemptOutcome.FocusRequired);
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
                    _scheduler.NoteAttempt(now, action.Slot, stroke, AttemptOutcome.FocusRequired);
                    return false;
                }
                if (!KeySender.SendToWindow(gameHandle, gamePid, stroke, _settings.KeyPressMs))
                {
                    _holdNote = "game window lost";
                    _scheduler.NoteAttempt(now, action.Slot, stroke, AttemptOutcome.WindowLost);
                    return false;
                }
            }

            _lastSlotPress[index] = now; // diagnostics only (see field note)
            _candidateTracker.NotePressed(action.Slot, now);
            _scheduler.NoteSent(now, action.Slot, stroke, action.SpellId);
            var sendInterval = _lastAnyPress <= 0 ? 0 : now - _lastAnyPress; // v1.5.0 telemetry
            _lastAnyPress = now;
            _telemetry?.Append(TelemetryEvent.Sent(now, "spell", action.Slot, stroke, sendInterval, action.SpellId));
            _lastKeySent = $"{SlotName(action.Slot)}: {stroke.Describe()}";
            SetLastAction(ActionNameOf(action.SpellId, stroke), action.Reason.ToString(), action.Provider, action.Evidence);
            return true;
        }

        if (held is { } heldStroke)
            _holdNote = $"holding {heldStroke.Describe()} (you are holding it)";

        return false;
    }

    /// <summary>
    /// Legacy path ([Scheduler] Enabled=0): [Intelligence] order or the exact
    /// pre-v1.4.0 priority array, global min-gap on _lastAnyPress. Behaviour
    /// is pinned byte-identical by DecisionEngineTests when intelligence is off.
    /// </summary>
    private readonly PolicyMemory _legacyPolicyMemory = new();

    private bool TrySendLegacy(BridgeFrame frame, IntPtr gameHandle, uint gamePid, long now)
    {
        var gap = Math.Max(1, _settings.MinKeyIntervalMs);

        // Global min-gap (cheap, one compare): never two presses faster
        // than MinKeyIntervalMs — protects against double-fire, not pacing.
        if (now - _lastAnyPress < gap) return false;

        KeyStroke? held = null;
        // v1.4.0 DECISION LAYER: the evaluator produces the send order from
        // normalized candidates + observable context (deterministic, no game
        // state beyond the protocol). Disabled = the legacy priority array,
        // so this loop is byte-identical to the pre-intelligence engine.
        // v1.5.0: while telemetry records, the context is always built (even
        // with intelligence off) so a legacy session can be replayed through
        // the evaluator as "would-select"; the SEND decision is unchanged.
        DecisionContext? context = null;
        DecisionResult decision;
        if (_settings.IntelligenceEnabled || _telemetry is not null)
        {
            context = BuildContext(frame, now);
            decision = _settings.IntelligenceEnabled ? DecisionEngine.Evaluate(context) : DecisionResult.Fallback();
        }
        else
        {
            decision = DecisionResult.Fallback();
        }
        _telemetryContext = context;
        _telemetryDecision = decision;
        _lastDecision = decision;
        // v2.1: the context is always built — the hard execution-safety gate
        // below runs even on the double-off legacy path (it is not knowledge
        // filtering). A v4/v1 frame yields an all-UNKNOWN context, so a stale
        // in-game addon keeps the byte-identical legacy behaviour.
        var combat = CombatContext.FromFrame(frame);
        _lastCombatContext = combat;

        // v1.3.3: scan the order EVERY tick (Main first by default). The
        // frame IS the order — no start-offset rotation (see send-site note).
        foreach (var slot in decision.Order)
        {
            var index = (int)slot;

            if (!_settings.SlotEnabled[index]) continue;
            if (frame[slot] is not { } stroke) continue;

            // Companion-only slots (Mobility / SelfHeal) exist solely because
            // the knowledge base curated them; without intelligence they are
            // never allowed to fire.
            if (!_settings.IntelligenceEnabled && slot is Slot.Mobility or Slot.SelfHeal) continue;

            // v2.1 EXECUTION SAFETY on the legacy path: even with scheduler and
            // intelligence both off, no GCD-riding key is sent into a live
            // cast/channel (interrupts/items and verified non-movement off-GCD
            // abilities stay allowed). An all-UNKNOWN v4/v1 context changes
            // nothing, so a stale addon keeps the legacy behaviour.
            var playerCast = combat.Cast;
            if (ExecutionSafety.CastHoldReason(slot, frame.SpellId(slot), playerCast, _catalog) is { } execHold)
            {
                _holdNote ??= execHold;
                continue;
            }

            // v2.0 SITUATIONAL POLICY on the legacy path: same evaluator and
            // knowledge as the scheduler path, so disabling the scheduler does
            // not silently disable intelligence. No intelligence = no filter.
            PolicyDecision? policy = null;
            var spellId = 0;
            if (_settings.IntelligenceEnabled)
            {
                if (context is not null)
                {
                    foreach (var candidate in context.Candidates)
                    {
                        if (candidate.Slot != slot) continue;
                        spellId = candidate.SpellId;
                        break;
                    }
                }
                policy = PolicyEvaluator.Evaluate(new PolicyInput
                {
                    Slot = slot,
                    SpellId = spellId,
                    Context = combat,
                    Options = PolicyOptions.FromSettings(_settings),
                    Memory = _legacyPolicyMemory,
                    NowMs = now,
                    InCombat = frame.InCombat,
                    HasTarget = frame.HasTarget,
                }, _catalog);
                if (policy.Value.Verdict != PolicyVerdict.Use)
                {
                    _holdNote ??= $"{SlotName(slot)}: {policy.Value.Reason}";
                    continue;
                }
            }

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
            _candidateTracker.NotePressed(slot, now); // decision-layer history
            if (_settings.IntelligenceEnabled && spellId > 0 && _catalog.TryGet(spellId) is { } used)
                _legacyPolicyMemory.NoteUse(used, now);
            var sendInterval = _lastAnyPress <= 0 ? 0 : now - _lastAnyPress; // v1.5.0 telemetry
            _lastAnyPress = now;
            _telemetry?.Append(TelemetryEvent.Sent(now, "spell", slot, stroke, sendInterval, spellId));
            SetLastAction(
                ActionNameOf(spellId, stroke),
                policy?.Reason ?? $"{slot} candidate; legacy order",
                policy?.Provider ?? "",
                policy?.Evidence ?? []);
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
        var sendInterval = _lastAnyPress <= 0 ? 0 : now - _lastAnyPress; // v1.5.0 telemetry
        _lastAnyPress = now;
        if (_settings.SchedulerEnabled) _scheduler.NoteExternalSend(now); // keep pacing aware
        _lastKeySent = $"Target: {KeySender.DescribeStroke(stroke)}";
        _telemetry?.Append(TelemetryEvent.Sent(now, "target", null, stroke, sendInterval));
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
        var sendInterval = _lastAnyPress <= 0 ? 0 : now - _lastAnyPress; // v1.5.0 telemetry
        _lastAnyPress = now;
        if (_settings.SchedulerEnabled) _scheduler.NoteExternalSend(now); // keep pacing aware
        _lastKeySent = $"Interact: {KeySender.DescribeStroke(stroke)}";
        _telemetry?.Append(TelemetryEvent.Sent(now, "interact", null, stroke, sendInterval));
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
        Slot.Mobility => "Mob",
        Slot.SelfHeal => "Heal",
        _ => slot.ToString(),
    };

    /// <summary>
    /// PERF (v1.3.9): diagnostics strings are OFF by default. The slot
    /// summary + raw cell hex used to be built (with a List + string.Join
    /// + 72-char hex) on EVERY tick — ~20-100 allocations per second of
    /// pure UI text nobody was reading. The UI flips this on while the
    /// Advanced popup is open.
    /// </summary>
    public volatile bool WantDiagnostics;

    /// <summary>Advanced-only: the last decision head + reason + confidence.</summary>
    private string DecisionSummary()
    {
        if (!WantDiagnostics) return "-";
        if (_settings.SchedulerEnabled)
        {
            var held = _lastPlan.PolicyHeld > 0 ? $" · {_lastPlan.PolicyHeld} policy-held" : "";
            var skipped = _lastPlan.PolicySkipped > 0 ? $" · {_lastPlan.PolicySkipped} policy-skipped" : "";
            if (_lastPlan.Selected is { } scheduled)
            {
                var demoted = _lastPlan.StaleDemoted ? " · stale demoted" : "";
                var suppressed = _lastPlan.Suppressed > 0 ? $" · {_lastPlan.Suppressed} blocked" : "";
                var detail = _lastPlan.PolicyDetail is { Length: > 0 } d ? $" · {d}" : "";
                // v2.7 explainability: provider + a compact evidence line.
                var head = _lastPlan.Actions.Length > 0 ? _lastPlan.Actions[0] : default;
                var provider = head.Provider is { Length: > 0 } pv ? $" · {pv}" : "";
                var evidence = head.Evidence.Count > 0 ? $" [{string.Join("; ", head.Evidence)}]" : "";
                return $"{SlotName(scheduled)}: {_lastPlan.Reason} ({_lastPlan.Confidence}%){provider}{evidence}{demoted}{suppressed}{detail}{held}{skipped}";
            }
            var holdDetail = _lastPlan.PolicyDetail is { Length: > 0 } hd ? $" · {hd}" : "";
            return $"scheduler: {_lastPlan.Reason}{holdDetail}{held}{skipped}";
        }
        if (!_settings.IntelligenceEnabled) return "off (legacy priority)";
        if (_lastDecision.Selected is not { } slot)
            return _lastDecision.Reason.ToString();
        var demotedInt = _lastDecision.DemotedStale ? " · stale demoted" : "";
        return $"{SlotName(slot)}: {_lastDecision.Reason} ({_lastDecision.Confidence}%){demotedInt}";
    }

    private void Report(string message, bool visible, BridgeState state, string summary = "-", string raw = "-")
    {
        // v1.5.0 telemetry: one tick event per Report (the engine's single
        // outcome funnel) + a link event only when visibility changes. No
        // frame = no candidate context, so a link-loss tick can never replay
        // as a decision.
        if (_telemetry is { } telemetry)
        {
            var now = _clock.ElapsedMilliseconds;
            var candidates = _telemetryContext?.Candidates
                ?? (_telemetryFrame is null ? [] : _candidateTracker.Snapshot(_settings.SlotEnabled));
            // The policy record only exists for ticks where the scheduler
            // actually ran; on the legacy path a stale plan must never be
            // recorded as if it explained this tick.
            var policyRecord = _planFreshThisTick
                ? TelemetryEvent.BuildPolicy(_lastPlan, true, _lastCombatContext,
                    _settings.IntelligenceEnabled ? PolicyOptions.FromSettings(_settings) : null)
                : null;
            telemetry.Append(TelemetryEvent.Tick(
                now,
                _telemetryFrame,
                _telemetryFault,
                _telemetryContext,
                _telemetryDecision,
                _settings.IntelligenceEnabled,
                candidates,
                message,
                visible,
                policyRecord));
            telemetry.RecordLink(now, visible, message, _telemetryFault);
        }
        StatusChanged?.Invoke(new EngineStatus(message, visible, state, summary, _lastKeySent, raw, DecisionSummary()));
    }

    public void Dispose()
    {
        Stop();
        _sampler.Dispose();
    }
}
