# Handover — MaxDps-Companion

## Status: v3.4.0 — CC appendix (opt-in, DR-safe, all 13 classes) + surroundings awareness gates (melee/cast/range catalog-driven, LoS fails open) + wired UI publish (solo sliders, bridge-health banner, cast-audit grid) + CC slot-6 candidate source (Option A, wire frozen) (660 xunit tests + 186 Lua harness checks, live validation owed)

## 2026-09-30 OVERLAY REGISTERCLICKS FIX (this change)

GOAL: stop `BuildOverlay` aborting so `Overlay` no longer stays nil (it
re-ran and errored on every `ADDON_LOADED`/`PLAYER_LOGIN`).

WHAT CHANGED: `addon/MaxDpsBridge/Panel.lua` line 545 called
`O:RegisterForClicks(...)` on a plain `Frame` — a Button-only method — which
aborted `BuildOverlay` before `Overlay = O`. Deleted that call; the overlay's
right-click reset moved from `OnClick` to `OnMouseUp` (a Frame never receives
`OnClick` without RegisterForClicks), with a comment explaining Frame vs Button.
Frame type, `SetMovable`, `ClampedToScreen`, `RegisterForDrag("LeftButton")`,
the `OnDragStart`/`OnDragStop` position save, the toggle Buttons and minimap
Button, the pixel bridge (Bridge/Bars/Reader/Toggles), and the wire are all
untouched — no PROTOCOL change.

VALIDATED (this machine): `luac -p addon/MaxDpsBridge/Panel.lua` exit 0.
LIVE OWED (retail 12.1): `/reload`; `/mdb overlay on` shows with no error; drag
persists; right-click resets; toggle Buttons blank/restore slots.

## v3.4.0 CC SLOT-6 CANDIDATE SOURCE — Option A (this change)

GOAL: give the companion's opt-in CC appendix a real in-game candidate path
without touching the frozen wire. `Catalog.lua` now emits a per-spec `cc` list
(from the auto-eligible `CrowdControlCatalog` rows), and the bridge reuses the
**Interrupt slot (wire 6)** as the CC source: MaxDps's own flagged + ready +
live-cast interrupt wins; only when it names none does a curated ready+bound CC
candidate fill the same slot. No `PixelProtocol.cs` / `KeySender.cs` /
`docs/PROTOCOL.md` change (version nibble stays 5; slot-6 cells already exist).

WHAT WAS BUILT:
- `Knowledge/AbilityCatalog.cs`: `CrowdControlGapFill(className, specName)` —
  the auto-eligible curated CC ids (curated preference order) the generator
  emits as `cc`. MaxDps-owned stuns (Storm Bolt, Shockwave, …) are
  `AutoEligible=false` and never emitted, so MaxDps authority is preserved.
- `Knowledge/CatalogLuaGenerator.cs`: emits `cc = { … }` per spec.
  `addon/MaxDpsBridge/Catalog.lua` + `tests/…/fixtures/Catalog.lua` regenerated
  via `--gen-catalog` (not hand-edited).
- `addon/MaxDpsBridge/Reader.lua`: `ExtraCandidates` accepts `"cc"`; new
  `MDB.GetCrowdControlCandidate` = `ExtraCandidates("cc", 1)[1]`, gated by the
  addon CC toggle (`MDB.Toggles.IsCC`, restrict-only, missing = ON), via the
  same ready+bound+`ActiveVariant` walk as the other extras. `MDB.FrameKey`
  includes `Extra.cc` so the dirty-flag cache invalidates on a catalog change.
- `addon/MaxDpsBridge/Bridge.lua` slot 6: interrupt-first
  (`GetInterruptSpellID` + `IsInterruptReady`); on no interrupt, when
  `Allowed(6)` (Interrupt toggle OFF-wins) a CC candidate is written through
  `WriteSlot(6, CcId)` with the ordinary `IsSpellReady` gate — deliberately
  **skipping `IsInterruptReady`** (a CC needs no live cast). Order is
  interrupt-first, so CC is never emitted while a live interrupt is pending.
- Companion authority unchanged: the CC id rides slot 6 but the app routes it
  through the existing one-line `CrowdControlVetoes.Evaluate` call-site, so
  `CrowdControlGate` (default OFF), the `Never/Manual` absolute user vetoes,
  the curated auto-eligible membership, the target/range/opener checks and the
  same-DR anti-chain memory all still govern. No new provider branch was needed.
- Tests: `CrowdControlTests.cs` gains slot-6 reuse tests (fires on the reused
  slot; bypasses interrupt vetoes when no cast is pending while a real
  interrupt id stays held; MaxDps-owned stun never offered; `CrowdControlGapFill`
  auto-eligible-only for all 13 classes). The suite is pinned to a
  `DisableParallelization` collection because `AppSettings.Load` reconfigures
  the process-global `CrowdControlGate` (pre-existing isolation race the new
  cases exposed).

SAFETY (all kept): no-target hold; AoE-CC-never-opener (provider-side, bridge
cannot see combat, so the provider stays the gate); same-DR anti-chain memory;
range-No unavailable; `CastHoldReason`; never emit CC while a live interrupt is
pending (bridge interrupt-first order); companion `CrowdControlGate` +
Never/Manual absolute.

VALIDATED (this machine): `dotnet build app\MaxDpsCompanion\MaxDpsCompanion.csproj
-c Release` 0 warnings / 0 errors; `dotnet test -c Release` (from
`tests\MaxDpsCompanion.Tests`) **660/660**; `lua tests/secret_harness.lua`
**186/186**; `luac -p` 8/8 bridge files clean; `pwsh tools/ability_audit.ps1`
exit 0 — Violations 0 / Warnings 0 / Missing 0 / Stale 0, committed addon
`Catalog.lua` matches the generated output. Not committed.

LIVE OWED (retail 12.1): with the companion `[CrowdControl] Enabled=1` and the
addon CC toggle ON, a spec with a curated CC row and no usable interrupt shows
the CC candidate in the interrupt slot and fires it on a confirmed in-range
target; an available interrupt always wins the slot; DR categories do not
auto-chain; turn the addon CC toggle OFF (and/or the companion gate OFF) and the
slot blanks/holds.

## v3.3.0 STREAM 4 — guardrails + cross-stream merge (this change)

FROZEN-CONTRACT AUDIT (no code edit, evidence only):
- `docs/PROTOCOL.md` diff is **documentation-additive only** (v3.3.0 Solo
  ladder + 13-toggle semantics). `PixelProtocol.cs` and `KeySender.cs` are
  **unmodified** (`git diff` empty) — nibble 5 and the 40-cell Ext2 layout are
  untouched. No non-additive protocol change.
- `Knowledge/CandidateProviders.cs` is the one double-owned file: Stream 1's
  Solo latch hunk (`SoloBandLatch.Latched`, ~line 350, Defensive provider) and
  Stream 3's preset/offensive hunks (~142, ~188) **coexist without conflict**;
  both are retained. No conflict markers anywhere in tracked files.
- Stream ownership check: every modified/added path falls inside the declared
  Stream 1/2/3 file sets. `settings.ini` (+ Escalation/Minor/Major/Immunity)
  is the only shared config surface and is additive.

OWED CROSS-STREAM WIRING (documented only — the target files are owned by other
streams / MainForm + RotationEngine are outside this task's edit scope):
1. Wire `Scheduler/BridgeHealth` advisor into `RotationEngine`/`MainForm`
   (stale-addon / skew guidance); class + advisor exist, not yet consumed.
2. Host `Ui/CastAuditView`, `Ui/SettingsPages` SoloBandEditor and
   `Ui/SemanticBanner` in `MainForm` (all three are built + tested, not
   mounted on any page yet).
3. Call `Intelligence/SpellIconCache.Invalidate()` from catalog regen so
   regenerated `Catalog.lua` slugs drop stale cached icons.
4. `AppSettings.PollIntervalMs` default: set to **33** (Stream 3-owned;
   `settings.ini` already ships `PollIntervalMs=33`). Recommended, not yet
   asserted as the code default.

FULL OFFLINE BAR (this machine, Stream 4 pass): `dotnet build -c Release` 0
warnings / 0 errors; `dotnet test -c Release` **613/613** (one earlier 612/613
run flaked the load-sensitive `ClassicUi_PopupOpen_Fast_SingleBoundedFade`
timing test; it passes standalone and on a quiet re-run — pre-existing, not
stream-caused); `lua tests/secret_harness.lua` **186/186**; `luac -p` 8/8 bridge
files clean; `pwsh tools/ability_audit.ps1` exit 0 — Violations 0 / Warnings 0
/ Missing 0 / Stale 0, committed Catalog.lua matches the generated output. Not
committed.

## v3.3.0 IN-GAME 13-TOGGLE UI (this change)

GOAL: expose the companion hero's 13 toggles (8 Spells + Solo / Out-of-combat /
Auto-target / Auto-interact / TTK) in-game through `/mdb` and the Options panel,
so the player can restrict what the bridge encodes without touching the
companion. The addon only RESTRICTS: `effective = companion AND addon`; an
addon OFF always wins. A missing DB key reads as ON. No wire change (version
nibble stays 5); `PixelProtocol.cs` untouched.

WHAT WAS BUILT:
- New `addon/MaxDpsBridge/Toggles.lua` — pure logic (harness-loadable, no
  frames): Get/Set/Flip/Keys/Label/Snapshot over `MaxDpsBridgeDB.Toggles`
  (13 booleans, default true) and `SlotAllowed(slot, ctx)` as the single gate
  `Bridge.Update` consults before `WriteSlot`. Rules: per-slot OFF; OOC OFF
  blanks slots 1-8 on a strict known out-of-combat; Solo OFF blanks
  Defensive(3)/SelfHeal(8) while known ungrouped except emergency HP <= 35
  (unknown HP/group fails open); TTK OFF forces target band 15;
  AutoTarget/AutoInteract silence states 3/4 -> 0. Every game API is
  pcall-contained and the whole gate fails open. Denials recorded in
  `MDB._LastBlank` for `/mdb why heal`.
- New `addon/MaxDpsBridge/Panel.lua` — plain-frame settings panel
  (`Settings.RegisterCanvasLayoutCategory`, `InterfaceOptions_AddCategory`
  fallback) with the 13 checkboxes + All on/off + overlay controls; draggable
  overlay (`/mdb overlay`) is a plain Frame (no `RegisterForClicks`) whose
  right-click reset is `OnMouseUp`, plus a pixel-strip overlap guard; optional
  minimap launcher. Nothing here runs on the pixel path.
- `Bridge.lua` v3.3.0: per-tick toggle context from pcall'd game APIs; deep
  `MergeDefaults` (old SavedVariables stay valid; nested Toggles/Ui tables are
  copied, never aliased); `reset` deep-restores; `/mdb toggles|overlay|
  <key> on|off|all on|off|why heal`; `status` gains
  `toggles=N/13 ON; OFF: ...`; TTK OFF forces `WriteTarget` band 15.
- `Options.lua`: thin "Toggles..." entry in both the StdUi and fallback paths.
- `MaxDpsBridge.toc` + root and addon `VERSION.txt` -> 3.3.0; load order
  Catalog, Keymap, Bars, Reader, Toggles, Bridge, Options, Panel.
- `tests/secret_harness.lua`: T21 in-game toggle suite (33 checks) — nil ctx,
  secret HP, each OFF effect, emergency exception, TTK band forcing.
- Docs: `docs/PROTOCOL.md` (v3.3.0 toggle semantics, no wire change),
  `ARCHITECTURE.md` (file map + bridge gate stage + invariant) — this pass.

VALIDATED (this machine, v3.3.0 toggles + Solo ladder): `dotnet build -c
Release` 0 warnings / 0 errors; `dotnet test -c Release` 613/613 (Stream 4
re-run on the merged tree; 585 at the stream snapshots);
`lua tests/secret_harness.lua` 186/186; `luac -p` clean (8 bridge files);
`pwsh tools/ability_audit.ps1` exit 0 — Violations 0 / Warnings 0 / Missing 0
/ Stale 0, committed addon Catalog.lua matches the generated output.
LIVE OWED (retail 12.1): `/reload`; open Esc > Options > MaxDps Bridge (or
`/mdb toggles`) and flip each toggle; confirm an OFF slot blanks in-game and
returns when ON; `/mdb overlay on` drag + strip-overlap guard; `/mdb why heal`
reason; AutoTarget/AutoInteract state; TTK OFF forces the unknown band; all
persist across `/reload`.

## v3.3.0 SOLO SURVIVAL LADDER (same release)

GOAL: the Solo rotation survives on heals AND mitigation/shields/absorbs, and
escalates bigger CDs as HP drops so health never reaches 0%. Catalog =
core+talent superset for all 13 classes; unchosen talents are ignored at
runtime (bridge offers only a ready+bound spell the player knows; the policy
never invents a candidate). No wire change.

BANDS (Solo only, valid HP required):
- <=75% Minor absorb/shield (SoloMinorHpPct, new; defensiveMinor list)
- <=65% heal (existing SelfSustainHpPct; SelfHeal slot, unchanged)
- <=50% Major (SoloMajorHpPct, new; defensiveMajor list, new)
- <=30% immunity/emergency (SoloImmunityHpPct, new; immunity list, new;
  EmergencyHpPct 35 stays for heals/majors/generic path)
Solo HP-substitution: an in-band gap-fill defensive bypasses the White-urgency
hold; out-of-band holds with "solo: HP x% above <band> band N%"; immunity
additionally requires no active immunity; MaxDps-flagged candidates and every
group verdict keep the classic urgency path byte-identical. Kept: T4
dying-target hold, overheal guard ceil(healPct*0.6), immunity-active hold,
DefensiveEscalateHpPct sequencing, per-ability ON/OFF veto, cast/channel hold.

DATA (verified catalogued/vendor ids only; no invented ids):
- MAGE Arcane/Fire/Frost selfHeal NEW: 235450 Prismatic / 235313 Blazing /
  11426 Ice Barrier (spec barriers as sustain); 55342 Mirror Image defensive
  membership widened Arcane-only -> all 3 mage specs.
- PRIEST selfHeal += 373481 Power Word: Life (all 3) + 15286 Vampiric Embrace
  (Shadow; vendor Defensive bucket, curated SelfHeal purpose).
- SHAMAN selfHeal += 5394 Healing Stream Totem (all 3).
- EVOKER Preservation selfHeal += 363534 Rewind.
- DH Havoc/Devourer selfHeal still NONE (no verified solo self-heal id; owed).
- Immunity ladder: 45438 Ice Block (mage all), 642 Divine Shield (paladin
  all), 31224 Cloak of Shadows (rogue all), 196555 Netherwalk (DH Havoc AND
  Devourer via vendor membership) flow through the new ImmunityGapFill.
- Warlock Dark Pact 108416 / Healthstone 6262 / Unending Resolve 104773,
  Shaman Astral Shift 108271, Evoker Obsidian 363916 / Renewing Blaze 374348
  were already covered (no change).
- OWED (do NOT add until verified against live 12.1 DB2): 1244090 Temporal
  Realignment, Shadow Mend id, Soul Immolation id, 186265 Aspect of the Turtle.

CONTRACTS (additive): PolicyOptions SoloEscalation=true + SoloMinorHpPct=75 +
SoloMajorHpPct=50 + SoloImmunityHpPct=30 with ValidateSoloBands
(immunity<major<minor else defaults); AppSettings [Solo] EscalationEnabled=1,
MinorHpPct 75 (40-99), MajorHpPct 50 (20-90), ImmunityHpPct 30 (5-60);
settings.ini same keys; telemetry sesc/smin/smaj/simm (omitted when default;
old replays unaffected); AbilityCatalog DefensiveGapFillMajor +
ImmunityGapFill + CatalogVersion 3->4; CatalogLuaGenerator emits
defensiveMajor + immunity; Catalog.lua + fixtures/Catalog.lua regenerated;
Reader.lua GetDefensiveCandidate Solo ladder block (bands armed by Bridge.lua
per-tick Solo-ON + known-ungrouped; group frames disarm -> pre-3.3.0
Red/Orange path); Toggles/Panel Solo semantics unchanged.
Docs: HANDOVER (this section) + ARCHITECTURE (ladder/provider lines) +
docs/PROTOCOL.md (v3.3.0 ladder note, no wire change) in the same pass.

VALIDATED (this machine, v3.3.0): dotnet build -c Release 0 warnings / 0
errors; dotnet test 613/613 (merged tree, Stream 4); secret_harness 186/186; luac -p clean;
tools/ability_audit.ps1 exit 0 — Violations 0 / Warnings 0 / Missing 0 /
Stale 0, committed addon Catalog.lua matches the generated output.
LIVE OWED (retail 12.1): each band fires at the right HP in Solo; nothing new
fires grouped; talent-unchosen spells ignored; owed ids verified live.

## v3.2.0 TTK INTELLIGENCE (T-A + T-B, previous)

GOAL: stop wasting cooldowns. A pure per-target time-to-kill (TTK) estimate now
gates offensive cooldowns and Solo defensive cooldowns: hold a major fired into
a target that dies before the CD pays off, fire early (bypass the pairing hold)
when the fight is long enough for two full uses or the target is in execute
range, and save a non-emergency Solo defensive when the target dies imminently.
No wire change and no Lua behaviour change: the estimator reuses the target HP
band already on the wire.

T-A — estimator + gates + plumbing (merge `f842a29`; A1–A6 `aa166cc`,
`cd9dda3`, `20f04ca`, `3d01da2`, `31ab1c1`):
- `Knowledge/TtkEstimator.cs` (new, pure/fake-clock): `frac = (band+0.5)/15`;
  RESET on no target, >10 s continuous unknown, or a >0.12 upward `frac` jump.
  FEED measures the decline from the last **FED anchor** (`inst =
  (feedFrac-frac)/feedDt`, so flat ticks accumulate time instead of biasing the
  rate upward) into an EWMA with a 3 s time constant; the **first** fed sample
  **seeds** `ewma` directly (no zero-bias warm-up). VALID at ≥2 fed samples,
  ≥2.5 s span and `ewma ≥ 0.004`; `TtkSec = clamp(frac/ewma, 0, 300)`. The
  anchor+seed method is the implemented §3.1 (amended below).
- `Knowledge/TtkPolicy.cs`: `MinTtkSec` usage defaults + offensives forwarding.
  `Knowledge/AbilityModel.cs` / `AbilityCatalog.cs`: parse `minTtkSec` /
  `executeBelowPct` / `executeFavored`; `AbilityIntelligence.cs` inspector line.
- `Knowledge/CandidateProviders.cs` gates: **T1** waste guard (Offensive, all
  sources; valid TTK < minTtk ⇒ Hold `"target ~Xs to die; saving <name> (needs
  Ns)"`), **T2** two-uses (valid TTK ≥ `2·cd+dur`, absent cd skips, ⇒ bypass
  the pairing hold), **T3** execute (`executeFavored` + target HP ≤
  `executeBelowPct` ⇒ bypass the pairing hold), **T4** dying-target (Defensive,
  **Solo only**, not emergency, valid TTK < 6 s ⇒ Hold). Unknown TTK skips all
  gates (fail open).
- `RotationEngine.cs` owns one estimator, feeds it once per real frame before
  the policy, and attaches the result via `CombatContext.WithTtk` (no scheduler
  signature change). `[TimeToKill] Enabled=1` kill-switch (AppSettings +
  settings.ini + one Modes-card toggle row in `MainForm.cs`). Telemetry gains
  additive `ttk`/`thp`/`ttkMs`; `ReplayRunner` rebuilds the estimator from the
  recorded `(ttkMs, hasTarget, thp)` series. New fixture
  `fixtures/ttk-warrior-burst.jsonl` replays **18 policy verdicts / 0
  mismatches**; the six legacy fixtures still replay 0 mismatches.
T-B — curation + validation (merge `c33cbf4`; B1–B4 `2dc1b19`):
- `abilities.json` gains **9 explicit `minTtkSec`** values (Army of the Dead 30;
  Summon Infernal/Shadowfiend/Gargoyle/Darkglare/Demonic Tyrant 20; Unholy
  Assault/Primordial Wave 5; Void Metamorphosis 20) and **one execute synergy**
  (Deathmark 35%, Maxroll "Zoldyck Recipe") — sparse, default OFF elsewhere.
- B1 cooldown corrections with sources: Metamorphosis (Havoc) `cdMs` 240000 →
  120000 (Blizzard Midnight pre-expansion notes + Icy Veins 12.1; the wiki's
  stale 3 min was superseded); Void Metamorphosis timer fields removed
  (resource-gated: 50 Soul Fragments / 35 with Soul Glutton, Fury-bar duration).
  Full trail in `docs/research/TTK_CURATION.md`.
- B4 freshness verdict: **no Lua change needed** — the target band is re-read
  every tick with no not-ready cache (`Reader.lua:1323-1341` `HealthPct` fresh
  `pcall` per call; `Reader.lua:1397-1402` `MDB.GetTargetContext` derives the
  0..14 band each call; `Bridge.lua:407-415` `WriteTarget` no cache;
  `Bridge.lua:573`→`740` per-tick `OnUpdate`). Harness therefore stays at 153.

REVIEW OUTCOME: **GO-WITH-FIXES**. The adversarial review of the merged diff
raised two amendments, both committed here as plan-doc changes (they document
what the code already does, no code edit):
1. **§3.1 feed correction** — measure from the last fed anchor and seed the EWMA
   on the first sample (the code's `_feedFrac`/`_feedMs` + `_samples == 0 ? inst
   : α·inst + …`).
2. **§3.3 Execute default** — add Execute 5 to the documented default table
   (`TtkPolicy` already applies it).

VALIDATED (this machine, v3.2.0):
- `dotnet test -c Release`: **571/571** (v3.1.0 was 521). `pwsh -File build.ps1`
  build 0 warnings / 0 errors.
- `lua tests/secret_harness.lua`: **153/153**.
- `tools/ability_audit.ps1`: exit 0 — Violations 0 / Warnings 0 / Missing 0 /
  Stale 0.
- **7 replay fixtures 0 mismatches**: solo 9, defensive 16, offensive-interrupt
  7, solo-hidden-hp 6, cooldown-reset 8, offensive-gapfill, ttk-warrior-burst
  18 policy verdicts.
- `--bench-scheduler`: **UNCHANGED** sends=1620 sha256=`b71a999d5e46570e`.
- `--ui-smoke-test` **PASS**.

LIVE OWED (retail 12.1 — not run here):
- **Trash-hold:** a major offensive is held into a short-lived target (telemetry
  `waiting on TTK` / `target ~Xs to die`), then fires on the boss.
- **Boss-fire:** the same CD fires once the TTK estimate supports it.
- **Execute:** Deathmark fires at/below 35% HP.
- **Solo-T4:** a non-emergency defensive is saved when the last mob is < 6 s from
  death; emergency HP still overrides.
- **Replay:** record a live session with `[TimeToKill] Enabled=1`, export, and
  `--replay` 0 mismatches. `/reload` only (addon pre-installed by the router).

HONEST LIMITS:
- **No live retail run.** Every T1–T4 behaviour is offline-proven only.
- **Coarse-band anchor behaviour.** The estimator's `frac` is the midpoint of a
  ~6.67%-wide wire band, and the execute gate uses the band's integer percent
  (≈ band·6.67, not the midpoint); both inherit the ±3.3% band resolution, so a
  threshold can be crossed one band early/late and the estimate's precision is
  bounded by the wire band, not by the EWMA.
- **Most vendor offensives lack a curated cooldown,** so T2's two-uses rule
  cannot evaluate for them and the T1 waste guard is the only gate that applies.
- **Void Metamorphosis is unmodeled:** it is resource-gated (Soul Fragments), so
  the linear TTK estimator cannot predict its availability; its curated timer
  fields were removed and it carries only a flat `minTtkSec`.
- **Band round-trip (review):** the recorded `thp` and `TtkEstimator
  .BandFromPercent` reproduce all 0..14 protocol bands exactly (`band→pct =
  round(band·100/15)`, `pct→band = round(pct·15/100)`), but the reconstructed
  percent is the band's integer value while TTK uses the band midpoint, so the
  two views of "current HP" differ by up to ~3.3%; replay is exact because it
  feeds the same recorded series.

## Status (previous): v3.1.0 — offensive/defensive gap-fill, reset-aware self-sustain, dynamic UI scaling (521 xunit tests + 153 Lua harness checks, live validation owed)

## v3.1.0 — R1 gap-fill + R2 reset-aware sustain + popup budget (this change)

GOAL: cooldowns the user toggled ON must fire even when MaxDps does not
surface them (offensive gap-fill, defensive Orange tier), and Solo self-sustain
must survive cooldowns / reset procs instead of being demoted as "stale".

R1 — offensive gap-fill + defensive Orange tier (branch `v3/r1-offgap`,
merged `254dd04`):
- `Knowledge/abilities.json` gains curated per-spec `offensive` lists (1-4 true
  burst CDs, shared burst first); `CatalogLuaGenerator` emits `offensive` and
  `defensiveMinor`; `Catalog.lua` regenerated (+fixture copy).
- `Reader.lua` `MDB.GetOffensiveCandidate` returns MaxDps's flagged+bound
  offensive first, else the first ready+bound curated entry, all inside MaxDps's
  `enableCooldowns` switch. The defensive gap-fill is extended from Red-only to
  the Orange tier via `defensiveMinor` (short-CD Minor/None mitigation only).
- `AbilityCatalog` `SpecExtras.Offensive` + `OffensiveGapFill` /
  `IsOffensiveGapFill` + `DefensiveGapFillMinor`; `CandidateProviders`
  `OffensiveCandidateProvider` derives `CompanionGapFill` by **id membership**
  (no wire source bit exists — decode is frozen); combat / Solo gate.
  `PolicyEvaluator` registry enforcement extended to the companion-only
  offensive source. `Telemetry` policy block records `cls`/`spec` (additive) so
  replay re-derives the source.
- Tests: `OffensiveGapFillTests.cs`, `OffensiveGapFillReplayTests.cs`, fixture
  `offensive-gapfill-warrior.jsonl`; 5 new harness checks; `docs/KNOWLEDGE.md`,
  `docs/PROTOCOL.md`, `ARCHITECTURE.md`.

R2 — cooldown/reset-aware self-sustain (branch `v3/r2-sustain-cd`, merged
`64a5c4b`):
- Bridge re-reads self-heal readiness EVERY tick (never cached; the keybind
  memo is dropped on bar/binding/talent/spec/`SPELL_UPDATE_COOLDOWN`), so a
  dynamic reset that makes the same spell ready again is offered next tick.
- Scheduler: a ready SelfHeal is never stale-demoted (unchanged slot content
  after a cooldown is a new opportunity, not a stuck suggestion) and never
  pending-confirm-demoted; a transient failed heal press is suppressed for at
  most 1.5 s with no escalating backoff (`NoteFailure`); permanent exclusions
  (policy OFF / unbound / unknown) keep existing behaviour. When HP is in the
  sustain window and no heal is ready, the plan holds with the distinct reason
  `SelfHealCoolingDown` ("waiting for self-heal cooldown").
- Telemetry: policy-level `cdWait` / `lastTriedMs`, verdict `cdWait` /
  `lastTriedMs` / `resetHint` (additive, informational; replay does not compare
  them). `resetHint` is an optional curated free-form string, never read by a
  decision rule.
- Tests: `SelfSustainCooldownTests.cs`, `SoloCooldownResetReplayTests.cs`,
  fixture `solo-cooldown-reset-warrior.jsonl`; harness checks;
  `docs/KNOWLEDGE.md`, `docs/TELEMETRY.md`.

POPUP-BUDGET FIX (`32d800d`): `ClassicUi_PopupOpen_Fast_SingleBoundedFade`
warm-run wall budget relaxed 150 ms → 500 ms because dev/CI boxes spike
300–800 ms under load. The D6 contract is unchanged and still asserted:
exactly one bounded fade ≤ 120 ms, skipped while the engine runs, UI thread
never blocked.

MERGE / CONFLICT NOTE: R1 and R2 were merged onto `v3/base` (`254dd04`,
`64a5c4b`) over overlapping surfaces — generated `Catalog.lua` (+fixture),
`CatalogLuaGenerator`, `CandidateProviders`, `TelemetryEvent`,
`ActionSchedulerPolicyTests`, `ARCHITECTURE.md`. They were resolved to the
combined behaviour: no conflict markers remain in tracked files, the committed
`Catalog.lua` still matches the generator output (audit drift check passes),
and the scheduler bench pin is unchanged.

VALIDATED (this machine, v3.1.0):
- `dotnet test -c Release`: **521/521** (v3.0.0 was 490); build 0 warnings /
  0 errors.
- `lua tests/secret_harness.lua`: **153/153** (was 143).
- `tools/ability_audit.ps1`: exit 0 — Violations 0 / Warnings 0 / Missing 0 /
  Stale 0.
- 6 replay fixtures **0 mismatches**: solo 9, defensive 16, offensive-interrupt
  7, solo-hidden-hp 6, cooldown-reset 8, offensive-gapfill.
- `--bench-scheduler`: **UNCHANGED** sends=1620 sha256=`b71a999d5e46570e`.
- `--ui-smoke-test` **PASS**.
- `--bench-ui`: mean ~4–20 µs, p95 < 15 µs; **startup is LOAD-DEPENDENT —
  400–3000 ms observed**, so the 500 ms startup target is best-effort on a
  quiet box, not a guaranteed ceiling.

LIVE OWED (retail 12.1 — not run here):
- **L1 (v3):** `/reload` → `/mdb status` shows `ext2=1 hpcurve=on`; `/mdb heal`
  lists keys; window scales across widths, scroll sticks, Launch Game works;
  Warrior Solo <65% in combat → Impending Victory fires once per CD then the
  rotation resumes; a bar holding 34428 (Victory Rush) still fires the 202168
  intent; repeat with a second class (Hunter Exhilaration or Paladin Word of
  Glory); `/mdb hpcurve off` falls back to plain HP; record + export + `--replay`
  0 mismatches.
- **NEW:** a toggled-on offensive CD that MaxDps never surfaces fires in combat;
  an Orange short-CD defensive fires at Orange; majors still wait Red; a reset
  proc re-fires the same self-heal within the 1.5 s cap.

HONEST LIMITS:
- **No live retail run.** Every R1/R2 behaviour above is offline-proven only.
- **Some gap-fill ids rest on vendor rows without curated rows:** 7 ids
  (`5217 / 102543 / 342817 / 114051 / 50334 / 102558 / 200851`) are carried by
  the generated gap-fill lists but lack a curated `abilities.json` row, so their
  live firing and tier classification still need live verification. Listed here
  so they are not mistaken for fully-curated entries.
- **Offensive gap-fill has no wire source bit,** so the companion infers it
  from id membership of the same per-spec list; telemetry records `cls`/`spec`
  so replay re-derives it, but a class/spec change between live and replay
  would be a genuine mismatch (pinned by `OffensiveGapFillReplayTests`).
- The pre-existing HP-curve ToS/policy risk and ±3.3% curve resolution limits
  from v3.0.0 still stand.

## v3.0.0 r1 — offensive gap-fill + defensive Orange tier (`v3/r1-offgap`)

GOAL: cooldowns the user toggled ON must fire even when MaxDps does not
surface them. The Offensive slot was MaxDps-wire-only and the Defensive
gap-fill only fired at Red, so user-enabled offensive CDs and short-CD
defensives never fired.

WHAT WAS BUILT:
- `Knowledge/abilities.json`: curated per-spec `offensive` gap-fill lists
  (1-4 true burst CDs each, shared burst first; all ids verified + name-matched
  against `spell-verification.json`). Added curated Ravager `228920` (the task's
  legacy `152277` is absent from the 12.1 export).
- `AbilityCatalog` `SpecExtras.Offensive` + `OffensiveGapFill` /
  `IsOffensiveGapFill` + `DefensiveGapFillMinor`; `CatalogLuaGenerator` emits
  `offensive` and `defensiveMinor`; `Catalog.lua` regenerated (+fixture copy).
- `Reader.lua`: `MDB.GetOffensiveCandidate` (MaxDps flagged+bound first, else
  the curated list inside `enableCooldowns`); defensive gap-fill extended to
  Orange via `defensiveMinor`.
- `CandidateProviders` offensive gap-fill: `CompanionGapFill` source derived by
  id membership (no wire bit), combat/Solo gate; `PolicyEvaluator` registry
  companion-only enforcement extended. `Telemetry` policy block records
  `cls`/`spec` (additive) so replay re-derives the source.
- Tests: `OffensiveGapFillTests.cs`, `OffensiveGapFillReplayTests.cs`,
  fixture `offensive-gapfill-warrior.jsonl`; 5 new harness checks;
  `docs/KNOWLEDGE.md` + `docs/PROTOCOL.md`.

VALIDATED (this machine): build 0 new warnings; xunit **512/512** with one
pre-existing flaky UI timing test `ClassicUi_PopupOpen_Fast_SingleBoundedFade`
(threshold 150 ms; passes intermittently under load — not touched by this
branch); harness **148/148**; `luac -p` clean; audit exit 0; 6 fixtures
0 mismatches; `--bench-scheduler` **unchanged** sends=1620 sha256=`b71a999d5e46570e`.

LIVE OWED: toggled-on offensive CD (e.g. Warrior Recklessness/Ravager) that
MaxDps never surfaces fires in combat; a short-CD defensive (e.g. Spell
Reflection) fires at Orange; majors still wait for Red; `/reload` + replay.



GOAL: restore a fast v1.3.9-style classic UI, keep all v2.8.1 functionality,
and make Solo self-sustain fire reliably for every class — including when the
game hides HP. Three parallel workstreams (C intel → B bridge → A ui) sharing
the frozen Ext2 contract, merged on `v3/base`.

WHAT WAS BUILT — workstream A (classic UI):
- `app/MaxDpsCompanion/MainForm.cs`, `UiControls.cs`, `Ui/**`,
  `ClassSkillsView.cs`, `Program.cs`, `MaxDpsCompanion.csproj`: fixed 660-wide
  borderless frame (default 660 × min(content, working area), MinimumSize
  520×560, no AutoScroll on any timer path), hero RoundedCard (status/LinkLamp
  + ClassBadge, live "Now:" action/why, StripView, Spells + Modes two-toggle
  rows), Start|Stop|Launch Game and Recalibrate|Abilities…|Advanced…|Open
  Folder rows, Advanced… (Configuration|Diagnostics|Intelligence) and Abilities…
  (Class skills|Explorer) scrim popups built lazily once, `--bench-ui`, 12
  snapshots. Deleted `Ui/AppShell.cs`, `Ui/AmbientBackground.cs`, `Ui/UiFonts.cs`,
  `Ui/Pages.cs` HomePage, `assets/fonts/Geist-*.ttf` + OFL; kept `UiClickable`.
- `tests/…/ClassicUiTests.cs`, `UiShellTests.cs`, `docs/UI.md`.
WHAT WAS BUILT — workstream B (bridge 3.0.0):
- `addon/MaxDpsBridge/{Reader,Bridge,Bars,Keymap,Options}.lua`,
  `MaxDpsBridge.toc`, `VERSION.txt`, `tests/secret_harness.lua`: 40-cell Ext2
  encode (cell 35 HP curve via `SetVertexColor(UnitHealthPercent(...))` with no
  read/compare, cells 36-38 SelfHeal2, cell 39 scoped checksum, cell 33 B
  bit2/bit3 presence, SelfHeal2 range in cell 28 B), spell variants +
  variant-aware binds, first-two-distinct extras selection, `/mdb heal`,
  `/mdb hpcurve on|off`, `ext2=1 hpcurve= sh2=` status, throttled EnsureEngine.
WHAT WAS BUILT — workstream C (intel):
- `PixelProtocol.cs` (+`PixelProtocolExt2Tests.cs`), `ScreenSampler.cs`,
  `BlockLocator.cs`, `ColorLearner.cs`, `RotationEngine.cs`, `Intelligence/**`,
  `Knowledge/**` + `CatalogLuaGenerator.cs`, `Scheduler/**`, `Telemetry/**`,
  `Decision/**`, `addon/MaxDpsBridge/Catalog.lua` (+fixture): 40-cell decode
  with SelfHeal2/curve, `CombatContext.HpSource{HpPct,HpPctUpper}` precedence
  plain > curve > unknown, curve-derived urgency, SelfHeal2 alternate with its
  own range, all-class self-heal extras + aliases (202168↔34428, 19647↔119910),
  telemetry `hpSrc`/`hpUp`/`alt`/`arng`; new tests `CombatContextHpTests.cs`,
  `KnowledgeExtrasTests.cs`, `SelfSustainAlternateTests.cs`, `SoloHiddenHpTests.cs`
  and fixture `fixtures/solo-hidden-hp-warrior.jsonl`; `docs/KNOWLEDGE.md`,
  `docs/TELEMETRY.md`.

VALIDATED (this machine, this session):
- `dotnet test -c Release`: **490/490** (was 446); build 0 warnings / 0 errors.
- `lua tests/secret_harness.lua`: **143/143** (was 94).
- `pwsh tools/ability_audit.ps1`: exit 0 — **Violations 0 / Warnings 0 /
  Missing 0 / Stale 0**.
- 4 replays **0 mismatches**: solo 9, defensive 16, offensive-interrupt 7,
  solo-hidden-hp 6 verdicts.
- `--bench-scheduler`: **UNCHANGED** sends=1620 sha256=`b71a999d5e46570e`.
- `--bench-ui`: mean ~**4.1 µs**, p95 ~**4.6 µs**, startup ~**416 ms** → PASS
  (targets <300 µs / <1 ms / <500 ms).
- `--ui-smoke-test` **PASS**; 12 snapshots (6 pages × {660×920, 520×560}).

LIVE OWED (retail 12.1 — not run here):
- **L0 (can run today on 2.8.1):** Warrior, Solo ON, telemetry ON; take damage
  below 65% in combat; Export → grep `hpKnown:false` / `player HP unknown` to
  confirm or rule out the hidden-HP hypothesis (H1). This chooses whether the
  curve path is even needed for the reported case.
- **L1 (v3):** `/reload` → `/mdb status` shows `ext2=1 hpcurve=on`; `/mdb heal`
  lists keys; window 660 wide, scroll sticks, Launch Game works; Warrior Solo
  <65% in combat → Impending Victory fires once per CD then the rotation
  resumes; a bar holding 34428 (Victory Rush) still fires the 202168 intent;
  repeat with a second class (Hunter Exhilaration or Paladin Word of Glory);
  `/mdb hpcurve off` falls back to plain HP; record + export + `--replay`
  0 mismatches.

HONEST LIMITS:
- **HP-curve ToS/policy risk.** Cell 35 reads the player's HP through the
  vendor's `UnitHealthPercent` *rendering* rather than the hidden `UnitHealth`
  value. It is a colour passthrough (never compared), but it deliberately
  recovers information Blizzard hides in Midnight; whether that is acceptable
  under the addon/client policy is a product decision to make before shipping.
- **Curve resolution ±3.3%.** One band is ~6.67% HP, so a curve-derived HP is
  a coarse band; `HpPctUpper` is the band top, but the exact value is
  unobservable.
- **No live retail run.** The curve is only trusted through the `14..16`
  R+G window because it has no checksum; a live client could render the ramp
  differently (gamma/colour profile) and must be verified.
- Snapshots are design artifacts, never behavioral proof; DPI 100/150/200%
  and the L0/L1 checklist stay OWED.

## Status: v2.8.1 — mouse-activation fix (rail/toggle/chips/tiles), vertical spacing, negative-scroll guard (446 xunit tests + 94 Lua harness checks, live validation owed)

## v2.8.1 MOUSE-ACTIVATION + SPACING FIX (reported by the user)

REPORT: "the menu buttons on the left dont do anything, i can no longer
access my other menus like abilities and configuration" + more vertical
spacing between text segments.

ROOT CAUSE (reproduced with real Win32 mouse messages, then fixed): the rail
items only wired Enter/Space to `NavigationRail.Activate`; a real mouse click
raised `Click` but nothing subscribed it, so mouse navigation never worked
(keyboard and Ctrl+digit did). Second, deeper finding: base-`Control` Click is
unreliable — `WmMouseUp` suppressed it entirely on the ToggleSwitch
(observed `down+up`, no `Click`, via the validation-cancelled path), which
means every custom clickable in the shell could go mouse-dead the same way.

FIX:
- `NavRailItem.OnClick` → `Rail.Activate(this)` (single, explicit activation).
- New `UiClickable` base (Ui/UiPrimitives.cs): disables
  `ControlStyles.StandardClick`, owns mouse-down/up/hover, raises `OnClick`
  only for a press+release inside the control. ToggleSwitch, FilterChip,
  MetricTile, NavRailItem and TitleBarButton derive from it; each keeps its
  Enter/Space path.
- Spacing: `KvRow` 42 → 56 px (value baseline 24), `GlassCard` body gap 8 → 12.
- Defensive: `StackPage.NormalizeScroll` (called from the live status
  refresh) resets a negative auto-scroll offset so focus-driven scrolling can
  never leave a large empty band above the first card.

REGRESSION TESTS (real mouse messages, form shown offscreen): rail click
navigates; toggle click checks. Both failed before the fix with precise
evidence (`events=down+up`, `Expected Abilities / Actual Home`).

VALIDATED: build 0/0; `dotnet test` 446/446 (was 443); `--ui-smoke-test`
PASS; 30 snapshots regenerated (5 pages × 6 widths) in `dist/ui-snapshots`;
published `dist/MaxDpsCompanion.exe` 2.8.1 via a redirected BaseOutputPath
(running instances untouched). Lua harness and engine benchmarks are
untouched by this change (UI-only).

LIVE OWED: close the running older instance, start `dist\MaxDpsCompanion.exe`
2.8.1, confirm rail clicks switch pages, toggles flip, spacing looks right;
then the `docs/TESTING.md` §3d checklist (DPI 100/150/200%).

## Status (previous): v2.8.0 — UI pass: measured layout (Home cards/pills/list fixed), Ethereal-Glass design system, honest structural smoke (443 xunit tests + 94 Lua harness checks, live validation owed)

## v2.8.0 UI PASS — TRUE LAYOUT, GLASS SYSTEM, HONEST SMOKE (this change)

GOAL (user): fix the visible v2.7 defects (empty Home cards, clipped pills) and
deliver the approved premium UI pass (P0–P2): Ethereal Glass x brass, bundled
Geist, all five pages, snapshots at six widths. No commits/PR; engine,
registry and scheduler untouched.

ROOT CAUSES (verified with a new layout dump, not guessed):
1. Home content was built with `Dock` children inside `FlowLayoutPanel`
   containers — unsupported in WinForms; the `Kv` panels collapsed to
   **width 0** so their labels were clipped away. `StatusPill` never enabled
   AutoSize (hard 120px) while painting measured-width text.
2. The v2.7 smoke could never catch either: the headless form is never shown,
   so `Control.Visible` is false for the entire tree and every walk skipped
   everything. Also unvalidated: owner-drawn text fit and card content.
3. NEW defect found while fixing: the Abilities `VirtualAbilityList` was never
   docked (raw Control defaults to 0x0) — the explorer list did not render in
   2.7 at all.

WHAT WAS BUILT:
- Measured layout core (`Ui/Layout.cs`): `IUiMeasured`, `UiMeasure`
  (height-by-width measurement, container-owned wrapping labels that re-measure
  on width change), `VertStack`, `WrapFlow`, `GridPanel`, `BentoSplit`
  (single-column under 980px). No Dock-in-flow anywhere in the shell.
- Primitives rebuilt (Ui/UiPrimitives.cs): double-bezel `GlassCard` (outer
  shell + concentric inner core + top highlight + tracked eyebrow + title
  rule), owner-drawn `KvRow`, `FieldRowPanel`, auto-sizing `StatusPill` /
  `MetricTile` / `FilterChip`, restyled `ToastHost`, `Ui.DrawTracked`.
- Design tokens v2 (`Ui/DesignTokens.cs`): OLED ink (#07090B), layered
  surfaces, hairlines, brass accent, radii (20/14/12/10), type scale + spacing.
- Ambient backdrop (`Ui/AmbientBackground.cs`): brass/teal radial glows + 3%
  grain, cached bitmap painted by the shell itself — deliberately NOT a child
  control (a full-size child painted last in DrawToBitmap covered the page).
- Typography: Geist (Regular/Medium/Bold, SIL OFL) vendored under
  `app/MaxDpsCompanion/assets/fonts` + `tools/Fetch-Geist.ps1` (pinned SHA256),
  loaded from embedded resources via `Ui/UiFonts.cs`; fallback chain
  Segoe UI Variable -> Segoe UI.
- Home recomposed as a responsive bento; Intelligence tiles auto-size and
  coverage is built at startup; rail restyled with hand-drawn 1.4px icons,
  brand mark and compact (icon-only) mode under 880px; explorer rows reserve
  explicit badge/label/toggle space (no overlap) and chip strips wrap.
- Honest smoke (Ui/UiShellValidation.cs): no Visible gate; `IUiTextFit`
  text-fit for owner-drawn primitives; `IUiContentHost` card-content checks
  (rendered content + inside-body bounds); wrapped-label height coverage;
  collapsed-content detection; plus the existing zero-size/overlap/tab checks.
  Every new check was proven to fail on the real defects before the fix.
  `--ui-smoke-test` writes `ui-smoke.txt` + `ui-smoke-layout.txt` and exits 1
  on findings; `--ui-smoke-shell.txt` added for shell-tree diagnostics.
- Snapshots: `--ui-snapshot-page=<page> --ui-snapshot=<path>
  --ui-snapshot-width=<px>` (existing flag set preserved); 30 renders
  (5 pages x 1440/1280/1024/880/480/360) in `dist/ui-snapshots`
  (regenerable; build.ps1 now preserves the folder on republish).

VALIDATED (this machine, this session): build 0 warnings / 0 errors;
`dotnet test` 443/443 (was 440; +Home update render, +wrapping-label re-measure,
+embedded Geist load); `lua tests/secret_harness.lua` 94/94; `--ui-smoke-test`
PASS with the hardened checks; `--bench-scheduler` unchanged sends=1620
(1.82/s) sha256=b71a999d5e46570e; replays solo 9 / defensive 16 /
offensive-interrupt 7, 0 mismatches; `tools/ability_audit.ps1` exit 0
(violations 0 / warnings 0 / missing 0 / stale 0, catalog match); published
`dist/MaxDpsCompanion.exe` 2.8.0 via a redirected BaseOutputPath (the user's
running 2.7 instance from app/bin was never touched); 30 snapshots non-blank.

LIVE OWED (retail 12.1): close the running 2.7 instance and start the 2.8 exe,
then docs/TESTING.md §3d — the 100/150/200% DPI and narrow-window visual spot
checks (snapshots stress widths but are not an honest DPI simulation), plus
the existing protocol/rotation/inspector checklist. NOT validated live here.

HONEST LIMITS: snapshot rendering is a design artifact, never behavioral
proof; DPI scaling of the offscreen render path is not simulated; the
Abilities inspector collapses to a 40% column on very narrow windows (usable
via its scrollbar, not yet a drawer); motion stays restrained by design
(page slide, rail indicator, hover/press physics, toggle spring) and is
suppressed while the engine runs.

## Status (previous): v2.7.0 — intelligence coverage registry + candidate providers + explainability + UI 2.0 (440 xunit tests + 94 Lua harness checks, live validation owed)

## v2.7.0 INTELLIGENCE COVERAGE + PROVIDERS + UI 2.0 (this change)

GOAL (user): every relevant ability must have an explicit intelligence
disposition; every ability the companion can automatically use must have a
deterministic context-aware candidate path; make the pipeline explain itself;
turn the settings form into a coherent modern application. No commits, no PR,
preserve all uncommitted work.

AUDIT FIRST (docs/research/V27_COVERAGE_MATRIX.md): the v2.6 report was
traced, not trusted. Confirmed implemented: protocol v5 + additive urgency,
bridge extras + Red-only defensive gap-fill, tri-state context, five-state
policy, deterministic scheduler, OS-gated execution, telemetry/replay, the
3279-entry registry and its audit. Confirmed registry-only (no runtime
branch): InterruptKind/OffensiveUsage/MobilityKind/OpportunityCost/
HoldForBurst/talent notes/Relations/MaxDpsRelationship. Confirmed the two
Unknown-masquerade gaps: `SlotBuffActive` bool (no tri-state) and the
documented zero-bit TargetInMelee/TargetCasting semantics. Confirmed app-side
candidate providers did not exist (category logic inline in PolicyEvaluator).

WHAT WAS BUILT:
- Registry axes (AbilityModel/AbilityCatalog): IntelligenceOwnership
  (Companion/MaxDps/Shared/Manual/Unavailable) and IntelligenceCompleteness
  (Complete/Partial/Delegated/ManualByDesign/Unobservable/ResearchPending/
  LiveUnverified) derived independently; DelegationReason flags (12 reasons)
  mandatory for every MaxDps-owned entry; ManualReason mandatory for every
  manual entry; per-entry patch metadata (IntroducedPatch/SourcePatch/
  LastValidatedPatch/SourceType/SourceUrl/SourceConfidence/LiveVerified);
  RelationshipKind expanded to 27 kinds (Replaces/ReplacedBy/Alias/
  HistoricalId/PairsWith/Supersedes/RequiresTalent/...). Curated overrides
  parse all of it; derivations are documented and pinned.
- Coverage manifest (`AbilityCoverage`, `--ability-coverage=<path>` +
  `--ability-audit`): DISCOVERED (vendor ∪ curated ∪ verified class-spells ∪
  embedded research ids) / REGISTERED / AUTOMATABLE / COMPANION-GENERATED /
  MAXDPS-DELEGATED / SHARED-GATED / MANUAL / UNOBSERVABLE / RESEARCH-PENDING
  / STALE / MISSING / FILTERED + duplicate-display-name warnings. Audit
  invariants: missing/stale/patch-newer/unowned/delegation-without-reason/
  manual-without-reason/automatable-without-candidate-path all Violations;
  `tools/ability_audit.ps1` now ENFORCES violations 0 / warnings 0 / missing
  0 / stale 0 (exit 3 non-clean, 2 catalog drift, 1 run failure).
- Spec examples verified: Recklessness = MaxDps/Delegated/RotationOrdering+
  ComplexBuffWindow; Pummel = Companion/Complete; Intimidating Shout =
  Manual/ManualByDesign/CrowdControlTargetStateUnobservable; Impending
  Victory = Companion.
- Candidate providers (Knowledge/CandidateProviders.cs): MaxDpsRotation,
  Offensive, Defensive, Interrupt, Mobility, SelfSustain, and a Utility
  provider structurally incapable of Use. Each decision carries Provider,
  Source (MaxDpsWire/BridgeExtra/CompanionGapFill/None) and structured
  evidence. The old inline branches moved faithfully: every verdict and
  reason string is byte-identical (pinned by the pre-existing suite).
- Explainability: RotationEngine.LastAction / CurrentPlanHead
  (LiveActionSnapshot: action, reason, provider, why[]), plan head in
  EngineStatus, telemetry records provider+evidence (old recordings replay 0
  mismatches; fixtures regenerated with the new trace). Scheduler carries
  provider/evidence without changing rank/timing.
- Secret safety: bridge 2.7.0 makes the self-buff block tri-state — cell 33 B
  bit1 is set only when every aura probe ran clean; otherwise the companion
  reads UNKNOWN (documented fail-open) instead of a silent "not active".
  Additive on the v5 wire (version nibble stays 5; pre-2.7 addons behave
  exactly as before). Target melee/cast zero-bit semantics documented as
  explicit field contracts.
- UI 2.0: navigation shell Home / Abilities / Intelligence / Configuration /
  Diagnostics with a rail, design tokens (colors/spacing/type scale),
  ability explorer (debounced search, filter+category chips, virtualized
  owner-drawn rows, expandable details, master-detail inspector, per-ability
  toggle through the existing AbilityPolicy), registry health dashboard with
  click-to-filter, grouped settings by intent, diagnostics page, live action
  "why" panel, toasts; keyboard navigation, Space/Enter activation,
  accessible names, focus rings; --ui-smoke-test now asserts zero-size /
  overlap / label-fit / tab-reachability / accessible-name findings (fail =
  exit 1); --ui-snapshot-page with --ui-snapshot-width for multiple widths.
- CLI: --ability-search= / --ability-class= / --ability-spec= plus the
  existing --ability-info / --ability-audit / --ability-coverage.
- Docs: docs/UI.md (UI architecture), PROTOCOL.md (cell 33 bit1 + buff
  semantics + cellsize correction), V27_COVERAGE_MATRIX.md (the audit
  artifact), TESTING.md §3d live checklist, KNOWLEDGE.md registry section.

VALIDATED (this machine, this session): build 0 warnings / 0 errors;
`dotnet test` 440/440; `lua tests/secret_harness.lua` 94/94; `--ui-smoke-test`
PASS (structural checks enabled); `--bench-scheduler` unchanged sends=1620
(1.82/s) sha256=b71a999d5e46570e; replays solo 9 / defensive 16 /
offensive-interrupt 7 verdicts, 0 mismatches each; `tools/ability_audit.ps1`
exit 0 — Violations 0 / Warnings 0 / Missing 0 / Stale 0, coverage
discovered=3279 registered=3279 automatable=3200 companion=47 delegated=3077
shared=76 manual=79 researchPending=2971 liveUnverified=123, addon
Catalog.lua byte-matches the generated output; 10 page snapshots rendered
non-blank at 1280/480 widths.

LIVE OWED (retail 12.1): reinstall the addon + /reload (bridge 2.7.0), then
docs/TESTING.md §3d: /mdb status protocol/catalog; class/spec detection; Home
status; Ability Explorer search/inspector + a toggle round-trip; Normal
emergency self-heal and Solo sustain; White/Yellow/Orange/Red defensive
urgency; one interrupt per class; one offensive cooldown; one mobility
ability; manual utility never fires; OFF never fires; Modes persistence;
patch warning behavior; record + export + replay a live session with 0
mismatches. NOT validated on a live client here.

HONEST LIMITS: liveVerified is 0 for every entry until a live pass records it
(the audit reports 123 live-unverified automatic companion/shared entries);
the 2971 class-spell tail remains RESEARCH-PENDING + MaxDps-delegated (no
independent generation); 104 duplicate display names are reported as
informational warnings (talent/spec variants must be linked with relations,
never merged by name); the Defensive slot still carries one candidate per
tick; enemy cast identity/danger and enemy count remain unobservable; UI DPI
simulation is not claimed — snapshot widths stress layout and real DPI runs
are part of §3d; an explicit user ON is an explicit policy transition for
non-utility manual entries (utility stays structurally blocked from Use).

## Status (previous): v2.6.0 — ability intelligence registry + five-state context engine (3279 entries, 0 violations, 361 xunit tests + 88 Lua harness checks, live validation owed)

## v2.6.0 ABILITY INTELLIGENCE REGISTRY (this change)

GOAL (user): replace the silent generic default with an explicit per-ability
intelligence registry; stage the 12.1 research machine-readably; move the
policy to five states; enforce the registry at runtime; promote the
emergency self-heal into Normal mode; surface status in the Class skills
screen; add a machine-checkable audit and per-ability user modes. Docs-only
follow-up to record it all; no commits, no PR.

WHAT WAS BUILT:
- Registry schema on `AbilityDefinition` (all derived where possible):
  AbilityKind; IntelligenceStatus (Unknown, Incomplete, MaxDpsBacked,
  CompanionRule, ConservativeSafety, Heuristic, ResearchBacked, Verified,
  ManualByDesign, UnsafeToAutomate); AutomationContext
  (Autonomous/MaxDpsOnly/Manual); MaxDpsRelationship; OpportunityCost;
  InterruptKind / OffensiveUsage / MobilityKind; AbilityRequirement flags;
  TalentNote/HeroTalentNote; PatchVerified; Relations; CapabilityTags;
  EnemyCountMin; HoldForBurst; derived flags. Derivation rules: vendor rows
  -> MaxDpsBacked; curated with `source` -> ResearchBacked ("vendor-only*"
  -> MaxDpsBacked); curated without source -> CompanionRule; never-automatic
  -> ManualByDesign; class-spell tail -> Incomplete + MaxDpsOnly.
- 12.1 research staging: `docs/research/ABILITY_INTELLIGENCE_RESEARCH.md` +
  `registry-research.json` - 14 interrupts typed (13 Dedicated + Silence for
  15487/119910; live 119910 Replaces vendor 19647), 51 offensive cooldowns
  classified, 22 mobility kinds, 33 manual utilities, plus corrections (Gust
  of Wind 192063 cdMs 30->30000 unit bug; Blink 1953 25s->20s; Anti-Magic
  Shell 48707 60s->45s; Quell 351338 20s/6s vs stale wiki table; 231895
  "Avenging Wrath", 228260 "Voidform", 383269 "Graveyard" - id-keyed merges
  only). Curated entries grew 146 -> 240; merged registry 3279 (368 vendor
  rows / 262 distinct + 240 curated + verified tail).
- Five-state policy: `PolicyVerdict.Use/Hold/Skip/Unavailable/Unknown`
  (out-of-range = Unavailable; context-unknown = Unknown; dispel/manual =
  Hold; vetoes/user-policy = Skip). Scheduler counts Hold+Unknown as held,
  Skip+Unavailable as skipped; only Use scheduled; one action per tick
  unchanged.
- Registry enforcement: Incomplete/Unknown/UnsafeToAutomate can never be
  generated by companion-only slots (Skip); on MaxDps slots they keep the
  exact pre-registry Generic path. Manual-by-design is structural OFF.
- Self-heal priority: emergency self-heal (<= 35%) is Use+Emergency in BOTH
  Normal and Solo and outranks main (SelfHeal 3 > Main 4); the wider sustain
  layer stays Solo-only; above emergency in Normal it holds.
- UI inspector: Class skills rows show status label (Verified /
  Research-backed / MaxDps-backed / Companion rule / Safety rule / Heuristic /
  Manual by design / Unsafe / Incomplete / Unknown) + tier + cooldown, manual
  rows muted/italic, a why-tooltip, and a compact legend line.
- Audit + modes: `--ability-audit=<path>` + `tools/ability_audit.ps1`
  (exit 0 clean, 1 run failure, 2 addon Catalog.lua drift);
  `--ability-info=<spellId>` (app-side `/mdb ability`); `--gen-catalog`
  emits revision 3; `[Abilities] Modes=id:Mode` persisted by AppSettings
  (SoloOnly/NormalOnly/Manual/Never/Always/Automatic; Always currently
  evaluates as Automatic - reserved).

VALIDATED (this machine, this session): `dotnet build -c Release` 0 warnings
/ 0 errors; `dotnet test` 361/361 (was 308; +AbilityRegistryTests 25,
+RegistryDecisionScenarioTests 18, +OffensiveInterruptReplayTests 3,
+appsettings/mode tests); `lua tests/secret_harness.lua` 88/88;
`--ui-smoke-test` exit 0; `--bench-scheduler` unchanged sends=1620 (1.82/s)
sha256=b71a999d5e46570e; replays: defensive 16 verdicts/0 mismatches, solo
9/0, NEW offensive-interrupt 7/0, sample legacy 0/0;
`tools/ability_audit.ps1` exit 0, Violations 0 / Warnings 0 / "committed
addon Catalog.lua matches the generated output".

LIVE OWED (retail 12.1): reinstall addon + /reload (Catalog.lua revision 3),
then the v2.6 checklist in docs/TESTING.md - Class skills status/mute/
tooltip; White/Yellow/Orange/Red unchanged; Normal-mode emergency self-heal
(Warrior Impending Victory below 35% fires and outranks main; above 35%
holds; Solo ON restores wider sustain); one interrupt per class
(kinds/timing); one major offensive per spec (paired-window hold; fires when
MaxDps surfaces it); gap closer only when out of melee; utility never fires;
ON/OFF incl. Modes round-trip; `--ability-info` vs live spell; recorded +
exported + replayed session 0 mismatches. NOT validated on a live client here.

HONEST LIMITS: the class-spell tail (2971 entries) is Incomplete/MaxDpsOnly
(modeled, delegated); enemy cast identity/danger unavailable on Midnight;
enemy count not observable (EnemyCountMin only for companion-backed
offensives); one action per tick; one defensive candidate per tick; no live
12.1 run this session; `Always` mode reserved; cooldown/duration values are
sequencing hints with recorded source disagreements.

## Status: v2.5.0 — verified-only class skills (official live-client DB2), pre-cached official icons, adjustable remembered window, state border, paired hero bubbles (308 xunit tests + 88 Lua harness checks), live validation owed

## v2.5.0 OFFICIAL VERIFICATION + UI POLISH (this change)

GOAL (user): remove every class-skill row that cannot be verified from
official sources as an actual 12.1 ability; fix missing icons by pulling them
from online/local sources; check names against official data; make the window
dynamically resizable and remember its size; give Class skills the same
fullscreen layout as Advanced; grow the main-menu bubbles so descriptions fit,
add longer hover descriptions, and put two toggles per row; colour the window
border by run state; build a new version.

VERIFICATION + ICONS (the big one):
- Wowhead tooltip endpoints are BLOCKED from this machine (404/403), and the
  runtime icon cache's tooltip discovery could never work here. Found the
  working OFFICIAL sources: `wago.tools/db2/{SpellName,SpellMisc,
  ManifestInterfaceData}/csv` (live client DB2 exports) and Blizzard's render
  CDN `render.worldofwarcraft.com/us/icons/56/{slug}.jpg`.
- New `tools/Verify-ClassSpells.ps1` streams those tables (SpellMisc's spell
  id is the TRAILING `SpellID` column, not `ID`; manifest slugs are
  space-stripped for the CDN) and writes deterministic
  `Knowledge/spell-verification.json`: 3566 entries, 3515 verified, 51 absent
  from the live client (removed in 12.1), 3510 official icon slugs.
- `ClassSpellBook` now uses the client's official NAME when verified,
  exposes the icon slug, and marks `Verified`; `AbilityCatalog.
  MergeClassSpells` skips unverified ids entirely — removed abilities can no
  longer appear in the table.
- The tool pre-downloaded all 3510 icons into `dist/assets/icons/{id}.jpg`
  (0 failures; render CDN first, zamimg fallback) and the dev bin cache. The
  runtime `SpellIconCache` prefers the official slug (ONE request) and keeps
  tooltip discovery only as a fallback.

UI CHANGES:
- Window size is dynamic and remembered: `[Window] Width/Height` saved on
  resize-end + close, restored (clamped) on start.
- Borderless frame ring: red while stopped, green while running (form padding
  ring + OnPaint; resize grip unaffected).
- Hero card: two toggle bubbles per row (7 pair rows at 84px), three-line
  descriptions that fit cleanly, and a longer hover tooltip per row
  (`SettingRow.Hint`). Card = 42 + 3*24 + 7*88 + 24 = 754.
- Class skills header now matches Advanced exactly (app icon + thick title +
  Back at right; Class/Spec pickers on a toolbar row); still fullscreen.
- Extra fix: `build.ps1` had been wiping `dist\settings.ini` on every publish
  (the clean step deleted it, then copied the default back). It now preserves
  `settings.ini` and `assets/` — verified by a publish that kept the user's
  values and the 3510-icon cache.

VALIDATED (this machine, this session): build 0/0; `dotnet test` 308/308 (was
304; +verification tests, +official-slug icon test); `lua tests/secret_harness
.lua` 88/88; `luac -p` clean; UI smoke 0; snapshots reviewed (card pair rows +
border red; Class skills with real official icons); `--bench-scheduler`
unchanged; replays 0 mismatches; `Verify-ClassSpells.ps1` determinism
(four runs, byte-identical JSON, 0 queued downloads on rerun).

LIVE OWED: in-game toggling spot-check, resize the window and confirm the size
survives a restart, confirm the frame goes green on Start / red on Stop.

## Status (previous): v2.4.0 — Class skills: per-ability toggles for every class/spec (generated class-spells layer, cached icons, animated screen), 304 xunit tests + 88 Lua harness checks, bridge unchanged, live validation owed

## v2.4.0 CLASS SKILLS SCREEN (this change)

GOAL (user): every class ability must be individually toggleable, grouped by
type, in a layered class → spec → ability menu (shared bucket + Main rotation /
Offensive / Defensive / Movement per spec), with game icons on the left
spanning both text lines, no technical text on screen, thick headlines, proper
spacing, smooth transitions/scrolling, and an adjustable window. Plus: the
rogue main rotation "only uses Backstab" investigation (see ROTATION NOTE).

DISCOVERY: `vendor/MaxDps/SpellData.lua` retail `ns.classSpellData` block is a
flat token→id map per spec — 8070 rows, 3566 distinct ids, 13 classes, 39
specs (Demon Hunter has no Devourer rows). It has NO names, NO categories and
NO active/passive flag (spell_durations covers passives too; class modules
reference almost no numeric ids — only 2 in Outlaw). So the layer is modeled:
the honest signal we do have is the curated catalog + vendor Cooldowns; the
rest is best-effort decoded and filtered, and every row's source is named in
the hover tooltip.

WHAT WAS BUILT:
1. `tools/Extract-ClassSpells.ps1` — brace-depth parser over the RETAIL block
   only (Cata/Mists ignored), deterministic, emits
   `Knowledge/class-spells.json` (embedded resource).
2. `Knowledge/ClassSpellBook.cs` — best-effort CamelCase name decoding with a
   connector-suffix split guarded against real words ("TricksoftheTrade" →
   "Tricks of the Trade", "ThousandCuts" → "Thousand Cuts") + a small alias
   map; junk/passive filter for profession/riding/heirloom/old-content tokens
   and the passive talent long tail, maintained with the new
   `--dump-class-skills=<path>` CLI.
3. `AbilityCatalog` third layer (curated > vendor > class spells) with
   `AbilityDefinition.Provenance`; `DefensiveGapFill` refuses ClassSpell rows
   so the generated bridge Catalog.lua cannot grow through a modeled layer.
4. `Knowledge/ClassSkillTree.cs` — shared-bucket vs per-spec membership
   (catalog membership + curated extras + book), section grouping by purpose,
   proven-first ordering so real abilities list above the modeled tail.
5. `ClassSkillsView.cs` — the full-size screen: class/spec dropdowns, shared +
   per-spec sections, 44×44 icon spanning both lines (`IconTile`), thick names,
   one-line "Recommendation: On/Off", ToggleSwitch, hover-only technicals,
   scrim+slide transitions (critically damped, interruptible), eased wheel
   scrolling (`SmoothScrollPanel`). Entry points: hero menu row ("Class
   skills…" next to "Advanced…") and the Advanced page section that REPLACED
   the old inline "Defensive automation" list (no duplicated UI).
6. `Intelligence/SpellIconCache.cs` — download-once CDN icons (Wowhead
   tooltip JSON → zamimg jpg) cached as `assets/icons/{id}.jpg`, validated
   slug, 6 s timeout, silent offline fallback to a drawn "?" tile. Opt-in by
   opening the screen; the app's only network use, documented.
7. Toggles reuse the existing `[Abilities] On=/Off=` storage. OFF = absolute
   automatic-use prohibition; for a Main-slot spell OFF means "skip this
   suggestion" (the scheduler falls through to the next suggested stroke) —
   it can never invent rotation content, only veto.

ROTATION NOTE (user report "the main rotation only uses Backstab"): the
companion cannot change what MaxDps suggests. Main-slot identity is
uncatalogued on old frames and never blocked; v2.4 catalogues Main spells only
to give them a toggle. At level 23 MaxDps/Assisted Combat simply suggests the
Backstab-family rotation for that character — this pass makes that visible
(you can see and toggle which rotation spells are suggested) but does not and
should not synthesize new rotation. The 35-cell wire frame also carries only
ONE main suggestion per tick; the companion is a follower, not a rotation
engine, by design.

REAL DEFECTS FOUND DURING THIS PASS (fixed, pinned):
1. WM_PRINT/composited render bug: a transparent sibling scrim made the whole
   shell subtree skip painting (snapshots came out blank while the real UI was
   fine). FIX: the transition tint is painted by the screen itself, behind the
   opaque shell; dead FadePanel removed.
2. `_header`/picker TableLayoutPanels had no RowStyles, so header children
   mis-sized and were clipped by the scroll panel. FIX: percent row styles;
   captions/theme fixed (owner-drawn dark combos).
3. Decoder connector pass split real words ("ThousandCuts" → "Thous and
   Cuts"); FIX: camel split first, guarded suffix split, no-split word set.
4. Modeled list opened on a screenful of passives; FIX: expanded junk filter +
   proven-first ordering.

VALIDATED (this machine, this session): build 0/0; `dotnet test` 304/304 (was
261; +ClassSpellBookTests, +ClassSkillsViewTests STA, +SpellIconCacheTests);
`lua tests/secret_harness.lua` 88/88; `luac -p` all 6 bridge files; UI smoke
0; `--bench-scheduler` unchanged (sends=1620, sha256=b71a999d5e46570e);
`--replay` both fixtures 0 mismatches; Class skills snapshots reviewed for
ROGUE/Outlaw and WARRIOR/Protection (header, sections, icons, toggles).

LIVE OWED (retail): open Class skills in-game; confirm the live class/spec is
preselected from the wire; toggle one ability off and confirm the companion
stops using it (and that the main-slot veto only skips that suggestion);
confirm icons download once onto disk and survive a restart offline; resize
the window and confirm the list scrolls smoothly. Bridge/protocol unchanged —
no /reload needed for this feature.

## Status (previous): v2.3.0 — MaxDps defensive intelligence + user ability policy (protocol v5 + additive urgency, 261 xunit tests, 88 Lua harness checks, live validation owed)

## v2.3.0 DEFENSIVE INTELLIGENCE (this change)

GOAL (user): one deterministic system around MaxDps's existing brain — consume
MaxDps's own defensive intelligence instead of recreating it, expose defensive
urgency, fill gaps where MaxDps names nothing, add per-class/spec/ability user
ON/OFF control, and keep ONE policy/scheduler/send pipeline. No commits, no PR,
preserve all working-tree changes; do not regress the v2.2 self-sustain path.

VENDOR DISCOVERY (traced, not guessed): `MaxDps:GlowDefensiveHPMidnight`
(vendor/MaxDps/Buttons.lua:1056-1110) is the entire defensive intelligence.
There is no discrete colour enum and no per-ability threshold: each spec calls it
for every `classCooldowns[class][spec].defensive` entry that passes
`CheckSpellUsable` (e.g. vendor/MaxDps_Warrior/.../Fury.lua:18-25), and the
function (a) sets `Flags[spellId]=true` for every usable defensive while alive,
(b) renders `UnitHealthPercent("player", false, GlowDcurve)` as the colour, with
GlowDcurve control points 0.3 = red (alpha 1), 0.5 = yellow (alpha 0.5), 1.0 =
green (alpha 0), and the cooldown's remaining duration as alpha. The single
per-spell special case is Purifying Brew (119582): `UnitStagger/UnitHealthMax`
through a reversed curve, falling back to the HP curve when the stagger read is
secret. So MaxDps's "recommendation" is presence (flags), and its "urgency" is
the rendered colour derived from HP (stagger for 119582) — never a simple
HP-band table, and never white/red as stored values.

DEFENSIVE URGENCY ON THE WIRE (ADDITIVE, wire version stays 5):
- cell 31 G = HP-curve defensive urgency (0 unknown / 1 white / 2 yellow /
  3 orange / 4 red), staged at the vendor curve's own control points
  (HP <=30 Red, <50 Orange, <100 Yellow, 100 White; stagger >=100 Red,
  >=50 Orange, >=30 Yellow, <30 White);
- cell 31 B bit0 = the Defensive slot is a catalog gap-fill (MaxDps named no
  bound defensive); bits 1-3 reserved;
- cell 32 B = stagger-curve urgency.
These nibbles were RESERVED and always 0 before bridge 2.3, and the extension
checksum over cells 11-33 already covered them, so the version nibble STAYS 5
(bridge 2.3.0): a pre-2.3 companion exe still decodes the updated strip, and a
v2.3 companion reads the block whenever it is non-zero. This closes the
Sep-2026 outage class where installing only the addon (or only the exe) left
the companion with nothing to decode. The companion accepts an explicit
version-6 nibble too (same layout, forward compatibility); zero = UNKNOWN, so
legacy frames behave conservatively (majors hold, minors keep the legacy
gates). The engine's Tick also re-decodes legacy v4 (9-cell) and v1 (8-cell)
windows out of the same 35-cell capture, so an old in-game addon still drives
the current exe.

GAP-FILL (real independent candidate source for the Defensive slot): MaxDps's
flagged + ready + BOUND defensive wins; only when MaxDps names none AND HP
urgency is Red, the bridge walks the generated Catalog.lua `defensive` list
(derived by `AbilityCatalog.DefensiveGapFill`: survival purpose, not
never-automatic, immunities excluded, Major before Minor, priority, id) and
encodes the first ready+bound entry with the source bit set. The whole path
stays inside MaxDps's `enableDefensives` switch; below Red the companion never
substitutes its own defensive for MaxDps's silence.

USER ABILITY POLICY (`Knowledge/AbilityPolicy.cs`, `[Abilities]` in settings.ini,
generated UI section "Defensive automation" in Advanced): explicit ON/OFF per
spell id over the curated default (!NeverAutomatic). OFF is an absolute
automatic-use prohibition (reason `user policy disabled`; no urgency, MaxDps
recommendation, Solo or emergency overrides it); ON means eligible, never spam
(White still holds). Rows/UI are generated from the catalog so the UI cannot
drift from the policy knowledge. Checkbox changes persist immediately.

POLICY MATRIX (Normal and Solo share it; Solo adds the v2.2 self-sustain layer):
White holds everything (hard invariant); majors need Red unless the exact
ability is the MaxDps-recommended Defensive slot candidate (one-stage discount,
never below Yellow, and never for curated MinimumUrgency or Escape/Movement/
External); minors need Yellow; Unknown holds majors and keeps minors; emergency
HP (default 35) overrides. Curated MinimumUrgency: Ignore Pain 190456 = Orange
(rage opportunity cost, deliberately not discounted); Purifying Brew's urgency
comes from the stagger curve. Escape/Movement are emergency-only even when
user-enabled; External stays in the same survival gate; Dispel holds (no
observable debuff state).

REAL DEFECTS FOUND DURING THIS PASS (fixed, pinned by tests):
1. The MaxDps-recommendation discount would have lowered a CURATED minimum
   (Ignore Pain's Orange) back to Yellow whenever MaxDps offered it — the
   common case — making the curation meaningless. FIX: `MinimumUrgencyCurated`
   and a discount only for tier-default requirements.
2. A user-enabled Escape/Movement ability would have fallen through to the
   fail-open `Use` fallback with no urgency gate (Vanish ON would fire at any
   HP). FIX: Emergency-only routing (Red or emergency HP) for Escape/Movement/
   External, with no MaxDps discount; Dispel holds.
3. The old checked-in self-sustain fixture (pre-v2.3 telemetry) would have
   replayed as policy-verdict mismatches after the urgency gate landed. FIX:
   policy records without `du` are classified as legacy and skipped (counted,
   reported), and the fixture was regenerated through the real pipeline.
4. Harness/protocol expectation: the v5 version nibble check was updated to v6
   (single existing check, deliberate) — later reverted when the block became
   additive (see below).
5. LIVE-WORKTREE OUTAGE (user report "the companion no longer executes any
   abilities on my rogue"): the in-game addon on this machine was still bridge
   1.3.9 / protocol v4 while the companion had moved to protocol 6, AND the
   Windows shortcut launched the old 1.3.12 exe from a stale checkout. A
   version-nibble bump also meant a new addon + old exe decoded nothing. FIXES
   (all in this pass): (a) the urgency block is ADDITIVE on the v5 reserved
   nibbles so the updated addon works with pre-2.3 exes and vice versa; (b) the
   engine keeps the v4/v1 fallback windows and the v1 target flag now fails
   open (a v1 frame has no target flag; the hard NoTarget gate no longer
   silently blocks ancient addons), pinned by `LegacyAddonCompatTests`; (c)
   `install-addon.ps1` was run so the game now has bridge 2.3.0; (d) the Start
   Menu shortcut was repointed at the current `dist\MaxDpsCompanion.exe` and
   the old per-machine settings (Interact on/F, BNetPath, slot toggles) were
   carried over; (e) `--probe` now mirrors the engine's decode chain (it
   reported "null" for a healthy v4 strip because it skipped the legacy
   windows). LIVE PROBE after the fix (game running, pre-reload v4 addon):
   `locate=0,0 cell 15`, `decode(locate)=proto=4 state=Active
   slots=E,Q,Shift+Q,...` — the strip and the decode chain are healthy; the
   game needs `/reload` to load bridge 2.3.0.

TESTS + EVIDENCE (this machine, this session):
- `dotnet build -c Release` 0 warnings / 0 errors (app + tests).
- `dotnet test` 261/261 (was 244; +DefensiveIntelligenceTests 22,
  +DefensiveReplayTests 4, +LegacyAddonCompat 2, +AppSettings 4, +Telemetry 5).
- `lua tests/secret_harness.lua` 88/88 (was 63; +25 additive-block checks incl. secret HP /
  secret stagger / gap-fill / checksum coverage).
- `luac -p` clean on all 6 bridge files.
- `--ui-smoke-test` exit 0; `--ui-snapshot-advanced` reviewed (generated
  section renders, no clipping after the 2-column pass).
- `--bench-scheduler` unchanged determinism pin: sends=1620 (1.82/s),
  sha256=b71a999d5e46570e.
- `--bench-telemetry` end-to-end 3.39 us/tick.
- `--replay` solo fixture: 9 verdicts, 0 mismatches. `--replay` defensive
  fixture (new, 8 policy ticks / 16 verdicts): 0 mismatches.
- New fixtures: `tests/MaxDpsCompanion.Tests/fixtures/defensive-warrior-urgency.jsonl`
  (White/Orange/Red/user-OFF/Unknown/failed-press cases);
  `solo-warrior-selfheal.jsonl` regenerated with protocol v5 + additive-urgency telemetry.

LIVE OWED (retail 12.1, see docs/TESTING.md §3b): install the 2.3.0 addon +
`/reload`; `/mdb status` must show protocol=5 (and `urg=`); bind a defensive and
verify White holds / Yellow fires a short-CD / Orange holds a major / Red fires
a major; Vanish unchecked never fires even at Red; toggle an ability OFF and
confirm `SKIP reason=user policy disabled` in telemetry; check the generated
Defensive automation section saves `[Abilities]`; record + export + replay a
session and confirm 0 verdict mismatches. NOT validated on a live client here.

HONEST LIMITS: only one defensive candidate per tick (the bridge picks MaxDps's
first flagged ready+bound; tier selection among several ready MaxDps-flagged
defensives is not possible without a second keybind channel); gap-fill requires
MaxDps's `enableDefensives` to stay on and only runs at Red; enemy cast identity
is still unavailable (reflection keys on "a cast is live"); no live 12.1 run.

## Status (previous): v2.2.0 — Solo/self-sustain end-to-end verification + completion (independent companion-only candidate source proven for Warrior, 216 xunit tests + 63 Lua harness checks)

## v2.2.0 SELF-SUSTAIN COMPLETION (previous)

GOAL (user): do not trust the v2.1 report's "implemented + unit-tested, live
owed" claim for Solo/Self-Sustain. Trace the full pipeline (bridge → decode →
tracker → policy → scheduler → send) for the exact scenario "Warrior +
Solo ON + Intelligence ON + low HP + Impending Victory ready" and fix every
real disconnect. No commits, no PR, preserve all working-tree changes.

AUDIT VERDICT: the v2.1 claims were mostly real — the ability catalog, the
SelfHeal slot, the extras-driven bridge candidate source, the policy branch,
the scheduler rank and the tests all genuinely exist; 194/194, 58/58 and the
bench hash reproduced exactly. FOUR real defects were found by tracing, not
by re-reading the report, and fixed:

1. LEGACY-PATH DISCONNECT (the "candidate never reaches the scheduler" bug
   class): `TrySendLegacy` iterated `DecisionEngine.EnabledOrder`, which did
   not contain Slot.SelfHeal/Mobility — with `[Scheduler] Enabled=0` and
   Intelligence ON, Solo mode silently lost every self-sustain press. The
   loop's own comment ("companion-only slots are never allowed to fire
   without intelligence") documented the intended opposite. FIX: EnabledOrder
   = `[Interrupt, SelfHeal, Main, Defensive, Mobility, Offensive,
   Consumable, Trinket]` — companion slots inserted, the six MaxDps slots'
   relative order unchanged; `FallbackOrder` (intelligence OFF) untouched.
   Tests updated deliberately + two new order tests.
2. ABILITY-SPECIFIC SELF-HEAL GATES: `SelfHeal()` ignored `neverAutomatic`
   (a raid heal/channel could be auto-fired), `useBelowHpPct` (Lay on Hands
   40%, the new Death Pact 50% ceiling), and target/range preconditions.
   FIX: those gates now run before the generic HP bands; the emergency
   branch still overrides for a survival press.
3. SLOT-8 RANGE NOT PROBED: `RANGE_SLOTS` in Bridge.lua skipped the
   SelfHeal slot, so a melee heal could be scheduled out of range. FIX:
   `[8] = true`; the policy consumes the tri-state (Skip on a confirmed
   out-of-range).
4. UNBOUND TALENT BLANKED THE SLOT: `ExtraSpellID` returned the first READY
   entry; `WriteSlot` then dropped it if no keybind resolved. FIX: the
   getter requires a resolvable keybind too.
5. TELEMETRY CROSS-CONTAMINATION (minor, found during replay): the scheduler
   path left `_telemetryDecision`/`_telemetryContext` from an earlier legacy
   tick; a mid-session scheduler toggle could record a stale decision and
   replay as a mismatch. FIX: cleared on the scheduler path.

PROOF ADDED (all deterministic, fake clock):
- `SelfSustainEndToEndTests` (11): real 35-cell wire frame →
  `PixelProtocol.Decode` → `CandidateTracker` → `ActionScheduler`: Warrior
  at 50% HP + Solo ON → plan `SelfHeal: SelfSustain (75%)`, verdict
  `Use "solo: HP 50% below sustain 65%; self-sustain"`, spell 202168, key H;
  then `NoteSent` → GCD frame confirms → cooldown frame returns to
  `MainRotation`. Negatives: 100/95/70% Hold+Main, 20% EmergencySurvival,
  Solo OFF Hold, Intelligence OFF excluded, out-of-range Skip, cooldown
  absent, no-target Hold, casting CastHold, failed press bounded + Main
  continues.
- `SelfSustainReplayTests` (2) + canonical fixture
  `tests/MaxDpsCompanion.Tests/fixtures/solo-warrior-selfheal.jsonl`:
  `--replay` → `9 policy verdicts recomputed, 0 mismatch(es)` with the
  full candidate→USE→selected→sent→resume trace in the report.
- `PolicyEvaluatorTests` (+5): ability ceiling, widened-window overheal,
  never-automatic, melee out-of-range, requires-target.
- `secret_harness.lua` (+5): unbound self-heal stays empty; bound one
  encodes VK+id; slot-8 range nibble encodes.

LIVE MECHANICS VERIFIED (2026-09-28, live 12.1; PTR page 1235382 NOT used):
Impending Victory 202168 = 10 Rage, melee 5 yd, instant, 25 s CD, Normal
GCD (1.5 s), heals 30% max HP, Warrior talent replacing Victory Rush
(wowhead live page); Exhilaration/Desperate Prayer/Crimson Vial/Death Pact
via warcraft.wiki.gg (Desperate Prayer corrected to 25%; Death Pact gains
`useBelowHpPct:50`). Coverage decision: extras carry only abilities MaxDps
never surfaces; vendor-surfaced sustains stay on MaxDps's Defensive gate +
the same policy. Per-spec classification table in
`docs/research/ABILITY_RESEARCH.md` §5.

VALIDATED: build 0/0; `dotnet test` 216/216; `lua tests/secret_harness.lua`
63/63; `luac -p` all 6 bridge files; `--ui-smoke-test` exit 0;
`--bench-scheduler` unchanged (sends=1620 1.82/s, CastHold=450,
ChannelHold=450, sha256=b71a999d5e46570e); `--bench-telemetry` 7.12 us/tick;
`--replay` solo fixture 0 mismatches.

LIVE OWED (retail): the Warrior Solo checklist in docs/TESTING.md §3a (bind
Impending Victory, controlled damage, watch the verdict reach Send, heal +
GCD, no spam, Solo OFF removes the independent press, rotation resumes) plus
the listed low-HP variants. NOT validated on a live client in this session.

## Status (previous): v2.1.0 — hostile audit + completion of the v2.0 intelligence pass (execution safety, interruptibility, recovery), protocol v5, 194 xunit tests + 58 Lua harness checks

## v2.1.0 HOSTILE AUDIT + COMPLETION (this change)

GOAL (user): do not trust the v2.0 report; inspect the working tree, classify
every claimed v2.0 feature, and bring the system to genuine compliance with
the situational-intelligence requirements. No commits, no PR, preserve all
existing uncommitted work.

AUDIT VERDICT (v2.0 claims): the architecture, knowledge base, policy,
scheduler, protocol v5, tests and docs all genuinely exist and the reported
numbers were reproducible (build 0/0, 168/168, 47/47). Real gaps were found,
fixed and pinned by tests:

1. CAST/CHANNEL PROTECTION (was non-compliant): the policy deliberately let
   the main rotation clip a channel (pinned by `Main_During_Channel_May_Clip`)
   and the gate vanished entirely when Intelligence was OFF; `CastHold` /
   `ChannelHold` were dead enum values. FIX: shared `ExecutionSafety`
   (`PolicyEvaluator.cs`) used by both the policy and the scheduler; channel
   now holds main; the scheduler applies the same gate when policy is off;
   the legacy loop applies it on v5 frames. Off-GCD exemption requires a
   VERIFIED non-movement classification (`GcdVerified`; curated GCD or the
   intrinsically off-GCD Interrupt/Consumable/Trinket categories). Tests:
   `Main_During_Channel_Holds`, `OnGcd_Defensive_Holds_During_Channel`,
   `Unverified_OffGcd_Defense_Holds_During_Channel`,
   `GapCloser_Holds_During_Cast_Even_Though_OffGcd`,
   `Interrupt_May_Fire_During_Channel`, `Casting_Holds_...` scheduler tests.
2. INTERRUPT VETO (was non-compliant): vendor `GlowInteruptMidnight` sets
   `Flags=true` for EVERY live cast and only dims the overlay for
   non-interruptible ones (Buttons.lua:1136-1143); the v1.3.0 zero-taint
   rewrite dropped the interruptibility re-check, so the bridge encoded
   non-interruptible casts. FIX: `Reader.IsInterruptReady` vetoes on the v5
   sensor's plain `UNIT_SPELLCAST_NOT_INTERRUPTIBLE` boolean (state in the
   event NAME, no payload read); policy skips on
   `TargetCastInterruptible == No` and on a stale `TargetCasting == No`.
   Tests: `Interrupt_Not_Interruptible_Cast_Skips`,
   `Interrupt_Stale_Cast_Skips`, `Stale_Interrupt_Suggestion_Does_Not_Block_Main`,
   harness `interrupt veto: ...`.
3. SENSOR LATCHES: target cast state cleared only on STOP/FAILED/INTERRUPTED
   (not CHANNEL_STOP or target change). FIX: PLAYER_TARGET_CHANGED +
   UNIT_SPELLCAST_CHANNEL_STOP resets; a 15 s watchdog degrades a
   missed-STOP latch to UNKNOWN (fail open). Harness: target change /
   channel stop / watchdog checks.
4. SCHEDULER RECOVERY: `_blockedUntil.Clear()` on any state transition
   re-armed FAILURE suppressions (GCD-pulse machine gun), and suppression was
   a fixed 1.5 s forever. FIX: `_failedUntil` is separate and never cleared by
   transitions; consecutive failures escalate 1.5/3/6/10 s and reset on a
   successful send; link loss clears the pending confirmation so a heal does
   not produce a false rejection. Also: a just-pressed non-main situational
   stroke is demoted behind fresh actions while its confirmation is pending,
   so a failing situational action cannot shadow main
   (`Failed_Situational_Press_Is_Demoted_Behind_A_Fresh_Main`).
5. REPLAY DETERMINISM: `VERSION.txt` claimed policy replay recomputed with 0
   mismatches while `ReplayRunner` only replayed the legacy `DecisionEngine`.
   FIX: the policy record now carries range/buff/interruptible/context-valid
   and the exact `[Solo]` options; send events carry the spell id; replay
   re-derives every recorded verdict with the memory rebuilt from sends in
   LIVE order (the engine writes the send event before its own tick, so a
   tick never observes its own send). Tests:
   `Policy_Verdicts_Recompute_With_Zero_Mismatches`,
   `Policy_Replay_Uses_Live_Send_Before_Tick_Order`,
   `Tampered_Policy_Verdict_Reports_Mismatch`,
   `Policy_Replay_Rebuilds_Trinket_Lockout_From_Sends`.
6. HONESTY: the research doc claimed the combat log exposes enemy spell
   identity; live 12.x sources say combat-log events were removed for addons
   and `UnitCastingInfo` is secret for non-player units. The addendum in
   `docs/research/ABILITY_RESEARCH.md` corrects this with sources; docs
   limitations updated (immune targets, enemy importance, legacy telemetry,
   target HP unread, coverage reality).
7. DATA: Impending Victory corrected to 30% max HP / 25 s (Wowhead 2026-09-27);
   `GcdVerified` semantics pinned; catalog re-inspected for all 40 specs.

VALIDATED (this machine, this session): `dotnet build -c Release` 0/0;
`dotnet test` 194/194; `lua tests/secret_harness.lua` 58/58; `luac -p` all 6
bridge files; `--ui-smoke-test` exit 0; `--bench-scheduler` 27000 ticks -
sends=1620 (1.82/s), CastHold=450, ChannelHold=450, Unavailable=4980,
RetryBackoff=180, intervals min=66 median=132 p95=1782 max=12012 ms,
sha256=b71a999d5e46570e; `--bench-telemetry` end-to-end 3.38 us/tick;
fixture `--replay` 0 mismatches, "0 policy verdicts recomputed" (legacy
fixture format, expected).

LIVE OWED (retail, see docs/TESTING.md §3): v5 calibrate; `/mdb status`
protocol=5 bridge 2.1.0; rotation cadence; interrupt kicks incl. a
non-interruptible veto; defensive emergency preemption; link-loss hold/resume;
policy spot checks; solo thresholds; one recorded+exported+replayed live
session (policy verdict mismatches must be 0).

## v2.0.0 COMBAT INTELLIGENCE (previous change)

GOAL (user): transform the Companion from a suggestion follower into a
deterministic combat execution/intelligence layer. MaxDps stays the
authoritative source for the main rotation; situational abilities become
USE / HOLD / SKIP decisions instead of blind presses. No LLM in the runtime,
no new unsafe game reads, no rewrite of working capture code.

DISCOVERY FIRST: the repo already had v1.4 decision layer, v1.5 telemetry +
replay and the v1.6 scheduler (94 tests, uncommitted). This pass extends
them; nothing was rebuilt. The vendor snapshot had already been refreshed to
MaxDps v11.3.49 (v11.2.11 class modules) — kept as the authoritative source
for ids/categories.

NEW — PROTOCOL v5 (35 cells; docs/PROTOCOL.md is normative):
- 8 slots: the six MaxDps slots + **Mobility** + **SelfHeal** (companion-only;
  keybinds from the generated Catalog.lua per-spec extras).
- 24-bit spell id per slot (0 = unknown) so the policy can key on ability
  identity; the keybind remains the physical identity for sends.
- Combat context: player HP% (UNKNOWN on secret), player cast/channel state,
  target melee/HP-band/cast+interruptible flags, per-slot range tri-state,
  per-slot self-buff-active bits, class/spec wire ids, context-valid flag.
- Two checksums + two commits (core 1-9, extension 11-33); v4 (9 cells) and
  v1 (8 cells) stay decodable; the sampler trims widths out of one BitBlt.
- Bridge sensors are all secret-safe: cast state from **arg-blind
  RegisterUnitEvent** frames (player + target, no event payload ever read);
  HP/range/aura probes via pcall + scrubsecretvalues -> UNKNOWN; `SafeRead`
  contains getter throws (warns once) so one bad getter can never abort a
  frame. tests/secret_harness.lua now drives the whole v5 encode with
  simulated cast events and verifies both checksums + id round-trips (47
  checks; it also caught a real bug: the core checksum wrongly included the
  spell-id cells).

NEW — KNOWLEDGE BASE (`app/MaxDpsCompanion/Knowledge/`):
- `vendor-abilities.json` GENERATED by `tools/Extract-VendorAbilities.ps1`
  from vendor Cooldowns.lua (368 rows -> 262 distinct abilities, categories
  authoritative for this game version).
- `abilities.json` curated: ~230 per-ability policy entries (purpose, tier,
  GCD, ranges, durations, hold conditions, sequencing groups) + per-spec
  Mobility/SelfHeal lists for all 40 specs.
- `AbilityCatalog` merges both with category defaults, normalises tiers for
  non-survival purposes, and assigns stable wire ids. `CatalogLuaGenerator`
  emits `addon/MaxDpsBridge/Catalog.lua`; `CatalogLuaSyncTests` fails on
  drift. `docs/KNOWLEDGE.md` documents the format and the honest limits;
  `docs/research/ABILITY_RESEARCH.md` is the research artifact (sources +
  dates + confidence marks).

NEW — POLICY (Knowledge/PolicyEvaluator.cs): USE/HOLD/SKIP + reason per
candidate. Main: out-of-range skip, hard-cast hold, channel clip allowed for
main only. Defensive: own-buff skip, requiresEnemyCast gate (Spell
Reflection holds without an observed cast), HP thresholds (LoH below 40%),
sequencing via PolicyMemory (own send history + curated durations) with
escalation and emergency override. Offensive: cast/channel hold, pair-window
hold, own-buff skip. Mobility: gap closers need TargetInMelee==No and
in-range; escapes/movement never automatic. SelfHeal: solo mode only, HP
gated, overheal guard. Unknown context -> fail-open for MaxDps slots, hold
for companion-only slots. `[Intelligence] Enabled` now defaults **ON**
(v2.0 product behaviour); `=0` restores the pre-intelligence path (pinned).

NEW — SCHEDULER v2: policy verdicts feed the plan (rank Interrupt >
Emergency > Defensive > SelfSustain > Main > Mobility > Offensive >
Consumable > Trinket); failure recovery: a GCD-riding press that never starts
a GCD inside 600 ms is a failed action (stroke suppressed 1.5 s, counted);
the same stroke sent >=5 times in 1.5 s backs off; suppression never blocks
other ranks. Policy verdicts are collected per tick when telemetry is on and
recorded with reasons (explainability requirement). `--gen-catalog` CLI added.

SETTINGS/UI: `[Intelligence] Enabled` default on; new `[Solo]` section
(Enabled, EmergencyHpPct=35, SelfSustainHpPct=65, DefensiveEscalateHpPct=60);
`[Spells] spell7/spell8` (Mobility/SelfHeal, default on); hero card now 11
rows (Mobility/Self-heal added) with updated height; Advanced gains a Solo
section and reworded Intelligence hint. Solo requires intelligence (stated in
the UI).

TESTS: 168 xunit (adds PixelProtocolV5Tests, AbilityCatalogTests,
PolicyEvaluatorTests 37 scenarios, ActionSchedulerPolicyTests 15) + the Lua
harness now 47 checks. Validated: build 0/0, `dotnet test` 165/165,
`luac -p` all 6 bridge files, `lua tests/secret_harness.lua` 47/47,
`--ui-smoke-test` exit 0, `--bench-scheduler` runs (plan hash
14ec76ef8c151770 for the default 27000-tick script: sends=2040 (2.29/s), intervals min=66 median=132 p95=1485 max=12012 ms, holds NoCandidate=7200/MinInterval=5460/Unavailable=3870/RetryBackoff=270, stale-demoted ticks=780, RejectionsDetected feeds the suppression count).

LIVE OWED (retail, per docs/TESTING.md §3): v5 calibrate; `/mdb status`
protocol=5; rotation cadence + interrupt kicks; defensive emergency
preemption; link-loss hold/resume; policy spot checks (Spell Reflection
gate, Charge positioning, defensive sequencing, solo thresholds).

## Status (previous): v1.6.0 — deterministic action scheduler (default on), 94 xunit tests

## v1.6.0 DETERMINISTIC ACTION SCHEDULER (this change)

GOAL (user): the Companion is now fast enough (v1.3.9 measured 4.2 ms tick
floor, 33 ms / 30 Hz cadence); make the action scheduler extremely reliable
instead of polling faster. Preserve the existing behaviour as the baseline,
change no rotation engine, bypass no Blizzard restriction, no memory
reading/injection/OCR, invent no unavailable combat state.

DESIGN: new `Scheduler/` module (app side only), a pure state machine over
the decoded MaxDps candidates + this process's own send/attempt history. No
clock/OS reads (NowMs is passed in), so every scenario is fake-clock testable:
- `SchedulerModel.cs`: ScheduleReason (holds + urgency), AttemptOutcome,
  ScheduleInput, ScheduledAction, SchedulePlan.
- `ActionScheduler.cs`: Observe(frame)/IsLinkLost, Advance(input) → plan,
  NoteSent / NoteAttempt / NoteExternalSend, Reset. Gate order:
  1. link — heartbeat frozen > HeartbeatTimeoutMs = LinkLost, NOTHING fires
     (fixes the stale-but-decodable strip that kept sending forever);
  2. protocol N/N-1 (anything else = ProtocolMismatch hold);
  3. paused / no-target;
  4. priority Interrupt > Defensive > Main > Offensive > Consumable > Trinket
     (upstream only lights Defensive when its HP gate fires — emergency above
     the rotation) + duplicate-stroke collapse;
  5. stale demotion (pressed-since-change AND unchanged ≥ StaleAfterMs moves
     behind fresh; a stale defensive can never block a fresh main; all-stale
     keeps order — no deadlock);
  6. GCD skip for GCD-riding candidates (interrupt bypasses);
  7. one press per MinKeyIntervalMs — a CHANGED interrupt stroke bypasses the
     interval (a genuine kick lands immediately after a DPS press); an
     identical repeated kick stroke waits it out (no poll-rate machine-gun);
  8. v1 frames carry no GCD flag: an identical re-suggested stroke waits
     RepeatSuppressMs instead of firing every poll;
  9. unavailable suppression: candidates rejected by local gates (movement
     bind, held key, focus, lost window) are skipped briefly so lower ranks
     fall through and the status line stops flapping.
  State transitions (target regained / GCD release / state change) clear
  local suppression; MinKeyInterval and link state persist.
- INTEGRATION: `RotationEngine.TrySendOne` dispatches to `TrySendScheduled`
  when enabled (default) and to `TrySendLegacy` otherwise — the legacy body
  is unchanged, so Enabled=0 is byte-identical. The engine observes every
  decoded frame for the heartbeat; link loss holds rotation AND
  auto-target/interact. Target/interact sends call NoteExternalSend so the
  min interval spans all input. Every existing OS gate (movement bind,
  physically held key, focus, background policy, window liveness) stays
  authoritative — the scheduler produces an order, never a bypass.
- SETTINGS/UI: `[Scheduler] Enabled=1` (default), HeartbeatTimeoutMs=500,
  RepeatSuppressMs=900; Advanced → "Action scheduler" checkbox + the
  Diagnostics decision line now reports plan head / reason / confidence /
  suppressed count. `[Intelligence]` documented as legacy: only reached
  when the scheduler is off.
- MEASUREMENT: `--bench-scheduler[=n]` runs a scripted 900-tick synthetic
  scenario (main spam, GCD windows, interrupt mid-GCD, defensive emergency
  then stale demotion, 2 s heartbeat freeze, protocol v1 no-GCD, target
  loss, idle) on a fake 33 ms clock → `bench-scheduler.txt`: sends + sends/s,
  interval min/median/p95/max, hold-reason histogram, suppression counts and
  a plan-sequence SHA-256. Same ticks in, same numbers out.

MEASURED (this machine, 27000 ticks = 891 s simulated, after the interrupt
fix): sends=3000 (3.37/s) — Main=1530, Offensive=210, Defensive=810,
Interrupt=450; intervals min=66 median=132 p95=396 max=12012 ms (max = the
240-tick no-suggestion idle phase + resume); holds MinInterval=8640,
NoCandidate=7200, NoTarget=3600, GcdHold=1800, RepeatSuppressed=1440,
LinkLost=1320; stale-demotion ticks=1020; sha256=55b0eafe7c8e1b55.

THE BENCH CAUGHT A REAL DEFECT during implementation: the first scheduler let
a persistent interrupt suggestion fire every 33 ms tick (1800 sends over the
scenario) because the interrupt bypassed pacing entirely. Fixed to
changed-stroke-bypass / identical-repeat-respects-interval (450 sends).

TESTS (new, 23) in `ActionSchedulerTests.cs` (fake clock, explicit NowMs):
null stale frame; protocol mismatch; frozen heartbeat = link loss + recovery;
long frozen window = zero sends; A→A→A one press per min interval; A→B
transition; order independent of input order; duplicate-stroke collapse;
disabled exclusion; interrupt preemption + GCD + min-interval bypass;
repeated identical interrupt pacing; GCD hold then falling-edge release;
defensive emergency preempts main; stale defensive behind fresh main;
all-stale no deadlock; target lost/regain + suppression reset; paused;
repeated unavailable fall-through + retry after the window; attempt/last-sent
history; v1 no-GCD repeat suppression; v4 no false repeat suppression;
movement-bound confidence. Suite 71 → 94.

VALIDATED: `dotnet build -c Release` 0 warnings / 0 errors; `dotnet test`
94/94; `--ui-smoke-test` exit 0; `--bench-scheduler` output above. LIVE
OWED (retail): with the scheduler default ON — interrupt lands during the
GCD; a defensive emergency preempts a live main; a persisting stale
suggestion demotes behind a fresh one; stopping the addon (kill the strip /
`/reload`) holds ALL input after ~500 ms and resumes when it comes back.

## v1.5.0 LOCAL ROTATION TELEMETRY + REPLAY (previous)

GOAL (user): diagnose why the Companion selected or failed to select an action
during Retail Midnight gameplay without reproducing the exact situation. No
external/cloud telemetry, no personal data, no Blizzard secret values, no
rotation-engine rewrite.

DESIGN: `Telemetry/` layer, integration at the engine's existing single
outcome funnel (`RotationEngine.Report`) plus `TrySendOne` (context/decision
were already built there):
- `TelemetryEvent` + source-gen `TelemetryJson`: JSONL format v1, kinds
  session/tick/send/link. Tick = monotonic `tMs` + `utc`, protocol version,
  state, heartbeat, in-combat/on-GCD/has-target, all six slot suggestions,
  candidate snapshot (enabled/actionable/observed/pressed), decision
  (src/sel/reason/conf/stale/order), staleness window, status note and decode
  `fault` (Length/Magic/Contrast/Version/State/Commit/Checksum). Send =
  what/slot/key/interval. No raw screen content, no Blizzard values, no
  network; disabled = a few null checks per tick.
- `TelemetryRecorder`: bounded circular buffer of pre-serialized lines
  (default capacity 10000 ≈ 8.5 MB ≈ 5-6 min at 30 Hz; measured), oldest
  evicted and counted; Export writes UTF-8 JSONL on demand; never grows
  forever.
- `TelemetryReader` + `ReplayRunner`: rebuilds each recorded
  `DecisionContext` and calls the unmodified `DecisionEngine.Evaluate`.
  Intelligence recordings must recompute with 0 mismatches (determinism
  proof); legacy recordings report the evaluator's "would-select". Report
  lists candidates, selected/reason/confidence, rejected candidates (disabled
  / movement-bound / duplicate stroke / lower rank / stale), GCD/stale holds,
  send timing and a summary; byte-identical across runs.
- `PixelProtocol.Diagnose`: shares the Decode core (cannot drift) and returns
  the fault classification for telemetry. Decode behaviour unchanged.
- Settings/UI/CLI: `[Telemetry] Enabled=0` (default off), `Capacity=10000`;
  Advanced → Telemetry section (arm checkbox, ring/status readout, Export…,
  Replay…); `--replay=<file>` writes `<file>.replay.txt`;
  `--bench-telemetry[=n]` writes `bench-telemetry.txt`.

TESTS (new, 35): serialization round-trip + pinned v1 line shape + null
omission + culture invariance + tolerant reader; ring eviction/order/export/
clear/sequence; replay determinism (same report twice), exact recompute
(0 mismatches), tamper detection, legacy would-select + fallback match, GCD
hold, rejected list, stale demotion, recorder→export→reader→replay E2E,
fixture replay, RunFile output; Diagnose classifications; `[Telemetry]`
settings defaults/parse/round-trip/clamp. Suite 36 → 71.

VALIDATED: `dotnet build -c Release` 0 warnings / 0 errors; `dotnet test`
71/71; `--ui-smoke-test` exit 0; `--bench-telemetry` numbers in
`bench-telemetry.txt`; `--replay` on
`tests/.../fixtures/sample-session.jsonl` → 0 mismatches. LIVE OWED: record
one real combat session (checkbox on, Start, fight, Export) and replay it;
confirm the report matches what happened on screen.

## v1.4.0 ROTATION INTELLIGENCE (previous)

GOAL (user): evolve `MaxDps suggestion → decode → priority loop → keybind`
into `suggestions → normalized candidates → observable context → deterministic
evaluator → selected action + reason/confidence → existing send pipeline`.
Explicitly NOT an LLM runtime, NOT new game state, protocol unchanged.

INSERTION POINT: `RotationEngine.Tick → TrySendOne`. The layer only produces
the send ORDER; every existing per-slot gate (slot toggles, GCD skip, movement
bind, physical hold, focus, key gap) is untouched and stays authoritative.

NEW (app side only):
- `Decision/DecisionModel.cs`: `ActionCandidate` (slot, stroke, enabled,
  actionable, first-seen/last-changed/last-pressed, pressed-since-change),
  `DecisionReason`, `DecisionContext`, `DecisionResult`.
- `Decision/DecisionEngine.cs`: pure static evaluator. `FallbackOrder` ==
  the exact legacy Priority (Main, Offensive, Interrupt, Defensive,
  Consumable, Trinket); `EnabledOrder` == Interrupt, Main, Defensive,
  Offensive, Consumable, Trinket. Rules: enabled-only candidates, duplicate
  `KeyStroke` collapse across categories, stale demotion (pressed since last
  change AND unchanged >= StaleAfterMs moves behind a fresh candidate; never
  removed; all-stale = order unchanged), GCD reported as `GcdHold`, reason +
  deterministic confidence (interrupt 95 / defensive 80 / main 70 / off 60 /
  cons 55 / trin 50; -20 not-actionable, -40 stale, GCD cap 20).
- `Decision/CandidateTracker.cs`: per-slot suggestion history + press marks.

WIRING:
- `RotationEngine`: tracker snapshot once per decoded frame; `TrySendOne`
  iterates `DecisionEngine.Evaluate(...)` when enabled, else
  `DecisionResult.Fallback()` (byte-identical legacy). Successful sends call
  `NotePressed`. `EngineStatus` gains trailing `Decision` (Advanced-only:
  head + reason + confidence).
- `AppSettings`/`settings.ini`: `[Intelligence] Enabled=0` (default),
  `StaleAfterMs=1500`. Disabled is the safety contract.
- `MainForm`: new Advanced "Intelligence" section (one checkbox + hint) and
  a third Diagnostics line for the decision readout.

TESTS: new `tests/MaxDpsCompanion.Tests` (xunit 2.5.3, .NET 8, x64; the
app csproj already had `InternalsVisibleTo("MaxDpsCompanion.Tests")`).
36 tests — fallback exact order, interrupt-first, defensive-above-offensive,
input-order independence, duplicate collapse, stale demotion + all-stale
no-op + never-pressed-not-stale + threshold boundary, GCD hold/interrupt,
actionability confidence, tracker windows/press-flag/reset, `[Intelligence]`
defaults/parse/round-trip, and v4+v1 decode / v2+v3 rejection / checksum /
TrimToV1.

VALIDATED: `dotnet test` 36/36; `dotnet build -c Release` 0 warnings /
0 errors; `--ui-smoke-test` exit 0; `luac -p` clean on all 5 bridge files
(addon untouched, still 1.3.9 / protocol v4). LIVE OWED: with
`[Intelligence] Enabled=1`, confirm interrupt preempts a live main, a stuck
main yields to a fresh offensive, and disabled still behaves exactly as
v1.3.12.

ARCHITECTURAL LIMITS (honest): the 12-bit spell id is discarded at decode —
candidate identity is the resolved KeyStroke, so duplicate detection is
stroke-level, not spell-level. No health/cooldown/cast state exists in the
companion, so "urgency" is presence+context, not HP-thresholded; real
emergency thresholds would need a protocol extension (deliberately deferred —
would require a version bump and the N/N-1 path). Staleness is retail-owed.

## Status (previous): v1.3.12 — legacy-path BlockLocator fix (build green, smoke green, probe-verified on live client)

Companion-only changes on top of `cfc3776` (v1.3.9). Bridge unchanged at
1.3.9, protocol v4. Three changes: "v1.3.10 STALE-ATTACHMENT FIX",
"v1.3.11 ADVANCED SIMPLIFICATION", and "v1.3.12 PIXEL-DETECTION FIX" below.

## v1.3.12 PIXEL-DETECTION FIX (this change — "detection broken in the new build")

USER REPORT: pixel detection broke after the UI/robustness builds; "it worked
in the older .9 build". Status: "No pixel block - client must be windowed or
borderless".

ROOT CAUSE — `BlockLocator` rejected the real strip on an UNLEARNED profile.
The strip is a single horizontal row of cells. Under the legacy (no learned
colour profile) magenta gate the NEXT cell's colour also passes the loose test
— live client measured cell1 = RGB(51,34,136): R+B=187 > G*2+32=100, R>=48,
B>=48. So the magic cell's horizontal run read 30px while its vertical run
(the true size, since it is a one-row strip) read 15px, and the old
`if (Math.Abs(width - height) > 1) continue;` square check threw the block
away. `StartEngine`'s sweep and the engine's `Relocate` both returned null, so
the companion kept the (wrong) stored/default cell size and decoded garbage →
"No pixel block".
WHY .9 SEEMED FINE: the user's working `settings.ini` had a LEARNED profile
(`LearnedAt` set); a learned profile matches by distance, rejects cell1, keeps
the run square, and the locate succeeds. The freshly deployed default (no
profile) took the legacy path and failed. A live `--probe` confirmed the strip
is present and decodes v4 `Active` (`slot Main='2'`) at 0,0 cell 15.

FIX (`BlockLocator.Scan`): the magic cell's VERTICAL run is its true size
(single-row strip), so use `size = Math.Min(width, height)` and only reject
wildly non-square blobs (`Math.Max(width, height) > size * 4`); `Verify`
(strict Decode/Classify) still filters false positives. Also added a
`--probe` CLI diagnostic (attach + locate + sample + decode → `probe.txt`)
used to verify this fix against the live client without the UI.

VALIDATED (live client, game running):
- `--probe` with the user's learned settings → `locate=0,0 cell 15`,
  `decode=Active slots=2,...` (unchanged, no regression).
- `--probe` with UNLEARNED default settings → `locate=0,0 cell 15` (was
  "null" before the fix), `decode(locate)=Active`.
- `dotnet build -c Release` 0 warnings / 0 errors; `--ui-smoke-test` exit 0.
LIVE OWED: launch the updated build against the strip and confirm "Sending".

## v1.3.11 ADVANCED OVERLAY SIMPLIFICATION (this change)

USER REQUEST: simplify the Advanced overlay, remove legacy options no longer
needed, make it the same space/window size as the main screen, and make the
content scrollable with a title segment carrying the generated app icon.

DONE (MainForm.cs / UiControls.cs):
- Advanced is now a FULL-SIZE body view (`Dock.Fill`, same space as the main
  screen) instead of a small centered 560x520 dialog over a scrim. It has a
  48px title segment with the generated app icon (`AppIcon()` → detached
  `assets/icon-256.png`) + "Advanced" + Back, and a scrollable settings stack.
- Main screen body is now scrollable (`_bodyScroll`, AutoScroll) so short
  windows scroll instead of clipping the card/buttons; the default window no
  longer shows a scrollbar it does not need (content 8px shorter than the
  exact sum).
- Removed legacy/dead UI:
  * `RequireForeground` option entirely (AppSettings property + parse + save,
    the Setup checkbox, settings.ini + README) — the engine never read it;
    mouse/interact always require the foreground, and `AllowBackgroundKeys`
    is the real switch.
  * the redundant "Slots" section + `_mainSlot` checkbox (mirrored the card
    toggles — two sources of truth).
  * the duplicate "Process" row in Strip (Setup already owns it).
  * dead controls `ClassBadge`, `LinkLamp`, `StripView` (+ `_badge`,
    `_linkLamp`, `_linkLabel`, `_stripView`, `_rawValue` fields).
  * the diagnostics labels were forced `Visible=false` by BuildCard and never
    re-shown — the section ("Live suggestion") rendered empty. Renamed
    "Diagnostics" and restored visible.
- Simplified the two clipped sections: Timing is one field per row (was a
  4-column pair grid whose captions wrapped); Targeting is one setting per row
  with short labels (the key captions were packed into a sub-flow that
  wrapped and clipped). Section order: Setup / Strip / Timing / Targeting /
  Color / Battle.net / Diagnostics.
- App version 1.3.11. Bridge unchanged.

VALIDATED: `dotnet build -c Release` 0 warnings / 0 errors; `--ui-smoke-test`
exit 0; `--ui-snapshot` + `--ui-snapshot-advanced` reviewed (no stray main
scrollbar, no clipped Targeting/Timing text, icon header present, Advanced
fills the body).

## v1.3.10 STALE-ATTACHMENT FIX (this change — "stops detecting the strip until restart")

USER REPORT (Sep-2026): after a relog / character change / toggling the
companion off-on, the companion stops detecting the pixel strip until the
app is fully restarted. Pressing Start alone did not recover it; after a
restart, Start worked again.

ROOT CAUSE (class): the engine's attachment was cached for the process
lifetime and never re-validated against the game's CURRENT state.
- `WowWindow.Refresh` trusted the cached HWND for as long as
  `IsWindow(handle)` stayed true. When the game recreates/replaces its
  window on relog, character switch or display-mode change, the old handle
  can linger as a live (hidden) window owned by the same process, so the
  cache never refreshed. The engine then sampled the wrong rectangle
  forever — while `StartEngine`'s own one-shot sweep used a FRESH
  `WowWindow`, found the (correctly placed) strip, and updated the offsets.
  That is why Start looked like it "found" the strip but the engine stayed
  blind, and why only a process restart healed it.
- `ScreenSampler` cached `GetDC(NULL)` + the DIB surface for the process
  lifetime. A display-mode change / DWM reset / session switch can
  invalidate that DC; the sampler then returned empty cells forever (the
  sweep, which uses a fresh `Graphics.CopyFromScreen`, kept working).
- Existing periodic `Relocate` could not help: it reused the stale origin
  and the stale capture surface.

FIX (companion only, no protocol/addon change):
- `WowWindow`: `Refresh` now re-validates the cached handle against the
  process's CURRENT `MainWindowHandle` at most once per second (`StillCurrentMainWindow`,
  via `GetWindowThreadProcessId` + `Process.GetProcessById`). A different
  non-zero main window ⇒ re-resolve. New `Reset()` drops the cache.
- `ScreenSampler`: BitBlt failure now rebuilds the screen DC + DIB surface
  and retries once; new `Reset()` drops the DC + surface on demand.
- `RotationEngine`: tracks the last frame it decoded; after
  `RecoveryIntervalMs` (2 s) without a frame it forces `RecoverAttachment()`
  (window + sampler reset) and re-attaches next tick. `Start()` also
  re-attaches from scratch, so toggling the companion off/on now heals a
  stale attachment instead of needing an app restart. Calibrate-pattern
  detection runs before recovery so a legitimate pattern never triggers it.
- App version 1.3.10 (csproj/assembly, title auto-sourced), repo
  `VERSION.txt`. Bridge `1.3.9`, protocol v4 unchanged.

VALIDATED: `dotnet build -c Release` 0 warnings / 0 errors;
`--ui-smoke-test` exit 0. LIVE OWED: relog / change character / toggle
off-on with the engine running → strip is re-detected (no app restart),
status returns to "Sending"; a display-mode change mid-session recovers.

## Status (previous): v1.2.0 — Spell Frame slot parity + grouped hero

Uncommitted on top of `41db101` (v1.1.0 readiness gate): protocol v2
(9 cells) + 6 Spell Frame-named slots + grouped hero card (see below).
Base `ac84240` (v1.0.0): 8-cell `MaxDpsBridge` addon + `MaxDpsCompanion`
.NET8 WinForms app (PRP chrome, Aethys engine), vendor snapshot of upstream
MaxDps v11.3.43 + all class modules.

## v1.1.0 — what changed and why

The companion hammered one unavailable key while other slots had live
suggestions. Root cause: the bridge encoded whatever MaxDps suggested
without checking castability, and the app's priority loop replays the
first valid slot every tick. Fix is bridge-side (one place, all slots):

- `addon/MaxDpsBridge/Reader.lua`: `MDB.IsSpellReady` (charges →
  `CooldownConsolidated` GCD-aware → `IsSpellUsable`), `MDB.IsInterruptReady`
  (ready + live interruptible cast on target; upstream flag alone lies —
  `GlowInteruptMidnight` sets `Flags` and only dims overlay alpha to 0 on
  non-interruptible casts, vendor `Buttons.lua:1136-1143`).
- All five getters gated; `WriteSlot` re-gates at encode time
  (`Bridge.lua`); `/mdb status` gains `ready=MCIDN` flags.
- Versions: bridge `1.1.0` (`MDB.VERSION`, `.toc`, `VERSION.txt`),
  app `1.1.0` (csproj + title), repo `VERSION.txt`.

## Validated

## Validated (v1.1.0)

- `dotnet build -c Release`: 0 warnings, 0 errors.
- `--ui-smoke-test` exit 0.
- Bridge installed at retail AddOns\MaxDpsBridge; upstream MaxDps* folders untouched.
- Start Menu shortcut `MaxDPS Companion.lnk` (taskbar: right-click
  the running app → Pin to taskbar; assembly identity is set).
- BNet launch: `battlenet://WoW/` protocol first, exe fallback minimized;
  remembered-account login, no credentials anywhere. SSO autologin via
  Battle.net `--exec="launch WoW"` (same path as the Play button).

## v1.2.0 — Spell Frame slot parity + grouped hero (this change)

Companion hero toggles now use the in-game overlay names from the
screenshot (`Spell Frame Options`): Show offensive / defensive /
consumable / trinket spells, in three labelled groups — SPELL SLOTS
(4 rows) → COMBAT & TARGETING (Out of combat / Auto-target /
Auto-interact) → INTERRUPT kill-switch. Research basis: toggle-list grouping
(>5 toggles need subheadings), chunking for scanning, WCAG AA contrast
(4.5:1 body), 16px+ type, static action labels, immediate-apply toggles
separate from Advanced submit-style settings (progressive disclosure).
Background functionality follows the names:

- Bridge `Reader.lua`: old consumable bucket split by `MaxDps.Consumables`
  itemID — potions stay `GetConsumableSpellID`, other `ItemSpells` become
  `GetTrinketSpellID`. `GetCooldownSpellID` renamed to
  `GetOffensiveSpellID` (same bucket) with a back-compat alias.
- Protocol v1 (8 cells) → v2 (9 cells): new cell 6 Trinket, status moves to
  7, version to 8 (R=2). Calibrate pattern paints cells 1-6; checksum covers
  cells 1-7. Bridge `1.2.0`, app `1.2.0`, repo `VERSION.txt` `1.2.0`.
- C#: `Slot` gains `Trinket`, `Priority` fires Int/Def/Off/Main/Cons/Trin,
  `ColorLearner`/`StripView`/`BlockLocator` generalised to 9 cells,
  `SlotEnabled` gains `spell6` (old 5-slot ini files keep toggle defaults).
- `settings.ini`/`AppSettings`/`README`/`PROTOCOL.md`/`ARCHITECTURE.md`
  updated; per-machine `dist\settings.ini` needs `spell6=0` added (or is
  rewritten on next save).
- Hero card (`MainForm.cs` + `UiControls.cs`): `GroupHeader` eyebrow labels
  (brass tick + small-caps + hairline, same language as `RuleSection`),
  fixed-row table (status 40 + headers 3x22 + rows 8x64 = 642 card, 860
  window) so rows can never squeeze; `SettingRow` smoke fill + near-white
  type (WCAG AA), per-group zebra (±6, resets per header), flat status
  row (nested tables collapsed to zero height and hid the status line —
  replaced with dot-Left + label-Fill panel); body table + `FitToScreen`
  860 / `ClampToScreen` for short screens; title stamp is build-generated
  (`build.ps1` rewrites `ThisAssembly.Gen.cs` on every publish, so the
  "outdated date" was a stale exe — rebuild fixes it, no code change).

## Validated (v1.2.0, this session)

- `dotnet build -c Release`: 0 warnings, 0 errors (final).
- `--ui-smoke-test` exit 0 (final).
- `luac -p` clean on all 5 bridge files.
- `install-addon.ps1` re-run: in-game bridge now v1.2.0 / 9 cells /
  protocol 2 (was stale v1.1.0 / 8 cells / protocol 1 — the calibrate
  outage source).
- Snapshot iterations: iter1 (spacers invisible + clipped subtitles) →
  iter2 (contrast pass) → iter3/4 (flow-model regressions: empty card,
  crushed buttons — FlowLayoutPanel mis-measures Dock.Fill children) →
  iter5 (fixed-row table restores rows, card overflows ~120px) → iter6
  (compact 64px rows, all chrome visible) → iter8/9-final (status row
  restored, grouping + no-clip confirmed at default size) → calfix
  (compat fix below, layout unchanged).

## Calibrate outage Sep-2026 — root cause + fix (this change)

SYMPTOM: `/mdb calibrate on` showed `MDB: calibrate pattern ON` in chat,
but Recalibrate failed with "No calibrate pattern on screen" and sent
`calibrate off` straight back (chat log in the report screenshot).

ROOT CAUSE — version skew, both halves uncommitted v1.2.0 work:
1. The v1.2.0 protocol bump (8→9 cells, status 6→7) changed the
   calibrate pattern AND the learner (`Classify` averages cells 1-6,
   status at 7) — but the in-game addon was still v1.1.0 (8 cells,
   status at 6). Verified on disk: installed `Bridge.lua` had
   `CELL_COUNT = 8`, `PROTOCOL_VERSION = 1` while the repo had 9/2.
   `install-addon.ps1` was never re-run after the protocol change,
   and nothing told the user a `/reload` was owed.
2. The v2-only `Classify` then rejected the live v1 pattern three ways
   at once: `cells.Length != 9` hard reject, status read at index 7
   (a v1 slot cell, not Paused), average/spread over cells 1-6 (cell 6
   is the v1 STATUS cell — a Paused encoding, never flat with the
   slots). Every gate failed, so "no pattern" was guaranteed even
   though the pattern was on screen.
3. Contributing: `install-addon.ps1` has no version check, the
   companion never surfaces skew (Decode just returns null → "no pixel
   block"), and the calibrate error text ("Addon updated? (/reload)")
   was right but buried as step-1-of-4 boilerplate.

FIX — companion decodes N and N-1 (contract in `docs/PROTOCOL.md` +
`ARCHITECTURE.md` — "companion decodes N and N-1, addon encodes N;
skew warns, never hard-fails"):
- `PixelProtocol`: v1 constants (`CellCountV1=8`, `StatusCellIndexV1=6`,
  `VersionCellIndexV1=7`, `SupportedVersionV1=1`, `SlotCountV1=5`);
  `Decode` routes by length into shared `DecodeCells`; v1 frames decode
  into the full 6-slot array with Trinket null (priority/settings
  indexing never shifts).
- `ColorLearner.Classify`: routes by length into shared `ClassifyCells`
  (v2: slots 1-6/status 7; v1: slots 1-5/status 6); fixed a latent bug
  where the status gate used `StatusCellIndex` instead of the passed
  cell (would have re-broken v1 the moment anyone called it); all
  helpers (`SlotRange`, average, spread, anchors, build) resolve the
  slot range per-frame.
- `ScreenSampler`: `SampleV1`/`SampleRegionV1` 8-cell windows.
- `BlockLocator.Verify`: tries the 9-cell window, then the 8-cell
  window (sweep finds a stale strip too).
- `RotationEngine.Tick`: v2 decode, then v1 fallback with an explicit
  "addon is v1 - run install-addon.ps1 + /reload" status (no more
  silent "no pixel block"); calibrate-pattern probe checks both widths.
- `MainForm.CalibrateWorker`: per-tick `SampleBoth` (v2 fast path, v1
  fallback); learns either pattern, then warns when it learned v1
  ("reinstall addon + /reload + recalibrate") instead of failing.
- `PatternVisible` probes both widths.
- Addon re-installed in-game (now 9 cells / protocol 2); user must
  `/reload` once, then Recalibrate learns the v2 pattern normally.

FUTURE-PROOFING (do not regress): any protocol bump must (a) keep the
N-1 decode path in `PixelProtocol`/`ColorLearner`, (b) keep the dual
sweep in `BlockLocator`, (c) keep the skew warning in `Tick` +
`CalibrateWorker`, (d) update `docs/PROTOCOL.md` + bump cell-8 R.
`install-addon.ps1` still has no version check — flagged, not fixed.

## Secret-taint outage Sep-2026 — root cause + fix (bridge v1.2.1)

SYMPTOM: BugGrabber 99x `Reader.lua:455 attempt to compare local 'Start'
(a secret number value, while execution tainted by 'MaxDpsBridge')` via
`IsSpellReady ← GetMainSpellID ← Bridge Update`, plus 384x `Reader.lua:424
attempt to compare local 'Charges' (a secret number value ...)` — most
actions never encoded, companion↔bridge execution failed. Calibration
itself worked (pattern path is secret-free).

ROOT CAUSE: on Midnight, `C_Spell.GetSpellCooldown` /
`C_Spell.GetSpellCharges` return `secret number` fields
(`startTime/duration`, `currentCharges/cooldownStartTime/
cooldownDuration` — see the report's `Info=<table>` dumps: every numeric
field is `<secret number>`). ANY comparison/arithmetic on a secret while
tainted (we called the API, so we always are) throws, and the throw
aborted the whole `Bridge.Update` tick → no slots encoded that frame →
companion fired nothing, 99x/384x per session. Upstream MaxDps never hits
this: it guards with `MaxDps:issecretvalue` (vendor `Core.lua:81`,
wrapper over global `issecretvalue`) before touching such fields.

FIX (`addon/MaxDpsBridge/Reader.lua`, `Bridge.lua` — addon only, no C#
change needed; bridge v1.2.1, protocol still v2):
- `IsSecret` guard (upstream `MaxDps:issecretvalue` → global
  `issecretvalue` → conservative type fallback) + `IsPlainSpellID`
  (plain non-zero number, not secret) + public `MDB.IsSecret` for
  `Bridge.WriteSlot`.
- `HasCharges`: secret `currentCharges` → nil (UNKNOWN → cooldown path
  decides); secret recharge window → nil; only plain-number 0/≥1 decide.
- `CooldownReady`: `CooldownConsolidated` via plain `.ready` boolean
  only (`== true` / `== false` branches; never `not Info.ready` —
  a secret/nil would invert to true); raw fallback fails OPEN on any
  secret Start/Duration/GCD field.
- `IsSpellReady`/`IsInterruptReady` entry-guard secret IDs (UNKNOWN →
  encode EMPTY, never throw); interrupt cast check reads only the
  notInterruptible BOOLEAN (`type() == "boolean"`, secrets are never
  plain booleans) and discards the rest.
- All spellID plumbing guarded: `GetMainSpellID` (engine pick, NextSpell
  return, AC pick), `FirstFlagged` (skip-no-prune secret keys),
  `ItemSpellIDs`/`BestFlaggedItem`/`GetOffensiveSpellID` (plain-only),
  `SyncSet` hook args (secret → drop), `FindSpellOnActionBar`
  (secret IDs/names never `==`-compared), `TextureBinding`/
  `ResolveBinding`/`SpellFrameBinding` (secret → nil), `Diag`
  (counts secrets, never tostrings them; adds `secretKeys=`).
- `Bridge.WriteSlot` last-line guard: secret ID or pcall-wrapped
  readiness → encode EMPTY, never throw out of `Update`.
- `/mdb status` line made secret-safe (no `~=`/`tostring` on
  `MaxDps.Spell` under taint).
- Rule for future edits (also in the gate header): NEVER compare or do
  arithmetic on any value from `C_Spell`/`C_Item`/`GetActionInfo`/
  `UnitCastingInfo` without an `IsSecret` guard first; UNKNOWN always
  fails OPEN (encode EMPTY / treat as no-pick), never throws.

VALIDATED: `luac -p` clean on all 5 bridge files; addon re-installed
in-game (bridge v1.2.1 on disk); C# untouched (build/smoke still green
from the calfix session). Live verify owed: `/reload` → `/mdb status`
(no BugGrabber errors, `ready=MOIDNT` sensible) → Start → `sending`.

## Follow-up: 204x nil-call outage (bridge v1.2.2, same session)

SYMPTOM after `/reload` with v1.2.1: 204x `Reader.lua:244 attempt to call
a nil value` via `SyncSet ← hook ← GlowDefensiveHPMidnight ←
Retribution.lua:22 ← pcall ← GetMainSpellID ← Bridge Update`. Every
Paladin glow pass threw, so defensive/rotation slots never encoded.

ROOT CAUSE — my own load-order bug, not a game change: the secret-guard
fix defined `IsSecret`/`IsPlainSpellID` as file-`local`s AFTER the hook
section, but hooks fire DURING load — `GetMainSpellID` (called from
`Bridge.Update` on the first tick, and from `EnsureHooks` paths) runs
`MaxDps.NextSpell`, which calls `GlowDefensiveHPMidnight`, which fires
our `hooksecurefunc` hook, which calls `SyncSet`, which calls `IsSecret`
— still nil, because Lua resolves `local`s lexically and the `local
function IsSecret` line lower in the file was invisible at the call
site. Classic forward-reference trap (the Bridge.lua `Print` header
comment warns about exactly this pattern). `luac -p` cannot catch it —
syntax is valid; the nil only materialises at runtime on the hook path.

FIX (bridge v1.2.2, addon only): hoisted the entire secret-guards block
(`IsSecret` + `IsSecretFallback` + `IsPlainSpellID` + `MDB.IsSecret`
assignment) ABOVE the category-hooks section, with a header comment
stating the rule: guards used by hooks MUST be defined before the hook
section, and `MDB.IsSecret` must never move down (Bridge.WriteSlot
calls it cross-file). `SyncSet` simplified to `IsPlainSpellID` (the
indirection dance is gone — the function genuinely exists now).
Re-parsed all 5 files clean, re-installed in-game (disk confirms
v1.2.2 + SECRET GUARDS header).

FUTURE-PROOFING (Lua lexical-scoping rule — pin this): in WoW Lua,
`local function F` is visible ONLY below its definition line. Any
function called from a hook (`hooksecurefunc`, `OnUpdate`, `OnEvent`)
must be defined ABOVE the hook registration, or the first hook firing
during load calls nil. `luac -p` will NOT flag it. When adding new
bridge helpers, define-then-register, top-down: guards → engine →
hooks → readout → encode. And WITHIN the guards: forward-declare with
`local Name;` + `Name = function...` whenever helper A calls helper B
(the 287x outage: `IsSecret` called `IsSecretFallback`, but hooks fired
via `Options.lua:18 → GetMainSpellID → NextSpell → glow → SyncSet →
IsPlainSpellID → IsSecret → IsSecretFallback = nil` before the fallback
line executed — `Res=false`, `Value=20271`, plain-number path, boom).
SECRET-BOOLEAN rule (14x/52x Reader.lua:659 outages, bridge v1.2.3→v1.2.4):
`type(x) == "boolean"` is TRUE for secret booleans, and even a guarded
`not IsSecret(x)` + `x == true` pair THROWS — because IsSecret's OWN
verdict-compare (`Res == true`) detonated on the secret verdict first.
The v1.2.3 patch was therefore still broken (52x at the same line: the
`not IsSecret(NotInt)` call itself threw before ever reaching `==`).
Real fix (v1.2.4): `IsPlainBool` / `IsPlainBoolFalse` pcall-probes
defined ABOVE `IsSecret` and used BY it — the only throw-safe way to
read a maybe-secret boolean is compare-strictly-inside-pcall, and the
probes are the innermost primitive everything else builds on
(`IsSecret` verdicts, `NotInt`, `Usable`, `Flags` payloads). Dead
`IsSecretResult` helper removed. Standing rule, sharpened: NEVER bare-
`==` ANY value from a Blizzard API under taint — not even after
`type()` says boolean, not even inside a guard's own verdict path;
only pcall-probed results may be branched on. Load-order corollary:
the probes sit above `IsSecret` (same lexical rule as the 287x).

## v1.3.1 MAIN-SLOT FIX + 5-icon realignment (this change)

USER REPORT (Sep-2026, zero BugGrabber spam, link alive): companion
starts, cooldown actions fire, but the MAIN rotation never executes.
User's model: MaxDps shows 5 icons — 1 main rotation (core!), 2
offensives (often off-GCD), 3 defensives, 4 consumables, 5 trinkets.

ROOT CAUSE (two halves):
1. `GetMainSpellID` read ONLY `MaxDps.Spell` — stale between engine
   ticks and usually secret/tainted in combat (caught by pcall → nil →
   EMPTY). The RELIABLE live pick is `MaxDps.SpellsGlowing`
   (InvokeNextSpell → GlowNextSpell → GlowSpell sets
   `SpellsGlowing[id]=1`, GlowClear zeroes on change — upstream maintains
   it on its trusted path every tick). We never read it → main cell
   mostly EMPTY while cooldown Flags kept firing.
2. Companion `Priority` ordered Interrupt/Defensive/Offensive BEFORE
   Main — even a live main waited behind situational slots; with main
   usually EMPTY the engine never round-robined back to it either.

FIX (bridge v1.3.1, protocol v3; companion Main-first):
- `Reader.GetMainSpellID`: `MaxDps.Spell` fast path (pcall-wrapped gate)
  + `SpellsGlowing` scrubbed+contained fallback (glowing pick returned
  even when the gate says unready — gate forgiveness already applied; a
  glowing main withheld starves the rotation, a 50 ms-early press is
  free). Idle (no glow, no pick) still → nil → EMPTY + Idle (by design).
- Protocol v2 → v3 (SAME 9 cells, R 2→3): interrupt moved cell 3 → 6 so
  cells 1-5 read as the 5-icon model (main/off/def/cons/trin + int).
  R bump forces stale rejection both ends (the v1→v2 lesson). C#
  `Slot` reordered (Def 2, Cons 3, Trin 4, Int 5) + explicit V1ToV3
  remap (Int 3→5, Def 4→2, Cons 5→3); v2 frames rejected by R check.
- Companion `Priority`: Main FIRST, then Offensive/Interrupt/Defensive/
  Consumable/Trinket (round-robin still lets live interrupts preempt).
- Hero card: new "Show main rotation" row (Group A now 5 rows), card
  642 → 706px, window 860 → 924px, `SlotEnabled[0]` bound to the toggle
  (was force-true), v2-ini comment updated. `/mdb status` letters now
  M/O/D/N/T/I (5-icon order).
- VALIDATED: luac ×5 clean, build 0/0, smoke 0, snapshot (9 rows,
  status+buttons above fold), installed (disk: bridge v1.3.1).
  LIVE OWED: `/reload` → `/mdb status` (`v1.3.1 ... ready=MODNTI`,
  M uppercase in combat) → Start → main keypresses in `LastKey`.

## v1.3.2 STUCK-ROTATION FIX (this change — main pressed 1 key, stuck rest)

USER REPORT (Sep-2026, Paladin, zero Lua errors, link alive): main
fires E but sticks on 1/2/R and never casts Shift+E (SE); a mystery F
fires unprompted. Key insight (user): the overlay HotKey text IS the
binding — MaxDps transports the keybinds itself, no visual reading.

ROOT CAUSES (three, all fixed):
1. MAIN GATED INTO STARVATION: the v1.1.0 readiness gate (built for
   cooldown triage — skip-the-unready) was applied to MAIN, where the
   pick is USUALLY "unready" (just fired → GCD; pooling → no resource).
   Tainted verdicts + GCD-tail vetoes held every main EMPTY → "gets
   stuck on 1,2,R". FIX: MAIN encodes UNGATED — SpellsGlowing-first pick
   + WriteSlot SkipGate for cell 1. Upstream's glow IS the castability
   verdict (CheckSpellUsable + CooldownConsolidated ran trusted inside
   InvokeNextSpell); second-guessing with tainted reads only vetoed
   correct answers. Situational slots keep the gate. Wrong-main = 1 GCD;
   no-main = whole rotation.
2. SHIFT+E BINDING LOST: HotKey overlay path was first but brittle —
   uncontained GetText (tainted secret-wrapped strings), single-shot
   parse (exotic tokens aborted the button loop), and the bar-scan
   fallback (GetBindingKey drops modifiers on some bar addons: SE → E).
   FIX: HotKeyBinding runs contained + miss-tolerant (keeps scanning
   buttons); overlay bindText promoted to #2 for main; scan demoted #3.
   All four paths are MaxDps-transported data (no OCR, as the user says).
3. MYSTERY F: the Interact kill-switch (default key F, retail interact)
   — ` need-interact` state (melee range, CheckInteractDistance) fired
   the InteractKey even with the toggle OFF if... (unchanged behaviour,
   but now documented: F = InteractKey fallback on state 4, NOT a main
   spell. If F fires with Auto-interact OFF, paste `/mdb status` state
   + hero `LastKey` line — the status distinguishes Interact: F from a
   main bind).
- Q/E explicitly NOT movement keys (MovementGuard excludes them —
   standard spell binds; collision-skip still protects held keys).
- VALIDATED: luac ×5 clean, build 0/0, smoke 0, installed (disk:
  bridge v1.3.2). LIVE OWED: `/reload` → `/mdb status` (`v1.3.2 ...`,
  M uppercase) → Start → full rotation incl. Shift+E; F only with
  state 4 + Auto-interact ON.

## BASELINE v1.3.6 — first functional rotation combo (locked)

**This is the frozen first base: Companion + Bridge + MaxDps on retail
(Interface 120100 / Midnight 12.1). Build forward from here.**

Functional definition of "working baseline" (live-verified 2026-09-24):
- Rotation executes in order (main → offensive → defensive → consumable →
  trinket → interrupt), GCD-paced, no wasted presses, no stuck slots.
- Bridge emits protocol v4 (9 cells) with status flags (in-combat,
  on-GCD, has-target); companion honours all three.
- No Lua errors in combat (zero-taint bridge: no issecretvalue, no raw
  C_Spell arithmetic, arg-blind hooks, class-table category routing).
- Out-of-combat: toggle OFF = hard pause; toggle ON = attack once a
  target exists (never spams empty space).
- Interrupts execute when MaxDps recommends them.
- Versions: companion title `v1.3.6` (assembly-sourced), bridge
  `MDB.VERSION 1.3.6`, protocol 4.

Regression guardrails (do not break these in future changes): see the
"future-proofing" notes below — Lua lexical order (guards before hooks,
forward-declare helpers), companion decodes N and N-1 protocol, toggled-
off slot types are skipped never waited on, MAIN never re-gated by
tainted readiness, GCD + target + combat gates in that order.

## v1.3.9 PERFORMANCE AUDIT (this change — measured, not claimed)

Goal: lowest FPS/frametime impact on WoW from both processes, plus the
lowest achievable action latency. Research basis (cited in chat):
Blizzard's own guidance that unthrottled OnUpdate is the #1 addon FPS
killer; Bruce Dawson / Blur Busters on Windows timer resolution; MSDN /
SO on BitBlt-vs-GetPixel cost and PostMessage delivery.

### Measured (this machine, `--bench-sample`, evidence in dist\bench.txt)
| Path | Cost |
| :--- | :--- |
| `BitBlt` 72x8 SRCCOPY (the tick floor) | **4.16 ms/op** |
| same + CAPTUREBLT | 4.17 ms/op (no difference) |
| legacy `GetPixel` x45 on a screen DC | **187.5 ms/op** |
| new `Sample()` (9 cells, 5 taps) | 4.17 ms/op == the BitBlt floor |
So: the whole cost is the single screen read; pixel reads are now free
(pointer deref) where they used to be 45 syscalls, and the tick no longer
issues up to 3 BitBlts (v2 sample + v1 re-capture + calibrate re-capture).

### Companion changes (app 1.3.9)
1. **DIB-section capture** (`Native.CreateDIBSection`): one BitBlt into a
   memory-mapped buffer, taps read straight from the pointer. Before: 45
   `GetPixel` syscalls + 27 LINQ arrays per tick (GC pressure = frametime
   spikes). Now: 0 syscalls, 0 allocations in the hot path (reused tap
   buffers + insertion sort; static learner path kept allocating).
2. **One BitBlt per tick**: the stale-addon fallback and the calibrate
   probe derive the 8-cell window via `PixelProtocol.TrimToV1` instead of
   re-capturing the screen (was up to 3 captures/tick).
3. **Duty-cycle guard**: measured tick cost sets a cadence floor
   (`tick * 10`, capped 250 ms) so capture CPU stays ≤ ~10% on any display
   path — a slow capture degrades latency instead of stealing frames.
4. **Diagnostics off by default**: the slot summary + 72-char raw hex are
   only built while the Advanced popup is open (`WantDiagnostics`).
5. **Below-normal engine thread** — the render thread always wins.
6. **High-resolution waitable timer** (`CreateWaitableTimerEx`, 100 ns)
   instead of `Thread.Sleep`: no 15.6 ms granularity jitter AND no
   `timeBeginPeriod` (which raises the system-wide timer interrupt and
   costs the game CPU/power — deliberately avoided).
7. **Adaptive cadence**: fast when a target is present, 4x slower when
   idle (no game / no block / out of combat).
8. **Relocate sweep 2 s → 5 s**: the full-client-area scan is the most
   expensive single operation in the process; it self-heals just as well.

### Bridge changes (addon 1.3.9 — all inside the game's frame budget)
1. **Per-tick memo** (`MDB.BeginTick`): the six getters shared one
   `scrubsecretvalues(Flags)`, one item map and one class/spec resolve
   (was ~10 table copies + ~6 `UnitClass`/`GetSpecialization` lookups per
   tick — all Lua garbage the game then has to collect).
2. **Cached evaluation curve**: the readiness gate built a fresh
   `ColorCurve` per slot per tick (~6 objects + ~24 engine calls); now
   built once, lazily, and reused.
3. **No per-slot closures**: the `dropsecretaccess()` probe was an
   anonymous function allocated 6x per tick; now a named local.
4. **Strip refresh 50 ms → 33 ms (30 Hz)**: the sampling-latency floor
   drops ~17 ms. Affordable because the memoised tick costs a fraction of
   the old un-memoised 20 Hz tick; the OnUpdate early-out stays two
   arithmetic ops per frame, so 60–240 fps pays nothing.

### Latency budget (honest, this machine)
bridge 33 ms worst-case staleness + engine cadence (33 ms, auto-stretched
to ~42 ms by the duty guard here) + press. Both are configurable; the
guard self-tightens on machines with a cheaper screen read.

## v1.3.8 ADVANCED POPUP + NO-CLIP LAYOUT (previous)

User requests: keep the main design; give the setting bubbles more room so
no text is cut off; and turn Advanced from an in-place compartment into a
**popup dialog over the main frame** you can back out of.

- Advanced is now a **popup layer**: `BuildAdvancedOverlay()` adds a scrim +
  centred `RoundedCard` to the body canvas (added last = on top). Opening
  it hides the body layout (`_bodyLayout.Visible=false`) and shows the
  popup — a true screen flow, no z-order ambiguity. **Back** button and
  **Esc** (`ProcessCmdKey`) return to the main view. The old accordion,
  `AdvancedExtraHeight` and `ClampToScreen` are deleted; the "Advanced"
  text link became a full-width ghost **"Advanced…"** button row.
- No clipped text anywhere: main-card rows 60→66 (card 42 + 3×24 headers +
  9×66 + 24 = 732); every Advanced `RuleSection` re-measured to its content
  (Setup 190, Pixel bridge 168, Timing 124, Slots 122, Live suggestion 96,
  Targeting 196, Color 168, Battle.net 130) and the popup body scrolls, so
  captions/hints/checkboxes render in full.
- New snapshot hook: `--ui-snapshot-advanced=<png>` renders the popup for
  review (`MainForm.OpenAdvancedForSnapshot`).
- App **1.3.8**; bridge unchanged (still 1.3.7 code) — UI-only release.
- VALIDATED: build 0/0, smoke 0, main + popup snapshots reviewed (Setup /
  Pixel bridge / Timing rows fully legible, no ellipsis).

## v1.3.7 END-USER UI PASS (previous — design skills applied)

Brief: fewer words, narrower blocks, high-contrast group descriptions, no
debug readouts — end-user UI only. Applied the opencode design skills
(high-end-visual-design, apple-design, frontend-design) to WinForms:

- Copy: one short hint per row ("Core rotation", "Offensive cooldowns",
  "Potions", "Target when needed", "Interrupt casts"); removed sentence-y
  explanations and all system-speak (bridge/slot/COMBAT-ONLY wording).
- Structure: three labelled groups — Spells / Combat / Interrupt — in
  sentence case (an all-caps micro-eyebrow is a known generated-UI tell;
  sentence case reads as language, not chrome).
- Contrast: group headers now Bone (near-white) instead of muted Tidewash
  — the "higher-contrast group descriptions" ask; brass marker + hairline
  keep the grouping structural.
- Width: window 760 → 660, canvas padding 40 → 24, card padding 26 → 18,
  rows 64 → 60 — the blocks no longer stretch edge-to-edge.
- Debug removed: the 8-cell strip preview and the "link alive" lamp/label
  row are gone from the main UI; the hero dot + status word ("Sending",
  "Waiting for target") is the single end-user status. Advanced keeps the
  diagnostics.
- Motion: ToggleSwitch thumb is now a critically-damped spring
  (interruptible, transform/paint only, zero timer at rest) and snaps on
  the initial settings load (a control that animates itself on first paint
  looks broken — motion answers a user action).
- Versions 1.3.7 (asset unchanged bridge, companion UI); title auto.
- VALIDATED: build 0/0, smoke 0, snapshot reviewed, luac clean.

## v1.3.6 TARGET GATE (previous — "attack OOC ok, don't spam empty space")

USER follow-up on v1.3.5: out-of-combat attack is WANTED; the problem is
spamming with no target. And: with "Out of combat" toggled OFF it should
hard-pause until in combat.

- Bridge status bit2 = HAS_TARGET (UnitExists + alive + UnitCanAttack —
  plain unit booleans, never secret).
- Companion Tick order (after Paused, after process-lock):
  1. `CombatOnly && !InCombat` ⇒ hard pause "holding (out of combat)"
     (Out-of-combat toggle OFF).
  2. `!HasTarget` ⇒ "waiting for target"; if Auto-target is on, press
     TargetKey instead. No rotation into empty space.
  3. otherwise normal rotation + GCD gate.
- So: toggle ON = out-of-combat attack once targeted; toggle OFF = hard
  pause until combat. Both honour "no target ⇒ never fire".
- Versions 1.3.6 (csproj/bridge/toc/VERSION.txt), title auto.

## v1.3.5 OUT-OF-COMBAT PAUSE + GCD WAIT + INTERRUPTS (previous change)

USER (rotation now kicking in): (1) spams out of combat / no target —
pause if MaxDps recommends nothing; (2) wait for the GCD and press in the
10-100 ms availability window, max smoothness/min latency; (3) interrupts
not executing.

PROTOCOL v3 → v4 (9 cells, same layout): STATUS cell B (was reserved 0)
now carries NeverSecret flags — bit0 in-combat (UnitAffectingCombat),
bit1 on-GCD (C_Spell.GetSpellCooldown(61304).isOnGCD). Decoders updated:
status B is flags, not a zero guard. R 3→4 rejects stale frames.
- #1: bridge reports real combat state; companion holds everything
  (rotation + auto-target/interact) when `CombatOnly` (Out-of-combat OFF)
  and not in combat → "holding (out of combat)". The Out of combat
  toggle opts back in.
- #2: `TrySendOne` skips every GCD-riding slot while the bridge reports
  an active GCD; the moment the GCD drops the next tick presses the
  CURRENT suggestion (≤50 ms poll → inside the window). Interrupt
  bypasses the GCD gate (off-GCD kick still lands). Status shows
  "waiting for GCD".
- #3 interrupts: the arg-blind hook redesign (v1.3.0) had broken category
  routing — `FirstFlagged` claimed via `Set.__dirty`, so the Interrupt
  scan could return ANY flagged spell (a smaller-id defensive won) and
  the real kick never encoded; the Offensive scan excluded nothing
  (sets hold no keys). FIX: category now read from upstream's STATIC
  class tables (`classInterrupts` / `classCooldowns.defensive|offensive`)
  — exact, plain data, never secret. `CategoryOf` + `FirstFlagged(cat)`.
- Version 1.3.5 everywhere, title bar auto (assembly-sourced).
- VALIDATED: luac ×5, build 0/0, smoke 0, snapshot `v1.3.5`, installed
  (disk: bridge v1.3.5, protocol 4, CategoryOf present). LIVE OWED:
  out of combat idle (no spam); in combat R→SE rides GCD (no double
  presses, no "waiting" stalls); interrupt fires on an interruptible
  cast; set Interact key to MB5 in the box if auto-interact wanted.

## v1.3.4 MELEE-STATE FIX + version single-sourcing (previous change)

USER LIVE TEST (screenshot): R clearly recommended in the overlay, the
companion spammed F and never sent R; still stuck with interact toggled
OFF; user's rule — toggled-off types must be SKIPPED, never waited on.

ROOT CAUSE (the real one, finally): `Bridge.TargetState()` returns
`NeedInteract` (4) whenever `CheckInteractDistance(target, 3)` is true —
which is TRUE FOR EVERY IN-MELEE TARGET. The old Update let that state
OVERRIDE Active (`if State == nil then ...`), so for a melee class the
companion saw state 4 on essentially every combat frame, held the whole
rotation, and (with auto-interact on) fired InteractKey every frame —
the F spam; with auto-interact OFF it just held ("still stuck"). R was
never sent because the frame was never Active.

FIXES:
- Bridge: **a live suggestion ALWAYS wins the state** — `AnySlot ⇒
  STATE_ACTIVE`; need-target/need-interact only when no slot is encoded.
  Target/interact states exist solely to ask for a target/interact when
  MaxDps has nothing to cast.
- Companion: belt-and-braces — on any non-Active frame, if the frame
  carries **enabled** slots, run the rotation FIRST and return; auto-
  target/interact only when the frame has no enabled slot.
- Companion: `AnyEnabledSlot` replaces `AllSlotsEmpty` for that gate —
  toggled-off types are SKIPPED, never waited on (user's explicit rule).
  A frame whose only suggestion is a disabled type now reads "holding
  (no suggestion from MaxDps)", not a stall.
- Version single-sourcing: title bar now `MaxDPS Companion v{Native.AppVersion}`
  read from the csproj assembly identity — the visible version can never
  drift from the build again (was a hardcoded string). csproj → 1.3.4,
  bridge/toc/VERSION.txt → 1.3.4, repo VERSION.txt → 1.3.4.
- VALIDATED: build 0/0, smoke 0, luac ×5, snapshot shows `v1.3.4` in the
  title bar. LIVE OWED: engage in melee → R/main fires in order (no F);
  set Interact key to `MB5` (box, not in-game) if auto-interact wanted.

## v1.3.3 STUCK-ROTATION + F-ON-ENGAGE (previous change — send-path fixes)

USER LIVE TEST (Paladin, zero Lua errors): F fires on every engage;
rotation sticks on R, manual R advances exactly one step then sticks on
Shift+E (SE). Required: full rotation in order, max perf, min latency.

ROOT CAUSES (all in the SEND path, none in the pixel path):
1. PER-SLOT GAP MACHINE-GUN: TrySendOne gated each slot on its own
   `_lastSlotPress[index]` AND advanced `_rotation` past the sent slot.
   R stayed valid across ticks → R,R,R… while SE's slot never became
   "due" and the rotation offset cycled past the fresh suggestion.
   FIX: single global GCD gate (`_lastAnyPress` only), always scan
   Priority from [0] (Main first — the frame IS the order), no
   round-robin advance. Poll 50 ms / gap 120 ms ≈ 2 idle ticks/press,
   near-zero CPU, GCD-paced sends (lowest latency WoW accepts).
2. F-ON-ENGAGE = STALE INTERACT DEFAULT: `dist\settings.ini` still held
   `InteractKey=F`; user bound Interact to MB5/Alt+MB5 but never typed
   it into the box, so need-interact (melee range on engage) fired F.
   FIX: TrySendInteractKey takes the frame + refuses any InteractKey
   stroke that EQUALS a live spell stroke (same VK+mods) with an
   explicit hold-note ("shadows a spell bind — change it"); parse
   failures surface LastParseError instead of silent hold. (MB5/Alt+MB5
   parsing itself shipped in the previous change.)
3. DEAD _mainSlot DIVERGENCE: Advanced → Slots kept a disabled
   "Main (always on)" checkbox force-checked while the card toggled
   SlotEnabled[0] — two sources of truth. FIX: checkbox enabled +
   two-way mirrored with the card row (same pattern as auto-target).
- VALIDATED: build 0/0 (retry warnings = live game-adjacent exe lock,
  not code), smoke 0, luac ×5 clean, snapshot (10 rows incl. main,
  status+buttons above fold), installed. LIVE OWED: type MB5 into
  Interact key → `/reload` → engage → full R→SE→… rotation, no F
  unless state 4 + Auto-interact ON (`LastKey: Interact: Mouse5`).

## Mouse-button InteractKey (user: Interact on MB5 / Alt+MB5)

`TryParseTargetKey` used WinForms `Keys` only — "MB5"/"Alt+MB5" FAILED
to parse, so TrySendInteractKey returned false EVERY need-interact
frame: silent dead fallback (the F mystery from the other side — F
never fired because the parse died before any send). Fixed C#-side
(addon untouched — InteractKey never crosses the pixel protocol):
- `MovementGuard.MouseNames` (MB1-5/Button1-5/Mouse1-5/XButton1-2/LMB/
  RMB/MMB/M3/M4/M5 + wheel names) → same VK codes as Keymap.lua
  (0x01/0x02/0x04/0x05/0x06/0x07/0x0B); `TryParseTargetKey` accepts
  "MB5", "Alt+MB5" etc. with Shift/Ctrl/Alt prefixes.
- Engine: mouse InteractKey takes the `KeySender.Send` (SendInput)
  branch — modifiers ride as keyboard input in the same batch
  (Alt+MB5 = Alt-down + XBUTTON2 + Alt-up). Physical-hold skip applies
  to keyboard only (holding MB5 for push-to-talk must NOT deadlock the
  fallback). Foreground gate still mandatory (no per-window mouse
  route) — background MB5 interact is impossible by OS design.
- `KeySender.DescribeStroke` (mouse-aware; KeyNames lacks mouse codes)
  for `LastKey` ("Interact: Alt+Mouse5"); TargetKey path same fix free.
- UI: Targeting caption + InteractKey tooltip document mouse names;
  key boxes widened 80 → 110px ("Alt+MB5" fits).
- VALIDATED: build 0/0, smoke 0. LIVE OWED: set InteractKey `MB5` (or
  `Alt+MB5`), game FOCUSED (foreground gate!), Auto-interact ON →
  need-interact (melee range) → `LastKey: Interact: Mouse5` and the
  character interacts.

## v1.3.0 ZERO-TAINT REDESIGN (previous change — ends the wave, not a patch)

ARCHITECTURE RESEARCH (MaxDps internals + Midnight API model):
- MaxDps computes everything on its OWN trusted path: `InvokeNextSpell`
  (Core.lua:731) runs on game events (login/combat/target), writes the
  verdict into `MaxDps.Spell` + per-spell `MaxDps.Flags[spell]=true`
  (Buttons.lua glow functions). `GetMainSpellID` re-invoking `NextSpell`
  was REDUNDANT — the answer already sat in `MaxDps.Spell` — and each
  re-invocation replayed glow calls (secret Duration/curve reads) under
  OUR taint. Deleted: take `MaxDps.Spell` when plain non-zero, else nil.
- Midnight's documented model: secrets are black boxes — tainted code
  may RECEIVE and PASS them into engine APIs (StatusBar, ColorCurve,
  Duration) but must never BRANCH on them. `C_Spell.GetSpellCooldown`
  marks isActive/isEnabled/isOnGCD **NeverSecret** (wiki-documented);
  `C_Spell.GetSpellCooldownDuration` returns Duration objects with
  engine-side `EvaluateRemainingDuration` (upstream Buttons.lua:1094
  already uses exactly this for glow alpha); `scrubsecretvalues` /
  `dropsecretaccess` are the documented containment APIs.
- The 260x (`IsSecretFallback` nil at :94 ← :83 ← :126 ← :370 ←
  Options.lua:18 panel build) proved the regress is STRUCTURAL:
  issecretvalue() verdicts arrive tainted → branching needs a guard →
  guarding the guard needs another guard → every layer adds load-order
  nil-call surface. Patching further = new wave every reload.

REDESIGN (bridge v1.3.0, addon only, protocol still v2):
- `issecretvalue` NEVER called — all guards/probes deleted (no regress,
  no load-order surface; leftovers fail LOUD via nil, caught by luac).
- Arg-blind hooks: `SyncSet` ignores its spellID arg (may be secret),
  sets only `Set.__dirty`. WHICH spell re-derived from `ScrubbedFlags`
  (`scrubsecretvalues(MaxDps.Flags)`, secrets→nil) — iteration over the
  clean table cannot observe a secret.
- Readiness: `CooldownConsolidated` verdict via scrubbed `.ready`
  (same helper/forgiveness as v1.1.0) → NeverSecret
  isActive/isEnabled/isOnGCD → Duration-object remaining ≤ 0.5 s tail →
  fail-open. NO raw startTime/duration/charges arithmetic anywhere
  (HasCharges now returns nil — charges folded into .ready upstream).
- `IsSpellReady` entry = pure-Lua type check (scrubbed inputs).
- `IsInterruptReady`: NO UnitCastingInfo call (all returns tainted);
  upstream's glow verdict (dirty + flagged) IS the live-cast verdict +
  UnitExists target-token check (strings, never secret).
- Binding/diag/status paths: `FindSpellOnActionBar` + `Diag` + `/mdb
  status` run under dropsecretaccess() containment; `Bars.ButtonTexture`
  likewise. `WriteSlot` keeps its pcall (per-slot EMPTY, never frame
  abort) minus the deleted IsSecret call.
- VALIDATED: luac clean ×5, installed (disk: v1.3.0 + ZERO-TAINT +
  Scrubbed + dropsecretaccess); C# untouched. LIVE OWED: `/reload` →
  zero BugGrabber lines expected (no guard left to nil-call, no secret
  left to compare) → `/mdb status` → Start → `sending`.

## Follow-up: shipped the REAL v1.2.2 + idle-by-design (same session)

The 97x `Reader.lua:68 attempt to call a nil value` was the SAME
forward-reference bug wearing a different line number: the repo file had
accumulated a DUPLICATE guards block (one above hooks at :60, a second
copy at :281), and the installed in-game copy was an older revision
(still guards-below-hooks). One `install-addon.ps1` + `/reload` cycle
had shipped v1.2.1's layout, not the hoisted one — so the user's client
kept executing the old order. Lesson: after ANY bridge edit, reinstall
+ `/reload` before reading further BugGrabber lines; stale-client errors
masquerade as new bugs. Deleted the duplicate block (single canonical
guards section above CATEGORY HOOKS), re-parsed, rebuilt the exe,
re-installed (disk verified: guards-once at :60, `MDB.VERSION 1.2.2`).

IDLE-BY-DESIGN (user correction, researched + confirmed): MaxDps shows
NO spell when it has nothing to recommend (idle out of combat, resource
pooling, proc waits — overlay dark, Flags empty). This was already
encoded correctly as all-slots-EMPTY + state Idle, but the companion
reported it as bare "holding", indistinguishable from our own holds
(movement-key/focus/gap). Changes:
- `Bridge.lua`: comment pins Idle-with-empty-slots as correct, not a
  failure; TargetState overrides still apply (auto-target/interact).
- `RotationEngine`: Active frame with zero encodable slots now reports
  "holding (no suggestion from MaxDps)" via `AllSlotsEmpty`, distinct
  from gate holds. No behaviour change — hold either way, never
  synthesize a press.

## Outstanding (needs retail run)

1. `/reload`, `/mdb status` → expect `v1.2.0 ... ready=MOIDNT`
   (M=Main, O=Offensive, I=Interrupt, D=Defensive, N=coNsumable,
   T=Trinket; uppercase = ready/encoded, lowercase m = suggested-but-
   unready, `-` = none).
2. Start → `link alive`, `sending`; verify it skips a cooling spell and
   fires the next ready slot instead of hammering one key.
3. Toggle each hero row and confirm the matching slot stops firing;
   confirm potion vs trinket fire independently.

## Post-release series (ac84240 → v1.1.0)

Perf/sampler: 1px→8px strip parity with Aethys, centre-pixel/median sampler,
drift-free loop, change-gated UI repaint, narrow-window handling, MinCell 2,
child-stamp.

Engine/readout: `EnsureEngine` demand-loads the class module + Fetch so idle
status answers; live `NextSpell` fallback + `next=` diagnostic; retail
action-bar path for the main spell + `FrameData` prep + class glow pass;
`LEARNED_SPELL_IN_TAB` event; guard class-function calls on `FrameData`.

Binding/diagnosis: `/mdb diag` bar-binding diagnosis, ElvUI dash-less HotKey
(`CQ`), probe real frames not addon presence, repaired corrupted merge in
`Reader.lua` (error-spam source), fixed `DiagPrint` ref.

Calibration UX: one-click Recalibrate hero flow, resizable fitted window,
calibrate-progress owns hero line, live anchor counter, auto-cal watchdog +
silent chat + settle + Verify clamp + alive ping, calibrate-on re-enables
bridge (stale paused strip), chat-command key discipline (Enter+Ctrl+V only).

Later reverts (HEAD `bef1acc`→`7cf7f63`): removed watchdog+alive (strip moved
mid-session), true anchor counter, always-sweep.

## Outstanding (needs retail run)

1. `/mdb status` in game + strip decode (`link alive`).
2. `Calibrate colors` learn + saved profile separation.
3. Main/CD/Interrupt/Defensive key presses in-game; AutoTarget/Interact
   kill-switches default OFF.
4. Live E2E on the shipped exe: the last three commits (`27eecd8`→`7cf7f63`)
   were mid-session debug fixes and have not been re-verified in a live client.

## Perf profile (current)

- Strip 8x1 px (Aethys parity), paint-cached Lua, 20Hz update; sampler 8x1
  BitBlt + centre-pixel read (~1ms/tick), drift-free sleep pacing (~2% core);
  UI 250ms timer with change-gated Invalidate (zero repaint at rest).

