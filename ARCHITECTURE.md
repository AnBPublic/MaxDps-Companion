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
MaxDpsBridge addon — 43-cell pixel strip (bridge 3.7.7; v5 + Ext2 layout
  unchanged from 3.0.0, Ext3 shipped by T1/T2). Ext3: a 43-cell strip
  (cells 40-42 = 14-bit app toggle mask + epoch + blocked nibble + cell-42
  checksum/commit, presence cell 28 B bit2); a pre-3.5 companion ignores it
  and an old 40-cell addon still drives the 3.5 companion (bit2=0).
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
  Ext3 (cells 40-42, additive, shipped v3.5 = T0 decode + T1/T2 render):
        cell 40 mask bits 0-11 ·
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
           melee probe: the addon's single CheckInteractDistance call site
           (Reader.MDB.ProbeTargetMelee, #nocombat-restricted in 12.x) is shared
           by GetTargetContext and Bridge.TargetState, gated by an event frame
           (`InCombatEv` regen/encounter + InCombatLockdown + scrubbed
           UnitAffectingCombat + 0.5 s post-combat SafeAfter) and a per-target
           0.25 s cache; ADDON_ACTION_BLOCKED on CheckInteractDistance is a
           breaker (2 hits ⇒ probe off for the session, melee UNKNOWN, no wire
           change — cell 29 bit1 already means UNKNOWN),
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
            Racial-scope rows (v3.x, no wire change): catalog entries tagged
            `Scope="Racial"` carry a normal Purpose and are appended to every
            spec's `offensive`, `defensiveMinor` and `selfHeal` lists; the
            bridge's known-spell filter selects the race the player actually is
            (no `UnitRace`, no wire field). Self-heal racials ride `selfHeal`
            (SelfHeal toggle), Defensive-category rows ride `defensiveMinor`
            (covered by the `DefensiveCatalogSource` wire bit; only offensives
            need `IsOffensiveGapFill` id membership). A `SourceConfidence=Low`,
            live-unverified racial is forced `NeverAutomatic`. CC/mobility
            racials stay Never/manual; passives are skipped.
           The whole path stays inside MaxDps's own `enableDefensives` /
           `enableCooldowns` switches, so muting them upstream also mutes
           the gap-fill.
           CC slot-6 reuse (v3.4.0 Option A; v3.5 CC fix): the bridge walks the
           generated per-spec `cc` list (auto-eligible curated CC rows) and
           writes the first ready+bound candidate into the reused Interrupt
           slot 6 ONLY when MaxDps names no interrupt that pins — and since
           v3.5 the pin requires the target sensor to confirm a live cast
           (`IsInterruptPinReady`), otherwise the slot rotates the CC pool. The
           CC walk skips boss targets (`IsBossTarget`, fail-open) and is gated
           by the addon CC toggle (`IsCC`, now a `Get("CC")` delegate); the
           companion's `CrowdControlGate` remains the authority. The provider
           holds any Stun/Silence row unless the target is observably casting
           (Q1 casting-only). No new slot and no new source bit — the id rides
           the existing slot-6 cells and the companion recognises it by curated
           CC membership at the existing `CrowdControlVetoes.Evaluate`
           call-site. Since v3.6 every class's qualifying single-target
           stun/silence is auto-eligible (Kidney Shot 408, Mighty Bash 5211,
           Intimidation 19577, Solar Beam 78675, Silence 15487); Shockwave
           (46968) is demoted to Suggest because the provider has no
           enemy-count signal. All Stun/Silence rows stay casting-gated.
  gates (bridge 3.3.0): Toggles.lua is the single addon-side restriction
        point. `SlotAllowed(slot, ctx)` is consulted before every WriteSlot —
        a denied slot is written empty with the valid flag clear, i.e. the
        ordinary no-candidate state (no wire bit). It owns the per-slot rules
        plus OOC (blanks 1-8 out of combat) and Solo (blanks 3/8 while known
        ungrouped, emergency HP excepted); TTK guard OFF forces target band 15
        (collaterally blinding execute + Burst consumers, v3.6) and
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
UI-only class/spec publish — 2026-10-01
  after the per-frame TTK feed and BEFORE the link/out-of-combat gates, the
  engine publishes the decoded class/spec for the UI readout
  (TryGetLiveClass/TryGetLiveSpec → class badge + console home). The v5
  class/spec cells decode independently of the sensor block, so a degraded
  (held out-of-combat) frame still reports its class; when the sensor block is
  invalid the publish falls back to a class/spec-only context. The send paths
  still assign their own real CombatContext before any scheduler/policy work
  (:844, :989), so the UI copy can never influence a decision — it closes the
  "AUTO DETECT while holding out of combat" readout defect.
        │
        ▼
Out-of-combat gate (CombatGate) — 2026-09-30, fail-closed
  after the link gate and before the paused/target/auto-target/auto-interact/
  send paths, the engine holds out of combat unless the app's OOC toggle is on
  (CombatOnly=false) AND the bridge echoed the Ext3 OOC mirror bit (bit 9) AND
  (the frame is Active with a target OR the state is NeedTarget/NeedInteract —
  the auto-target/interact asks, gated per-toggle); an in-combat frame always
  passes. An
  explicit `[Targeting] CombatOnly=0` is honoured but only warns
  (`AppSettings.LoadWarnings`). The SAME predicate rides the scheduler input
  (`ScheduleReason.OutOfCombat`) and the MovementGuard auto-target/auto-interact
  gates require the mirror bit too.
       │
       ▼
Candidate tracker (Decision/CandidateTracker)
  v3.5 last-seen set keyed by (slot, spellId): stroke, first/last-seen,
  pressed-since-change, TTL Clamp(max(1000, 3*tickMs), 1000, 2500) ms where
  tickMs is the measured frame interval (EMA of consecutive Update stamps,
  alpha 1/4; no clock read — derived only from the NowMs already passed in;
  <0 falls back to the 1000 ms floor; the bridge's assumed 33 ms tick also
  floors at 1000 ms). The floor keeps a just-rotated sibling eligible; the
  ceiling bounds a stalled sampler. The bridge rotates a slot's ready/bound
  pool, so a per-slot single stroke could never be stale; the set keeps an
  independent stamp per candidate and the TTL snapshot drops any identity the
  bridge stopped offering (stale is never pressed). Companion-only slots enter here exactly
  like MaxDps slots — one scheduler input, one send path, no second engine.
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
  v3.6 adds early paths so the guard still covers fast trash: a **provisional**
  estimate (`Provisional=true`, `Valid` still false) from a fast drop (≥3 bands
  over ≥0.5 s within 2.5 s) or a low first sight (band ≤3 in combat), plus
  `AgeSec` (time on the current target) and a **fast-pack latch** set by two
  quick kills (≤12 s life, last frac ≤0.25) within 20 s and cleared by any
  target reaching age ≥12 s with frac >0.5 or TTK ≥30 s. The policy uses
  `EffectiveTtkKnown = TtkValid || TtkProvisional`; no-target/unknown-HP clears
  provisional but keeps the latch.
  v3.7 adds an adaptive-history stage: every valid kill (a low-HP departure with
  life ≥2 s and a ≥0.15 frac loss) is appended to a bounded rolling KillHistory
  (last N=8 kills, 240 s max age, cleared after 90 s of no target + out of
  combat). The window's nearest-rank p75 burn rate (frac/s, deliberately
  pessimistic) blends with the live EWMA — `w = clamp((spanSec-2.5)/6, 0, 1)` —
  so a fresh pull starts on the trash-learned rate and the live rate wins by
  ~8.5 s. With no trusted live rate but a target, in combat and a usable frac,
  the history rate alone yields a provisional estimate (`HistProvisional`).
  The policy's `NeedAdaptive = max(MinTtkSec, min(DurFactor·activeDur, 20))`
  then holds a T1 offensive while `HistTtkSec` is below it (reason `ttk-hist`),
  restricted to the provisional-eligible majors while the live estimate is
  invalid. 2026-10-04: `LiveReleasesHistory` fails that history hold open when a
  valid live TTK ≥ the need, or (no valid live TTK) the target is ≥8 s old with
  known HP ≥85% — a trash-learned window cannot hold
  Recklessness/Avatar/Ancestral Call on a long-lived rare/elite.
  `History=0` reproduces the pre-history estimator exactly.
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
    · TTK gates (v3.2.0, extended v3.6): T1 holds an offensive when
      valid/provisional TTK < minTtkSec (usage default or curated; the
      provisional guard holds only MajorBurst/Transformation/Summon/
      WindowDriven, never minors); T2 bypasses the pairing hold when valid TTK
      ≥ 2·cd+dur (absent cd skips); T3 bypasses it when executeFavored and target
      HP ≤ executeBelowPct. Invalid TTK skips all three (fail open) **except**
      the v3.6 grace hold — MajorBurst/Transformation/Summon with no estimate,
      the fast-pack latch set and age <4 s holds `"fast pack, waiting for TTK"`
      (never without the latch). A v3.6 kill-secure bypass fires either
      MajorBurst/Summon or a curated `killSecure:true` major when the estimate
      is valid, age ≥20 s, target ≤35% and TTK 3–20 s; execute range also
      bypasses the guard when TTK ≥3 s or unknown. v3.8 adds a **warmup hold**
      (`"warming up TTK"`): a provisional-eligible major with no valid/provisional
      estimate, target age < `[TimeToKill] WarmupSec` (default 3 s; 0 = legacy)
      and a binding history short of its buff-aware need is held; execute /
      kill-secure / zero-need / long-history carve-outs fail open. The
      adaptive-need input is now the ability's own **buff duration**
      (`max(MinTtk, min(DurFactor·buffDur, 20))`, the research 1/2 rule) rather
      than the T2 2·cd+dur       window. 2026-10-04 adds a **sub-50s bypass**, widened 2026-10-06 to
      **≤60 s** (`TtkPolicy.SubFiftyBypass`): a curated `CooldownMs` in (0, 60 s]
      fails the waste/history/warmup/grace guards (and the Burst preset) open,
      so a rotational short CD (Colossus Smash/Warbreaker/Demolish/Odyn's Fury/
      Shield Charge/Demoralizing Shout 45 s, Essence Break 40 s, Divine Toll
      60 s) fires regardless of target TTK; `CooldownMs == 0` stays gated
      (fail-closed), and `OffensiveUsage.Summon` is excluded so the true 60 s
      summon major Summon Demonic Tyrant 265187 stays TTK-gated
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
    · defensives T4 (v3.2.0, tiered v3.6): emergency HP always overrides.
      Solo holds Minor at TTK <6 s, Major at <10 s, Immunity at <15 s; the
      Solo ladder-band carve-out lets a Major bypass the hold at/below
      `SoloMajorHpPct` and Immunity at/below `SoloImmunityHpPct`. Group holds a
      Minor only when the estimate is valid <4 s, urgency is below Orange and
      the fast-pack latch is set; group Major/Immunity are never gated (enemy
      count is unobservable and the tank may be dying to other mobs)
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
  link/protocol gate → fail-closed out-of-combat gate (2026-09-30:
  `CombatGate.OutOfCombatPermitted`, holds with `ScheduleReason.OutOfCombat`)
  → hard cast/channel gate (policy-off) → five-state
  policy verdicts (Use / Hold / Skip / Unavailable / Unknown; Hold+Unknown =
  held, Skip+Unavailable = skipped, only Use scheduled)
  → rank (interrupt > emergency > defensive > self-sustain > main > mobility
  > offensive > consumable > trinket) → duplicate collapse (a collapsed Main
  is kept aside and re-admitted when the same-stroke press that replaced it is
  held, not emitted) → stale and
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
  window (situational 1.5/3/6/10 s; the Main rotation is hard-capped at
  1.5/3/3/3 s so a stuck Arms suggestion can never silence it — reset by a
  success); a >6 s silence restarts the ladder, and a target/combat edge drops
  only the Main failure memory while situational slots stay held; failure
  suppression is otherwise NOT cleared by state transitions, so a dead action
  cannot be re-armed by every GCD pulse and can never block other ranks

Local telemetry (opt-in: [Telemetry] Enabled, default 0)
  every Report tick → JSONL event (tick/send/link) with the plan head,
  policy verdicts + reasons, combat context (range/buff/interruptible/
  options) and candidate snapshot, plus the additive `ttk`/`ttkp`/`age`/`latch`
  fields (v3.6.0) and the v3.7 `hk`/`hr`/`hs`/`hprov` adaptive-history readout
  → bounded in-memory ring (no disk until
  Export) ──► Export file ──► ReplayRunner: recorded DecisionContext →
  DecisionEngine.Evaluate AND every recorded policy verdict →
  PolicyEvaluator.Evaluate (memory rebuilt from send events in live order:
  a send precedes its own tick, so a tick never sees its own send; the TTK
  estimator is rebuilt by feeding the recorded (ttkMs, hasTarget, thp)
  series in order, which also rebuilds the v3.7 kill window) → diagnostic
  report (legacy decisions and policy verdicts must both recompute with 0
  mismatches; the recorded hk/hs are compared against the rebuilt window and
  must reproduce exactly)
```

No memory read, no injection, no OCR at any stage. The knowledge base is
static data; the policy only consumes decoded protocol fields and the
companion's own send history. Nothing invented, nothing secret, no LLM in the
runtime.

## Custom MaxDps 12.1 fork (bridge-side, no wire change)

```
vendor/ (read-only) ──► custom/upstream-pristine/ ──► custom/patches.json
                                                        │  tools/Sync-CustomMaxDps.ps1
                                                        ▼
                                              custom/out/  ──► AddOns
                                              (generated, reversible)

addon/MaxDpsBridge/MajorCooldowns.lua  MDB.MajorCDDeny
        └─► Reader.GetMainSpellID: skip denied ids in the SpellsGlowing scan,
            reject a denied MaxDps.Spell → MAIN never carries a major CD
```

The fork corrects 12.1 stale major-cooldown ids without editing `vendor/`.
`MDB.MajorCDDeny` is the runtime fix and needs no sync run; `custom/out/` is
an optional, reversible vendor patch set for the same symptom. P-DATA-023
(2026-10-04) additionally inserts Ravager 228920 into the Arms `offensive`
table — an **addition**, not an id move — so the vendor glow reaches the
bridge's Offensive slot while `MDB.MajorCDDeny` keeps it out of Main. P-DATA-024
(2026-10-06) applies the same addition pattern to Divine Toll 375576 in the
Retribution `offensive` table (not denied from Main: a 60 s on-GCD core button
may legitimately be a Main suggestion). Scope is
**Arms only**: Fury/Protection list Ravager in the bridge catalog but their
vendor `offensive` tables lack it, so adding those is a separate patch if
wanted. Neither changes the wire: cells, `PROTOCOL_VERSION` and the bridge
encoder are untouched. See `custom/CUSTOM_FORK.md` and `docs/TESTING.md` §3h.

## No-downtime MAIN (bridge + scheduler, no wire change)

The MAIN slot must never be stranded on a dead tick (Q1 = NO empty Main).
Neither the pixel layout nor `PROTOCOL_VERSION` changes; the pipeline is:

```
Reader.GetMainSpellID (addon; only what MaxDps already glows)
  └─ collect every glowing id (On == 1), drop MDB.MajorCDDeny, sort ascending
       └─ first id with C_Spell.IsSpellUsable (power veto only:
          usable==false AND noPower==true; secret/nil/throw fail OPEN) ──► MAIN
       └─ all denied/power-starved ⇒ MDB.MainFallback[specID | "CLASS:Spec"]
          (MainFallback.lua; Fury 72 / "WARRIOR:Fury" → Bloodthirst 23881;
          Ret 70 / "PALADIN:Retribution" → Judgment 20271, Blade of Justice
          184575, Crusader Strike 35395) ──► MAIN
       └─ no candidate at all / unlisted spec ⇒ nil (idle by design)

ActionScheduler (companion)
  └─ a failed MAIN press re-arms at MainReprobeMs = 150 ms (2026-10-04 WHITE
     Main immediate; was 400 ms); MainSameSpellNoOpCap = 3 bounds the no-op
     repeats and a GCD-observed send resets the count
  └─ a CHANGED Main identity drops the superseded pick's backoff and
     presses the next tick
CandidateTracker
  └─ the sole Main candidate's TTL is refreshed while the frame heartbeat
     stays fresh, so a brief empty-Main re-probe does not expire it
```

The fallback is the deliberate, user-approved exception to "the bridge only
encodes what MaxDps suggests" — it is custom behaviour, not upstream parity;
unlisted specs stay nil and are OWED. See
`docs/plans/2026-10-03-no-downtime-main.md` and `docs/TESTING.md` §3i.

## WHITE Main immediate (scheduler + decision, no wire change)

2026-10-04, spec `docs/plans/2026-10-04-main-immediate.md`. The WHITE core
rotation (Main slot) executes immediately when the bridge reports enemy in
range/sight/castable; only the game-forbidden holds remain. No wire change,
bridge untouched; Offensive keeps TTK/burst/pair. `DecisionEngine` mirrors the
stale exemption on the legacy path.

Main gate table (`Scheduler/ActionScheduler.cs`):

| Gate | Main behaviour |
| :--- | :--- |
| recency-stale demotion | exempt — `Slot.SelfHeal or Slot.Main` bypass `IsStale` (`:657`) |
| pending-confirm demotion | exempt (`:680-683`) |
| MinInterval | `MinIntervalApplies` (`:1037`): a **different** Main press never waits; only an **identical** slot+stroke+spell repeat inside `MinKeyIntervalMs` (120 ms) is held — the pre-GCD-flip double-fire guard |
| send cap / backoff | `MainSameSpellNoOpCap` (3) counts only sends with `!_pendingConfirm.SawGcd`; a GCD-observed send resets the count (`:236`); re-arm `MainReprobeMs = 150` ms |
| failure suppression | `NoteFailure` never writes `_failedUntil` for Main (`:950`); streak/decay counters still advance for telemetry/replay |
| GCD / cast / channel / range / melee / target / power / OS `blockedUntil` / TTL | KEEP — game truth or structural |

`Decision/DecisionEngine.cs` mirrors C1: Main is exempt from stale demotion
(`:87`) and takes no stale confidence penalty (`:103`), so a Main candidate is
never demoted behind a fresh Offensive/Trinket/Consumable. 2026-10-06
zero-delay core: a Main that shares a physical stroke with a higher-ranked
collapsed candidate is re-admitted when that press is held, and `NoteAttempt`
suppresses a non-emitted Main at `MainReprobeMs` (150 ms), not the 500 ms
situational window. Live checks OWED (`docs/TESTING.md` §3j).

## MaxDpsBridgeExp (CURRENT in-game-config fork, no wire change)

**`addon/MaxDpsBridge` = LEGACY (frozen). `addon/MaxDpsBridgeExp` = CURRENT.**
Exp is the stable sources renamed `MDB→MDBX` / `MaxDpsBridge→MaxDpsBridgeExp`,
with a manual copy of the stable catalog/fallback/flag files; ports had to be
applied to BOTH only until the freeze — from now on Exp-only (stable gets no
further ports unless explicitly requested). The shared MaxDps fork pipeline
below is unchanged.

A second, separate addon (`addon/MaxDpsBridgeExp/`) implements the approved
`docs/plans/2026-10-03-ingame-config.md` fork: the player configures the
toggles in-game. It is **mutually exclusive** with the stable bridge — the
fork checks `C_AddOns.IsAddOnLoaded("MaxDpsBridge")` and stays inert (no pixel
frame, no strip ticker, no slash command) when the stable addon is loaded.
No stable file, `vendor/`, or wire file changes; the 43-cell v5 + Ext2 + Ext3
strip and `PROTOCOL_VERSION = 5` are byte-identical, so the same companion exe
decodes either addon.

```
addon/MaxDpsBridgeExp/
  MaxDpsBridgeExp.toc      Interface 120100, OptionalDeps MaxDps,
                           SavedVariables MaxDpsBridgeExpDB,
                           SavedVariablesPerCharacter MaxDpsBridgeExpCharDB
  Exp.xml                  load order (Toggles, Catalog, MajorCooldowns,
                           Keymap, Bars, MainFallback, Reader, Bridge, Core,
                           Profiles, Overlay, Settings, Slash)
  Toggles.lua ... Bridge.lua  stable copies renamed MDB→MDBX /
                           MaxDpsBridge→MaxDpsBridgeExp, VERSION 3.7.1-exp;
                           Bridge.lua no longer registers /mdb and exposes
                           MDBX.HandleCommand for diagnostics
  Exp/Core.lua             identity, Print, 200-line console ring,
                           MDBX.IsInert() mutual-exclusion guard
  Exp/Profiles.lua         v1 Global/Spec/Talent store, per-character
                           override, versioned export/import line
  Exp/Overlay.lua          insecure BackdropTemplate overlay (Header +
                           14-pill/4-col grid + Footer), drag-if-unlocked,
                           SetClampedToScreen, dirty flag + 0.2 s ticker only
                           OnShow, InCombatLockdown→PLAYER_REGEN_ENABLED
                           defer queue; no secure templates
  Exp/Settings.lua         Settings.RegisterCanvasLayoutCategory (+ legacy
                           InterfaceOptions fallback), 6 tabs (Pause /
                           Rotation / Binds / Settings / Console / Debug)
  Exp/Slash.lua            /mdbx ONLY + global MDBXBinding(key)
  Bindings.xml             BINDING_HEADER_MDBX + MDBX_TOGGLE_OVERLAY +
                           MDBX_TOGGLE_<KEY> ×14
```

The fork owns toggles (ADDON-WINS, the same `Toggles.SlotAllowed` single
gate); the companion Exp mode mirrors the Ext3 mask cells 40-42 read-only.
Offline bar: `luac -p` every file, `lua tests/secret_harness.lua`,
`pwsh tools/ability_audit.ps1`. Live retail checks stay OWED.

## Class Browser (S6) / Class skills screen

```
AbilityCatalog (vendor Cooldowns + curated + merged class-spells)
  + ClassSpellBook (generated class-spells.json)
  + curated per-spec extras (Mobility/SelfHeal/Defensive)
  + ClassOverlayLoader (Knowledge/classes/<CLASS>.json; registry-gated, then
    AbilityOverrides.Apply = per-machine mode/minUrgency store)
        │
        ▼
ClassBrowserView (S6, v3.5) — one window, Class | Spec | Mode selectors
  (All, Main, Offensive, Defensive, Interrupt, CC, Mobility, Solo
  self-sustain, Consumable, Trinket, Utility/manual) + quick knobs
  (min HP% filter, urgency floor, solo-only/normal-only/always)
  rows: icon + name + cooldown + tier/ownership badges + live why-held
  verdict + ON/OFF toggle, virtualized/owner-drawn via VirtualAbilityList
        │
        ▼
AbilityPolicy.With / WithMode + AppSettings override store
  → settings.ini [Abilities] On=/Off=/Modes= + ability-overrides.json
  → PolicyEvaluator / ActionScheduler
```

The browser CONSUMES the already-resolved registry entry (registry + overlay +
overrides) and the engine's published live verdict; it never evaluates a
scheduler gate — the scheduler owns evaluation. `ClassSkillTree` still owns the
membership/ordering used by the legacy `ClassSkillsView` (kept as a test seam)
and `--dump-class-skills`. Icons come from `SpellIconCache`, never from game
memory.
`MaxDpsCompanion.exe --dump-class-skills=<path>` dumps the merged tree for
filter curation, and `--ui-snapshot-class-skills=<png>`
[`--ui-snapshot-class=CLASS --ui-snapshot-spec=SPEC`] renders the screen for
design review without a game.

## Console home (S7, v3.5) / diagnostics

```
MainForm
  └─ ConsoleHome (default view, mounted over the classic body; S7)
       top bar: Pause · Folder · Console · Binds · Settings · Rotation ▾ · Debug
       spec header: inferred role badge + per-spec summary
       status block: app/bridge state · now-action + why · last change
       rolling status + update logs (RollingLog)
       preset strip (PresetChip): Solo · Levelling · Dungeon · Raid · Tank
         │  PresetRequested / RotationRequested
         ▼
MainForm.ApplyPreset → writes the SAME 14 hero ToggleSwitch.Checked values
  through the existing SaveNow path (never a hidden mode, never a wire change)
```

`ConsolePresets.cs` expresses the five bundles as the same 14 toggles plus a
`ConsoleRole` spec-name role inference. Every `ConsoleHome` update method is
value-only (text writes / invalidate), so the status timer performs no layout.
The console home is the default view but is hidden while an Advanced/Abilities
popup is open, so only one full-window overlay shows at a time; the popup
restores it on close only if the popup hid it.
The **Install doctor** (`Diagnostics/InstallDoctor.cs`, S8, mounted on the
Advanced → Diagnostics page with a ~2 s throttle) is a pure `Audit` over
best-effort file reads: exe build commit vs repo HEAD, configured vs emitted
`CellSize`, app mask vs the Ext3 mirror, and addon version vs companion
(major/minor). `WhyNotFiring` (S8) explains a non-firing row (toggle state,
scheduler verdict, candidate staleness) and is rendered owner-drawn.

## Classic UI shell (v3.0.0; replaces the v2.8 rail/pages shell)

```
MainForm (borderless; 660-wide fixed frame; no frame ring; tray; pause
          hotkey; remembered size only when [Window] Layout=classic3)
  ├─ title bar 48px
  └─ GradientCanvas
       ├─ classic body (TableLayoutPanel, fixed absolute rows; NO AutoScroll on
       │   any timer path — the v2.8.1 NormalizeScroll defect class is pinned
       │   by ClassicUiTests.ClassicUi_ScrollSurvivesRefresh)
       │     hero RoundedCard: status 42 (LinkLamp + state + ClassBadge)
       │       · live 42 ("Now: <action> — <why>", ellipsis + tooltip)
       │       · strip 40 (StripView, live sampled cells)
       │       · "Rotation" header + 4 two-toggle rows (Main|Offensive,
       │         Defensive|Interrupt, Self-heal|Mobility, Consumable|Trinket)
       │       · "Automation" header + 3 two-toggle rows (Solo|Out of combat,
       │         Auto-target|Auto-interact, Time-to-kill|Crowd control)
       │     button row 1 (52): Start | Stop | Launch Game
       │     button row 2 (46): Recalibrate | Abilities… | Advanced… | Open Folder
       │     status line (26)
        ├─ Advanced scrim + centred card (tabs Configuration | Diagnostics |
        │   Intelligence), built lazily once, measured GlassCards per tab
         └─ Abilities scrim + centred card (one "Class browser" tab, S6)
           Both masks are built by the one BuildUnifiedPopup shell and fill
           client-24 × client-24 (min 360×280), capped at their maxWidth
           (Advanced 620 / Class browser 900)
    One full-window overlay at a time: opening Advanced/Abilities hides the S7
    console home and the other scrim (RestoreConsoleAfterPopup only if a popup
    hid it). v3.5.2: the scrims are parented to the top-level Form (not the
    canvas) and track Form.ClientRectangle, and Form.Padding is empty, so ONE
    opaque mask covers the entire window; the 24/10/24/12 inset lives on the
    classic body scroll and the console home stretches to the full body canvas —
    no background menu/ring peeks behind.

  Default client 660 × min(content, working area); MinimumSize 520×560; Esc
  closes the topmost popup. Width tiers (UiScale, D5): client width picks
  Compact (≤560) / Classic (≤700, the 660 default) / Roomy (≤950) / Wide
  (>950); a tier scales font step, row heights, card padding, button heights
  and toggle size and is applied to the main window plus both popups on a
  120 ms debounced resize (never mid-drag). The client height is the MEASURED
  content height (`MainForm.LayoutHero` measures the hero + body rows and
  `ApplyContentHeight` clamps it to the working area), so the window grows with
  its content instead of scrolling. Restored classic primitives live in UiControls.cs
  (LinkLamp, StripView, ClassBadge, RoundedCard, GradientCanvas,
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

## companion-rs (Rust port, side-by-side) — 2026-10-10

`companion-rs/` is a Cargo workspace that ports the C# companion
(`docs/plans/2026-10-10-rust-companion.md`), kept beside `app/` until a parity
gate. It consumes the SAME bridge wire (v5/Ext2/Ext3) and the SAME hybrid
input contract as `KeySender.cs` — keyboard PostMessage-only, mouse/wheel
`SendInput` behind a mandatory foreground gate; `addon/*.lua`, `app/*.cs` and
`vendor/` are untouched and there is no `docs/PROTOCOL.md` change.

```
mdc-protocol  decode v5/v4/v1 + Ext2/Ext3 (pure; golden harness)
      │
mdc-engine    candidate tracker → scheduler → decision/rotation (injected Clock)
      │
mdc-platform{,-win,-mac}   find_wow / capture (BitBlt DIB) / post_key (PostMessage)
                           / post_mouse + is_foreground (gated SendInput mouse)
      │
mdc-settings / mdc-telemetry / mdc-commands / mdc-cli / mdc-app (eframe shell)
```

Toolchain is pinned to `stable-x86_64-pc-windows-gnu`
(`companion-rs/rust-toolchain.toml`); MSVC has no linker on the dev box (no VS
C++ build tools), and the GNU host requires a MinGW-w64 sysroot on PATH
(WinLibs `mingw64\bin`) for the `-lshlwapi` import libs that `mdc-app` needs.
`#![forbid(unsafe_code)]` in every crate except `mdc-platform-win` (documented
Win32 FFI). Offline bar: `cargo build --workspace`, `cargo test --workspace`,
`cargo clippy --workspace --all-targets -- -D warnings`. `mdc-app` live
window/capture and real captured golden vectors remain OWED.
Static ≠ automated test ≠ live in-game.

### Gallant console (mdc-app, 2026-10-10)

`crates/mdc-app` opens the Gallant console
(`docs/plans/2026-10-10-gallant-parity.md` §1/§3/§5) as its default view; the
pro-panel dock is kept behind a default-off `Tools` checkbox.

```
mdc-app/src/main.rs      eframe shell: window title `MaxDPS Companion v3.7.7
                         Gallant`, resizable viewport inner 720x780 / min 460x400,
                         theme::apply_theme, settings_path() ->
                         Settings::load(exe_dir/settings.ini), default_platform()
                         -> Runtime::new, console as the default view; Viewer
                         dispatches dock tabs to the real panels (no stubs)
mdc-app/src/console.rs   Console: owns Runtime + local state (details_expanded,
                         cached Snapshot, capacity-200 VecDeque log ring); header
                         (title + gear `Settings` + widgets::pill), tight 2x3
                         transport grid (local item_spacing 6, full-width buttons)
                         wired to runtime start/stop/toggle_pause/calibrate/
                         open_game, and the status card (hero | Details | Now |
                         Log HH:mm:ss MONO_STAMP mono). ui() returns
                         ConsoleActions { open_settings }; snapshot() exposes the
                         cached Snapshot. tick() polls + requests a 10 Hz repaint;
                         the engine thread never touches egui
mdc-app/src/panels/      REAL panels (<300 LOC each): panel_settings.rs (edits
                         settings.ini, clamp + Settings::save + Revert),
                         panel_doctor.rs (labelled rows: settings path/exists,
                         probe_wow window, protocol, exe dir, version, commands,
                         build profile; Re-run + Copy), panel_classbrowser.rs
                         (8 wire slots + enable checkboxes + Snapshot::
                         suggested_slot marker; no class registry yet),
                         panel_telemetry.rs (JsonlRecorder start/stop, <=50 live
                         log tail, replay summary; local-only). mod.rs holds
                         PanelEnv, Panels, InfoRow/Tone, probe_wow, exe_dir
mdc-app/src/theme.rs     Gallant tokens + apply_theme; install_fonts appends
                         Windows system symbol faces (seguisym.ttf `Segoe UI
                         Symbol`, then segmdl2.ttf `Segoe MDL2 Assets`) to the
                         Proportional + Monospace fallback lists so transport
                         glyphs render instead of tofu; has_glyphs(ctx, s) guards
                         button labels (drop icon when no font covers it)
mdc-app/src/widgets.rs   RunState (From<mdc_runtime::RunState>), pill,
                         transport_button (fills column width, glyph-gated), card
```

Settings map to the runtime engine config at Start/Calibrate; the Settings panel
writes `settings.ini` via `mdc-settings` (unknown keys/comments preserved) and
the Class Browser writes `[Spells]` enable flags; `mdc-commands` `ui.panel.*`
handlers return real structured panel data (never `status:stub`). `OPEN GAME`
executes the runtime launcher (`[Launch] BNetPath`, auto-detect fallback). No
wire change. Live window/render OWED. Static ≠ automated test ≠ live in-game.

### 2026-10-10 Gallant console UI polish (mdc-app)

- **Resizable window**: viewport is now `.with_resizable(true)` with a modest
  `.with_min_inner_size([460, 400])` and `.with_inner_size([720, 780])` (was a
  hard `[900, 600]` floor that blocked shrinking); the transport grid and status
  card reflow via full-width buttons and the scrolling log.
- **Tight transport grid**: `console.rs` saves/restores `item_spacing` (6,6)
  locally around the 2x3 grid and both `start_button` and
  `widgets::transport_button` size to `ui.available_width()` (no fixed 96px
  gutter); vertical gaps between header/grid/card reduced 8 -> 6.
- **Icons**: `theme::install_fonts` appends Segoe UI Symbol (covers
  `▶ ■ ⏸ ◉ ⚙ ⤢`) and Segoe MDL2 Assets to the font fallback chain; every icon
  is gated through `theme::has_glyphs`, so a missing font yields a text-only
  label (never a tofu box).

### 2026-10-10 legacy declaration — C# client frozen, Rust is the future

**`app/MaxDpsCompanion/` + `dist/` are LEGACY/FROZEN** repo-wide (`LEGACY.md`,
`app/MaxDpsCompanion/LEGACY.md`): no future rollouts/features, only critical
security fixes on explicit request. The future is **`companion-rs/`**; the
current target addon + distribution stays `MaxDpsBridgeExp` + `dist-exp`
(the stable `addon/MaxDpsBridge` + `dist\` pair is likewise frozen). Physical
directories are not renamed (would break the csproj/workflows); the C# tree is
retained as the behavioural fidelity reference for the Rust port.

## File map

```
MaxDps-Companion/
  companion-rs/              Rust port of the companion (side-by-side with app/
                             until the parity gate; no wire/PROTOCOL change).
                             Cargo workspace; rust-toolchain.toml pins
                             stable-x86_64-pc-windows-gnu; needs a MinGW-w64
                             sysroot on PATH; #![forbid(unsafe_code)] except
                             mdc-platform-win. Mouse `SendInput` + foreground
                             gate implemented; mdc-app live run OWED.
  addon/MaxDpsBridge/        bridge addon 3.7.7 (v5 + Ext2 + additive Ext3
                             encoder, candidate rotation, in-game toggle UI,
                             /mdb commands)
    Catalog.lua              GENERATED class/spec ids + extras (--gen-catalog,
                             incl. per-spec offensive (curated), defensive
                             (Red) and defensiveMinor (Orange) gap-fill lists,
                             plus the v3.4.0 `cc` auto-eligible crowd-control
                             list, and the aliases block emitted as
                             MDB.SpellAliases). v3.x appends scope=Racial ids
                             to every spec's offensive/defensiveMinor lists
                             (race implicit via the known-spell filter)
    Keymap.lua               binding string -> virtual key
    Reader.lua               MaxDps readout, secret guards, v5 sensors,
                             defensive urgency + gap-fill, spell variants
                             (base/override/alias resolution), SelfHeal2,
                             ExtraCandidates (mobility/selfHeal/defensive/cc),
                              MDB.GetCrowdControlCandidate (slot-6 CC source),
                              CategoryOf tail also consults
                              MDB.FlagOffensiveExtra (228920 Ravager
                              2026-10-04; 375576 Divine Toll 2026-10-06), so a
                              flagged offensive absent from
                              MaxDps.classCooldowns.offensive is still
                              classified for the Offensive Flags scan,
                              v3.5 MDB.IsInterruptPinReady (casting-gated
                             interrupt pin) + MDB.IsBossTarget (CC boss skip),
                             Ext2 HP-curve source,
                             v3.6 MDB.ProbeTargetMelee (single event-gated,
                             breaker-protected CheckInteractDistance site;
                             EnsureProbeEvents owns regen/encounter/target/
                             world/BLOCKED events)
    Bridge.lua               strip rendering + 40-cell v5 + Ext2 encode
                             (urgency + HP curve + SelfHeal2 + Ext2 checksum);
                             slot 6 = interrupt-first only while the target
                             sensor confirms a live cast, else the v3.4.0 CC
                             candidate (wire frozen, no new slot);
                             v3.6 TargetState consumes MDB.ProbeTargetMelee
                             (never calls CheckInteractDistance itself);
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
     MajorCooldowns.lua       custom MaxDps 12.1 fork (T3): `MDB.MajorCDDeny`
                              table of stale/current major-CD ids. GetMainSpellID
                              skips every denied id in the SpellsGlowing scan
                              (Reader.lua:332-355) and rejects a denied
                              MaxDps.Spell, so a stale glow or a 2-3 min CD is
                              never encoded as the MAIN slot; the Offensive slot
                              is an independent Flags-scan, so an empty Main
                              cannot deadlock the rotation. 2026-10-04: 167105
                              Colossus Smash UN-denied (vendor/live-verified as a
                              ~45 s Arms rotation button), 228920 Ravager denied
                              for MAIN and added to the new
                               `MDB.FlagOffensiveExtra` (228920 Ravager). Reader
                              CategoryOf maps a flagged id to "offensive" so the
                              Flags scan still routes it though it is absent from
                              `MaxDps.classCooldowns.offensive`. 2026-10-06 adds
                              375576 Divine Toll to the flag and deliberately
                              NOT to D (60 s on-GCD core button). Bridge-side
                              PRIMARY fix; no vendor edit, no wire change.
     MainFallback.lua         no-downtime MAIN fallback (plan 2026-10-03): when
                              every non-denied Main glow is denied/power-starved,
                              Reader returns the first castable per-spec filler
                              here instead of an empty slot. Fury 72 /
                               "WARRIOR:Fury" → Bloodthirst 23881; 2026-10-04
                               Arms 71 / "WARRIOR:Arms" → Mortal Strike 12294 then
                               Overpower 7384; 2026-10-06 Ret 70 /
                               "PALADIN:Retribution" → Judgment 20271, Blade of
                               Justice 184575, Crusader Strike 35395 — all vendor
                               name-verified). Unlisted
                              specs stay nil. Deliberate exception to
                              "encode only what MaxDps suggests".
   custom/                    custom MaxDps fork (read-only vendor preserved):
     upstream-pristine/        untouched v11.3.49 snapshot + MANIFEST.json
                              (fileSha256 churn baseline; captured 2026-10-03)
      patches.json              declarative patch manifest (77: 24 data +
                                40 guard + 13 canary); P-DATA = Cooldowns.lua id
                                moves (22) plus the Ravager 228920 Arms-offensive
                                addition (P-DATA-023; anchor = commented
                                Sweeping Strikes line) and the Divine Toll 375576
                                Retribution-offensive addition (P-DATA-024;
                                anchor = commented Wake of Ashes line), P-GUARD =
                                Specialization ACSpells bypass, canary =
                                `local setSpell` sentinel
     out/                      GENERATED patched tree (build artifact, absent
                              until a sync run; never hand-edited)
   tools/Sync-CustomMaxDps.ps1 T2 sync tool: churn-diff a new upstream drop vs
                              MANIFEST, apply patches.json into custom/out/,
                              write SYNC-REPORT.md; update-aware (fixedWhen ⇒
                              FIXED-UPSTREAM skip; ambiguous anchor ⇒ CONFLICT;
                              all-fixed ⇒ skip the run entirely); publishes to
                              `\Interface\AddOns` only on a clean run, with a
                              `_backup/<stamp>/` rollback; vendor/ never written
   app/MaxDpsCompanion/       WinForms companion (sampler → PostMessage).
                              LEGACY/FROZEN 2026-10-10 (LEGACY.md): no future
                              rollouts; fidelity reference for companion-rs/.
                              dist/ + addon/MaxDpsBridge/ (stable) also frozen.
    Knowledge/               ability knowledge base (v2.0; defensive v2.3; registry v2.6)
      AbilityModel.cs        enums + AbilityDefinition + slot mapping,
                             `Scope` ("Racial" = race-carried, carried by every
                             class/spec; null = class-bound),
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
                             path), RacialScope/RacialIds + racial-aware
                             IsOffensiveGapFill (v3.x, class-bound arrays
                             unchanged), registry derivation rules, patch guard
                             (12.1 / 120100 / 11.3.49, CatalogVersion 4)
      CrowdControlCatalog.cs curated verified CC registry (v3.4.0): DR
                             category, Single/AoE, CD, AutoEligible; read by
                             AbilityCatalog.CrowdControlFor / CrowdControlGapFill.
                             v3.5 raised Storm Bolt 107570 to AutoEligible=true.
                             v3.6 adds the all-class auto-fire set: flips Kidney
                             Shot 408, Mighty Bash 5211, Intimidation 19577 true;
                             adds Solar Beam 78675 (Balance) and Silence 15487
                             (Shadow); renames 9484 "Shackle Undead"; demotes
                             Shockwave 46968 back to Suggest (no enemy-count
                             signal). Stun/Silence stays casting-gated.
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
                             holds + Solo HP-banded escalation (SoloBandLatch);
                               v3.6 provisional waste guard + grace hold +
                               kill-secure + execute carve-out + tiered defensive
                               lookup; v3.7 ttk-hist adaptive branch + consumable/
                               trinket Burst release above a 5 s binding history;
                               v3.8 buff-aware need (buffDur, not T2 window) +
                               warmup hold before the gap-fill/pair gates
      KillHistory.cs         v3.7 pure/fake-clock bounded rolling kill window
                             (KillRecord AtMs/LifeSec/StartFrac/EndFrac;
                             Add/Prune/Clear/Count, nearest-rank
                             RateQuantile(q) and MedianLifeSec). Backs the
                             adaptive-history blend; no clock, no I/O.
      TtkEstimator.cs        v3.2.0 pure/fake-clock per-target TTK estimator
                             (band -> frac, reset/feed-from-anchor/EWMA seed,
                             clamp 300 s; invalid fails open); v3.6 provisional
                             estimate + AgeSec + fast-pack latch (extended
                             TtkEstimate record, additive defaults); v3.7
                             KillHistory hook + live/history blend + history-only
                             provisional estimate (`TtkOptions` defaults
                             History=1/Kills=8/MinKills=3/MaxAgeSec=240/
                             Quantile=75/DurFactor=0.5; `History=0` = exact
                             pre-history behaviour)
       TtkPolicy.cs           v3.2.0 MinTtkSec usage defaults + TTK field
                              forwarding for the T1-T4 gates; v3.6 tiered
                              thresholds, grace-hold/kill-secure helpers and the
                              `[TimeToKill] Fallback` mode; v3.7 NeedAdaptive
                              (max(base, min(DurFactor·activeDur, 20))) and the
                              `ttk-hist` T1 branch; v3.8 BuffNeed (buff duration)
                              + `DefaultWarmupSec`/`WarmupHoldHolds`; 2026-10-04
                               `LiveReleasesHistory` (valid live TTK ≥ need, or no
                               valid TTK + target age ≥8 s + known HP ≥85%) fails
                               `HistoryWasteGuardHolds` open, so a trash-learned
                               window never holds a major against a long-lived
                               rare/elite; 2026-10-06 sub-60s `SubFiftyBypass`
                               (`CooldownMs > 0 && <= 60_000` and not
                               `OffensiveUsage.Summon`, fail-closed on 0)
                               fails the waste/history/warmup/grace guards open so
                               curated rotational short CDs (Warbreaker 262161,
                               Divine Toll 375576, ...) are never TTK-conserved
                               while Summon Demonic Tyrant 265187 stays gated;
                               the `CandidateProviders` burst-preset hold is
                               gated the same way
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
                             (v3.x no longer filters the catalogued racial
                             actives BloodFury/ArcaneTorrent/GiftOfTheNaaru/
                             Berserking/Stoneform; racial passives stay junk)
      ClassSkillTree.cs      membership model: shared vs per-spec, section
                             grouping for the Class skills screen
      vendor-abilities.json  GENERATED from vendor/ (extraction script)
      abilities.json         CURATED policy layer
      class-spells.json      GENERATED lowest-priority layer from the retail
                             classSpellData block (tokens + ids only)
      spell-verification.json GENERATED live-client name/icon/verified per
                             class-spell id (wago.tools DB2 export); unverified
                             ids are not merged (see ClassSpellBook)
    ClassSkillsView.cs       legacy full-size Class skills screen (superseded
                             by Ui/ClassBrowserView.cs in S6; kept as a test
                             seam): class/spec dropdowns, shared + per-spec
                             sections, toggles; S5 TreeBuilder seam (S8 routes
                             it to the cache)
    Ui/ClassBrowserPrecompute.cs  S8: off-thread Class Browser row cache keyed
                             by (class, spec); warmed on Application.Idle and
                             consumed through ClassSkillsView.TreeBuilder
    Ui/ScaledIconCache.cs    S8: device-scaled icon bitmaps keyed by
                             (spell id, px); evicted on SpellIconCache.IconReady
    Ui/WhyNotFiring.cs       S8: pure explainer — toggle state + scheduler
                             verdict + candidate staleness (frame age)
    Ui/WhyNotFiringPanel.cs  S8: owner-drawn render of the explainer
    Diagnostics/InstallDoctor.cs  S8: pure install checks — exe build vs repo
                             HEAD, configured vs emitted CellSize, app mask vs
                             Ext3 mirror, addon version; best-effort file reads
    Ui/                      UI shell (classic v3): DesignTokens (system
                             variable-font chain — Geist/UiFonts removed),
                             Layout (measured VertStack/WrapFlow/GridPanel/
                             BentoSplit + UiMeasure), UiPrimitives (GlassCard,
                             KvRow, pills/tiles/chips, UiClickable),
                             ClassBrowserView (S6: selector + mode filter +
                             knobs + virtualized rows), AbilityExplorer /
                             AbilityInspector (VirtualAbilityList + legacy
                             Explorer, kept as test seams),
                             ConsoleHome (S7: owner-drawn read-only status
                             console + rolling logs + top bar),
                             ConsolePresets (S7: five named bundles over the
                             same 14 toggles + ConsoleRole inference),
                             Pages (StackPage hosts for the popup tabs),
                             SettingsPages (Configuration/Diagnostics +
                             v3.3.0 SoloBandEditor Minor/Major/Immunity rows;
                             v3.3.0 Mode/Target preset rows),
                             UiShellValidation (honest structural smoke)
    UiControls.cs            restored classic primitives: RoundedCard (hero),
                             GradientCanvas, LinkLamp, StripView,
                             ClassBadge, WheelSafeNumeric
    MainForm.cs              borderless classic 660-wide fixed main window
                             (no AutoScroll on the timer path, no frame ring):
                             hero + Rotation/Automation toggles + button rows,
                             Advanced…/Abilities… scrim popups (one overlay at
                             a time, console home hidden behind); remembered
                             size only when [Window] Layout=classic3
    Intelligence/CombatContext.cs  tri-state context from the frame; HpSource
                             {Plain, Curve, Unknown} + HpPct/HpPctUpper with
                             precedence plain > curve > unknown
    Intelligence/SpellIconCache.cs  opt-in skill icons from the WoW CDN,
                             cached in assets/icons, offline placeholder
    CombatGate.cs            2026-09-30 fail-closed out-of-combat predicate
                             (engine gate + scheduler input + auto-target/
                             auto-interact movement guards)
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
    ThisAssembly.Gen.cs      build stamp (git HEAD + date; v3.5.2: shown on the
                             install doctor card, not the title bar)
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
    RacialTogglesTests.cs    v3.x scope=Racial rows: scope carried, multi-id,
                             purpose routing, gap-fill recognition, CC/mobility
                             manual, passives absent, catalog emission
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
    ClassBrowserViewTests.cs  S6 STA window: selectors/modes, warm open
                             (&lt; 150 ms) + owner-drawn paint smoke
    SpellIconCacheTests.cs   download-once + disk cache, offline degradation,
                             hostile slug rejection, official-slug single
                             request
    fixtures/defensive-warrior-urgency.jsonl  canonical defensive session
    TtkEstimatorTests.cs     v3.2.0 estimator: convergence, reset on switch/
                             heal/no-target/sustained-unknown, coarse-band
                             staircase, noise, clamp, determinism; v3.6
                             provisional paths, AgeSec and fast-pack latch
    TtkPolicyTests.cs        v3.2.0 T1-T4 gate matrix (waste hold, two-uses,
                             execute, Solo T4 + emergency override, unknown
                             fail-open, kill-switch); v3.6 provisional/grace
                             hold, kill-secure, execute carve-out, tiered
                             defensive lookup, Fallback mode; v3.8 BuffNeed +
                             WarmupHoldHolds (young unknown major, carve-outs,
                             history release, provider wiring)
    TtkCurationTests.cs      raw-JSON TTK field schema conformance (defaults,
                             ranges, execute pairing, v3.6 killSecure)
    TtkEstimatorHistoryTests.cs  v3.7 fake-clock history: kill filter, window
                             cap/prune/idle-clear, nearest-rank quantile,
                             below-MinKills fail-open identity, blend weights,
                             history-only provisional
    TtkPolicyHistoryTests.cs v3.7 NeedAdaptive, ttk-hist hold/use, invalid-live
                             restriction, carve-outs, consumable Burst release,
                             [TimeToKill] parse/clamp, replay hk/hs reproduce
    TtkReplayTests.cs        v3.2.0 recorded-series estimator rebuild (in-memory
                             + the checked-in ttk fixture, 0 mismatches)
    fixtures/ttk-warrior-burst.jsonl  canonical v3.2.0/v3.8 TTK recording (0
                             mismatches; warmup hold / trash hold / boss fire)
    fixtures/ttk-trash-pack.jsonl     v3.6 dying-trash recording (6 mobs each
                             <5 s; 0 mismatches; latch / grace hold)
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
  surfaces the state in the hero status row (LinkLamp + state label), the
  console-home status block and the tray icon; the frame draws no border ring
  (the former 2px red/green `OnPaint` ring was removed). The invisible resize
  grip is unaffected. The client size is remembered in `[Window] Width=`
  / `Height=`, written on resize-end and close and restored on start
  (clamped to the working area; `CollapsedWantHeight` is the 960 default).
- **Determinism.** Policy and scheduler are pure functions of the decoded
  frame, the send history and settings — fake-clock testable, no clock/OS
  reads inside. Telemetry replay recomputes the decision layer with 0
  mismatches; policy verdicts are recorded with reasons.
- **TTK knowledge is advisory; v3.6 guards dying trash without failing open.**
  The estimator is pure and fake-clock (built only from decoded fields), **no
  wire field and no Lua change** — it reuses the target HP band already on the
  wire. v3.2.0 skipped every T1–T4 gate on an invalid estimate (fast trash was
  never valid, so the guard never applied); v3.6 adds provisional estimates and
  the fast-pack latch so the waste guard covers fast trash, with the grace hold
  as the **only** fail-open exception (latch + age <4 s, majors only). A long
  fight still fires: the latch clears on age ≥12 s/frac >0.5 or TTK ≥30 s, and
  the kill-secure exception covers a long fight ending. T1 is a hold, never a
  lockout: MaxDps re-suggests next tick. The `[TimeToKill]` kill-switch (labelled
  **"TTK guard"**) disables the estimator and all gates; OFF forces band 15
  (UNKNOWN) and therefore also blinds the execute gate and Burst consumers, not
  just the TTK gates (documented collateral). `[TimeToKill]
  Fallback=ConserveMajors` holds any unknown-TTK major even without a latch.
  Live retail remains OWED (M+ trash→boss no-hold check; dungeon tank Minor
  check).
- **Execution safety before intelligence.** Cast/channel protection is not
  knowledge filtering: it runs in every mode (policy on/off, scheduler
  on/off, v5 frames) and is the *only* thing allowed to hold the main
  rotation besides out-of-range. Off-GCD exemption requires `GcdVerified`
  (curated, or an intrinsically off-GCD category) and never applies to
  gap-closer/movement purposes.
- **Out of combat is fail-closed (2026-09-30).** The companion holds out of
  combat unless the app's OOC toggle is on (`CombatOnly=false`) **and** the
  bridge echoed the Ext3 OOC mirror bit **and** either the frame is Active with
  a target or the state is `NeedTarget`/`NeedInteract` (the auto-target /
  auto-interact asks, which have no target yet and are gated per-toggle); in
  combat always passes. A disabled config gate (`CombatOnly=0`) or an
  old/unsynced 40-cell addon is never permission on its own. One shared pure
  predicate (`CombatGate`) drives the engine gate, the scheduler gate
  (`ScheduleReason.OutOfCombat`) and the auto-target/auto-interact movement
  guards, so no path can disagree. The scheduler gate is a **required** input
  (`ScheduleInput.OutOfCombatPermitted`, no null default), so a caller can never
  omit it and fail open.
- **Failure recovery.** A failed action (no GCD after a press, or the same
  stroke sent too often in the window) is suppressed with an escalating
  window (situational slots 1.5 s → 3 s → 6 s → 10 s, reset by a successful
  send), never retried forever, and never blocks the rest of the plan. The
  Main rotation is the Arms-stuck exception: its ladder is capped at
  1.5/3/3/3 s, any Main send clears every Main ladder, and a target/combat
  edge drops the Main failure memory (situational slots stay held); a >6 s
  silence restarts the ladder. Failure suppression lives in `_failedUntil`,
  is otherwise never cleared by state transitions, and a pending non-main
  stroke is demoted behind fresh actions while its confirmation is pending.
  The engine's per-slot OS
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
  status flags; TTK guard OFF forces target band 15, which also blinds the
  execute + Burst consumers (v3.6, documented collateral). No layout/version
  change.
- **Toggle authority is ADDON-WINS (2026-09-30 flip; supersedes v3.5 T5 app-wins).**
  The in-game overlay owns the effective toggle mask. The bridge publishes its
  local 14 toggles in the additive Ext3 cells 40-42 and the companion consumes
  that mirror as the single authority. The companion is deliberately permissive
  (the user turns everything ON in the companion to mean "let the overlay
  drive") and **never auto-pushes** `/mdb mask <hhhh> <e>` at Start or on a
  settings change, so the bridge stays at epoch 0 and its local toggles win; the
  v3.5 push ladder is retained only behind an explicit, call-site-free
  `ToggleSync.RequestExplicitPush`. `ToggleSync.EffectiveMask` is the echo while
  valid and **0** otherwise (no echo = no addon truth = fail-closed, never the
  app mask); `AppMask` is diagnostics-only (`InstallDoctor` drift report).
  `RotationEngine.TryGetToggleMirror` publishes the read-only echo + combat flag.
  `CombatGate` reads the mirror bits directly: with companion `CombatOnly=0`,
  OOC requires the mirror OOC bit + Active + target (`CombatOnly=1` is a hard
  companion-side hold); `MovementGuard` additionally requires the overlay's
  AutoTarget / AutoInteract mirror bits, so an overlay OFF always holds. The
  `[Meta] ConfigVersion` migration turning Mobility / CrowdControl ON once for a
  pre-3.5 `[Spells]` config (fixes RC1/RC2) is unchanged.
- **Addon-side overlay-wins resolution (2026-09-30).** `Toggles.Get` returns the
  stored in-game boolean as the effective value; with the companion not pushing
  (epoch 0) that is the whole truth, and a missing key is ON except OOC, which
  is fail-closed OFF (hold) until the player sets it. If a build ever pushes a
  mask (epoch ~= 0), an explicit local value still wins and the app bit only
  fills in for unset keys — the app mask never vetoes (the old `AppControlled`
  early-return in `SlotAllowed` is gone). `EffectiveMask` republishes all 14
  resolved bits in Ext3 every frame, so the mirror's OOC / AutoTarget /
  AutoInteract bits track the overlay for `CombatGate`; `/mdb <key>` and the
  in-game panel stay writable during a live epoch. `Defaults.Toggles.OOC` is
  intentionally unseeded; the panel hint / OOC tooltip and the
  `AppSettings.LoadWarnings` text both tell the user a fresh install reads OOC
  OFF (hold) until they enable it in the overlay. `MDB.SetEnabled` is the single
  writer for the `Enabled` / sticky `UserPaused` pair (`/mdb on|off|toggle` and
  the Options checkbox route through it), so a UI pause can never diverge the
  two flags. No wire/format change; the Ext3 layout is byte-identical and
  `PROTOCOL_VERSION` stays 5. The semantics-only flip is a real release:
  **3.5.1 "Holdfast"** (the earlier "no bump, ship inside 3.5.0" decision was
  reversed by the 2026-10-01 release spec), so both app and bridge now read
  3.5.1 and `InstallDoctor` stays in agreement. The window title renders
  `Native.DisplayVersion` = `v3.5.1 Holdfast`; the build hash/time moved from
  the title bar into the Advanced Diagnostics install doctor card.
- **Single full-window mask (v3.5.2, UI only).** The Advanced / Class-browser
  scrims are parented to the top-level `Form` (added after the window layout in
  `BuildLayout`) and each tracks `Form.ClientRectangle`, so one opaque layer
  covers the whole window; `Form.Padding(2)` is removed and the
  `GradientCanvas` padding is emptied, with the 24/10/24/12 inset moved onto
  the classic body scroll. The console home already stretched to the full body
  canvas, so it stays the default opaque view over the classic body — no
  background menu or ring peeks at any edge. No wire/format change;
  `PROTOCOL_VERSION` stays 5 and the Ext3 layout is byte-identical.
  **3.7.7 "Gallant"** is the release; the title renders
  `v3.7.7 Gallant` and `InstallDoctor` agrees with the bridge 3.7.7.
- **M-route unified mask (UI only, no wire change).** `MainForm.BuildUnifiedPopup`
  is the single shell every mask must call (opaque scrim + `RoundedCard` +
  `SegmentedTabs`, main-window tokens). `Center` fills `client-24 × client-24`
  (floor 360×280), so Advanced (620) fills height and the Class browser can
  reach its 900 maxWidth on a roomy window. `PageHeader` subtitles and
  `GlassCard` titles wrap (2 lines) instead of ellipsising; `Hint`/Advanced
  checkboxes wrap at a card-inner width. `VirtualAbilityList` row toggles reuse
  the `ToggleSwitch` brass palette and `ConsolePalette.Brass` is aliased to
  `DesignTokens.Accent` (one accent). Dead `RuleSection`, `StatusDot`,
  `SingleToggleRow` and `ShowClassSkills` removed.
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
- **CC reuses the Interrupt slot; the wire stays frozen (v3.4.0, Option A;
  v3.5 casting-only fix).**
  The bridge writes a curated ready+bound crowd-control candidate into wire
  slot 6 only after MaxDps names no usable interrupt (`GetInterruptSpellID`
  first), and since v3.5 the interrupt pins the slot only while the target
  sensor confirms a live cast (`IsInterruptPinReady`; no sensors = fail open);
  otherwise the slot rotates the CC pool, so a live interrupt always wins and
  CC is never emitted while an interrupt is genuinely pending. The CC path
  deliberately skips `IsInterruptReady` (a CC needs no live cast) and is gated
  by the addon CC toggle (`Toggles.IsCC`, now a fail-open `Get("CC")` delegate
  so the Ext3 bit 13 / local key resolve through the one Canon path). The
  slot-6 CC walk skips worldboss/boss-level targets (`MDB.IsBossTarget`,
  pcall, fail-open include). No new slot, no new source bit, no
  `PixelProtocol.cs` / `docs/PROTOCOL.md` change: the id rides the existing
  slot-6 cells, and the companion routes it through the existing
  `CrowdControlVetoes.Evaluate` call-site. `CrowdControlGate` (default OFF),
  the Never/Manual user vetoes, curated auto-eligibility, target/range/opener
  checks and the same-DR anti-chain memory all still govern. Since v3.5 the CC
  provider holds every **Stun/Silence** row unless `TargetCasting == Yes`
  (Q1 casting-only — these rows are interrupt substitutes, never blind stuns).
  v3.5 made Storm Bolt (107570) auto-eligible; v3.6 extends the same rule to
  every class's qualifying single-target stun/silence (Kidney Shot 408, Mighty
  Bash 5211, Intimidation 19577, Solar Beam 78675, Silence 15487) and demotes
  Shockwave (46968) to Suggest (no enemy-count signal, so AoE stun safety is
  unprovable, as with Leg Sweep). MaxDps-owned/curated-status Incomplete rows
  ride the delegated interrupt path (itself casting-gated by
  `InterruptVetoes`), preserving MaxDps authority over blind stuns.
- **Self-sustain is reset-aware (r2).** Self-heal readiness is re-read every
  tick and never cached; a ready SelfHeal is never stale- or pending-demoted;
  a transient failed press is capped at 1.5 s with no escalating backoff. A
  cooldown wait is surfaced as `SelfHealCoolingDown` (telemetry `cdWait` /
  `lastTriedMs`, verdict `resetHint`) and never changes a verdict or an order.
- `settings.ini` keys mirror `AppSettings` sections 1:1; adding a key means
  updating both plus the README table.
