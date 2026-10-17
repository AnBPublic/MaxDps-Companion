# WHITE Main immediate execution (2026-10-04)

## Intent
User directive: all MaxDps WHITE core-rotation (Main slot, SpellsGlowing via GlowNextSpell) executes IMMEDIATELY when enemy in range + in sight + castable. Only game-forbidden holds: paused/OOC/no-target, GCD, range, cast/channel, melee, target, Main toggle off, power unusable, user Never/Manual, registry delegation, OS blockedUntil, candidate TTL. Remove all companion-strategy Main delays: stale demotion, MinInterval (except identical-repeat), Main retry cap 400ms/3-sends, failure suppression ladder. No wire/PROTOCOL change. Bridge untouched (its Main gates are game truth). Offensive keeps TTK/burst/pair.

## Contracts
- C1 Stale exemption: ActionScheduler.cs:639 `entry.Candidate.Slot is not (Slot.SelfHeal or Slot.Main) && IsStale(...)`; DecisionEngine.cs:87/:103 Main never stale-demoted, no confidence penalty, never behind fresh Offensive/Trinket/Consumable.
- C2 MinInterval: new helper `MinIntervalApplies(slot, candidate, ...)` replaces ActionScheduler.cs:745-747 condition. False (no hold) for Interrupt + Main when !_hasSent, or last send different slot/stroke/spell. True only for identical slot+stroke+spell repeat inside interval (double-fire guard before OnGcd flips). New private `_lastSentSpellId`, set in NoteSent, cleared in Reset. MinKeyIntervalMs default 120ms stays.
- C3 Main retry+suppression: MainReprobeMs 400->150; Main _attempts counts only sends with !_pendingConfirm.SawGcd (GCD-observed send resets count); NoteFailure slot==Main: no _failedUntil write, keep counters+telemetry+RejectionsDetected++. Non-Main ladder (1.5/3/6/10s, 5-per-1.5s cap) untouched.
- C4 Policy: no change to Main branches PolicyEvaluator.cs; add pin test Main never Hold/Skip from TTK/burst/pair/waste/own-history.
- C5 Docs: HANDOVER.md + ARCHITECTURE.md (scheduler gate table) + docs/TESTING.md owed live. PROTOCOL unchanged.

## Tasks (disjoint, contracts first)
- T1 spec this doc (done).
- T2 scheduler one writer: C1+C2+C3 in Scheduler/ActionScheduler.cs (+SchedulerModel.cs only if _lastSentSpellId needs exposing, not expected).
- T3 decision: C1 in Decision/DecisionEngine.cs:87,103 only (parallel with T2, different file).
- T4 tests after T2+T3: new tests/MaxDpsCompanion.Tests/MainImmediateTests.cs — stale Main ahead of fresh Offensive; Main no MinInterval wait after different-slot/spell; identical repeat held exactly MinKeyIntervalMs; Main failure sets no _failedUntil; SawGcd resets cap; non-Main backoff unchanged; GCD/cast/range/melee/NoTarget still hold; fallback Main identical; update old-backoff-pinning tests (MainReprobeMs=400, 1.5/3s ladder, stale case).
- T5 docs parallel T4: HANDOVER.md + ARCHITECTURE.md + docs/TESTING.md (not PROTOCOL).

## Double-press safety
- GCD gate (ActionScheduler.cs:712, PixelProtocol.cs:140) blocks repeats while OnGcd. Pre-flip window covered by C2 identical-repeat hold. Refused key (resource/immune): <=1/150ms, <=3 sends w/o GCD. Off-GCD Main: <=1/MinKeyIntervalMs. Movement/physical-hold guards RotationEngine.cs:915-932 stay.

## Coexistence
- Offensive ranked after Main (RankFor unchanged); off-GCD Offensive fires while Main GCD-held; C2 stops Offensive send delaying different Main spell; Offensive TTK/burst/pair/waste intact. Fallback (MainFallback.lua) same cell inherits. Legacy [Scheduler] Enabled=0 path (RotationEngine.cs:987-993 _lastAnyPress) unchanged scope.

## Verify
1. `dotnet build app/MaxDpsCompanion/MaxDpsCompanion.csproj -c Release` 0/0. 2. `dotnet test -c Release` (tests dir) all pass (STA flake acceptable only standalone-green). 3. `lua tests/secret_harness.lua` pass. 4. `luac -p addon/MaxDpsBridge/*.lua` (+Exp) clean. 5. `pwsh tools/ability_audit.ps1` exit 0. 6. `git diff --stat` no vendor/PROTOCOL change.

## Risks + owed live
- RotationEngine.cs tick/poll/KeyPressMs/_lastAnyPress unread — may hold Main. CrowdControlVetoes PolicyEvaluator.cs:527 Main applicability unconfirmed. Mis-bound Main: ~6/s spam visible not gameplay risk; old backoff was Arms-stuck mitigation => regression possible.
- OWED retail 12.1: Main fires first tick post-GCD in range/sight; no double-press per GCD; blank-glow fallback immediate; Offensive/Main interleave; no-target/OOR/casting holds; refused key capped.
