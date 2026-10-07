# UI architecture — MaxDps Companion 3.7.7 "Gallant" (classic)

The companion is one WinForms application (`MaxDpsCompanion.exe`, .NET 8,
x64, PerMonitorV2). v3.0.0 restored a fast, fixed **classic** shell on top of
all v2.8.1 functionality: the main window is a single, non-scrolling frame and
every former page now lives in a tabbed popup. The engine is untouched by the
UI: the shell reads engine status and writes only through the existing
settings/policy storage.

## Main window (A3)

```
MainForm (borderless; 2px ring red stopped / green running; tray; pause hotkey)
  ├─ title bar 48px
  └─ GradientCanvas
       ├─ classic body (TableLayoutPanel, fixed absolute rows — NO AutoScroll)
       │    ├─ hero RoundedCard (592px):
       │    │    status 42  LinkLamp · state word · ClassBadge
       │    │    live   42  "Now: <action> — <why>" (ellipsis + tooltip)
       │    │    strip  40  StripView (live sampled cells)
       │    │    "Spells" header + 4 two-toggle rows
       │    │      Main|Offensive  Defensive|Interrupt
       │    │      Self-heal|Mobility  Consumable|Trinket
       │    │    "Modes" header + 2 two-toggle rows
       │    │      Solo|Out of combat  Auto-target|Auto-interact
       │    ├─ button row 1 (52): Start | Stop | Launch Game
       │    ├─ button row 2 (46): Recalibrate | Abilities… | Advanced… | Open Folder
       │    └─ status line (26)
       ├─ Advanced scrim + centred card (tabs Configuration|Diagnostics|Intelligence)
       └─ Abilities scrim + centred card (tabs Class skills|Explorer)
```

- Default client **660 × min(content, working area)**; `MinimumSize` **520×560**.
  The client height is the measured content height (no fixed 920, no empty
  zone below the buttons); `FitToScreen` only caps it to the working area.
- Remembered `[Window] Width/Height` is honoured **only** when
  `[Window] Layout=classic3`; otherwise the v2 geometry is ignored and the
  layout tag is recorded (persisted on the next save).
- The two `Modes`/`Spells` toggles mirror the Advanced checkboxes (one setting).
- `Esc` closes the topmost popup. `settings.ini` schema is unchanged.

## Width tiers (D5)

The client width selects one tier; every tier moves the whole type hierarchy by
one shared step (system Segoe UI Variable chain only, no bundled faces) and
re-measures row heights, card padding, button heights and toggle size. Applied
to the main window **and** both popups:

| Client width | Tier | Font step | Base | Row | Toggle |
| :--- | :--- | :--- | :--- | :--- | :--- |
| ≤ 560 | compact | −1 pt | 9 | 56 | 46×26 |
| ≤ 700 | classic | 0 pt | 10 | 66 | 52×30 |
| ≤ 950 | roomy | +1 pt | 11 | 74 | 58×34 |
| > 950 | wide | +2 pt | 12 | 82 | 64×38 |

The tier is recomputed on a **120 ms debounced** resize only — never while the
user drags — so no layout runs mid-drag. Each control's *own* designed size is
captured once and re-applied with the step (body/meta hierarchy preserved).

## Motion (D6)

- A popup opens synchronously (config/diagnostics built once, lazily) and then
  runs exactly **one** fade of ≤120 ms driven by a single WinForms timer; the
  UI thread is never blocked (no sleep, no nested message loop).
- The fade is skipped entirely while the engine is running — the scrim is
  applied instantly so a live rotation never competes for the UI thread.
- No chained animations: `ClassSkillsView` settles instantly when reached
  through the popup, so the popup's single scrim fade is the only motion.
- Reopening the Abilities popup with an unchanged class/spec reuses the built
  row tree instead of recreating every row and window handle.
- `UiClickable` (the v2.8.1 real mouse-message click fix) is unchanged.

## Advanced popup (A4)

Dimmed scrim, centred card `width=min(client−40, 620)`, `height=client−60`,
header with Back. Each tab is one `Panel{AutoScroll=true}` of fixed-height
`RuleSection`s, **built lazily once** on first open (never by a timer). It
hosts every v2.8.1 control/readout:

| v2.8.1 location | v3 classic home |
| :--- | :--- |
| Home status / live action / identity | hero status row + live row + ClassBadge |
| Home coverage / patch | Intelligence tab (metric tiles + provenance) |
| Home Start/Stop | button row 1 |
| Configuration · Automation (scheduler, intelligence) | Configuration tab |
| Configuration · Combat slots (8 toggles) | Configuration tab |
| Configuration · Safety (Solo, combat-only) | Configuration tab |
| Configuration · Targeting (auto-target/interact, keys) | Configuration tab |
| Configuration · Input (pause hotkey, background keys) | Configuration tab |
| Configuration · Bridge (process name) | Configuration tab |
| Configuration · Advanced (Abilities…, Open Folder) | Configuration tab |
| Diagnostics · Protocol/bridge (6 readouts) | Diagnostics tab |
| Diagnostics · Telemetry (toggle, Export, Replay, status) | Diagnostics tab |
| Diagnostics · Calibration (calibrate, reset, find strip, tolerance, status) | Diagnostics tab |
| Diagnostics · Timing (poll, key hold, min gap) | Diagnostics tab |
| Diagnostics · Strip (cell size, offset X/Y) | Diagnostics tab |
| Diagnostics · Patch/registry audit | Diagnostics tab |
| Diagnostics · Battle.net (path, Browse, Launch, Open Folder) | Diagnostics tab |
| Intelligence (13 metric tiles, proportion bar, patch/catalog) | Intelligence tab |
| Class skills (full-screen overlay) | Abilities → Class skills tab |
| Ability explorer + inspector | Abilities → Explorer tab |

## Abilities popup (A5)

Full-frame overlay (card width `min(client−40, 900)`), tabs Class skills
(`ClassSkillsView`) | Explorer (`AbilityExplorer`). Toggles keep writing through
the existing `AbilityPolicy`. The explorer keeps its inspector split; at narrow
widths the list column dominates.

## Performance rules (A6)

- No `PerformLayout`/`Add`/`Remove` on timer paths; text setters assign only on
  change; no per-character measuring.
- **No `AutoScrollPosition` writes on any timer path** — pinned by
  `ClassicUiTests.ClassicUi_ScrollSurvivesRefresh`.
- Advanced tab content is built once, lazily, on first open.
- `UiFonts` + embedded Geist resources removed; `DesignTokens.FamilyName` uses
  the system variable-font chain.
- `BentoSplit`/`GlassCard`/`KvRow`/`StackPage`/`AppShell` survive only because
  the tabbed pages (Configuration/Diagnostics/Intelligence/Explorer) still use
  them; deleting them is deferred to integration (see Deviations).

## Verification

- `dotnet test -c Release` (from `tests\MaxDpsCompanion.Tests`): 496 tests.
- `--ui-smoke-test`: shows the form offscreen, selects every popup tab, and
  runs the structural invariants (zero size, text fit, card content, Tab
  reachability, accessible names, sibling overlap). Exit 0 = pass; findings
  make exit 1 and are written to `ui-smoke.txt`.
- `--bench-ui`: 2000× `RefreshStatus` → mean/p95 µs + construct-to-shown ms →
  `bench-ui.txt`. Targets: mean < 300 µs, p95 < 1 ms, startup < 500 ms.
- Snapshots: `--ui-snapshot-page=main|advanced-config|advanced-diag|
  advanced-intel|abilities-class|abilities-explorer` with `--ui-snapshot=`,
  `--ui-snapshot-width=`, `--ui-snapshot-height=`. The review set is 6 pages ×
  {660×920, 520×560} plus `main` and `abilities-explorer` at 900×1100 and
  1100×1100 (16 files). The requested width tier is applied and the requested
  height is kept. Snapshots are design artifacts, never behavioral proof; real
  DPI/live runs stay OWED.

## Rules

1. The UI never owns engine state and never sends input.
2. One canonical ability representation: explorer/inspector/CLI render the same
   `AbilityDefinition` + audit view.
3. Settings schema (`settings.ini` sections/keys) is unchanged.
4. `[Solo] Enabled` default stays 0; `[Intelligence] HpCurve` default stays 1.
