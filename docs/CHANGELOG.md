# Changelog - MaxDPS Companion

Every release adds a new `## <version> <codename>` section at the **top** of
this file, in the same commit as the version bump. The app's Details disclosure
renders the first entry from `app/MaxDpsCompanion/Ui/Foundation/ReleaseNotes.cs`
and `ReleaseNotesTests` fails when this document is missing a section for any
in-app release, so notes cannot drift from the binary.

Format: Keep a Changelog sections (`Added` / `Changed` / `Fixed` / `Removed`)
per release, plus the identity + wire line. The complete pre-4.0 history lives
in [`../VERSION.txt`](../VERSION.txt) and `docs/archive/HANDOVER-history.md`.

## 4.5.0 Voidlight - 2026-10-25

**Companion 4.5.0 "Voidlight" / bridge 4.5.0.** UI-only release, NO wire
change: protocol v5 + Ext2/Ext3/Ext4, 51 cells, every cell byte-identical.
The addon bump is identity-only (TOC / `VERSION.txt` / `MDCB.VERSION` /
`MDCB.CODENAME`).

### Added

- **Changelog in Details.** The Exp console's Details disclosure now renders the
  current release's highlights plus the earlier version line
  (`ReleaseNotes`), scrollable inside the fixed-height disclosure.
  `docs/CHANGELOG.md` is the long form; `ReleaseNotesTests` enforces that both
  are bumped together, and `AGENTS.md` records the rule for every future
  release.
- **Midnight palette.** Void-black ground with a violet undertone, amethyst
  (`#A78BFA`) accent, Sunwell gold (`#F0C05A`) warning/relaunch accent,
  moonlit-silver text. The classic console and the Exp shell now share one
  palette instead of drifting apart.
- **Gradient surfaces.** Window ground (violet-to-void diagonal + aurora wash),
  title bar, transport buttons (role-coloured vertical ramps), ghost buttons,
  the status pill and every card core are gradients; no large flat fills remain.

### Changed

- **Title bar** uses the Midnight gradient with an amethyst hairline, a display
  typeface for the product title, and owner-drawn hover/press washes
  (amethyst on minimize, crimson on close).
- **Start** is based on the deeper `OkFill` green and **Stop** on `DangerFill`
  so light text keeps contrast across the whole gradient.
- **Restart Game** keeps a Sunwell-gold hairline ring (the "Light" action)
  while the app accent stays amethyst.
- **Exp ground** is the shared Midnight gradient instead of a flat terminal
  token.

### Fixed

- **White band above the title bar.** `SetBorderAndTitleBar(false, false)`
  followed by `IsResizable = true` gave the window a ~8 DIP non-client resize
  frame; the default `WM_NCCALCSIZE` handler inset the client area by it and
  DWM painted the top edge in the light system frame colour (`#F3F3F3`).
  `Win32Host` now answers `WM_NCCALCSIZE` (wParam = TRUE) with a full-window
  client area, and DWM is switched to immersive dark mode with the Midnight
  keyline as the border colour. `WS_THICKFRAME` stays, so edge resizing and the
  8 DIP grip are unchanged.
- **Caption drag offset.** The title-bar caption rectangles were computed from
  the client origin but interpreted as window-relative, so they were off by the
  frame thickness; with the frame gone the drag region is exact.

### Details

- Files: `app/MaxDpsCompanion/Ui/Controls/{ControlTheme,ChamferButton,Surfaces}.cs`,
  `Ui/Theme.xaml`, `Ui/Foundation/{ExpTheme,ReleaseNotes}.cs`,
  `Ui/Shell/{ShellWindow.xaml,ShellWindow.Content.cs,ExpConsoleView.cs,ConsoleHomeView.cs,Win32Host.cs}`,
  `MaxDpsCompanion.csproj`, `Native.cs`, addon identity files, `AGENTS.md`,
  `docs/CHANGELOG.md`, `docs/UI.md`, `docs/TESTING.md`, `VERSION.txt`,
  `README.md`, `HANDOVER.md`.
- Windows only. The macOS Avalonia client stays at 4.4.0 "Wayfinder" (no
  Midnight port yet); `app/Version.props` is therefore not bumped.
- Live retail checks stay OWED (`docs/TESTING.md` §3p/§3q).

## 4.4.0 Wayfinder - 2026-10-25

Transport rework, foreground-gated detection, quiet bridge log. No wire change.

- Exp row 2 became `Open Game | Restart Game`; `Open Game` is disabled and
  reads `Game Running` while the configured WoW process exists, `Game
  Starting…` during the launch grace.
- `Restart Game` confirms (SavedVariables warning), kills the process tree and
  relaunches through Battle.net.
- Calibrate and the `Now:` line moved into the collapsed Details disclosure
  (fixed 140 DIP expanded).
- Screen sampling/decoding runs only while the game window is foreground;
  alt-tab pauses detection and resumes silently.
- `BridgeLogGate`: one warning per outage (after ~5 s) and one per run;
  transient capture statuses are filtered out of the Exp/classic log.
- macOS: the same 4.4.0 tag also ships an Apple Silicon `.app` + the same addon.

## 4.3.0 Namesake - 2026-10-18

Naming release. No wire change.

- Addon renamed to **MaxDps Companion Bridge** (`MaxDpsCompanionBridge.toc`,
  `MaxDpsCompanionBridgeDB`, `/mcb` with `/mdb` + `/mdbx` aliases, `MDCB`
  namespace, `InGame/`); the legacy `MaxDpsBridge` is archived under
  `addon/legacy/` and `dist-legacy/`.
- The in-game-config shell became the default (`InGameConfigMode=1`).
- Existing installs migrate SavedVariables and `bindings-cache.wtf` with
  backups.

## 4.2.0 Nightblade - 2026-10-18

CC intelligence. No wire change.

- Auto-eligible CC narrowed to the single-target stun/silence interrupt
  fallback; break-on-damage CC is manual-only.
- 12.1.5 DR ladder applied to the delegated Kidney Shot path.
- Stun discipline: never stun a non-interruptible cast, latch ineffective
  stuns, hold Main/Offensive/Interrupt after break-on-damage CC.
- Manual party-CC hold hotkey (`Ctrl+Shift+C` default) and Console button.

## 4.1.0 Paragon - 2026-10-18

Audit remediation. No wire change.

- Addon hot path: cache no longer wiped per cooldown event, keyed FrameKey,
  per-tick memo, fewer allocations, HotbarWatch debounce, slot fixes.
- Windows engine: single frame decode, immutable snapshots, checked key
  delivery, focus-blocked slot fix, process-scan throttle.
- Shared brain: Windows consumes `MaxDpsCompanion.Core`; the Mac gains Ext4
  decode, the Main master gate and the typing veto.

## 4.0.4 Aegis - 2026-10-17

- The in-game Main toggle is the **master switch**: OFF blanks all eight slots
  and suspends every companion send path; the other toggles keep their stored
  state.
- Overlay profile footer removed (300x210 -> 300x165).

## 4.0.3 Bulwark - 2026-10-11

- Additive Ext4 hotbar-map block (cells 43-50, 51-cell strip, version nibble
  stays 5).
- Hotbar press queue: `HotbarWatch.lua` streams the keybound action-bar map;
  the companion queues recognised presses into the current GCD window.

## 4.0.0 - 4.0.2 Lumen - 2026-10-11

- WinUI 3 desktop client replaces the WinForms shell: double-bezel cards,
  full-pill 42 DIP buttons, in-game-config (Exp) console, fast static scrim.
