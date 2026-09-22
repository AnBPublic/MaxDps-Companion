# Handover — MaxDps-Companion

## Status: v1.0.0 + post-release fix series (static + smoke verified, live E2E outstanding)

Tag/base `ac84240` (v1.0.0): 8-cell `MaxDpsBridge` addon + `MaxDpsCompanion`
.NET8 WinForms app (PRP chrome, Aethys engine), vendor snapshot of upstream
MaxDps v11.3.43 + all class modules.

HEAD `7cf7f63` (2026-09-10): 18 fix/feat commits landed after the v1.0.0 base
(see *Post-release series* below). `build.ps1` publish green at HEAD
(commit `7cf7f63`, 2026-09-21), `install-addon.ps1` deployed to retail
`_retail_\Interface\AddOns` and the live addon files hash-match the repo.

## Validated

- `build.ps1` (`dotnet publish -c Release win-x64`): 0 warnings, 0 errors
  at HEAD `7cf7f63`; `dist\MaxDpsCompanion.exe` refreshed same commit.
- `--ui-smoke-test` exit 0; `--ui-snapshot` renders hero card clean
  (status + 7 toggles + strip + link + 5 buttons). *(v1.0.0-era; re-check
  after the one-click Recalibrate flow if in doubt.)*
- Bridge installed at retail AddOns\MaxDpsBridge (7 files) and **all 7
  files SHA-256 match the repo source**; upstream MaxDps* folders untouched.
- Start Menu shortcut `MaxDPS Companion.lnk` created (taskbar: right-click
  the running app → Pin to taskbar; assembly identity is set).
- BNet launch: `battlenet://WoW/` protocol first, exe fallback minimized;
  remembered-account login, no credentials anywhere. SSO autologin via
  Battle.net `--exec="launch WoW"` (same path as the Play button).

## Post-release series (ac84240 → 7cf7f63)

Perf/sampler: 1px→8px strip parity with Aethys, centre-pixel/median sampler,
drift-free loop, change-gated UI repaint, narrow-window handling, MinCell 2,
child-stamp.

Engine/readout: `EnsureEngine` demand-loads the class module + Fetch so idle
status answers; live `NextSpell` fallback + `next=` diagnostic; retail
action-bar path for the main spell + `FrameData` prep + class glow pass;
`LEARNED_SPELL_IN_TAB` event; guard class-function calls on `FrameData`.

Binding/diagnosis: `/mdb diag` bar-binding diagnosis, ElvUI dash-less HotKey
(`CQ`), probe real frames not addon presence, repaired corrupted merge in
`Reader.lua` (error-spam source), fixed `DiagPrint` ref.

Calibration UX: one-click Recalibrate hero flow, resizable fitted window,
calibrate-progress owns hero line, live anchor counter, auto-cal watchdog +
silent chat + settle + Verify clamp + alive ping, calibrate-on re-enables
bridge (stale paused strip), chat-command key discipline (Enter+Ctrl+V only).

Later reverts (HEAD `bef1acc`→`7cf7f63`): removed watchdog+alive (strip moved
mid-session), true anchor counter, always-sweep.

## Outstanding (needs retail run)

1. `/mdb status` in game + strip decode (`link alive`).
2. `Calibrate colors` learn + saved profile separation.
3. Main/CD/Interrupt/Defensive key presses in-game; AutoTarget/Interact
   kill-switches default OFF.
4. Live E2E on the shipped exe: the last three commits (`27eecd8`→`7cf7f63`)
   were mid-session debug fixes and have not been re-verified in a live client.

## Perf profile (current)

- Strip 8x1 px (Aethys parity), paint-cached Lua, 20Hz update; sampler 8x1
  BitBlt + centre-pixel read (~1ms/tick), drift-free sleep pacing (~2% core);
  UI 250ms timer with change-gated Invalidate (zero repaint at rest).
