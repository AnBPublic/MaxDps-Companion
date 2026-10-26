# Handover - MaxDps-Companion

Read order: this file -> `ARCHITECTURE.md` -> `docs/PROTOCOL.md`. Full history
(every release back to 1.x, root-cause write-ups, per-release verify logs) is in
[`docs/archive/HANDOVER-history.md`](docs/archive/HANDOVER-history.md); search it
by release name or date before re-investigating an old problem.

## CLIENT STATUS (current)

- **Current (Windows):** `4.5.5 "Voidlight"` - `app/MaxDpsCompanion/` (WinUI 3
  Windows client) + `app/MaxDpsCompanion.Core/` (shared brain) +
  `addon/MaxDpsCompanionBridge/` (4.5.1) + `dist/`. **C# is the long-term base**
  (decision 2026-10-17).
- **macOS:** `4.5.5 "Voidlight"` (Apple Silicon, ad-hoc signed, not
  notarized); bridge 4.5.1. The Mac host keeps its Avalonia shell (the Windows
  4.5.x Midnight UI is not ported). `app/Version.props` is now 4.5.5, guarded
  against drift by `ReleaseIdentityTests`.
- **Changelog norm (since 4.5.0):** every release adds
  `docs/CHANGELOG.md` (newest section) + a `ReleaseNotes` entry in the same
  commit; `ReleaseNotesTests` and `AGENTS.md` enforce it.
- **Frozen:** `addon/legacy/MaxDpsBridge/` (legacy stable bridge), `dist-legacy/` + `build.ps1`
  (3.7.9 WinForms), and `companion-rs/` (Rust port; reference only, no Ext4). See `LEGACY.md`.
- The legacy and companion bridge addons are mutually exclusive per character; use the
  companion bridge (`/mcb`; legacy aliases `/mdb`, `/mdbx`).
  Deploy: `pwsh tools/publish.ps1` + `pwsh tools/install-addon.ps1` (both only on the user's go-ahead).
- Wire: protocol v5 + Ext2/Ext3/Ext4 (51 cells), unchanged since 4.0.3. Any wire change is an
  architect-trigger task (`AGENTS.md`).

## 2026-10-26 macOS 4.5.5 "VOIDLIGHT" RELEASE (Apple Silicon, same tag; Version.props corrected)

Branch `release/v4.5.5-macos`. Root cause: `app/Version.props` (imported by
Core, which the Mac host references) stayed at **4.4.0 Wayfinder** through the
4.5.0-4.5.5 cuts while the Windows csproj moved to 4.5.5. The Mac/Core build
therefore reported 4.4.0 even though it consumed the 4.5.x Core. No engine,
wire or addon change; the bridge stays **4.5.1**.

DONE
- `app/Version.props` -> **4.5.5 "Voidlight"**; `ReleaseIdentityTests` now
  reads the file and fails when it disagrees with the Windows assembly or the
  current `ReleaseNotes` entry.
- `.github/workflows/release-macos.yml`: default tag `v4.5.5`; if a tag's
  shared `Version.props` is stale, replace ONLY `<Version>`/`<Codename>` with
  the dispatch commit's values, after requiring them to match the tag and the
  Windows csproj; `version_props_source` records the overlay. App/Core/addon
  source stays the tag; `HEAD == tag^{commit}` is still asserted.
- `tools/package-macos.sh`: app and addon versions are independent; the app
  asset is `MaxDpsCompanion-4.5.5-macos-arm64.zip`, the addon asset is
  `MaxDpsCompanionBridge-4.5.1-macos-addon.zip` (no collision with the Windows
  `MaxDpsCompanionBridge-4.5.1.zip`); `addon_version`/`version_props_source`
  are in BUILD-INFO.
- Docs: README/Mac README, `docs/CHANGELOG.md` 4.5.5 (macOS bullet + Details),
  `VERSION.txt`, `ARCHITECTURE.md`, `docs/TESTING.md` 4.0a. Release notes state
  the Mac keeps the Avalonia shell; the Windows 4.5.x Midnight UI is not
  ported.

VERIFY (2026-10-26, dry run 40330496953 + publish run 40330893217)
- Dry run passed with `version_props_source=workflow:51ab08a... (tag file was
  4.4.0)`, `source_tree=tag+version-overlay`, `version=4.5.5`,
  `codename=Voidlight`, `addon=MaxDpsCompanionBridge 4.5.1`,
  `dotnet_sdk=8.0.427`, `runtime=net8.0 (self-contained)`.
- `codesign --verify --deep --strict` and the `ditto` zip round-trip passed;
  Info.plist is `CFBundleVersion=4.5.5` / `4.5.5 Voidlight (mac)`.
- Published: app 43,681,215 B; addon 139,031 B; BUILD-INFO 1,105 B;
  SHA256SUMS 301 B. Downloaded all four Mac assets from the release and
  `sha256sum -c` was clean.
- Release body updated to `(Windows + macOS)`, 1120 xUnit, macOS assets +
  install + OWED section.

OWED
- First live retail run from the release asset on the MacBook Air M5: launch
  after the Gatekeeper override, grant Screen Recording + Accessibility,
  install the 4.5.1 addon, then locate/decode/send. Live macOS capture/send
  stays OWED; CI cannot prove it.

## 2026-10-26 RELEASE 4.5.5 "VOIDLIGHT" (GW2_UI taint root hotfix; bridge 4.5.1 unchanged; NO wire change)

Correction release after 4.5.4 shipped. The bridge guard stops the
assisted-combat frame's tick/cascade, but the 3,619 `SetCooldown` errors came
from Blizzard's shared `ActionBarButtonEventsFrame`: once the override button
was tainted, `ACTIONBAR_UPDATE_COOLDOWN` kept throwing until `/reload`. The
root was GW2_UI (`Core/API/utils.lua` `OverrideActionBar:HookScript(...)` calls
`Hide()` on the protected bar in combat; the extra-action `AddFrame` hook calls
`ActionButton_UpdateCooldown`). Branch `release/v4.5.5-gw2ui-hotfix`.
Identity: csproj 4.5.5, bridge **4.5.1** (unchanged), no wire change.

DONE
- `tools/patch-gw2ui-taint.ps1`: idempotent, backs up
  `*.maxdps-backup-4.5.4`, comments out both GW2_UI paths, `luac -p` check,
  `-Revert` restores. Tested on a temp GW2_UI fixture (patch, second-run no-op,
  revert) and on the live install (already patched).
- Local GW2_UI patched: `Core/API/utils.lua` (override-bar hooks) and
  `Games/Modern/Actionbar/extraAB_bossAB.lua` (extra-action cooldown call/frame
  pokes); backups kept; parse clean. `/reload` required (game was not running).
- Changelog/README/VERSION/ReleaseNotes 4.5.5 + `docs/TESTING.md` §3u step 0;
  4.5.4 stack split corrected (shared frame vs assisted frame).
- GitHub v4.5.4 release body corrected and `patch-gw2ui-taint.ps1` attached as
  an asset (tag `v4.5.4` remains; the tool is on `master`/release assets).

VERIFY (this machine, 2026-10-26)
- `dotnet build app/MaxDpsCompanion -c Release`: 0 warnings / 0 errors.
- `dotnet test -c Release`: 1119 passed / 0 failed (ReleaseIdentity 4.5.5).
- `--ui-smoke-test` / `--ui-smoke-test --exp`: PASS, title
  `MaxDPS Companion v4.5.5 Voidlight`.
- `lua tests/secret_harness.lua`: 415 passed / 0 failed (bridge unchanged).
- `luac -p` bridge + patched GW2_UI files: clean.
- `tools/patch-gw2ui-taint.ps1` on a temp GW2_UI fixture: patched, parse OK,
  second run no-op, `-Revert` restored; live install reports already patched.

OWED (live retail 12.1.5) - `docs/TESTING.md` §3u: launch/reload with the
patched GW2_UI, fight in restricted content, confirm the SetCooldown count
stops; `/mcb guard` must show the taint state. The addon cannot untaint a
button from a previous session; a reload is mandatory.

ROLLED OUT (2026-10-26, primary checkout `A:\AI\GitHub\WoW-Companions\MaxDps-Companion`)
- PR #14 merged to `master` (`dfb9839`), CI green on Windows + macOS; tag
  `v4.5.5` pushed and the GitHub release published
  (https://github.com/AnBPublic/MaxDps-Companion/releases/tag/v4.5.5), with the
  `MaxDpsCompanionBridge-4.5.1.zip` and `patch-gw2ui-taint.ps1` assets.
- `pwsh tools/publish.ps1`: FileVersion **4.5.5.0**, `dist\VERSION.txt`
  **4.5.5 Voidlight**, `settings.ini` hash unchanged.
- `pwsh tools/install-addon.ps1`: bridge **4.5.1** (TOC 4.5.1, `SecretGuard.lua`
  present) installed to `_retail_\Interface\AddOns\MaxDpsCompanionBridge`.
- Shipped smoke: `--ui-smoke-test` **PASS**, title
  `MaxDPS Companion v4.5.5 Voidlight`; a fresh 4.5.5 instance runs from `dist\`
  (PID 5620).
- Live confirmation is OWED (§3u); the WoW client is not running. The local
  GW2_UI files are patched; the next login/reload clears the old taint.



Live 12.1.5 report: while the companion pressed in combat the game logged
`ActionButton.lua:847 SetCooldown ... Secret values are only allowed during
untainted execution` thousands of times, plus the matching
`ADDON_ACTION_BLOCKED ... SetAttribute`. Branch `release/v4.5.4-taint-guard`.
Identity: csproj 4.5.4 (codename unchanged), bridge **4.5.1**, no wire change.

DONE
- **Diagnosis.** The stack is Blizzard's assisted-combat frame
  (`ActionButton_AssistedCombatRotationFrameMixin:OnUpdate` at
  `ActionButton.lua:1880` -> `OnActionBarSlotChanged` -> `Update` ->
  `ActionButton_UpdateCooldown` at 842 -> `ApplyCooldown` at 866 ->
  `SetCooldown` at 847), not the companion's key path. The same session's
  BugGrabber record names **GW2_UI** for
  `OverrideActionBarButton3:SetAttribute()`, and the cooldown error is on
  `OverrideActionBarButton2`. Counter 3619 at MaxDps's
  `assistedCombatIconUpdateRate` (0.15 s) = one error per assisted-frame tick.
- **Fix: `addon/MaxDpsCompanionBridge/SecretGuard.lua` (new, bridge 4.5.1).**
  While `C_Secrets.ShouldCooldownsBeSecret()` is true it hides the
  assisted-combat frame on override/vehicle/extra buttons, and on any button
  where `issecurevariable(button, field)` returns a named taint source. WoW
  does not run `OnUpdate` on hidden frames, so the tick stops. Clean
  main/multi-bar buttons are untouched; `C_AssistedCombat` still returns the
  next spell and the companion still presses normally.
- `/mcb guard` reports the secret predicate, `assistedCombatIconUpdateRate`,
  every assisted frame + parent button + taint source, and the loaded bar
  addons; `/mcb guard on|off` toggles `DB.TaintGuard` (default true).
- `Bridge.lua` `Defaults.TaintGuard = true`; TOC + `MDCB.VERSION` 4.5.1;
  `InGame.xml` loads `SecretGuard.lua` after `Bars.lua`.
- **Follow-up root fix (same day).** The guard stops the assisted-combat
  frame's own tick/cascade, but the shared `ActionBarButtonEventsFrame`
  `ACTIONBAR_UPDATE_COOLDOWN` dispatch is a second route: once the parent
  button itself is tainted, Blizzard's `SetCooldown` keeps throwing until
  `/reload`, and the addon cannot untaint it. The root in this setup is GW2_UI
  `Core/API/utils.lua` `normPetAndOverride`:
  `OverrideActionBar:HookScript("OnShow"/"OnHide", ...)` calls `Hide()` on the
  protected bar from tainted code in combat. `Games/Modern/Actionbar/
  extraAB_bossAB.lua` also calls `ActionButton_UpdateCooldown(button)` from its
  `ExtraAbilityContainer:AddFrame` hook. Both paths are commented out locally
  with `tools/patch-gw2ui-taint.ps1` (backups `*.maxdps-backup-4.5.4`,
  idempotent, `-Revert` restores). The tool is a third-party compatibility
  patch, not part of the addon zip; run it after a GW2_UI update and `/reload`.
- Companion 4.5.4 identity/changelog/README/VERSION/HANDOVER/TESTING §3u.

VERIFY (this machine, 2026-10-26)
- `lua tests/secret_harness.lua`: 415 passed / 0 failed (6 new guard cases).
- `luac -p` all bridge Lua files: 16 files, 0 failed.
- `dotnet build app/MaxDpsCompanion -c Release`: 0 warnings / 0 errors.
- `dotnet test -c Release`: 1119 passed / 0 failed (ReleaseIdentity 4.5.4).
- `tools/ability_audit.ps1`: clean (0 violations/warnings/missing/stale).
- `--ui-smoke-test` / `--ui-smoke-test --exp`: PASS, title
  `MaxDPS Companion v4.5.4 Voidlight`.
- `tools/patch-gw2ui-taint.ps1` on a temp GW2_UI fixture: patched both files,
  `luac` parse OK, second run no-op, `-Revert` restored both backups.
- Live GW2_UI files patched and `luac -p` clean:
  `Core/API/utils.lua` (override-bar hooks commented) and
  `Games/Modern/Actionbar/extraAB_bossAB.lua` (cooldown call/frame pokes
  commented). The game was not running; `/reload` is still required.

OWED (live retail 12.1.5) - `docs/TESTING.md` §3u: after a `/reload`, in a
restricted/instance fight, confirm the `[taint]` line and that the SetCooldown
count stops; `/mcb guard` must name the tainted button/source if anything is
still tainted. `/mcb guard off` must restore the stock assisted-combat refresh.

ROLLED OUT (2026-10-26, primary checkout `A:\AI\GitHub\WoW-Companions\MaxDps-Companion`)
- PR #13 merged to `master` (`5252346`), CI green on Windows + macOS; tag
  `v4.5.4` pushed and the GitHub release published
  (https://github.com/AnBPublic/MaxDps-Companion/releases/tag/v4.5.4), with the
  `MaxDpsCompanionBridge-4.5.1.zip` addon asset attached.
- `pwsh tools/publish.ps1`: FileVersion **4.5.4.0**, `dist\VERSION.txt`
  **4.5.4 Voidlight**, `settings.ini` hash unchanged.
- `pwsh tools/install-addon.ps1`: installed bridge **4.5.1** to
  `_retail_\Interface\AddOns\MaxDpsCompanionBridge` (21 files, `SecretGuard.lua`
  present); `MaxDpsCompanionBridge: enabled` in the character's AddOns.txt, and
  the legacy `MaxDpsBridge` is not installed.
- Shipped smoke: `--ui-smoke-test` **PASS**, title
  `MaxDPS Companion v4.5.4 Voidlight`; a fresh 4.5.4 instance runs from `dist\`
  (PID 36060).
- GW2_UI root hotfix applied locally: `Core/API/utils.lua` (override-bar
  hooks) and `Games/Modern/Actionbar/extraAB_bossAB.lua` (extra-action cooldown
  call/frame pokes) patched by `tools/patch-gw2ui-taint.ps1`; backups
  `*.maxdps-backup-4.5.4` kept; `luac -p` clean. The tool is committed on
  `master` (`82b9dc9`), attached to the v4.5.4 release, and the release body
  now carries the follow-up/root-cause note; the game needs `/reload` (it was
  not running at rollout time).
- Live guard confirmation is still OWED (§3u); the WoW client was not running
  at rollout time.

## 2026-10-26 RELEASE 4.5.3 "VOIDLIGHT" (toggles in the log, holds named; NO wire change)

Follow-up after a "companion does not press anything" report: the in-game
**Main** toggle was OFF (the 4.0.4 master switch), and the UI showed the green
"waiting for suggestion" with only a hex mask in Details, so the cause was
invisible. Branch `release/v4.5.3-toggles`. Identity: csproj 4.5.3 (codename
unchanged), `ReleaseIdentityTests`, `ReleaseNotes`. **Addon unchanged at 4.5.0**;
no wire change.

DONE
- **Toggles communicated.** `Ui/Foundation/ToggleStateCopy.cs` (pure) summarises
  the 14-bit ADDON-WINS mirror and diffs transitions. `UpdateMirrorPills` logs
  first sight / every change / mirror loss (`[toggles] Main ON · all others ON`,
  `[toggles] Main OFF - rotation suspended (turn it back on: /mcb t Main on)`,
  `[toggles] Trinket OFF, Defensive ON`) and feeds a live `toggles:` line in the
  Exp Details disclosure (`ExpConsoleView.SetToggles`).
- **Holds named.** `Ui/Foundation/HoldCopy.cs` maps the engine's hold status to
  `holding - <reason>` (Main off in game / typing in game chat / out of combat /
  link lost); the classic status line shows `Holding - <reason>`.
- **Wrong hint fixed.** `ToggleSync.MainOffNote` pointed at `/mcb main on`, which
  is not a command; it now says `turn Main ON in the overlay or /mcb t Main on`.
  (`/mcb t Main on` already exists in `InGame/Slash.lua`.)
- Details open height 252 -> 276 to fit the toggles line (still clamped by
  `inner - 164`, so the log keeps its reserve).
- Docs: CHANGELOG, VERSION.txt, README, TESTING §3t.

VERIFY (this machine, 2026-10-26)
- Live diagnosis that motivated the fix: `dist\MaxDpsCompanion.exe --probe`
  decoded `proto=5 state=NeedTarget slots=-,...` (strip healthy, 8 px cells at
  0,0), the engine ran at 30 fps and detected `ROGUE · Assassination`, and the
  log said `Holding (addon Main off - rotation suspended)`; the addon's saved
  `Toggles.Main` was `false` and `AppMask` 16383.
- `dotnet build -c Release`: 0 warnings / 0 errors.
- `dotnet test -c Release`: 1119 passed / 0 failed (new HoldCopyTests 11,
  ToggleStateCopyTests 11, ToggleSyncTests copy updated).
- `--ui-smoke-test` / `--ui-smoke-test --exp`: PASS (title 4.5.3 Voidlight).

OWED (live retail 12.1.5) - `docs/TESTING.md` §3t: flip Main off/on in the
overlay and watch the `[toggles]` lines and the Details summary; hold reasons
(typing, out of combat, link lost); recovery with `/mcb t Main on`.

ROLLED OUT (2026-10-26, primary checkout `A:\AI\GitHub\WoW-Companions\MaxDps-Companion`)
- PR #12 merged to `master` (`1ffe97b`), CI green on Windows + macOS; tag
  `v4.5.3` pushed and the GitHub release published
  (https://github.com/AnBPublic/MaxDps-Companion/releases/tag/v4.5.3).
- `pwsh tools/publish.ps1`: FileVersion **4.5.3.0**, `dist\VERSION.txt`
  **4.5.3 Voidlight**, `settings.ini` hash unchanged.
- Shipped smoke: `--ui-smoke-test` **PASS**, title
  `MaxDPS Companion v4.5.3 Voidlight`; a fresh 4.5.3 instance runs from `dist\`.
- Live check this session: the app was running against the 12.1.5 client and
  holding (`Holding (addon Main off - rotation suspended)`); the toggle log and
  the `holding - Main off in game` status are the 4.5.3 wiring, verified by
  unit tests + smoke here. The client was closed before the final capture, so
  the in-game `[toggles]` line flip remains OWED (§3t).
- Addon untouched at 4.5.0. **User action:** turn Main ON in the overlay or
  `/mcb t Main on`.

## 2026-10-25 RELEASE 4.5.2 "VOIDLIGHT" (edge/corner resizing hotfix; NO wire change)

Hotfix after the 4.5.0/4.5.1 chrome work. Branch `release/v4.5.2-resize`.
Identity: csproj 4.5.2 (codename stays Voidlight), `ReleaseIdentityTests`,
`ReleaseNotes`. **Addon unchanged at 4.5.0**; no wire change.

DONE
- **Root cause:** `WM_NCCALCSIZE` gives a full-window client area, and WinUI
  hosts the XAML content in a child HWND
  (`Microsoft.UI.Content.DesktopChildSiteBridge`) that answers `WM_NCHITTEST`
  with `HTCLIENT` for the whole client area. The parent `Win32Host` subclass
  therefore never saw the edges, so the window had no resize cursors and could
  not be dragged at the borders (the 4.4.0 frame had been doing it for us).
- **Fix:** `ShellWindow` declares the four `NonClientRegionKind` border regions
  (`TopBorder`/`BottomBorder`/`LeftBorder`/`RightBorder`, 8 DIP from
  `Win32Host.EdgeGripDip`) and refreshes them on `RootGrid.SizeChanged` as well
  as on title-bar size / XAML-root changes. The caption rect is inset below the
  top grip, and the caption-button passthrough rects start below it too, so the
  top corners resize while drag-to-move and the buttons keep working.
- `Win32Host.EdgeGripDip` is now public (single source for the region and the
  `WM_NCHITTEST` fallback).
- Docs: CHANGELOG, VERSION.txt, README, UI.md, TESTING §3s.

VERIFY (this machine, 2026-10-25)
- `dotnet build -c Release`: 0 warnings / 0 errors.
- `dotnet test -c Release`: 1096 passed / 0 failed (ReleaseIdentity 4.5.2).
- `--ui-smoke-test` / `--ui-smoke-test --exp`: PASS, title
  `MaxDPS Companion v4.5.2 Voidlight`.
- Live `WM_NCHITTEST` probe on the built client (parent): left 10, right 11,
  top 12, bottom 15, corners 13/14/16/17, title bar 2 (caption), centre 1
  (client). Before the fix every edge returned 1.

OWED (live retail 12.1) - `docs/TESTING.md` §3s: arrows on every edge/corner,
drag resize at 100/150 %, drag-to-move below the grip, caption buttons; plus
§3p/§3q/§3r carried items.

ROLLED OUT (2026-10-25, primary checkout `A:\AI\GitHub\WoW-Companions\MaxDps-Companion`)
- PR #11 merged to `master` (`21b1888`), CI green on Windows + macOS; tag
  `v4.5.2` pushed and the GitHub release published
  (https://github.com/AnBPublic/MaxDps-Companion/releases/tag/v4.5.2).
- `pwsh tools/publish.ps1`: FileVersion **4.5.2.0**, `dist\VERSION.txt`
  **4.5.2 Voidlight**, `settings.ini` hash unchanged (the running 4.5.1 was
  closed after the release was online).
- Shipped smoke: `--ui-smoke-test` **PASS**, title
  `MaxDPS Companion v4.5.2 Voidlight`; a fresh 4.5.2 instance runs from `dist\`.
- Shipped `WM_NCHITTEST` probe (window 825x1150): left 10, right 11, top 12,
  bottom 15, TL 13, TR 14, BL 16, BR 17, caption 2, centre 1 - the resize
  arrows and the sizing loop are restored on the release binary.
- Addon untouched at 4.5.0.

## 2026-10-25 RELEASE 4.5.1 "VOIDLIGHT" (live resize, wrapped log lines, readable changelog; NO wire change)

Follow-up to 4.5.0. Spec: `docs/plans/2026-10-25-responsive-4.5.1.md`.
Branch `release/v4.5.1-responsive`. Identity: csproj 4.5.1 (codename stays
Voidlight), `ReleaseIdentityTests`, `ReleaseNotes`. **Addon unchanged at 4.5.0**
and no wire change (v5 + Ext2/3/4, 51 cells).

DONE
- **Live resize:** `ExpConsoleView` re-clamps the open Details disclosure on
  `SizeChanged` (`ExpandedHeight()` = card inner - 164 DIP reserve, capped at
  `DetailsWanted` 252). The disclosure body is the only scroller; a slim
  amethyst thumb tracks extent/viewport and hides when everything fits, because
  WinUI's own scrollbar is hover-only.
- **Log wrapping:** `RollingLogView` renders a stamped entry as a two-column
  grid (fixed `HH:mm:ss` column, wrapping body) so long lines are never
  ellipsized and continuation lines align under the message. Empty state wraps.
  Classic console logs are height-capped (220/180 DIP).
- **Responsive classic shell:** new `ResponsiveColumns` panel; `ToggleRowPanel`
  stacks below 500 DIP and the console body switches to two equal star rows
  below 560 DIP (scrolls when the stacked cards need more height).
- **Tier chrome:** `ChamferButton.ApplyScale` now follows the tier height
  (42 -> 52 DIP) and padding, not just the type step.
- **Changelog copy:** `EarlierSummary` caps at four releases + ellipsis so the
  line stays on one row at the default width.
- Docs: CHANGELOG, VERSION.txt, README, UI.md responsive rules, TESTING §3r
  (and §3q item 6 updated), plan doc.

VERIFY (this machine, 2026-10-25)
- `dotnet build app/MaxDpsCompanion/MaxDpsCompanion.csproj -c Release`: 0 warnings / 0 errors.
- `dotnet test -c Release`: 1096 passed / 0 failed (ReleaseIdentity now 4.5.1).
- `lua tests/secret_harness.lua`: 409 passed / 0 failed.
- `luac -p` addon root + `InGame/`: 15 files, 0 failed.
- `pwsh tools/ability_audit.ps1`: exit 0, violations 0 / warnings 0 / missing 0 / stale 0.
- `--ui-smoke-test` / `--ui-smoke-test --exp`: PASS, title
  `MaxDPS Companion v4.5.1 Voidlight`.
- Snapshots: classic at 660 DIP (two cards) and 520 DIP (stacked, body scrolls),
  Exp at 520/660 DIP; long log lines wrap with a hanging indent.
- Live resize capture (dev build): 650x700 -> 825x1150 with Details open; the
  disclosure re-clamped each time, the thumb appeared only while cramped, and
  the full changelog was visible at 825x1150 with no thumb.

OWED (live retail 12.1) - `docs/TESTING.md` §3r (resize with Details open,
scroll affordance, log wrapping at 100/150 %, narrow stacking, tier chrome)
plus §3p/§3q carried items.

ROLLED OUT (2026-10-25, primary checkout `A:\AI\GitHub\WoW-Companions\MaxDps-Companion`)
- PR #10 merged to `master` (`047fc9d`), CI green on Windows + macOS; tag
  `v4.5.1` pushed and the GitHub release published with `### Changelog` +
  `### Details` sections
  (https://github.com/AnBPublic/MaxDps-Companion/releases/tag/v4.5.1).
- `pwsh tools/publish.ps1`: `dist\MaxDpsCompanion.exe` FileVersion **4.5.1.0**,
  `dist\VERSION.txt` **4.5.1 Voidlight**, `MaxDpsCompanion.pri` present,
  `dist\settings.ini` hash unchanged across the publish.
- Shipped-artifact smoke: `dist\MaxDpsCompanion.exe --ui-smoke-test` **PASS**,
  title `MaxDPS Companion v4.5.1 Voidlight`, in-game-config shell True.
- 4.5.0 closed after the release was online; a fresh 4.5.1 instance runs from
  `dist\`. Live `PrintWindow` capture: window 825x1150, client **825x1150**,
  row 0 `#1B1333`, Details expanded shows the full 4.5.1 changelog with no cut
  text and no scroll thumb.
- Addon untouched: `MaxDpsCompanionBridge` remains 4.5.0 (installed 20/20 in the
  retail AddOns folder by the 4.5.0 rollout).

## 2026-10-25 RELEASE 4.5.0 "VOIDLIGHT" (Midnight palette, gradient chrome, no title-bar band; NO wire change)

Spec/plan: `docs/plans/2026-10-25-midnight-ui-4.5.0.md`.
Branch `release/v4.5.0-voidlight`. Identity: csproj, `Native.CodenameFallback`,
`ReleaseIdentityTests`, addon TOC / addon `VERSION.txt` / `MDCB.VERSION` /
`MDCB.CODENAME` 4.5.0 "Voidlight". Addon change is **identity-only** (no Lua
logic, no Catalog, no cell).

DONE
- **Root cause of the white band:** `OverlappedPresenter.SetBorderAndTitleBar(false, false)`
  clears the caption, then `IsResizable = true` re-applies `WS_THICKFRAME`; the
  default `WM_NCCALCSIZE` insets the client by the ~8 DIP resize frame and DWM
  paints its top edge in the light system frame colour (`#F3F3F3` measured on
  the 4.4.0 build; window 650x702 px, XAML root 632x684 px, 9 px of frame on
  every side). `Win32Host` now answers `WM_NCCALCSIZE` (wParam = TRUE) by
  returning 0 without touching `rgrc[0]`, so the client area is the whole
  window; `WS_THICKFRAME` + `WM_NCHITTEST` keep edge resizing. DWM runs in
  immersive dark mode with `#2B2244` as the border colour. Caption drag rects
  are no longer offset by the frame thickness.
- **Midnight palette** (`ControlTheme`, mirrored in `Theme.xaml`/`ExpTheme`):
  ground `#0A0714`, surface `#141022`, elevated `#1D1735`, border `#2B2244`,
  text `#F3EEFB`/`#B3A9CB`/`#7C7294`, accent `#A78BFA` (+ `#6D38E0` strong),
  success `#4FD6A8`, warning (Sunwell gold) `#F0C05A`, danger `#D9455E`, info
  `#8CB8FF`. Classic console + Exp shell share it.
- **Gradients** (no large flat fills): shared ground (`Ground()` 110 deg violet
  -> void -> violet + `GroundAurora()` 25 deg wash) in both shells;
  `ShellWindow.xaml` title bar `#1B1333 -> #110C1F` with an amethyst hairline;
  `ChamferButton.Pill()` (top +10 %, bottom -28 %, hover +6 %, press -10 %) and
  `GhostPill()`; `RoundedCard` core (+6 %/-12 %); Exp status pill gradient;
  console top bar gradient. `Start` uses `ExpTheme.OkFill` `#237257` and `Stop`
  `DangerFill` `#C9405A` so the light label keeps AA contrast at the ramp's
  midpoint; `Restart Game` keeps a Sunwell-gold ring.
- **Stock-chrome leak fixed:** `UiClickableStyle` was declared with
  `TargetType=controls:UiClickable`, which WinUI rejects for the internal
  subclasses ("cannot apply a Style with TargetType ... to an object of type
  Button"); the exception was swallowed, so every owner-drawn control carried
  the stock grey Button fill behind it (the box around Details, the filled nav
  items). The style now targets `Button` and each concrete subclass opts in
  from its own constructor (`ConsoleNavItem`, `PresetChipView`,
  `ExpDetailsToggle`, `MetricTile`, `FilterChip`).
- **App accent:** `App.xaml` overrides `SystemAccentColor*` + the `Accent*`
  brushes so stock WinUI controls (CheckBox, ComboBox, ProgressRing, ...) use
  the amethyst instead of the Windows user accent (blue).
- **Changelog in Details:** `Ui/Foundation/ReleaseNotes.cs` (newest-first
  releases) renders the current highlights + `Earlier:` line inside the Exp
  Details disclosure. `DetailsWanted` 244 DIP clamped so the log keeps a 220
  DIP reserve; the detail body is the only scroller, so nothing clips
  mid-line. `ReleaseNotesTests` (+6) pins version/codename and requires a
  `docs/CHANGELOG.md` section per release; `AGENTS.md` records the rule.
- Docs: `docs/CHANGELOG.md` (new), `docs/UI.md` theme section, `docs/TESTING.md`
  §3q, README banner + 4.5.0 section, `VERSION.txt` (root + addon), plan doc.

VERIFY (this machine, 2026-10-25)
- `dotnet build app/MaxDpsCompanion/MaxDpsCompanion.csproj -c Release`: 0 warnings / 0 errors.
- `dotnet test -c Release` (from `tests/MaxDpsCompanion.Tests`): 1096 passed / 0 failed
  (+6 over 4.4.0: ReleaseNotesTests x6; ReleaseIdentity updated to 4.5.0 Voidlight).
- `lua tests/secret_harness.lua`: 409 passed / 0 failed.
- `luac -p` on `addon/MaxDpsCompanionBridge/*.lua` + `InGame/*.lua`: 15 files, 0 failed.
- `pwsh tools/ability_audit.ps1`: exit 0, violations 0 / warnings 0 / missing 0 / stale 0;
  committed Catalog.lua matches the generated output.
- `--ui-smoke-test --exp` (dev build): PASS, title `MaxDPS Companion v4.5.0 Voidlight`,
  in-game-config shell True, snapshot 825x1150, distinct grid colours 354.
- `--ui-smoke-test` (classic): PASS, in-game-config shell False.
- `--ui-snapshot-page=configuration`: checkboxes/accents render amethyst (no blue).
- Live `PrintWindow` capture of the dev build: window 825x1150, client 825x1150
  (no non-client frame), top row `#1B1333` (title-bar gradient, no white band);
  Details expanded shows the full changelog with the log intact.

OWED (live retail 12.1) - `docs/TESTING.md` §3q: no band on a light desktop
theme, edge/corner resizing at 100 %/150 %, caption drag, gradient hover/press,
label contrast, changelog copy/scroll, version surfaces, and (carried from 4.4.0)
§3p transport/foreground checks. Static != automated test != live.

ROLLED OUT (2026-10-25, primary checkout `A:\AI\GitHub\WoW-Companions\MaxDps-Companion`)
- PR #9 merged to `master` (`754bda7`), CI green on Windows + macOS; tag
  `v4.5.0` pushed and the GitHub release published with `### Changelog` +
  `### Details` sections
  (https://github.com/AnBPublic/MaxDps-Companion/releases/tag/v4.5.0).
- `pwsh tools/publish.ps1`: `dist\MaxDpsCompanion.exe` FileVersion **4.5.0.0**,
  `dist\VERSION.txt` **4.5.0 Voidlight**, `MaxDpsCompanion.pri` present,
  `dist\settings.ini` hash unchanged across the publish.
- Shipped-artifact smoke: `dist\MaxDpsCompanion.exe --ui-smoke-test` **PASS**,
  title `MaxDPS Companion v4.5.0 Voidlight`, in-game-config shell True.
- A fresh 4.5.0 instance was started from `dist\`. Live `PrintWindow` capture of
  the shipped exe: window 825x1150, client **825x1150** (no non-client frame),
  row 0 = `#1B1333` (title-bar gradient, no white band); Details expanded shows
  the full 4.5.0 changelog with the log intact.
- Per-machine window size: `dist\settings.ini` was sitting at the 520x560 DIP
  minimum (Width=650/Height=702 at 125 %), where the expanded changelog only
  fits by scrolling. The per-machine file was reset once to the designed
  660x920 DIP (Width=825/Height=1150); nothing was committed for it.
- `pwsh tools/install-addon.ps1` installed `MaxDpsCompanionBridge` **20/20**
  files at TOC `## Version: 4.5.0` / `X-Codename: Voidlight`; a `/reload` is
  required in the running client. Live checks stay OWED.

## 2026-10-25 macOS 4.4.0 "WAYFINDER" RELEASE (Apple Silicon, same tag/version)

Branch `release/v4.4.0-macos`. **No app, wire or addon change:** the macOS
client was already at 4.4.0 in `app/MaxDpsCompanion.Mac`. This release makes it
downloadable from the SAME `v4.4.0` release/tag as Windows (commit `7b8bbfa`).

DONE
- `.github/workflows/release-macos.yml`: tag-checkout build on `macos-15`
  (arm64), `SELFTEST=0`, `MAC_SIGN_MODE=adhoc`, .NET 8, version/tag/commit
  assertions, `codesign --verify --deep --strict`, `ditto` zip, optional
  `gh release upload` to the existing tag.
- `tools/package-macos.sh`: refuses a mismatched tag/`Version.props`/HEAD;
  writes `BUILD-INFO-macos.txt` (source tag/commit, arch, signing, addon/wire,
  Windows counterpart); signs after adding BUILD-INFO; zips the `.app` and the
  addon; extracts and re-verifies the shipped zip; emits
  `SHA256SUMS-macos.txt`.
- `tools/install-addon-mac.sh` + `Install Addon.command` in the addon zip:
  installs the same `MaxDpsCompanionBridge` 4.4.0 into the retail AddOns
  folder; never touches other addons or SavedVariables.
- `tools/mac-signing.sh`: `MAC_SIGN_MODE=adhoc` path for CI (no keychain
  identity; arm64 still signed).
- Packaging scripts come from the workflow commit, not the tag; app/Core/
  addon/version come from the tag. BUILD-INFO records both.
- README + Mac README: download/install, Gatekeeper first-launch override,
  TCC re-grant after an ad-hoc rebuild, and the explicit parity claim (same
  version/source/brain/wire/addon; not byte-identical OS binaries). CI adds
  `bash -n` for all macOS scripts.

VERIFY (2026-10-25, publish run 40197890931; release assets downloaded back
and checked on Windows)
- `gh release view v4.4.0 --json assets`: app zip 43,681,295 B, addon zip
  133,712 B, BUILD-INFO 901 B, SHA256SUMS 295 B - all `state=uploaded`.
- Build job: `.app` built on `macos-15` arm64; `codesign --verify --deep
  --strict` OK; `ditto` zip round-trip OK; `dotnet_sdk=8.0.426`,
  `runtime=net8.0 (self-contained)`, `source_commit=7b8bbfa`.
- Downloaded all four assets from the release and ran
  `sha256sum -c SHA256SUMS-macos.txt`: app/addon/BUILD-INFO all OK. The app
  zip contains `Contents/MacOS/MaxDpsCompanion.Mac`,
  `libmdc_capture.dylib` and `Contents/Resources/BUILD-INFO.txt`;
  Info.plist is `4.4.0` / macOS 14.0.
- Release notes updated with the macOS assets and the parity wording.

OWED
- First live retail run from the release asset on the MacBook Air M5: launch
  after the Gatekeeper override, grant Screen Recording + Accessibility,
  install the addon, then locate/decode/send. CI cannot prove capture or
  in-game behaviour.

## 2026-10-25 RELEASE 4.4.0 "WAYFINDER" (transport rework + foreground-gated detection; NO wire change)

Spec/plan: `docs/plans/2026-10-25-wayfinder-transport-foreground.md` (approved 2026-10-25).
Branch `release/v4.4.0-wayfinder`. Identity: csproj, `app/Version.props`, `Native.cs`
(`CodenameFallback`), `ReleaseIdentityTests`, addon TOC / `VERSION.txt` / `MDCB.VERSION` 4.4.0,
`MDCB.CODENAME` Wayfinder. Addon change is **identity-only** (no Lua logic, no Catalog, no cell).

DONE
- **Identity/docs:** csproj `<Version>4.4.0</Version>` + `<Codename>Wayfinder</Codename>`,
  `app/Version.props`, `Native.CodenameFallback` + XML comments, addon TOC `## Version: 4.4.0`
  / `## X-Codename: Wayfinder`, `Bridge.lua` `MDCB.VERSION`/`MDCB.CODENAME`,
  `InGame/Core.lua` defaults, addon + repo `VERSION.txt` 4.4.0 entries, README banner/history/
  build text, `docs/UI.md` Exp transport, `docs/TESTING.md` §3p, `ARCHITECTURE.md` UI note.
- **Exp transport (UI):** row 1 `Start | Pause | Stop`; row 2 `Open Game | Restart Game`.
  `Open Game` disabled + relabelled `Game Running` while the configured WoW process exists and
  `Game Starting…` during the 20 s launch grace; `Restart Game` confirms (session
  SavedVariables warning), kills the process tree and relaunches through Battle.net.
  `Calibrate` + the `Now:` line moved into the collapsed Details disclosure (fixed 140 DIP
  expanded height); classic shell unchanged (`InGameConfigMode=0`).
- **Foreground gate:** the Windows `RotationEngine.Tick` reports `GameInForeground`; while the
  game is not foreground it skips sampling/decoding (`CaptureDiagnostics.BackgroundMessage`,
  telemetry off, `_idle`), state reads `game in background` in a neutral tone, and detection
  resumes silently. Hard gate, independent of `[Window] AllowBackgroundKeys` (key delivery only).
- **Quiet bridge log:** `Ui/Shell/BridgeLogGate.cs` (pure, clock-injected) emits one warning
  per outage (after ~5 s) / one per run; the old `_expBridgeHintShown` latch is gone; transient
  capture status strings are filtered out of the Exp/classic log.
- **Core/Mac untouched:** Core's `EngineStatus` keeps the trailing `GameInForeground = true`
  default and the Core engine is unchanged, so the Mac build keeps its behaviour.
- **Wire:** protocol v5 + Ext2/Ext3/Ext4, 51 cells, every cell byte-identical (no PROTOCOL.md
  change).

VERIFY (this machine, 2026-10-25)
- `dotnet build app/MaxDpsCompanion/MaxDpsCompanion.csproj -c Release`: 0 warnings / 0 errors.
- `dotnet test -c Release` (from `tests/MaxDpsCompanion.Tests`): 1090 passed / 0 failed
  (+18 over 4.3.0: BridgeLogGate 7, WowWindowProcess 7, CaptureDiagnostics +4; ReleaseIdentity updated).
- `lua tests/secret_harness.lua`: 409 passed / 0 failed.
- `luac -p` on `addon/MaxDpsCompanionBridge/*.lua` + `InGame/*.lua`: 15 files, 0 failed.
- `pwsh tools/ability_audit.ps1`: exit 0, violations 0 / warnings 0 / missing 0 / stale 0;
  committed Catalog.lua matches the generated output.
- `--ui-smoke-test --exp` on the dev build: PASS, title `MaxDPS Companion v4.4.0 Wayfinder`,
  in-game-config shell True; the live WoW process made row 2 render
  `Game Running | Restart Game` with the Open Game button disabled; log fills the card.
- Published artifact smoke (`dist\MaxDpsCompanion.exe --ui-smoke-test`): see ROLLED OUT below.

ROLLED OUT (2026-10-25, primary checkout `A:\AI\GitHub\WoW-Companions\MaxDps-Companion`)
- The running 4.3.0 instance was closed gracefully; `pwsh tools/publish.ps1` published
  `dist\MaxDpsCompanion.exe` FileVersion **4.4.0.0**, `dist\VERSION.txt` **4.4.0 Wayfinder**,
  `MaxDpsCompanion.pri` present, `dist\settings.ini` hash unchanged.
- Shipped-artifact smoke: `dist\MaxDpsCompanion.exe --ui-smoke-test` **PASS**, title
  `MaxDPS Companion v4.4.0 Wayfinder`, in-game-config shell True, snapshot 632x684; with the
  live WoW process row 2 rendered **`Game Running | Restart Game`** (Open Game disabled) and the
  log filled the card (126 px at that window size).
- `pwsh tools/install-addon.ps1` installed `MaxDpsCompanionBridge` 20/20 files; TOC
  `## Version: 4.4.0` / `X-Codename: Wayfinder`, VERSION.txt 4.4.0, `MDCB.VERSION`/`CODENAME`
  4.4.0/Wayfinder. A `/reload` is required for the running client; live checks stay OWED.
- A fresh 4.4.0 instance was started from `dist\` (title verified); engine left stopped.
- GitHub: branch `release/v4.4.0-wayfinder` -> PR -> tag `v4.4.0` + release (this rollout,
  source commit f3d6c12).

OWED (live retail 12.1) - `docs/TESTING.md` §3p: Open Game disabled + `Game Running` while WoW
is up; `Game Starting…` gap; restart confirm/kill/relaunch; Calibrate inside Details; Now only in
Details; alt-tab pauses detection with no capture spam and resumes silently; never-established
bridge warns once after ~5 s; background state pill; `AllowBackgroundKeys` overlay note; restart
SavedVariables warning. Static != automated test != live.

## 2026-10-18 RELEASE 4.3.0 "NAMESAKE" (Companion Bridge rename; NO wire change)

Spec/plan: `docs/plans/2026-10-18-companion-bridge-rename.md` (approved; tasks 1-8).
Branch `release/v4.3.0-namesake`. Identity: csproj, `app/Version.props`, `Native.cs`,
`ReleaseIdentityTests`, addon TOC/`VERSION.txt`/`MDCB.VERSION` 4.3.0, `MDCB.CODENAME` Namesake.

DONE (code side)
- Addon renamed `MaxDpsBridgeExp` -> `MaxDpsCompanionBridge` (TOC `MaxDpsCompanionBridge.toc`,
  Title "MaxDps Companion Bridge", `Exp/` -> `InGame/`); Lua namespace `MDBX` -> `MDCB`;
  saved variables `MaxDpsCompanionBridgeDB` / `MaxDpsCompanionBridgeCharDB`; keybinds
  `MDCB_TOGGLE_*`; slash `/mcb` with `/mdb` + `/mdbx` aliases; overlay/settings/chat branding updated.
- Legacy stable addon archived: `addon/MaxDpsBridge` -> `addon/legacy/MaxDpsBridge`
  (byte-identical rename, frozen); the harness legacy-compat section points at the archive path.
- Companion: the in-game-config shell is now the DEFAULT (`InGameConfigMode=1`; `=0` opts into
  the classic shell; `--exp` stays a compatibility alias); `tools/publish.ps1` +
  `tools/install-addon.ps1` target `dist\` + the new addon; the legacy build output convention
  moves to `dist-legacy\`; the catalog audit is strict again (exit 0; addon Catalog.lua
  regenerated to byte-match the generator).
- Tests: 1072 passed / 0 failed; Lua harness 409/409; `luac -p` clean on addon root + InGame;
  `ability_audit.ps1` exit 0; `--ui-smoke-test` title `MaxDPS Companion v4.3.0 Namesake`.

ROLLED OUT (2026-10-18, primary checkout `A:\AI\GitHub\WoW-Companions\MaxDps-Companion`)
- `dist\` -> `dist-legacy\` (frozen 3.7.9 output, 6 entries); `dist-exp\` -> `dist\` (user
  `settings.ini` travelled, SHA256 unchanged); tracked `dist/.gitkeep` recreated;
  `tools/publish.ps1` -> VERSION.txt `4.3.0 Namesake`, exe FileVersion 4.3.0.0.
- Addon installed to `...\_retail_\Interface\AddOns\MaxDpsCompanionBridge` (20/20 files,
  TOC 4.3.0, Title "MaxDps Companion Bridge"); retired `MaxDpsBridgeExp` folder moved to
  `AddOns-Disabled\` (joins the archived `MaxDpsBridge`).
- WTF migration (all backups `*.4.3.0.bak`): account + Mokvar/Phio/Dion SavedVariables
  renamed `MaxDpsBridgeExp.lua` -> `MaxDpsCompanionBridge.lua` with `MaxDpsBridgeExpDB` ->
  `MaxDpsCompanionBridgeDB` / `MaxDpsBridgeExpCharDB` -> `MaxDpsCompanionBridgeCharDB`
  (1 occurrence each, count-verified); `bindings-cache.wtf` 8x `MDBX_TOGGLE_*` ->
  `MDCB_TOGGLE_*`.
- Taskbar shortcut `MaxDPS Companion.lnk` retargeted to `dist\MaxDpsCompanion.exe` (Start
  Menu shortcut already pointed at `dist\`).
- Verified on the published artifact: `dist\MaxDpsCompanion.exe --ui-smoke-test` -> PASS,
  title `MaxDPS Companion v4.3.0 Namesake`, in-game-config shell True (snapshot
  `dist\ui-smoke.png`). Note: `tools/ui_smoke.ps1` without `-ExePath` smokes the dev
  `bin\Release` tree, which can be stale - use `-ExePath dist\MaxDpsCompanion.exe` for the
  shipped artifact.

OWED (live retail) - addon loads as "MaxDps Companion Bridge", `/mcb` (+ aliases) responds,
toggle keybinds work, overlay icons + settings page intact; plus the 4.2.0 CC checks
(`docs/TESTING.md` §3o) still owed.
Static != automated test != live.

## 2026-10-18 RELEASE 4.2.0 "NIGHTBLADE" (CC intelligence; NO wire change)

Research: `docs/research/CC_DR_12.1.5.md`. Plan:
`docs/plans/2026-10-18-rogue-cc-intelligence.md`. Identity: csproj, `Native.cs`,
`ReleaseIdentityTests`, `app/Version.props`, Exp TOC/`VERSION.txt`/`MDBX.VERSION`
4.2.0-exp, `MDBX.CODENAME` Nightblade.

DONE
- **CC role split:** only single-target Stun/Silence interrupt-fallback rows are
  auto-eligible; Blind/Gouge/Sap/Polymorph/Fear/Hex/Repentance and every other
  break-on-damage row are manual-only. Rogue generated `cc = { 408 }`.
- **12.1.5 DR:** 20 s reset from the end of the last application, 1/0.5/0.25/
  0.125/0.0625, PvP immune after two, PvE 1.0 s usefulness floor. Applied to
  the delegated Kidney Shot path (registry Incomplete) as well as the curated
  CC provider. Kidney Shot uses the conservative 2 s base.
- **Interrupt/stun discipline:** casting-only, explicit not-interruptible skip,
  no blind stun, and a stun-ineffective latch (still casting ~800 ms after a
  stun on a confirmed cast) until target loss/OOC.
- **Break-on-damage guard:** `[CrowdControl] BreakGuard=1`; manual Blind/Gouge/
  Sap/Polymorph/Fear (Ext4 press) holds Main/Offensive/Interrupt for the
  expected duration (cap 10 s). Manual party-CC hold: `ManualHoldMs=8000`,
  `HoldHotkey=Ctrl+Shift+C`, Console "CC Hold" top-bar item.
- Limits documented: party CC is unobservable, active DoTs can still break a
  manual CC, DR is self-tracked, native DR tracker is not readable.
- Tests: 1071 passed / 0 failed; Lua harness 409/409; `luac -p` 9 Exp root files;
  catalog fixture + Exp Catalog regenerated.

OWED (live retail 12.1.5) - Kick vs Kidney Shot fallback, second/third stun DR,
Blind/Gouge attack guard, manual CC hold, boss/immune latch, status strings.
Static != automated test != live.

## 2026-10-18 RELEASE 4.1.0 "PARAGON" (audit remediation; NO wire change)

Spec/plan: `docs/plans/2026-10-17-paragon-audit-remediation.md` (architect-reviewed).
Branch `release/v4.1.0-paragon`. Identity: csproj, `Native.cs`, `ReleaseIdentityTests`,
`app/Version.props` (Core + Mac now share it), Exp TOC/`VERSION.txt`/`MDBX.VERSION` 4.1.0-exp.

DONE
- **Addon hot path** (`addon/MaxDpsBridgeExp/*`): `SPELL_UPDATE_COOLDOWN` no longer wipes the
  binding cache (revision bump only; full wipes coalesced); `FrameKey` keyed on the exact decision
  edges (HP 30/35/50/75, stagger stage) and now includes the previously missing `InCombat`,
  `Grouped`, stagger inputs; per-tick memo for `SpellVariants`/`KnownSpell`/`IsSpellReady`/
  `RotationCandidates`; per-tick closures/tables hoisted; HP-curve black paint cached; Toggles DB
  cached; HotbarWatch rebuild debounced, `gen` bumps only on content change; vehicle/override/bonus
  bar events; SavedVariables `SchemaVersion` 1 + `Migrate`; strip layout clamped to the screen;
  one shared slot->command table (slots 13-24 -> ACTIONBUTTON, 61-72 -> MULTIACTIONBAR1, 73-144 ->
  no key, texture fallback; source warcraft.wiki.gg/wiki/Action_slot). Harness 324 -> 393 pins.
  `issecretvalue` stays in `KeyIdent` (removal was conditional on proving `tostring(secret)` stable).
- **Windows engine:** one frame decode per tick; immutable toggle-mirror and colour-profile
  snapshots (no tearing); `KeyDelivery` (checked PostMessage/SendInput results, key-up always sent,
  lParam fix, UIPI detection); `NoteExternalSend` after a delivered queued user press; a
  focus-blocked keyboard slot no longer aborts lower ranks (`SlotGate`); `Process.GetProcesses`
  throttled to 1 s; duty-cycle stretch capped at one poll interval; `Stop()` checks the join and
  `PanicRelease`s injected modifiers; minimized / black-frame ("strip occluded") diagnostics.
- **`[Window] ModifierInput=Post|Auto|SendInput`** (default `Post` = byte-identical old behaviour).
  `Auto` uses SendInput only while the game is focused (re-checked right before sending), never
  releases a modifier the user holds, defers when an extra modifier is held, ignores its own injected
  keys in the physical-key scan (30 ms own-injection window), shows a status note for a background
  chord. Whether WoW honours posted modifiers is UNPROVEN - see TESTING 3n.
- **Shared brain:** Windows references `MaxDpsCompanion.Core` (32 byte-identical files + 8 merged
  supersets removed from the Windows project; Knowledge JSONs single-sourced in Core). Only
  `RotationEngine` still exists in both assemblies (allow-listed by `ShadowedTypeGateTests`); merging
  the two engines is Wave C, NOT done. Core's engine gained the Main master gate and the typing
  veto, so the Mac client honours them; it still has no user-press queue (Ext4 is decoded but unused).
- **Mac host:** failed permission check no longer starts the engine; keyboard is per-process only
  (no global-tap fallback); numpad / OEM / nav / F13-F20 keys mapped; region changes off the tick;
  Ext3 mirror in stream mode; settings persisted + pause/target/interact key boxes; Start/Stop off
  the UI thread; Swift buffer-lifetime and error-string races fixed (UNVERIFIED on a Mac);
  `build-mac.sh` no longer hardcodes a user path, reads `Version.props`, runs `selftest.swift`.
- **CI:** `.github/workflows/ci.yml` (windows: build/test/Lua harness/luac/ability audit; macos:
  Core+Mac build + `swiftc -parse`). `.gitattributes` keeps `*.sh` LF.
- Repo: history moved to `docs/archive/`; `LEGACY.md` rewritten (C# base, Rust frozen).

VERIFY (this machine, 2026-10-18): `dotnet build app/MaxDpsCompanion/MaxDpsCompanion.csproj -c Release`
0 warnings / 0 errors; Mac build 0 / 0; `dotnet test -c Release` 1054 passed / 0 failed (975 at 4.0.4);
`lua tests/secret_harness.lua` 393 passed / 0 failed; `luac -p` 15 Exp files clean; `pwsh
tools/ability_audit.ps1` Violations 0 / Warnings 0 / Missing 0 / Stale 0 (exit 2 = documented
stable-Catalog drift); a `dotnet publish` to a temp dir contains `MaxDpsCompanion.exe`, `.pri`,
`MaxDpsCompanion.Core.dll` and `--ui-smoke-test --exp` PASSES from it. Static != automated test != live.

OWED (live retail 12.1) - `docs/TESTING.md` 3n: ModifierInput chords (Post vs Auto/SendInput,
foreground + background); elevated-game delivery failure; stuck-modifier panic release; no double
press after a queued user press; focus-blocked slot lets an off-GCD interrupt through; strip-occluded
message; addon frametime before/after (CPU profiler); Ext4 map stays populated through cooldown bursts
(gen only bumps on content change); stance/vehicle bar binds (slots 73-144 now use the texture
fallback). Mac: permissions, key delivery to the game, key map on a real keyboard, Swift edits,
notarization. CI: first run on GitHub.

NEXT (not started): Wave C (one engine: characterisation tests -> port to Core -> delete the Windows
engine -> `SendGate`/status split); stages for 4.2 needing a wire change or a user decision: Assisted
Combat (`C_AssistedCombat`) source, on-screen suggestion icon, cooldown/aura/AoE/resource signals,
Windows Graphics Capture backend, dead profile sliders (`MinTtkSec`, Solo HP bands), melee probe in
combat via `IsSpellInRange`, stance/vehicle bar key mapping.

## Hard-won lessons (still true; details in the history file)

- A denied slot MUST repaint its cell empty (4.0.3 root cause #3), else the core checksum never matches.
- The addon `Main` pill is the master switch (4.0.4); a stale `Main=false` in SavedVariables looks like
  "only non-core skills fire" - check `/mcb main on` first.
- The user-press queue never suppresses the plan; it only wins the send-site race (4.0.3 hotfix).
- Keep `WindowsPackageType=None` + `EnableMsixTooling=true` paired or `MaxDpsCompanion.pri` is missing
  and the app dies `0xC000027B`.
- Never claim live validation without a retail run.
