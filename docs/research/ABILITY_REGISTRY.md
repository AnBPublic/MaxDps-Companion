# Ability Intelligence Registry (v2.6.0)

The registry is the companion's machine-readable answer to "what is this
ability, and what do we actually know about it". It replaces the silent
generic default: every entry carries an explicit intelligence level, an
automation context, and per-capability rules. Anything without meaningful
intelligence says so and cannot drive automatic situational use.

OFFLINE VERIFIED on this machine 2026-09-28 (build, tests, replays, audit).
LIVE WOW VERIFICATION IS STILL OWED - see "Live validation" below.

## v2.7.0 update — coverage axes, reasons, patch metadata

v2.7 keeps every v2.6 field and adds two orthogonal disposition axes, so
ownership and completeness can no longer be conflated:

- **Ownership** — Companion / MaxDps / Shared / Manual / Unavailable.
- **Completeness** — Complete / Partial / Delegated / ManualByDesign /
  Unobservable / ResearchPending / LiveUnverified.
- **Delegation reasons** — mandatory for every MaxDps-owned entry
  (MainRotation / RotationOrdering / ResourceOptimization / ProcInteraction /
  TalentInteraction / ComplexBuffWindow / TargetSelectionUnavailable /
  EnemyCountUnavailable / SecretValueLimitation / MaxDpsAlreadyModelsThis /
  NotWorthDuplicating / InsufficientObservableContext); no bare MaxDpsOnly.
- **Manual reasons** — mandatory for every manual entry (CC target state /
  debuff identity / buff identity / threat state / ally state / raid
  coordination / safety rule / user choice / other).
- **Patch metadata per entry** — introducedPatch / sourcePatch /
  lastValidatedPatch / sourceType / sourceUrl / sourceConfidence /
  liveVerified; stale and newer-than-supported entries are audit violations.
- **Coverage manifest** — `--ability-coverage=<path>` +
  `docs/research/ABILITY_COVERAGE.json` classify discovered / registered /
  automatable / companion-generated / MaxDps-delegated / shared-gated /
  manual / unobservable / research-pending / stale / missing / filtered, with
  duplicate-display-name warnings. The audit of the pre-v2.7 implementation is
  `docs/research/V27_COVERAGE_MATRIX.md`; field derivations are in
  `docs/KNOWLEDGE.md`.

Counts move to: 3279 registered (Companion 47 / MaxDps 3077 / Shared 76 /
Manual 79), automatic 3200, delegated 3077, research-pending 2971,
live-unverified 123, violations/warnings/missing/stale 0.

## What counts as an ability (AbilityKind)

The catalog only ever carries player-usable combat abilities plus explicitly
curated manual utilities. `AbilityKind` exists so an audit correction can
classify an entry honestly instead of silently treating everything as active:

- `ActiveCombatAbility` - the normal case: castable in combat.
- `Passive`, `Aura`, `TalentModifier` - never appear as suggestions; when they
  materially change an active ability they are recorded as `TalentNote` /
  `HeroTalentNote` / `Relations` on the active entry, not as their own rows.
- `InternalSpell`, `QuestSpell`, `ProfessionSpell`, `NonCombatAbility`,
  `Deprecated`, `Unknown` - excluded from automatic behaviour; `Deprecated`
  ids are dropped at merge when the live-client verification marks them gone.

What does NOT count: vendor passive-talent long tail, professions, riding,
heirlooms, old-content perks (dropped by the `ClassSpellBook.IsJunk` filter);
removed-in-12.1 ids (dropped by the live-client verification); anything the
three source files below do not name.

## Canonical source (no duplication)

Three files, one merge order, one owner each:

| File | Role | Count (this pin) |
| :--- | :--- | :--- |
| `Knowledge/vendor-abilities.json` | GENERATED from `vendor/MaxDps/Cooldowns.lua`. Authoritative for ids, names, class/spec membership, coarse category. | 368 rows, 221 distinct ids |
| `Knowledge/abilities.json` | CURATED policy layer: purpose, tier, GCD, ranges, cooldown/duration hints, hold conditions, conflict groups, per-spec Mobility/SelfHeal extras, research `source`, per-capability intelligence. | 240 curated entries (was 146) |
| `Knowledge/class-spells.json` | GENERATED lowest-priority layer from the retail `ns.classSpellData` block (token + id only), filtered by `spell-verification.json`. | merged verified tail |

Merge order: curated > vendor > class-spells. `AbilityDefinition.Provenance`
records which layer won. Nothing is duplicated: extras carry only abilities
MaxDps never surfaces; vendor-surfaced sustains stay on the Defensive gate;
the defensive gap-fill list is DERIVED (`AbilityCatalog.DefensiveGapFill`),
not hand-listed.

Load-time patch guard: `AbilityCatalog.ExpectedGamePatch "12.1"`,
`ExpectedInterfaceVersion 120100`, `ExpectedMaxDpsVersion 11.3.49`,
`CatalogVersion 3`. A curated file stamped with another patch throws at load.
Generated `addon/MaxDpsBridge/Catalog.lua` is revision 3.

Merged registry on this pin: **3279 catalog entries** = 368 vendor rows
(221 distinct) + 240 curated entries + merged verified class-spell tail.

## Intelligence levels (IntelligenceStatus)

- `Verified` - cross-checked against live-client DB2 + a second current source.
- `ResearchBacked` - a current 12.1 research record exists (source + date).
- `MaxDpsBacked` - MaxDps itself carries the use intelligence (vendor category
  membership); the companion adds no rule and delegates.
- `CompanionRule` - a deliberate companion policy rule (curated defaults).
- `ConservativeSafety` - the rule exists only to prevent waste/harm; not a
  claim about optimal use.
- `Heuristic` - best-effort classification without a second source.
- `ManualByDesign` - never automatic; the user must decide.
- `UnsafeToAutomate` - automation is known to be dangerous.
- `Incomplete` - no meaningful intelligence yet (visible, never silent).
- `Unknown` - not classified at all (must be 0 for automatic entries).

Status counts on this pin: ResearchBacked 75, MaxDpsBacked 61,
CompanionRule 93, ManualByDesign 79, Incomplete 2971, Unknown 0.
Automatic (eligible) 3200 - of which 2971 are the `MaxDpsOnly` Incomplete
modeled tail; Manual / never-automatic 79.

Derivation rules (in the loader, not hand-stamped per row): vendor rows map
to MaxDpsBacked; a curated entry with a `source` maps to ResearchBacked
(source "vendor-only*" maps to MaxDpsBacked); a curated entry without source
maps to CompanionRule; never-automatic maps to ManualByDesign; the
class-spell tail maps to Incomplete + `MaxDpsOnly`; extras-derived class/spec
membership is added by the loader; OpportunityCost derives from tier +
cooldown; OffensiveUsage derives from purpose + cooldown (vendor-only rows
may stay Unknown - delegated to MaxDps); InterruptKind derives Dedicated,
with curated Silence for 15487 and 119910.

## Source hierarchy

When sources disagree, precedence is: live game mechanics (DB2 export,
in-game behaviour) > current MaxDps vendor tables > Blizzard docs (patch
notes, forums) > community data (wiki, guides, snippets) > conservative
companion rule > Manual / Unknown. Id-keyed merges only - name-keyed merges
are forbidden (live names drift: 231895 "Avenging Wrath", 228260 "Voidform",
383269 "Graveyard").

Machine-readable research staging lives in
`docs/research/ABILITY_INTELLIGENCE_RESEARCH.md` +
`docs/research/registry-research.json` (the 12.1 research records).

## MaxDps relationship (who owns the decision)

`MaxDpsRelationship`: NotSurfaced, MainRotation, OffensiveBucket,
DefensiveBucket, InterruptBucket, ConsumableBucket, TrinketBucket.
`AutomationContext`: Autonomous (companion may generate), MaxDpsOnly
(companion never generates; acts only when MaxDps suggests), Manual.

Rule: **MaxDps stays authoritative for the main rotation.** The companion
never synthesizes rotation content; a Main-slot OFF toggle only skips that
suggestion. Companion-backed offensives with a curated `EnemyCountMin` hold
when the count is unobservable; MaxDps-backed rows delegate that call to
MaxDps. Vendor-only offensives may keep `OffensiveUsage Unknown` - that is
delegation, not a gap.

## Manual-by-design

33 manual-by-design CC/purge/threat/dispel entries (purposes CrowdControl /
Purge / Threat / Dispel; e.g. 5246 Intimidating Shout, 118 Polymorph,
2094 Blind, 370 Purge, 8122 Psychic Scream, 51514 Hex). Never automatic: no
observable debuff/target state exists on Midnight. User OFF is absolute;
user ON is still eligibility (still holds). Structural: NeverAutomatic maps
to `Automation.Manual`, and OFF cannot be overridden by urgency, MaxDps,
Solo, or emergency.

## Five-state decision model

`PolicyVerdict`: Use / Hold / Skip / Unavailable / Unknown.

- Out-of-range (confirmed) = Unavailable.
- Context-unknown = Unknown: gap-closer melee unknown, ability range unknown,
  reflect incoming-cast unknown unless it fails open, defensive urgency
  unknown for majors.
- Not-interruptible / stale-cast / user-policy / manual = Skip.
- Dispel / manual utilities = Hold ("manual utility has no observable
  trigger; never automatic").

Scheduler consumption: Hold + Unknown count as PolicyHeld; Skip +
Unavailable count as PolicySkipped; only Use is scheduled. One action per
tick is unchanged. Five-state verdicts never collapse to a boolean.

## Self-sustain priority

Emergency self-heal (HP <= `SoloEmergencyHpPct`, default 35) is Use +
Emergency in BOTH Normal and Solo and outranks the main rotation (scheduler
rank SelfHeal 3 > Main 4). The wider sustain layer (below
`SoloSelfSustainHpPct` 65, overheal guard, ability ceiling `UseBelowHpPct`,
immunity / conservation guards) remains Solo-only; above emergency in Normal
mode it holds ("solo mode off; self-heal held above emergency HP"). Normal
mode otherwise stays as close to MaxDps as possible.

## Capability intelligence and secret-value ceilings

- Interrupt: 14 interrupts; 12.1 kinds are 13 Dedicated + Silence for Shadow
  Priest 15487 and Felhunter Spell Lock 119910 (live id 119910 Replaces
  vendor 19647). Enemy cast spell identity/danger cannot be read on Midnight
  (combat-log removal + secret `UnitCastingInfo`) - safe behaviour is
  "interrupt any observed interruptible cast", never blind spam; the bridge
  only suggests while MaxDps flags a live cast; arg-blind interruptible
  boolean veto.
- Offensive: "ready never means use now" - own-buff skip, paired
  ConflictGroup window hold, melee requirement, range gates, curated
  `EnemyCountMin` enforced only for companion-backed offensives (MaxDps-backed
  rows delegate). 51 cooldowns classified from research (e.g. 1719
  Recklessness MajorBurst holdForBurst, 31884 Avenging Wrath MajorBurst,
  205180 Summon, 228260 Voidform Transformation, 98008
  DefensiveOffensiveHybrid).
- Mobility: 22 kinds (GapCloser / Disengage / Teleport / SpeedBurst / Escape /
  MovementImmunity); only target-reaching gap closers are ever automatic and
  only with confirmed out-of-melee + in-range; escapes / teleports / speed
  bursts are manual-by-design or emergency-only.
- Utility: 33 entries, never automatic (above).
- Capability counts: Defensive 195, Interrupt 15, Offensive 213,
  Self-sustain 30, Mobility 42, Utility 33.

New model fields on `AbilityDefinition` (all derived where possible):
AbilityKind; IntelligenceStatus; AutomationContext; MaxDpsRelationship;
OpportunityCost (Unknown / Low / Medium / High / Critical); InterruptKind
(Dedicated / Silence / Stun / Displacement / Incapacitate); OffensiveUsage
(MajorBurst / MinorBurst / ShortCooldown / Execute / AoeOnly /
SingleTargetOnly / ProcDriven / ResourceDriven / WindowDriven /
DefensiveOffensiveHybrid / Summon / Transformation / Manual); MobilityKind
(GapCloser / Disengage / Teleport / SpeedBurst / MovementImmunity / Escape);
AbilityRequirement flags; TalentNote / HeroTalentNote; PatchVerified;
Relations (Requires / Enhances / Replaces / ConflictsWith / SynergizesWith /
Consumes / Protects / Follows / Precedes); CapabilityTags; EnemyCountMin;
HoldForBurst; derived CooldownClass / TargetKind / EmergencyCapability /
capability flags / AutomationAllowed / HasIntelligence / ManualByDesign.

## Audit and runtime enforcement

- `MaxDpsCompanion.exe --ability-audit=<path>` writes the machine-generated
  `docs/research/ABILITY_REGISTRY_AUDIT.md`; `--ability-info=<spellId>` is the
  app-side inspector (app-side equivalent of `/mdb ability`; writes
  `ability-info.txt` + stdout).
- `tools/ability_audit.ps1` runs the audit and checks the committed addon
  `Catalog.lua` matches the generated output (exit 0 clean, 1 run failure,
  2 addon Catalog.lua drift). `--gen-catalog` still emits
  `addon/MaxDpsBridge/Catalog.lua` (revision 3).
- Zero-violation invariant: Violations 0, Warnings 0 on this pin.
- Runtime enforcement: Incomplete / Unknown / UnsafeToAutomate entries can
  never be generated by companion-only slots (Mobility / SelfHeal, Defensive
  gap-fill) - verdict Skip ("ability intelligence incomplete; companion never
  generates it"); on MaxDps slots they fall back to the exact pre-registry
  Generic path. No silent generic default.
- User modes `[Abilities] Modes=id:Mode` persisted by AppSettings
  (SoloOnly / NormalOnly / Manual / Never / Always / Automatic; Always
  currently evaluates as Automatic - reserved). ON/OFF stays the UI surface.

## How to add or correct an ability

1. Add or edit the row in `Knowledge/abilities.json` (id-keyed; include
   `source` + date for ResearchBacked, or leave sourceless for
   CompanionRule). Never edit `vendor/` (read-only) and never duplicate a
   vendor-surfaced ability into extras.
2. Cite the source in `docs/research/ABILITY_INTELLIGENCE_RESEARCH.md` /
   `registry-research.json` when the change is research-driven.
3. Regenerate and audit: `--gen-catalog`, then `--ability-audit` /
   `tools/ability_audit.ps1` - Violations must stay 0 and the committed
   `Catalog.lua` must match.
4. If the change alters behaviour, add a scenario test
   (`RegistryDecisionScenarioTests` / policy tests) and a replay case if the
   verdict shape changed.

## Live validation (OWED - never claimed offline)

Reinstall the addon + `/reload` (Catalog.lua revision 3), then the v2.6
checklist in `docs/TESTING.md`: Class skills rows show status / mute /
tooltip; Normal-mode emergency self-heal fires below 35% and outranks main,
holds above; one interrupt per class (kinds / timing); one major offensive
per spec (holds when the paired window is active; fires when MaxDps surfaces
it); gap closer only when out of melee; utility never fires; ON/OFF incl.
Modes round-trip; `--ability-info` vs live spell check; a recorded +
exported + replayed session with 0 mismatches.

Honest limits: the class-spell tail (2971 entries) is Incomplete /
MaxDpsOnly (modeled, delegated); enemy cast identity/danger unavailable on
Midnight; enemy count not observable (`EnemyCountMin` enforced only for
companion-backed offensives); one action per tick; one defensive candidate
per tick; `Always` user mode reserved; cooldown/duration values are
sequencing hints with recorded source disagreements.
