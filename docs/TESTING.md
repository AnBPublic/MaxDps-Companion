# Testing

Two layers are fully offline (no game required); the third layer is a live
retail checklist and is explicitly OWED, never claimed from the offline runs:

## 1. C# deterministic suite — `tests/MaxDpsCompanion.Tests`

```powershell
cd tests\MaxDpsCompanion.Tests
dotnet test -c Release
```

946 tests, all fake-clock (no wall time, no I/O except temp files). (The P2-E
note below lists the WinUI port's reinstated/deleted UI cases; that pass moved
the net suite count from 905 to 946.) Groups:

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
| `UiShellTests` | model-level (XAML-free): explorer filter/search over `AbilityRowsPrecompute`, coverage/intelligence report via `AbilityIntelligence.Audit` (live tree walk moved to `--ui-smoke-test`) |
| `SpellIconCacheTests` | download-once + disk-cache pipeline (exactly two requests), the official-slug path (a single request), offline degradation to the placeholder, hostile icon-slug rejection, invalid ids ignored |
| `DecisionEngineTests`, `CandidateTrackerTests` | legacy decision layer + identity-aware tracker (stroke OR spell-id change); the enabled order inserts the companion slots without reordering the MaxDps slots |
| `AppSettingsTests` | `[Intelligence]`/`[Solo]` defaults, parsing, clamping, round-trip |
| `TelemetrySerializationTests`, `TelemetryRingBufferTests`, `TelemetryReplayTests` | JSONL format (incl. the policy context + send spell id), ring bounds, legacy replay determinism, and policy-verdict replay: exact recompute, live send-before-tick order, tamper detection, trinket lockout from sends |

The class-spell verification and official-slug icon tests are part of this
suite (`dotnet test`) and stay offline: they read the embedded
`spell-verification.json` and use a stubbed `HttpMessageHandler`, so the live
wago.tools fetch is never exercised by the test run.

### 1a. P2-E WinUI port: fates of the 69 compile-excluded UI tests

(v4.0.2 Lumen; plan `docs/plans/2026-10-11-winui3-v4-migration.md` §6. Every
formerly excluded method has an explicit fate; net suite count 905 -> 946.)

| File | Reinstated | Deleted (WinForms-only) |
| :--- | :--- | :--- |
| `ClassicUiTests` | 4: window consts, Solo toggle -> settings (ToggleModel), width tier fonts/rows + popup toggle geometry (UiScale) | 10: scroll internals, button control lists, launcher test-delegate, 50x layout events, popup re-parenting, row measure/resize internals, scrim paint/timing, bubble click |
| `UiShellTests` | 2: explorer filter/search (AbilityRowsPrecompute), intelligence report (AbilityIntelligence.Audit) | 5: structural smoke (now `--ui-smoke-test`), popup/Esc click plumbing, tile-click drill, toggle mouse click, label wrap measure |
| `ClassBrowserViewTests` | 3: per class/spec/mode rows, mode vocabulary (ClassBrowserView.ModeOptions), warm-reopen <=150 ms cache | 1: DrawToBitmap paint smoke (now the RenderTargetBitmap non-blank PNG in `--ui-smoke-test`) |
| `ConsoleHomeTests` | 3: five preset bundles, role inference, preset -> ToggleModel -> settings | 1: ConsoleHome chips/update-log internals |
| `ApproachADrillThroughTests` | 3: preset vocabulary, condition line, explorer preset filter | 1: Class skills screen (dropped with ClassSkillsView, plan §0) |
| `ExpModeTests` | 5 (6 cases): the InGameConfigMode settings/ini/`--exp`/save contract | 8: Exp layout control counts/coords/enabled/text internals (live replacement: `--ui-smoke-test --exp` asserts zero toggles; `--ui-snapshot-page=main --exp` reviews layout) |
| `ReleaseIdentityTests` | 3: 4.0.3 / v4.0.3 Bulwark / real runtime title | - |
| `S8PerfDiagnosticsTests` | 15: precompute cache/keys (AbilityRowsPrecompute), ScaledIcon null guards, WhyNotFiring explainer (Ui.Shell port), InstallDoctor | 1: ScaledIcon Bitmap width/reuse (WinUI BitmapImage decodes async on the UI thread; live via `--ui-snapshot-page=abilities`) |
| `CrowdControlTests` (guard) | 1: CrowdControl ToggleModel round-trip + gate publish | - |
| `Stream3Tests` (guard) | 1: solo bands validate + FromSettings (PolicyOptions) | 1: slider Minimum/Maximum ranges (now NumberBox clamps in AdvancedConfigurationView.LoadFrom; reviewed via snapshot) |

The `WINUI_P0` define and every `<Compile Remove>` for these files are gone
(the app csproj has no Compile Remove/Include scaffold left: the legacy
WinForms files — `MainForm.cs`, `UiControls.cs`, `ClassSkillsView.cs`, every
old `Ui/*.cs` — were deleted from disk in P3, and the broken unused
`WheelSafeNumeric` control was deleted with them). Live UI coverage moved to
`tools/ui_smoke.ps1` (`--ui-smoke-test`); the `--bench-ui` startup-target miss
is recorded in the benchmarks section below.

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

> DEPLOY: the **CURRENT** client is the WinUI 3 companion — install the addon with
> `tools/install-addon.ps1` and run the `dist\` exe (the in-game-config shell is the
> default; `/mcb`, legacy aliases `/mdb`, `/mdbx`). Any legacy-addon step below
> applies only when it explicitly targets the frozen `addon/legacy/MaxDpsBridge`.

Static (parses/builds) ≠ automated test ≠ live validation. The following are
**required** in a real client and cannot be automated offline:

1. `/mcb calibrate on` → Recalibrate → v5 pattern learned (35 cells).
2. `/mcb status` shows `protocol=5`, class/spec/extras sensible.
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
   produce a kick (watch `/mcb status` + the plan reason); an interruptible
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

1. Reinstall the addon (`install-addon.ps1`) and `/reload`; `/mcb status`
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
   revision 3); `/mcb status` shows `protocol=5`.
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

1. Reinstall the addon (`install-addon.ps1`) + `/reload`; `/mcb status` shows
   bridge 2.7.0, `protocol=5`, sensible class/spec/extras.
2. Calibrate (`/mcb calibrate on` -> Recalibrate) and confirm the strip
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

**3.7.9: the offensive conservation verified here was removed — see §3k. This
section is retained as v3.6 history (the T4/defensive parts still apply).**

Offline evidence is the estimator/policy/curation tests plus the
`ttk-trash-pack.jsonl` replay (6 mobs, each <5 s) and the unchanged legacy
replays at 0 mismatches; that is never live proof. What a v3.6 client showed in
a real client with Intelligence ON, TTK guard ON (or the documented OFF
collateral):

1. **M+ trash → boss (no-hold check) — superseded in 3.7.9, no longer
   applies.** Mobs dying in <5 s used to hold the major burst (`"fast pack,
   waiting for TTK"` after the second quick kill, or the provisional waste
   hold); the latch then cleared on the long boss fight and it fired (telemetry:
   no Major held vs the boss, estimate valid and TTK ≥30 s), and a kill-secure
   major could fire late (age ≥20 s, target ≤35%, TTK 3–20 s). 3.7.9 removed all
   offensive TTK holds, so a MaxDps-offered burst now fires regardless of the
   pack — there is nothing to observe here.
2. **Dungeon tank Minor check — T4 retained.** As a tank with a short-CD Minor
   defensive, confirm group-scope gating is conservative: a group Minor may only
   be held when the estimate is valid <4 s, urgency is below Orange and the
   fast-pack latch is set; group Major/Immunity must **never** be gated, and
   emergency HP always overrides. (Rationale: enemy count is unobservable; the
   tank may be dying to other mobs.)
3. **Toggle semantics.** Confirm the hero/`/mcb` label reads **"TTK guard"** and
   the tooltip says *OFF = cooldowns fire without dying-target protection*. The
   former band-15 collateral (blanking the execute gate/Burst consumers and the
   `[TimeToKill] Fallback=ConserveMajors` unknown-TTK hold) is **superseded in
   3.7.9 — no longer applies**; ConserveMajors no longer conserves offensives.
4. **No false holds — superseded in 3.7.9, no longer applies.** The grace
   hold/provisional path and the kill-secure exception were removed; there is no
   offensive TTK hold left to false-trigger.
5. Record + export + replay the run: 0 mismatches for decisions and policy
   verdicts (`ttkp`/latch fields included).

### 3f. TTK v3.7 adaptive real-data history (live 12.1 retail, OWED)

**3.7.9: the `ttk-hist` offensive hold was removed — see §3k. The estimator
history feeds remain (T4/telemetry).**

Offline evidence is `TtkEstimatorHistoryTests` (kill filter, window
cap/prune/idle-clear, nearest-rank quantile, below-MinKills fail-open identity,
blend weights, history-only provisional). The former `TtkPolicyHistoryTests`
decision-layer coverage (`NeedAdaptive`, `ttk-hist` hold/use, invalid-live
restriction, carve-outs) was **removed in 3.7.9**; the retained test now covers
the consumable/trinket Burst release, `[TimeToKill]` parse/clamp and replay
hk/hs reproduction - never live proof. The pre-3.7.9 history line was: with
Intelligence ON and `[TimeToKill] History=1`, clear a fast trash pack then pull
a boss; the first ~8.5 s of the boss could be held on the trash-learned rate
(reason `ttk-hist`) before the live rate won and the major fired. Telemetry
still carries `hk`/`hr`/`hs`/`hprov` and the replay report must read
`adaptive-history reconstruction 0 mismatch(es)`. `History=0` reproduces the
pre-history behaviour exactly.

### 3g. TTK v3.8 warmup + buff-aware gating (live 12.1 retail, OWED)

**3.7.9: the warmup offensive hold was removed — see §3k.**

The former `TtkPolicyTests` coverage (`BuffNeed` half-buff/cap-20/zero-fallback,
`WarmupHoldHolds` young-unknown major hold, window close, valid/provisional/
execute/AoE/minor/kill-secure carve-outs, binding-history release, provider Hold
`"warming up TTK"` vs Use) and the regenerated `ttk-warrior-burst.jsonl`
warmup-hold expectation were **removed in 3.7.9**. Never live proof. The
pre-3.7.9 live line was: with Intelligence ON and `[TimeToKill]` defaults
(WarmupSec=3), pull a target whose TTK is not yet measurable; a major was held
`"warming up TTK"` for ~3 s after first sight, then the estimate/history took
over and the major fired on a long fight. `WarmupSec=0` reproduced the legacy
fail-open exactly; the setting was recorded as `ttkw` and the replay had to
reproduce the hold.

### 3h. Main = MaxDps core rotation, deny list removed (3.7.8; live 12.1 retail, OWED)

Offline evidence is static only — `luac -p addon/MaxDpsCompanionBridge/*.lua`, `lua
tests/secret_harness.lua` (the former `MDBX.MajorCDDeny` pins are inverted for
3.7.8) and the `MDCB.FlagOffensiveExtra` routing checks — never live proof.
There is no automated in-game test. Observe in a real client with the companion
bridge addon loaded (`/reload`, `/mcb status` shows `protocol=5`):

1. **Formerly denied core-rotation recommendations ARE Main.** Play a Warrior
   and a Fire Mage with the relevant ability available; while MaxDps glows it as
   its core rotation, the MAIN slot must encode it (e.g. Avatar 107574,
   Combustion 190319, Ravager 228920). There is no denylist, no exception.
2. **Offensive routing unchanged vs 3.7.7.** The same ids must still fire
   through the **Offensive** slot when ready and policy-Use
   (`Reader.GetOffensiveCandidate` → `FirstFlagged("offensive")`, via
   `MDCB.FlagOffensiveExtra`); Offensive / Defensive / Interrupt are untouched.
3. **No dead Main.** A power-starved glow (`usable == false AND noPower ==
   true`) must fall through to the next glow, or to the per-spec `MainFallback`
   filler, never stranding the slot. Secret/nil/throw fail open. An off-GCD
   major must not stall the rotation, and a hold is non-latching.
4. Record + export + replay the run: 0 mismatches for decisions and policy
   verdicts.

Also OWED: the true 12.1 ids (fill `newSpellId`; the `MDBX.MajorCDDeny` table is
**gone in Exp 3.7.8** — Main now trusts the MaxDps core rotation — while the legacy
`addon/legacy/MaxDpsBridge/` keeps its frozen deny) and the `Sync-CustomMaxDps.ps1`
publish + `_backup/` rollback end-to-end. Static ≠ automated test ≠ live
in-game.

### 3i. No-downtime MAIN / Fury fallback (live 12.1 retail, OWED)

Offline evidence is `MainNoDowntimeTests` (Fury Rampage rage-blind fixture:
`MainReprobeMs` = 150, re-probe ≥ `MinKeyInterval`, never silent beyond
`MainReprobeMs + MinKeyInterval`; a changed Main picks the next tick; 20
stationary no-op ticks keep every gap; no filler is invented when `ranked==0`)
plus the Q1 block in `lua tests/secret_harness.lua` (glowing∩usable scan,
denied/power-starved fall-through, per-spec filler, fail-open on secret/nil/
throw) — never live proof. Observe in a real client with a Fury Warrior and the
bridge 3.7.9 addon loaded:

1. **Rampage only at Rage ≥ 80.** While Rampage (184367) is suggested but the
   player is below 80 Rage, it must **not** be pressed; the MAIN slot carries
   the castable filler Bloodthirst (23881) instead of blanking.
2. **No GCD gaps / no silent Main.** The rotation keeps a legal MAIN key every
   tick: the gap between attempts never exceeds ~0.15 s (`MainReprobeMs`) plus
   one `MinKeyInterval`, and a no-op press never latches a hold.
3. **Alternate pressed.** When MaxDps swaps the MAIN suggestion to a different
   spell id, the new identity is pressed on the next tick (the superseded
   pick's backoff is dropped).
4. Record + export + replay the run: 0 mismatches for decisions and policy
   verdicts.

Unlisted specs have no filler (MAIN may legitimately stay empty and are OWED);
the Fury ids are verified by name against the read-only `vendor/` tree (see
`addon/MaxDpsCompanionBridge/MainFallback.lua`). Static ≠ automated test ≠ live in-game.

### 3j. WHITE Main immediate execution (live 12.1 retail, OWED)

Offline evidence is `MainImmediateTests` (stale Main ahead of fresh Offensive;
Main no MinInterval wait after a different slot/spell; identical repeat held
exactly `MinKeyIntervalMs`; Main failure sets no `_failedUntil`; SawGcd resets
the cap; non-Main backoff unchanged; GCD/cast/range/melee/NoTarget still hold;
fallback Main identical) — never live proof. Observe in a real client with the
bridge 3.7.9 addon loaded, Intelligence ON, Main toggle ON:

1. **Main first tick post-GCD.** With an enemy in range + sight and a WHITE
   core suggestion live, the Main key is sent on the first tick after the GCD
   falls — no companion-strategy delay and no recency-stale demotion.
2. **No double-press.** Within one GCD the identical Main spell is sent at
   most once (the pre-GCD-flip guard holds an identical repeat for
   `MinKeyIntervalMs`); a different Main spell from MaxDps is pressed
   immediately even right after a different-slot press.
3. **Fallback immediate.** With every Main glow denied/power-starved, the
   per-spec `MainFallback` filler (Fury Bloodthirst 23881; Arms Mortal Strike
   12294 → Overpower 7384) fires immediately rather than waiting a ladder.
4. **Offensive/Main interleave.** An off-GCD Offensive still fires while the
   Main is GCD-held; a Companion Offensive does not delay a different Main
   spell (C2); Main still never carries a major CD.
5. **No-target / OOR / casting holds.** With no target, out of range, or while
   hard-casting/channeling, no Main key is sent (game-forbidden holds only).
6. **Refused-key cap.** Force a refused Main (immune/resource-starved): sends
   stay capped at ≤3 per window and ≤1 / 150 ms, and Main re-arms immediately
   after the cap with no long suppression ladder.
7. A refused Main never silences the rest of the rotation: a fresh Offensive/
   Defensive/interrupt on another stroke still fires.
8. Record + export + replay the run: 0 mismatches for decisions and policy
   verdicts. Static ≠ automated test ≠ live in-game.

### 3k. Offensive TTK conservation removed (3.7.9; live 12.1 retail, OWED)

Offline evidence is the candidate/provider + policy test updates and the
regenerated TTK fixtures/replays for the no-conservation behaviour, plus the
offline vendor-offensive audit (161 ids across all 13 classes; 0 carrying
`neverAutomatic`/`ManualByDesign`/`UnsafeToAutomate`; 49/161 vendor-only rows
resolve to `MaxDpsBacked`) — never live proof. Observe in a real client with the
companion bridge addon loaded, Intelligence ON:

1. **No TTK hold on a MaxDps offer.** Against a target whose TTK is ≤15 s (and
   in trash), a MaxDps-recommended offensive — Avatar 107574, Ravager 228920,
   Recklessness, Bladestorm (and peers) — must **press** when the bridge offers
   it; there must be no `"target ~Xs to die; saving <name>"`,
   `"warming up TTK"` or `"fast pack, waiting for TTK"` hold.
2. **Burst preset still conserves.** With the user's **Burst** preset selected,
   majors are still held until a boss TTK is measurable; selecting Normal/None
   restores the no-conservation behaviour. The AoE preset still conserves
   catalogued single-target-only cooldowns.
3. **Gap-fill unchanged.** A companion gap-fill major still fires only when the
   bridge offers it (MaxDps silent) and stays inside the gap-fill
   out-of-combat hold; melee/range and the other game-truth/structural gates
   still apply. Only 212084 Fel Devastation and 98008 Spirit Link Totem route
   to the Defensive slot by `classOf` precedence (documented deviation).
4. Record + export + replay the run: 0 mismatches for decisions and policy
   verdicts. Static ≠ automated test ≠ live in-game.

This supersedes the offensive parts of §3e/§3f/§3g, which remain as the
v3.2–v3.8 history of the removed conservation.

### 3l. Hotbar press queue (4.0.3; live 12.1 retail, OWED)

Install: `pwsh tools/publish.ps1` + `pwsh tools/install-addon.ps1`,
then `/reload`. `/mcb hotbar` prints gen/seq/entries and marks the keybound
bar entries ready=yes (incl. macros and items); `/mcb status` shows the new
`ext4=1` field.

| # | Check | Expected |
| :--- | :--- | :--- |
| 1 | Press a GCD-riding ability key *early* inside a GCD (focused game) | The cast fires at the end of THAT GCD (no lost press); `Now` shows `<name> (you)`; telemetry note `queued` -> `delivered` |
| 2 | Press the same key late (inside the client ~400 ms queue window) | No double cast (the cooldown rejects the duplicate) |
| 3 | Press an off-GCD key (trinket / item) mid-GCD | Fires immediately; the rotation is not deferred; focused native press is not re-sent |
| 4 | Spam a cooling-down ability key | Recognised/delivered, but the rotation never stalls (no persistent `your ... queued` hold) |
| 5 | Press a UI key (B/C/M) or a movement key (WASD/Space/arrows) | Never queued |
| 6 | Type `f` in game chat while in combat | Not delivered (typing veto; `/mcb hotbar` shows typing=yes) |
| 7 | Rebind an ability mid-session | `/mcb hotbar` reflects it within ~1 s; the old key stops queueing |
| 8 | Play with a pre-4.0.3 addon | No crash; status shows `user queue: addon too old...`; everything else unchanged |
| 9 | `[UserQueue] Background=1`, game unfocused, press a bound key | Delivered in the next window (opt-in only) |
| 10 | Record + export + replay a session containing user presses | 0 mismatches (user sends never feed policy memory) |
| 11 | ElvUI / Bartender / DiabolicUI bars | Binding resolution works (at least the command-mapped bars) |
| 12 | 51-cell strip does not clip a screen edge | If it clips, re-anchor (`/mcb offset 0 0`) or move it; decode stays green |
| 13 | Keep the rotation going for 30 s after a mid-fight press (hotfix regression check) | Core rotation (Main) continues the whole time; the pressed key fires once; no `only interrupt/trinket` pattern |
| 14 | `/mcb main off`, then watch the companion status | Status says `holding (addon Main off - rotation suspended)`; `/mcb main on` restores the rotation |
| 15 | Flip a slot toggle OFF *after* it has been firing (hotfix regression check) | Strip keeps decoding — the companion never reports "No pixel block" and the rotation continues with that slot blanked |

Static != automated test != live in-game E2E: none of the above is claimed
before a retail run.

### 3m. Main master switch + compact overlay (4.0.4; live 12.1 retail, OWED)

Install the current companion + the MaxDps Companion Bridge addon, then `/reload`.

| # | Check | Expected |
| :--- | :--- | :--- |
| 1 | Turn Main OFF while the rotation is firing | Every slot stops; status says `holding (addon Main off - rotation suspended)`; no ability is sent |
| 2 | Inspect the other pills while Main is OFF | Offensive / Defensive / Utility / Heal etc. keep their physical ON/OFF state (not rewritten) |
| 3 | Turn Main back ON | The rotation resumes with exactly the previous per-slot policy; no `/reload` needed |
| 4 | Main OFF, then press a bound hotbar key mid-GCD | No companion delivery/queue; the native focused-game press still behaves normally |
| 5 | Main OFF, AutoTgt/AutoInteract ON, no target | No Tab/MB5 send; status stays on the master-off line |
| 6 | Main ON, the other toggles still work | A per-slot OFF still blanks only that slot; Solo/OOC/TTK semantics unchanged |
| 7 | `/mcb main off` / `/mcb main on` | Same master behaviour as the overlay pill |
| 8 | Overlay geometry | No `profile:` line; frame is 300x165, 13 pills in the 4-column grid; status dot still bottom-right |
| 9 | Profile workflows | `/mcb profile Global / Spec / Talent` and the Config Profiles tab still work (the overlay no longer shows the profile text) |
| 10 | Version skew | 4.0.3 companion + 4.0.4 addon: every slot blanks, but the old companion may still auto-target/interact (no master gate). 4.0.4 companion + 4.0.3 addon: the Ext3 mirror still closes the companion master gate |
| 11 | Flip Main OFF, then ON, repeatedly during a fight | No stuck blank, no duplicate send, no stale queued press from before the OFF |

Static != automated test != live in-game E2E: none of the above is claimed
before a retail run.


### 3n. 4.1.0 "Paragon" audit remediation (live 12.1 retail, OWED)

Offline (done, see HANDOVER): build 0/0, `dotnet test` 1054 passed, harness 393, `luac -p`, audit clean.
Live checks - each needs a retail run and stays OWED until then:

1. **Modifier chords (you use Shift/Ctrl/Alt binds).** With `[Window] ModifierInput=Post` (default) fire a
   Shift+key and a Ctrl+key bound ability from the rotation, game FOCUSED, then NOT focused. Repeat with
   `Auto` and `SendInput`. Record which combination casts. Post-vs-SendInput on the same chord is the control:
   if Post already works in both states keep the default; if Post fails only for chords, set `Auto`.
2. **Held modifiers.** Hold Shift while a Ctrl chord is due under `Auto`/`SendInput`: it must defer with the
   status note (never send Shift+Ctrl). Hold Shift while a Shift chord is due: Shift must stay down afterwards.
3. **Stuck modifier.** Pause / Stop during a chord: no modifier stays down (Windows key state, in-game).
4. **Elevated game.** Run WoW as administrator: the status line must say the window was lost / input blocked
   instead of "sending", and the scheduler must back off (no silent no-ops).
5. **No double press after a queued user press** (press a hotbar key during a GCD; rotation must not fire a
   second key in the same tick).
6. **Focus-blocked slot** with `AllowBackgroundKeys=0` and the game unfocused: an off-GCD interrupt/trinket
   slot is still evaluated; nothing is sent while unfocused.
7. **Occluded / minimized strip:** minimize WoW, then cover the strip with another window: the status shows
   the minimized / "strip occluded" message, not "no pixel block", and recovers when uncovered.
8. **Addon frametime:** compare addon CPU (`/run UpdateAddOnCPUUsage()` with CPU profiling on, or the in-game
   AddOn profiler) for `MaxDpsBridgeExp` before (4.0.4) and after (4.1.0) in a combat dummy fight.
9. **Ext4 map stays populated** through a cooldown-heavy fight (the hotbar queue keeps recognising presses;
   `/mcb hotbar`).
10. **Bars:** stance/form bar (druid/rogue), vehicle and override bars: no wrong key is sent; slots 73-144 use
    the texture fallback and may resolve no key (expected, record any ability that stops firing).
11. **SavedVariables migration:** log in with a 4.0.4 `MaxDpsBridgeExpDB`; `SchemaVersion` becomes 1, a
    previously paused bridge stays paused, a resumed one stays resumed.
12. **Mac (on a Mac):** missing Screen Recording blocks Start with a visible message; keys reach WoW with it
    in the background; numpad/punctuation/F13-F20 binds fire; `--probe`; `build-mac.sh` runs `selftest.swift`.
13. **CI:** the first GitHub Actions run is green on both jobs (UI-timing tests may flake).

### 3o. 4.2.0 "Nightblade" CC intelligence (live 12.1.5 retail, OWED)

Offline (done, see HANDOVER): build 0/0, `dotnet test` 1071 passed, harness 409,
`luac -p`, catalog fixture/Exp Catalog regenerated. Live checks:

1. **Kick wins.** Target casting, Kick bound+ready: only Kick fires; Kidney Shot
   must not be pressed in the same cast.
2. **Stun fallback.** Put Kick on cooldown, keep the target casting: Kidney Shot
   fires. Second cast inside 20 s: at most a 50% stun (1 s from the 2 s base).
   Third cast inside 20 s: held with a DR/immune reason.
3. **PvP DR.** In a duel/arena, stun the same player twice inside 20 s. The
   third Kidney Shot must not be generated (DR 2/2 immune).
4. **Blind/Gouge guard.** Manually Blind a dummy, then watch Main/Offensive/
   Interrupt: no rotation attack is sent for ~8 s (Gouge ~4 s), status shows
   "target CC'd by Blind ... attacks held". A manually pressed attack still
   goes through (your press always wins).
5. **Manual party CC hold.** Press `Ctrl+Shift+C` (or Console "CC Hold") while
   a party member Polymorphs a mob: attacks hold for 8 s; status shows
   "manual CC hold". Press again to cancel.
6. **Boss / immune.** On a worldboss or an inherently stun-immune elite, the
   auto CC pool must not offer Kidney Shot; if a stun is attempted and the cast
   continues ~800 ms, a second auto stun must not fire until target loss.
7. **Uninterruptible cast.** An explicitly not-interruptible cast must not
   produce Kick or Kidney Shot.
8. **No auto Blind/Gouge/Sap.** With `[CrowdControl] Enabled=1`, the companion
   never presses those abilities itself; `/mcb` slot 6 offers Kidney Shot only
   for a rogue.
9. **Telemetry/replay** (optional): record a fight with a stun/Blind and replay;
   0 verdict mismatches.

### 3p. 4.4.0 "Wayfinder" transport + foreground gate (live 12.1 retail, OWED)

Offline (see HANDOVER/final report): build, `dotnet test`, `luac -p`, `--ui-smoke-test`
title `MaxDPS Companion v4.4.0 Wayfinder`. Live checklist:

1. **Open Game while WoW runs.** With the client already up, `Open Game` is
   disabled (darkened, lock glyph) and reads `Game Running`; the tooltip/UIA
   HelpText say the same. Clicking it does nothing.
2. **Launch gap.** Close WoW, press `Open Game`: the button switches to
   `Game Starting…` (disabled) for the launch grace and returns to a normal
   enabled `Open Game` only if the launch fails. No second client can be started
   by clicking again during the gap.
3. **Restart Game.** With WoW up, `Restart Game` asks for confirmation
   (Restart WoW / Cancel); Cancel does nothing. Confirm: the client is killed and
   relaunched through Battle.net, both transport buttons stay disabled for the
   duration, and the status/log show the restart.
4. **Calibrate lives in Details.** The main card shows no Calibrate button; open
   **Details** and `◉ Calibrate` is there, works (pattern renders, companion
   learns the chain) and flips to `Cancel` while calibrating.
5. **Now lives in Details.** The main card has no always-visible `Now:` line;
   expanding Details shows `Now: <action> — <why>` (wrap to 2 lines, tooltip
   with the full text) and the log does not move when the text wraps.
6. **Alt-tab pauses detection.** While WoW is foreground the strip/log update;
   alt-tab away: state reads `game in background`, the pill goes hollow, fps 0,
   the bridge banner reads `paused (game in background)`, and **no**
   `no pixel block` / occluded / minimized lines appear in the log. Return to
   the game: detection resumes silently (no log chatter).
7. **Bridge warning, once.** With WoW foreground and the addon **not** loaded,
   the log warns exactly once after ~5 s and does not repeat. Load the addon:
   the warning clears. Repeat an outage (reload with the strip hidden): at most
   one warning per outage (after ~5 s).
8. **Background state pill.** Background vs foreground state words/pills match
   the copy above and the status-line tone stays neutral (never danger) while
   paused in the background.
9. **`AllowBackgroundKeys` note.** With `[Window] AllowBackgroundKeys=1`, alt-tab
   still pauses detection (keys-only setting); confirm the overlay/settings note
   says detection is foreground-only.
10. **Restart SavedVariables warning.** The restart confirmation states that the
    kill loses the current session's SavedVariables; after a restart, values
    written earlier in that session are gone as warned.

### 3q. 4.5.0 "Voidlight" chrome + changelog (live 12.1 retail, OWED)

Offline (see HANDOVER/final report): build 0/0, `dotnet test`, `--ui-smoke-test`
title `MaxDPS Companion v4.5.0 Voidlight`, `--ui-snapshot` review, plus a live
`PrintWindow` capture of the shipped exe. Live checklist:

1. **No title-bar band.** With the shipped `dist\MaxDpsCompanion.exe` on a light
   desktop theme, the very top row of pixels is the title-bar gradient - no
   white/light strip above it, no frame inset on any edge (resize the window and
   re-check; the client area must reach the rounded corners).
2. **Resize still works.** Drag each edge and corner: the window resizes,
   honours the 520x560 DIP minimum, and the 8 DIP grip feels the same as 4.4.0.
3. **Caption drag.** Dragging the title bar moves the window immediately under
   the cursor (the caption rects are no longer offset by the old frame).
4. **Gradients.** Ground, title bar, Start/Pause/Stop, Open Game/Restart Game,
   the status pill and both cards show a visible top-lit ramp; hovering and
   pressing must not flatten or invert them.
5. **Contrast.** Start/Stop labels stay legible across the whole pill at 100 %
   and 150 % Windows scaling; disabled pills keep a crisp ring with faded text.
6. **Changelog.** Open **Details**: `WHAT'S NEW · 4.5.1 Voidlight` lists the four
   highlights plus the `Earlier:` line, wraps at the card width, and expanding
   Details keeps the log readable (252 DIP wanted, re-clamped when short).
7. **Version surfaces.** Title bar, `dist\VERSION.txt`, addon TOC
   (`## Version: 4.5.0` / `## X-Codename: Voidlight`) and `/mcb` report the
   companion 4.5.1 / bridge 4.5.0 pair.

### 3r. 4.5.1 "Voidlight" live resize + wrapped text (live 12.1 retail, OWED)

Offline (see HANDOVER/final report): build 0/0, `dotnet test`, snapshots at
520/660/1100 DIP, live resize capture with Details open. Live checklist:

1. **Resize with Details open.** Drag the window smaller and larger with the
   disclosure open: the changelog re-clamps on every step, the log keeps a
   readable reserve, and nothing is clipped by the card edge.
2. **Scroll affordance.** At a short height the Details body shows the slim
   amethyst thumb and the wheel scrolls the changelog to the `Earlier:` line;
   at a tall height the thumb disappears and the whole changelog is visible.
3. **Log wrapping.** A long `[spec]` / status / bridge line wraps at the card
   width with continuation lines aligned under the message, at 520 and 660 DIP
   and at 150 % Windows scaling.
4. **Narrow stacking.** Below ~560 DIP the console's status/update cards stack
   into two rows (the body scrolls), and below ~500 DIP the classic toggle rows
   stack into one column.
5. **Tier chrome.** Widening past 950 DIP grows the transport pills (42 -> 52
   DIP) and type; shrinking returns to 42 without clipping the labels.

### 3s. 4.5.2 "Voidlight" resize frame (live 12.1 retail, OWED)

1. **Resize arrows.** Hover each edge and corner of the window: the cursor
   becomes the horizontal / vertical / diagonal resize arrow (not the plain
   pointer), including the top-left and top-right corners.
2. **Drag resize.** Drag every edge and corner: the window follows, respects the
   520x560 DIP minimum, and the XAML content (pill, transport, card, log) reflows
   during the drag.
3. **Drag-to-move.** Dragging the title bar below the top 8 DIP moves the
   window; dragging within the top 8 DIP resizes from the top instead.
4. **Caption buttons.** Minimize/close still receive clicks, and the 8 px above
   them resizes the window.

### 3t. 4.5.3 "Voidlight" toggle visibility + hold reasons (live 12.1.5, OWED)

1. **Toggles on first sight.** Start the engine with the game foreground: the
   log prints one `[toggles] ...` line summarising the 14 addon toggles, and
   Details shows `toggles: ...`.
2. **Main off is loud.** Turn the in-game **Main** pill OFF: the status card
   reads `holding - Main off in game` (warning tone) and the log prints
   `[toggles] Main OFF - rotation suspended (turn it back on: /mcb t Main on)`.
   Turn it ON again: `holding` clears and the log prints
   `[toggles] Main ON - rotation resumed`.
3. **Other toggles.** Toggle Trinket/Defensive in the overlay: the log prints
   `[toggles] Trinket OFF` (or `ON`) and the Details summary updates.
4. **Other hold reasons.** Typing in chat shows `holding - typing in game chat`;
   out of combat shows `holding - out of combat`; a frozen bridge heartbeat shows
   `holding - link lost - addon heartbeat`.
5. **Recovery command.** `/mcb t Main on` flips the master switch and the
   companion resumes without a restart.

### 3u. 4.5.4 "Voidlight" secret-cooldown flood guard (bridge 4.5.1, live 12.1.5, OWED)

0. **Root fix first.** If GW2_UI is installed, run
   `pwsh tools/patch-gw2ui-taint.ps1` once (it backs up
   `Core/API/utils.lua` and `Games/Modern/Actionbar/extraAB_bossAB.lua` as
   `*.maxdps-backup-4.5.4`), then `/reload`. This comments out the
   `OverrideActionBar` hooks that taint the override buttons; the bridge guard
   alone cannot untaint an already-poisoned button.
1. **Reproduce the old condition.** In a restricted/instance fight (where
   `C_Secrets.ShouldCooldownsBeSecret()` is true) let the companion press. The
   game must **not** log the repeating
   `ActionButton.lua:847 SetCooldown ... Secret values are only allowed during
   untainted execution` error, and must not log the matching
   `ADDON_ACTION_BLOCKED ... SetAttribute` for the assisted-combat button.
2. **Guard line.** The in-game chat/console prints one
   `[taint] secret cooldowns are active - guarding the assisted-combat refresh`
   and one
   `[taint] paused the assisted-combat icon refresh on OverrideActionBarButtonN
   (tainted by <addon>)` (the N and addon depend on the bar setup).
3. **Rotation unchanged.** The companion still presses the same spells at the
   same cadence; `MaxDps.Spell` / the strip stay live, and the SBA API still
   returns suggestions. Only the Blizzard assisted-combat icon refresh on that
   button is paused.
4. **Diagnostics.** `/mcb guard` prints `secret=yes`, the assisted frames it
   found, their parent buttons, the taint source when the client reports one,
   and the loaded bar addons. With no assisted frame present it prints
   `no assisted-combat frames on the standard bars`.
5. **Opt-out.** `/mcb guard off` prints `[taint] guard is OFF`, the stock
   Single-Button Assistant refresh is no longer paused, and the error may
   return; `/mcb guard on` re-arms it.
6. **Clean setup.** On a character/UI with no tainted bar addon the main-bar
   assisted frame is left alone (`paused=0` while secrets are active), so the
   stock SBA icon still updates normally.

## 4. macOS client (live retail, OWED)

Everything in this section needs a real client on a Mac. Static/offline
results never close these items.

Preconditions: the **MaxDps Companion Bridge** addon (`addon/MaxDpsCompanionBridge`)
installed and enabled, the legacy `MaxDpsBridge` **disabled** (the companion bridge
is inert while the legacy one is loaded), Screen Recording + Accessibility granted
to the app bundle.

```sh
APP="/Applications/MaxDPS Companion.app/Contents/MacOS/MaxDpsCompanion.Mac"
"$APP" --probe          # writes ~/Library/Application Support/MaxDpsCompanion/probe.txt
```

| # | Step | Expected |
| :--- | :--- | :--- |
| 4.1 | `--probe` with the game running | `capture-backend=ScreenCaptureKit ... granted`; `stream=started region=1024x128pt`; `locate=<x>,<y> cell=8`; `decode=proto=5 state=...` |
| 4.2 | `--probe` again | `stream=after-locate regionPx` small (~352x16 for 8 px cells); frames/fps reported |
| 4.3 | `--probe` on the other display (1080p monitor <-> MacBook panel, incl. a Retina-scale display) | locate succeeds at the same window-relative offsets; no recalibration needed |
| 4.4 | Start in the UI, game focused, press a key the addon suggests | the status line's "Now:" matches the pressed ability; no stray input in other apps |
| 4.5 | Start, then focus another app while a keyboard slot fires | stroke still lands in the game (per-process delivery), nothing leaks |
| 4.6 | A mouse-slot ability (if bound) while the game is unfocused | hold with the "needs focus (mouse input)" reason (by design) |
| 4.7 | Watch `repairs` over a session (`--probe` after playing) | ideally 0; non-zero means the display's ICC pipeline distorts the strip (report the count and the display profile) |
| 4.8 | `/mcb status` in game | `cell=8`, offsets match the app's located values; protocol 5 |
| 4.9 | Change the strip (`/mcb cellsize 12`, or move the offset), then Start | the ladder re-locates within ~5 s and the stream region shrinks to the new strip |
| 4.10 | Move the game window / switch resolution while running | the supervisor rebuilds the stream within ~1 s; the strip keeps decoding |
| 4.11 | `--bench-capture=10` while playing | steady-state region ~20-40 KB/frame; per-sample mean well under 0.5 ms; our CPU near 1 % of one core |
| 4.12 | Quit the app after playing | no capture session left behind (`Activity Monitor`: no SCK helper burning CPU; app gone) |

### 4.0a Release asset provenance + bundle checks (offline, no game)

For the `v4.5.5` macOS release, before launching:

```sh
shasum -a 256 -c SHA256SUMS-macos.txt
codesign --verify --deep --strict "/Applications/MaxDPS Companion.app"
lipo -info "/Applications/MaxDPS Companion.app/Contents/MacOS/libmdc_capture.dylib"
```

Expected: all checksums PASS; the signature verifies; `lipo` reports `arm64`.
`BUILD-INFO-macos.txt` must read `version=4.5.5`, `codename=Voidlight`,
`source_tag=v4.5.5`, `source_commit=dfb9839...`, `arch=arm64`,
`addon=MaxDpsCompanionBridge 4.5.1`, and a `version_props_source` of either
`tag` or `workflow:<sha> (tag file was 4.4.0)`. The app subtitle reads
`v4.5.5 Voidlight - Mac`. This proves the download is the 4.5.5 build; it does
**not** close any live retail item below.

### 4.0 Offline app-level capture check (no game needed)

A synthetic window exercises the real capture path without WoW. Build a stub
whose executable is named `Wow` (any process whose name matches the app's
window list: `World of Warcraft`, `World of Warcraft.app`, `Wow`) drawing a
43-cell strip (cell size 8, magenta magic cell + `n*17` channel ramps) at its
top-left corner, then:

```sh
"/Applications/MaxDPS Companion.app/Contents/MacOS/MaxDpsCompanion.Mac" --probe
```

Expected: `process=... bounds=...`; `diag frame:` shows the window origin and
`region=0,0 ...pt`; `diag trace:` shows `run=8 step=8` and the sampled first
cells matching the drawn pattern (magenta 255,70,255 at cell 0, then 17/33/51);
`locate=null ... fault=Version` is CORRECT for a stub (it has no valid v5
checksums). If the sampled cells are blank/shifted, the geometry or the blank
frame handling regressed. `--bench-capture=3` must also run (it reports "strip
not found" for a stub).

## 5. Benchmarks / diagnostics (no game)



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
MaxDpsCompanion.exe --ui-smoke-test              # P2-E hosted window: ShellValidation visual-tree walk + RenderTargetBitmap
                                                 # PNG (ui-smoke.png, non-blank colour grid) + ui-smoke.txt/layout; exit 3 on findings
MaxDpsCompanion.exe --ui-smoke-test --exp        # same in the in-game-config shell (asserts ZERO ToggleSwitch mounted)
MaxDpsCompanion.exe --ui-snapshot-page=main --ui-snapshot=main.png --ui-snapshot-width=1280
                                                 # pages: main|configuration|diagnostics|intelligence|abilities
                                                 # writes ui-snapshot.txt + the PNG; exit 3 + ui-error.txt on blank/unknown
MaxDpsCompanion.exe --ui-snapshot=<png>          # whole shell as shown (1280x900 default; width/height mods apply)
MaxDpsCompanion.exe --ui-snapshot-advanced=<png> # Advanced overlay (Configuration tab)
MaxDpsCompanion.exe --bench-ui                   # startup/refresh/snapshot timings -> bench-ui.txt (exit 1 on target miss)
                                                 # 4.0.0 KNOWN MISS: startup 774.9 ms > 500 ms target;
                                                 # refresh mean 19.8 us / p95 29.6 us and snapshot 26.3 ms pass
                                                 # targets: mean < 300 us, p95 < 1000 us, construct+show < 500 ms
pwsh tools/ui_smoke.ps1                          # runs the bin exe --ui-smoke-test; checks exit 0 + ui-smoke.png
MaxDpsCompanion.exe --dump-class-skills=<path>   # merged class/spec skill tree
                                                 # CLASS<TAB>SPEC<TAB>section<TAB>provenance<TAB>name<TAB>id
                                                 # DROPPED in 4.0.0 (print "unavailable in this build", exit 3):
                                                 # --ui-snapshot-class-skills=, --ui-snapshot-scroll=
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
luac -p addon/MaxDpsCompanionBridge/*.lua addon/MaxDpsCompanionBridge/InGame/*.lua  # Lua syntax (root + InGame)
dotnet build -c Release              # 0 warnings / 0 errors is the bar
```

