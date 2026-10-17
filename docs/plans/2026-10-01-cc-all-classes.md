# Spec v3.6: all-class CC auto-fire (contracts-first)

Date: 2026-10-01 · Branch: `v3.5-class-browser` · Base: v3.5 CC fix `97f097c`
Architect verdict: APPROVED. Wire frozen, slot 6 unchanged, catalog SSOT.

## Why (user request)

v3.5 made only Storm Bolt + Shockwave auto-eligible. User: cover ALL
qualifying skills/talents from the intelligence body + online research, all
classes. Web research (Midnight 12.x, Wowhead/Icy-Veins) + architect review
produced the per-id verdicts below.

## Per-id verdicts (architect)

| id | Spell | Change | Note |
|---|---|---|---|
| 408 | Kidney Shot (ROGUE) | false→true | Casting-gated Stun; spends combo points (OWED live: may fight MaxDps finisher → revert to Suggest if so) |
| 5211 | Mighty Bash (DRUID all) | false→true | Long CD, care |
| 78675 | Solar Beam (DRUID Balance) | NEW true | Silence, aoe=true (combat-only), cd 60000; ground-targeted (OWED live) |
| 19577 | Intimidation (HUNTER, pet) | false→true | Fails harmlessly without pet (OWED live) |
| 15487 | Silence (PRIEST Shadow) | NEW true | Single, cd 45000 |
| 107570 | Storm Bolt | unchanged true | |
| 853 | Hammer of Justice | unchanged true | |
| 221562 | Asphyxiate | unchanged true | Single row; 108194 base alias NOT added (unverified; live check OWED which id bridge reports) |
| 46968 | Shockwave | true→FALSE | Demoted: provider has no enemy-count signal; M+/AoE DR risk → Suggest only |
| 119381 | Leg Sweep | stays false | Same reason |
| 9484 | Shackle Undead (was mislabeled "Horror") | rename, stays false | + one-line audit-doc correction |
| NO list | 1833,163505,22570,64044,30283,113724,192058,89766,47476,20066,1776,2094 + breaks/scatter/knockback/pet-kick/PvP/removed | no change | Reasons: stealth-gated, finisher/key DPS, cast-time setup, breaks-on-damage, scatter/knock grief, pet-kick-only, PvP-only, removed |
| Kicks | 47528,183752,187707,147362,2139,116705,19647,278326 | no change | Interrupt path, not CC provider |

Result: AE true 24→28, false 20→17; total rows 44→46. Stun 4/10→6/9.

## Contract (per file)

- `Knowledge/CrowdControlCatalog.cs`: flips + 2 new `Cc(...)` rows (78675 Balance; 15487 Shadow); rename 9484; header/v3.5 comments updated. Record signature unchanged.
- `AbilityCatalog.cs` / `CatalogLuaGenerator.cs`: NO code change (GapFill already emits AE=true).
- `addon/MaxDpsBridge/Catalog.lua` + `tests/.../fixtures/Catalog.lua`: REGENERATE via generator, never hand-edit. Expected cc diffs: ROGUE +408; DRUID +5211 +78675 (Balance); HUNTER +19577; PRIEST Shadow +15487; WARRIOR −46968.
- `abilities.json`: worker must confirm none of the flipped/new ids carries `neverAutomatic:true`/`UnsafeToAutomate` (provider would Hold); report per id.
- `HANDOVER.md` + `ARCHITECTURE.md`: status + cc-pool list. `docs/PROTOCOL.md` unchanged.

## Invariants

Wire frozen; slot 6 unchanged. Gate chain unchanged (opt-in OFF default, status,
user policy, AE, cast-hold, target, range, Stun/Silence TargetCasting==Yes,
20s DR, InterruptVetoes). Every Stun/Silence stays casting-gated. Never-auto
list untouched. Incap/fear/root/disorient not newly enabled. Overlay
display-only. No `vendor/` edits. No `singleTargetOnly` flag (no enemy-count
signal to read it — dead schema).

## Tests

Theory: flipped/new ids AE==true + Stun/Silence + class/spec. Theory: 46968,
119381 AE==false; NO-list ids false-or-absent. Provider per new id: casting+ON→Use;
not-casting/unknown→Hold; gate OFF→Hold. Matrix: 15487/78675 absent other
specs/classes. Maim 22570 `Find("DRUID","Feral",22570) is null`. Parity:
generated cc == committed Catalog.lua.

## OWED live (retail)

Enemy-count unobservable (Shockwave/Leg Sweep stay Suggest until wire signal).
Solar Beam ground-target assumption; Intimidation pet-less/cast-state; Kidney
vs MaxDps finisher; Asphyxiate reported id; full cast/non-boss/boss matrix.
