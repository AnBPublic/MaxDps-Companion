# Engagement Gate Spec — Long Offensive CDs (2026-10-01, HOLD — user tests current build first)

## Status
HOLD: do NOT implement until user tests rebuilt racial build. User suspects CD planning may already be fine on new version; old exe 3.5.2 Fullcover (built 09:45) predates racial work (12:46+).

## Rule (when implemented)
Long offensive CDs fire only if >=3 enemies in combat with me OR bronze/silver/elite in combat with me.

## Updated counting rule (user 2026-10-01)
Count = ALL enemies in combat with me, whether they target me or not — threat ignored. Must work in dungeons when NOT tank, and solo. Therefore NO UnitThreatSituation filter. Per nameplate unit token: count when UnitCanAttack(player,u) AND UnitAffectingCombat(u). If any call secrets/throws, degrade to Unknown.

## Long-CD definition
Purpose==MajorOffensive AND derived BurstClass==MajorBurst (cdMs>=120000, AbilityCatalog.cs:1248). Covers Ancestral Call 274738. MinorOffensive excluded. Global rule, no per-spec table, no abilities.json edit, no Catalog.lua regen. Computed AbilityModel.IsLongOffensiveCd.

## Wire (cell 29 R bits 2-3 = EngagementClass)
0 UNKNOWN (old addon/sensor absent/secret), 1 NO (readable, rule not met), 2 YES-via-adds (>=3 in-combat enemies, threat ignored), 3 YES-via-elite (target elite/rareelite/worldboss or level -1 AND UnitAffectingCombat(target)). PixelProtocol.cs: enum + BridgeFrame.Engagement (default Unknown), decode (cell29.R>>2)&3 beside melee decode (~:545-560, used :676), no reserved-bit assert. No version bump (additive).

## Bridge sensor (Reader.lua beside IsBossTarget :1600, pcall+Scrubbed, returns 0-3)
Nameplates via C_NamePlate.GetNamePlates(); elite via UnitClassification/UnitLevel as IsBossTarget does. No CheckInteractDistance (blocked in combat, Reader.lua:1381). Nearby = nameplate range. Read-only, no protected calls, never enables nameplates. Encode in Bridge.lua cell-29 R write (~:410-430). Bump bridge version string.

## Bronze/silver (open Q1)
No confirmed UnitClassification API for bronze/silver; propose elite/rareelite/worldboss. Await user: Delves/Prey meaning?

## Companion changes (contracts-first)
T1: docs/PROTOCOL.md cell-29 R table (:293-294) + remove Phase-2 reservation (:213-215) + Engagement section; docs/KNOWLEDGE.md:429; HANDOVER.md + ARCHITECTURE.md same pass; docs/TESTING.md OWED live items.
T2: PixelProtocol.cs, Intelligence/CombatContext.cs (Engagement init Unknown, FromFrame ~:239 + copies :282,:323, bool LongCdEngaged => Adds|Elite), AbilityModel.cs IsLongOffensiveCd near :346-362/:863, CandidateProviders.cs gate before :319 (after pair window), settings [Offensive] LongCdUnknown=Allow|Hold default Allow + settings.ini tracked default, TelemetryEvent.cs:489 + ReplayRunner.cs:433 Engagement default Unknown.
Gate: if (input.InCombat && ability.IsLongOffensiveCd && !ctx.LongCdEngaged && (ctx.Engagement==No || (Unknown && LongCdUnknown==Hold))) return Hold("long CD needs >=3 in-combat enemies or elite"). Applies to MaxDps + gap-fill. EnemyCountMin gate :319 untouched.
T3 (parallel after T1): Reader.lua + Bridge.lua + secret_harness.lua secret cases per API.
T4: EngagementGateTests.cs + PixelProtocolV5Tests additions.

## Safe defaults
Unknown -> Use (fail-open, matches TTK Fallback=FailOpen); LongCdUnknown=Hold opt-in strict. OOC unchanged (:307 holds). Solo/trash holds 120s+ CDs (intended). Only explicit No holds. Default Q2 = Allow (confirm with user).

## Verify
dotnet build -c Release (0/0); dotnet test -c Release from tests\MaxDpsCompanion.Tests (all pass); lua tests/secret_harness.lua (pass incl. secret cases); luac -p addon/MaxDpsBridge/*.lua (no output); pwsh tools/ability_audit.ps1 (clean, catalog unchanged).

## Unit tests (one per branch)
Decode 0-3 + old->Unknown; melee bits unchanged; long+No->Hold; long+Adds->Use; long+Elite->Use; Unknown+Allow->Use; Unknown+Hold->Hold; Minor/Short+No->Use; 119999 vs 120000 boundary; Ancestral Call affected; OOC unchanged; T1 TTK precedence; replay round-trip.

## Risks + OWED live
Secrets (nameplate enum/UnitAffectingCombat may secret) -> Unknown -> fail-open; nameplates off -> count unreadable (elite-via-target still works); no true radius; solo/trash over-hold; TTK T1 waste guard runs first (elite short-TTK still held); no protected calls. OWED retail: 3-mob count non-tank + solo, elite/worldboss classification, cell-29 decode real capture, old-addon compat. Never claim live without retail.
