# Handover — MaxDps-Companion

## Status: v1.2.0 — Spell Frame slot parity + grouped hero (build green, snapshots green, live verify after)

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
