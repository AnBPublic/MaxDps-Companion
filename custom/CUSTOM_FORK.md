# Custom MaxDps Fork (12.1 / Midnight)

Status: **T4 docs** — provenance, manifest, patch authoring and the sync tool
are done (T1-T3); this pass fills the behavior/design sections. In Exp 3.7.8 the
bridge-side denylist was **removed** (Main trusts the MaxDps core rotation); the
fork's remaining job is the vendor offensive-table un-comments / id routing
(P-DATA). The live 12.1 spell-ID check and the publish run stay **OWED** (see the
bottom of this file).

## Attribution

- Upstream addon: **MaxDps** by **Kaminaris** (`kaminaris MaxDps`).
- Source: CurseForge project **91970** —
  https://www.curseforge.com/wow/addons/maxdps (TOC also carries
  `X-WoWI-ID: 24637`, `X-Wago-ID: VBNBWM6x`).
- Snapshot origin: retail `_retail_\Interface\AddOns\MaxDps*` copied by the
  lead at install time (see `vendor/pin-versions.txt`), then re-copied into
  `custom/upstream-pristine/` by T1.
- License copied verbatim from upstream: `LICENSE` — **All Rights Reserved**,
  Copyright (c) 2021-2024 Kaminaris. Bundled libraries keep their own
  licenses (`MaxDps/Libs/LibCustomGlow-1.0/LICENSE`,
  `MaxDps/Libs/LibRangeCheck-3.0/LICENSE`); each class module ships the same
  `LICENSE`. Redistribution/patching here is for the owner's private client,
  not a public release of upstream code.

## Captured snapshot

| Field | Value |
|---|---|
| Upstream version (core TOC) | `v11.3.49` |
| `pin-versions.txt` claim | `v11.3.43` (drift: TOCs capture 11.3.49) |
| Interface | `120100` |
| Addons | 15 (`MaxDps` + 13 class modules + `MaxDps_MacroSupport`) |
| Files | 333 |
| Captured (UTC) | `2026-10-03T12:12:52Z` (`MANIFEST.capturedUtc`) |
| Per-file hash | SHA-256, `MANIFEST.fileSha256` |
| Class module versions | `tocVersions` in `MANIFEST.json` (v11.2.x range) |
| Manifest | `custom/upstream-pristine/MANIFEST.json` |

`MANIFEST.json` is the churn baseline: `fileSha256` maps every pristine file to
its hash, and `tocVersions` / `tocInterfaces` record each folder's `## Version`
and `## Interface`. (The plan/skeleton mentioned `custom/MANIFEST.json`; the
manifest actually lives beside the pristine tree, inside `upstream-pristine/`.)

## Hard rule: never edit `vendor/` (or the pristine tree)

`vendor/` is a read-only reference snapshot and is **never** an edit target.
`custom/upstream-pristine/` is the working pristine copy and is also never
edited in place. The sync tool copies a fresh upstream drop into
`custom/out/` and applies declarative patches there. Every patch is
declarative and reversible; there are no hand edits to any MaxDps Lua in
`vendor/`, `upstream-pristine/` or `out/`.

`custom/out/` is a **generated build artifact** (not committed; absent until a
sync run materialises it). `custom/patches.json` is the source of truth for
what a patch does.

## Behavior model: P-DATA / P-GUARD / bridge Offensive flags

Three layers, in precedence order:

1. **Bridge Offensive flags (runtime, no vendor edit).**
   `addon/MaxDpsBridgeExp/MajorCooldowns.lua` populates
   `MDBX.FlagOffensiveExtra`; `Reader.CategoryOf` maps a flagged id to
   `"offensive"` so the `FirstFlagged("offensive")` scan routes an offensive
   that is absent from `MaxDps.classCooldowns.offensive`. **3.7.8 removed the
   former `MDBX.MajorCDDeny` denylist** — MAIN now trusts the MaxDps core
   rotation with no exception; only the game-truth power veto + `MainFallback`
   filler remain. Stable `addon/MaxDpsBridge/` keeps its frozen deny. See below.
2. **P-GUARD** (`kind: "guard"`, 40 entries) — `*/Specialization/*.lua`
   patch that replaces the Assisted-Combat suppression branch
   (`if not MaxDps.FrameData.ACSpells or not MaxDps.FrameData.ACSpells[spellID] then`)
   with an always-true bypass, so `MaxDps:GlowCooldownMidnight` still fires an
   offensive cooldown when AC suppresses it. Secondary/reversible vendor
   coverage for the same symptom class.
3. **P-DATA** (`kind: "data"`, 23 entries) — `MaxDps/Cooldowns.lua`
   offensive-table spell-ID moves. Since the new 12.1 IDs are **not known
   offline**, `newSpellId` stays `null`; the patch annotates the stale
   `v11.3.49` row with a comment (`-- P-DATA-0NN stale-v11.3.49 id; 12.1 move
   pending`) so a future drop/re-capture is obvious. It is a marker (and, for
   the un-comment entries, the actual id routing the fork relies on) — Exp has
   no bridge denylist.

All three are declared once in `custom/patches.json`. `P-GUARD` and `P-DATA`
are evaluation-order patches owned by the sync tool; the bridge Offensive flags
(`MDBX.FlagOffensiveExtra`) are Lua that the addon loads directly and do not
depend on a sync run.

## Bridge Offensive routing: `MajorCooldowns.lua`

In Exp 3.7.8 `MajorCooldowns.lua` (loaded from the TOC alongside
Catalog/Keymap/Reader) holds **only** `MDBX.FlagOffensiveExtra` — the non-vendor
ids `Reader.CategoryOf` classifies as `"offensive"` so the
`FirstFlagged("offensive")` scan still routes them.

The former `MDBX.MajorCDDeny` denylist — the stale `v11.3.49` "needs move" set
and the "already correct" majors (Avatar 107574, Recklessness 1719, Void
Eruption 228260, …) — and the `GetMainSpellID` deny scan were **removed in
3.7.8**: MAIN now trusts the MaxDps core rotation with no exception, and the
only vetoes are game truth (`usable == false AND noPower == true`) plus the
per-spec `MainFallback` filler. Stable `addon/MaxDpsBridge/` keeps its frozen
3.7.7 deny. The fork's purpose is now only the vendor offensive-table
un-comments / id routing (P-DATA below).

Every denylisted id was vendor-verified by spell **name** against
`vendor/MaxDps/SpellData.lua` and `vendor/MaxDps/Cooldowns.lua` (v11.3.49);
the live 12.1 mapping is OWED.

There is **no deadlock on an empty Main**: the Offensive slot is an independent
`MaxDps.Flags` scan (`Reader.GetOffensiveCandidate`) and never routes through
`GetMainSpellID`. `ActionScheduler` holds only when no candidate of *any* slot
survives; an empty Main simply makes the Main slot absent and does not veto the
Offensive candidate. Holds are re-evaluated every tick, so an empty Main cannot
strand the rotation.

## `patches.json`

Declarative manifest authored by T1 (`custom/patches.json`), **76 entries** —
23 `data`, 40 `guard`, 13 `canary`. Entry schema:

```json
{
  "id": "P-DATA-001",
  "kind": "data | guard | canary",
  "addon": "MaxDps",
  "file": "Cooldowns.lua",
  "class": "MAGE",
  "spec": "Fire",
  "spellId": 190319,
  "spellName": "Combustion",
  "newSpellId": null,
  "anchor":   { "regex": "...", "scope": null },
  "apply":    { "op": "replace", "text": "..." },
  "fixedWhen":{ "regex": "..." }
}
```

- `anchor.regex` is a regex (never a line number); optional `anchor.scope`
  disambiguates repeated lines.
- `fixedWhen` makes re-application idempotent (skip if the new upstream
  already carries the fix).
- A missing or non-unique anchor is a **CONFLICT-risk** entry and must be
  reviewed before the tool trusts it.

### Kinds

- **data** — `MaxDps/Cooldowns.lua` offensive-table spell-ID moves (23: 22
  moves + 1 addition, P-DATA-023 Ravager 228920 to the Arms offensive table).
- **guard** — ACSpells loop bypass so `MaxDps:GlowCooldownMidnight` still
  fires when Assisted Combat suppresses an offensive cooldown (40 retail spec
  files; names `P-GUARD-001..040`).
- **canary** — a `local setSpell` sentinel that must survive on the
  legacy/TWW fallback path (13 entries, one representative `Specialization/TWW`
  file per class module; names `P-CANARY-001..013`).

T1 verification: all 76 anchors match exactly once; 0 conflicts.

> **Reconciliation resolved (2026-10-04).** The T2 tool consumed the authored
> `custom/patches.json` end-to-end: a real dry-run
> (`Sync-CustomMaxDps.ps1 -NewUpstream custom\upstream-pristine -WhatIf`)
> reported **APPLIED 63 / CONFLICT 0 / FIXED-UPSTREAM 13** across all 76
> entries, including `P-DATA-023` (no adapter was needed). The earlier
> "flatter shape / not yet consumed" note is obsolete.

## Sync tool

`tools/Sync-CustomMaxDps.ps1` (T2; the plan's `custom/patches/Sync.ps1` name
never landed — the tool lives under `tools/`). There is no `-Verify` switch;
dry-run is `-WhatIf`. Flow:

1. Read TOC versions + hash the new upstream drop; diff against
   `custom/upstream-pristine/MANIFEST.json` and report added/changed/removed
   churn (interface drift warns).
2. For each manifest entry, test `fixedWhen` against the **new** upstream:
   a match means upstream already covers it → `FIXED-UPSTREAM`, skip.
3. Otherwise apply the op into the working copy; a missing or ambiguous anchor
   (or a canary miss after apply) → `CONFLICT`.
4. Copy `NewUpstream` → `custom/out/` fresh, then write the patched files and
   `custom/out/SYNC-REPORT.md` (status counts + churn + versions).
5. If **every** entry is `FIXED-UPSTREAM`, print `upstream covers all` and
   **skip the whole run** (no `out/`, no report, no publish) — the fork is no
   longer needed against that drop.
6. Publish to `-PublishTo` only when there are zero CONFLICTs.

`NewUpstream` is always read-only and may be the live `AddOns` tree or a fresh
drop; `vendor/` is never written. Exit code 1 when any CONFLICT remains.
Fixture tests: `tests/sync/Sync-CustomMaxDps.Tests.ps1` (self-contained, no
Pester).

## Publish path

```
custom/out/  ──(Sync -PublishTo)──►  <WoW>\_retail_\Interface\AddOns\MaxDps*
```

- Default `-PublishTo` is
  `A:\Games\BattleNet\World of Warcraft\_retail_\Interface\AddOns`.
- Before overwriting, each existing target folder is moved to
  `custom/out/_backup/<yyyyMMdd-HHmmss>/` so a bad publish is reversible.
- A publish never happens on a CONFLICT run and never touches `vendor/`.
- `custom/out/` is a build artifact: regenerate it, do not edit it in place.

## Spell-ID move table (22 moves + 1 addition, from the 23 `data` patches)

New 12.1 IDs are **not yet known offline**; `newSpellId` is `null`. The vendor
data patch is the id-routing fix (the bridge denylist is gone in Exp 3.7.8). The
22 moves below are stale-id markers; P-DATA-023 is a **new row addition**, not a
move (see the Additions table after the move table).

| Ability | Old ID | Class | Spec |
|---|---|---|---|
| Combustion | 190319 | MAGE | Fire |
| Arcane Surge | 365350 | MAGE | Arcane |
| Evocation | 12051 | MAGE | Arcane |
| Doom Winds | 384352 | SHAMAN | Enhancement |
| Implosion | 196277 | WARLOCK | Demonology |
| Dark Ascension | 391109 | PRIEST | Shadow |
| Unholy Assault | 207289 | DEATHKNIGHT | Unholy |
| Essence Break | 258860 | DEMONHUNTER | Havoc |
| Warbreaker | 262161 | WARRIOR | Arms |
| Colossus Smash | 167105 | WARRIOR | Arms (already commented) |
| Deep Breath | 357210 | EVOKER | Preservation |
| Soul Reaper | 343294 | DEATHKNIGHT | Unholy |
| Bonestorm | 194844 | DEATHKNIGHT | Blood |
| Glaive Tempest | 342817 | DEMONHUNTER | Havoc |
| Fel Barrage | 258925 | DEMONHUNTER | Havoc |
| Berserk | 50334 | DRUID | Guardian |
| Rage of the Sleeper | 200851 | DRUID | Guardian |
| Fire Breath | 382266 | EVOKER | Preservation |
| Eternity Surge | 382411 | EVOKER | Preservation |
| Holy Prism | 114165 | PALADIN | Holy |
| Mindbender | 200174 | PRIEST | Shadow |
| Shield Charge | 385952 | WARRIOR | Protection |

### Additions (offensive-table, 1 — not a move)

| Ability | Added ID | Class | Spec |
|---|---|---|---|
| Ravager | 228920 | WARRIOR | Arms |

P-DATA-023 inserts `["Ravager"] = 228920` into the Arms `offensive` table
(anchored on the commented Sweeping Strikes line) so the vendor glow lands in
the bridge's Offensive slot. Since Exp 3.7.8 removed the denylist, Ravager is no
longer kept out of Main (the stable bridge's frozen deny still holds it). This
is an **addition**, not one of the 22 move-table entries, so the moves count is
unchanged.

Scope is **Arms only.** Fury and Protection both list Ravager in the generated
`Catalog.lua`, but their vendor `offensive` tables lack it (`Cooldowns.lua`
Fury :756-761 = Recklessness 1719 + Avatar 107574). Adding Ravager to Fury/Prot
is a separate `data` patch if wanted.

Already correct (major CDs, no data patch): Avatar 107574, Recklessness 1719,
Void Eruption 228260, Metamorphosis, Tyrant, Shadowfiend, Trueshot, Icy Veins,
Celestial/Incarnation, Ascendance, Infernal, Shadow Blades.

## OWED

- [ ] **Live 12.1 id check** — resolve the true 12.1 ids for the 22 moves and
      the "already correct" majors against a real client and fill `newSpellId`.
      (Exp 3.7.8 deleted `MDBX.MajorCDDeny`; there is no deny table left to
      refresh, and stable `addon/MaxDpsBridge/` keeps its frozen deny.) Static
      vendor names are not live proof.
- [x] **Publish run** — DONE 2026-10-04: `Sync-CustomMaxDps.ps1` end-to-end
      onto the real `AddOns` folder. APPLIED 63 / CONFLICT 0 / FIXED-UPSTREAM
      13; published hash == `custom/out/`; backup
      `custom/out/_backup/20261004-172307`.
- [x] **Manifest schema reconciliation** — DONE 2026-10-04: the tool consumed
      the rich `custom/patches.json` shape end-to-end (APPLIED 63 /
      FIXED-UPSTREAM 13), so no adapter is needed.
- [ ] **Full retail run** — fork vs stock after `/reload`
      (see `docs/TESTING.md` §3h). Static ≠ automated test ≠ live in-game.
