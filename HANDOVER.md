# Handover — MaxDps-Companion

## Status: v1.1.0 — readiness gate (build green, smoke green, live verify next)

HEAD: readiness-gated slots (bridge `1.1.0`) + app `1.1.0` title/csproj.
Base `ac84240` (v1.0.0): 8-cell `MaxDpsBridge` addon + `MaxDpsCompanion`
.NET8 WinForms app (PRP chrome, Aethys engine), vendor snapshot of upstream
MaxDps v11.3.43 + all class modules.

## v1.1.0 — what changed and why

The companion hammered one unavailable key while other slots had live
suggestions. Root cause: the bridge encoded whatever MaxDps suggested
without checking castability, and the app's priority loop replays the
first valid slot every tick. Fix is bridge-side (one place, all slots):

- `addon/MaxDpsBridge/Reader.lua`: `MDB.IsSpellReady` (charges →
  `CooldownConsolidated` GCD-aware → `IsSpellUsable`), `MDB.IsInterruptReady`
  (ready + live interruptible cast on target; upstream flag alone lies —
  `GlowInteruptMidnight` sets `Flags` and only dims overlay alpha to 0 on
  non-interruptible casts, vendor `Buttons.lua:1136-1143`).
- All five getters gated; `WriteSlot` re-gates at encode time
  (`Bridge.lua`); `/mdb status` gains `ready=MCIDN` flags.
- Versions: bridge `1.1.0` (`MDB.VERSION`, `.toc`, `VERSION.txt`),
  app `1.1.0` (csproj + title), repo `VERSION.txt`.

## Validated

## Validated (v1.1.0)

- `dotnet build -c Release`: 0 warnings, 0 errors.
- `--ui-smoke-test` exit 0.
- Bridge installed at retail AddOns\MaxDpsBridge; upstream MaxDps* folders untouched.
- Start Menu shortcut `MaxDPS Companion.lnk` (taskbar: right-click
  the running app → Pin to taskbar; assembly identity is set).
- BNet launch: `battlenet://WoW/` protocol first, exe fallback minimized;
  remembered-account login, no credentials anywhere. SSO autologin via
  Battle.net `--exec="launch WoW"` (same path as the Play button).

## Outstanding (needs retail run)

1. `/reload`, `/mdb status` → expect `v1.1.0 ... ready=MCIDN` (uppercase =
   ready/encoded, lowercase m = suggested-but-unready, `-` = none).
2. Start → `link alive`, `sending`; verify it skips a cooling spell and
   fires the next ready slot instead of hammering one key.

## Post-release series (ac84240 → v1.1.0)

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
