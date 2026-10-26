# Changelog - MaxDPS Companion

Every release adds a new `## <version> <codename>` section at the **top** of
this file, in the same commit as the version bump. The app's Details disclosure
renders the first entry from `app/MaxDpsCompanion/Ui/Foundation/ReleaseNotes.cs`
and `ReleaseNotesTests` fails when this document is missing a section for any
in-app release, so notes cannot drift from the binary.

Format: Keep a Changelog sections (`Added` / `Changed` / `Fixed` / `Removed`)
per release, plus the identity + wire line. The complete pre-4.0 history lives
in [`../VERSION.txt`](../VERSION.txt) and `docs/archive/HANDOVER-history.md`.

## 4.5.5 Voidlight - 2026-10-26

**Companion 4.5.5 "Voidlight" / bridge 4.5.1 (unchanged).** Diagnosis
correction + GW2_UI compatibility hotfix packaging, NO wire change and NO
rotation change.

### Fixed

- **Corrects the 4.5.4 stack split.** The 3,619
  `ActionButton.lua:847 SetCooldown ... Secret values are only allowed during
  untainted execution` copies came from Blizzard's shared
  `ActionBarButtonEventsFrame` (`ActionButton.lua:967 OnEvent` -> `:223`) ->
  `ActionButton_UpdateCooldown` (842) -> `ApplyCooldown` (866) ->
  `SetCooldown` (847) on `OverrideActionBarButton2`. The assisted-combat frame
  was the second path (`:1880 OnUpdate` -> ... -> `SetAttribute`, one
  `ADDON_ACTION_BLOCKED`). Both follow from a tainted override button; the
  companion's key path is not the cause.
- **The root was GW2_UI.** `Core/API/utils.lua` `normPetAndOverride` installs
  `OverrideActionBar:HookScript("OnShow"/"OnHide", ...)` and calls `Hide()` on
  the protected bar from tainted code in combat; its extra-action
  `AddFrame` hook also calls `ActionButton_UpdateCooldown(button)`. The
  bridge guard hides the assisted-combat frame (stops that cascade), but no
  addon can untaint an already-poisoned button.

### Added

- **`tools/patch-gw2ui-taint.ps1`** (attached to the release): comments out
  both GW2_UI paths, keeps `*.maxdps-backup-4.5.4` backups, verifies `luac -p`,
  is idempotent and supports `-Revert`. Run it after a GW2_UI update, then
  `/reload`.
- The local GW2_UI install was patched on 2026-10-26; the game needs a reload.

### Details

- No engine, scheduler, key-delivery, protocol or addon change: bridge stays
  **4.5.1**, strip stays 51 cells / protocol v5 + Ext2/Ext3/Ext4.
- `docs/TESTING.md` §3u step 0 is the GW2_UI hotfix; live confirmation stays
  OWED.
- The in-app changelog is corrected here so a future GW2_UI update does not
  lose the root-cause note.

## 4.5.4 Voidlight - 2026-10-26

**Companion 4.5.4 "Voidlight" / bridge 4.5.1.** Bridge-only fix for the
in-game secret-cooldown error flood, NO wire change and NO rotation change.

### Fixed

- **The thousands of `ActionButton.lua:847 SetCooldown ... Secret values are
  only allowed during untainted execution` errors stop.** The 3,619 copies in
  the live session came from Blizzard's shared
  `ActionBarButtonEventsFrame` (`ActionButton.lua:967 OnEvent` -> `:223`) ->
  `ActionButton_UpdateCooldown` (842) -> `ApplyCooldown` (866) ->
  `SetCooldown` (847) on `OverrideActionBarButton2`. A second path was the
  assisted-combat frame's `OnUpdate` (`ActionButton.lua:1880`) ->
  `OnActionBarSlotChanged` -> `Update` -> `UpdatePressAndHoldAction` ->
  `SetAttribute` (one `ADDON_ACTION_BLOCKED`). Both are consequences of a
  tainted override button; the target button/store does not come from the
  companion's key path.
- **Bridge 4.5.1 adds `SecretGuard.lua`.** While cooldowns are secret it hides
  the assisted-combat frame on override/vehicle/extra buttons, and on any
  button where the client's `issecurevariable` names a taint source. WoW does
  not run `OnUpdate` on hidden frames, so the assisted-frame tick and its
  cascade stop. Clean main/multi-bar buttons are left untouched, the
  `C_AssistedCombat` API still returns the next spell, and the companion keeps
  pressing normally. Hiding cannot untaint an already-poisoned button, so the
  shared-frame `SetCooldown` path also needs the GW2_UI hotfix below.
- **The taint source is named.** `/mcb guard` prints the secret predicate,
  `assistedCombatIconUpdateRate`, every assisted frame, its parent button and
  the addon the client blames; `/mcb guard off` restores the stock
  Single-Button Assistant refresh.

### Details

- Addon: `SecretGuard.lua` (new, loaded after `Bars.lua`), `TaintGuard = true`
  default in `Bridge.lua`, `/mcb guard [on|off]`, TOC/bridge version **4.5.1**.
- No engine, scheduler, key-delivery or protocol change: the strip stays
  51 cells, protocol v5 + Ext2/Ext3/Ext4, and the companion decodes it
  unchanged. The guard only touches Blizzard's assisted-combat UI frame, never
  the action buttons themselves.
- Harness: `lua tests/secret_harness.lua` - 415 passed / 0 failed (6 new
  guard cases: clean main bar untouched, override pause, tainted main pause,
  not-secret no-op, full sweep hide/leave).
- The underlying taint is a UI-addon issue (the same session's
  `ADDON_ACTION_BLOCKED` names **GW2_UI**); stopping the flood does not
  untaint that addon. If override-bar buttons still misbehave, update or
  disable the named bar addon. Live confirmation of the guard stays OWED
  (`docs/TESTING.md` §3u).

> **Follow-up (2026-10-26, same release).** The guard stops the
> assisted-combat frame's own tick/cascade, but the shared
> `ActionBarButtonEventsFrame` `ACTIONBAR_UPDATE_COOLDOWN` path is a second
> route: once the parent button itself is tainted, Blizzard's handler keeps
> throwing until `/reload`, and no addon can untaint it. The root in this setup
> was GW2_UI's `OverrideActionBar:HookScript("OnShow"/"OnHide", ...)` (it calls
> `Hide()` on the protected bar in combat) plus its extra-action
> `ActionButton_UpdateCooldown` call. `tools/patch-gw2ui-taint.ps1` comments
> both out, keeps `*.maxdps-backup-4.5.4` backups, and is idempotent. Run it
> once after a GW2_UI update, then `/reload`. This is a third-party
> compatibility patch, not part of the addon zip.

## 4.5.3 Voidlight - 2026-10-26

**Companion 4.5.3 "Voidlight" / bridge 4.5.0 (unchanged).** Diagnostics and
visibility, NO wire change and NO addon change.

### Added

- **The addon's toggles are now communicated.** The 14-bit ADDON-WINS mirror is
  logged the first time it is seen, on every change, and when it stops:
  `[toggles] Main ON · all others ON`, `[toggles] Main OFF - rotation suspended
  (turn it back on: /mcb t Main on)`, `[toggles] Trinket OFF, Defensive ON`.
  The Exp Details disclosure carries the same summary as a live line
  (`toggles: Main ON · OFF: Trinket`).

### Fixed

- **A hold is no longer disguised as "waiting for suggestion".** When the engine
  held every send path, the status card still showed the green "waiting for
  suggestion" line, which read like a broken companion. The card (and the
  classic status line) now shows `holding - <reason>` in the hold tone.
- **`Main off in game`** is called out by name, with the real fix: since 4.0.4
  the in-game Main pill is the master switch and the addon blanks every slot
  while it is OFF. The old hint pointed at `/mcb main on`, which is not a
  command; the note and the log now say `turn Main ON in the overlay or
  /mcb t Main on`.
- Same treatment for `typing in game chat`, `out of combat` and
  `link lost - addon heartbeat`.

### Details

- New pure helpers `Ui/Foundation/HoldCopy.cs` and
  `Ui/Foundation/ToggleStateCopy.cs`; `HoldCopyTests` (11) and
  `ToggleStateCopyTests` (11) pin the copy. `ToggleSyncTests` updated for the
  corrected master-switch hint.
- No engine, scheduler, key-delivery, protocol or addon change: the 4.5.2
  pipeline was verified healthy against the live 12.1.5 client (strip decodes
  `proto=5 state=NeedTarget`, 30 fps, spec `ROGUE · Assassination`) while the
  log reported `Holding (addon Main off - rotation suspended)`.
- Live retail check stays OWED (`docs/TESTING.md` §3t).

## 4.5.2 Voidlight - 2026-10-25

**Companion 4.5.2 "Voidlight" / bridge 4.5.0 (unchanged).** Hotfix for window
resizing, NO wire change and NO addon change.

### Fixed

- **Edge and corner resizing.** The 4.5.0 chrome fix removed the system
  non-client frame, and the XAML content is hosted in a child HWND
  (`Microsoft.UI.Content.DesktopChildSiteBridge`) that answers `WM_NCHITTEST`
  with `HTCLIENT` for the entire client area - so the parent subclass never saw
  the edges and the window lost its resize cursors and drag-resize.
  `ShellWindow` now declares the four `NonClientRegionKind` border regions
  (`TopBorder`/`BottomBorder`/`LeftBorder`/`RightBorder`, 8 DIP each) and
  refreshes them on every size change, handing the frame back to the system.
  The caption is inset below the top grip (drag-to-move still works) and the
  caption buttons' passthrough rects start below it so the top corners resize.

### Details

- Verified by probing `WM_NCHITTEST`: left 10 / right 11 / top 12 / bottom 15 /
  corners 13-14-16-17 / title bar 2 / centre 1 on the running build.
- Files: `Ui/Shell/{ShellWindow.xaml.cs,Win32Host.cs}` (grip constant made
  public), `Ui/Foundation/ReleaseNotes.cs`, version + docs.
- Addon stays 4.5.0; macOS stays 4.4.0 (OWED).
- Live retail check stays OWED (`docs/TESTING.md` §3s).

## 4.5.1 Voidlight - 2026-10-25

**Companion 4.5.1 "Voidlight" / bridge 4.5.0 (unchanged).** UI-only follow-up,
NO wire change and NO addon change: protocol v5 + Ext2/Ext3/Ext4, 51 cells,
every cell byte-identical.

### Added

- **Live resize.** The Exp Details disclosure re-clamps itself while the window
  is resized (previously the row kept the height computed at expansion time, so
  shrinking the window clipped the card). The disclosure's scrollbar is forced
  visible exactly while the body cannot show everything.
- **Responsive classic layout.** `ResponsiveColumns` (new panel) stacks the
  two-up toggle rows below 500 DIP and the console's status/update cards below
  560 DIP; the console body scrolls when the stacked cards need more height.
- **Width-tier chrome.** `ChamferButton.ApplyScale` now follows the tier height
  (42 -> 52 DIP) and padding, not just the font step, so the transport band
  grows with a wide window.

### Changed

- **Log lines wrap.** `RollingLogView` renders a stamped entry as a two-column
  grid (fixed `HH:mm:ss` column, wrapping body), so long status/spec/bridge
  lines are fully readable at any width and continuation lines stay aligned
  under the message instead of being ellipsized. The empty state wraps too.
- **Changelog wraps and scrolls.** The Details body is the single scroller for
  the in-app changelog; bullets wrap and `Earlier:` wraps, so no text is cut at
  narrow widths.
- **Console logs are height-capped** (status 220 / history 180 DIP) so the
  stacked cards stay usable in a short window.

### Fixed

- Second console card no longer overflows past the window edge at compact
  widths.
- Details body no longer clips mid-line when the window is too short for the
  full disclosure.

### Details

- Files: `Ui/Shell/{RollingLogView,ExpConsoleView,ConsoleHomeView}.cs`,
  `Ui/Controls/{ChamferButton,SettingRow}.cs`, `Ui/Foundation/Layout.cs`,
  `Ui/Foundation/ReleaseNotes.cs`, `MaxDpsCompanion.csproj`, docs + tests.
- Addon stays 4.5.0 (identity-only release); macOS stays 4.4.0 (OWED).
- Live retail checks stay OWED (`docs/TESTING.md` §3r).

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
