# Architecture — MaxDps-Companion

## Pipeline

```
MaxDps engine (vendor/, read-only)  ── the AUTHORITATIVE rotation source
  └─ MaxDps.Spell / SpellsGlowing / Glow* category hooks
       │
       ▼
Vendor discovery (read-only): MaxDps:GlowDefensiveHPMidnight (Buttons.lua:1056)
  colours every defensive through a 3-point curve (0.3 red / 0.5 yellow /
  1.0 transparent) evaluated on player HP; 119582 Purifying Brew uses the
  reversed stagger curve. The bridge stages that rendered colour at the
  curve's own control points — see docs/research/ABILITY_RESEARCH.md §6.
       │
       ▼
MaxDpsBridge addon — 40-cell pixel strip (bridge 3.3.0; v5 + Ext2 layout
  unchanged from 3.0.0). Ext3 T0 contract declared (v3.5): a 43-cell strip
  (cells 40-42 = 14-bit mask + epoch + blocked nibble + cell-42 checksum/commit,
  presence cell 28 B bit2); the addon still ships 40 cells until T1+.
  v5 core (35 cells, version nibble stays 5): magic · 8 slots (Main/Off/Def/
        Cons/Trin/Int/Mobility/SelfHeal) · status · version+checksum · 8 × 24-bit
        spell id · vitals · cast · target · range tri-states · self-buff bits
        · additive defensive urgency (cell 31 G HP curve, cell 32 B stagger curve)
        · additive Defensive gap-fill source bit (cell 31 B bit0)
        · class/spec · extension checksum
  Ext2 (cells 35-39, additive): cell 35 HP curve (UnitHealthPercent's colour
        passed to SetVertexColor, never read/compared) · cells 36-38 SelfHeal2
        key + 24-bit id · cell 39 checksum (scope = cells 36-38 only); presence
        bits cell 33 B bit2/bit3, SelfHeal2 range in cell 28 B bits0-1
  Ext3 (cells 40-42, additive, T0 decode only): cell 40 mask bits 0-11 ·
        cell 41 R mask bits 12-13 / G epoch / B blocked nibble · cell 42
        checksum over cells 40-41 + commit; presence cell 28 B bit2; a cell-42
        failure drops only the block, never the frame/core/Ext2
  variant resolution (bridge 3.0.0): every slot key resolves across base /
        talent-override / alias ids (FindBaseSpellByID, FindSpellOverrideByID,
        GetOverrideSpell, generated MDB.SpellAliases), so a bar holding
        Victory Rush 34428 still matches an intent written as 202168
  sensors: arg-blind RegisterUnitEvent cast tracking (player + target),
           pcall+scrub HP/target-HP/stagger, IsSpellInRange tri-states (incl.
           the SelfHeal slot), aura probe,
           generated Catalog.lua extras (Mobility/SelfHeal/Defensive per spec)
  extras:  the companion-only slots are an INDEPENDENT candidate source:
           the bridge walks the curated per-spec list and encodes the first
           entry that is ready AND has a resolvable keybind — MaxDps itself
           never suggests these abilities (Impending Victory, Exhilaration,
           Crimson Vial, ...), so without this path Solo mode would have no
           candidate to act on. Bridge 3.0.0 encodes the first entry into slot
           8 and the NEXT DISTINCT entry into Ext2 cells 36-38 (SelfHeal2),
            each as the variant the player actually knows. The Defensive slot
            keeps MaxDps's
            flagged+ready+bound candidate first; only when MaxDps names none
            AND the observed HP urgency is Red (major list) or Orange
            (short-CD list, v3.0.0) does the generated Catalog.lua defensive
            list supply a gap-fill candidate (Major before Minor, immunities
            excluded) and set the source bit.
            The Offensive slot (v3.0.0) mirrors it: MaxDps's flagged+ready
            offensive first, else the curated per-spec `offensive` list
            (shared burst first, spec-specific second, 1-4 entries) supplies
            the first ready+bound entry. No wire source bit exists, so the
            companion derives the source by id membership in the same list.
           The whole path stays inside MaxDps's own `enableDefensives` /
           `enableCooldowns` switches, so muting them upstream also mutes
           the gap-fill.
           CC slot-6 reuse (v3.4.0 Option A): the bridge walks the generated
           per-spec `cc` list (auto-eligible curated CC rows) and writes the
           first ready+bound candidate into the reused Interrupt slot 6 ONLY
           when MaxDps names no flagged+ready+live-cast interrupt. The CC path
           skips `IsInterruptReady` (no live cast needed) and is gated by the
           addon CC toggle; the companion's `CrowdControlGate` remains the
           authority. No new slot and no new source bit — the id rides the
           existing slot-6 cells and the companion recognises it by curated CC
           membership at the existing `CrowdControlVetoes.Evaluate` call-site.
  gates (bridge 3.3.0): Toggles.lua is the single addon-side restriction
        point. `SlotAllowed(slot, ctx)` is consulted before every WriteSlot —
        a denied slot is written empty with the valid flag clear, i.e. the
        ordinary no-candidate state (no wire bit). It owns the per-slot rules
        plus OOC (blanks 1-8 out of combat) and Solo (blanks 3/8 while known
        ungrouped, emergency HP excepted); TTK OFF forces target band 15 and
        AutoTarget/AutoInteract silence states 3/4. The addon can only
        RESTRICT: `effective = companion AND addon`, an addon OFF wins; a
        missing DB key = ON and every gate fails open. Panel.lua is the
        plain-frame settings panel + draggable overlay (`/mdb toggles`,
        `/mdb overlay`; the overlay is a plain Frame — no RegisterForClicks,
        right-click reset via OnMouseUp) and never runs on the pixel-update path.
       │  (flat colours, top-left corner overlay; every probe degrades to
       │   UNKNOWN — never throws, never compares a secret value)
       ▼
MaxDpsCompanion.exe — DIB BitBlt sample @ PollIntervalMs
  decode v5 capture at 35 (core) / 40 (Ext2) / 43 (Ext3) cells
  → BridgeFrame (slots + keybinds + spell ids + SelfHeal2 + Ext3 mask/epoch/
   blocked + CombatContext: HP + HpSource + HpPctUpper, cast, target, range,
   defensive urgency, stagger urgency, defensive gap-fill source). HP precedence:
   plain cell 27 > Ext2 curve > unknown; `[Intelligence] HpCurve=0` ignores the curve.
       │
       ▼
Candidate tracker (Decision/CandidateTracker)
  per-slot observation history: stroke + spell id, first-seen/changed,
  pressed-since-change. Companion-only slots enter here exactly like MaxDps
  slots — one scheduler input, one send path, no second rotation engine.
  Ext2 SelfHeal2 (v3.0.0) is the alternate self-sustain candidate: the
  scheduler evaluates it with its OWN range probe (cell 28 B) when the primary
   SelfHeal verdict is not Use, and a Use from either wins the slot; rank,
   one-action-per-tick and timing are unchanged.
        │
        ▼
TTK estimator (Knowledge/TtkEstimator) — v3.2.0, fed pre-policy
  RotationEngine owns ONE pure/fake-clock estimator and feeds it once per real
  frame, before the policy. Inputs are already-decoded fields only: nowMs,
  hasTarget, targetHpValid, targetHpBand (the 0..14 band reconstructed exactly
  from the decoded target percent). frac = (band+0.5)/15; resets on no target,
  >10 s unknown, or a >0.12 upward jump; feeds real declines from the last FED
  anchor into a 3 s EWMA (first sample seeds it). The result is attached via
  CombatContext.WithTtk (no scheduler signature change) and feeds the T1–T4
  gates; an invalid estimate fails open (every gate skipped).
        │
        ▼
Execution safety (Knowledge/PolicyEvaluator.ExecutionSafety) — ALWAYS

  while the player is casting or channeling, every GCD-riding / unverified
  ability is held (CastHold / ChannelHold) — including the main rotation,
  because a channel is not ours to clip. Exempt: Interrupt / Consumable /
  Trinket (off-GCD by design) and curated non-movement off-GCD abilities
  (GcdVerified); gap closers/movement are held (movement cancels a cast).
  Runs on the policy path, the scheduler path (intelligence off) and the
  legacy loop (v5 frames; v4/v1 = UNKNOWN = legacy behaviour).
       │
       ▼
Situational policy (Knowledge/PolicyEvaluator) — [Intelligence] Enabled=1
  registry layer (Knowledge/AbilityIntelligence): every candidate is keyed to
  its AbilityDefinition intelligence view first — status (Verified /
  ResearchBacked / MaxDpsBacked / CompanionRule / ConservativeSafety /
  Heuristic / ManualByDesign / UnsafeToAutomate / Incomplete / Unknown),
  automation context (Autonomous / MaxDpsOnly / Manual), MaxDps relationship,
  opportunity cost, and per-capability intelligence (InterruptKind /
  OffensiveUsage / MobilityKind + requirements / relations / EnemyCountMin /
  HoldForBurst).
  enforcement step: Incomplete / Unknown / UnsafeToAutomate entries can never
  be generated by companion-only slots (Skip); on MaxDps slots they keep the
  exact pre-registry Generic path. Manual-by-design is structural OFF.
  user enable/disable (AbilityPolicy, incl. [Abilities] Modes) → ability lookup by spell id →
  USE / HOLD / SKIP / UNAVAILABLE / UNKNOWN + reason
  · main rotation: only two gates (out of range, active cast/channel)
  · defensives: MaxDps's own flagged/usable/enabled recommendation is the
    trigger; the rendered urgency (HP curve, stagger curve for 119582) is
    the gate — White holds everything, majors need Red unless the ability is
    the MaxDps-recommended Defensive slot (one-stage discount, never below
    Yellow), minors need Yellow, Unknown holds majors and keeps minors on
    the legacy gates, emergency HP overrides. Policy still prevents waste
    (own buff active, overlapping mitigation) and gates reflect abilities
    on an observed incoming cast. Escape/Movement and External are
    emergency-only; a gap-fill Defensive candidate is never fired above Red
  · interrupts: vetoed when the observed cast is explicitly not
    interruptible or has already ended (v5 sensors); 12.1 kinds (13 Dedicated
    + Silence for 15487 / 119910); bridge suggests only while MaxDps flags a
    live cast — never blind spam
  · offensives: no casts/channels, no curated pairing window, own-buff skip,
    melee/range gates, curated EnemyCountMin for companion-backed rows
    (MaxDps-backed rows delegate); "ready never means use now"
    · TTK gates (v3.2.0): T1 holds an offensive when valid TTK < minTtkSec
      (usage default or curated); T2 bypasses the pairing hold when valid TTK
      ≥ 2·cd+dur (absent cd skips); T3 bypasses it when executeFavored and target
      HP ≤ executeBelowPct. Invalid TTK skips all three (fail open)
    · mobility: gap closers need a confirmed out-of-melee target + in-range
      ability; escapes/movement are never automatic
    · self-heals: emergency self-heal (HP <= EmergencyHpPct, default 35) is a
      survival Use in BOTH Normal and Solo and outranks main; the wider Solo
      sustain layer stays Solo-only (sustain HP gate, overheal guard, ability
      ceiling useBelowHpPct, immunity / conservation guards, target/range
      preconditions, never-automatic veto); above emergency in Normal it holds
    · solo ladder (v3.3.0): Solo-only HP bands widen survival beyond heals —
      Minor absorb/shield at/below SoloMinorHpPct (75), heal at/below
      SelfSustainHpPct (65), Major at/below SoloMajorHpPct (50), immunity
      at/below SoloImmunityHpPct (30). In-band gap-fill bypasses the
      White-urgency hold (HP-substitution); out-of-band holds; immunity needs
      no active immunity; MaxDps-flagged + all group verdicts unchanged;
      T4 dying-target hold + overheal guard + escalate rule still apply
    · defensives T4 (v3.2.0): in Solo only and not emergency, a valid TTK < 6 s
      holds the defensive ("target dies in ~Xs; saving <mitigation>"); emergency
      HP always overrides, and Normal mode is unaffected
    · audit/inspector: `--ability-audit=<path>` + `tools/ability_audit.ps1`
      (Violations 0 / Warnings 0 / Missing 0 / Stale 0 enforced; exit 3 when
      non-clean, 2 on addon Catalog.lua drift); `--ability-info=<spellId>` app
      inspector; `--ability-coverage=<path>` machine-readable manifest;
      `--ability-search/class/spec` registry filters
        │
        ▼
Candidate providers (Knowledge/CandidateProviders.cs) — v2.7
  explicit per-category owners instead of inline branches: MaxDpsRotation
  (Main/Consumable/Trinket + fail-open tail), Offensive (source = MaxDps wire
  or, v3.0.0, curated catalog gap-fill detected by id membership, combat/Solo
  gated), Defensive (source = MaxDps recommendation or catalog gap-fill,
  Red majors / Orange short-CDs; v3.3.0 Solo ladder adds HP-banded
  Minor/Major/Immunity gap-fill with urgency substitution), Interrupt, Mobility
  (BridgeExtra), SelfSustain (BridgeExtra), Utility (structurally incapable
  of Use). Every decision carries Provider + CandidateSourceKind
  (MaxDpsWire/BridgeExtra/CompanionGapFill/None) + structured evidence;
  verdicts and reason strings are unchanged (pinned byte-for-byte).
        │
        ▼
Action scheduler (Scheduler/ActionScheduler) — deterministic state machine
  link/protocol gate → hard cast/channel gate (policy-off) → five-state
  policy verdicts (Use / Hold / Skip / Unavailable / Unknown; Hold+Unknown =
  held, Skip+Unavailable = skipped, only Use scheduled)
  → rank (interrupt > emergency > defensive > self-sustain > main > mobility
  > offensive > consumable > trinket) → duplicate collapse → stale and
  pending-confirm demotion (never for SelfHeal, r2) → GCD → min interval →
  unavailable/retry suppression → ordered plan + reason/confidence (one action
  per tick). A transient failed SelfHeal press is capped at 1.5 s with no
  escalation; when HP is in the sustain window and no heal is ready the plan
  holds with the distinct reason SelfHealCoolingDown (r2).
       │
       ▼
PostMessage WM_KEYDOWN/WM_KEYUP → WoW window only
  (player-bound keys; mouse/interact gated on foreground)
  failure recovery: a GCD-riding press that never starts a GCD inside
  ~600 ms is marked failed and the stroke is suppressed with an escalating
  window (1.5/3/6/10 s, reset by a success); failure suppression is NOT
  cleared by state transitions, so a dead action cannot be re-armed by
  every GCD pulse and can never block other ranks

Local telemetry (opt-in: [Telemetry] Enabled, default 0)
  every Report tick → JSONL event (tick/send/link) with the plan head,
  policy verdicts + reasons, combat context (range/buff/interruptible/
  options) and candidate snapshot, plus the additive `ttk`/`thp`/`ttkMs`
  fields (v3.2.0) → bounded in-memory ring (no disk until
  Export) ──► Export file ──► ReplayRunner: recorded DecisionContext →
  DecisionEngine.Evaluate AND every recorded policy verdict →
  PolicyEvaluator.Evaluate (memory rebuilt from send events in live order:
  a send precedes its own tick, so a tick never sees its own send; the TTK
  estimator is rebuilt by feeding the recorded (ttkMs, hasTarget, thp)
  series in order) → diagnostic report (legacy decisions and policy verdicts
  must both recompute with 0 mismatches)
```

No memory read, no injection, no OCR at any stage. The knowledge base is
static data; the policy only consumes decoded protocol fields and the
companion's own send history. Nothing invented, nothing secret, no LLM in the
runtime.

## Class skills screen

```
AbilityCatalog (vendor Cooldowns + curated + merged class-spells)
  + ClassSpellBook (generated class-spells.json)
  + curated per-spec extras (Mobility/SelfHeal/Defensive)
        │
        ▼
ClassSkillTree.Build(class, spec)
  shared ids (present in every wire spec) + per-spec ids
  grouped Main / Offensive / Defensive / Movement, provenanced-first sort
        │
        ▼
ClassSkillsView rows (icon + name + recommendation + ToggleSwitch)
  class/spec dropdowns (shared section then per-spec sections)
        │
        ▼
AbilityPolicy.With(spellId, ...) → settings.ini [Abilities] On=/Off=
  → PolicyEvaluator / ActionScheduler
```

The screen is read/build-only over the same immutable catalog the policy
uses; a toggle just writes the existing per-spell override. `ClassSpellBook`
decodes tokens, applies the live-client verification (removed ids are absent;
names/icons are the client's own) and flags junk/passives; `ClassSkillTree`
owns membership and ordering. Icons come from `SpellIconCache` (below), never
from game memory.
`MaxDpsCompanion.exe --dump-class-skills=<path>` dumps the merged tree for
filter curation, and `--ui-snapshot-class-skills=<png>`
[`--ui-snapshot-class=CLASS --ui-snapshot-spec=SPEC`] renders the screen for
design review without a game.

## Classic UI shell (v3.0.0; replaces the v2.8 rail/pages shell)

```
MainForm (borderless; 660-wide fixed frame; 2px ring red stopped / green
          running; tray; pause hotkey; remembered size only when
          [Window] Layout=classic3)
  ├─ title bar 48px
  └─ GradientCanvas
       ├─ classic body (TableLayoutPanel, fixed absolute rows; NO AutoScroll on
       │   any timer path — the v2.8.1 NormalizeScroll defect class is pinned
       │   by ClassicUiTests.ClassicUi_ScrollSurvivesRefresh)
       │     hero RoundedCard: status 42 (LinkLamp + state + ClassBadge)
       │       · live 42 ("Now: <action> — <why>", ellipsis + tooltip)
       │       · strip 40 (StripView, live sampled cells)
       │       · "Spells" header + 4 two-toggle rows (Main|Offensive,
       │         Defensive|Interrupt, Self-heal|Mobility, Consumable|Trinket)
       │       · "Modes" header + 2 two-toggle rows (Solo|Out of combat,
       │         Auto-target|Auto-interact)
       │     button row 1 (52): Start | Stop | Launch Game
       │     button row 2 (46): Recalibrate | Abilities… | Advanced… | Open Folder
       │     status line (26)
       ├─ Advanced scrim + centred card (tabs Configuration | Diagnostics |
       │   Intelligence), built lazily once, one AutoScroll panel of fixed
       │   RuleSections per tab
       └─ Abilities scrim + centred card (tabs Class skills | Explorer)
  Default client 660 × min(content, working area); MinimumSize 520×560; Esc
  closes the topmost popup. Width tiers (UiScale, D5): client width picks
  Compact (≤560) / Classic (≤700, the 660 default) / Roomy (≤950) / Wide
  (>950); a tier scales font step, row heights, card padding, button heights
  and toggle size and is applied to the main window plus both popups on a
  120 ms debounced resize (never mid-drag). The client height is the MEASURED
  content height (`MainForm.LayoutHero` measures the hero + body rows and
  `ApplyContentHeight` clamps it to the working area), so the window grows with
  its content instead of scrolling. Restored classic primitives live in UiControls.cs
  (LinkLamp, StripView, ClassBadge, RoundedCard, RuleSection, GradientCanvas,
  WheelSafeNumeric forwarding the wheel to its scrollable parent); the tabbed
  pages still use Ui/Layout.cs (VertStack/WrapFlow/GridPanel/BentoSplit/
  UiMeasure) and Ui/UiPrimitives.cs (GlassCard/KvRow/pills/tiles/chips/
  UiClickable, the v2.8.1 mouse-gesture base). Type: system variable-font chain
  (DesignTokens.FamilyName) — bundled Geist/UiFonts removed in v3.
  --ui-smoke-test shows the form offscreen, opens every popup tab and runs the
  structural invariants (zero size, text fit, card content, Tab reachability,
  accessible names, sibling overlap); exits 1 on findings.
  --bench-ui: 2000× RefreshStatus → mean/p95 µs + startup-to-shown ms
  (targets mean < 300 µs, p95 < 1 ms, startup < 500 ms).
  --ui-snapshot-page=main|advanced-config|advanced-diag|advanced-intel|
  abilities-class|abilities-explorer [--ui-snapshot= --ui-snapshot-width=
  --ui-snapshot-height=]; the review set is 6 pages × {660×920, 520×560} in
  dist/ui-snapshots.
```

## File map

```
MaxDps-Companion/
  addon/MaxDpsBridge/        bridge addon 3.3.0 (v5 + Ext2 encoder, in-game
                             13-toggle UI, /mdb commands)
    Catalog.lua              GENERATED class/spec ids + extras (--gen-catalog,
                             incl. per-spec offensive (curated), defensive
                             (Red) and defensiveMinor (Orange) gap-fill lists,
                             plus the v3.4.0 `cc` auto-eligible crowd-control
                             list, and the aliases block emitted as
                             MDB.SpellAliases)
    Keymap.lua               binding string -> virtual key
    Reader.lua               MaxDps readout, secret guards, v5 sensors,
                             defensive urgency + gap-fill, spell variants
                             (base/override/alias resolution), SelfHeal2,
                             ExtraCandidates (mobility/selfHeal/defensive/cc),
                             MDB.GetCrowdControlCandidate (slot-6 CC source),
                             Ext2 HP-curve source
    Bridge.lua               strip rendering + 40-cell v5 + Ext2 encode
                             (urgency + HP curve + SelfHeal2 + Ext2 checksum);
                             slot 6 = interrupt-first, else the v3.4.0 CC
                             candidate (wire frozen, no new slot);
                             per-tick toggle context + /mdb toggles|overlay|
                             <key>|all|why, deep Defaults merge
    Toggles.lua              in-game 13-toggle policy (pure logic, no frames):
                             Get/Set/Flip/Keys/Label/Snapshot over
                             MaxDpsBridgeDB.Toggles + SlotAllowed(slot, ctx),
                             the single gate before WriteSlot (missing = ON,
                             fails open; addon can only restrict)
    Panel.lua                in-game settings panel + draggable overlay
                             (overlay = plain Frame, no RegisterForClicks,
                             right-click reset via OnMouseUp; panel uses
                             Settings.RegisterCanvasLayoutCategory
                             with InterfaceOptions_AddCategory fallback);
                             `/mdb toggles` opens it, `/mdb overlay on|off`
                             shows/hides the overlay, optional minimap button
  app/MaxDpsCompanion/       WinForms companion (sampler → PostMessage)
    Knowledge/               ability knowledge base (v2.0; defensive v2.3; registry v2.6)
      AbilityModel.cs        enums + AbilityDefinition + slot mapping,
                             DefensiveUrgency + MinimumUrgency + UrgencySource
                             + registry fields (AbilityKind,
                             IntelligenceStatus, AutomationContext,
                             MaxDpsRelationship, OpportunityCost,
                             InterruptKind / OffensiveUsage / MobilityKind,
                             requirements, talent notes, relations, patch
                             metadata, derived flags)
      AbilityCatalog.cs      loader: vendor base + curated overrides +
                             extras, wire ids, stable lookups, defensive
                             gap-fill derivation (DefensiveGapFill),
                             CrowdControlGapFill (v3.4.0 slot-6 `cc` read
                             path), registry derivation rules, patch guard
                             (12.1 / 120100 / 11.3.49, CatalogVersion 4)
      CrowdControlCatalog.cs curated verified CC registry (v3.4.0): DR
                             category, Single/AoE, CD, AutoEligible; read by
                             AbilityCatalog.CrowdControlFor / CrowdControlGapFill
      CrowdControlVetoes.cs  companion CC opt-in gate (CrowdControlGate,
                             default OFF), companion-only same-DR anti-chain
                             memory (fail open), and CrowdControlVetoes.Evaluate
                             — the single evaluation call-site the slot-6 CC
                             candidate flows through
      AbilityIntelligence.cs derived per-capability intelligence +
                             registry enforcement gate, the machine audit and
                             the inspector text
      AbilityCoverage.cs     v2.7 coverage manifest (discovered / registered /
                             automatable / delegated / manual / stale / missing
                             + live-verified classes) and stale/newer detection
      CandidateProviders.cs  v2.7 explicit candidate providers + candidate
                             source identity + structured decision evidence;
                             v3.2.0 T1-T4 TTK gates; v3.3.0 Burst/AoE preset
                             holds + Solo HP-banded escalation (SoloBandLatch)
      TtkEstimator.cs        v3.2.0 pure/fake-clock per-target TTK estimator
                             (band -> frac, reset/feed-from-anchor/EWMA seed,
                             clamp 300 s; invalid fails open)
      TtkPolicy.cs           v3.2.0 MinTtkSec usage defaults + TTK field
                             forwarding for the T1-T4 gates
      SoloBandLatch.cs       v3.3.0 Solo HP-band hysteresis latch (enter band,
                             then stay eligible to enter+5 once engaged; with no
                             prior engagement it is the plain enter threshold)
      AbilityPolicy.cs       user per-spell ON/OFF overrides ([Abilities],
                             incl. Modes SoloOnly/NormalOnly/Manual/Never/
                             Always/Automatic)
      PolicyEvaluator.cs     USE/HOLD/SKIP rules + PolicyMemory (sequencing)
      CatalogLuaGenerator.cs emits addon/MaxDpsBridge/Catalog.lua
      ClassSpellBook.cs      loads class-spells.json, applies the live-client
                             verification (official name/icon, removed ids
                             dropped), decodes tokens, flags junk/passive rows
      ClassSkillTree.cs      membership model: shared vs per-spec, section
                             grouping for the Class skills screen
      vendor-abilities.json  GENERATED from vendor/ (extraction script)
      abilities.json         CURATED policy layer
      class-spells.json      GENERATED lowest-priority layer from the retail
                             classSpellData block (tokens + ids only)
      spell-verification.json GENERATED live-client name/icon/verified per
                             class-spell id (wago.tools DB2 export); unverified
                             ids are not merged (see ClassSpellBook)
    ClassSkillsView.cs       full-size Class skills screen: class/spec
                             dropdowns, shared + per-spec sections, toggles
    Ui/                      UI shell (classic v3): DesignTokens (system
                             variable-font chain — Geist/UiFonts removed),
                             Layout (measured VertStack/WrapFlow/GridPanel/
                             BentoSplit + UiMeasure), UiPrimitives (GlassCard,
                             KvRow, pills/tiles/chips, UiClickable),
                             AbilityExplorer / AbilityInspector,
                             Pages (StackPage hosts for the popup tabs),
                             SettingsPages (Configuration/Diagnostics +
                             v3.3.0 SoloBandEditor Minor/Major/Immunity rows;
                             v3.3.0 Mode/Target preset rows),
                             UiShellValidation (honest structural smoke)
    UiControls.cs            restored classic primitives: RoundedCard (hero),
                             RuleSection, GradientCanvas, LinkLamp, StripView,
                             ClassBadge, WheelSafeNumeric
    MainForm.cs              borderless classic 660-wide fixed main window
                             (no AutoScroll on the timer path): 2px state ring
                             (red stopped / green running), hero + Spells/Modes
                             toggles + button rows, Advanced…/Abilities… scrim
                             popups; remembered size only when
                             [Window] Layout=classic3
    Intelligence/CombatContext.cs  tri-state context from the frame; HpSource
                             {Plain, Curve, Unknown} + HpPct/HpPctUpper with
                             precedence plain > curve > unknown
    Intelligence/SpellIconCache.cs  opt-in skill icons from the WoW CDN,
                             cached in assets/icons, offline placeholder
    Scheduler/               deterministic action scheduler
      SchedulerModel.cs      input/plan/reason/verdict models
      ActionScheduler.cs     pure state machine: link, policy, rank, pacing,
                             failure recovery (rejection + retry backoff)
      SchedulerBench.cs      --bench-scheduler deterministic measurement;
                             v3.3.0 p95 frame-cost regression baseline
      BridgeHealth.cs        v3.3.0 stale-addon / protocol-skew advisor
                             (OWED: not yet consumed by RotationEngine/MainForm)
    Decision/                legacy decision layer (v1.4.0, opt-in)
    Telemetry/               local rotation telemetry + replay (opt-in)
      CastAudit.cs           v3.3.0 suggested-vs-cast audit over recorded
                             events (pure; read-only report)
    Ui/CastAuditView.cs      v3.3.0 read-only audit grid (OWED: not mounted)
    Ui/SemanticBanner.cs     v3.3.0 status-tone banner control (OWED: not mounted)
    ThisAssembly.Gen.cs      build stamp (git HEAD + date, title bar)
  tests/MaxDpsCompanion.Tests/ xunit suite (613 tests)
    ClassicUiTests.cs        classic shell: scroll survives refreshes, no Layout
                             events on value-only refreshes, default/min sizes,
                             7 button labels, real mouse-message clicks, launcher
                             seam, toggle persistence, Esc closes popups
    PixelProtocolExt2Tests.cs 40-cell Ext2 decode: valid curve/band, nR+nG out
                             of range, bit2=0 with junk in 35-39, bit3=0,
                             cell-39 checksum failure drops only SelfHeal2,
                             v5/v4/v1 byte-identical, 35-cell addon in a 40-
                             cell capture
    CombatContextHpTests.cs  HpSource precedence plain > curve > unknown,
                             HpPctUpper band top, HpCurve=0 ignores curve,
                             curve-derived defensive urgency
    SelfSustainAlternateTests.cs  SelfHeal2 alternate evaluated with its own
                             range; a Use from either candidate wins the slot
    SoloHiddenHpTests.cs     hidden plain HP + valid curve drives Solo gates
                             (all-spec property + scheduler integration)
    KnowledgeExtrasTests.cs  all-class self-heal extras, aliases (202168↔34428,
                             19647↔119910), catalog sync
    fixtures/solo-hidden-hp-warrior.jsonl  canonical hidden-HP curve recording
                             (6 verdicts, 0 mismatches)
    AbilityCoverageTests.cs  ownership/completeness/delegation/manual reasons,
                             patch metadata, stale/newer violations, manifest
                             consistency and JSON classes
    CandidateProviderTests.cs  the v2.7 decision matrix per category
                             (self-sustain / defensive / interrupt / offensive
                             / mobility / utility) incl. provider + source
    ProviderPropertyTests.cs   catalog-wide properties: OFF never Use, utility
                             never Use, unknown-context fallbacks, no evidence
                             leaks, manual/ON scopes
    UiShellTests.cs          popup navigation, explorer filter/search, coverage
                             report, hardened smoke-no-findings, long-value
                             render, wrapping-label re-measure
    AbilityRegistryTests.cs  registry counts, derivation rules, patch guard,
                             zero-violation audit (25 tests)
    RegistryDecisionScenarioTests.cs  registry-driven policy scenarios (18 tests)
    OffensiveInterruptReplayTests.cs  offensive-interrupt fixture replay (3 tests)
    fixtures/offensive-interrupt-warrior.jsonl  canonical offensive-interrupt
                             recording (7 verdicts, 0 mismatches)
    DefensiveIntelligenceTests.cs  additive v5 decode + checksum coverage, urgency /
                             user-policy matrix, scheduler integration,
                             failed-press recovery
    DefensiveReplayTests.cs  canonical defensive recording + legacy-skip +
                             checked-in fixture replay
    ClassSpellBookTests.cs   token name decoding, junk filter, merge
                             provenance/priority, live-client verification
                             (official name/icon, removed id not merged),
                             gap-fill exclusion, tree shared/per-spec, main
                             opt-out, all-class coverage
    ClassSkillsViewTests.cs  STA window builds the Class skills screen with
                             rows for every class
    SpellIconCacheTests.cs   download-once + disk cache, offline degradation,
                             hostile slug rejection, official-slug single
                             request
    fixtures/defensive-warrior-urgency.jsonl  canonical defensive session
    TtkEstimatorTests.cs     v3.2.0 estimator: convergence, reset on switch/
                             heal/no-target/sustained-unknown, coarse-band
                             staircase, noise, clamp, determinism
    TtkPolicyTests.cs        v3.2.0 T1-T4 gate matrix (waste hold, two-uses,
                             execute, Solo T4 + emergency override, unknown
                             fail-open, kill-switch)
    TtkReplayTests.cs        v3.2.0 recorded-series estimator rebuild (in-memory
                             + the checked-in ttk fixture, 0 mismatches)
    fixtures/ttk-warrior-burst.jsonl  canonical v3.2.0 TTK recording (18 policy
                             verdicts, 0 mismatches; trash hold / boss fire)
    SoloEscalationTests.cs   v3.3.0 Solo HP-band escalation: ValidateSoloBands
                             ordering, Minor/Major/Immunity ladder verdicts,
                             group behaviour unchanged
  tests/secret_harness.lua   offline bridge secret-safety + v5/Ext2 encode
                             harness (186 checks, incl. the 33 in-game toggle
                             T21 cases: nil ctx, secret HP, OFF effects, TTK)
  tools/Extract-VendorAbilities.ps1  vendor Cooldowns.lua -> JSON
  tools/ability_audit.ps1  registry audit + addon Catalog.lua drift check
                           (exit 0 clean, 1 run failure, 2 drift)
  docs/PROTOCOL.md           normative v5 + Ext2 (40-cell additive) spec
  docs/KNOWLEDGE.md          ability knowledge format + curation rules
  docs/research/ABILITY_REGISTRY.md  v2.6.0 registry reference
  docs/research/ABILITY_INTELLIGENCE_RESEARCH.md + registry-research.json
                           v2.6.0 machine-readable 12.1 research staging
  tools/Extract-ClassSpells.ps1  retail classSpellData block -> class-spells.json
  tools/Verify-ClassSpells.ps1  live wago.tools DB2 export (SpellName /
                             SpellMisc / ManifestInterfaceData) ->
                             spell-verification.json + pre-cached icon dirs
  docs/PROTOCOL.md           normative v5 + Ext2 (40-cell additive) spec
  docs/KNOWLEDGE.md          ability knowledge format + curation rules
  docs/TESTING.md            how to run every test layer
  docs/TELEMETRY.md          JSONL format + record/export/replay guide
  docs/research/ABILITY_RESEARCH.md  class/spec research artifact + sources
  vendor/                    pinned upstream MaxDps* snapshot (read-only)
  build.ps1 / install-addon.ps1 / settings.ini / dist\ / VERSION.txt
```

## Key invariants

- **The registry drives behavior; unregistered/incomplete abilities never
  jump the queue.** Incomplete / Unknown / UnsafeToAutomate entries can never
  be generated by companion-only slots; on MaxDps slots they keep the exact
  pre-registry Generic path. Manual-by-design is structural OFF.
- **One action per tick, unchanged.**
- **Five-state verdicts never collapse to a boolean.** Hold + Unknown =
  PolicyHeld, Skip + Unavailable = PolicySkipped, only Use is scheduled;
  telemetry records all five names.
- **MaxDps owns the rotation.** The companion never generates a rotation
  action; the main slot is only gated on out-of-range and cast-in-progress.
  "MaxDps shows an ability" never means "press it now" for situational
  slots — the policy decides that.
- **Protocol v5 is 35 cells, with the defensive-urgency and Ext2 blocks
  additive.** Any layout change bumps cell 10 R and `docs/PROTOCOL.md` first.
  The companion decodes the 40-cell Ext2 frame, v5 and v6 (35 cells,
  forward-compatible), v4 (9 cells) and v1 (8 cells); the addon encodes v5
  plus Ext2. The urgency nibbles reuse reserved space, so a pre-2.3 encoder
  decodes with all of them UNKNOWN, and an updated addon still drives an older
  exe because the version nibble stays 5. Skew warns, never hard-fails
  (Sep-2026 outage lesson).
- **Ext2 is presence-gated and checksum-scoped (v3.0.0).** The frame grows to
  40 cells, but cells 0-34 and both v5 checksums (cells 10, 34) are unchanged.
  Cell 33 B bit2 = EXT2 PRESENT, bit3 = HP CURVE ACTIVE. The decoder reads
  cells 35-39 only from a 40-cell capture with bit2 set, so a stale 35-cell
  addon decodes byte-identically. The cell-39 checksum covers **only** cells
  36-38: a failure (or commit mismatch) drops only SelfHeal2, never the frame
  or the curve. SelfHeal2 additionally requires cell 36 flags bit3 and its
  range from cell 28 B bits0-1.
- **HP precedence is plain > curve > unknown; the curve is a rendering, never
  a measurement.** The bridge passes `UnitHealthPercent`'s colour straight to
  `SetVertexColor` and Lua never reads or compares it. The companion accepts
  cell 35 as an HP band only when bit3 is set and the R+G nibble sum is in the
  accepted `14..16` window; `[Intelligence] HpCurve=0` ignores it entirely. One
  band is ~6.67% HP, so a curve reading resolves to ±3.3%; `HpPctUpper` is the
  band's top so the overheal guard never assumes the low end.
- **SelfHeal2 is an alternate, not a second rotation.** The bridge encodes the
  next distinct ready+bound curated self-heal; the scheduler evaluates it with
  its OWN range probe only when the primary SelfHeal verdict is not Use, and a
  Use from either wins the slot. One action per tick and the rank order are
  unchanged.
- **Variant encoding (bridge 3.0.0).** Keybinds are matched against any base /
  talent-override / alias id and the encoded slot uses the variant the player
  actually knows, so a bar holding Victory Rush 34428 still satisfies an intent
  written as 202168. Aliases live in the generated `Catalog.lua`
  (`MDB.SpellAliases`), never hand-maintained in the encoder.
- **Defensive urgency is staged from the vendor curve, not invented.**
  `GlowDefensiveHPMidnight` (`vendor/MaxDps/Buttons.lua:1056-1110`) has
  three control points; the bridge maps the rendered colour to
  White/Yellow/Orange/Red at those same HP (or stagger, for 119582)
  boundaries. White is a hard invariant: at full HP MaxDps renders no glow,
  so every automatic defensive holds. Emergency HP is a separate survival
  override (`Solo.EmergencyHpPct`, default 35); it cannot co-occur with
  White, because White requires full HP and emergency requires low HP.
  A gap-fill defensive candidate is only offered at Red and only inside
  MaxDps's own `enableDefensives` switch, is never fired above Red, and only
  when MaxDps names no bound defensive. Unknown urgency is never treated as
  White or Red: majors hold, minors keep the legacy gates. A live MaxDps
  recommendation of the exact ability discounts the requirement by one
  stage, never below Yellow, and never against a curated `MinimumUrgency`.
- **User ability policy is absolute for OFF, eligibility-only for ON.**
  `[Abilities] Off=` never fires automatically — urgency, MaxDps
  recommendation, Solo mode and emergency HP cannot override it; `On=` only
  makes the ability eligible for the normal gates (White still holds, ON is
  never spam). Absent an override the curated `NeverAutomatic` default
  applies. Only one action is scheduled per tick and the scheduler state
  machine is unchanged.
- **Intelligence coverage is a two-axis, generated property (v2.7).** Every
  entry carries Ownership + Completeness; every MaxDps-owned entry a
  delegation reason and every manual entry a manual reason; stale and
  newer-than-supported patch metadata are audit violations; the coverage
  manifest compares DISCOVERED sources against REGISTERED (missing = 0) and
  is written to `docs/research/ABILITY_COVERAGE.json` by the audit tool. A
  registry row can never imply implemented intelligence.
- **Candidate providers are identity, not behavior (v2.7).** The per-category
  providers own the existing policy branches and add Provider +
  CandidateSourceKind + structured evidence; verdicts, reason strings,
  scheduler rank and timing are unchanged (bench hash pinned). The Utility
  provider is structurally incapable of returning USE.
- **The self-buff block is tri-state (v2.7).** Bridge 2.7.0 sets cell 33 B
  bit1 only when every aura probe ran clean; otherwise the block reads
  UNKNOWN and the policy keeps its documented fail-open. A failed probe can
  never masquerade as "buff absent".
- **The UI never owns engine state (v2.7).** The shell reads engine status
  and writes only through existing settings/policy storage; tab order,
  keyboard activation and accessible names are part of the smoke test, which
  fails on zero-size/overlap/label-fit findings.
- **Secret safety.** No comparison, arithmetic, stringify, sort or
  table-key use of any value a Blizzard API may mark secret. Cast state is
  observed through arg-blind `RegisterUnitEvent` events; every probe is
  `pcall` + `scrubsecretvalues`; UNKNOWN is a first-class value and every
  rule has a documented UNKNOWN fallback. Pinned by `secret_harness.lua`.
- **Knowledge is data, not if-statements.** Ids/categories are extracted
  from the vendor pin; policy fields are curated; both are validated at load
  and by tests. The bridge mirrors the catalog through a generated file
  whose drift fails `CatalogLuaSyncTests`. The class-spells layer is merged
  as the lowest priority and is excluded from the bridge's defensive
  gap-fill, so a modeled source can never enlarge what the addon offers.
- **Network use is opt-in and confined to skill icons.** The Class skills
  screen is the app's only network path (`Intelligence/SpellIconCache.cs`):
  fixed hosts, the slug validated against `^[a-z0-9_]{1,96}$`, a 6 s
  timeout, and silent failure to the drawn "?" placeholder. The preferred
  path is the official slug from the class-spells verification (one request
  to the Blizzard render CDN, then the zamimg mirror); only when that is
  absent does it fall back to Wowhead tooltip discovery for the slug. Icons
  cache to `assets/icons/{spellId}.jpg` next to the exe; the repo ships a
  pre-cached set in `dist/assets/icons` (**3510** official icons this pin),
  and `build.ps1` preserves `settings.ini` and `assets/` across publishes so
  the cache survives rebuilds. Opening the screen is the opt-in. Telemetry
  and every other path stay network-free.
- **The window state is visible at a glance.** The borderless main window
  paints a 2px frame ring (form padding 2 + `OnPaint`): red (202,64,68)
  while stopped, green (58,196,125) while running; the invisible resize
  grip is unaffected. The client size is remembered in `[Window] Width=`
  / `Height=`, written on resize-end and close and restored on start
  (clamped to the working area; `CollapsedWantHeight` is the 960 default).
- **Determinism.** Policy and scheduler are pure functions of the decoded
  frame, the send history and settings — fake-clock testable, no clock/OS
  reads inside. Telemetry replay recomputes the decision layer with 0
  mismatches; policy verdicts are recorded with reasons.
- **TTK knowledge is advisory and fails open (v3.2.0).** The estimator is pure
  and fake-clock (built only from decoded fields), **no wire field and no Lua
  change** — it reuses the target HP band already on the wire. An invalid/
  unknown estimate skips every T1–T4 gate, because holding a cooldown on an
  unknown target is the DPS loss the feature exists to avoid. T1 is a hold,
  never a lockout: MaxDps re-suggests next tick. The `[TimeToKill]` kill-switch
  disables the estimator and all gates without touching any other path.
- **Execution safety before intelligence.** Cast/channel protection is not
  knowledge filtering: it runs in every mode (policy on/off, scheduler
  on/off, v5 frames) and is the *only* thing allowed to hold the main
  rotation besides out-of-range. Off-GCD exemption requires `GcdVerified`
  (curated, or an intrinsically off-GCD category) and never applies to
  gap-closer/movement purposes.
- **Failure recovery.** A failed action (no GCD after a press, or the same
  stroke sent too often in the window) is suppressed with an escalating
  window (1.5 s → 3 s → 6 s → 10 s, reset by a successful send), never
  retried forever, and never blocks the rest of the plan: failure
  suppression lives in `_failedUntil`, is never cleared by state
  transitions, and a pending non-main stroke is demoted behind fresh
  actions while its confirmation is pending. The engine's per-slot OS
  gates (movement bind, physically held key, focus, background policy,
  window liveness) stay authoritative — the scheduler produces an order,
  never a bypass.
- **Self-healing attachment (v1.3.10):** window handle re-validated against
  the process's current main window; engine forces a re-attach after ~2 s
  without a frame and on every Start; sampler rebuilds its DC on a failed
  BitBlt.
- **In-game toggles can only restrict (v3.3.0).** The addon's 13 toggles
  (`Toggles.lua`) gate the bridge before it encodes: an OFF slot is written
  empty with the valid flag clear, and the wire has no "user muted" bit, so a
  user-blanked slot and a genuinely-empty slot are the same decoded state and
  the companion's ordinary no-candidate policy runs. `effective = companion
  AND addon`; an addon OFF wins. A missing SavedVariables key = ON, and every
  gate fails open on unknown/secret context. OOC OFF blanks slots 1-8; Solo
  OFF blanks only Defensive/SelfHeal while known ungrouped (emergency HP
  excepted); AutoTarget/AutoInteract silence states 3/4 without touching the
  status flags; TTK OFF forces target band 15. No layout/version change.
- **Toggle SSOT: the app wins (v3.5 T5).** The companion packs its 14 toggles
  into the additive Ext3 14-bit mask and pushes `/mdb mask <hhhh> <e>` at engine
  Start and on every toggle change — out of combat only — with a retry ladder
  (1.5 s window, 2 retries, then a red badge). The bridge echoes the accepted
  mask in Ext3 cells 40-42; `ToggleSync.EffectiveMask` uses that echo while it
  is valid and falls back to the app mask otherwise (fail-open). `ToggleSync.cs`
  is a pure state machine; `ChatCommander.SendToggleMask` does the silent send;
  `RotationEngine.TryGetToggleMirror` publishes the read-only echo + combat flag.
  The `[Meta] ConfigVersion` migration turns the new Mobility / CrowdControl
  defaults ON once for a pre-3.5 `[Spells]` config (fixes RC1/RC2).
- **Cross-stream wiring owed (v3.3.0 Stream 4).** The three streams ship
  complete units but four connections are intentionally deferred (their target
  files are outside this merge pass): `Scheduler/BridgeHealth` consumed by
  `RotationEngine`/`MainForm`; `Ui/CastAuditView` + `Ui/SettingsPages`
  SoloBandEditor + `Ui/SemanticBanner` hosted in `MainForm`;
  `SpellIconCache.Invalidate()` called from catalog regeneration;
  `AppSettings.PollIntervalMs` code default set to 33 (`settings.ini` already
  ships it). Documented in `HANDOVER.md` "Stream 4"; do not treat any of these
  as shipped behaviour until wired.
- **Legacy contract.** `[Intelligence] Enabled=0` = pre-intelligence
  behaviour (scheduler still paces, no knowledge filtering, companion-only
  slots off), except the hard execution-safety cast/channel gate which is
  not knowledge and runs on v5/v6 frames in every mode. `[Scheduler] Enabled=0`
  = the legacy send loop, policy still applied when intelligence is on,
  and the companion slots stay reachable there (they are inserted into
  `DecisionEngine.EnabledOrder`: SelfHeal before Main, Mobility after
  Defensive) — Solo mode does not silently vanish with the scheduler.
  A v4/v1 frame carries no cast state, so a stale in-game addon keeps the
  byte-identical legacy behaviour. Both paths pinned by tests.
- **Self-sustain is an independent candidate source, not a policy on
  MaxDps suggestions.** The bridge's curated extras walk produces the
  SelfHeal slot even when MaxDps never names the ability; from the tracker
  onward it is one more candidate in the same scheduler/send pipeline. Ext2
  SelfHeal2 (bridge 3.0.0) is the next distinct such candidate, so a spec with
  two ready heals gets a real alternate instead of a shadowed one. Nothing in
  the main rotation is synthesized or replaced.
- **Gap-fill enumerates existing knowledge; it never invents a candidate
  (r1).** The offensive gap-fill list is curated in `abilities.json` and emitted
  to `Catalog.lua`, and only true burst CDs enter it. The Defensive slot carries
  a wire source bit (cell 31 B bit0); the Offensive slot does NOT (decode is
  frozen) so its source is derived by id membership of the same generated list.
  Defensive majors gap-fill only at Red; short-CD (Minor/None) gap-fill opens at
  Orange; both stay inside MaxDps's own `enableDefensives` / `enableCooldowns`
  switches, so muting them upstream mutes the gap-fill too.
- **CC reuses the Interrupt slot; the wire stays frozen (v3.4.0, Option A).**
  The bridge writes a curated ready+bound crowd-control candidate into wire
  slot 6 only after MaxDps names no usable interrupt (`GetInterruptSpellID` +
  `IsInterruptReady` run first), so a live interrupt always wins and CC is
  never emitted while an interrupt is pending. The CC path deliberately skips
  `IsInterruptReady` (a CC needs no live cast) and is gated by the addon CC
  toggle (`Toggles.IsCC`, restrict-only, missing = ON). No new slot, no new
  source bit, no `PixelProtocol.cs` / `docs/PROTOCOL.md` change: the id rides
  the existing slot-6 cells, and the companion routes it through the existing
  `CrowdControlVetoes.Evaluate` call-site. `CrowdControlGate` (default OFF),
  the Never/Manual user vetoes, curated auto-eligibility, target/range/opener
  checks and the same-DR anti-chain memory all still govern; MaxDps-owned stuns
  (AutoEligible=false) are never emitted, so MaxDps authority is preserved.
- **Self-sustain is reset-aware (r2).** Self-heal readiness is re-read every
  tick and never cached; a ready SelfHeal is never stale- or pending-demoted;
  a transient failed press is capped at 1.5 s with no escalating backoff. A
  cooldown wait is surfaced as `SelfHealCoolingDown` (telemetry `cdWait` /
  `lastTriedMs`, verdict `resetHint`) and never changes a verdict or an order.
- `settings.ini` keys mirror `AppSettings` sections 1:1; adding a key means
  updating both plus the README table.
