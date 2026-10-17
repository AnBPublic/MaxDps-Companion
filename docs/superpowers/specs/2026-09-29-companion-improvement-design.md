# MaxDps-Companion Improvement Plan — Audit + 4 Parallel Streams

Date: 2026-09-29 | Status: PLAN ONLY (no code changed) | Base: v3.3.0 (585 xunit + 186 Lua harness, live OWED)
Priorities: 1) Solo survivability + usability, 2) perf + bridge reliability, 3) DPS accuracy (+ app design polish as support)
Constraint: safety rails INTACT — suggest-only PostMessage, pixel-strip bridge, no protected Lua, vendor/ read-only, single MaxDpsCompanion.exe. Iterative only. Wire protocol FROZEN (v5+Ext2 40-cell, additive-only, no version bump).

## 0. Where we stand (audit summary)

Pipeline: MaxDps engine (vendor/, read-only) → MaxDpsBridge addon (Bridge.lua + Catalog.lua generated + Reader/Bars/Keymap/Toggles/Panel, 40-cell strip) → MaxDpsCompanion.exe DIB BitBlt sample @ PollIntervalMs (ScreenSampler → PixelProtocol decode → BridgeFrame/CombatContext → CandidateTracker → TTK estimator → PolicyEvaluator/AbilityPolicy → DecisionEngine → ActionScheduler → KeySender PostMessage).

Strengths: additive protocol compat (v5+Ext2/v5/v6/v4/v1 decode), restrict-only 13-toggle gate (fails open, pcall-contained), Solo HP ladder (Minor<=75/heal<=65/Major<=50/Immunity<=30), TTK EWMA estimator, telemetry replay, offline bar green.

Gaps found:
- Solo: DH Havoc/Devourer self-heal NONE; Turtle/ShadowMend/SoulImmolation/TemporalRealignment IDs unverified-owed; band-edge flapping (no hysteresis); per-ability veto exists in policy but not surfaced in /mdb + companion UI; OOC/Solo gates can false-positive on unknown HP/group.
- Perf/reliability: Bridge OnUpdate per-tick full recompute (no dirty-flag); ScreenSampler single-DIB BitBlt + 5-tap median untuned; no SchedulerBench regression gate in CI-loop; calibration skew warning copy weak; stale-addon detection warns but doesn't guide to install-addon.ps1+/reload.
- DPS: TTK EWMA (3s) + T1–T4 gates untuned vs Hekili forward-sim standard; Offensive gap-fill source derived by id-membership (no explicit wire bit — parity gap vs Defensive source bit cell31B-bit0); no Burst/Full or ST/AoE preset (ConRO lesson); SelfHeal2 alternate evaluation exists but rank/timing review owed.
- Design: Ui/ DesignTokens/Layout/SettingsPages functional but dated (WinForms); SpellIconCache staleness; no suggested-vs-cast audit view (telemetry exists, no UI); ability_audit covers catalog but no UX copy review.

Research borrow-list (staying inside rails): Hekili per-spell toggles + shareable profiles + primary+2 queue display (companion-side only, no in-game queue); ConRO Burst/Full + ST/AoE + group toggles as companion policy presets; Ovale event/dirty-flag recompute; GSE spam-key ms timing only as documented manual-aid numbers (companion stays suggest-only); WeakAuras movable/scalable minimal in-game footprint (Panel overlay stays off pixel path); second-PC/hardware-key pattern NOTED but REJECTED (risky, out of rails).

## 1. Stream 1 — Solo survivability + usability (P1)

Goal: never die solo to a missing/insistent gap-fill; player can see + restrict every solo decision in-game or in app.
Tasks:
1.1 Catalog gap-fill (generated Catalog.lua via Knowledge/CatalogLuaGenerator + AbilityCatalog): verify-then-add owed IDs against live 12.1 DB2 ONLY (Turtle 186265, Shadow Mend, Soul Immolation, TemporalRealignment 1244090); DH self-heal: keep NONE until verified — surface "no verified solo self-heal" in UI instead of inventing. Run tools/ability_audit.ps1 → 0/0/0/0.
1.2 Band hysteresis: SoloMinor/Major/Immunity thresholds get enter/exit gap (e.g. enter <=X, exit >=X+5, companion-side AbilityPolicy + PolicyOptions.ValidateSoloBands); prevents heal/major flap at 75/50/30 edges. Telemetry sesc/smin/smaj/simm unchanged (omitted when default).
1.3 Veto surfacing: per-ability ON/OFF veto already in policy — expose in Panel.lua checkboxes (grouped: SelfHeal/Defensive/Immunity) + companion ClassSkillsView/AbilityExplorer with same labels; effective = companion AND addon (addon OFF wins, missing key = ON).
1.4 Gate false-positive reduction: Toggles SlotAllowed OOC/Solo unknown-HP/group fails open (keep); add "unknown" reason to MDB._LastBlank + `/mdb why heal` copy ("unknown HP — allowed"); no wire change.
1.5 Usability copy: `/mdb toggles` status line already `toggles=N/13 ON; OFF: ...` — add one-line `why` hint; companion SettingsPages Solo section mirrors Minor/Major/Immunity sliders with 40-99/20-90/5-60 ranges.
Files (exclusive): addon/MaxDpsBridge/Catalog.lua (generated), Toggles.lua, Panel.lua, app/.../Knowledge/AbilityCatalog.cs, AbilityPolicy.cs, CatalogLuaGenerator.cs, ClassSkillsView.cs. NO PixelProtocol.cs, NO ScreenSampler.cs, NO Scheduler/*.
Verify: dotnet build -c Release (0/0); dotnet test -c Release; lua tests/secret_harness.lua; luac -p addon/MaxDpsBridge/*.lua; pwsh tools/ability_audit.ps1. Live OWED: retail solo HP sweep 80→25%, each veto OFF blanks, /mdb why heal reason, persist across /reload.

## 2. Stream 2 — Perf + bridge reliability (P2)

Goal: lower CPU + latency, kill stale/skew outage class, make recalibrate the obvious repair.
Tasks:
2.1 Bridge throttling + dirty-flag: Bridge.lua OnUpdate — cache per-tick toggle ctx; recompute slot intents only on dirty (MaxDps suggestion change, HP band change, combat/group change, toggle change); otherwise rewrite identical strip (cheap vertex-color writes). Keep every probe pcall-contained, fail-open. Measure via SchedulerBench before/after.
2.2 Sampler tuning: ScreenSampler.cs — keep single-DIB BitBlt; tune PollIntervalMs default vs 5-tap median threshold (≥2px cells); BlockLocator anchor/size + DPI/scale guard; ColorLearner tolerance ±8 default preserved unless [Color] profile overrides. No decode logic change.
2.3 Stale/skew UX: app warns (never hard-fails) on version/width skew; copy points to install-addon.ps1 + /reload + calibration wizard; companion auto-suggests recalibrate when checksum/commit mismatch rate exceeds threshold (existing counters, new copy only).
2.4 Regression gate: SchedulerBench recorded baseline; fail stream if p95 frame cost regresses >10% (bench only, no live claim).
Files (exclusive): addon/MaxDpsBridge/Bridge.lua, Bars.lua, Reader.lua, app/.../ScreenSampler.cs, BlockLocator.cs, ColorLearner.cs, ColorProfile.cs, Scheduler/*. NO Knowledge/*, NO Ui/*, NO Decision/*.
Verify: same offline bar + SchedulerBench comparison quoted. Live OWED: retail 10-min idle + combat sampler CPU + missed-heartbeat count.

## 3. Stream 3 — DPS accuracy + app design polish (P3 + design)

Goal: squeeze rotation accuracy from existing cells; modernize companion UI without new exe or protocol.
Tasks:
3.1 TTK tuning: Knowledge/TtkEstimator.cs + TtkPolicy.cs — review EWMA 3s seed, >10s-unknown reset, >0.12 upward-jump reset, (band+0.5)/15 frac; T1–T4 gate thresholds vs telemetry replays (ReplayRunner). Invalid estimate still fails open. No scheduler signature change (WithTtk path).
3.2 Offensive source parity (companion-side ONLY): Decision/CandidateTracker + PolicyEvaluator derive offensive gap-fill by id-membership (documented); add explicit derived flag in CombatContext/telemetry (no wire bit — wire frozen). Defensive path byte-identical.
3.3 Mode presets (restrict-only, companion-side): Burst/Full + ST/AoE preset switch in AppSettings + SettingsPages (e.g. Burst = hold majors/consumable/trinket unless TTK-valid boss; AoE = prefer curated AoE-ranked candidates where catalog already ranks). Addon can only further restrict via existing toggles. Default = current behavior.
3.4 Design polish: Ui/DesignTokens/Layout/SettingsPages/Pages — spacing/type/contrast pass, Solo + Modes sections, empty-states ("no verified DH self-heal"), skew/stale banner style; SpellIconCache staleness fix (TTL + invalidate on catalog regen); Telemetry suggested-vs-cast audit view (read-only grid over existing TelemetryRecorder/Reader JSON).
Files (exclusive): app/.../Knowledge/TtkEstimator.cs, TtkPolicy.cs, PolicyEvaluator.cs, CandidateProviders.cs, Decision/*, Ui/*, Telemetry/*, AppSettings.cs, RotationEngine.cs (TTK feed only). NO addon/*, NO PixelProtocol.cs, NO KeySender.cs.
Verify: offline bar + ReplayRunner on checked-in replays (no behavior claim beyond replays). Live OWED: retail target-dummy + M+ suggested-vs-cast log review.

## 4. Stream 4 — Guardrails + integration (runs alongside 1–3)

Goal: parallel streams merge clean with rails + docs intact.
Tasks:
4.1 Ownership enforcement: streams touch ONLY their listed files; shared files (HANDOVER.md, ARCHITECTURE.md, docs/PROTOCOL.md, settings.ini defaults) touched by Stream 4 only, in merge pass.
4.2 Protocol freeze check: diff docs/PROTOCOL.md + PixelProtocol.cs + KeySender.cs + Scheduler/** + Decision/** + Knowledge/** schema + addon/*.lua trigger list — any non-additive edit REJECTED back to stream.
4.3 Doc sync: after each stream merges, update HANDOVER.md (status/next) + ARCHITECTURE.md (pipeline/file map) in same pass (repo hard rule); settings.ini default stays tracked, per-machine edits in dist\ only.
4.4 Merge order (lowest-conflict first): Stream 2 (perf) → Stream 1 (solo) → Stream 3 (DPS/design) → full offline bar + ability_audit. Quote every command + output; static ≠ test ≠ live; live stays OWED until retail run (§3 docs/TESTING.md).
Files (exclusive): HANDOVER.md, ARCHITECTURE.md, docs/*.md, settings.ini, tools/*.

## File-ownership matrix (disjoint — safe for parallel chat streams)

| Stream | Owns | Must NOT touch |
|---|---|---|
| 1 Solo | addon Catalog/Toggles/Panel, Knowledge AbilityCatalog/AbilityPolicy/CatalogLuaGenerator, ClassSkillsView | PixelProtocol, ScreenSampler, Scheduler, Decision, Ui, Telemetry |
| 2 Perf | addon Bridge/Bars/Reader, ScreenSampler/BlockLocator/ColorLearner/ColorProfile, Scheduler/* | Knowledge, Decision, Ui, Telemetry, addon Catalog/Toggles/Panel |
| 3 DPS/Design | Knowledge (Ttk/Policy/Providers), Decision/*, Ui/*, Telemetry/*, AppSettings, RotationEngine TTK feed | addon/*, PixelProtocol, KeySender, ScreenSampler |
| 4 Guardrails | HANDOVER/ARCHITECTURE/docs/settings.ini/tools | implementation files except in merge pass |

## Acceptance + non-goals

Accept per stream: offline bar green (build 0/0, tests 585+new, harness 186+new, luac clean, audit 0/0/0/0), no wire change (nibble 5, 40-cell), vendor/ diff empty, single exe, live items filed as OWED with exact retail steps.
Non-goals (rejected): new wire version/queue cells, in-game next-2 overlay, memory-read/injection, automated gameplay decisions, second binary, committed per-machine settings, any protected-Lua call.

## Self-review (spec check)

- Placeholders: none — all thresholds/files/commands concrete; owed IDs explicitly gated on live DB2.
- Consistency: wire frozen in all streams; restrict-only invariant repeated; fail-open preserved; merge order respects disjoint ownership.
- Scope: single plan, 4 parallel streams, shippable today; cross-stream features (new urgency cell → new rule) deliberately excluded.
- Ambiguity: "tuning" bounded to listed constants/flags; "polish" bounded to listed Ui files + cache TTL + read-only audit view; no silent protocol bump path.
