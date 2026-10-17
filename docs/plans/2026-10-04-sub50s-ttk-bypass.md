# Sub-50s TTK bypass, all classes/specs (2026-10-04)

## Intent
User directive: all companion-gated skills with cooldown <50s bypass TTK locks unrestricted, every class and spec. Rotational short CDs (e.g. Colossus Smash ~45s, Warbreaker ~45s, Demolish ~45s) must never be waste/history/warmup/grace/burst-preset held. 90s+ majors (Avatar, Ravager, Recklessness) stay gated.

## Rule (generic, class-agnostic)
- `TtkPolicy.SubFiftyBypass(ability)`: true iff `ability.CooldownMs > 0 && ability.CooldownMs < 50_000`.
- Early `return false` (don't hold) in `WasteGuardHolds` (~:220), `HistoryWasteGuardHolds` (~:290), `GraceHoldHolds` (~:311), `WarmupHoldHolds` (~:199) in `app/MaxDpsCompanion/Knowledge/TtkPolicy.cs` (place helper near `:106 MinTtkSec`).
- Gate burst-preset hold `Knowledge/CandidateProviders.cs:272` with `&& !TtkPolicy.SubFiftyBypass(ability)`.
- Execute/KillSecure bypasses untouched; LiveReleasesHistory untouched. No settings keys, no wire/PROTOCOL change.

## Cooldown backfill (abilities.json, warrior sub-50s currently CooldownMs=0)
- Colossus Smash 167105 -> cdMs 45000 (ClassSpell-only, class-spells.json:45165); Warbreaker 262161 -> cdMs 45000 (entry :221 has minTtkSec15, no cdMs); Demolish 436358 -> cdMs 45000 (:45243); Odyn's Fury 385059 -> cdMs 45000 (:45621). Shield Charge 385952 / Demoralizing Shout 1160: verify real CDs, backfill only if <50s.
- Source: `abilities.json` cdMs (ms) -> `AbilityOverride.CdMs` (AbilityCatalog.cs:1456) -> `AbilityDefinition.CooldownMs` (AbilityModel.cs:623, default 0 at :1299-1303). 218 curated rows carry cdMs; generic rule covers all of them automatically.
- Caveat (documented): CooldownMs==0 (unknown/uncurated) stays gated — fail-closed. Do NOT blanket-bypass unknowns.

## Tests (new tests/MaxDpsCompanion.Tests/TtkSubFiftyBypassTests.cs)
- Sub-50s (Warbreaker 262161 post-backfill + one curated non-warrior <50s): trash history + invalid TTK + young target => NO hold on waste/history/warmup/grace/burst-preset.
- 90s (Recklessness 1719 / Avatar 107574): short live TTK => hold (unchanged).
- Unknown cd (CooldownMs 0): holds (fail-closed documented).

## Docs (same pass)
- HANDOVER.md: new section; also fix stale HANDOVER:47 (Exp no longer denies 167105).
- ARCHITECTURE.md: SubFiftyBypass file-map entry.

## Acceptance
- `dotnet build app/MaxDpsCompanion/MaxDpsCompanion.csproj -c Release` => 0/0; `dotnet test -c Release` (from tests dir) all pass incl new; `lua tests/secret_harness.lua` PASS; `luac -p` clean; `pwsh tools/ability_audit.ps1` exit 0.
- OWED live: Arms shift+2/shift+F fire incl trash; Fury rares unchanged for 90s CDs; trash conservation for <50s is intentionally relaxed (user accepted waste risk).
