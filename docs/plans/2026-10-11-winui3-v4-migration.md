# MaxDps Companion 4.0.0 — WinUI 3 migration plan

Date: 2026-10-11. Route: L (architect spec, Sonnet 5.5). Status: **complete** —
P0-P4 executed; P3 cleanup (legacy WinForms files deleted, publish-exp.ps1) +
full offline bar + dist-exp publish/live smoke, and the P4 doc pass, all
landed (evidence: HANDOVER.md 4.0.0 section).
Directive: replace the WinForms UI shell with WinUI 3 (Windows App SDK), keep all engine
functionality identical (automatic bridge reading, toggles, button pressing), release as **4.0.0**.
The addon (MaxDpsBridgeExp 3.7.9-exp) and wire protocol v5 are UNCHANGED.

## 0. Resolved decisions (router defaults; all renameable/flippable later)

- Codename placeholder: **Lumen**.
- Deployment: **framework-dependent** (requires Windows App SDK 1.8 runtime, same model as the
  existing .NET 8 Desktop Runtime prerequisite). ~37 MB / 43 files / 1 exe.
  Rejected: self-contained (ships `RestartAgent.exe` = second exe, violates one-exe rule, ~136 MB);
  `PublishSingleFile=true` (crashed in the spike, ~275 MB).
- Background: **solid** (no Mica) to preserve the classic look. DWM rounded corners retained.
- Dropped legacy extras: `--ui-snapshot-class-skills`, `--ui-snapshot-scroll`, `ClassSkillsView`
  screen (already unreachable in UI since S6; it was a test seam only).
- User directive supersedes `HANDOVER.md` "do not add work to the C# client". The Rust port
  (`companion-rs/`) is untouched and unaffected.

## 1. Target architecture (single project, one exe)

- Project: `app/MaxDpsCompanion/MaxDpsCompanion.csproj`, TFM `net8.0-windows10.0.19041.0`.
- Unpackaged WinUI 3: `WindowsPackageType=None` **and** `EnableMsixTooling=true` are both
  REQUIRED at publish (without msix tooling, published output lacks the XAML resource index and dies
  `0xC000027B` XamlParseException — proven in spike `C:\Users\Karin\AppData\Local\Temp\opencode\winui-spike`).
  Unpackaged naming: the file is `<TargetName>.pri` (MaxDpsCompanion.pri), NOT `resources.pri` (that
  name needs `AppxPackage=true`).
- Custom entry point: `DISABLE_XAML_GENERATED_MAIN`; `Program.Main` keeps `[STAThread]`, runs all
  headless CLI modes before any XAML starts, then `Application.Start`.
- Engine freeze: RotationEngine, Scheduler/, Knowledge/, Decision/, Telemetry/, PixelProtocol,
  KeySender, ScreenSampler, ToggleSync, AppSettings keep behavior. Permitted edits: usings /
  namespace only. PixelProtocol.cs / KeySender.cs / Scheduler/** / Decision/** / Knowledge/**
  are architect-trigger protected paths — no behavioral edit is planned.
- System.Drawing: `<Using Include="System.Drawing"/>` (csproj) restores the implicit global using
  for engine files; add `System.Drawing.Common` (exact pin after first restore) for
  `Graphics`/`Bitmap` in BlockLocator.cs, ColorLearner.cs, Intelligence/SpellIconCache.cs.
  UI code uses `Windows.UI.Color`; convert via `DesignTokens.ToUi(System.Drawing.Color)`.
- UI logic lives in XAML-free model classes (`ConsoleHomeModel`, `ToggleModel`, `UiScale`);
  models are frozen at the end of P1 and are what tests assert against.
- Popups (Advanced, Abilities) are an in-window scrim overlay `Grid` — not extra windows.

### 1.1 csproj key properties

```xml
<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
<TargetPlatformMinVersion>10.0.19041.0</TargetPlatformMinVersion>
<UseWinUI>true</UseWinUI>
<UseWindowsForms>false</UseWindowsForms>
<WindowsPackageType>None</WindowsPackageType>
<EnableMsixTooling>true</EnableMsixTooling>
<WindowsAppSDKSelfContained>false</WindowsAppSDKSelfContained>
<SelfContained>false</SelfContained>
<PublishSingleFile>false</PublishSingleFile>
<Platforms>x64</Platforms>
<PlatformTarget>x64</PlatformTarget>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<DefineConstants>$(DefineConstants);DISABLE_XAML_GENERATED_MAIN</DefineConstants>
<Version>4.0.0</Version>
<Codename>Lumen</Codename>
<ApplicationManifest>app.manifest</ApplicationManifest>
```

- Remove `ApplicationHighDpiMode` (WinForms-only). Keep `Native.SetProcessDpiAwarenessContext`
  as the first call in `Main` (if WinUI startup objects, drop it and record why).
- Package pins (exact): `Microsoft.WindowsAppSDK` 1.8.260921001,
  `Microsoft.Windows.SDK.BuildTools` 10.0.26100.9169. Do not upgrade mid-migration.
- `app.manifest`: keep PerMonitorV2 + longPathAware; remove WinForms/comctl6 entries.
- Keep embedded Knowledge JSON resources and `assets/icon-256.png` + `assets/companion.ico`
  copy behavior exactly as they are.

### 1.2 Publish (replaces the manual dist-exp publish for the UI client)

New file `tools/publish-exp.ps1`:

```
dotnet publish app/MaxDpsCompanion/MaxDpsCompanion.csproj -c Release -r win-x64 --self-contained false -p:Platform=x64 -p:EnableMsixTooling=true -p:WindowsPackageType=None -p:PublishSingleFile=false -o dist-exp
```

- Must NOT delete `dist-exp\settings.ini`. Writes/updates `dist-exp\VERSION.txt` ("4.0.0 Lumen").
- Asserts: `MaxDpsCompanion.exe` present, `MaxDpsCompanion.pri` present.
- `dist-exp` pre-existing `mdc-app.exe` / `mdc-cli.exe` belong to the Rust workstream — leave them.
- `build.ps1` and `dist\` stay legacy and frozen; do not touch.

## 2. Control map (WinForms -> WinUI 3)

| WinForms | WinUI 3 |
|---|---|
| ChamferButton | `ChamferButton : Button` custom template, `Path` (chamfer polygon), VisualStateManager hover/press/focus |
| TitleBarButton | `Button` + Segoe Fluent Icons glyph |
| WindowChrome (DWM) | P/Invoke `DwmSetWindowAttribute` in `Win32Host`; solid backdrop; rounded corners |
| GradientCanvas | `Grid` + `LinearGradientBrush` |
| RoundedCard / GlassCard | `Border` + CornerRadius (+ AcrylicBrush option) |
| GroupHeader / SettingRow / ToggleRowPanel | `UserControl` over `Grid`/`StackPanel` |
| ToggleSwitch (15 ms anim) | custom `ToggleButton` template, thumb `Composition` offset animation (~150 ms); same IsOn/Toggled API; engine semantics in the model |
| LinkLamp | `Ellipse` + brush binding (+ pulse storyboard) |
| StripView (43 cells) | `ItemsRepeater` + `UniformGridLayout`, 43 `Rectangle`s, updated on the 250 ms tick |
| WheelSafeNumeric | `NumberBox` with wheel handler disabled |
| ClassBadge | `Border` + `TextBlock` |
| ThemedScrollHost | `ScrollViewer` |
| SegmentedTabs | `SelectorBar` (or RadioButtons-style ListView) |
| OwnedComboBox | `ComboBox` |
| OwnedToolTip | `ToolTipService` |
| LegendGrid / KvRow / MetricTile / StatusPill / FilterChip | `UserControl`s |
| ToastHost | `InfoBar` stack / `TeachingTip` |
| UiClickable | `Button` with transparent template |
| VertStack / WrapFlow / GridPanel / BentoSplit | `StackPanel` / `ItemsRepeater`+`UniformGridLayout` / `Grid` / `VisualStateManager` `AdaptiveTrigger` on the 4 `UiScale` tiers |
| Scrim popups | overlay `Grid`: 60% black `Rectangle` + centered card; scrim click and Esc close |
| ClassBrowser / AbilityExplorer / AbilityInspector | `ListView` + `ItemsRepeater` virtualization |
| ScaledIconCache | retained; `BitmapImage` per size from stream |
| CastAuditView / WhyNotFiring(+Panel) / SemanticBanner | `UserControl`s |
| MessageBox | `ContentDialog` (XamlRoot) async |
| OpenFileDialog / SaveFileDialog | `FileOpenPicker`/`FileSavePicker` + `InitializeWithWindow` |
| Screen / Cursor | `DisplayArea` + `GetCursorPos` P/Invoke |
| ClassSkillsView (legacy) | delete; seam logic moves into models |

Note: ToggleSwitch animation in WinForms was 15 ms knob travel; WinUI uses ~150 ms standard —
parity means same toggle semantics (mask bits, ADDON-WINS mirror echo), not the same tween duration.

## 3. Shell window (parity list)

`Ui/Shell/ShellWindow.cs` (Window) + `Ui/Shell/Win32Host.cs` (HWND subclass `SetWindowSubclass`):

- Borderless: `OverlappedPresenter.SetBorderAndTitleBar(false,false)`, resizable true.
- Custom title bar; drag region via `InputNonClientPointerSource.SetRegionRects` (Caption), buttons
  as Passthrough rects; min -> `presenter.Minimize()`; close -> close or hide-to-tray per setting.
- Edge resize: subclass `WM_NCHITTEST`, 8 px DPI-scaled border, HTLEFT/HTTOP/... codes.
- Min size 520x560 via `WM_GETMINMAXINFO` (DPI-scaled); default 660x920.
- Tray: `Shell_NotifyIcon` P/Invoke (`WM_APP+1`), popup menu via `CreatePopupMenu/TrackPopupMenu`
  (Show, Pause, Quit); double-click restores.
- Minimize-to-tray via `AppWindow.Changed` + `ShowWindow(SW_HIDE)`.
- Pause hotkey: `RegisterHotKey` on the HWND; `WM_HOTKEY` enqueued to the UI thread (same pause toggle).
- Esc: `KeyboardAccelerator` on the root grid closes an open overlay.
- DPI: `RasterizationScale` + `XamlRoot.Changed` drive `UiScale` tiers.
- Position/size persistence: debounced `AppWindow.Changed` -> existing `AppSettings` keys
  `WindowWidth/WindowHeight/WindowLayout` in physical pixels, restore clamped to `DisplayArea`.

## 4. Threading

- Engine thread unchanged (background, BelowNormal). It keeps writing `_status` (non-volatile,
  existing benign race — preserved deliberately) and raising `StatusChanged`/`LocationChanged`.
- UI reads on a 250 ms `DispatcherQueueTimer`, no marshaling (same as today's WinForms timer).
- Calibration thread: `InvokeRequired/BeginInvoke` -> `DispatcherQueue.TryEnqueue`.
- The other three WinForms timers -> `DispatcherQueueTimer`.
- `Application.DoEvents`/`Application.Idle` (13 sites): CLI modes run synchronously with no UI;
  UI paths use `await Task.Delay` / queued callbacks.

## 5. Headless CLI fate

| Family | Fate |
|---|---|
| `--ability-audit`, `--ability-coverage`, `--ability-info`, `--ability-search/class/spec`, `--gen-catalog`, `--dump-class-skills`, `--probe`, `--replay`, `--bench-sample/telemetry/scheduler` | KEEP, identical behavior (run before `Application.Start`) |
| `--bench-ui` | PORT to hosted window |
| `--ui-snapshot-page`, `--ui-snapshot*`, `--ui-smoke-test` | PORT via `RenderTargetBitmap.RenderAsync` + `BitmapEncoder`; keep non-blank assertion; `ShellValidation` walks the visual tree |
| `--ui-snapshot-class-skills`, `--ui-snapshot-scroll` | DROP (legacy screen gone) |
| during P0/P1 only | `--ui-*` + `--bench-ui` print "unavailable in this build", exit 3 |

- Unavailable list (permanent): class-skills snapshot, scroll-offset snapshot, `DrawToBitmap`,
  pixel-exact comparison with 3.7.9.
- Keep ui-error.txt + exit code 3 semantics for `--ui-*` failures.

## 6. Tests

- Test csproj: TFM `net8.0-windows10.0.19041.0`, `UseWindowsForms=false`, ProjectReference to app.
- G0 gate: all non-UI tests run against the WinUI project (fallback: compile-include engine sources
  excluding `Ui/**`, `App*.cs`, `MainWindow*`, `MainForm*`, `UiControls*`, `ClassSkillsView*`).
- UI-coupled files (rebuild in P2-E against models, not controls): ClassicUiTests(14),
  UiShellTests(7), ClassBrowserViewTests(4), ConsoleHomeTests(4), ExpModeTests layout half(15),
  UiClassPublishTests(4), ApproachADrillThroughTests(4), ReleaseIdentityTests title assert(1).
- No XAML/STA-in-xunit tests; live UI checks move to `tools/ui_smoke.ps1` (`--ui-smoke-test`,
  exit code + PNG check).
- Engine test count must not shrink below the pre-migration non-UI count.

## 7. Phases (build-green checkpoint after each)

- **P0 — scaffold (one worker):** csproj conversion, manifest, global usings/System.Drawing.Common,
  compile-exclusions for old UI files, new `Program.cs` (kept CLI modes), placeholder window,
  test csproj retarget + exclusions, version bump to 4.0.0, dist-exp publish + smoke.
- **P1 — shell:** App.xaml, Theme.xaml, ShellWindow, Win32Host, TrayIcon + models frozen
  (`ConsoleHomeModel`, `ToggleModel`). Checkpoint: window launches, tray/hotkey/min/close work.
- **P2 — parallel, file-disjoint:**
  - B: primitives (`Ui/Controls/*`), UiPrimitives, UiFoundation, Layout, DesignTokens/ExpTheme parity.
  - C: main screen view (hero card, 14 toggles, buttons, status readouts, Exp layout), ConsoleHome, Pages, SettingsPages, Advanced overlay.
  - D: ClassBrowser, AbilityExplorer, AbilityInspector, CastAudit, WhyNotFiring/banners.
  - E: model test rewrites, snapshot/smoke CLI port, tools/ui_smoke.ps1.
- **P3:** delete `MainForm.cs`, `UiControls.cs`, `ClassSkillsView.cs`; write `tools/publish-exp.ps1`;
  run full offline bar.
- **P4:** final 4.0.0 bump pass; update HANDOVER.md (status/next), ARCHITECTURE.md (pipeline/file map),
  TESTING.md (commands), README build section — same pass (repo rule).

## 8. Acceptance commands

- `dotnet build app/MaxDpsCompanion/MaxDpsCompanion.csproj -c Release` -> 0 warnings / 0 errors.
- `dotnet test -c Release` (from `tests\MaxDpsCompanion.Tests`) -> all green; engine count >= pre-migration non-UI count.
- `lua tests/secret_harness.lua`, `luac -p addon/MaxDpsBridge/*.lua`, `pwsh tools/ability_audit.ps1` -> unchanged.
- `pwsh tools/publish-exp.ps1` -> MaxDpsCompanion.exe + MaxDpsCompanion.pri present; VERSION.txt 4.0.0.
- `dist-exp\MaxDpsCompanion.exe --ui-smoke-test` -> PASS, exit 0 (after P2-E).

## 9. Risks / OWED live checks

Risks: G0 project-reference shape; clean-machine runtime prerequisite (WinAppSDK 1.8);
borderless resize via subclass hit tests; `RenderTargetBitmap` needs a shown window;
tray icon is raw Win32; System.Drawing.Common is Windows-only (fine here).

OWED live (cannot be closed offline): retail run (sampling, key sending, hotkey), tray +
minimize-to-tray, per-monitor DPI across monitors, clean machine with only the runtime,
visual parity review.
