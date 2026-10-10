> [!IMPORTANT]
> **LEGACY:** The C# WinForms client (`app/MaxDpsCompanion/`), the legacy
> distribution (`dist/`) and the stable bridge (`addon/MaxDpsBridge/`) are
> **frozen**. They receive no updates except critical security fixes.
> The future is the Rust companion in **`companion-rs/`**, targeting
> `MaxDpsBridgeExp` + `dist-exp`. See [`LEGACY.md`](LEGACY.md) and
> [`docs/plans/2026-10-10-rust-companion.md`](docs/plans/2026-10-10-rust-companion.md).

> [!TIP]
> **Rust Companion (Beta) — `v3.8.0-rust-beta`:** the new Rust companion
> (`companion-rs/` → `dist-exp/mdc-app.exe` + `mdc-cli.exe`) targets
> **`MaxDpsBridgeExp` + `dist-exp`** and is the current experimental build
> (supersedes `v3.8.0-rust-alpha`). Requirements: **Windows 11 x64**, WoW
> **retail**, and the **MaxDps** + **`MaxDpsBridgeExp`** addons installed via
> `pwsh tools/install-addon-exp.ps1`; run `mdc-app.exe` with `settings.ini`
> beside it, then use the console transport (Start/Pause/Stop/Calibrate/OPEN GAME)
> and the Settings / Doctor / Class Browser / Telemetry panels.
> **Works (offline-verified):** `cargo build`/`test`/`clippy` clean, Gallant
> parity console + real panels. **OWED (live retail, unproven):** full
> Start → sample → decode → keypress loop, the mouse/wheel foreground gate, and
> Calibrate against real DPI/borders. Static build ≠ automated test ≠ live E2E.
> **Download:** [v3.8.0-rust-beta](https://github.com/AnBPublic/MaxDps-Companion/releases/tag/v3.8.0-rust-beta);
> see [`dist-exp/README.md`](dist-exp/README.md),
> [`docs/plans/2026-10-10-rust-companion.md`](docs/plans/2026-10-10-rust-companion.md)
> and [`docs/plans/2026-10-10-gallant-parity.md`](docs/plans/2026-10-10-gallant-parity.md).

# MaxDPS Companion (Retail Midnight 12.1, v3.7.7 "Gallant")

Pixel bridge driver for [kaminaris MaxDps](https://www.curseforge.com/wow/addons/maxdps)
(vendor pin: MaxDps v11.3.49). No memory read, no injection, no OCR, no LLM.
It reads what MaxDps suggests through a pixel strip and presses the player's
own bound keys via `PostMessage` into the game window only.

Two pieces:

 | Piece | What it does |
 | :--- | :--- |
 | `MaxDpsBridge` (addon, `addon/MaxDpsBridge/`) | Queries the MaxDps rotation engine each frame and encodes suggestions, ability ids and combat context into a 40-cell strip of flat-coloured pixels (35-cell v5 core + additive Ext2 block). |
 | `MaxDpsCompanion.exe` (desktop app, `app/MaxDpsCompanion/`) | Samples those pixels, decodes the frame, evaluates every situational suggestion (USE / HOLD / SKIP / UNAVAILABLE / UNKNOWN) against an embedded ability intelligence registry + explicit candidate providers, schedules one action at a time, and replays the player's own keybinds into the attached game window. |

## v3.7.7 "Gallant" — version-only identity bump (no behaviour or wire change)

Companion **and** bridge version bump with **no protocol change**: `PROTOCOL`
stays at 5 and every cell is byte-identical. New dedicated codename **Gallant**
(Vigil/Warden/Fullcover/Holdfast/Onslaught/Reaver/Vindicator/Templar are never
reused). csproj `<Version>`/`<Codename>`, the `Native.cs` fallback, `MDB.VERSION`,
`MDBX.VERSION`, both TOCs, both addon `VERSION.txt` files, the repo `VERSION.txt`,
this README, `ARCHITECTURE.md`, `docs/UI.md`, `docs/TESTING.md` and
`ReleaseIdentityTests` all read 3.7.7 / Gallant. The title bar reads
`MaxDPS Companion v3.7.7 Gallant`.

## v3.7.6 "Templar" — identity bump + offensive gap-fill verification fix

Identity bump to the new dedicated codename **Templar**, plus a data-only
offensive gap-fill fix: `abilities.json` 382245 renamed `Cold Blood` ->
`Cold Blooded Killer` (live SpellName), and live-unverified ids 370452 (Evoker
Devastation) / 392060 (Hunter Marksmanship) removed from the companion
gap-fill lists (rows kept; the MaxDps fork un-comment still routes them on the
wire). `PROTOCOL` stays at 5 and every cell is byte-identical. csproj
`<Version>`/`<Codename>`, the `Native.cs` fallback, `MDB.VERSION`, both TOCs,
both addon `VERSION.txt` files, the repo `VERSION.txt`, this README,
`ARCHITECTURE.md`, `docs/UI.md`, `docs/TESTING.md` and `ReleaseIdentityTests`
all read 3.7.6 / Templar. The title bar reads `MaxDPS Companion v3.7.6 Templar`.

## v3.7.5 "Vindicator" — version-only identity bump (no behaviour or wire change)

A companion **and** bridge version bump with **no protocol change**: `PROTOCOL`
stays at 5 and every cell is byte-identical. The codename moves to the new
dedicated **Vindicator** (Vigil/Warden/Fullcover/Holdfast/Onslaught/Reaver are
never reused). csproj `<Version>`/`<Codename>`, the `Native.cs` fallback,
`MDB.VERSION`, both TOCs, both addon `VERSION.txt` files, the repo `VERSION.txt`,
this README, `ARCHITECTURE.md`, `docs/UI.md`, `docs/TESTING.md` and
`ReleaseIdentityTests` all read 3.7.5 / Vindicator. The title bar reads
`MaxDPS Companion v3.7.5 Vindicator`.

## v3.7.4 "Reaver" — version-only identity bump (no behaviour or wire change)

A companion **and** bridge version bump with **no protocol change**: `PROTOCOL`
stays at 5 and every cell is byte-identical. The codename stays **Reaver**.
csproj `<Version>`, `MDB.VERSION`, both TOCs, both addon `VERSION.txt` files, the
repo `VERSION.txt`, this README, `ARCHITECTURE.md`, `docs/UI.md`,
`docs/TESTING.md` and `ReleaseIdentityTests` all read 3.7.4 / Reaver. The title
bar reads `MaxDPS Companion v3.7.4 Reaver`.

## v3.7.3 "Reaver" — version-only identity bump (no behaviour or wire change)

A companion **and** bridge version bump with **no protocol change**: `PROTOCOL`
stays at 5 and every cell is byte-identical. The codename moves to the new
dedicated **Reaver** (Vigil/Onslaught are never reused). csproj
`<Version>`/`<Codename>`, the `Native.cs` fallback, `MDB.VERSION`, both TOCs,
both addon `VERSION.txt` files, the repo `VERSION.txt`, this README,
`ARCHITECTURE.md`, `docs/UI.md`, `docs/TESTING.md` and `ReleaseIdentityTests`
all read 3.7.3 / Reaver. The title bar reads `MaxDPS Companion v3.7.3 Reaver`.

## v3.7.2 "Onslaught" — Arms + Fury execution fix (un-deny + offense routing + history release, no wire change)

A companion **and** bridge version bump with **no protocol change**: `PROTOCOL`
stays at 5 and every cell is byte-identical. Colossus Smash 167105 is no longer
denied (the 12.1 stale-id guess was wrong — it is a ~45 s Arms rotation button),
so Arms MAIN can encode it again, with the new `MainFallback.lua` Arms filler
(Mortal Strike 12294 then Overpower 7384). Ravager 228920 is added to
`MDB.MajorCDDeny` (never MAIN) and to `MDB.FlagOffensiveExtra`, which
`Reader.CategoryOf` maps to `"offensive"` so `FirstFlagged("offensive")` routes
it into the Offensive slot. On the companion, `TtkPolicy.LiveReleasesHistory`
releases `HistoryWasteGuardHolds` when a valid live TTK >= the need, or (no
valid live TTK) the target is >= 8 s old with HP known and >= 85%, so Fury
Recklessness / Avatar / Ancestral Call fire on long-lived rares while
trash-learned majors stay conserved. The title bar reads
`MaxDPS Companion v3.7.2 Onslaught`.

## v3.7.1 "Vigil" — no-downtime MAIN (usable-glow scan + Fury filler, no wire change)

A companion **and** bridge version bump with **no protocol change**: `PROTOCOL`
stays at 5 and every cell is byte-identical. The MAIN slot can no longer be
stranded: `Reader.GetMainSpellID` collects every non-denied glowing id, sorts
ascending, skips power-starved picks (`C_Spell.IsSpellUsable`), and falls
through to the new `MainFallback.lua` per-spec filler (Fury → Bloodthirst
23881) instead of a dead Main slot. The scheduler re-probes a failed Main at
`MainReprobeMs = 400` (`MainSameSpellNoOpCap = 3`) and `CandidateTracker`
refreshes the sole Main TTL while the frame heartbeat stays fresh. The fallback
is a deliberate, user-approved exception to "the bridge only encodes what
MaxDps suggests"; unlisted specs stay empty. The title bar reads
`MaxDPS Companion v3.7.1 Vigil`.

## v3.7.0 "Vigil" — custom MaxDps 12.1 fork (bridge denylist + reversible sync, no wire change)

A companion **and** bridge version bump with **no protocol change**: `PROTOCOL`
stays at 5 and every cell is byte-identical. A stale major cooldown can no
longer be encoded as the MAIN rotation pick: the new `MajorCooldowns.lua`
denylist makes `GetMainSpellID` skip stale/current major-CD ids, and the
Offensive slot stays an independent candidate so an empty Main cannot stall the
rotation. `custom/` vendors the untouched MaxDps v11.3.49 snapshot, a 75-entry
declarative patch manifest and `tools/Sync-CustomMaxDps.ps1`, which applies
patches into a generated, reversible `custom/out/` and publishes to `AddOns`
only on a clean run (with a `_backup/` rollback). `vendor/` is never edited.
The title bar reads `MaxDPS Companion v3.7.0 Vigil`.

## v3.6.0 "Warden" — taint-safe probe, TTK buff gating, unified masks (no wire change)

A companion **and** bridge version bump with **no protocol change**: `PROTOCOL`
stays at 5 and the Ext3 layout stays byte-identical. Three changes ship
together. (1) **Taint-safe melee probe:** `CheckInteractDistance` is
`#nocombat`-restricted, so it now has exactly one call site
(`Reader.MDB.ProbeTargetMelee`), event-gated and per-target cached; the Bridge
consumes that result and never calls it itself, and an `ADDON_ACTION_BLOCKED`
on the call backs off then disables the probe for the session. (2) **TTK-aware
buff gating:** a major offensive cooldown is held `"warming up TTK"` while the
target is younger than `[TimeToKill] WarmupSec` (default 3 s; 0 = legacy) and
TTK is unknown, and the adaptive-need input is the ability's own buff duration.
(3) **Unified masks:** one `BuildUnifiedPopup` shell builds both the Advanced
and Class-browser masks. The title bar reads `MaxDPS Companion v3.6.0 Warden`;
the build hash/time remains on the Advanced Diagnostics install doctor card
only. Both app and addon move together because `InstallDoctor` warns on a
version mismatch.

## v3.5.2 "Fullcover" — single full-window mask (UI only, no wire change)

A UI-only release with **no protocol change**: `PROTOCOL` stays at 5 and the
Ext3 layout stays byte-identical. The Advanced and Class-browser masks are now
parented to the top-level window and track its client rectangle, and the old
two-pixel form padding is gone, so **one opaque layer covers the entire
window** — no background ring or menu peeks at any edge. The 24/10/24/12 inset
moved from the gradient canvas onto the classic body scroll, so the gradient
ring surrounds the toggle card rather than the window. The title bar reads
`MaxDPS Companion v3.5.2 Fullcover`; the build hash/time remains on the
Advanced Diagnostics install doctor card only. Both app and addon move together
because `InstallDoctor` warns on a version mismatch.

## v3.5.1 "Holdfast" — semantics-only (no wire change)

A companion **and** bridge version bump with **no protocol change**: `PROTOCOL`
stays at 5 and the Ext3 layout stays byte-identical. It ships the semantic
flips: the companion now holds **fail-closed out of combat** unless the bridge
echoes the Ext3 OOC mirror bit, and toggle authority is **addon-wins** (the
in-game overlay is authoritative; the app never auto-pushes a mask). The title
bar reads `MaxDPS Companion v3.5.1 Holdfast`; the build hash/time moved from the
title bar into the Advanced Diagnostics install doctor card. Both app and addon
move together because `InstallDoctor` warns on a version mismatch.

## v3.5.0 — reliable coverage, Class Browser, Console home

The v3.5 release closes the "toggled skills never fire" defects (RC1–RC7) and
rebuilds the UI around them. The bridge now rotates **every** ready+bound+
policy-eligible candidate per slot instead of only the first, so a held gap
closer no longer starves an escape; Escape/Movement route to the Mobility
provider; and the scheduler can preempt on a brief GCD-off tick. Toggles are
**app-wins**: the companion pushes its 14-bit mask to the addon and the bridge
echoes the effective mask in the new additive **Ext3** block (43-cell strip,
cells 40–42; nibble stays 5, a pre-3.5 companion ignores it). The Class Browser
replaces the separate Class skills/Explorer tabs (Class|Spec|Mode, knobs,
virtualized owner-drawn rows, live why-held), and the default view is a
read-only Console home with rolling log and five named presets. Per-class
verified overlays (`Knowledge/classes/<CLASS>.json`, 13 classes) plus an
override store give every spec a companion-owned path. Perf/diagnostics add
off-thread browser precompute, cached scaled icons, a why-not-firing panel and
an install doctor. Protocol: v5 core + Ext2 + additive Ext3 (no v5 change).
Offline-proven; live retail checklist stays OWED (`docs/TESTING.md` §3).

## Intelligence coverage (v2.7.0, unchanged in 2.8)

Every registry entry now answers two independent questions: **who owns the
when-to-use decision** (Companion / MaxDps / Shared / Manual / Unavailable)
and **how complete that knowledge is** (Complete / Partial / Delegated /
ManualByDesign / Unobservable / ResearchPending / LiveUnverified). Every
MaxDps-owned entry carries an explicit delegation reason, every manual entry a
manual reason, and every entry carries patch metadata (introduced / source /
last-validated / source type / confidence / live-verified). A generated
coverage manifest reports DISCOVERED / REGISTERED / AUTOMATABLE /
COMPANION-GENERATED / MAXDPS-DELEGATED / MANUAL / RESEARCH-PENDING / STALE /
MISSING - so "3,279 entries" can never be mistaken for "3,279 intelligent
abilities".

```
MaxDpsCompanion.exe --ability-audit=<path>          # machine-checkable report; the audit tool enforces Violations 0 / Warnings 0 / Missing 0 / Stale 0
MaxDpsCompanion.exe --ability-coverage=<path>       # coverage manifest (JSON, stable classes)
MaxDpsCompanion.exe --ability-info=<spellId>        # inspect one ability (same canonical model the UI shows)
MaxDpsCompanion.exe --ability-search=<text> [--ability-class=CLASS --ability-spec=SPEC]
```

Explicit candidate providers (MaxDpsRotation / Offensive / Defensive /
Interrupt / Mobility / SelfSustain / Utility) own the per-category decision
logic, record where a candidate came from (MaxDps wire / bridge extra /
companion gap-fill / none) and attach structured "why" evidence. The Utility
provider is structurally incapable of firing. Verdicts and reason strings are
unchanged, and the scheduler hash is pinned (`b71a999d5e46570e`).

## Classic UI (v3.0.0)

A fast fixed 660-wide classic shell replaces the v2.8.1 rail/pages layout: one
non-scrolling main window whose hero card shows status, the live action with a
human-readable why, the sampled strip, and two-toggle rows for the Spells and
Modes. `Start` / `Stop` / `Launch Game` sit under it, with `Recalibrate`,
`Abilities…` and `Advanced…`. `Advanced…` opens a tabbed popup (Configuration |
Diagnostics | Intelligence) and `Abilities…` hosts Class skills | Explorer;
`Esc` closes the topmost. The shell never owns engine state. `--bench-ui`
measures 2000 refreshes (mean/p95 µs + startup ms); 12 snapshots (6 pages ×
{660×920, 520×560}) live in `dist/ui-snapshots`. Architecture: `docs/UI.md`.

```
MaxDpsCompanion.exe --ui-smoke-test
MaxDpsCompanion.exe --ui-snapshot-page=main --ui-snapshot=main.png --ui-snapshot-width=660 --ui-snapshot-height=920
```

## Gap-fill + reset-aware sustain (v3.1.0)

v3.1.0 closes two gaps. The Offensive slot gains a curated per-spec gap-fill
list (1–4 true burst cooldowns, shared burst first) so a cooldown the user
toggled ON fires even when MaxDps never surfaces it, and the defensive
gap-fill opens a short-cooldown (Orange) tier while majors still wait for Red.
Self-sustain is now reset-aware: self-heal readiness is re-read every tick, a
reset proc re-offers the same spell, a transient failed press is capped at
1.5 s, and a ready heal is never demoted behind the rotation (a cooldown wait
is reported as `SelfHealCoolingDown`). The classic shell also scales by width
tier (Compact/Classic/Roomy/Wide) and measured content height. Protocol is
unchanged (v5 core + additive Ext2); all of it is offline-proven — the live
retail checklist stays OWED.

## TTK intelligence (v3.2.0)

A pure, fake-clock estimator derives a per-target time-to-kill from the target
HP band already on the wire (no wire change, no Lua change) and uses it to stop
wasting cooldowns: **T1** holds a major offensive while the target dies before
the cooldown pays off, **T2** fires early (bypassing the pairing hold) when the
fight is long enough for two full uses, **T3** fires a favored execute cooldown
below its HP threshold, and **T4** saves a non-emergency Solo defensive when the
target dies imminently. An unknown/invalid estimate fails open (every gate is
skipped), and `[TimeToKill] Enabled=0` disables the whole feature. Telemetry
records `ttk`/`thp`/`ttkMs` so replay rebuilds the estimator deterministically.

## Ability intelligence (v2.6.0)

Every ability in the registry carries an explicit intelligence level -
Verified / Research-backed / MaxDps-backed / Companion rule / Manual by
design / Incomplete / Unknown - plus what MaxDps thinks of it and when the
companion may act on it. Anything without real intelligence says so and can
never be invented by the companion: the 2971-entry modeled tail only ever
fires when MaxDps itself suggests it. Manual utilities (CC, purges, dispels)
never fire automatically. An emergency self-heal (at or below 35% HP) now
outranks the rotation even outside Solo mode; the wider self-sustain layer
stays Solo-only.

The Class skills screen shows each row's status, tier and cooldown with a
why-tooltip; full reference: `docs/research/ABILITY_REGISTRY.md`.

MaxDps remains **the only rotation source**. The companion is the execution
and context layer: the main rotation is authoritative and always stays
responsive; situational abilities (offensive, defensive, interrupt,
consumable, trinket, mobility, self-heal) are only pressed when the policy
says now is the right time.

## Protocol v5 35-cell core + Ext2 40-cell block

Each cell is `CellSize` physical pixels square; every channel carries one
nibble (`nibble * 17`). Slot names mirror the in-game Spell Frame categories.
The wire version nibble **stays 5**: both the defensive-urgency nibbles
(bridge 2.3) and the Ext2 block (bridge 3.0.0) are **additive** on reserved
bits, so an updated addon still decodes with an older companion exe and a
stale 35-cell addon still drives the current exe. Full spec:
`docs/PROTOCOL.md`.

| Cells | Contents |
| :--- | :--- |
| 0 | magic (presence / alignment / calibration reference) |
| 1-8 | slots: Main, Offensive, Defensive, Consumable, Trinket, Interrupt, Mobility, SelfHeal (key hi/lo + modifier flags) |
| 9 | state + heartbeat + status flags (in-combat, on-GCD, has-target, context-valid) |
| 10 | protocol version + core checksum + commit |
| 11-26 | per-slot 24-bit spell id (0 = unknown identity) |
| 27 | player HP% (or unknown) |
| 28 | player cast/channel state + Ext2 SelfHeal2 range (bits0-1) |
| 29 | target: melee flag, HP band, cast/interruptible flags |
| 30-31 | per-slot range tri-state (unknown / in / out) + additive HP-curve defensive urgency + gap-fill source bit |
| 32 | per-slot self-buff-active bits + additive stagger-curve defensive urgency |
| 33 | class + spec wire ids (bit0 valid, bit1 buff block valid, bit2 Ext2 present, bit3 HP curve active) |
| 34 | extension checksum + commit |
| 35 | Ext2 HP curve (a live reading when bit3 is set, otherwise black; never checksummed) |
| 36-38 | Ext2 SelfHeal2 key + 24-bit spell id (the next distinct ready+bound self-heal) |
| 39 | Ext2 checksum over cells 36-38 (a failure drops only SelfHeal2) |

The companion decodes the 40-cell Ext2 frame, v5/v6 (35 cells), v4 (9 cells)
and v1 (8 cells): a stale in-game addon degrades gracefully (urgency and
context UNKNOWN → fail-open/conservative) and the app says so instead of going
blind. The Ext2 **HP curve** gives Solo a coarse HP reading when the game
hides cell 27 (priority: plain cell 27 > curve > unknown); the **SelfHeal2**
slot is a second, independently ranged self-sustain candidate. The self-buff
block is tri-state since bridge 2.7. Solo mode itself is unchanged from v2.8:
`[Solo] Enabled=1` runs the survival layer (sustain at ≤65%, emergency at
≤35%, self-heals as an independent candidate source) — see the Solo section
below. The addon is installed by `install-addon.ps1`; after updating always
`/reload` (or `/mdb status` to confirm `protocol=5 ext2=1`).

## In-game commands (`/mdb`)

| Command | Effect |
| :--- | :--- |
| `/mdb status` | Offset, cell size, state, bound slots, class/spec + extras; `ext2=1 hpcurve=<on\|off> sh2=<id\|->`. |
| `/mdb on` / `/mdb off` / `/mdb toggle` | Enable / pause the bridge. |
| `/mdb offset <x> <y>` | Move the strip in physical pixels from top-left. |
| `/mdb cellsize <px>` | Resize cells (1–64; 8 default/recommended). |
| `/mdb calibrate on\|off` | Show/hide the 54-step calibration pattern. |
| `/mdb heal` | Per curated self-heal entry: known / ready / usable / key / why (slot-8 + SelfHeal2 selection). |
| `/mdb hpcurve on\|off` | Ext2 HP-curve cell on/off (SavedVariables, default on). |
| `/mdb diag` | Scrubbed MaxDps internals snapshot. |
| `/mdb reset` | Back to defaults (clears calibrate mode). |

## Combat intelligence (v2.0/v2.1, `[Intelligence] Enabled=1`)

The core of the product. A deterministic, non-LLM policy layer:

```
MaxDps candidate → context evaluation → ability knowledge → policy
  → USE / HOLD / SKIP → scheduler → executor
```

- **Execution safety first (v2.1).** While the player is casting OR
  channeling, every GCD-riding ability is held — including the main
  rotation, because a channel is not ours to clip. Only interrupts, item
  activations and curated non-movement off-GCD abilities may fire during a
  cast/channel. This gate runs in every mode (policy on/off, scheduler
  on/off) on protocol v5/v6 frames; it is the only thing besides target range
  allowed to hold the main rotation.
- **Main rotation** stays authoritative. Only two gates exist: a clearly
  out-of-range target (skip) and an active cast/channel (hold; the next
  suggestion fires the moment it ends).
- **Defensives (v2.3)** use MaxDps's own flagged/usable recommendation as the
  trigger and the **rendered glow urgency** as the gate: at full health
  (White) no defensive fires and the rotation continues; a short-CD defensive
  fires from Yellow; a major needs Red unless it is the exact ability MaxDps
  is currently recommending (one-stage discount, never below Yellow);
  emergency HP overrides. The policy still prevents waste: a defensive
  already running is skipped, overlapping mitigation is sequenced (smallest
  sufficient first, escalate when HP keeps dropping), and reflect-type
  abilities (Spell Reflection, Diffuse Magic) need an observed incoming
  cast. Urgency is the vendor's own colour curve, not invented HP bands; the
  bridge stages it at the curve's anchors (docs/PROTOCOL.md, §6 of the
  research doc).
- **Offensives** never fire during a cast/channel and never into a curated
  pairing window or their own active buff. (Offensive cooldown scheduling
  itself stays with MaxDps's own engine — stacking is intentional.)
- **Interrupts** rank first, bypass the GCD and the key interval when the
  suggestion changes, and are range-gated. The bridge vetoes a kick when the
  target's cast was observed as NOT interruptible, and the policy skips a
  stale suggestion (no live cast). (Blizzard exposes no cast-school or cast
  identity for enemy units, so "important vs trivial cast" cannot be
  distinguished — documented limit.)
- **Mobility** (curated gap closers: Charge, Shadowstep, Harpoon, Death
  Grip, …) only fires when the target is confirmed outside melee, the
  ability is confirmed in range, and combat is active. Escape / movement
  utility (Blink, Vanish, Disengage, Sprint) is never automatic.
- **Self-heals** exist only in Solo mode (below); a running immunity and an
  overheal guard both conserve the heal. The ability's own preconditions also
  gate it: a melee self-heal with a confirmed out-of-range target is skipped,
  a target-requiring heal holds without one, and an ability curated as
  never-automatic stays manual.
- **Unknown means unknown.** Midnight secret values degrade each sensor to
  UNKNOWN, and every rule has a documented fallback (fail-open to MaxDps's
  own gate for MaxDps-gated slots, hold for companion-only slots). A missed
  cast-stop event is watch-dogged to UNKNOWN after 15 s instead of holding
  the rotation forever.

## Defensive automation (v2.3, `[Abilities]`)

MaxDps renders each defensive through a 3-point colour curve
(Red ≤30% HP / Orange 30-50% / Yellow 50-100% / no glow at 100%); Purifying
Brew uses the reversed stagger curve instead. The bridge stages that rendered
colour into the protocol, and the companion maps it to a per-ability gate:

| Urgency | Behaviour |
| :--- | :--- |
| White (full HP) | every automatic defensive holds; the rotation continues |
| Yellow | a short-CD defensive may fire; a major holds |
| Orange | a major holds unless it is the exact ability MaxDps is recommending (one-stage discount) |
| Red (≤30% HP) | a ready major fires; if MaxDps names no bound defensive, the catalog's derived gap-fill list may supply one (Major first, immunities excluded) |
| Unknown | majors hold; minors keep the legacy gates |

The **Class skills** screen (below) shows a generated class → spec → ability
list with a toggle per ability; the old inline Advanced list moved there.
**OFF never fires** — urgency, a MaxDps
recommendation, Solo mode and emergency HP cannot override it. **ON is only
eligibility** (White still holds, ON is never spam). Defaults are curated:
manual-by-design abilities (externals, escapes, dispels) start unchecked, and
a user can explicitly enable one (e.g. Vanish), which then stays
emergency-only. This whole path lives inside MaxDps's own `enableDefensives`
switch, so muting defensives upstream mutes the gap-fill too. See
`docs/KNOWLEDGE.md` and `docs/PROTOCOL.md`.

## Class skills (per-ability toggles)

A full-size screen opened from the main menu ("Class skills…", next to
"Advanced…") and from the Advanced page. Pick a class and spec; abilities are
grouped into a "Shared — all <class> specs" section, then per-spec Main
rotation / Offensive / Defensive / Movement.

- The data is the same knowledge base: curated `abilities.json` > vendor
  `Cooldowns.lua` > the generated class-spells book (`class-spells.json`, the
  retail `ns.classSpellData` block). Each row shows the 44×44 game icon, the
  ability name and a "Recommendation: On/Off" subtitle; spell id, source and
  default live only in the hover tooltip.
- The book is cross-checked against the live client (`spell-verification.json`,
  built by `tools/Verify-ClassSpells.ps1`): names and icons are the client's
  own official values, and abilities the client no longer has (removed in 12.1)
  are omitted from the list rather than offered as dead toggles.
- Icons are downloaded once from the official WoW CDN — preferring the slug
  from that same verification — and cached next to the exe
  (`assets/icons/{spellId}.jpg`, preserved across publishes); offline a drawn
  placeholder is shown and the screen still works. This is the app's only
  network use, opt-in by opening the screen.
- Toggling writes the existing `[Abilities] On=`/`Off=` storage. **OFF** is
  the absolute automatic-use prohibition (for a Main-slot spell the suggestion
  is skipped and the scheduler falls through to the next suggested stroke,
  never a rotation rewrite); **ON** is eligibility only. Movement abilities
  stay manual/emergency-only.

Classification and name decoding are modeled best-effort: the vendor data has
no active/passive flag, so a passive talent that evades the filter can appear
(its toggle is inert because it never arrives as a suggestion). See
`docs/KNOWLEDGE.md`.

## Solo / self-sustain mode (`[Solo] Enabled=0`, requires intelligence)

Survival-first when enabled:

| HP | Behaviour |
| :--- | :--- |
| above 65% | defensives stay MaxDps-triggered; self-heals conserve |
| below 65% | efficient self-heals eligible (overheal guard: a heal that is mostly wasted is held) |
| below 60% | a major defensive waits while a minor mitigation is running |
| below 35% | every available survival action outranks the rotation (emergency) |

**Where the self-heal candidate comes from (this is the part MaxDps cannot
provide).** MaxDps never suggests Impending Victory, Exhilaration, Crimson
Vial and friends — they are not in any damage rotation. The bridge therefore
resolves a per-spec *curated self-heal list* (generated `Catalog.lua`, the
same knowledge base the companion embeds) and writes the first ability that is
**ready and has a resolvable keybind** into the companion-only SelfHeal slot.
That slot is an independent candidate source: it does not wait for MaxDps to
suggest anything, and the Solo policy decides USE/HOLD/SKIP for it exactly
like any other candidate. The normal MaxDps rotation is never transformed —
the heal is an extra candidate that can preempt damage only while the HP
policy says it should.

Ability-specific policy (not one hardcoded percentage): the curated
`useBelowHpPct` ceiling (e.g. Lay on Hands below 40%) and `healPct` overheal
estimate are per-ability; melee heals are range-gated on the slot-8 probe;
target-requiring heals hold without a target; never-automatic heals (raid
cooldowns, channels that would be clipped) stay manual even in Solo mode; and
`EmergencyHpPct` shortens every rule for a survival action that can execute.

The same layer runs on the legacy path when the scheduler is disabled
(`[Scheduler] Enabled=0`): the companion slots are inserted into the
intelligence order (SelfHeal before Main, Mobility after Defensive) so Solo
mode does not silently disappear with the scheduler. With intelligence OFF
the companion-only slots are never considered — standard behaviour.

## Action scheduler (default on, `[Scheduler] Enabled`)

Deterministic state machine between the policy and the send path. Pure: a
function of the frame, the knowledge base, the policy memory and settings —
no clock reads, no randomness, no new game state.

| Rule | Behaviour |
| :--- | :--- |
| Link gate | A heartbeat frozen past `HeartbeatTimeoutMs` is link loss: nothing fires. |
| Protocol gate | A frame version that is neither supported nor a known older width holds. |
| Rank | Interrupt → emergency survival → defensive → solo self-sustain → Main → Mobility → Offensive → Consumable → Trinket; duplicate strokes collapse to the highest rank. |
| Stale demotion | A pressed-and-unchanged candidate moves behind fresh ones — never removed; a stale defensive cannot block a fresh main. |
| GCD + interval | Nothing that rides the GCD fires (interrupts bypass); one key press per `MinKeyIntervalMs`; a **changed** interrupt stroke bypasses the interval. |
| Cast/channel gate | The hard execution-safety hold (above) also runs here when intelligence is off, with `CastHold`/`ChannelHold` plan reasons. |
| Failure recovery | A press that never starts a GCD inside ~600 ms is treated as failed; the stroke is suppressed with an escalating window (1.5 s → 3 s → 6 s → 10 s, reset by a success) instead of hammered. The same stroke sent ≥5 times in 1.5 s backs off the same way. Failure suppression survives state transitions, so a dead action cannot be re-armed by every GCD pulse; a pending non-main press is demoted behind fresh actions while its confirmation is pending. |
| Unavailable suppression | A candidate rejected by a local gate (movement bind, held key, focus, lost window) is skipped briefly so lower ranks get a turn. |

Every existing per-slot OS gate stays authoritative. Advanced → Diagnostics
shows the plan head, reason, confidence, policy verdict/skip counts and the
hold reason; `--bench-scheduler` measures it on a scripted fake clock.

## Settings (`settings.ini`)

| Section | Key | Meaning |
| :--- | :--- | :--- |
| `Bridge` | `CellSize`, `OffsetX`, `OffsetY` | Strip geometry; match `/mdb status`. |
| `Window` | `ProcessName` | Game process without `.exe` (`Wow`). |
| `Window` | `AllowBackgroundKeys` | `1` = spell keys send while in background (mouse/interact always need focus). |
| `Window` | `Width` / `Height` | Remembered client size (`0` = default); saved on resize-end and close, restored clamped to the working area on start. |
| `Pause` | `Button` | Global pause hotkey (`Pause`, `F9`, …). |
| `Spells` | `spell1`…`spell8` | Which slots may fire: Main, Offensive, Defensive, Consumable, Trinket, Interrupt, Mobility, SelfHeal. Item slots default off; Mobility/SelfHeal on. |
| `Timing` | `PollIntervalMs` / `MinKeyIntervalMs` / `KeyPressMs` | Sample rate / gap between inputs / hold length. |
| `Targeting` | `AutoTargetEnabled`, `CombatOnly`, `TargetKey` | Press `TargetKey` when bridge reports no-target. Never a movement key. |
| `Interact` | `InteractEnabled`, `InteractKey` | Press `InteractKey` when bridge reports need-interact. |
| `Scheduler` | `Enabled` | `1` (default) = deterministic scheduler. `0` = legacy loop (policy still applies when intelligence is on). |
| `Scheduler` | `HeartbeatTimeoutMs` / `RepeatSuppressMs` | Link-loss window / v1-frame repeat suppression. |
| `Intelligence` | `Enabled` | `1` (default) = situational policy (USE/HOLD/SKIP). `0` = every MaxDps suggestion fires (companion-only slots off). |
| `Intelligence` | `StaleAfterMs` | Pressed-and-unchanged demotion window. |
| `Solo` | `Enabled` | `0` (default) = standard. `1` = survival-first layer (requires intelligence). |
| `Solo` | `EmergencyHpPct` / `SelfSustainHpPct` / `DefensiveEscalateHpPct` | 35 / 65 / 60 — see the table above. The emergency and escalation thresholds also tune normal-mode defensive behaviour (emergency override / save-the-major rule); `Enabled` gates the proactive self-heal layer. |
 | `Abilities` | `On` / `Off` | Comma lists of spell ids explicitly enabled/disabled over the curated default (v2.3). `Off` never fires automatically (absolute); `On` is eligibility, not spam. Written only as a diff from the catalog default. |
 | `Abilities` | `Modes` | Per-ability modes `id:Mode` (v2.6.0): `SoloOnly` / `NormalOnly` / `Manual` / `Never` / `Always` / `Automatic` (`Always` currently evaluates as `Automatic` - reserved). |
| `Telemetry` | `Enabled` / `Capacity` | Opt-in local JSONL ring (no network, no Blizzard values). |
| `Color` | (empty = unlearned) | Learned display-chain profile from `/mdb calibrate`. |
| `Launch` | `BNetPath` | Empty = auto-detect Battle.net. **Launch Game** uses the launcher's remembered account — no credentials stored anywhere. |

The borderless window's 2px frame ring is red while stopped and green while
running, so the state is readable at the window edge without looking at the
status line; the resize grip is unaffected. The client size is remembered (see
`Width`/`Height` above).

## Ability knowledge base

`docs/KNOWLEDGE.md` documents the two-layer catalog: the vendor-extracted
base (`vendor-abilities.json`, regenerated by
`tools/Extract-VendorAbilities.ps1`) and the curated policy layer
(`abilities.json`) with per-ability purpose/tier/conditions plus per-spec
Mobility/SelfHeal lists and a derived defensive gap-fill list. The bridge
mirrors the class/spec ids and extras via the generated
`addon/MaxDpsBridge/Catalog.lua`
(`MaxDpsCompanion.exe --gen-catalog=<path>`); a test fails if it drifts. The
user per-ability ON/OFF layer is `Knowledge/AbilityPolicy.cs` (`[Abilities]`).
Research artifact and sources: `docs/research/ABILITY_RESEARCH.md`.

## Rotation telemetry + replay (opt-in, `[Telemetry] Enabled`)

Local-only record of each decoded tick — suggestions, policy verdicts with
reasons, decision, keys sent, timing and decode faults. Bounded in-memory
ring, nothing on disk until you export, no network, no Blizzard values.

1. Advanced → **Telemetry** → *Record rotation telemetry* → **Start**.
2. Fight, then Advanced → **Export…** → `telemetry-<time>.jsonl`.
3. `MaxDpsCompanion.exe --replay="<file>.jsonl"` writes a diagnostic report:
   per-tick candidates, selected action + reason, policy verdicts, rejected
   candidates, GCD/stale holds. Recorded *intelligence* decisions recompute
   with **0 mismatches**. Format spec: `docs/TELEMETRY.md`.

## Build / install

```powershell
.\tools\install-addon-exp.ps1   # CURRENT: copy MaxDpsBridgeExp into Interface\AddOns (never touches MaxDps*)
.\build.ps1                     # publish MaxDpsCompanion.exe (legacy dist\ target)
#     Exp client = the dist-exp\ exe published with InGameConfigMode=1
```

Then in game: `/reload`, `/mdbx status`, Start in the app. Client must be
windowed/borderless and visible. Tests: `docs/TESTING.md`.

`MaxDpsBridge` + `dist\` + `install-addon.ps1` are **LEGACY/deprecated** (frozen);
all releases, fixes and deploys target the Exp client unless legacy is explicitly
requested.

Version locations: addon `MaxDpsBridge.toc` + `addon/MaxDpsBridge/VERSION.txt`,
app title bar (`ThisAssembly.Gen.cs` stamp), repo `VERSION.txt`.

## Safety limits

- Windowed or borderless windowed; visible (not minimized/covered).
- Keys go to the game window handle only; mouse-bound slots need focus.
- MaxDps is a single-spell engine: the main slot is the rotation; everything
  else is situational and policy-gated.
- **Immune targets** cannot be detected (no immune flag in any safe API): a
  press that the game rejects is bounded by the failure recovery (escalating
  suppression), never by knowledge.
- **UNKNOWN range fails open** for MaxDps-gated slots (MaxDps's own readiness
  gate owns range); a known out-of-range target is always skipped.
- **Enemy cast identity / importance** is not observable in Midnight 12.x
  (UnitCastingInfo is secret for non-player units; combat-log events are not
  available to addons) — reflect filtering uses "a cast is live", never
  "which spell".
- ~50–120 ms pixel-to-key latency; a suggestion follower, not a bot.
