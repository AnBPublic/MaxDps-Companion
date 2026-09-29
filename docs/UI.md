# UI architecture — MaxDps Companion 3.0 (classic)

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
- Remembered `[Window] Width/Height` is honoured **only** when
  `[Window] Layout=classic3`; otherwise the v2 geometry is ignored and the
  layout tag is recorded (persisted on the next save).
- The two `Modes`/`Spells` toggles mirror the Advanced checkboxes (one setting).
- `Esc` closes the topmost popup. `settings.ini` schema is unchanged.

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

- `dotnet test -c Release` (from `tests\MaxDpsCompanion.Tests`): 451 tests.
- `--ui-smoke-test`: shows the form offscreen, selects every popup tab, and
  runs the structural invariants (zero size, text fit, card content, Tab
  reachability, accessible names, sibling overlap). Exit 0 = pass; findings
  make exit 1 and are written to `ui-smoke.txt`.
- `--bench-ui`: 2000× `RefreshStatus` → mean/p95 µs + construct-to-shown ms →
  `bench-ui.txt`. Targets: mean < 300 µs, p95 < 1 ms, startup < 500 ms.
- Snapshots: `--ui-snapshot-page=main|advanced-config|advanced-diag|
  advanced-intel|abilities-class|abilities-explorer` with `--ui-snapshot=`,
  `--ui-snapshot-width=`, `--ui-snapshot-height=`; the review set is 6 pages ×
  {660×920, 520×560} in `dist/ui-snapshots`. Snapshots are design artifacts,
  never behavioral proof; real DPI/live runs stay OWED.

## Rules

1. The UI never owns engine state and never sends input.
2. One canonical ability representation: explorer/inspector/CLI render the same
   `AbilityDefinition` + audit view.
3. Settings schema (`settings.ini` sections/keys) is unchanged.
4. `[Solo] Enabled` default stays 0; `[Intelligence] HpCurve` default stays 1.
