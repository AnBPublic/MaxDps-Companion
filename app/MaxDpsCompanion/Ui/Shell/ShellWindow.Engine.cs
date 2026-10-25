using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using WColor = Windows.UI.Color;

// Wave B (4.1 "Paragon"): this file is the ONLY Windows file that both declares
// and uses the Windows RotationEngine instance, and RotationEngine is the one
// type intentionally still defined in BOTH MaxDpsCompanion.Core and this
// assembly until Wave C merges the engines. CS0436 ("type in source conflicts
// with the imported type ... using the type defined in source") is therefore
// expected HERE for exactly that type, with the local (Windows, concrete
// capture/input) copy correctly winning. The suppression is file-scoped on
// purpose: ShadowedTypeGateTests asserts the shadow set is exactly
// {MaxDpsCompanion.RotationEngine}, so any OTHER duplicate still fails the
// build loudly, and this pragma disappears together with the Windows engine.
#pragma warning disable CS0436

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// P2-C engine wiring (docs/plans/2026-10-11-winui3-v4-migration.md §4): the
/// <see cref="RotationEngine"/> lifecycle, the 250 ms status pump, calibration
/// worker + DispatcherQueue marshaling, dialogs and file pickers — a port of
/// MainForm.StartEngine :2665, StopEngine :2715, TogglePauseFromConsole :1045,
/// RecalibrateFull :2834 / RecalibratePositionOnly :2856, CalibrateWorker
/// :2339-2479, RefreshStatus :2890-2942, UpdateHero :3036-3110 and
/// UpdateExpReadouts :971-1019.
///
/// Threading contract (deliberately identical to the WinForms form):
/// the engine thread writes plain fields <c>_status</c> / <c>_pendingLocation</c>
/// (NON-VOLATILE — the existing benign race is preserved, plan §4) and raises
/// StatusChanged / LocationChanged; the UI reads them on the DispatcherQueue
/// timer with no locks. Calibration marshals via
/// <c>DispatcherQueue.TryEnqueue</c> (InvokeRequired/BeginInvoke parity).
/// </summary>
public sealed partial class ShellWindow
{
    private RotationEngine? _engine;

    // ADDON-WINS toggle authority: the mirror echo is tracked for diagnostics
    // only; no auto-push (/mdb mask) call site exists.
    private readonly ToggleSync _toggleSync = new();

    // Engine thread -> UI handoff. Plain fields, NON-VOLATILE by design: this
    // preserves the existing benign race (plan §4). Do not "fix" with
    // volatile/locks — the WinForms shell behaved exactly like this.
    private EngineStatus _status;
    private BlockLocation? _pendingLocation;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _statusPump;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _settingsDebounce;
    private string _statusMessage = "Stopped";
    private StatusTone _statusMessageTone = StatusTone.Muted;
    // Last status line appended to the console home log (old _consoleLastStatus :146).
    private string _consoleLastStatus = "";
    // 4.4.0: the message actually LOGGED last (transient statuses advance the
    // tracker above but never reach the log, so A → transient → A must not
    // re-log A when the foreground returns).
    private string _consoleLastLogged = "";

    // Calibration worker (MainForm :2310-2312).
    private Thread? _calThread;
    private volatile bool _calCancel;
    private volatile bool _calibrating;

    // ContentDialog serialization: one modal at a time per XamlRoot.
    private readonly SemaphoreSlim _dialogGate = new(1, 1);
    private int _openDialogs;

    // Attached by the Advanced Diagnostics tab (telemetry card); kept here so
    // the engine's telemetry recorder has a single owner in the shell.
    private TelemetryRecorder? _telemetryRecorder;

    // Exp bridge-hint latch + S8 install-doctor throttle/witness (old :100, :178, :176).
    // 4.4.0 "Wayfinder": the latch became the clock-injected BridgeLogGate
    // (background pauses never log; one warning per outage/run) and the Exp log
    // tracker is separate from the classic console's.
    private readonly BridgeLogGate _bridgeLogGate = new();
    private string _expLastStatus = "";
    private string _expLastLogged = "";
    private int _doctorTick;
    private int? _lastLocatedCellSize;

    // 4.4.0 process presence + launch gap. The 1 s System.Threading.Timer only
    // writes the volatile flag (never touches UI); the launch grace is a
    // TickCount64 deadline so Open Game stays disabled until WoW appears.
    private volatile bool _gameProcessRunning;
    private long _launchGraceUntilMs;
    // 4.4.0: a poll that started before a restart must not write its stale
    // observation after the kill (it could re-assert "running" and clear the
    // launch grace). The restart bumps this epoch; stale results are dropped.
    private long _gamePollEpoch;
    private bool _restarting;
    private System.Threading.Timer? _gamePoll;
    // Last Open Game transport phase (0 idle / 1 starting / 2 running) so the
    // 250 ms pump only rewrites the label/glyph/help on an actual change.
    private int _expOpenState = -1;

    private void InitializeEngine(AppSettings settings)
    {
        _engine = new RotationEngine(settings);
        _engine.StatusChanged += s => _status = s;
        _engine.LocationChanged += location => _pendingLocation = location;

        // 4.4.0: initial presence is synchronous so the first painted transport
        // state is right (no "Open Game" flash while WoW is already running).
        _gameProcessRunning = WowWindow.IsProcessRunning(settings.ProcessName);
        _gamePoll = new System.Threading.Timer(_ => PollGameProcess(), null, 1000, 1000);

        // 250 ms UI pump, exactly the WinForms _uiTimer cadence (plan §4).
        _statusPump = DispatcherQueue.CreateTimer();
        _statusPump.Interval = TimeSpan.FromMilliseconds(250);
        _statusPump.IsRepeating = true;
        _statusPump.Tick += (_, _) => RefreshStatus();

        // 600 ms settings debounce, exactly the WinForms _saveDebounce cadence
        // (MainForm :108); Advanced text fields save through SaveSoon.
        _settingsDebounce = DispatcherQueue.CreateTimer();
        _settingsDebounce.Interval = TimeSpan.FromMilliseconds(600);
        _settingsDebounce.IsRepeating = false;
        _settingsDebounce.Tick += (_, _) =>
        {
            _settingsDebounce.Stop();
            SaveNow();
        };
    }

    private void StartStatusPump() => _statusPump?.Start();

    // ----- 4.4.0 process presence / launch gap (Exp transport) -----

    /// <summary>
    /// 1 s timer callback: refreshes the cached presence flag and clears the
    /// 20 s launch grace as soon as the process is actually observed. Timer
    /// thread only — never touches XAML; the 250 ms pump reads the flag.
    /// </summary>
    private void PollGameProcess()
    {
        var epoch = Volatile.Read(ref _gamePollEpoch);
        bool running;
        try
        {
            running = WowWindow.IsProcessRunning(_settings.ProcessName);
        }
        catch (Exception)
        {
            return;
        }
        // A restart invalidated this observation while it was being taken.
        if (epoch != Volatile.Read(ref _gamePollEpoch)) return;
        _gameProcessRunning = running;
        if (running) ClearLaunchGrace();
    }

    /// <summary>Open/Restart are blocked while the game runs OR during the launch gap.</summary>
    private bool GameBlocked =>
        _gameProcessRunning || Environment.TickCount64 < Volatile.Read(ref _launchGraceUntilMs);

    /// <summary>Arms the post-launch gap (20 s from Open/Restart).</summary>
    private void SetLaunchGrace(int ms) =>
        Volatile.Write(ref _launchGraceUntilMs, Environment.TickCount64 + Math.Max(0, ms));

    /// <summary>Clears the launch gap (process seen, launch failed, or restart ended).</summary>
    private void ClearLaunchGrace() => Volatile.Write(ref _launchGraceUntilMs, 0);

    /// <summary>
    /// Exp transport labels/enabled state. Called from UpdateExpReadouts (every
    /// pump tick, value-only writes) AND on click so the buttons disable
    /// immediately, before the next 250 ms frame.
    /// </summary>
    private void UpdateExpTransportState()
    {
        if (_expLaunchGame is not null)
        {
            var running = _gameProcessRunning;
            var blocked = GameBlocked;
            var state = running ? 2 : blocked ? 1 : 0;
            if (state != _expOpenState)
            {
                _expOpenState = state;
                var (glyph, text, accessible, font, help) = running
                    ? ("\uE72E", "Game Running", "\u25CF", FluentIcons,
                        "World of Warcraft is already running")
                    : blocked
                        ? ("\uE72C", "Game Starting\u2026", "\u21BB", FluentIcons,
                            "Waiting for World of Warcraft to start")
                        : ("\u2922", "Open Game", "\u2922", null,
                            "Open World of Warcraft through Battle.net");
                _expLaunchGame.SetLabel(glyph, text, accessible, font);
                ToolTipService.SetToolTip(_expLaunchGame, help);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(_expLaunchGame, help);
            }
            _expLaunchGame.IsEnabled = !blocked && !_restarting;
        }

        if (_expRestartGame is not null)
            _expRestartGame.IsEnabled = _gameProcessRunning && !_restarting;
    }

    /// <summary>Re-registers the global pause hotkey (MainForm.RegisterPauseHotkey :3135-3145).</summary>
    private void RegisterPauseHotkey() => _win32.RegisterPauseHotkey(_settings.PauseHotkey);

    /// <summary>v4.2.0: re-registers the manual party-CC hold hotkey.</summary>
    private void RegisterCcHoldHotkey() => _win32.RegisterCcHoldHotkey(_settings.CcHoldHotkey);

    // ----- status refresh (MainForm.RefreshStatus :2890-2942) -----

    internal void RefreshStatus()
    {
        if (_engine is null) return;

        if (_pendingLocation is { } located)
        {
            _pendingLocation = null;
            ApplyLocation(located);
            SetStatus("Block found, re-aligned.", ControlTheme.Accent);
            return;
        }

        if (_calibrating) return;

        var status = _status;
        if (!_engine.IsRunning)
        {
            if (_statusMessage != "Stopped") SetStatus("Stopped", ControlTheme.TextMuted);
        }
        else
        {
            var verb = Capitalise(status.Message);
            // 4.4.0: a background pause is neutral, not a failure. Danger only
            // while running, foreground, and the bridge is invisible.
            SetStatus(
                _engine.Paused ? $"Paused - {verb}" : verb,
                status.BridgeVisible || !status.GameInForeground || !_gameProcessRunning
                    ? ControlTheme.TextPrimary : ControlTheme.Danger);
        }

        PumpToggleSync();
        UpdateDiagnostics(status);
        UpdateHero();
        // P2-D: live browser verdicts + the S8 why-not-firing snapshot
        // (old MainForm.RefreshStatus :2931 and :3076).
        if (_abilitiesOverlay is { IsOpen: true }) _classBrowser?.RefreshLive();
        UpdateWhyNotFiring();
        if (_settings.InGameConfigMode) UpdateExpReadouts(status);
    }

    /// <summary>
    /// Diagnostics-page live readouts + bridge banner + throttled doctor
    /// (MainForm.RefreshStatus :2917-2938). Value-only writes; the doctor file
    /// reads stay throttled to ~2 s and only run after the page was built.
    /// </summary>
    private void UpdateDiagnostics(EngineStatus status)
    {
        _advancedDiagnostics?.UpdateLive(status, _engine is { IsRunning: true });
        UpdateBridgeBanner(status);
        if (_advancedContentBuilt && ++_doctorTick >= 8)
        {
            _doctorTick = 0;
            RefreshInstallDoctor();
        }
    }

    /// <summary>
    /// MainForm.PumpToggleSync :2706-2713. ADDON-WINS: re-reads the configured
    /// toggles (diagnostics only) and feeds the engine's Ext3 mirror so the
    /// observer tracks the overlay's authoritative state; never pushes.
    /// </summary>
    private void PumpToggleSync()
    {
        _toggleSync.ObserveSettings(_settings);
        Ext3Block? mirror = null;
        if (_engine is { IsRunning: true }) _engine.TryGetToggleMirror(out mirror, out _);
        if (_engine is not null) _toggleSync.ObserveMirror(_engine.ElapsedMs, mirror);
    }

    // ----- hero state (MainForm.UpdateHero :3036-3110) -----

    internal void UpdateHero()
    {
        if (!_uiReady || _engine is null) return;
        var status = _status;
        var running = _engine.IsRunning;
        var processPresent = _gameProcessRunning;
        // 4.4.0: the foreground pause is a normal state, not a failure. The
        // filter is computed ONCE here, before the classic/Exp fork, and the
        // last-status trackers still advance below (A → transient → A must not
        // re-log A). The "Paused - " prefix is a RefreshStatus decoration, so
        // strip it before classifying the underlying engine message.
        var background = running && processPresent && !status.GameInForeground;
        var transient = CaptureDiagnostics.IsTransientStatusLine(_statusMessage);

        var (stateText, stateColor) = !running
            ? ("Stopped", ControlTheme.TextMuted)
            : !processPresent ? ("Waiting for game", ControlTheme.TextMuted)
            : background ? ("Game in background", ControlTheme.TextMuted)
            : _engine.Paused ? ("Paused", ControlTheme.Warning)
            : status.BridgeVisible ? ("Running", ControlTheme.Success)
            : ("No bridge / window", ControlTheme.Danger);
        if (_stateLabel is not null)
        {
            if (_stateLabel.Text != stateText) _stateLabel.Text = stateText;
            _stateLabel.Foreground = ControlTheme.Brush(stateColor);
        }

        _linkLamp.Dot = !running || background || !processPresent
            ? ControlTheme.TextMuted
            : status.BridgeVisible ? ControlTheme.Success : ControlTheme.Danger;

        var classSpec = _engine.TryGetLiveClass(out var cls) && cls is { Length: > 0 }
            ? cls + (_engine.TryGetLiveSpec(out var spec) && spec is { Length: > 0 } ? $" {spec}" : "")
            : "AUTO DETECT";
        if (_classBadge is not null && _classBadge.Text != classSpec) _classBadge.Text = classSpec;

        var current = _engine.CurrentPlanHead;
        var lastAction = _engine.LastAction;
        var action = current?.Action ?? lastAction?.Action ?? "-";
        var why = lastAction is { Why.Count: > 0 } ? string.Join(" \u00b7 ", lastAction.Why)
            : current is { Why.Count: > 0 } ? string.Join(" \u00b7 ", current.Why)
            : "no reasons recorded";
        // In the background there is no live suggestion to show: say why the
        // Now line is idle instead of leaving a stale action on screen. When
        // the process is absent, say we are waiting for it (not "background").
        if (background)
        {
            action = "idle";
            why = CaptureDiagnostics.BackgroundMessage;
        }
        else if (running && !processPresent)
        {
            action = "idle";
            why = $"waiting for '{_settings.ProcessName}'";
        }
        var live = $"Now: {action} \u2014 {why}";
        if (_liveValue is not null)
        {
            if (_liveValue.Text != live) _liveValue.Text = live;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_liveValue, live);
        }

        if (_statusLine is not null)
        {
            if (_statusLine.Text != _statusMessage) _statusLine.Text = _statusMessage;
            _statusLine.Foreground = ControlTheme.Brush(ControlTheme.StatusColor(_statusMessageTone));
        }

        // Classic transport keeps the same "no second instance" rule; its text
        // never changes (Exp labels are owned by UpdateExpTransportState).
        if (_launchGame is not null)
            _launchGame.IsEnabled = !GameBlocked && !_restarting;

        // v3.5 S7: mirror the same values into the console home / Exp card.
        // Every call is value-only, so the 250 ms timer performs no layout here
        // (old UpdateHero :3078-3109).
        _engine.TryGetLiveClass(out var consoleClass);
        _engine.TryGetLiveSpec(out var consoleSpec);
        if (_consoleHome is not null)
        {
            _consoleHome.SetSpec(consoleClass, consoleSpec);
            _consoleHome.SetState(stateText, stateColor);
            _consoleHome.SetBridge(running ? $"Bridge: {status.State}" : "Bridge: stopped");
            _consoleHome.SetNow(action, why);
            _consoleHome.SetPaused(_engine.Paused);
            _consoleHome.SetCcHold(_engine.CcHoldActive, _engine.CcHoldRemainingMs);
            if (_consoleLastStatus != _statusMessage)
            {
                _consoleLastStatus = _statusMessage;
                if (!transient && _consoleLastLogged != _statusMessage)
                {
                    _consoleLastLogged = _statusMessage;
                    _consoleHome.AppendLog(_statusMessage, ControlTheme.StatusColor(_statusMessageTone));
                }
            }
        }

        if (_expConsole is not null)
        {
            _expConsole.SetSpec(consoleClass, consoleSpec);
            // Exp status card copy: a SHORT human phrase only; bridge/mask
            // jargon lives in the collapsed Details disclosure (:3089-3099).
            var (expState, expColor) = !running ? ("stopped", ToUi(ExpTheme.Secondary))
                : background ? ("game in background", ToUi(ExpTheme.Secondary))
                : _calibrating ? ("calibrating\u2026", ToUi(ExpTheme.Accent))
                : _engine.Paused ? ("paused", ToUi(ExpTheme.Accent))
                : action == "-" ? ("waiting for suggestion", ToUi(ExpTheme.Ok))
                : (action, ToUi(ExpTheme.Ok));
            _expConsole.SetState(expState, expColor);
            _expConsole.SetNow(action, why);
            if (_expLastStatus != _statusMessage)
            {
                _expLastStatus = _statusMessage;
                if (!transient && _expLastLogged != _statusMessage)
                {
                    _expLastLogged = _statusMessage;
                    _expConsole.AppendLog(_statusMessage, ControlTheme.StatusColor(_statusMessageTone));
                }
            }

            // 4.4.0 quiet bridge log: only the one warning the gate allows
            // reaches the Exp log (never while the game is in the background).
            if (_bridgeLogGate.Observe(status.BridgeVisible, running, status.GameInForeground,
                    Environment.TickCount64))
            {
                _expConsole.AppendLog(
                    $"No bridge sample decoding. {BridgeHealth.RepairHint} {BridgeHealth.CalibrateHint}",
                    ControlTheme.Danger);
            }
        }

        UpdateMirrorPills();
    }

    /// <summary>
    /// Sets the status line (MainForm.SetStatus :3112-3121). Marshals when
    /// called from the calibration worker: WinUI controls are not free-threaded.
    /// </summary>
    internal void SetStatus(string message, WColor color)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => SetStatus(message, color));
            return;
        }
        _statusMessage = message;
        _statusMessageTone = ToneFor(color);
        if (_uiReady) UpdateHero();
    }

    private static StatusTone ToneFor(WColor color)
        => color == ControlTheme.Danger ? StatusTone.Danger
        : color == ControlTheme.Accent ? StatusTone.Warning
        : color == ControlTheme.TextMuted ? StatusTone.Muted
        : color == ControlTheme.Success ? StatusTone.Success
        : StatusTone.Neutral;

    private static string Capitalise(string? message) =>
        string.IsNullOrEmpty(message) ? "-" : char.ToUpperInvariant(message[0]) + message[1..];

    // ----- engine lifecycle (MainForm :2659-2723) -----

    private void ToggleEngine()
    {
        if (_engine is { IsRunning: true }) StopEngine();
        else StartEngine();
    }

    internal void StartEngine()
    {
        if (_engine is null || _engine.IsRunning) return;
        ApplyToSettings();

        var game = new WowWindow();
        if (!game.Refresh(_settings.ProcessName))
        {
            SetStatus($"no process named '{_settings.ProcessName}'", ControlTheme.Danger);
            _ = ShowDialogAsync(AppTitle,
                $"No process named '{_settings.ProcessName}'.\n\nStart WoW first (Launch Game), then press Start.");
            return;
        }

        if (game.TryGetClientOrigin(out var origin, out var size)
            && BlockLocator.Locate(origin, size, _settings.Color) is { } found)
            ApplyLocation(found);

        _engine.Paused = false;
        _engine.Start();
        if (_start is not null) _start.IsEnabled = false;
        if (_stop is not null) _stop.IsEnabled = true;
        // ADDON-WINS: a fresh session only resets the mirror bookkeeping — no
        // mask is pushed, so the in-game overlay keeps authority (:2689-2691).
        _toggleSync.BeginSession(_settings);
        SetStatus("Engine started.", ControlTheme.Success);
    }

    internal void StopEngine()
    {
        if (_engine is null || !_engine.IsRunning) return;
        _engine.Stop();
        if (_start is not null) _start.IsEnabled = true;
        if (_stop is not null) _stop.IsEnabled = false;
        SetStatus("Engine stopped.", ControlTheme.TextMuted);
    }

    /// <summary>Global pause hotkey (MainForm.WndProc :3232-3234): toggles Paused unconditionally.</summary>
    private void TogglePauseHotkey()
    {
        if (_engine is null) return;
        _engine.Paused = !_engine.Paused;
    }

    /// <summary>v4.2.0: global manual CC-hold hotkey (party CC the addon cannot see).</summary>
    private void ToggleCcHoldHotkey()
    {
        _engine?.ToggleCcHold();
        SetStatus("CC hold toggled (manual party-CC hold).", ControlTheme.Accent);
    }

    /// <summary>v4.2.0: console button for the manual party-CC hold.</summary>
    internal void ToggleCcHoldFromConsole()
    {
        _engine?.ToggleCcHold();
        SetStatus("CC hold toggled (manual party-CC hold).", ControlTheme.Accent);
    }

    /// <summary>Console/Exp pause button (MainForm.TogglePauseFromConsole :1045-1055).</summary>
    internal void TogglePauseFromConsole()
    {
        if (_engine is null) return;
        if (!_engine.IsRunning)
        {
            StartEngine();
            return;
        }
        _engine.Paused = !_engine.Paused;
        SetStatus(
            _engine.Paused ? "Paused." : "Running.",
            _engine.Paused ? ControlTheme.Accent : ControlTheme.Success);
    }

    /// <summary>MainForm :2618-2655, P2-C scope: the 14 toggles + Advanced pages through their models.</summary>
    internal void ApplyToSettings()
    {
        _toggles.ApplyToSettings(_settings);
        _advancedConfig?.ApplyTo(_settings);
        _advancedDiagnostics?.ApplyTo(_settings);
    }

    /// <summary>
    /// MainForm.SaveNow :486-492: persist everything now (no debounce), then
    /// re-register the pause hotkey. Called by the Advanced pages on change.
    /// </summary>
    internal void SaveNow()
    {
        ApplyToSettings();
        SaveSettings();
        RegisterPauseHotkey();
        RegisterCcHoldHotkey();
    }

    /// <summary>MainForm SaveSoon :573-577: coalesce rapid edits (text fields).</summary>
    internal void SaveSoon()
    {
        if (_settingsDebounce is null) { SaveNow(); return; }
        _settingsDebounce.Stop();
        _settingsDebounce.Start();
    }

    private void ApplyLocation(BlockLocation location)
    {
        _settings.OffsetX = location.OffsetX;
        _settings.OffsetY = location.OffsetY;
        _settings.CellSize = location.CellSize;
        _lastLocatedCellSize = location.CellSize; // S8 install-doctor witness (:2882)
        if (_advancedDiagnostics is not null)
        {
            // Keep the strip card editors in step with a located block
            // (:2883-2885); the settings write above is authoritative.
            _advancedDiagnostics.SetLocationValues(location.OffsetX, location.OffsetY, location.CellSize);
        }
    }

    internal static void OpenAppFolder() =>
        System.Diagnostics.Process.Start("explorer.exe", Program.AppDir);

    // ----- launcher (MainForm.LaunchGame :2548-2557) -----

    internal bool LaunchGame()
    {
        ApplyToSettings();
        var path = string.IsNullOrWhiteSpace(_settings.BNetPath) ? null : _settings.BNetPath;
        var (ok, message) = Launcher(path);
        if (ok) SetStatus(message, ControlTheme.TextPrimary);
        else _ = ShowDialogAsync(AppTitle, message);
        return ok;
    }

    /// <summary>Launcher seam (MainForm :3412): tests must never start the real Battle.net.</summary>
    internal Func<string?, (bool Ok, string Message)> Launcher { get; set; } =
        path => BattleNetLauncher.TryLaunch(path, out var message) ? (true, message) : (false, message);

    private void OpenGameFromExp()
    {
        if (string.IsNullOrWhiteSpace(_settings.BNetPath))
            _expConsole?.AppendLog(
                "BNetPath not set - auto-detecting Battle.net (set [Launch] BNetPath in settings.ini to override).",
                ControlTheme.TextMuted);
        // Keep Open Game disabled through the Battle.net → WoW launch gap; the
        // 1 s poll clears it the moment the process appears.
        SetLaunchGrace(20_000);
        UpdateExpTransportState();
        if (LaunchGame()) return;
        ClearLaunchGrace();
        UpdateExpTransportState();
    }

    /// <summary>
    /// 4.4.0 "Wayfinder" Restart Game: confirm, kill the running client (same
    /// session only), then relaunch through the existing Battle.net path.
    /// </summary>
    internal async Task RestartGameAsync()
    {
        if (_restarting) return;
        // Latch BEFORE the confirmation: a second click (or a queued keyboard
        // activation) must not stack a second confirmation behind this one.
        _restarting = true;
        UpdateExpTransportState(); // both transport buttons disable synchronously
        try
        {
            var name = _settings.ProcessName;
            var confirmed = await ShowConfirmAsync(
                "Restart World of Warcraft?",
                $"The running game ('{name}') will be closed immediately and relaunched "
                + "through Battle.net.\n\n"
                + "Closing the client this way can lose this session's SavedVariables "
                + "(the game may not get a chance to write them). Log back in afterwards.",
                "Restart WoW",
                "Cancel");
            if (!confirmed) return;

            // Invalidate any in-flight presence poll: its reading predates the
            // kill and must not re-assert "Game Running" or clear the grace.
            Interlocked.Increment(ref _gamePollEpoch);
            SetStatus($"restarting game - closing {name}...", ControlTheme.Accent);
            var (_, ok, message) = await Task.Run(() => WowWindow.KillGameProcesses(name, 10_000));
            if (!ok && WowWindow.IsProcessRunning(name))
            {
                SetStatus(message, ControlTheme.Danger);
                await ShowDialogAsync(AppTitle, message);
                return;
            }

            // A process that vanished between the confirmation and the kill is
            // still a valid restart: relaunch it instead of reporting an error.
            _gameProcessRunning = false;
            SetLaunchGrace(20_000);
            SetStatus($"{message} - relaunching...", ControlTheme.Accent);
            if (!LaunchGame()) ClearLaunchGrace();
        }
        catch (Exception ex)
        {
            // Never leave the shell in "restarting" with an unobserved task.
            SetStatus($"restart failed - {ex.Message}", ControlTheme.Danger);
        }
        finally
        {
            _restarting = false;
            UpdateExpTransportState();
        }
    }

    // ----- telemetry (MainForm :2727-2832) -----

    /// <summary>Attaches the local rotation-telemetry recorder (Advanced Diagnostics owns the toggle).</summary>
    internal void AttachTelemetryRecorder(TelemetryRecorder? recorder)
    {
        _telemetryRecorder = recorder;
        if (_engine is not null) _engine.Telemetry = recorder;
    }

    /// <summary>
    /// MainForm.SyncTelemetryRecorder :2727-2743: attach/detach the bounded
    /// in-memory recorder; a mid-session enable stamps an "attached" session
    /// marker onto the running engine exactly like the form.
    /// </summary>
    internal void SyncTelemetryRecorder()
    {
        if (_settings.TelemetryEnabled)
        {
            _telemetryRecorder ??= new TelemetryRecorder(_settings.TelemetryCapacity);
            if (_engine is not null) _engine.Telemetry = _telemetryRecorder;
            if (_engine is { IsRunning: true })
                _telemetryRecorder.Append(TelemetryEvent.Session(
                    _engine.ElapsedMs, Native.AppVersion, PixelProtocol.SupportedVersion,
                    _telemetryRecorder.Capacity, recording: true, note: "attached"));
        }
        else if (_engine is not null)
        {
            _engine.Telemetry = null;
        }
        UpdateTelemetryStatus();
    }

    /// <summary>MainForm.UpdateTelemetryStatus :2745-2755.</summary>
    private void UpdateTelemetryStatus()
    {
        var recorder = _telemetryRecorder;
        var on = _settings.TelemetryEnabled;
        var text = recorder switch
        {
            null => "off - enable recording, run the engine, then Export",
            { Count: 0 } => on ? "armed - press Start to capture" : "buffer empty",
            _ => $"{recorder.Count} events buffered ({recorder.Dropped} dropped)" + (on ? " - recording" : " - recording off"),
        };
        _advancedDiagnostics?.SetTelemetryStatus(text);
    }

    internal async Task ExportTelemetryAsync()
    {
        if (_telemetryRecorder is not { Count: > 0 } recorder)
        {
            SetTelemetryStatus("nothing to export - enable recording and Start the engine first");
            return;
        }
        var file = await PickSaveFileAsync("JSONL telemetry", ".jsonl", $"telemetry-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
        if (file is null) return;
        try
        {
            var count = recorder.Export(file.Path);
            SetTelemetryStatus($"exported {count} events -> {file.Path}");
        }
        catch (Exception ex)
        {
            SetTelemetryStatus($"export failed: {ex.Message}");
        }
    }

    internal async Task ReplayTelemetryAsync()
    {
        var file = await PickFileAsync("JSONL telemetry", ".jsonl");
        if (file is null) return;
        try
        {
            var result = ReplayRunner.RunFile(file.Path);
            SetTelemetryStatus($"replayed {result.Ticks} ticks ({result.Errors} errors, {result.Mismatches} mismatches) -> {result.ReportPath}");
        }
        catch (Exception ex)
        {
            SetTelemetryStatus($"replay failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads an exported JSONL session for the suggested-vs-cast audit
    /// (MainForm.LoadCastAudit :2812-2832): the grid lands in the Diagnostics
    /// telemetry card and the summary also goes to the status line.
    /// </summary>
    internal async Task LoadCastAuditAsync()
    {
        var file = await PickFileAsync("JSONL telemetry", ".jsonl");
        if (file is null) return;
        try
        {
            var report = CastAudit.FromFile(file.Path);
            _advancedDiagnostics?.SetCastAudit(report);
            SetTelemetryStatus($"audit: {report.Casts}/{report.Suggestions} plan-head suggestions cast from {file.Name}");
        }
        catch (Exception ex)
        {
            SetTelemetryStatus($"audit load failed: {ex.Message}");
        }
    }

    private void SetTelemetryStatus(string text) =>
        Post(() => _advancedDiagnostics?.SetTelemetryStatus(text));

    // ----- bridge health banner + install doctor (MainForm :2044-2086, :2944-3034) -----

    /// <summary>
    /// Stream 3 bridge-health banner port (MainForm.UpdateBridgeBanner
    /// :2951-3008). Warn-only copy from the frozen <see cref="EngineStatus"/>;
    /// wrapped fail-open — a copy bug must never stall the UI timer.
    /// </summary>
    private void UpdateBridgeBanner(EngineStatus status)
    {
        try
        {
            string title, detail;
            StatusTone tone;

            if (_toggleSync.HasConflict)
            {
                // ADDON-WINS: this badge can only fire after an explicit
                // RequestExplicitPush (auto-push is removed), i.e. a deliberate
                // hand-off to the app that the addon never mirrored.
                (title, tone, detail) = ("Toggle sync: blocked", StatusTone.Warning,
                    "An explicit toggle push was not mirrored by the in-game addon. " +
                    "Run install-addon.ps1 + /reload, then retry.");
            }
            else if (_engine is not { IsRunning: true })
            {
                (title, tone, detail) = ("Bridge health: unknown", StatusTone.Info, "");
            }
            else if (!_gameProcessRunning)
            {
                // 4.4.0: no client at all is not a bridge fault; the engine is
                // simply waiting for the configured process.
                (title, tone, detail) = ("Bridge health: game not running", StatusTone.Info, "");
            }
            else if (!status.GameInForeground)
            {
                // 4.4.0: detection is paused, not broken. Neutral, no detail.
                (title, tone, detail) = ("Bridge health: paused (game in background)", StatusTone.Info, "");
            }
            else
            {
                var sampled = ParseSampledVersion(status.Message) ?? ParseSampledVersion(status.RawSample) ?? 0;
                var counters = MismatchCountersFrom(status);
                var notice = BridgeHealth.VersionSkewNotice(sampled, PixelProtocol.SupportedVersion)
                    ?? BridgeHealth.MismatchNotice(counters.Faults, counters.Samples);

                if (notice is not null)
                {
                    (title, tone, detail) = ("Bridge needs attention", StatusTone.Warning, notice);
                }
                else if (!status.BridgeVisible)
                {
                    (title, tone, detail) = ("Bridge not visible", StatusTone.Warning,
                        "No strip sample is decoding. " + BridgeHealth.RepairHint + " " + BridgeHealth.CalibrateHint);
                }
                else
                {
                    (title, tone, detail) = ("Bridge health: good", StatusTone.Success, "");
                }
            }

            _advancedDiagnostics?.SetBanner(title, tone, detail);
        }
        catch
        {
            // Fail-open: reset to the neutral placeholder, never rethrow.
            _advancedDiagnostics?.SetBanner("Bridge health: unknown", StatusTone.Info, "");
        }
    }

    /// <summary>EngineStatus carries no decode counters; the visible state is the only witness (:3515-3518).</summary>
    private static (int Faults, int Samples) MismatchCountersFrom(EngineStatus status) =>
        status.BridgeVisible
            ? (0, BridgeHealth.MismatchMinSamples)
            : (0, 0);

    /// <summary>First bare "v&lt;digits&gt;" token in the text, or null. Never throws (:3020-3034).</summary>
    private static int? ParseSampledVersion(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        for (var i = 0; i + 1 < text.Length; i++)
        {
            if (text[i] != 'v' && text[i] != 'V') continue;
            var start = i + 1;
            var end = start;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            if (end > start && int.TryParse(text.AsSpan(start, end - start), out var version) && version > 0)
                return version;
        }
        return null;
    }

    /// <summary>
    /// S8 install doctor port (MainForm.RefreshInstallDoctor :2050-2073):
    /// read-only observations of the exe build vs HEAD, configured vs emitted
    /// cell size, app mask vs the Ext3 mirror and the addon version. Warn-only
    /// where data is absent; a copy bug shows a neutral failure line.
    /// </summary>
    internal void RefreshInstallDoctor()
    {
        if (_advancedDiagnostics is null) return;
        try
        {
            var appDir = Program.AppDir;
            var inputs = new DoctorInputs(
                ExeCommit: ThisAssemblyGen.GitCommit,
                HeadCommit: InstallDoctor.TryReadHeadCommit(appDir),
                AppVersion: InstallDoctor.TryReadVersionFile(Path.Combine(appDir, "VERSION.txt")),
                AddonVersion: InstallDoctor.TryReadVersionFile(FindAddonVersionPath(appDir)),
                SettingsCellSize: _settings.CellSize,
                LocatedCellSize: _lastLocatedCellSize,
                AppMask: ToggleSync.BuildMask(_settings),
                MirrorMask: _toggleSync.MirrorMask,
                MirrorValid: _toggleSync.MirrorValid);
            var findings = InstallDoctor.Audit(inputs);
            var issues = findings.Where(f => f.Severity is DoctorSeverity.Warn or DoctorSeverity.Fail).ToList();
            var text = issues.Count == 0
                ? InstallDoctor.Summarize(findings)
                : string.Join("   ", issues.Select(f => $"[{f.Check}] {f.Detail}"));
            _advancedDiagnostics.SetDoctorText(text, issues.Count == 0);
        }
        catch
        {
            _advancedDiagnostics.SetDoctorText("Install doctor failed.", ok: false);
        }
    }

    /// <summary>Locates the shipped addon VERSION.txt from the exe dir upward (:2076-2086).</summary>
    private static string FindAddonVersionPath(string appDir)
    {
        var dir = appDir;
        for (var i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, "addon", "MaxDpsBridge", "VERSION.txt");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return Path.Combine(appDir, "addon", "MaxDpsBridge", "VERSION.txt");
    }

    // ----- calibration (MainForm :2310-2522) -----

    private void SetCalibrating(bool running) =>
        Post(() =>
        {
            _calibrating = running;
            // 4.4.0: Exp owns calibration inside the Details disclosure; the
            // classic Recalibrate button is classic-only.
            if (_settings.InGameConfigMode)
            {
                _expConsole?.SetCalibrating(running);
            }
            else if (_recalibrate is not null)
            {
                _recalibrate.Content = running ? "Cancel" : "Recalibrate";
                _recalibrate.IsEnabled = true;
            }
            if (_start is not null) _start.IsEnabled = !running && _engine is { IsRunning: false };
            // Advanced Diagnostics calibration buttons (old _learnColors/_recalibrateAdv :2324-2327).
            _advancedDiagnostics?.SetCalibrating(running);
        });

    private void SetColorStatus(string text) =>
        Post(() =>
        {
            _advancedDiagnostics?.SetColorStatus(text);
            if (_calibrating || text.StartsWith("Calibrat", StringComparison.Ordinal))
                SetStatus(text, ControlTheme.Accent);
        });

    private void RefreshColorStatus()
    {
        var profile = _settings.Color;
        var text = profile.IsLearned
            ? $"Colors {profile.LearnedAt} - separation {profile.Separation()}"
            : "No color profile - legacy magenta detection.";
        Post(() =>
        {
            _advancedDiagnostics?.SetColorStatus(text);
            _advancedDiagnostics?.SetColorTolerance(profile.Tolerance); // old :2518
        });
    }

    /// <summary>MainForm.ResetColors :2504-2513.</summary>
    internal void ResetColors()
    {
        // 4.1 A2.10: replace the reference with a fresh profile; never mutate
        // the instance a live engine tick may be reading.
        _settings.Color = new ColorProfile();
        SaveSettings();
        RefreshColorStatus();
    }

    internal void RecalibrateFull()
    {
        if (_calThread is { IsAlive: true })
        {
            _calCancel = true;
            return;
        }
        if (_engine is { IsRunning: true })
        {
            _ = ShowDialogAsync(AppTitle,
                "Stop the engine first, then Recalibrate.\n\n"
                + "The calibrate pattern reports Paused, so a running engine would just hold.");
            return;
        }
        ApplyToSettings();
        _calCancel = false;
        SetCalibrating(true);
        _calThread = new Thread(CalibrateWorker) { IsBackground = true, Name = "MaxDpsCompanion.Calibrate" };
        _calThread.Start();
    }

    /// <summary>
    /// Position-only recalibrate (MainForm :2856-2875). The Diagnostics entry
    /// button lands in the Pages pass; the behavior is ported here.
    /// </summary>
    internal void RecalibratePositionOnly()
    {
        ApplyToSettings();
        var location = RotationEngine.Locate(_settings, _settings.Color, out var message);
        if (location is { } found)
        {
            ApplyLocation(found);
            SetStatus(message, ControlTheme.Accent);
            return;
        }

        SetStatus(message, ControlTheme.Danger);
        _ = ShowDialogAsync(AppTitle,
            $"{message}.\n\nCheck that:\n"
            + "  - the addon is loaded ( /mdb status in game )\n"
            + "  - the client is windowed or borderless, not exclusive fullscreen\n"
            + "  - the strip is not covered by another window");
    }

    private void CalibrateWorker()
    {
        if (_engine is null) return;
        var game = new WowWindow();
        bool Cancelled() => _calCancel;
        try
        {
            if (!game.Refresh(_settings.ProcessName) || !game.TryGetClientOrigin(out var origin, out var size))
            {
                SetColorStatus($"Calibration failed - no process named '{_settings.ProcessName}'.");
                return;
            }

            SetColorStatus("Calibrating - starting the pattern in game...");
            if (Cancelled()) return;
            if (!ChatCommander.SendChatCommandSilent(game, "mdb calibrate on"))
            {
                SetColorStatus("Calibration failed - could not focus the game window.");
                return;
            }
            Thread.Sleep(600);
            if (Cancelled()) return;

            BlockLocation? block = BlockLocator.Locate(origin, size, _settings.Color);
            if (Cancelled()) return;
            if (block is not { } found || !PatternVisible(origin, found))
            {
                SetColorStatus("Calibration failed - no pattern on screen. Addon updated? (/reload)");
                PostDialog(
                    "No calibrate pattern on screen. Check in order:\n\n"
                    + "  1. In game, type: /mdb calibrate on - you must see\n"
                    + "     'MDB: calibrate pattern ON'. If not: /reload first.\n"
                    + "  2. Display Mode must be Windowed or Borderless\n"
                    + "     (exclusive Fullscreen is invisible to capture).\n"
                    + "  3. The game must be visible, not covered.\n"
                    + "  4. Then Calibrate colors again.");
                ChatCommander.SendChatCommandSilent(game, "mdb calibrate off");
                return;
            }
            var known = found;

            _settings.OffsetX = known.OffsetX;
            _settings.OffsetY = known.OffsetY;
            _settings.CellSize = known.CellSize;
            // Marshal site parity (:2383 BeginInvoke): the Configuration editors
            // land with the Pages pass, so the located block is surfaced in the
            // status line instead of the (not yet ported) numeric fields.
            Post(() => SetColorStatus($"Block located: offset {known.OffsetX},{known.OffsetY}, cell {known.CellSize}."));

            ColorLearner.LearnResult? result = null;
            var sampledVersion = 0;
            using var sampler = new ScreenSampler();
            SetColorStatus("Calibrating - sampling (keyboard locked, Cancel to stop)...");
            using (new InputLock().Install())
            {
                var lastNote = "";
                Color[] SampleBoth(Point at, int cell)
                {
                    var full = sampler.Sample(at, cell);
                    if (ColorLearner.Classify(full, _settings.Color) >= 0)
                    {
                        sampledVersion = PixelProtocol.SupportedVersion;
                        return full;
                    }
                    var v4 = PixelProtocol.TrimToV4(full);
                    if (ColorLearner.Classify(v4, _settings.Color) >= 0)
                    {
                        sampledVersion = PixelProtocol.SupportedVersionV4;
                        return v4;
                    }
                    var old = PixelProtocol.TrimToV1(full);
                    if (ColorLearner.Classify(old, _settings.Color) >= 0)
                        sampledVersion = PixelProtocol.SupportedVersionV1;
                    return old;
                }
                result = ColorLearner.Learn(
                    SampleBoth,
                    new Point(origin.X + known.OffsetX, origin.Y + known.OffsetY),
                    known.CellSize,
                    _settings.Color,
                    Math.Clamp(_settings.Color.Tolerance, 16, 128),
                    Cancelled,
                    note => { if (note != lastNote) { lastNote = note; SetColorStatus(note); } });
            }
            if (result is not null && sampledVersion != PixelProtocol.SupportedVersion)
            {
                var which = sampledVersion == PixelProtocol.SupportedVersionV1 ? "v1" : "v4";
                SetColorStatus($"Calibrated against the OLD ({which}) addon - /reload + reinstall the addon, then recalibrate.");
                PostDialog(
                    $"Calibration succeeded against the OLD {which} bridge addon.\n\n"
                    + "Your in-game addon is stale: run install-addon.ps1, then\n"
                    + "/reload in game, then Recalibrate again so the current v5\n"
                    + "strip (with the mobility/self-heal slots and combat context)\n"
                    + "is what gets learned.");
            }

            ChatCommander.SendChatCommandSilent(game, "mdb calibrate off", settleMs: 400);

            if (Cancelled())
            {
                SetStatus("Calibration cancelled - pattern turned off.", ControlTheme.TextPrimary);
                SetColorStatus("Calibration cancelled - pattern turned off.");
                return;
            }

            if (result is not { } learned)
            {
                SetStatus("Calibration failed - pattern not stable.", ControlTheme.Danger);
                SetColorStatus("Calibration failed - pattern not stable.");
                PostDialog(
                    "No stable pattern was sampled.\n\nCheck that:\n"
                    + "  - the client is windowed or borderless\n"
                    + "  - the strip is not covered by another window");
                return;
            }

            // 4.1 A2.10: the learned profile is already a fresh instance built
            // by ColorLearner.Build — publish it by reference instead of
            // copying eight fields into the object an engine tick may be using.
            _settings.Color = learned.Profile;
            SaveSettings();
            Post(RefreshColorStatus); // marshal-site parity (:2471 BeginInvoke)
            SetStatus(learned.Note + " - saved.", ControlTheme.TextPrimary);
            SetColorStatus(learned.Note + " - saved.");
        }
        finally
        {
            SetCalibrating(false);
        }
    }

    private bool PatternVisible(Point origin, BlockLocation known)
    {
        try
        {
            using var sampler = new ScreenSampler();
            var at = new Point(origin.X + known.OffsetX, origin.Y + known.OffsetY);
            var full = sampler.Sample(at, known.CellSize);
            if (ColorLearner.Classify(full, _settings.Color) >= 0) return true;
            if (ColorLearner.Classify(PixelProtocol.TrimToV4(full), _settings.Color) >= 0) return true;
            return ColorLearner.Classify(PixelProtocol.TrimToV1(full), _settings.Color) >= 0;
        }
        catch
        {
            return false;
        }
    }

    // ----- Exp readouts (MainForm.UpdateExpReadouts :971-1019) -----

    private void UpdateExpReadouts(EngineStatus status)
    {
        if (_engine is null) return;
        var running = _engine.IsRunning;
        var paused = running && _engine.Paused;
        var processPresent = _gameProcessRunning;
        // 4.4.0 hard gate: while the game is not foreground detection is
        // paused, so the card reads "game in background" (secondary), the pill
        // is hollow and fps is 0 — never a red failure state. When the process
        // is absent this is "waiting for game", not "background".
        var background = running && processPresent && !status.GameInForeground;
        var calibrating = _calibrating;
        var state = calibrating ? "Calibrating"
            : !running ? "Stopped"
            : !processPresent ? "waiting for game"
            : background ? "game in background"
            : paused ? "Paused"
            : "Running";
        // Sample rate = the configured poll cadence; 0 when no frames flow.
        var fps = running && !paused && !calibrating && !background && processPresent
            ? (int)Math.Round(1000.0 / Math.Max(1, _settings.PollIntervalMs)) : 0;
        var ok = running && !paused && !calibrating && !background && processPresent && status.BridgeVisible;
        var glyph = ok ? "\u25CF" : "\u25CB"; // ● live / ○ inactive
        // v4.0.1 hierarchy: the state run carries the semantic tone; the fps
        // run stays white/45 (SetExpPill owns the separator).
        SetExpPill(glyph, state, fps, ToUi(calibrating ? ExpTheme.Accent
            : !running ? ExpTheme.Secondary
            : !processPresent ? ExpTheme.Secondary
            : background ? ExpTheme.Secondary
            : paused ? ExpTheme.Accent
            : status.BridgeVisible ? ExpTheme.Ok : ExpTheme.Danger));

        if (_pauseButton is not null)
        {
            var pausedLabel = _engine.Paused;
            _pauseButton.SetLabel(
                pausedLabel ? "\uE768" : "\uE769",
                pausedLabel ? "Resume" : "Pause",
                pausedLabel ? "\u25B6" : "\u23F8",
                FluentIcons);
            _pauseButton.IsEnabled = running;
        }
        if (_start is not null) _start.IsEnabled = !running && !calibrating;
        if (_stop is not null) _stop.IsEnabled = running;
        // Open/Restart labels, tooltips and enabled state (4.4.0 transport).
        UpdateExpTransportState();
        // Calibration state lives on the Details pill in Exp mode; the classic
        // _recalibrate button is never mounted in this shell.
        _expConsole?.SetCalibrating(calibrating);

        // The mask is the single ADDON-WINS witness: it lives in the collapsed
        // Status card "Details" disclosure (bridge + mask hex + sample rate),
        // never in the always-visible status line or the pill (:999-1004).
        var mask = _toggleSync.EffectiveMask;
        _expConsole?.SetDetails(
            $"bridge {(status.BridgeVisible ? "ok" : "none")} \u00B7 mask 0x{mask:X4} \u00B7 {fps} fps");

        // The bridge warning moved to UpdateHero's BridgeLogGate (once per
        // outage/run, never while the game is in the background).
    }

    // ----- dialogs + pickers (MainForm MessageBox / OpenFileDialog sites) -----

    /// <summary>
    /// Worker-thread dialog post (BeginInvoke parity). WinUI's ContentDialog is
    /// async and unmarshaled differently from MessageBox, so every one of the
    /// seven MessageBox sites uses ContentDialog (no Win32 MessageBox
    /// fallback); the gate only serializes concurrent shows.
    /// </summary>
    private void PostDialog(string message)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            _ = ShowDialogAsync(AppTitle, message);
            return;
        }
        DispatcherQueue.TryEnqueue(() => _ = ShowDialogAsync(AppTitle, message));
    }

    private async Task ShowDialogAsync(string title, string message)
    {
        if (RootGrid.XamlRoot is null) return; // window not shown yet: never block a worker
        await _dialogGate.WaitAsync();
        _openDialogs++;
        try
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot,
            };
            await dialog.ShowAsync();
        }
        catch (Exception)
        {
            // A modal host that refuses to open (closing window, another modal)
            // must never take down the worker or the shell.
        }
        finally
        {
            _openDialogs--;
            _dialogGate.Release();
        }
    }

    /// <summary>
    /// 4.4.0: two-button confirmation (Restart Game). Same dialog gate as
    /// <see cref="ShowDialogAsync"/> so the shell never opens two modals at once.
    /// Returns true only when the primary button was chosen; any failure to
    /// show (closing window, another modal) reads as "not confirmed".
    /// </summary>
    private async Task<bool> ShowConfirmAsync(string title, string message, string primary, string close)
    {
        if (RootGrid.XamlRoot is null) return false;
        await _dialogGate.WaitAsync();
        _openDialogs++;
        try
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = primary,
                CloseButtonText = close,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            _openDialogs--;
            _dialogGate.Release();
        }
    }

    /// <summary>Marshals an action onto the UI thread (InvokeRequired/BeginInvoke parity).</summary>
    private void Post(Action action)
    {
        if (DispatcherQueue.HasThreadAccess) action();
        else DispatcherQueue.TryEnqueue(() => action());
    }

    /// <summary>FileOpenPicker replacement for OpenFileDialog (:2526, :2788, :2814).</summary>
    private async Task<StorageFile?> PickFileAsync(string description, string extension)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add(extension);
        InitializeWithWindow.Initialize(picker, _win32.Handle);
        return await picker.PickSingleFileAsync();
    }

    /// <summary>FileSavePicker replacement for SaveFileDialog (:2766).</summary>
    private async Task<StorageFile?> PickSaveFileAsync(string description, string extension, string suggestedName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName,
        };
        picker.FileTypeChoices.Add(description, new List<string> { extension });
        InitializeWithWindow.Initialize(picker, _win32.Handle);
        return await picker.PickSaveFileAsync();
    }

    /// <summary>Browse for Battle.net.exe (MainForm.BrowseBNet :2524-2546).</summary>
    internal async Task BrowseBattleNetAsync()
    {
        var file = await PickFileAsync("Battle.net", ".exe");
        if (file is null) return;
        _settings.BNetPath = file.Path;
        SaveSettings();
        SetStatus($"Battle.net path: {file.Path}", ControlTheme.TextPrimary);
    }
}

#pragma warning restore CS0436
