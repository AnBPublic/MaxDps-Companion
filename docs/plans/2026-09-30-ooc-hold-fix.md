# Spec: Fail-Closed Out-of-Combat Hold Fix

Date: 2026-09-30
Route: L (architect-triggered: `Decision/**`, `Scheduler/**`, `addon/MaxDpsBridge/*.lua`)

## Goal

Make the companion **fail closed** out of combat: it must hold actions
unless explicitly permitted, instead of firing because a config gate is
disabled or the scheduler bypasses the rotation gate.

## Root Cause

1. Companion config `CombatOnly=0` in `dist/settings.ini:46` disables the
   out-of-combat gate in `RotationEngine.cs:543`.
2. Scheduler bypass suspected in `ActionScheduler.cs:288-298,351-352`.
3. Bridge auto-revive in `Bridge.lua:854-864` can resume suggestions after
   the user pauses.

## Patch

1. **RotationEngine.cs** — fail-closed OOC gate: hold unless
   `InCombat OR (CombatOnly=false AND Active AND HasTarget AND mirror OOC
   bit)`. `Paused`/null hold **before** auto-target/interact.
2. **ActionScheduler.cs** — apply the same OOC predicate.
3. **MovementGuard.cs:290,311** — gate auto-target/interact on the mirror
   OOC bit.
4. **AppSettings.cs:84** — keep default `true`; emit a load warning when
   overridden to false.
5. **dist/settings.ini** — local only, set `CombatOnly=1`. Verify with
   `git ls-files dist`; **never commit**.
6. **Bridge.lua:854-864** — add `userPaused` flag that blocks revive and
   keeps `Paused` state = 2.

## Out of Scope / Do Not Touch

- `docs/PROTOCOL.md` — unchanged.
- Toggles resolution stays app-wins.
- `vendor/` — read-only.
- Tracked `settings.ini` — unchanged.

## Verification

On `tests\MaxDpsCompanion.Tests`:

- `dotnet build -c Release` — 0 warnings / 0 errors
- `dotnet test -c Release`
- `lua tests/secret_harness.lua`
- `luac -p addon/MaxDpsBridge/*.lua`
- `pwsh tools/ability_audit.ps1`

### Test matrix

| Scenario | Expected |
|---|---|
| OOC + Paused | hold |
| OOC + no frame | hold |
| OOC + CombatOnly=false + mirror clear | hold |
| OOC + CombatOnly=false + Active + target + OOC bit set | fires |

## Docs

Update `HANDOVER.md` (status/next) and `ARCHITECTURE.md` (pipeline/file map)
in the same pass.

## Outstanding

Live retail E2E run — **OWED** until executed in a real client.
