# Northstar 12.1.5 — upstream MaxDps reconciliation audit

**Date:** 2026-11-03. **Development only, not published RC2.**
**Authoritative upstream:** [kaminaris/MaxDps](https://github.com/kaminaris/MaxDps)
`v11.3.49...v11.3.58`, 9 commits, 6 changed files.
**Target client:** WoW Retail Midnight 12.1.5, interface 120105.

## Direct source evidence (not inferred from a release number)

| Resource | Old embedded snapshot | Verified upstream v11.3.58 | Comparison |
| --- | --- | --- | --- |
| `Cooldowns.lua` | 11.3.49; 368 class/spec/category/name rows | [v11.3.58 file](https://github.com/kaminaris/MaxDps/blob/v11.3.58/Cooldowns.lua), Git blob `3640261ac4c3b31692434f26ce60d48978e3da03` | 368 rows; one spell-ID replacement |
| `SpellData.lua` retail block | 8,070 class/spec/token/id rows | [v11.3.58 file](https://github.com/kaminaris/MaxDps/blob/v11.3.58/SpellData.lua), Git blob `694b53dbc8bbc226994f8d6610f3a17fcdb9e909` | All 8,070 tuples identical |
| `MaxDps.toc` | older 120100 snapshot | [v11.3.58 TOC](https://github.com/kaminaris/MaxDps/blob/v11.3.58/MaxDps.toc) at 120105 | Confirms runtime target, not behavioral accuracy |
| upstream diff | [compare tags](https://github.com/kaminaris/MaxDps/compare/v11.3.49...v11.3.58) | `Buttons.lua`, `Cooldowns.lua`, `Core.lua`, `Helper.lua`, `MaxDps.toc`, `.github/workflows/build.yml` | No class-module files changed within the tag interval |

### One cooldown/interrupt entry corrected

| Class | Spec | Category | Ability | Old ID | New ID |
| --- | --- | --- | --- | ---: | ---: |
| Death Knight | Frost | Offensive | Breath of Sindragosa | 152279 | **1249658** |

The new ID **already exists** in the checked-in `class-spells.json`
retail extraction and `spell-verification.json`, so this is a source
consistency repair. It does *not* prove talent selection, resource banking,
cooldown timing, current cast behavior, or boss encounter intelligence.
The companion continues to defer this offensive decision to the live MaxDps
engine. No additional speculative companion-only offense is emitted.

## Runtime substitutions outside the tabular inventory

The upstream `Core.lua` diff contains two **new live decision overrides**
that will not appear as new rows in `Cooldowns.lua` or `SpellData.lua`:

| Upstream runtime branch | Observable/source behavior | Current companion research status |
| --- | --- | --- |
| Unholy Death Knight / Blightfall `1271967` | If available in `MaxDps.Spells`, upstream replaces suggested Outbreak `77575` with Blightfall; [spell reference](https://www.wowhead.com/ptr-2/spell=1271967/blightfall), [12.1.5 talent context](https://www.method.gg/guides/unholy-death-knight/talents) | `1271967` is absent from the older checked-in class spell and spell-verification inventories. The current Companion's unknown Main path **delegates** to MaxDps; no independently intelligent Blightfall choice or variant-OFF certification is established. Must capture and verify action replacement + binding on live client. |
| Holy Paladin / Shield of the Righteous `415091` | Upstream may replace its Main suggestion at 5 Holy Power | `415091` exists in the checked-in class spell and verification tables. Timing/resource override itself requires retail inspection; no new independent rule was added. |

**Interpretation:** comparing 368 cooldown rows and 8,070 spell tokens
is necessary but **insufficient** for every talent. Code-level runtime
substitutions, talent-driven action replacements, hero talents, spell
book bindings and combat-secrecy behavior must be audited separately.
Any absent source ID remains MaxDps-delegated Main at most; the
Companion must not invent independent situational usage. A later phase
must reconcile replacement/alias family blocks against user OFF modes
using real client evidence, then test each affected talent configuration.

## Source boundaries after reconciliation

1. `app/MaxDpsCompanion.Core/Knowledge/vendor-abilities.json`: refreshed
   against upstream **11.3.58**, with independent `maxDpsVersion`,
   `gamePatch` and `interface` stamps. The loader rejects stamped values
   inconsistent with this reviewed source pin.
2. `app/MaxDpsCompanion.Core/Knowledge/class-spells.json`: kept as-is
   because the retail class/spec/token/ID tuples match upstream byte-for-byte
   at the parsed entry level. This is a discovery dataset, not 8,070 safe
   automated actions.
3. `app/MaxDpsCompanion.Core/Knowledge/abilities.json`: **intentionally still
   12.1 / MaxDps 11.3.49 curation**. Its evidence, cooldown durations,
   talent conditions, ownership and manual policies are NOT silently
   revalidated for 12.1.5.
4. `vendor/MaxDps` and `custom/upstream-pristine`: historical **read-only**
   11.3.49 sources; not modified. The bridge consumes the installed MaxDps
   addon live. Do not blindly copy 11.3.58 internals into the custom patch
   pipeline; its declarative patches and guarded API paths need separate
   compatibility review.
5. `Catalog.lua` and v5 pixel wire: unchanged in this source-only phase;
   the edited spell is a MaxDps-delegated action, not a curated gap-fill pool
   member. Existing generated-list sync tests must remain green.

### Regenerate from an independently acquired official tag

```powershell
pwsh -File tools/Extract-VendorAbilities.ps1 \
  -InputPath '../MaxDps-11.3.58/Cooldowns.lua' \
  -MaxDpsVersion '11.3.58' -GamePatch '12.1.5' -InterfaceVersion 120105
```

The extractor now refuses the old `vendor/MaxDps/Cooldowns.lua` path,
rejects suspicious inventory counts, and requires the audited Frost ID.
Review the generated diff rather than treating a source filename or
version string as verification.

## Verification gates

**Offline (CI is required; no local execution was inferred):**
`dotnet build -c Release`; `dotnet test -c Release`;
`lua tests/secret_harness.lua`; `luac -p` on current bridge sources;
`pwsh tools/ability_audit.ps1`.
New `NorthstarVendor125ReconciliationTests` assert the source stamps,
row counts, ID repair, legacy curation separation, safe delegation,
and rejection of falsely labeled vendor data.

**In-game (still owed):** update the real official MaxDps addon to the
compatible release and test all current 12.1.5 DPS/tank specifications,
build variants, early/90 leveling, ordinary world mobs, elites, Delves,
dungeons/M+, and raid encounters; exercise manual OFF variants,
invalid target/range, unknown readiness, cast/secret values, combat UI,
chat focus and lost foreground. Healer ally-target logic is separately
unimplemented. Save matched FPS/frame-latency evidence, not estimates.

**Certification status:** extracted source reconciled; **not 100% talent
coverage, not flawless gameplay, not encounter-certified, not live-verified**.
A green static audit cannot observe secret information unavailable to Lua,
nor can it substantiate perfect real-time input or game-rule compliance.
