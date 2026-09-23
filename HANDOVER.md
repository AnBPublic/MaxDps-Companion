# Handover — MaxDps-Companion

## Status: v1.2.0 — secret-safe readiness gate (static + harness green, live verify owed)

HEAD: secret-safe bridge `1.2.0` (Reader/Bridge) + unchanged app `1.1.0`.
Base `ac84240` (v1.0.0): 8-cell `MaxDpsBridge` addon + `MaxDpsCompanion`
.NET8 WinForms app (PRP chrome, Aethys engine), vendor snapshot of upstream
MaxDps v11.3.43 + all class modules.
Prior release `41db101` (v1.1.0): readiness gate (first cut), which turned
out to be secret-unsafe — see below.

## v1.2.0 — what changed and why

Live BugSack: `860x MaxDpsBridge/Reader.lua:417: attempt to compare local
'Start' (a secret number value, while execution tainted by
'MaxDpsBridge')` via `IsSpellReady` <- `GetMainSpellID` <- `Bridge.lua:288`.
Midnight 12.x hands back SECRET `startTime`/`duration` from
`C_Spell.GetSpellCooldown`/`GetSpellCharges` under combat/encounter/M+/PvP
restrictions; tainted code may not compare or do arithmetic on them, so the
v1.1.0 gate threw every 50 ms tick. Fix (addon-only, no app changes):

- `addon/MaxDpsBridge/Reader.lua`
  - `MDB.IsValueSafe` / `IsSpellIDValue` / `SafeBool` helpers: the only
    place a possibly-secret value is probed (`issecretvalue` in pcall;
    failure = unsafe = treated as unknown).
  - `CooldownReady`: branches only on NeverSecret `isActive` / `isOnGCD`
    booleans (plain in the live error dump). GCD-only waits stay forgiven;
    legacy table shape falls back to a pcall-contained numeric check that
    fails open on a secret throw.
  - `HasCharges`: NeverSecret `isActive` first; `currentCharges` compared
    only when `MDB.IsValueSafe` passes; secret counts fail open.
  - `MDB.IsSpellReady` / getters / `SyncSet` / `FirstFlagged` /
    `ItemSpellIDs` / `ResolveBinding`: secret spell IDs degrade to
    "no suggestion" (they cannot be branched on or nibble-encoded).
  - `IsInterruptReady`: reads only a *readable* `notInterruptible`
    (cast #8 / channel #7, both slots probed); secret cast flags => fail
    open instead of throwing.
  - `MaxDps:CooldownConsolidated` is never called (its math throws on the
    same secrets, upstream `Helper.lua:1892`).
- `addon/MaxDpsBridge/Bridge.lua`: containment layer — `SafeReadout` +
  `WriteSlotSafe` wrap the 20 Hz readout so a future client change degrades
  to an empty slot + one warning instead of error spam; `TargetState` is
  pcall-isolated; `/mdb status` guards `MaxDps.Spell` before `~= 0`.
- `tests/secret_harness.lua`: offline Lua 5.4 harness with secret
  simulation (compare/arith/boolean-test/bool-tostring throw) and stubbed
  WoW globals; 33 checks: restricted cooldown shapes, GCD forgiveness,
  secret IDs/charges/flags, throw containment, full Bridge `Update` tick.

## Validated (v1.2.0)

- `luac -p` all five addon Lua files: OK.
- `lua tests/secret_harness.lua`: 33 passed, 0 failed (exit 0).
- No `GetSpellCooldownDuration`/`CooldownConsolidated`/`canaccessvalue`
  on any bridge code path (grep-verified).

## Outstanding (needs retail run)

1. `/reload`, `/mdb status` → expect `v1.2.0 ... ready=MCIDN` (uppercase =
   ready/encoded, lowercase m = suggested-but-unready, `-` = none).
2. In combat/instance: no `Reader.lua` secret compare errors in BugSack
   (the 860x spam must not recur); Main/CD/Interrupt keys still fire.
3. Kill a spell mid-cooldown and confirm the next ready slot fires instead
   (secret charge counts may make a 0-charge press once, by design).

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

## Perf profile (current)

- Strip 8x1 px (Aethys parity), paint-cached Lua, 20Hz update; sampler 8x1
  BitBlt + centre-pixel read (~1ms/tick), drift-free sleep pacing (~2% core);
  UI 250ms timer with change-gated Invalidate (zero repaint at rest).
