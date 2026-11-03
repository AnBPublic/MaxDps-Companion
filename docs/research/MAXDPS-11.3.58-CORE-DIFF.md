# Retail 12.1.5 — MaxDps v11.3.58 core source reconciliation

**Audit date:** 2026-11-03  
**Target:** World of Warcraft Midnight 12.1.5 / Interface 120105  
**Source:** upstream `kaminaris/MaxDps`, Git tag `v11.3.58` (released 2026-11-01)  
**Compared with:** Northstar RC2's embedded v11.3.49 extraction.  
**Status:** Core table reconciled; **not** a full talent/rotation/live certification.

## Checked sources

| File | 11.3.58 Git blob SHA | Result |
|---|---|---|
| `Cooldowns.lua` | `3640261ac4c3b31692434f26ce60d48978e3da03` | 368 Retail class/spec/category/name rows; exactly one changed ID |
| `SpellData.lua` | `694b53dbc8bbc226994f8d6610f3a17fcdb9e909` | 8,070 Retail class/spec/token/ID rows; **no differences** |
| `MaxDps.toc` | `37957aba40d0e00b99f3f09fcf076e979e37256d` | Interface 120105 and Interface-Retail 120105 |

The comparison used the repository's `Extract-VendorAbilities.ps1`
indentation-aware class/spec/category rules and `Extract-ClassSpells.ps1`
first-Retail-block brace-depth rules. Values were compared by
`class|spec|category|name` and `class|spec|token|id`, respectively,
rather than by file formatting.

### Verified upstream Cooldowns change

| Class/spec | Category | Ability | Old 11.3.49 | New 11.3.58 |
|---|---|---|---:|---:|
| DEATHKNIGHT / Frost | Offensive | Breath of Sindragosa | `152279` | `1249658` |

The **new** ID already exists in the older embedded Retail
`class-spells.json` data (Frost, Blood and Unholy token). This patch
updates the *official Cooldowns category/spec membership*, not the
spell's talent eligibility or current character knowledge. The previous
ID is no longer supplied by the current Cooldowns table.

The generated `Catalog.lua` candidate lists do not include either
Breath ID as a curated companion-generated offensive gap-fill; the
Companion must continue to trust the actual live MaxDps recommendation
and observed readiness rather than inventing a standalone cast trigger.

## Reproduction

1. Obtain **`Cooldowns.lua` at tag v11.3.58** from
   https://github.com/kaminaris/MaxDps/blob/v11.3.58/Cooldowns.lua .
2. From the project root run:
   `pwsh -File tools/Extract-VendorAbilities.ps1 -SourcePath PATH_TO_SOURCE`.
3. Compare the resulting `Knowledge/vendor-abilities.json` against
   the checked-in file and run the normal offline build, xUnit tests,
   Lua harness, and ability audit. The extraction verifies the exact upstream Git blob SHA and enforces the known
   368-row count and DK Frost ID so accidental old-source regeneration
   fails before writing.

**Do not overwrite** the read-only `vendor/` snapshot; the existing
custom fork manifest and patch offsets refer to its older version.

## What this does NOT establish

- The `abilities.json` curated decisions, source/validated-patch
  claims and `spell-verification.json` were researched on **12.1**,
  not fully validated on **12.1.5**. They are deliberately NOT
  relabelled as current. `AbilityCatalog.GamePatch` stays 12.1.
- The 8,070 source rows are *spell tokens*, not a current live talent
  matrix. Current classes, hero talents, ranks, learned/unlearned
  talents, mechanics, and all level-specific contexts still need
  live-current DB2 and in-game validation.
- The comparison is of two **core source tables** only. It is not a
  verification of all per-class rotation modules, the 75+ declarative
  custom-fork patches, available keybinds, secret combat data, or
  every content tier.
- The C# offline suite is not a World of Warcraft gameplay run. CI,
  when green, establishes build and deterministic-test success only;
  no claims about flawless Mythic+, Mythic raids or Delves follow.
- Automated gameplay inputs may violate Blizzard's rules; assess
  live-account compliance independently.

## Next evidence gates

1. Compare every current MaxDps class module and custom fork override
   against the official 12.1.5 release; do not apply stale patches
   blindly.
2. Inventory live 12.1.5 spellbook/talent selections and hero talents
   per class/spec, using only permitted and non-secret context.
3. For each discovered/unknown ID record owner, eligibility, trigger,
   requirements, variants, safe fallbacks, live observations and
   mismatches. Unknown companion-only actions must stay held.
4. Replay and test all DPS/tank specs, talent variants and level bands.
   Verify healers separately; a class inventory is not healing logic.
5. Complete controlled gameplay tests (world, Follower/normal/heroic,
   Mythic+, raid difficulties, hardest Delves, focus and chat gates).
6. Review CI and live evidence before merging or publishing another RC.
