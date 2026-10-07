# Arms + Fury execution fix (2026-10-04)

## Symptoms
- Arms: stuck on Ravager (shift+F) and Colossus Smash (shift+2) never pressed; Avatar (shift+5), Demolish (shift+1) work.
- Fury: Recklessness, Avatar, Ancestral Call never fire on rare/elite mobs.

## Diagnosis
### Bug 1 Arms (bridge addon/MaxDpsBridge)
- Colossus Smash 167105 denied in MajorCooldowns.lua:43; vendor Arms.lua sets it as MAIN setSpell first-wins; Main scan Reader.lua:388-399 yields nothing; MainFallback.lua only Fury 72 so Arms Main stays empty = stuck. Not in vendor offensive list (Cooldowns.lua:741 commented out) so Offensive slot never carries it. Deny was stale 12.1 guess; it is ~45s core rotation not 2-3min major.
- Ravager 228920 only GlowCooldowned (Flags true) but CategoryOf Reader.lua:559-572 returns nil since not in vendor classCooldowns.offensive; FirstFlagged offensive :583 never sees it. Main scan ascending takes lower ids first so would starve anyway.

### Bug 2 Fury rares (companion policy)
- No enemy classification (only MDB.IsBossTarget Reader.lua:1834-1844 for CC skip). TTK gate is lower-bound only. Strongest cause: HistoryWasteGuardHolds TtkPolicy.cs:263-271 runs even when live valid+long, holds on trash-learned TtkHistSec<NeedAdaptive. On rares HP band barely moves -> ewma<0.004 frac/s, TtkValid never true (TtkEstimator.cs:128,134,140,441) -> Burst preset hold CandidateProviders.cs:272-276 holds forever. Recklessness curated 10s (abilities.json:44), Avatar 15s (:140), Ancestral Call uncurated MajorBurst 15s (:274) share path.

## Contracts (no wire/PROTOCOL change; do not touch PixelProtocol.cs, docs/PROTOCOL.md, KeySender.cs)
### Lua bridge
- MajorCooldowns.lua: remove D[167105]; add MDB.MajorCDDeny[228920]=true (Main never encodes Ravager); add MDB.FlagOffensiveExtra={[228920]=true} (comment: vendor-verified by name at vendor/MaxDps/SpellData.lua:50).
- Reader.lua CategoryOf: return "offensive" when MDB.FlagOffensiveExtra[Id], after existing checks. FirstFlagged unchanged.
- MainFallback.lua: add [71] and ["WARRIOR:Arms"] fallback Mortal Strike 12294 then Overpower 7384 (worker confirms each by name in vendor/MaxDps/SpellData.lua + Arms spec table, cite line in comment).
- Sync addon/MaxDpsBridgeExp/MajorCooldowns.lua line 43 only if HANDOVER says Exp kept in lockstep (open question).
### C# TtkPolicy.cs
- Add pure static bool LiveReleasesHistory(AbilityDefinition a, CombatContext ctx, double need): true when (a) ctx.TtkValid && ctx.TtkSec>=need, or (b) !ctx.TtkValid && ctx.TargetAgeSec>=8.0 && ctx.TargetHpValid && ctx.TargetHpFrac>=0.85. Confirm CombatContext field names (TargetHpValid/TargetHpFrac/TargetAgeSec) in code.
- HistoryWasteGuardHolds returns false when LiveReleasesHistory true. Execute/kill-secure carve-outs stay first. GraceHoldHolds/WarmupHoldHolds unchanged. No new settings keys (optional LongLivedAgeSec).

## Tasks (bridge + C# disjoint, parallel)
- T1 bridge (code-worker): files MajorCooldowns.lua, Reader.lua CategoryOf only, MainFallback.lua, tests/secret_harness.lua new cases (Arms glow Main=167105; Arms fallback denied->12294; Ravager GetOffensiveCandidate=228920 and Main never 228920).
- T2 policy (code-worker): files app/MaxDpsCompanion/Knowledge/TtkPolicy.cs + new tests/MaxDpsCompanion.Tests/TtkRareTargetTests.cs using real abilities.json Recklessness 1719/Avatar 107574/Ancestral Call 274738: trash-history live60s no hold; rare invalid age10 frac0.95 hist6 no hold; live5s hold; early age2 invalid hold; trash invalid age10 frac0.4 hold. Scheduler test in T6RoutingSchedulerTests style only if disjoint new file.
- T3 docs (code-worker-high after T1/T2): HANDOVER.md status + deny reversal + OWED live; ARCHITECTURE.md file map FlagOffensiveExtra/Arms fallback/history-override; docs/TESTING.md owed live.

## Acceptance (repo root unless noted)
- dotnet build -c Release => 0 warn 0 err
- dotnet test -c Release (from tests/MaxDpsCompanion.Tests) => all pass incl new
- lua tests/secret_harness.lua => new Arms/Ravager PASS no regressions
- luac -p addon/MaxDpsBridge/*.lua => no output
- pwsh tools/ability_audit.ps1 => exit 0 (reconcile deny/extras if flagged)

## Risks + owed live
- Colossus Smash id: verify spell-verification.json:4044; if 167105 stale Main encodes dead id (fallback mitigates, deny was old mitigation).
- Ravager Offensive slot: confirm ConflictGroup/pairActive abilities.json:45 shared burst with Avatar could hold while Avatar active; check ability policy enable toggle T6RoutingSchedulerTests.cs:329.
- Lowest-id ordering Avatar 107574 before Ravager 228920 acceptable (next tick picks Ravager).
- Fury hypothesis unproven: if tests pass but live rares still fail, cause in estimator/HP band TtkEstimator.cs:128-141,344; need telemetry TtkValid/TtkSec/TtkHistSec/TtkHistBinding/age on rare. 85%/8s heuristic bounded by KillSecure.
- OWED live retail: Arms shift+2 + shift+F pressed; Fury Reck/Avatar/Ancestral on rare/elite; trash conserved. Offline pass closes none.
- Open: Exp lockstep? CombatContext field names? custom/patches.json:221-230 Colossus patch dependency (reconcile only if build checks it).
