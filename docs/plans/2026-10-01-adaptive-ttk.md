# Adaptive Real-Data TTK Spec (2026-10-01)

## Goal
Stop wasting offensive CDs (10s+ active) on enemies dying 2s later. TTK becomes determining factor, dynamically adjusted from real fight telemetry: rolling kill-time + burn-rate history of last enemies fought. Always adaptive, real data not estimates.

## Decision
No wire change (no damage/DPS field; bridge never reads it). History from RecordDeparture (TtkEstimator.cs:382-394) using HP-band data already measured. Frac-rate proxy for DPS.

## Design
- KillRecord(AtMs, LifeSec, StartFrac, EndFrac). Kill = departure with lastFrac<=0.25 and not unknown-HP reset. Valid only if lifeSec>=2 and startFrac-lastFrac>=0.15 (discard spawn-at-20% mobs).
- Window: last N=8 kills, max age 240s. Clear after 90s no-target + out-of-combat.
- Statistic: 75th-percentile burn rate rPq (nearest-rank), pessimistic so CDs hold more. Binding only if Count>=HistoryMinKills=3. No per-context (no boss/trash class on wire).
- Blend with live: w=clamp((spanSec-2.5)/6,0,1) (0 at 2.5s span, 1 at 8.5s). Valid live: rateEff=ewma+(1-w)*max(0,rPq-ewma). Invalid live but target+combat+frac known: rateEff=rPq, mark HistProvisional. HistTtkSec=clamp(frac/rateEff,0,300). After ~8.5s live wins (boss pull not throttled by trash).
- Adaptive need: needAdapt=max(policyNeedSec, min(HistoryDurFactor*activeDurSec,20)), HistoryDurFactor=0.5. Only while binding. 20s CD needs >=10s expected life. activeDurSec = provider T2 value (2*cd+dur)/1000.
- Hold rule: new T1 branch after execute + kill-secure carve-outs, reason ttk-hist. Hold if HistTtkSec<needAdapt. Invalid-live restricts to provisional-eligible (Major/Transformation/Summon/Window :109-113); valid-live applies to all T1 classes. Execute bypass, kill-secure, curated MinTtkSec unchanged. T4 unchanged (group Major/Immunity never).

## File changes (contracts-first, A first then B+C parallel then D)
Task A: Knowledge/TtkEstimator.cs + new Knowledge/KillHistory.cs — KillRecord struct; KillHistory(cap,maxAgeMs) Add/Prune/Clear/Count/RateQuantile(q)/MedianLifeSec (nearest-rank, 0 if empty, pure fake-clock); TtkOptions History/Kills/MinKills/MaxAgeSec/Quantile/DurFactor (defaults 1/8/3/240/75/0.5); RecordDeparture hook with filters; TtkEstimate :16-22 append defaulted HistBinding/HistKills/HistRate/HistTtkSec/HistProvisional/HistMedianLifeSec/NeedDurFactor; compute at :315-322.
Task B: AppSettings.cs (:163/169,:409-410,:615-618) + settings.ini (:83-85) — [TimeToKill] History=1 HistoryKills=8(3..20) HistoryMinKills=3(2..10) HistoryMaxAgeSec=240(30..900) HistoryQuantile=75(50..95) HistoryDurFactor=0.5(0..1). Clamp+parse like Fallback. Tracked defaults only.
Task C: Intelligence/CombatContext.cs (:83-110,:268-302,:177-187) — TtkHistBinding/TtkHistSec/TtkHistProvisional/TtkHistKills; WithTtk new optional param (keep old overload); Unknown false/0. Knowledge/TtkPolicy.cs — NeedAdaptive(baseNeed,activeDur,durFactor) pure static; WasteGuardHolds :143-152 ttk-hist branch. CandidateProviders.cs :284 pass activeDurSec+settings; :200-202 consumable/trinket Burst hold: invalid-live + binding history hold if HistTtkSec<5.
Task D: RotationEngine.cs (:776-787,:831,:976) options in + WithTtk fields; Telemetry tick hk/hr/hs/hp beside ttk/ttkp/age/latch (:282-309); ReplayRunner :82,:188-209 deterministic replay + hk/hs compare; HANDOVER.md + ARCHITECTURE.md same pass (KillHistory file map + history stage); docs/TESTING.md history line.

## Safe defaults (fail-open)
Enabled=0 or History=0 => old behavior. Count<MinKills, empty/stale history, no target => no effect. Legacy/Unknown fail-open. Fallback unchanged. History never holds on unknown HP.

## Verify
dotnet build -c Release (0/0); dotnet test -c Release from tests\MaxDpsCompanion.Tests; lua tests/secret_harness.lua; luac -p addon/MaxDpsBridge/*.lua; pwsh tools/ability_audit.ps1. Lua/audit unchanged expected.

## Unit tests
TtkEstimatorHistoryTests.cs + TtkPolicyHistoryTests.cs (fake clock): kill filter (life<2/delta<0.15/end>0.25 dropped); window cap 9th evicts 1st + 240s prune; quantile nearest-rank; Count<MinKills HistBinding=false + golden identical; blend w=0@2.5s w=1@8.5s rateEff=ewma@w=1; invalid-live+binding => HistProvisional; NeedAdaptive (10,25,0.5)=12.5 (10,60,0.5)=20; guard 20s CD HistTtk6 Hold / HistTtk14 Use, carve-outs bypass, group Major T4 never; settings parse+clamp; replay hk/hs reproduce.

## Risks + OWED live
Boss-after-trash opener delay up to ~8s (blend mitigates; History=0 escape). Frac-rate != DPS (no max HP; trash-learned rate mis-predicts big pools; live takes over). p75/N=8 heuristic needs retail tuning. Cold start <3 kills = today (deliberate). Open: boss flag from bridge? activeDurSec at :284? OWED retail: dungeon trash hold check; boss opener <=8s; telemetry hk/hr/hs export. Never claim live without retail.
