# TTK Intelligence (v3.2.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax. Commit per task on **your own branch only**. Never push.

**Goal:** Stop wasting cooldowns. The companion estimates per-target time-to-kill (TTK) and uses it to (a) HOLD major offensive CDs fired into targets that die before the CD pays off, (b) FIRE (bypass pairing holds) when the fight is long enough for two full uses or the target is in execute range, (c) HOLD non-emergency defensives in Solo when the target dies imminently.

**Architecture:** Two parallel workstreams, disjoint file ownership, frozen contract below. Merge order **T-B → T-A** (data first, then code's full suite runs over it). No wire change. No Lua behavior change except target-band freshness if T-B finds a gap.

**Tech:** .NET 8 WinForms, xunit, Lua 5.1 + retail 12.1 APIs, PowerShell 7, Tavily/web for CD research.

## Global Constraints (same as v3 plan, plus)

- `vendor/**` read-only. No protected Lua. No secret compare/arith/stringify/key.
- Build bar 0 errors, 0 new warnings (2 pre-existing CS0108 in untouched `Ui/Layout.cs`).
- All existing tests green; all 6 legacy fixtures replay 0 mismatches unregenerated.
- `--bench-scheduler` pin `sends=1620 sha256=b71a999d5e46570e` unchanged or tick-explained.
- `tools/ability_audit.ps1` exit 0. `Catalog.lua` never hand-edited (no regen needed — new fields are companion-side only).
- No pushes. Stop rule: same error twice / 3 tries → handover packet.
- No live-validation claims. Live OWED.

## 1. Research summary (why TTK, with sources)

- **SimC APL** (`simulationcraft/simc` wiki ActionLists): `potion,if=target.time_to_die<=25`; `bloodlust,if=time_to_die<60` (60s before fight end); `recklessness,if=time_to_20pct>180` (fires only if it comes back for execute); fight length decides how many CD uses fit — a 3-min CD in a 3-min fight pressed twice wastes the second press. Warriors stack CDs together (`sync=`).
- **Hekili** (retail rotation helper, now discontinued for Midnight): tracks `target.time_to_die` (own target) + `time_to_die`/`fight_remains` (last engaged enemy) via a damage-tracking DPS estimate; non-boss TTD seeds at 15s (not 300s). Known failure: overly conservative TTD (8+s shown on mobs dead in <1s) — lesson: clamp, require minimum observation, fail open when unknown.
- **Classic TTK weakauras** (e.g. WarriorBossTTK): if boss dies in ~30s, DON'T pop Death Wish (it would still be up at the kill = wasted); at ~40s, pop it (covers the last 30s).
- **M+ heuristics** (raider.io, class guides): biggest DPS loss is holding CDs too long — default is use-on-cooldown; space pulls so CDs align with hard encounters; don't overlap a major with an already-covered damage event; hold only if another uncovered event is coming soon.
- **Defensive rule** (wownoob thread, holy-paladin M+ guide): personal defensives exist to prevent YOUR death; optimal to NOT press if incoming damage won't kill; stagger coverage across upcoming events.

## 2. Repo audit (what exists, what is missing)

- ON WIRE already: target HP band 0..14 + 15=unknown (cell 29; `Reader.lua:1397-1402`), decoded `TargetHpPct` (`PixelProtocol.cs:498-499,609`), context `TargetHpValid/TargetHpPct` (`CombatContext.cs:74-75,186-187`).
- MISSING: no damage tracking, no DPS estimate, no TTK/TTD anywhere (grep confirms zero hits).
- ADJACENT (reuse, do not duplicate): `HoldForBurst` + `PolicyMemory.OffensiveActive(ConflictGroup)` (paired-window hold); `EnemyCountMin` (unobservable → Uncertain for non-MaxDps); `OpportunityCost`; `MinimumUrgency`; `DefensiveEscalateHpPct`; conflict-group pairing. R1 offensive gap-fill + R2 sustain-CD are merged (v3.1.0).
- KEY INSIGHT: boss classification is NOT observable and NOT needed. The TTK estimate itself separates trash (dies fast → hold majors) from bosses (lives long → fire). Unknown TTK fails open (fire) — holding on unknown is exactly the "holding CDs too long" DPS loss.

## 3. Frozen contract (both workers implement exactly this)

### 3.1 Estimator (`Knowledge/TtkEstimator.cs`, new, pure, fake-clock)

- Inputs per tick: `nowMs (long)`, `hasTarget (bool)`, `targetHpValid (bool)`, `targetHpBand (0..14, 15=unknown)`.
- `frac = (band + 0.5) / 15.0`.
- RESET (epoch++, clear EWMA) when: `!hasTarget`; `!targetHpValid` continuously for >10s (track unknown-since; keep last estimate but mark stale until then); `frac` jumps UP by >0.12 (new target or big heal — re-learn).
- FEED on HP decline only: `inst = (prevFrac - frac) / dtSec`; if `inst < 0.004` ignore for EWMA (noise/heal; still update prev). Else `ewma = α·inst + (1-α)·ewma`, `α = 1 - exp(-dt/3.0)` (3s half-life).
- VALID when: ≥2 fed samples AND observation span ≥2.5s AND `ewma >= 0.004` frac/s. Else invalid.
- `TtkSec = clamp(frac / ewma, 0, 300)`. Below min rate → 300 ("long").
- Output: `(bool Valid, double TtkSec, double TargetHpFrac)`.

### 3.2 Schema (abilities.json, curated by T-B, parsed by T-A)

- `minTtkSec` (number, optional): don't fire this ability when valid TTK < value. Absent → tier default (§3.3).
- `executeBelowPct` (number 0..100, optional, default null = off): target-HP% at/below which this burst is favored (execute).
- `executeFavored` (bool, optional): allows the execute-bypass rule; without it `executeBelowPct` is inert documentation.
- Parsing in `AbilityCatalog.cs` + model fields in `AbilityModel.cs` (T-A). Audit must stay clean (T-B verifies).

### 3.3 Defaults (`MinTtkSec` by `OffensiveUsage`, T-A, when curated absent)

MajorBurst 12, Transformation 20, Summon 20, WindowDriven 10, ShortCooldown 5, ProcDriven 5, AoeOnly 5, SingleTargetOnly 5, ResourceDriven 5, DefensiveOffensiveHybrid n/a (defensive path), unknown usage 10.

### 3.4 Gates (providers, T-A)

- **T1 waste guard** (Offensive, gap-fill AND MaxDps-sourced): TTK valid && `TtkSec < minTtk` → Hold `"target ~{X}s to die; saving {name} (needs {N}s)"`. (MaxDps re-suggests next tick; no lockout. Waste is waste in all modes.)
- **T2 two-uses** (Offensive): TTK valid && `TtkSec >= 2·cdSec + durSec` (cdMs/durMs curated, absent cd → skip rule) → bypass pairing hold (`OffensiveActive`) and fire. Rationale (SimC): a full second pairing opportunity will recur.
- **T3 execute** (Offensive): `executeFavored` && target HP% valid && `<= executeBelowPct` → bypass pairing hold and fire. Rationale (SimC `time_to_20pct` family).
- **T4 dying-target** (Defensive): **Solo mode only** + NOT emergency + TTK valid && `TtkSec < 6` → Hold `"target dies in ~{X}s; saving defensive"`. Emergency HP always overrides. (Group scope is a follow-up: other enemies may exist, enemy count unobservable.)
- T5: nothing new for AoE — `EnemyCountMin>1` stays Uncertain (still unobservable).
- Reasons always cite TTK seconds + source. Scheduler rank/one-per-tick unchanged.

### 3.5 Plumbing (T-A)

- `RotationEngine` owns one `TtkEstimator`; feeds it every tick from the decoded frame (before policy); exposes `TtkValid/TtkSec/TargetHpFrac` into `PolicyInput` (or `DecisionContext` — T-A decides, documents).
- `[TimeToKill] Enabled=1` kill-switch (default ON; when 0, estimator off, all TTK gates skipped). Added to `AppSettings.cs` + `settings.ini` + `AppSettingsTests` (T-A). One Modes-card toggle row in `MainForm.cs` (T-A, add-only).
- Telemetry additive `ttk` (seconds, 1 decimal, omitted when invalid) on policy records; verdict evidence cites TTK. `ReplayRunner` reconstructs the estimator and feeds RECORDED `(timestamp, targetHpValid, band)` in order → 0 mismatches by construction.
- New fixture `tests/MaxDpsCompanion.Tests/fixtures/ttk-warrior-burst.jsonl` replays 0 mismatches.

## 4. Workstream T-A — estimator + gates + plumbing (branch `v3/ttk-a`)

**Owns:** `Knowledge/TtkEstimator.cs` (new), `Knowledge/AbilityModel.cs`, `Knowledge/AbilityCatalog.cs` (parsing only), `Knowledge/CandidateProviders.cs`, `Knowledge/PolicyEvaluator.cs` (or new `TtkPolicy.cs`), `Intelligence/CombatContext.cs` (TTK passthrough fields only), `RotationEngine.cs`, `Decision/*`, `Scheduler/*` (read-only unless a verdict needs carrying — prefer `PolicyInput`), `Telemetry/*` (ttk field + replay feed), `AppSettings.cs` + `settings.ini` + `AppSettingsTests` (TimeToKill key only), `MainForm.cs` (ONE Modes toggle row, add-only), new tests `TtkEstimatorTests.cs`, `TtkPolicyTests.cs`, fixture + replay test, `docs/TELEMETRY.md` (ttk field), `docs/KNOWLEDGE.md` § TTK estimator + gates.
**Must NOT touch:** `abilities.json` (T-B owns all curation), `Catalog.lua`, `addon/**` Lua (no Lua changes), `Ui/**` (except nothing), vendor.

- [ ] **A1 Estimator TDD.** `TtkEstimatorTests`: scripted HP series — steady decline → converges to true TTK ±20%; target switch (frac jump +0.2) → resets to invalid; heal (+0.15) → resets; no-target → invalid; unknown band 15 for 11s → invalid; coarse-band staircase still converges; 1-tick noise ignored; clamp 300 on trickle; determinism (same series → identical output). FAIL first, implement §3.1 exactly.
- [ ] **A2 Schema parse.** `minTtkSec/executeBelowPct/executeFavored` → model + catalog parse + inspector line (follow `HoldForBurst` plumbing as template). Tests: present/absent/defaults.
- [ ] **A3 Engine wiring.** Own estimator, feed per tick pre-policy, expose on input; `Enabled=0` skips. Tests: disabled → gates skipped; feed order (send events don't disturb estimator).
- [ ] **A4 Gates T1–T4** per §3.4 with reason strings. Tests per rule incl: MaxDps-sourced offensive held into 5s target; two-uses bypass (cd 120s, TTK 300s → Use despite active pair); execute bypass; Solo defensive T4 hold + emergency override; non-Solo defensive unaffected; unknown TTK → all gates pass-through.
- [ ] **A5 Telemetry + replay.** `ttk` field; replay feeds recorded target series; new fixture (trash-hold Avatar / boss-fire / execute / solo-T4) 0 mismatches; 6 legacy fixtures 0 mismatches.
- [ ] **A6 Settings + toggle.** Key, tests, Modes row. Commit `ttk-a:` per task.

**A acceptance:** build 0 new warnings; `dotnet test` green; audit exit 0; 7 replays 0mm; bench pin unchanged; `ttk` in telemetry docs.

## 5. Workstream T-B — curation + validation (branch `v3/ttk-b`)

**Owns:** `Knowledge/abilities.json` (minTtkSec/executeBelowPct/executeFavored ONLY — no other edits), schema-conformance test file (new `TtkCurationTests.cs`), `tests/secret_harness.lua` + `addon/MaxDpsBridge/{Reader,Bridge}.lua` ONLY if a target-band freshness gap is found (prove with failing harness test first; else no Lua changes), `docs/KNOWLEDGE.md` § TTK curation (append-only section), `docs/research/TTK_CURATION.md` (per-class sources, new file allowed).
**Must NOT touch:** any C# except the new test file; `AppSettings.cs`, `settings.ini`, `Catalog.lua` (no regen needed — fields are companion-side), `MainForm.cs`, vendor.

- [ ] **B1 Research durations.** For EVERY MajorOffensive/Transformation/Summon entry: verify `cdMs`/`durMs` against wowhead / warcraft.wiki.gg / method class guides (12.1/Midnight); where current values conflict, correct them with `sourceUrl` (follow existing convention). Record per-class source list in `TTK_CURATION.md`. (Tavily/web tools available.)
- [ ] **B2 Curate TTK fields.** `minTtkSec`: start from §3.3 defaults; raise for long-setup summons (Army 8min → 30), lower for 45–60s ShortCooldowns that read as waste-safe (→ 5). `executeBelowPct` + `executeFavored`: only where guides confirm an execute synergy (e.g. warrior Execute-window burst); default OFF everywhere else — sparse, not blanket.
- [ ] **B3 Conformance test.** `TtkCurationTests`: every MajorOffensive/Transformation/Summon has explicit `minTtkSec` OR is on a documented-default list; every `executeBelowPct` has `executeFavored:true` and 0<pct≤35; every curated id verified in `spell-verification.json` with matching name. Audit exit 0 (new fields must not trip invariants — check `AbilityIntelligence` audit rules first).
- [ ] **B4 Target-band freshness.** Verify `GetTargetContext`/`HealthPct("target")` is re-read per tick with no not-ready cache (read code; if cached, failing harness test first, then fix + `SPELL_UPDATE_*` invalidation check). If clean, report "no Lua change needed" with file:line evidence. Commit `ttk-b:` per task.

**B acceptance:** audit exit 0; new conformance tests green; full `dotnet test` green in its worktree (over T-A's absence the parse fields are inert — data-only); harness count ≥153; curation doc complete.

## 6. Merge + verify + review (router)

Order **T-B → T-A** into `v3/base`; resolve (KNOWLEDGE.md append-sections auto-merge expected). Full suite: build, `dotnet test`, harness, `luac -p`, audit, 7 replays, `--bench-scheduler`, `--ui-smoke-test`, `--bench-ui`. Review diff adversarially. Docs: HANDOVER v3.2.0 + ARCHITECTURE pipeline/invariants + README paragraph. Version 3.2.0 (csproj/VERSION.txt/addon VERSION.txt/.toc). `build.ps1` + `install-addon.ps1` (router runs install itself). No push without approval.

## 7. Live checklist (user, ~25 min, OWED)

Trash pack: majors held (telemetry `waiting on TTK`); boss: burst fires; execute: favored CD fires ≤threshold; Solo: defensive saved when last mob <6s; `/reload` only (addon pre-installed by router); record+export+replay 0 mismatches.

## Prompt template (fill <WS>/<branch>/<path>)

You are implementing Workstream <WS> of docs/plans/2026-09-29-ttk-intelligence.md in worktree <path> on branch <branch>. Read AGENTS.md, HANDOVER.md (top only), ARCHITECTURE.md, then your section (§4 or §5). Global Constraints (§Global) mandatory. Edit ONLY owned files (`git status --porcelain` before every commit). TDD per task, commit `<ws>: <task> <summary>`. Never push, never vendor, never hand-edit Catalog.lua. Final report ≤300 words: commits, every acceptance command + last 15 output lines, deviations, risks + live OWED. Stop rule: same error twice / 3 tries → handover packet.
