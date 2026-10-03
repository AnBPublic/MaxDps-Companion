# Testing

Two layers are fully offline (no game required); the third layer is a live
retail checklist and is explicitly OWED, never claimed from the offline runs:

## 1. C# deterministic suite — `tests/MaxDpsCompanion.Tests`

```powershell
cd tests\MaxDpsCompanion.Tests
dotnet test -c Release
```

571 tests, all fake-clock (no wall time, no I/O except temp files). Groups:

| File | Covers |
| :--- | :--- |
| `PixelProtocolCompatTests` | v4/v1 decode, slot remap, same-length version rejection, checksum, TrimToV1 |
| `AbilityCoverageTests` | v2.7 ownership/completeness axes, mandatory delegation + manual reasons, patch metadata, stale/newer-patch audit violations, manifest consistency and JSON classes |
| `PixelProtocolV5Tests` | v5 wire format (35 cells): fields, spell-id boundaries, both checksums, tear detection, range/buff/class-spec decoding, TrimToV4, context projection |
| `AbilityCatalogTests` | catalog load, curated overrides, extras validation, wire-id round-trip, internal consistency, `GcdVerified` conservatism, generated-Lua sync |
| `PolicyEvaluatorTests` | USE/HOLD/SKIP scenarios: main responsiveness, cast/channel execution safety (incl. the verified-off-GCD / gap-closer exemptions), Spell Reflection enemy-cast gate, interrupt vetoes (not-interruptible / stale / unknown), defensive thresholds + sequencing + escalation, gap-closer positioning, solo self-sustain + ability ceiling + overheal + never-automatic + melee range/target guards + immunity guard, unknown-context fallbacks |
| `CandidateProviderTests` | v2.7 §51 decision matrix per category (self-sustain healthy/moderate/low/critical + Normal/Solo + overheal, defensive White/Yellow/Orange/Red/Unknown + curated minimum + MaxDps discount + gap-fill, interrupt no-cast/interruptible/non-interruptible/range/channel, offensive paired window/buff/enemy-count/movement, mobility melee/outside/unknown/invalid, utility manual/ON/OFF) with provider + candidate-source assertions |
| `ProviderPropertyTests` | catalog-wide properties: every ability resolves a provider; OFF never Use; Utility never Use; manual/ON scopes; no evidence string leaks; unknown-context fallbacks pinned |
| `ActionSchedulerTests` | link loss, protocol gate, priority, duplicate collapse, stale demotion, GCD, pacing, repeat suppression, unavailable suppression |
| `ActionSchedulerPolicyTests` | policy filtering + ranking, emergency survival above main, mobility below main, companion-slot gating, trinket lockout, failed-press rejection, GCD confirmation, escalating retry backoff, suppression not blocking other ranks, channel/cast holds (policy on and off), pending-confirm demotion, stale-interrupt main continuation, range revalidation |
| `SelfSustainEndToEndTests` | real v5 wire frame → decode → tracker → scheduler: Impending Victory generated, policy USE, `SelfSustain`/`EmergencySurvival` selection, GCD confirmation and return to main; healthy/cooldown/out-of-range/no-target/casting/GCD/conservation negatives; Solo OFF; Intelligence OFF; bounded failure recovery |
| `SelfSustainReplayTests` | canonical Solo recording replayed with 0 verdict mismatches (in-memory + the checked-in `fixtures/solo-warrior-selfheal.jsonl` used by `--replay`) |
| `SelfSustainCooldownTests` | r2 reset-aware sustain: a ready heal is never stale-demoted, a transient failed press is capped at 1.5 s with no escalation, the distinct `SelfHealCoolingDown` wait is reported, and an optional curated `resetHint` is parsed (informational only) |
| `SoloCooldownResetReplayTests` | r2 canonical cooldown/reset recording recomputes every verdict with 0 mismatches (in-memory + the checked-in `fixtures/solo-cooldown-reset-warrior.jsonl`) |
| `OffensiveGapFillTests` | r1 offensive gap-fill: curated per-spec lists, source derived by id membership, combat/Solo gate, registry enforcement, and the unchanged reason strings for MaxDps-sourced rows |
| `OffensiveGapFillReplayTests` | r1 canonical offensive-gap-fill recording recomputes every verdict with 0 mismatches, with `cls`/`spec` rebuilt from telemetry (in-memory + the checked-in `fixtures/offensive-gapfill-warrior.jsonl`) |
| `DefensiveIntelligenceTests` | additive v5 protocol decode (urgency/source/stagger + reserved-nibble and extension-checksum coverage), the urgency policy matrix (White hard hold, Yellow/Orange/Red tiers, the MaxDps-recommendation one-stage discount, curated Ignore Pain Orange, stagger override + HP fallback), emergency override, user policy (OFF absolute, Vanish default-OFF and emergency-only ON), scheduler integration (Red selects, White holds and main continues, Orange gap-fill major held but minor fires, OFF reported as a skip) and failed-press recovery |
| `DefensiveReplayTests` | canonical defensive recording recomputes every verdict with 0 mismatches (in-memory + the checked-in `fixtures/defensive-warrior-urgency.jsonl`), a failed defensive press is rejected, and a policy record with no `du` is treated as legacy pre-v2.3 (verdicts skipped, 0 mismatches, report says so) |
| `ClassSpellBookTests` | token name decoding (incl. connector/alias cases), the junk/passive filter, merge provenance and priority (curated/vendor win over class-spells), live-client verification (official name/icon, a removed id not merged), `DefensiveGapFill` exclusion of the modeled layer, shared-vs-per-spec tree, all-class coverage, and a Main-spell opt-out through `AbilityPolicy` OFF |
| `ClassSkillsViewTests` | STA window construction of the Class skills screen: the dropdown preselects the class/spec, rows are generated for every class, icons optional |
| `UiShellTests` | v2.7 shell construction (STA): navigation + rail active state, explorer filter/search, coverage report rendering, smoke validation returns no findings |
| `SpellIconCacheTests` | download-once + disk-cache pipeline (exactly two requests), the official-slug path (a single request), offline degradation to the placeholder, hostile icon-slug rejection, invalid ids ignored |
| `DecisionEngineTests`, `CandidateTrackerTests` | legacy decision layer + identity-aware tracker (stroke OR spell-id change); the enabled order inserts the companion slots without reordering the MaxDps slots |
| `AppSettingsTests` | `[Intelligence]`/`[Solo]` defaults, parsing, clamping, round-trip |
| `TelemetrySerializationTests`, `TelemetryRingBufferTests`, `TelemetryReplayTests` | JSONL format (incl. the policy context + send spell id), ring bounds, legacy replay determinism, and policy-verdict replay: exact recompute, live send-before-tick order, tamper detection, trinket lockout from sends |

The class-spell verification and official-slug icon tests are part of this
suite (`dotnet test`) and stay offline: they read the embedded
`spell-verification.json` and use a stubbed `HttpMessageHandler`, so the live
wago.tools fetch is never exercised by the test run.

## 2. Lua addon harness — `tests/secret_harness.lua`

```powershell
lua tests/secret_harness.lua
```

153 checks. Stubs the WoW API with Midnight secret semantics (values that
throw on compare/arithmetic), loads Catalog + Keymap + Reader + Bridge, fires
`ADDON_LOADED`, and drives `Update` directly. Covers:

- cooldown shapes (active / GCD-only / none / legacy), secret spell ids,
  charge handling, Duration-object containment;
- interrupts never calling `UnitCastingInfo`/`UnitChannelInfo`, and the
  v2.1 interruptibility veto (NOT_INTERRUPTIBLE ⇒ empty slot; unknown ⇒
  fail open);
- the full v5 encode (additive urgency nibbles): version/checksums/commit, slot + spell-id round-trip
  (Main 185358, curated Charge 100), vitals, target state, class/spec ids,
  the disabled path (valid Paused frame), and arg-blind player/target cast
  events (start/stop/channel/interruptible), target-change and channel-stop
  clears, the 15 s missed-STOP watchdog, and the
  `C_SpecializationInfo`-first class/spec resolution;
- defensive urgency (v2.3): the HP curve stages at ≤30 / <50 / <100 / 100%,
  the Purifying Brew stagger curve with its HP fallback, the Defensive
  MaxDps-first / Red-only gap-fill candidate + source bit, and every probe
  contained (a secret/nil stagger or HP reading encodes 0 = UNKNOWN);
- offensive + defensiveMinor gap-fill (r1): a MaxDps-named offensive wins and
  the curated `offensive` list is offered only when MaxDps names none (inside
  `enableCooldowns`); the `defensiveMinor` list is offered at the Orange tier
  while majors stay Red-only;
- reset-aware self-heal readiness (r2): a keybind memo is invalidated on
  cooldown/talent/spec events so a reset re-offers the same heal, and readiness
  is re-read every tick (never cached);
- getter-throw containment (`SafeRead`: frame survives, warns once);
- self-sustain extras (v2.2): a ready-but-unbound self-heal stays empty, a
  bound one encodes (VK + spell id) and the slot-8 range probe is encoded;
- self-buff tri-state (v2.7): without `AuraUtil` the block-valid bit (cell 33
  B bit1) stays clear and no buff bit is a silent "active"; a working probe
  sets the bit per slot while class/spec validity is preserved; a throwing
  probe clears the bit again.

## 3. Live in-game E2E (retail) — OWED

Static (parses/builds) ≠ automated test ≠ live validation. The following are
**required** in a real client and cannot be automated offline:

1. `/mdb calibrate on` → Recalibrate → v5 pattern learned (35 cells).
2. `/mdb status` shows `protocol=5`, class/spec/extras sensible.
3. In combat: main rotation fires at the normal cadence; interrupts land;
   a defensive emergency preempts a live main.
4. Housekeeping checks: stop the addon (`/reload`) → link holds after
   ~500 ms; restart → resumes.
5. Policy spot-checks with Intelligence ON: Spell Reflection holds with no
   cast and fires with a cast; Charge not pressed while already in melee;
   a major defensive waits while a minor is running; Solo mode self-heals
   below 65% HP and conserves above it.
6. Execution safety (v2.1): while hard-casting, no GCD-riding key is sent
   (kicks/items still land); a channel is never clipped, not even by main.
7. Interruptibility (v2.1): against a NOT_INTERRUPTIBLE cast MaxDps must not
   produce a kick (watch `/mdb status` + the plan reason); an interruptible
   cast still kicks normally.
8. Recovery (v2.1): a suggestion that keeps failing (e.g. an immune target)
   must decay its retry rate (1.5 s → 10 s) and never block the main
   rotation; recording a live session and replaying it must report
   `0 mismatch(es)` for both decisions and policy verdicts.
9. Class skills (v2.4): open **Class skills** in-game and pick the live
   class/spec; confirm the shared section carries the class-wide abilities
   once and the per-spec Main rotation / Offensive / Defensive / Movement
   sections carry the rest, and that toggling one ability OFF stops the
   companion using it (diagnostics/plan reason shows the skip) while ON
   restores the normal gates. Icons download once and stay cached across
   restarts; offline, placeholder tiles still let the screen work.

### 3a. Warrior Solo / Impending Victory acceptance (live, OWED)

The offline proof is `SelfSustainEndToEndTests` + the canonical replay
(`tests/MaxDpsCompanion.Tests/fixtures/solo-warrior-selfheal.jsonl`, 0
verdict mismatches via `--replay`). The following must still be observed in a
real client (Solo ON + Intelligence ON, Impending Victory talented AND on an
action bar):

1. Enable Intelligence; enable Solo; confirm Impending Victory is bound.
2. Enter combat with a valid target; take controlled damage to ~50% HP.
3. Watch Advanced → Diagnostics / telemetry: the SelfHeal slot carries
   `Heal: H`, the plan head is `SelfHeal: SelfSustain (75%)` with verdict
   `Use "solo: HP 50% below sustain 65%; self-sustain"`.
4. Confirm the key is actually sent (hero `LastKey` / status "sending" /
   the heal lands and HP rises).
5. Confirm the next frames are `GcdHold`, then the heal slot goes empty
   (cooldown) and `MainRotation` resumes — no repeat presses.
6. At ~90% HP the verdict must be `Hold` (conserve); at/below 35% the plan
   head must be `SelfHeal: EmergencySurvival`.
7. Turn Solo OFF at low HP: the verdict becomes
   `Hold "solo mode off; self-heals are not automatic"` and the normal
   rotation continues unchanged.
8. No spam: after a successful heal, no second press until the cooldown
   expires; if a press fails (out of range/immune), the retry decays
   (1.5 s → 10 s) and the main rotation keeps running.

Variants to observe: low HP + offensive cooldown available (no heal waste
above threshold); low HP + defensive available (defensive emergency may
preempt, one action per tick); low HP + active cast (CastHold, no key);
low HP + active channel (ChannelHold); low HP + target out of melee
(Skip on the slot-8 range probe); ability on cooldown (no candidate);
insufficient resource (bridge does not encode, no candidate).

### 3b. Defensive intelligence acceptance (live 12.1 retail, OWED)

The offline proof is `DefensiveIntelligenceTests` + `DefensiveReplayTests` +
the canonical replay (`tests/MaxDpsCompanion.Tests/fixtures/defensive-warrior-urgency.jsonl`,
0 verdict mismatches). The following must still be observed in a real
Midnight 12.1 client (Intelligence ON; the Defensive slot enabled; a
defensive actually bound to a key):

1. Reinstall the addon (`install-addon.ps1`) and `/reload`; `/mdb status`
   must show `protocol=5`. **An updated addon must still decode with an older
   companion exe** (the wire version nibble stays 5 and the additive urgency
   nibbles are ignored by the old decoder — the Sep-2026 "no abilities
   execute" outage class); likewise a stale pre-2.3 in-game addon must still
   decode into the current companion (urgency UNKNOWN) and downgrade
   gracefully rather than error.
2. Bind a short-CD defensive (e.g. a minor) and a major. At full HP the
   bridge reports White urgency: a ready defensive must **hold** (no press)
   and the main rotation must continue.
3. Drop to ~40% HP (Yellow band) with a short-CD defensive ready: the
   short-CD defensive fires; a major held only on the tier default must not
   fire.
4. Drop to ~40% HP (Orange band) with a major ready: a major that MaxDps is
   **not** currently recommending must hold; a major that is the exact
   Defensive-slot MaxDps recommendation may fire (one-stage discount). Watch
   the plan reason to confirm which path was taken.
5. Drop to ≤30% HP (Red band): a ready major fires (MaxDps recommendation
   not required); the gap-fill path may supply a defensive only when MaxDps
   names none, and only at Red.
6. Set Vanish (and any other escape) to OFF in the generated "Defensive
   automation" list: it must never fire, even at Red or at emergency HP. Set
   it to ON and confirm it is still emergency-only (Red or emergency HP).
7. Toggle a defensive OFF and watch Advanced → Diagnostics / telemetry: the
   verdict must read `Skip "user policy disabled"`; toggle it back and the
   regular urgency verdict returns.
8. Record a live session and replay it: the report must say
   `0 mismatch(es)` for both decisions and policy verdicts. Replay a
   recording made before v2.3 (no `du` field) and confirm the report says
   `legacy pre-v2.3` and skips those verdicts (0 mismatches, not a defect).

Honest limits to observe, not bugs: only one defensive candidate is offered
per tick; the gap-fill only runs when MaxDps's `enableDefensives` switch is
on; enemy cast identity/importance remains unobservable (reflect still fires
on "a cast is live"); and no live result can be claimed from the offline
suite.

### 3c. v2.6 live validation (retail 12.1, OWED)

OFFLINE VERIFIED (this machine, 2026-09-28 - do not re-claim live): `dotnet
build -c Release` 0 warnings/0 errors; `dotnet test` 361/361;
`lua tests/secret_harness.lua` 88/88; `--ui-smoke-test` exit 0;
`--bench-scheduler` unchanged sends=1620 (1.82/s)
sha256=b71a999d5e46570e; replays defensive 16 verdicts/0 mismatches, solo
9/0, offensive-interrupt 7/0, sample legacy 0/0;
`tools/ability_audit.ps1` exit 0, Violations 0 / Warnings 0.

LIVE WOW VERIFIED (owed - observe in a real client, Intelligence ON):

1. Reinstall the addon (`install-addon.ps1`) + `/reload` (Catalog.lua
   revision 3); `/mdb status` shows `protocol=5`.
2. Open Class skills in-game: rows show the status label (Verified /
   Research-backed / MaxDps-backed / Companion rule / Safety rule /
   Heuristic / Manual by design / Unsafe / Incomplete / Unknown) + tier +
   cooldown; manual rows are muted/italic; the why-tooltip explains
   use/hold/skip; the legend line renders.
3. White/Yellow/Orange/Red behaviour unchanged (§3b steps 2-5).
4. Normal-mode emergency self-heal: Warrior with Impending Victory bound,
   Solo OFF, HP below 35% - the heal fires and outranks main; above 35% it
   holds ("solo mode off; self-heal held above emergency HP"); Solo ON
   restores the wider sustain layer (§3a).
5. One interrupt per class: correct kind/timing (Dedicated vs Silence 15487 /
   119910); never blind spam - only while a live interruptible cast is
   observed.
6. One major offensive per spec: holds when the paired ConflictGroup window
   is active; fires when MaxDps surfaces it outside the window.
7. Mobility: a gap closer fires only when confirmed out-of-melee + in-range;
   escapes / teleports / speed bursts never fire automatically.
8. Utility (CC / purge / threat / dispel) never fires automatically, ON or OFF.
9. ON/OFF persistence incl. Modes round-trip: toggle OFF (never fires),
   toggle ON (eligibility only), set a Mode and confirm it survives a restart.
10. `--ability-info=<spellId>` vs the live spell check (name / cooldown /
    status sane).
11. Record + export + replay a live session: 0 mismatches for decisions and
    policy verdicts.

### 3d. v2.7 live validation checklist (retail 12.1, OWED)

Offline evidence is NEVER live proof. Mark every line below on the machine
that ran retail: `LIVE VERIFIED` / `LIVE UNVERIFIED` / `LIVE FAILED`.
OFFLINE VERIFIED (this session, do not re-claim live): build 0/0;
`dotnet test` 571/571; `lua tests/secret_harness.lua` 153/153;
`--ui-smoke-test` PASS (structural checks); `--bench-scheduler` sends=1620
sha256=b71a999d5e46570e; 7 replays 0 mismatches (solo 9, defensive 16,
offensive-interrupt 7, solo-hidden-hp 6, cooldown-reset 8, offensive-gapfill,
ttk-warrior-burst 18);
`tools/ability_audit.ps1` exit 0 (Violations 0 / Warnings 0 / Missing 0 /
Stale 0; addon Catalog.lua matches).

1. Reinstall the addon (`install-addon.ps1`) + `/reload`; `/mdb status` shows
   bridge 2.7.0, `protocol=5`, sensible class/spec/extras.
2. Calibrate (`/mdb calibrate on` -> Recalibrate) and confirm the strip
   decodes (Home shows connected + bridge state).
3. Home answers the three questions at a glance: connected? what is it doing
   (current/last action + why)? what will it do automatically (mode,
   automation, coverage counts)?
4. Abilities Explorer: search a name and an id; filter chips change the list;
   select a row and verify the inspector matches `--ability-info=<id>`
   (owner / completeness / delegation or manual reason / patch / source).
5. Toggle one ability OFF in the explorer, confirm `[Abilities] Off=` persists
   in `settings.ini` and the action stops; toggle it back ON.
6. Normal mode (Solo OFF): Warrior + Impending Victory bound + HP below 35%
   fires ("emergency self-sustain") and outranks main; above 35% it holds.
7. Solo mode: the wider sustain layer fires below the sustain threshold and
   respects the overheal guard; Solo OFF removes the independent press.
8. Defensive urgency: White holds; Yellow fires only a short-CD; Orange holds
   a major; Red fires the MaxDps-recommended defensive; a Red gap-fill never
   fires above Red.
9. Interrupt: one per class - fires only on a live interruptible cast, never
   on a non-interruptible one, never blind.
10. Offensive: one major per spec - holds during its paired window, fires when
    MaxDps surfaces it outside the window.
11. Mobility: a gap closer fires only out of melee + in range; escapes and
    speed bursts never fire automatically.
12. Manual utility (CC / purge / dispel / threat): never fires, even with ON
    toggled; verify the Home/plan "why" says the utility path is manual.
13. OFF is absolute: a defensive/user-disabled ability never fires at Red or
    in emergency (telemetry reason "user policy disabled").
14. Modes persistence: set `[Abilities] Modes` via the storage path, restart
    the app, confirm it is honoured (SoloOnly/NormalOnly/Manual).
15. Patch warning behavior: run with a registry stamped for another patch
    (fixture build) and confirm the app refuses to load it; a newer-patch
    entry fails the audit (`patch-newer`).
16. Self-buff tri-state: with a buff-active ability (e.g. Shield Block), the
    "own buff already active" skip appears; with the aura API unavailable the
    companion treats buffs as UNKNOWN (no silent skip claim).
17. Record + export + replay a live session: 0 mismatches (decisions and
    policy verdicts, provider + evidence included).
18. Registry health dashboard numbers match `--ability-coverage` and the
    audit report; clicking a count filters the explorer.
19. Window: resize, restart, size remembered; ring green running / red
    stopped; run at 100% / 150% / 200% DPI and a narrow window without
    clipped labels (structural smoke + visual spot-check).
20. Keyboard: Tab through a page, Space/Enter a toggle, arrow the rail, Esc
    returns Home.
21. Representative live matrix (minimum): Warrior Impending Victory, Warrior
    Pummel, Warrior Recklessness, one Warrior defensive (e.g. Shield Wall),
    one mobility ability (e.g. Charge), one ManualByDesign utility
    (Intimidating Shout). Expand class-by-class afterwards.
22. Complete the §55 coverage report (`--ability-coverage`) after the run and
    record which class/spec lines were exercised.

### 3e. TTK v3.6 dying-trash guard acceptance (live 12.1 retail, OWED)

Offline evidence is the estimator/policy/curation tests plus the
`ttk-trash-pack.jsonl` replay (6 mobs, each <5 s) and the unchanged legacy
replays at 0 mismatches; that is never live proof. Observe in a real client
with Intelligence ON, TTK guard ON (or observe the documented OFF collateral):

1. **M+ trash → boss (no-hold check).** Clear a trash pack where individual
   mobs die in <5 s, then pull the boss. During trash the major burst must be
   **held** (`"fast pack, waiting for TTK"` after the second quick kill, or the
   provisional waste hold); against the boss it must **fire normally** — the
   latch must clear on the long fight (telemetry: no Major held vs the boss,
   estimate valid and TTK ≥30 s). Confirm a kill-secure major still fires late
   in a long fight (age ≥20 s, target ≤35%, TTK 3–20 s).
2. **Dungeon tank Minor check.** As a tank with a short-CD Minor defensive,
   confirm group-scope gating is conservative: a group Minor may only be held
   when the estimate is valid <4 s, urgency is below Orange and the fast-pack
   latch is set; group Major/Immunity must **never** be gated, and emergency HP
   always overrides. (Rationale: enemy count is unobservable; the tank may be
   dying to other mobs.)
3. **Toggle semantics.** Confirm the hero/`/mdb` label reads **"TTK guard"**
   and the tooltip says *OFF = cooldowns fire without dying-target protection*.
   With it OFF, band 15 blanks the execute gate and Burst consumers as well as
   the TTK gates (documented collateral); `[TimeToKill] Fallback=ConserveMajors`
   holds an unknown-TTK major even with no latch.
4. **No false holds.** Confirm a normal single-target boss pull (long, slow
   decline) is never held by the grace hold/provisional path, and that the
   kill-secure exception cannot fire before age 20 s.
5. Record + export + replay the run: 0 mismatches for decisions and policy
   verdicts (`ttkp`/latch fields included).

### 3f. TTK v3.7 adaptive real-data history (live 12.1 retail, OWED)

Offline evidence is `TtkEstimatorHistoryTests` (kill filter, window
cap/prune/idle-clear, nearest-rank quantile, below-MinKills fail-open identity,
blend weights, history-only provisional) and `TtkPolicyHistoryTests`
(NeedAdaptive, `ttk-hist` hold/use, invalid-live restriction, carve-outs,
consumable/trinket Burst release, `[TimeToKill]` parse/clamp, replay hk/hs
reproduce) — never live proof. One history line: with Intelligence ON and
`[TimeToKill] History=1`, clear a fast trash pack then pull a boss; the first
~8.5 s of the boss may be held on the trash-learned rate (reason `ttk-hist`),
then the live rate wins and the major fires. Telemetry carries `hk`/`hr`/`hs`/
`hprov` and the replay report must read `adaptive-history reconstruction 0
mismatch(es)`. `History=0` reproduces the pre-history behaviour exactly.

### 3g. TTK v3.8 warmup + buff-aware gating (live 12.1 retail, OWED)

Offline evidence is `TtkPolicyTests` (`BuffNeed` half-buff/cap-20/zero-fallback,
`WarmupHoldHolds` young-unknown major hold, window close, valid/provisional/
execute/AoE/minor/kill-secure carve-outs, binding-history release, provider
Hold `"warming up TTK"` vs Use) and the regenerated `ttk-warrior-burst.jsonl`
fixture (warmup holds from t=0, then the T1 hold, then the boss fire, 0
mismatches). Never live proof. One live line: with Intelligence ON and
`[TimeToKill]` defaults (WarmupSec=3), pull a target whose TTK is not yet
measurable; a major is held `"warming up TTK"` for ~3 s after first sight, then
the estimate/history takes over and the major fires on a long fight.
`WarmupSec=0` reproduces the legacy fail-open exactly; the setting is recorded
as `ttkw` and the replay must reproduce the hold.

### 3h. Custom MaxDps 12.1 fork acceptance (live 12.1 retail, OWED)

Offline evidence is static only — `luac -p addon/MaxDpsBridge/*.lua`, the T2
fixture suite (`pwsh tests/sync/Sync-CustomMaxDps.Tests.ps1`) and the
`MDB.MajorCDDeny` table — never live proof. There is no automated in-game test
for the fork. Observe in a real client with the bridge 3.6.0 addon loaded
(`/reload`, `/mdb status` shows `protocol=5`):

1. **Avatar / Combustion no longer Main.** Play a Warrior and a Fire Mage with
   the relevant major off cooldown; while MaxDps suggests the major, the MAIN
   slot must **not** encode it (`MDB.GetMainSpellID` returns the next allowed
   source or nil). Diagnostics/plan must never show a 2-3 min cooldown as the
   Main rotation pick.
2. **Offensive fires the moved CDs.** The same major must still fire through
   the **Offensive** slot when ready and policy-Use (`Reader.GetOffensiveCandidate`
   → `FirstFlagged("offensive")`), independent of the denied Main pick.
3. **No stall on a denied AC pick.** Hold the rotation on an Assisted-Combat
   pick that is denied: the bridge must fall through to the next allowed
   source, and an empty Main must not block the Offensive candidate or latch a
   hold. Confirm the next tick re-evaluates (a hold is non-latching) and the
   rotation resumes with no extra delay.
4. **Fork vs stock after `/reload`.** With the custom `out/` tree published
   into `AddOns`, `/reload` and confirm the denylist is active (no major as
   Main, Offensive still works); swap back to stock MaxDps, `/reload`, and
   confirm the pre-fork behavior (major may be encoded as Main). This is the
   A/B that proves the fork, not the companion, fixed it.
5. Record + export + replay the run: 0 mismatches for decisions and policy
   verdicts.

Also OWED: the true 12.1 ids (fill `newSpellId` / refresh `MDB.MajorCDDeny`)
and the `Sync-CustomMaxDps.ps1` publish + `_backup/` rollback end-to-end.
Static ≠ automated test ≠ live in-game.

## Benchmarks / diagnostics (no game)

```powershell
MaxDpsCompanion.exe --bench-scheduler            # scripted 27000-tick plan + plan hash
                                                 # (current: sends=1620, CastHold=450, ChannelHold=450, sha256=b71a999d5e46570e)
MaxDpsCompanion.exe --bench-telemetry            # telemetry serialization/ring cost
MaxDpsCompanion.exe --bench-sample               # screen-capture hot path
MaxDpsCompanion.exe --probe                      # attach + locate + decode -> probe.txt
MaxDpsCompanion.exe --replay=<file.jsonl>        # deterministic decision replay
                                                 # solo self-sustain fixture:
                                                 # --replay=tests\MaxDpsCompanion.Tests\fixtures\solo-warrior-selfheal.jsonl
                                                 # defensive urgency fixture:
                                                 # --replay=tests\MaxDpsCompanion.Tests\fixtures\defensive-warrior-urgency.jsonl
                                                 # offensive-interrupt fixture (v2.6):
                                                 # --replay=tests\MaxDpsCompanion.Tests\fixtures\offensive-interrupt-warrior.jsonl
                                                 # solo cooldown/reset fixture (r2):
                                                 # --replay=tests\MaxDpsCompanion.Tests\fixtures\solo-cooldown-reset-warrior.jsonl
                                                 # offensive gap-fill fixture (r1):
                                                 # --replay=tests\MaxDpsCompanion.Tests\fixtures\offensive-gapfill-warrior.jsonl
                                                 # TTK fixture (v3.2.0):
                                                 # --replay=tests\MaxDpsCompanion.Tests\fixtures\ttk-warrior-burst.jsonl
                                                 # TTK dying-trash fixture (v3.6):
                                                 # --replay=tests\MaxDpsCompanion.Tests\fixtures\ttk-trash-pack.jsonl
MaxDpsCompanion.exe --ability-audit=<path>         # registry audit report (Violations 0 / Warnings 0 / Missing 0 / Stale 0 enforced by tools/ability_audit.ps1, exit 3 when non-clean)
MaxDpsCompanion.exe --ability-coverage=<path>      # v2.7 machine-readable coverage manifest (default ABILITY_COVERAGE.json)
MaxDpsCompanion.exe --ability-info=<spellId>       # inspect one ability (writes ability-info.txt + stdout)
MaxDpsCompanion.exe --ability-search=<text>        # registry filter (also --ability-class= / --ability-spec=)
MaxDpsCompanion.exe --ui-smoke-test              # hardened structural checks (text-fit / card content / wrapped labels / zero-size / overlap / tab); exit 1 on findings
MaxDpsCompanion.exe --ui-snapshot-page=home --ui-snapshot=home.png --ui-snapshot-width=1280
                                                 # page snapshots: home|abilities|intelligence|configuration|diagnostics
                                                 # review set: dist/ui-snapshots (5 pages x 6 widths, regenerable)
MaxDpsCompanion.exe --dump-class-skills=<path>   # merged class/spec skill tree
                                                 # CLASS<TAB>SPEC<TAB>section<TAB>provenance<TAB>name<TAB>id
MaxDpsCompanion.exe --ui-snapshot-class-skills=<png> [--ui-snapshot-class=CLASS --ui-snapshot-spec=SPEC]
                                                 # render the Class skills screen (default ROGUE/Outlaw; no game)
```

## Knowledge verification (network)

```powershell
pwsh -File tools/Verify-ClassSpells.ps1   # streams wago.tools DB2 CSV -> Knowledge/spell-verification.json + pre-cached icon dirs
```

Network required. It regenerates the live-client name/icon/verified data for
every class-spell id and downloads the missing icons into `dist/assets/icons`
and the dev bin cache; the committed JSON is deterministic (byte-identical
reruns). It is not part of `dotnet test` — the suite only consumes the
embedded result.

## Tooling

```powershell
luac -p addon/MaxDpsBridge/*.lua     # Lua syntax (all 6 files)
dotnet build -c Release              # 0 warnings / 0 errors is the bar
```

