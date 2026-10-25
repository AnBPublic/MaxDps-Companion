# 4.4.0 "Wayfinder" — transport rework, foreground-gated detection, quiet bridge log

Status: approved for implementation (architect review 2026-10-25).
Branch: `release/v4.4.0-wayfinder`. Wire protocol: **UNCHANGED** (v5 + Ext2/Ext3/Ext4,
51 cells). Addon change is identity-only (`MDCB.VERSION` / `MDCB.CODENAME` / TOC /
VERSION.txt); no Lua logic, no Catalog, no cell change.

## User requirements

1. Exp transport: row 1 stays `Start | Pause | Stop`; row 2 becomes
   `Open Game | Restart Game` (two buttons). `Calibrate` moves into the collapsed
   **Details** disclosure, hidden by default.
2. `Open Game` is disabled/darkened while the configured WoW process exists and
   its label changes to **`Game Running`**. During the launch gap it reads
   **`Game Starting…`**. `Restart Game` kills the running process and relaunches
   through the existing Battle.net launcher.
3. The always-visible `Now: <action> — <why>` row moves into the Details
   disclosure; the main card no longer shows it.
4. Screen sampling/decoding runs only while the game window is the foreground
   window. Alt-tabbing pauses detection. This is a hard gate (independent of
   `[Window] AllowBackgroundKeys`, which only describes key delivery).
5. The Exp log must not spam `no pixel block` / occluded / minimized lines on
   alt-tab. Once the bridge has been seen, transient capture gaps are silent.
   Warn once after 5 s of continuous foreground-without-frame, and at most once
   per outage / run.
6. Release 4.4.0 "Wayfinder": companion + addon identity, docs, dist publish,
   local addon install, GitHub push + release. Live checks stay OWED.

## Design

### EngineStatus contract (Core)

- `EngineStatus` gains a trailing optional member:
  `bool GameInForeground = true`.
- Windows `RotationEngine.Report` passes its current `_gameInForeground`;
  Core's engine keeps the default (`true`) so the Mac build is untouched.
- The UI reads foreground from the status snapshot, never from a separate
  engine property (no cross-thread tear).

### Foreground gate (Windows `RotationEngine.Tick`)

After `_window.Refresh(...)` succeeds and **before** `TryGetClientOrigin`:

```
_gameInForeground = _window.IsForeground;
if (!_gameInForeground)
{
    _lastFrameMs = _clock.ElapsedMilliseconds;   // no recovery on return
    _idle = true;
    Report(CaptureDiagnostics.BackgroundMessage, _lastVisible, BridgeState.Idle, "-", "-",
           recordTelemetry: false);
    return;
}
```

- `_gameInForeground = false` on Refresh failure and in `Stop()`.
- `_lastVisible` tracks the last reported bridge visibility. `Report` updates
  it for every report except the background branch (which passes it through),
  so `BridgeVisible` stays last-known during a background pause.
- Reset `_lastVisible = false` in `Start()` and when `Refresh` fails.
- `Report` gains `bool recordTelemetry = true`; the background branch passes
  false so the telemetry ring is not filled with paused ticks.
- Keep `public bool GameInForeground => _gameInForeground;` only for tests /
  diagnostics; the UI uses the EngineStatus snapshot.

### Bridge log gate (testable, new file `Ui/Shell/BridgeLogGate.cs`)

Pure state machine, clock injected as `long nowMs`:

- `Reset()`: clears run/seen/warned/outage.
- `Observe(bool bridgeVisible, bool running, bool foreground, long nowMs)`:
  - not running → reset run state, no warn;
  - not foreground → outage clock stops (0), no warn;
  - visible → `EverSeen = true`, outage 0, warned false;
  - invisible + foreground → start outage once; after 5000 ms (measured from
    engine start if never seen, else from outage start) return true exactly
    once; recovery re-arms the next outage.
- The Exp shell appends the existing `BridgeHealth.RepairHint + CalibrateHint`
  warning only when `Observe` returns true. The old `_expBridgeHintShown`
  latch is deleted.

### Transient status filter

Add constants + `IsTransientStatus(string)` to `CaptureDiagnostics`:

- `BackgroundMessage = "game in background - detection paused"`
- `NoBlockMessage = "no pixel block - client must be windowed or borderless"`
- `ReattachMessage = "re-attaching to the game window"`
- existing `MinimizedMessage`, `BlackFrameMessage`
- `NoClientAreaMessage`, `WindowLostMessage`

These (plus `game window is minimized`) never reach the Exp/classic log.
The filter updates the last-status tracker even when suppressed (so A → transient
→ A does not re-log A). Non-transient status lines still log on change. Do not
blanket-dedupe `AppendLog`: legitimate repeated messages must survive.

### Process helpers (`WowWindow`)

- `internal static bool IsProcessRunning(string processName)` — exact
  `GetProcessesByName` after normalising `.exe`; disposes every Process.
- `internal static (int Killed, bool Ok, string Message) KillGameProcesses(
  string processName, int waitMs)`:
  - refuse empty/whitespace;
  - refuse the companion's own PID / process name;
  - same `SessionId` only;
  - `Kill(entireProcessTree: true)`, catch access denied/already exited,
    bounded wait;
  - return a human-readable message.

### Shell process polling + transport state

- `System.Threading.Timer` (1 s) in `ShellWindow.Engine`, started in
  `InitializeEngine` (initial synchronous poll first) and disposed in
  `OnClosed`; callback writes `volatile bool _gameProcessRunning`.
- `_launchGraceUntilMs` = 20 s after Open/Restart launch; clears as soon as the
  process is observed. `GameBlocked = _gameProcessRunning || now < grace`.
- Open Game: `IsEnabled = !GameBlocked && !_restarting`;
  label/glyph/tooltip:
  - running → `Game Running` (Fluent lock glyph `\uE72E`), disabled;
  - starting gap → `Game Starting…` (Fluent refresh `\uE72C`), disabled;
  - idle → `Open Game` (`\u2922`), enabled.
  UIA HelpText + tooltip follow the same state.
- Restart Game: ghost/accent, `\uE72C` + accessible `↻`, text `Restart Game`,
  enabled only while the process runs and no restart is in flight.
- `RestartGameAsync`:
  1. confirm with a ContentDialog (Primary `Restart WoW`, Close `Cancel`);
     copy states that killing loses the current session's SavedVariables;
  2. `_restarting = true`, disable both buttons synchronously;
  3. status/log `restarting game - closing <name>...`;
  4. `Task.Run` the kill (10 s wait);
  5. on failure: status + dialog, clear `_restarting`, refresh;
  6. on success: cache false, set 20 s grace, status/log, `LaunchGame()`;
  7. clear grace when launch fails; `_restarting = false`; refresh.
- `LaunchGame()` returns `bool` so the restart path can clear the grace.
- Classic `_launchGame` is also disabled while `GameBlocked` (text unchanged).

### Exp status card (`ExpConsoleView`)

- Grid: eyebrow, state, divider, Details, divider, LOG header, log.
  The always-visible Now row and its two dividers are removed.
- Details body (collapsed by default, `Visibility.Collapsed`):
  1. `_detailsValue` (bridge / mask / fps, mono, 1 line);
  2. `_nowValue` (`Now: …`, wrap, `MaxLines = 2`, ellipsis, tooltip full text);
  3. compact `ChamferButton` `◉ Calibrate` (32 DIP, ghost, accessible name)
     raising `CalibrateRequested`.
- Fixed heights: collapsed 24, expanded **140** (no Auto height on the timer
  path; wrapping Now cannot re-lay out the log). Motion.Reveal on expand.
- `SetCalibrating(bool)` relabels the details button (`Cancel` / `Calibrate`);
  `SetNow`/`SetDetails` stay value-only with change guards.
- `ShellWindow.Content` wires `_expConsole.CalibrateRequested += RecalibrateFull`
  and stops mounting `_recalibrate` in the Exp transport.
- `SetCalibrating`/`UpdateExpReadouts` route Exp calibration state to
  `_expConsole`; `_recalibrate` is classic-only.

### Exp state / Now copy

- `UpdateHero`/`UpdateExpReadouts` use `status.GameInForeground`:
  - state `game in background` (secondary tone) when running + not foreground;
  - pill hollow, fps 0 when background;
  - Now in Details shows `Now: idle — game in background - detection paused`;
  - bridge banner shows a neutral "paused (game in background)" instead of the
    red "Bridge not visible";
  - status-line tone stays neutral (not danger) while background.
- When foreground resumes, normal state/Now/bridge copy returns without a log
  line.

### Tests

- `WowWindowProcessTests`: current process name → true; GUID name → false;
  `.exe` suffix normalisation; empty kill refused; killing the companion's own
  name is refused (no real process harmed).
- `BridgeLogGateTests`: never-seen warning only after 5 s; once per run;
  seen → outage → once; recovery re-arms; background never warns; reset.
- `CaptureDiagnosticsTests` (extend): every transient constant is filtered and
  normal status strings are not.
- `ReleaseIdentityTests`: 4.4.0 / `v4.4.0 Wayfinder` / title
  `MaxDPS Companion v4.4.0 Wayfinder`.

## Release identity (4.4.0 "Wayfinder")

- `app/MaxDpsCompanion/MaxDpsCompanion.csproj` Version/Codename
- `app/Version.props`
- `app/MaxDpsCompanion/Native.cs` `CodenameFallback`
- addon TOC Version/X-Codename
- `addon/MaxDpsCompanionBridge/Bridge.lua:79,83` MDCB.VERSION/CODENAME
- `addon/MaxDpsCompanionBridge/InGame/Core.lua:24-25` defaults
- addon `VERSION.txt` + repo `VERSION.txt` top entry
- README banner/history/build text; HANDOVER status + 4.4.0 entry;
  docs/UI.md Exp transport; docs/TESTING.md §3p; ARCHITECTURE UI/file-map note.

## Acceptance

Offline (must quote commands):

```
dotnet build app/MaxDpsCompanion/MaxDpsCompanion.csproj -c Release   # 0 warnings / 0 errors
dotnet test -c Release                                              # from tests/MaxDpsCompanion.Tests
lua tests/secret_harness.lua
luac -p addon/MaxDpsCompanionBridge/*.lua addon/MaxDpsCompanionBridge/InGame/*.lua
pwsh tools/ability_audit.ps1
pwsh tools/publish.ps1                                              # after closing the running 4.3.0 app
dist\MaxDpsCompanion.exe --ui-smoke-test                            # PASS, title 4.4.0 Wayfinder
```

Live (OWED, retail): Open Game disabled/label while WoW runs; Game Starting gap;
Restart confirmation + kill + relaunch; Calibrate inside Details; Now inside
Details; alt-tab pauses detection and logs no capture spam; never-established
bridge warns once after ~5 s; foreground return resumes silently.
