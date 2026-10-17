# Racial Toggles Spec — Midnight 12.1 (2026-10-01)

## Decision
No wire change, no race field. Racials become catalog rows with Scope=racial, routed via existing Purpose -> Offensive/Defensive/Mobility providers. Toggles stay slot-based. Race implicit via known-spell filter (ready+bound+known). CC racials stay manual (Never). Passives/travel-only skipped.

## Classification (Off/Def routing)
Offensive (purpose MajorOffensive/MinorOffensive -> Offensive toggle): Orc Blood Fury (20572,33697 variants), Troll Berserking (26297), Mag'har Ancestral Call, Dark Iron Fireblood (off half), Lightforged Light's Judgment, Nightborne Arcane Pulse, Goblin Rocket Barrage, Earthen Azerite Surge (OffGcd=false channel), Blood Elf Arcane Torrent, Vulpera Bag of Tricks (dmg use), Haranir Thorn Bloom (dmg+heal hybrid -> Off), Kul Tiran Haymaker (only if CC-veto covers, else Never).
Defensive (SelfHeal/DefensiveMinor/Absorb -> Defensive toggle): Draenei Gift of Naaru (multi-ID), Zandalari Regeneratin', Mechagnome H.O.L.O. (ID unverified), Dwarf Stoneform, Kul Tiran Brush It Off, Night Elf Shadowmeld (58984).
Mobility (Movement -> Mobility provider, no toggle): Worgen Darkflight, Void Elf Spatial Rift, Goblin Rocket Jump, Zandalari Pterrordax Swoop, Dracthyr Glide/Soar, Highmountain Bull Rush as GapCloser.
Manual/skip: Tauren War Stomp, Pandaren Quaking Palm, Dracthyr Wing Buffet (CC risk -> Never); Gnome Escape Artist, Human Will to Survive, Undead Will of Forsaken (Dispel manual); passives skip (Combat Analysis, Light's Reckoning, Touch of Grave, Rugged Tenacity, Embrace of Loa, Ingest Minerals, Lash Out, Subterranean Predator, Emergency Failsafe); travel/out-of-combat skip (Mole Machine, Rootwalking, Cannibalize, Make Camp/Return).

## File changes (contracts first)
1. Knowledge/AbilityModel.cs: add string? Scope to AbilityDefinition + carry through AbilityOverrides. No enum change.
2. Knowledge/abilities.json + AbilityCatalog.cs loader: add scope:racial rows with new spell IDs, purpose/tier/offGcd; keep out of classes/*.json; resolve Q1/Q2 (nullable class? coverage asserts?).
3. Knowledge/ClassSpellBook.cs:263-266: remove only catalogued racial tokens from JunkTokens; keep ArcaneResistance, Hardiness, Perception, EveryManForHimself, specializations.
4. Knowledge/CandidateProviders.cs:110-148: no logic change (Purpose routing); optional comment citing racial scope.
5. Knowledge/CatalogLuaGenerator.cs: emit racial IDs into Catalog.lua lists (Catalog.lua:66+); regenerate Catalog.lua, no hand-edit.
6. addon/MaxDpsBridge/Toggles.lua: expected no change (SlotAllowed slot-based :62-71,116-120,299).
7. docs/PROTOCOL.md: add paragraph under v3.x no-wire-change: racials are catalog rows, race implicit in known-spell filter. Docs only.
8. HANDOVER.md + ARCHITECTURE.md: update status/next + file map same pass; mark live racial test OWED.
9. Tests: unit tests for new rows (routing per purpose, Scope carried, passives absent).

## Race detection
Known-spell filtering only. Future optional UnitRace("player") into spare nibble of class/spec cell (Bridge.lua:587) — NOT now (protocol change).

## Risks
Spell IDs unverified except Berserking 26297, Shadowmeld 58984, Blood Fury 20572/33697. Others need spell-verification.json pass (Stoneform, Fireblood, Ancestral Call, Light's Judgment, Haymaker, Bag of Tricks, Regeneratin', Arcane Pulse, Azerite Surge, Thorn Bloom, H.O.L.O.). Multi-ID variants (Blood Fury, Arcane Torrent, Gift of Naaru) need one row per ID. CC auto-fire danger -> keep CC Never. MaxDps may only glow Berserking/Shadowmeld on some specs; rest rely on ExtraCandidates.

## Verify
dotnet build -c Release (0 warn/0 err); dotnet test -c Release from tests\MaxDpsCompanion.Tests; lua tests/secret_harness.lua; luac -p addon/MaxDpsBridge/*.lua; pwsh tools/ability_audit.ps1. Live retail (OWED): Off racial blocked when Offensive OFF; Def racial blocked when Defensive OFF; non-matching race never fires; mobility holds in melee.

## Open Qs
Q1 nullable class? Q2 coverage asserts class/spec? Q3 MaxDps glow slot for Berserking? Q4 audit vs spell-verification.json racial coverage? Q5 Blood Fury/Berserking already routed?

## Review resolution (2026-10-01 request-changes)

1. Multi-ID coverage: added Blood Fury 33702 (wowhead active), Arcane Torrent
   25046 (Rogue) / 69179 (Warrior) / 129597 (Monk), Gift of the Naaru
   59542 / 59543 / 59544 / 59547 / 59548 / 121093. **59546 dropped**: wowhead
   live 12.1 shows it is "Transport Ship UD FX", not a Gift id. Final count: 38
   racial rows.
2. Spell-ID corrections (wowhead live 12.1 + vendor pin + live DB2
   `spell-verification.json`): Fireblood 273104 -> **265221** (273104 is the
   8s buff aura); Arcane Pulse 260369 -> **260364** (260369 is the trigger;
   260364 is the 3min / on-GCD active); Thorn Bloom 1238467 -> **1237885**
   (1238467 is the heal aura). **Brush It Off 291628 dropped**: it is a racial
   PASSIVE (291843 is its proc heal aura), so nothing is pressable. Azerite
   Surge 451897 is the triggered pulse ("Not In Spellbook", no cooldown) and
   stays `liveVerified=false` + Low confidence -> forced Manual. Every racial
   stays `liveVerified=false` (offline evidence only; not a live claim).
3. Urgency: Shadowmeld 58984 (drops combat / resets threat) and Stoneform 20594
   are curated `minimumUrgency=Orange` so a blind Yellow auto-press cannot
   waste them; Brush It Off is gone.
4. Self-heal routing: racial SelfHeal rows are emitted into `selfHeal` (not
   `defensiveMinor`) so the SelfHeal toggle governs them. Defensive-category
   racials keep `defensiveMinor`, which is covered by the bridge's dedicated
   `DefensiveCatalogSource` wire bit, so no `IsDefensiveGapFill` shadow list is
   needed (only offensives lack a source bit and need `IsOffensiveGapFill`).
5. Tests: `RacialTogglesTests` pins the exact 38-row count, per-purpose routing,
   Orange urgency, `liveVerified=false` handling, JunkTokens regression, and the
   committed `Catalog.lua` fixture (`secret_harness.lua` heal count updated to
   12 for the new selfHeal racials).
6. Mixed diff: the TTK v3.6 changes (`Intelligence/CombatContext*`,
   `Knowledge/Ttk*`, the TTK holds in `CandidateProviders`, `settings.ini`
   `[TimeToKill] Fallback`, `Toggles.lua` "TTK guard" label) are intentional and
   specified by `docs/plans/2026-10-01-ttk-intelligence.md`; they share this
   working tree with the racial work. `vendor/` is untouched and the tracked
   `settings.ini` default is preserved (per-machine edits belong in `dist\`).
