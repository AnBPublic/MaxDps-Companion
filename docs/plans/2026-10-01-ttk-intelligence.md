# TTK Cooldown Intelligence — Approved Architect Spec

Date: 2026-10-01

# Spec: TTK cooldown intelligence v3.6, "dying-trash guard" (2026-10-01)

## 1) Problem and decision
Why it fails open on dying trash: TtkEstimator valid only after >=2 declines AND >=2.5s span. Trash dies faster, never valid. Every gate skips (TtkPolicy fails open), CDs fire on mobs we want to protect. Each new target resets estimator. T4 covers Solo only. TTK toggle OFF forces band 15 = blinds execute + Burst consumers.
Decision: Phase 1 (companion-only, no PROTOCOL change): provisional estimate + fast-pack latch, tiered age-aware policy, fix solo/group defensive gating, rename/document toggle. Phase 2 (optional wire change): encode target class — separate spec.

## 2) Contracts
TtkEstimate extended (additive, defaults keep callers compiling): bool Valid, double TtkSec, double TargetHpFrac, bool Provisional=false, double AgeSec=0, bool FastPackLatch=false. Invalid unchanged. Provisional = early rate available but Valid false.
New estimator constants: ProvisionalMinDropBands=3 (~20%, above 6.7% quant step), ProvisionalMinSpanSec=0.5, LowFirstSightBand=3 (<=~23%), KillMaxLifeSec=12, KillMaxLastFrac=0.25, LatchKills=2, LatchWindowSec=20, LatchHoldSec=20.
Estimator rules:
1. Fast-drop provisional: within first 2.5s, band falls >=3 over >=0.5s → Provisional=true, TtkSec=frac/(dropFrac/dt). One sample suffices. Valid stays false until normal rule met.
2. Low-first-sight provisional: first obs band<=3 and in combat → Provisional=true, TtkSec=frac*15s (seed rate 1/15 per sec).
3. Fast-pack latch: kill = departure (no target or upward jump >0.12) where prev target lifetime<=12s and last frac<=0.25. Two kills in 20s sets FastPackLatch for 20s. Clears early when any target AgeSec>=12 with frac>0.5, or valid TtkSec>=30.
4. AgeSec = time since first obs of current target, 0 with no target.
5. Reset/no-target/unknown-HP clears provisional but keeps latch. Latch survives target swaps.
CombatContext: WithTtk(TtkEstimate) signature unchanged; plumb TtkProvisional, TargetAgeSec, FastPackLatch, EffectiveTtkKnown => TtkValid||TtkProvisional. Clone must copy new fields.
TtkPolicy (pure; seconds): MajorBurst 15 (was 12), Transformation/Summon 20, WindowDriven 10, ShortCooldown/ProcDriven/ResourceDriven/SingleTargetOnly 5, Execute 3, AoeOnly 0 (primary-target TTK says nothing about pack), Hybrid 0 (never gated). Curated minTtkSec still wins. ShortCooldown cd<=45s uses min(threshold,3).
Waste guard applies to Valid||Provisional. Provisional guard: TtkSec<threshold, only for MajorBurst/Transformation/Summon/WindowDriven. Provisional never holds minors.
Grace hold (only fail-open exception): usage MajorBurst/Transformation/Summon, neither valid nor provisional, FastPackLatch true, AgeSec<4 → Hold reason "fast pack, waiting for TTK". Never fires without latch.
Kill-secure exception (research: pop major at TTK<=20s to secure): bypasses waste guard when ability is MajorBurst/Summon or curated killSecure:true, AND Valid, AgeSec>=20, TargetHpFrac<=0.35, TtkSec 3..20. Rationale: long fight ending is not trash; Age separates the two.
Execute carve-out: ExecuteRange now also bypasses waste guard, with TtkSec>=3 or unknown.
Defensive tiers (DyingTargetSec → per-tier lookup): Solo: Minor TTK<6, Major TTK<10, Immunity TTK<15. Group: Minor TTK Valid<4 only + urgency below Orange + FastPackLatch required; Major/Immunity never gated. Emergency always overrides. Ladder-band carve-out: Major bypasses hold when HP<=SoloMajorHpPct, Immunity when HP<=SoloImmunityHpPct. Group rule rationale: enemy count unobservable, tank may be dying to other mobs.

## 3) Signals
Phase 1 uses only: TargetHpPct band, HasTarget, frame clock (age), InCombat, curated minTtkSec/killSecure/executeBelowPct.
Rejected: absolute enemy HP vs level (secret in Midnight, only coarse band on wire). Rejected: enemies targeting player (no observable signal). Elite/boss not on wire today; MDB.IsBossTarget bridge-local only for CC walk. Phase 2 optional: cell 29 R bits 2-3 = target class — separate L-route spec.

## 4) Toggle semantics
Phase 1: document+rename, do NOT invert (every other toggle OFF=automation off; flipping makes TTK ON=fire everything). Defect is collateral blinding: band 15 kills execute+Burst consumers. Changes: rename to "TTK guard" in /mdb help, hero toggle, tooltip ("OFF = cooldowns fire without dying-target protection; target HP band hidden from companion"). PROTOCOL.md §201 note. Companion setting [TimeToKill] Fallback=FailOpen|ConserveMajors, default FailOpen. ConserveMajors applies grace-hold to any unknown-TTK Major with no latch needed.

## 5) File-by-file changes (Phase 1, disjoint sets)
1. Knowledge/TtkEstimator.cs — provisional paths, latch, AgeSec, extended record, constants.
1. Intelligence/CombatContext.cs — new fields, extended WithTtk, clone copy.
2. Knowledge/TtkPolicy.cs — thresholds, Classify/guard helpers, grace-hold, kill-secure, tiered defensive lookup. Update stale header.
3. Knowledge/CandidateProviders.cs — waste-guard call with provisional + execute carve-out; grace-hold branch; defensive T4 tier/group/band carve-outs. Burst preset unchanged (requires Valid, document).
3. Knowledge/AbilityModel.cs + AbilityCatalog.cs — optional killSecure bool, parsed like executeFavored.
4. Knowledge/abilities.json — killSecure:true on confirmed majors only (~8 entries). Review ~10 curated minTtkSec vs new defaults.
4. Settings — AppSettings.cs + settings.ini [TimeToKill] Fallback; tracked default only.
5. Telemetry + replay — additive ttkp/latch fields; ReplayRunner rebuilds estimator from (ts,hasTarget,valid,band).
6. Tests — TtkEstimatorTests, TtkPolicyTests, TtkCurationTests. New replay fixture ttk-trash-pack.jsonl (6 mobs each <5s). Legacy fixtures 0 mismatches.
7. Docs — docs/KNOWLEDGE.md TTK section, docs/TESTING.md OWED live items, HANDOVER.md, ARCHITECTURE.md TTK sections.
7. UI+Lua labels — toggle rename MainForm.cs + Bridge.lua /mdb help; label only, no behavior change.

## 6) Acceptance (offline)
dotnet build -c Release 0 warn/0 err; dotnet test -c Release (tests\MaxDpsCompanion.Tests) green; lua tests/secret_harness.lua; luac -p addon/MaxDpsBridge/*.lua; pwsh tools/ability_audit.ps1 exit 0; all replays 0 mismatches.
Behavioral: 6 mobs <4s → after 2nd kill Major at age<4s held (grace-hold), minors not held. First sight band 2 in combat → provisional ~2.3s, MajorBurst held. Drop band 14→10 in 1s → provisional frac/0.267, Major held. Boss series TTK 120s never holds, latch clears. Kill-secure major fires at age 25s band 4 TTK 8s; same held at age 5s. Execute-favored at 30% fires despite TTK 2s; at TTK<3s guard still holds it. Solo Minor defensive held at TTK 4s, emergency overrides. Group Major never held. Solo Major at HP<=major band not held. AoeOnly never gated. Estimator deterministic.

## 7) Risks / owed live
False holds on provisional/latch (mitigate: majors only, expiry, latch clears on long target; owed live: M+ trash then boss, telemetry no Major held vs boss). Coarse band 6.7% noisy (3-band min heuristic; tune from telemetry). Group defensive gating could hide needed Minor (requires latch+valid<4s, never Major/Immunity; owed live: dungeon tank). Hard rules: estimator+policy pure C#, no memory reads/injection, PostMessage unchanged. Phase 1 no Lua behavior beyond help-string label. vendor/ untouched. settings.ini tracked default only. All offline; live retail OWED, build never closes it.
