# Custom MaxDps 12.1 Fork — Architect Spec

Date: 2026-10-03

## Decision

Vendor **v11.3.49** as a *Midnight stub* (read-only, never edited). Apply
**declarative patches** of two kinds: **P-DATA** (spell-ID / table data
corrections) and **P-GUARD** (denylist / canary guards). The bridge-side
denylist is the **primary fix**; vendor patching is secondary and reversible.

## Layout

```
custom/
  upstream-pristine/      # untouched v11.3.49 snapshot
  patches.json            # declarative patch manifest
  out/                    # generated patched tree (build artifact)
  MANIFEST.json           # hashes of pristine vs out
  patches/Sync.(ps1|cs)   # Sync tool
```

## patches.json schema + detectors

- Entry: `{ id, type: "data"|"guard", detector, target, find, replace, canary }`
- Detectors: `data` (spell-ID mismatch), `guard` (missing denylist),
  `canary` (sentinel absent after apply => abort).
- Sync tool validates every detector before/after; any canary miss fails the run.

## Bridge edits

- `addon/MaxDpsBridge/MajorCooldowns.lua`: add 12.1 denylist of stale IDs.
- `GetMainSpellID` edit: resolve current 12.1 primary spell IDs.

## Sync tool flow

1. Copy `upstream-pristine/` -> `out/`.
2. Load `patches.json`; run detectors; report drift.
3. Apply P-DATA then P-GUARD; verify canaries.
4. Write `MANIFEST.json` (sha256 per file).

## Tasks

- **T0** Vendor v11.3.49 snapshot into `upstream-pristine/` + MANIFEST.
- **T1** Author `patches.json` + detectors + canary sentinels.
- **T2** Build Sync tool (`out/` generator, manifest, canary gate).
- **T3** Bridge: denylist + `GetMainSpellID` edit.
- **T4** Wire build to consume `out/`; update HANDOVER/ARCHITECTURE.

## Acceptance commands

```
dotnet build -c Release
dotnet test -c Release        # from tests\MaxDpsCompanion.Tests
lua tests/secret_harness.lua
luac -p addon/MaxDpsBridge/*.lua
pwsh tools/ability_audit.ps1
pwsh custom/patches/Sync.ps1 -Verify
```

## Risks + owed live tests

- Vendor drift breaks detectors; mitigated by canary gate + hashes.
- Spell-ID churn across 12.1 hotfixes; mitigated by declarative P-DATA.
- **OWED**: full retail run (static != automated != live in-game).

## Spell-ID move table

### Needs move

| Ability | Old ID |
|---|---|
| Combustion | 190319 |
| Arcane Surge | 365350 |
| Evocation | 12051 |
| Doom Winds | 384352 |
| Implosion | 196277 |
| Dark Ascension | 391109 |
| Unholy Assault | 207289 |
| Essence Break | 258860 |
| Warbreaker | 262161 |
| Colossus Smash | 167105 |
| Deep Breath (Pres) | 357210 |
| Soul Reaper | 343294 |
| Bonestorm | 194844 |
| Glaive Tempest | 342817 |
| Fel Barrage | 258925 |
| Berserk (Guardian) | 50334 |
| Rage of Sleeper | 200851 |
| Fire Breath | 382266 |
| Eternity Surge | 382411 |
| Holy Prism | 114165 |
| Mindbender (Shadow) | 200174 |
| Shield Charge | 385952 |

### Already correct

Avatar 107574, Recklessness, Void Eruption, Metamorphosis, Tyrant,
Shadowfiend, Trueshot, Icy Veins, Celestial/Incarnation, Ascendance,
Infernal, Shadow Blades.
