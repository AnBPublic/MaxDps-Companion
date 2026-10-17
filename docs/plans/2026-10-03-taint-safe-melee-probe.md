# Taint-safe melee probe (ADDON_ACTION_BLOCKED fix)

Date: 2026-10-03 · Route: L (protected `addon/*.lua`) · Wire: **no change**

## Problem

`CheckInteractDistance` is `#nocombat`-restricted in 12.x. It was called from
two sites (Reader `ProbeTargetMelee`, Bridge `TargetState`) guarded only by
`InCombatLockdown()`. The client can still raise `ADDON_ACTION_BLOCKED`
(lockdown timing races, hidden restrictions), and the Bridge call bypassed the
Reader guard entirely.

## Design

One shared probe in Reader (the only `CheckInteractDistance` call site);
Bridge consumes it. Four gates plus a machine breaker:

- `MeleeCache = {val, guid, at}` — per-target result, TTL 0.25 s.
- `SafeAfter` — no probe before this `GetTime()` stamp (0.5 s post-combat).
- `InCombatEv` — event-driven combat (regen/encounter), not just the API.
- `BlockHits` — `ADDON_ACTION_BLOCKED` count; at 2 the probe is off for the
  session (melee stays UNKNOWN).

`ProbeSafe()` is true only when **all** hold: `InCombatEv == false` AND
(`InCombatLockdown == nil` or it returns false) AND
`Scrubbed(UnitAffectingCombat("player")) ~= true` AND `GetTime() >= SafeAfter`
AND `BlockHits < 2`. `MDB.ProbeTargetMelee()` returns `nil` before touching
the API or the cache, else the cached `1|0` while the same GUID and TTL hold,
else one `pcall(CheckInteractDistance, "target", 3)` mapped through `Scrubbed`
(secret/failed ⇒ `nil`, never `0`).

Event frame (`MDB._ProbeEvents`, reused if present):
regen-disabled / encounter-start ⇒ `InCombatEv=true`, clear cache;
regen-enabled / encounter-end ⇒ `InCombatEv=false`, `SafeAfter=now+0.5`;
target-changed ⇒ clear cache; entering-world ⇒ `InCombatEv=InCombatLockdown()`,
`SafeAfter=now+0.5`; action-blocked ⇒ if the addon is MaxDpsBridge and the
function name contains `CheckInteractDistance`, `BlockHits++` and clear cache.
The first hit backs off finitely (`SafeAfter=now+5`, so the probe resumes once)
and logs one `DiagPrint`; only the second hit sets `SafeAfter=huge` and disables
the probe for the session.

`GetTargetContext` (Reader) keeps `Melee=2` on `nil`; `TargetState` (Bridge)
returns `STATE_NEED_INTERACT` only on `1`. No wire change: PROTOCOL cell 29
bit1 already encodes UNKNOWN.

## Files

- `addon/MaxDpsBridge/Reader.lua` — state locals + `SafeNow` (top),
  `EnsureProbeEvents`/`ClearMeleeCache` (before `InitSensors`), `ProbeSafe` +
  `MDB.ProbeTargetMelee` (was `local ProbeTargetMelee`), `GetTargetContext`.
- `addon/MaxDpsBridge/Bridge.lua` — `TargetState` calls `MDB.ProbeTargetMelee`.
- `tests/secret_harness.lua` — `UnitGUID` stub, vararg `FirePlainEvent`,
  cases (a)–(f).
- `HANDOVER.md`, `ARCHITECTURE.md` — status/pipeline in the same pass.

## Verification

`luac -p` 8 files clean; `lua tests/secret_harness.lua` 256/256; app
`dotnet build -c Release` 0/0; `dotnet test -c Release` 933/933 (one STA UI
timeout on the first run passed standalone and on rerun — pre-existing flake);
`pwsh tools/ability_audit.ps1` clean (violations 0, warnings 0).

## OWED (live retail 12.1)

Enter combat with a melee target and confirm no `ADDON_ACTION_BLOCKED` popup,
that melee/mobility reads UNKNOWN in combat and resumes ~0.5 s after, that one
block backs off ~5 s then resumes once, and that two blocks silently disable the
probe for the session. Static ≠ automated ≠ live.
