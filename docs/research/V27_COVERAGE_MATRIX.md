# v2.7 Internal Coverage Matrix (pre-implementation audit)

Audited from the working tree (branch `master`, v2.6.0 uncommitted). Baseline reproduced:
`dotnet test` 361/361, `lua tests/secret_harness.lua` 88/88 (re-verified this session),
registry 3279 entries / 0 violations (v2.6 numbers reproduced).

No docs trusted; every line below traces to code. Line refs are from the audit session.

## A. What is actually implemented (code + tests + reachable at runtime)

| Area | Mechanism | Where |
|---|---|---|
| Bridge encode | 35-cell v5 strip, arg-blind cast sensors, HP/stagger/range/buff probes, extras walk (ready+bound), defensive gap-fill at Red only | `addon/MaxDpsBridge/Reader.lua:1355-1467`, `Bridge.lua:231-253,534-556` |
| Decode | v5/v6 35-cell + v4/v1 fallback; tri-state range/cast; additive urgency | `PixelProtocol.cs:393-480` |
| Context | `CombatContext` tri-state (HP valid, cast, target cast, melee, range) | `Intelligence/CombatContext.cs:106-128` |
| Candidates | `CandidateTracker` per-slot history → `ActionCandidate` (all 8 slots) | `Decision/CandidateTracker.cs:24-73` |
| Policy | Five-state verdicts; main range/cast; offensive buff/pair/count/melee/range; defensive v2.3 urgency matrix + MaxDps discount + gap-fill Red-only; interrupt vetoes; mobility melee/range; self-heal emergency (both modes) + solo layer + overheal; utility hold; consumable/trinket lockout | `Knowledge/PolicyEvaluator.cs:256-631` |
| Scheduler | link/protocol/hard gates; rank Interrupt > Emergency > Defensive > SelfSustain > Main > Mobility > Offensive > Consumable > Trinket; GCD; min-interval; stale + pending-confirm demotion; failure escalation 1.5/3/6/10 s; blocked-suppression; one action/tick | `Scheduler/ActionScheduler.cs:236-710` |
| Execution | OS gates + `PostMessage` WM_KEYDOWN/UP, auto-target/interact | `RotationEngine.cs:673-765,962-1040`, `KeySender.cs:24-57` |
| Telemetry/replay | JSONL tick/send + policy verdicts with reasons + options; replay recomputes 0 mismatches | `Telemetry/*`, fixtures |
| Registry | 3279 entries; statuses (ResearchBacked 75 / MaxDpsBacked 61 / CompanionRule 93 / ManualByDesign 79 / Incomplete 2971); audit 0 violations; structural enforcement (Incomplete/Unknown/Unsafe can't be companion-generated; Manual = structural OFF) | `AbilityModel.cs`, `AbilityCatalog.cs:642-679`, `AbilityIntelligence.cs:103-241` |
| User policy | `[Abilities] On/Off/Modes`; OFF absolute; ON eligibility-only | `AbilityPolicy.cs`, tests |
| CLI/tools | `--ability-audit`, `--ability-info`, `--gen-catalog`, `tools/ability_audit.ps1` (exit 0/1/2) | `Program.cs`, tools |

## B. Registry-only (classification exists; no runtime branch reads it)

| Field / layer | Reality |
|---|---|
| `InterruptKind` | audit + inspector only; runtime interrupt path is kind-agnostic |
| `OffensiveUsage` | audit + inspector; runtime derives from status for the enemy-count gate only |
| `MobilityKind` | inspector only; runtime mobility branch is purpose-based |
| `OpportunityCost` | registry/UI only |
| `HoldForBurst`, `TalentNote`, `HeroTalentNote`, `Relations` | recorded, never consulted by policy |
| `EnemyCountMin` | *partially* live: gates companion-backed offensives only (`PolicyEvaluator.cs:435-437`) |
| `CapabilityTags` | audit/UI only |
| `PatchVerified` | audit warning only |
| `MaxDpsRelationship` | registry/UI only |
| Class-spells tail (2971) | visible + toggleable; acted on ONLY when MaxDps itself suggests (pre-registry Generic path) |

## C. What can actually generate a candidate

- **MaxDps wire only:** Main, Offensive, Defensive (MaxDps flagged+bound), Interrupt, Consumable, Trinket (`Bridge.lua:534-550`).
- **Bridge extras (independent):** Mobility, SelfHeal — first ready+keybind-bound entry from the generated per-spec list (`Reader.lua:1355-1378`).
- **Bridge gap-fill (independent, Red-only):** Defensive when MaxDps names none, inside `enableDefensives` (`Reader.lua:1456-1467`).
- **App side: NONE.** No provider classes exist; `PolicyEvaluator` returns verdicts on candidates that already exist. There are zero policy-created candidates.

## D. What can actually reach the scheduler

All 8 slots via tracker → enabled filter (`[Spells]`, default Trinket+Interrupt OFF) → policy `Use` only.
Utility never reaches it (manual/Hold). Interrupt requires MaxDps to flag a live interruptible cast.
SelfHeal/Mobility are the only categories with companion-independent generation today.

## E. What can actually cause a keypress

Scheduled action → per-action OS gates → `KeySender.PostMessage`. Nothing else
(auto-target/interact use the same gates). Interrupt/Trinket are OFF by default.

## F. Verified v2.7 gaps (each maps to a workstream)

1. **Ownership / completeness / optionality:** absent from the model; `AuditSummary` conflates delegation with Incomplete.
2. **Delegation reasons:** absent; the 2971 MaxDpsOnly entries carry a hardcoded audit Info string, no reason.
3. **Manual reasons:** absent (79 manual entries).
4. **Patch metadata per entry:** only free-text `PatchVerified`; no introduced/lastValidated/source patch, source type/url/confidence.
5. **Coverage manifest:** no DISCOVERED/REGISTERED/AUTOMATABLE/... classification; missing/stale/duplicate ids unchecked; `ability_audit.ps1` doesn't enforce Violations==0 (test does).
6. **Research schema:** `registry-research.json` has raw free-text `source`; no structured provenance; only 120 of 3279 entries represented.
7. **Providers:** none exist; category logic is inline in `PolicyEvaluator`; no per-action provider/source identity.
8. **Explainability:** reason strings + `ScheduleReason`; no structured evidence (HP state, policy, MaxDps, deferred-main) for the UI §39 inspector.
9. **Context tri-state gaps (secret-safety):** `SlotBuffActive` is `bool[]` — a failed aura probe is indistinguishable from "buff absent" (documented fail-open); `TargetInMelee`/`TargetCasting` decode zero bits to `No` (documented wire contract; must stay documented).
10. **UI:** four sibling panels toggled ad hoc; no navigation shell; ~1908 px of repeated fixed cards in Advanced; duplicated settings (target/interact/combat-only); scattered raw colors and font sizes; no tab order/keyboard toggle activation; smoke test asserts nothing; no clipping/zero-size/overlap checks; no ability explorer/inspector/coverage dashboard; settings form rather than application.

## G. Behavior that must NOT regress (pinned)

bench `sends=1620 sha256=b71a999d5e46570e`; 361 xunit + 88 Lua checks; Five-state verdicts;
White-holds hard rule; OFF absolute; ManualByDesign absolute; one action per tick;
self-heal emergency priority; gap-fill Red-only; replay 0 mismatches; protocol v5 additive compatibility.
