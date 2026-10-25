# UI architecture - MaxDps Companion 3.7.9 "Unyielding" (classic)

> **Note (4.5.0 "Voidlight"):** the layout notes below describe the frozen
> 3.7.9 WinForms shell. The current Windows client is WinUI 3
> (`app/MaxDpsCompanion/Ui/`); its theme, palette and gradients are documented
> in the next section, `ARCHITECTURE.md` and
> `docs/plans/2026-10-25-midnight-ui-4.5.0.md`.

## 4.5.0 "Voidlight" theme (WinUI 3)

One palette for both shells. `Ui/Controls/ControlTheme.cs` is the code source of
truth; `Ui/Theme.xaml` mirrors the same values for XAML. `Ui/Foundation/ExpTheme.cs`
holds the in-game-config (Exp) tokens and derives from the same family.

| Token | Hex | Role |
| :--- | :--- | :--- |
| `Background` | `#0A0714` | void-black ground, violet undertone |
| `Surface` / `SurfaceElevated` | `#141022` / `#1D1735` | cards / raised rows |
| `Border` | `#2B2244` | quiet keyline |
| `TextPrimary` / `Secondary` / `Muted` | `#F3EEFB` / `#B3A9CB` / `#7C7294` | moonlit silver type scale |
| `Accent` / `AccentStrong` / `AccentSoft` | `#A78BFA` / `#6D38E0` / `#DDD0FF` | amethyst (focus, ticks, eyebrows) |
| `Success` | `#4FD6A8` | running / Start fill base (`ExpTheme.OkFill` `#2E9E75`) |
| `Warning` | `#F0C05A` | Sunwell gold (Restart ring, warnings) |
| `Danger` | `#D9455E` | Stop fill base (`ExpTheme.DangerFill` `#C9405A`) |
| `Info` | `#8CB8FF` | arcane blue (spec/log info) |

Gradients (all built in `ControlTheme`, no flat large surfaces):

- **Ground** - `Ground()`: 110 deg `#171029 -> #0A0714 -> #1A1030`;
  `GroundAurora()` adds a 25 deg `#3A1F73` wash in the upper-left. Both shells
  paint it (`GradientCanvas`); `ConsoleHomeView` paints the same ramp opaquely
  because it sits over the classic body.
- **Title bar** - vertical `#1B1333 -> #110C1F` with a 1 px amethyst hairline
  (`AccentRing`, 22 % alpha).
- **Pills** - `Pill(fill, lift)`: top `Lighten(fill, 10 %)`, bottom
  `Darken(fill, 28 %)`; hover lifts 6 %, press darkens 10 %. Ghost pills use
  `GhostPill(surface, hover)` (surface lifted toward white at the top).
- **Cards** - `RoundedCard` core: `Lighten(fill, 6 %) -> Darken(fill, 12 %)`
  inside the white/4 bezel; the top-edge highlight border is unchanged.
- **Status pill** - `Lighten(ExpTheme.Card, 6 %) -> ExpTheme.Card` with an
  amethyst ring.

Chrome geometry: `WM_NCCALCSIZE` is answered with a full-window client area, so
there is **no** non-client resize frame band; `WS_THICKFRAME` + `WM_NCHITTEST`
still provide edge resizing and the caption rects are window-relative. DWM runs
in immersive dark mode and paints its outline in `#2B2244`.

## 4.5.1 responsive rules (WinUI 3)

- **Width tiers** (`UiScale`, unchanged): Compact <= 560, Classic <= 700,
  Roomy <= 950, Wide above. `ApplyTier` fires on `RootGrid.SizeChanged` and
  drives type, toggle size, button height (42 -> 52 DIP) and button padding.
- **Toggle rows** (`ResponsiveColumns`, `StackWidth` 500): one column below
  500 DIP, two equal columns above.
- **Console body**: two cards side by side above 560 DIP (58/42), two equal star
  rows below; the body scrolls when the stacked cards need more height.
- **Exp Details**: opens at 252 DIP, re-clamped on every resize to
  `cardInner - 164` (log keeps its reserve). The disclosure body is the only
  scroller and shows a slim amethyst thumb while the content does not fit.
- **Logs**: `RollingLogView` wraps every entry (fixed `HH:mm:ss` column +
  wrapping body); classic console logs are capped at 220/180 DIP.

Release notes: the Exp **Details** disclosure renders
`Ui/Foundation/ReleaseNotes.cs` (current release highlights + earlier-version
line) at a 244 DIP open height clamped to keep a 220 DIP log reserve; the
disclosure body is the only scroller, so small windows scroll instead of
clipping. `docs/CHANGELOG.md` is the long form and `ReleaseNotesTests` keeps
them in sync with the assembly version.

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
| Configuration · Input (pause hotkey, background keys, hotbar keypress queue) | Configuration tab |
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

## Exp (in-game-config) shell — 4.4.0 transport

The in-game-config shell (`InGameConfigMode=1`, the default since 4.3.0) is a
WinUI 3 surface, not the classic WinForms layout described above (that section
describes the `InGameConfigMode=0` shell). 4.4.0 reworks only its transport
card and its detection gating; the wire is untouched.

```
Exp transport card
  ├─ eyebrow + state word (+ state pill)
  ├─ divider
  ├─ Details disclosure (collapsed by default; 24 DIP / 140 DIP expanded)
  │    ├─ details value   bridge · mask · fps (mono, 1 line)
  │    ├─ Now value       "Now: <action> — <why>" (2 lines max, ellipsis, tooltip)
  │    └─ ◉ Calibrate     compact ghost button (label flips to Cancel while calibrating)
  ├─ divider
  └─ LOG header + log
```

- **Row 1** stays `Start | Pause | Stop`. **Row 2** is `Open Game | Restart Game`.
- `Open Game` is `IsEnabled=false` while the configured WoW process exists and
  its label reads **`Game Running`** (lock glyph); during the 20 s launch grace
  after a launch it reads **`Game Starting…`** (refresh glyph) and stays
  disabled; idle it reads `Open Game` and is enabled. UIA HelpText and the
  tooltip follow the same three states.
- `Restart Game` is enabled only while the process runs and no restart is in
  flight. It confirms with a dialog (`Restart WoW` / `Cancel`) that states the
  kill loses the current session's SavedVariables, disables both buttons, kills
  the process tree (bounded 10 s wait, companion's own process refused), then
  relaunches through the configured Battle.net launcher.
- The old always-visible Now row is gone from the main card; the card is state +
  log only. Details keeps a **fixed** expanded height (140 DIP) so a wrapping
  Now line can never re-lay out the log on a timer path; expansion uses
  `Motion.Reveal`.
- **Foreground-gated detection:** state reads `game in background` (secondary
  tone, hollow pill, fps 0) and detection pauses while another window is
  foreground; the Now line reads
  `Now: idle — game in background - detection paused` and the bridge banner goes
  neutral (`paused (game in background)`) instead of red. Detection resumes
  silently when WoW returns to the foreground — no log line, no capture-spam.
- The classic shell keeps its own `Recalibrate` button and Now row; in the Exp
  shell `CalibrateRequested` from the details disclosure is the only entry point.

## Rules

1. The UI never owns engine state and never sends input.
2. One canonical ability representation: explorer/inspector/CLI render the same
   `AbilityDefinition` + audit view.
3. Settings schema (`settings.ini` sections/keys) is unchanged.
4. `[Solo] Enabled` default stays 0; `[Intelligence] HpCurve` default stays 1.
