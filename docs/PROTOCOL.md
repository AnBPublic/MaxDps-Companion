# Pixel protocol v5 + Ext2/Ext3/Ext4/Ext5 (additive; 35/40/43/51/58-cell) — MaxDpsCompanionBridge ↔ MaxDpsCompanion

Normative spec. Bridge encodes, companion decodes. The frame is the **35-cell
v5 core** plus the **additive Ext2 block** (cells 35-39), the **additive
Ext3 block** (cells 40-42), the **additive Ext4 block** (cells 43-50) and the
**additive Ext5 block** (cells 51-57), so a
bridge that ships Ext5 renders a **58-cell** strip, left to right, each
`CellSize` physical pixels square.
The app samples the centre pixel (5-tap median at ≥2 px cells).

> **Compatibility contract (Sep-2026 outage lesson):** the companion MUST
> decode the previous protocol versions too. It decodes **v5/Ext5 (58 cells),
> v5/Ext4 (51 cells), v5/Ext3 (43 cells), v5/Ext2 (40 cells), v5/v6 (35 cells,
> v6 accepted forward-compatible), v4 (9 cells) and v1 (8 cells)**; the in-game
> addon encodes the widest layout it ships (Ext2 = 40 cells; Ext3 = 43;
> Ext4 = 51; Ext5 = 58). The addon goes stale whenever the user
> runs a new exe without `install-addon.ps1` + `/reload`. Rule for every
> protocol bump: **companion decodes N and the older supported widths; addon
> encodes N**, and the app warns (never hard-fails) on skew so recalibrate is
> the repair path. The defensive-urgency block, Ext2, Ext3, Ext4 and Ext5 are
> all **additive**: a pre-2.3 encoder leaves the three urgency nibbles at `0` =
> UNKNOWN, a pre-3.0 encoder leaves cell 33 B bit2 clear (cells 35-39 ignored),
> a pre-Ext3 encoder leaves cell 28 B bit2 clear (cells 40-42 ignored), a
> pre-Ext4 encoder leaves cell 28 B bit3 clear (cells 43-50 ignored) and a
> pre-Ext5 encoder leaves cell 50 R bit0 clear and renders 51 cells (cells
> 51-57 ignored). The
> wire version nibble stays `5` in all directions — an updated addon still
> decodes with an older companion exe (it ignores the reserved nibbles, whose
> checksums it already covered), and a stale addon still drives the current
> companion.

Slot naming: slots 1-6 mirror the in-game Spell Frame categories (offensive =
`classCooldowns` offensive bucket, defensive = `GlowDefensiveHPMidnight`,
consumable = `MaxDps.Consumables` potions, trinket = other `ItemSpells`,
interrupt = `GlowInteruptMidnight`). Slots 7-8 are **companion-only**:
the bridge resolves their keybinds from the curated per-spec lists in the
generated `Catalog.lua` (MaxDps never surfaces them itself).

## Nibble encoding

Every channel carries one nibble: `byte = nibble * 17` (0, 17, …, 255).
Decode: `nibble = round(channel / 17)`, tolerance ±8 unless a `[Color]`
profile says otherwise.

## Cells

| Cell | R | G | B | Meaning |
| :--- | :--- | :--- | :--- | :--- |
| 0 | 15 | 0 | 15 | magic: presence + alignment + calibration reference |
| 1 | Main vk hi | Main vk lo | flags | main rotation suggestion |
| 2 | Offensive vk hi | Off vk lo | flags | offensive suggestion |
| 3 | Defensive vk hi | Def vk lo | flags | defensive suggestion |
| 4 | Consumable vk hi | Cons vk lo | flags | potion suggestion |
| 5 | Trinket vk hi | Trin vk lo | flags | on-use trinket suggestion |
| 6 | Interrupt vk hi | Int vk lo | flags | interrupt suggestion |
| 7 | Mobility vk hi | Mob vk lo | flags | gap-closer suggestion (curated) |
| 8 | SelfHeal vk hi | Heal vk lo | flags | self-heal suggestion (curated) |
| 9 | state | heartbeat | status flags | engine state + liveness + context flag |
| 10 | 5 | checksum(cells 1-9) | commit (=heartbeat) | version + core integrity |
| 11-12 | 6 nibbles | | | Main spell id (0 = unknown) |
| 13-14 | 6 nibbles | | | Offensive spell id |
| 15-16 | 6 nibbles | | | Defensive spell id |
| 17-18 | 6 nibbles | | | Consumable spell id |
| 19-20 | 6 nibbles | | | Trinket spell id |
| 21-22 | 6 nibbles | | | Interrupt spell id |
| 23-24 | 6 nibbles | | | Mobility spell id |
| 25-26 | 6 nibbles | | | SelfHeal spell id |
| 27 | hp% hi | hp% lo | hp flags | player health |
| 28 | cast state | remaining band | Ext2: SelfHeal2 range bits0-1; Ext3: presence bit2; Ext4: presence bit3 | player cast/channel (+ alternate range + Ext3/Ext4 presence) |
| 29 | target flags | target hp band | target cast flags | target state |
| 30 | slot1\|slot2 | slot3\|slot4 | slot5\|slot6 | per-slot range tri-state (2 bits each) |
| 31 | slot7\|slot8 | defensive urgency (HP curve) | gap-fill source | per-slot range tri-state + additive urgency |
| 32 | bits 0-3 | bits 4-7 | stagger urgency | per-slot self-buff active + additive stagger urgency |
| 33 | class id | spec id | bit0 class/spec valid, bit1 self-buff block valid (v2.7), bit2 Ext2 present, bit3 HP curve active (v3.0.0) | class + spec (wire ids) |
| 34 | 0 | checksum(cells 11-33) | commit (=heartbeat) | extension integrity |

## Additive urgency block (v2.3, wire version stays 5)

The defensive-urgency block is **additive on top of v5**: it fills nibbles
that were reserved and always `0` in pre-2.3 encoders, so the cell count,
layout and version nibble are unchanged. The wire version **stays 5** — cell
10 R keeps encoding `5`.

- **Reserved nibbles, no renumber.** Cell 31 G (HP-curve defensive urgency),
  cell 31 B bit0 (the Defensive slot is a catalog gap-fill) and cell 32 B
  (stagger-curve defensive urgency) were reserved and written as `0` by the
  original v5 encoder. A pre-2.3 encoder still writes `0` there = UNKNOWN,
  which the policy treats conservatively (no automatic major defensive).
- **Old companion + new addon works.** A pre-2.3 companion ignores the
  reserved nibbles (its extension checksum over cells 11-33 already covered
  cells 31 and 32), so an updated addon that fills them still decodes. This
  is the key improvement: a bumped version nibble would have been rejected by
  the old decoder as a version fault (the Sep-2026 "no abilities execute"
  outage class).
- **New companion + old addon works.** The current decoder reads the three
  nibbles only when they are non-zero; an all-zero pre-2.3 frame reads as
  UNKNOWN and the policy holds majors conservatively. Pinned by tests
  (`PixelProtocolV5Tests`, `DefensiveIntelligenceTests`, `LegacyAddonCompatTests`).
- **Forward-compatible version 6.** The companion also accepts `cell 10 R ==
  6` — the same 35-cell layout — purely so a future explicit version bump
  needs no immediate companion release. The addon still encodes `5`.

Nothing was moved or renumbered.

| Cell | Channel | Additive (v2.3) meaning | Pre-2.3 (reserved) |
| :--- | :--- | :--- | :--- |
| 10 | R | version = **5** | version = 5 |
| 31 | G | HP-curve defensive urgency (0-4, see below) | reserved (0) |
| 31 | B | bit0 = the Defensive slot is a catalog gap-fill; bits 1-3 reserved | reserved (0) |
| 32 | B | stagger-curve defensive urgency (0-4) | reserved (0) |
| 33 | B | bit1 = self-buff probe block valid (v2.7, bridge 2.7.0) | reserved (0) |

The extension checksum (cell 34 G, over cells 11-33) already covers cells 31
and 32, so its formula is unchanged. A pre-2.3 encoder writes `0` into all
three nibbles, which the companion reads as UNKNOWN.

### Defensive urgency (cells 31 G and 32 B)

MaxDps has no discrete defensive colour enum. It renders every defensive
through `MaxDps:GlowDefensiveHPMidnight` (`vendor/MaxDps/Buttons.lua:1056-1110`),
which builds a linear colour curve with exactly three control points —
`0.3 → red (1,0,0,1)`, `0.5 → yellow (1,1,0,0.5)`, `1.0 → green (0,1,0,0)` —
and evaluates it at the player HP fraction. The bridge stages the rendered
colour at those same control points ("white" is the transparent end where
MaxDps draws no glow). `spec.lua` files call this for each
`classCooldowns[class][spec].defensive` entry gated by `CheckSpellUsable`
(e.g. `vendor/MaxDps_Warrior/Specialization/Fury.lua:18-25`), so a flagged,
usable, enabled defensive is "recommended" while the colour is its urgency.

| Value | Name | HP curve (cell 31 G) | Stagger curve (cell 32 B) |
| :--- | :--- | :--- | :--- |
| 0 | Unknown | no reading / pre-2.3 encoder / failed probe | no reading / failed probe |
| 1 | White | HP = 100% (transparent, no glow) | stagger < 30% |
| 2 | Yellow | 50% ≤ HP < 100% | 30% ≤ stagger < 50% |
| 3 | Orange | 30% < HP < 50% (red→yellow blend) | 50% ≤ stagger < 100% |
| 4 | Red | HP ≤ 30% (red anchor) | stagger ≥ 100% |

### Companion gap-fill sources (v3.0.0, no wire change, v3.3.0 Solo ladder additive)

Two slots carry companion-generated gap-fill candidates. The **Defensive**
slot has the cell 31 B bit0 source flag (Red majors / Orange short-CDs). The
**Offensive** slot has **no source bit**: PixelProtocol decode is frozen
(no new flag field) and every slot-flag nibble bit is already used, so the
bridge just encodes the candidate into the normal slot and the companion
derives the source by **id membership** in the same generated per-spec
offensive list the bridge walked (`AbilityCatalog.IsOffensiveGapFill`). No
wire format changed; cells/counts/version nibble are untouched. Because the
offensive gap-fill is gated on combat/Solo, the tick's decoded class/spec is
recorded in the telemetry policy block (`cls`/`spec`, additive/omitted on
legacy records) so a replay re-derives the same source.

Cell 31 G carries the urgency for the spell currently in the Defensive slot
(so it follows the slot's identity). Cell 32 B always carries the raw
stagger-curve stage, which lets the policy apply the per-ability
`UrgencySource` choice itself. The one vendor per-spell special case is
spellId **119582 (Purifying Brew)**, which uses `UnitStagger("player") /
UnitHealthMax("player")` through the reversed curve above; when the stagger
read is secret the vendor falls back to the HP curve
(`if not color then color = UnitHealthPercent(...)`), and the bridge mirrors
that fallback exactly. Out-of-range nibbles (5-15) decode as UNKNOWN, so a
corrupt or reserved value can never read as White/Red.

#### v3.3.0 Solo survival ladder (no wire change)

The ladder is companion+bridge policy over the SAME cells: the bridge arms
`MDB.SoloLadderBands = { minor = 75, major = 50, immunity = 30 }` only while
the in-game Solo toggle is ON and the player is known ungrouped
(`Bridge.lua` per-tick context; Solo OFF / grouped / unknown group disarms
all bands). When armed and MaxDps names no defensive, the Defensive slot
offers `defensiveMinor` at/below the minor band, `defensiveMajor` at/below
the major band, and `immunity` at/below the immunity band
(`Reader.lua GetDefensiveCandidate`; `Catalog.lua` carries the two new lists
alongside the unchanged `defensive`/`defensiveMinor` lists). The companion
policy gates the same bands Solo-only (`CandidateProviders` Solo escalation:
in-band gap-fill bypasses the White-urgency hold; out-of-band holds;
immunity additionally needs no active immunity; MaxDps-flagged candidates and
all group verdicts keep the classic urgency path). Unchosen talents are
ignored at both ends: the bridge only offers a ready+bound spell the player
knows (`ExtraCandidates`), and catalog membership is a core+talent superset.
No cell, count, or version nibble changed.

#### v3.3.0 in-game 13 toggles (no wire change)

Bridge 3.3.0 adds the companion hero's 13 toggles to the addon
(`addon/MaxDpsCompanionBridge/Toggles.lua` + `InGame/Overlay.lua`): 8 per-slot toggles (slots
1-8) plus Solo / Out-of-combat / Auto-target / Auto-interact / TTK (the `ttk`
key is labelled **"TTK guard"** from v3.6; see §201 for its OFF semantics). The
addon only ever **restricts** what the companion already decided —
`effective = companion AND addon`, an addon OFF always wins. The wire version
nibble stays `5`: no cell, nibble lane or checksum changed, and a companion
that does not know about toggles decodes the frame exactly as before.

- **A user-blanked slot is indistinguishable from "no candidate".** An OFF
  slot is written as an all-zero cell with the slot-valid flag (bit 3) clear —
  exactly the encoding the bridge already emits when MaxDps names nothing. The
  companion decodes both as empty and runs its ordinary no-candidate policy;
  the wire carries no "user muted this" bit and there is no new semantics to
  decode.
- **Out-of-combat OFF** blanks slots 1-8 whenever the bridge reads
  `UnitAffectingCombat("player") == false`. The check is a strict boolean:
  nil/secret combat state is not a false and fails open (no blank).
- **Solo OFF** blanks only the survival slots (Defensive 3, SelfHeal 8), only
  while the player is known ungrouped, and only when HP is known above the
  emergency floor (35). Unknown group or HP fails open.
- **Auto-target / Auto-interact OFF** force cell 9 R to state `0` (idle)
  instead of `3`/`4`. The status-flag nibble is untouched, so the real target
  and combat facts are still reported.
- **TTK guard OFF (v3.6 rename)** forces the target HP band (cell 29 G) to
  `15` = UNKNOWN; the melee flag and cast/interrupt flags are unchanged. This is
  **not** an inversion of the toggle (OFF is automation-off everywhere else,
  and flipping it would make ON = fire everything): OFF explicitly means
  *"cooldowns fire without dying-target protection; target HP band hidden from
  the companion"*. Collateral, and the reason the toggle is easy to
  misunderstand: band 15 blinds not only the v3.2.0/v3.6 TTK gates but also
  the execute gate and every other target-band consumer (Burst presets), which
  go blind exactly as they do for a genuinely hidden band. The companion's
  `[TimeToKill] Fallback=FailOpen|ConserveMajors` setting (default `FailOpen`)
  governs what an *unknown* TTK does when the feature is otherwise ON; it is
  companion-side, not a wire field. **Phase 2 (separate L-route spec, not this
  change)** may add a target-class encoding (cell 29 R bits 2–3); it is not
  required for the v3.6 guard and does not alter this OFF semantics.

#### v4.0.4 Main master switch (Exp addon; no wire change)

The Exp addon promotes **Main** from "core rotation slot" to the master switch
for the whole rotation. While `Toggles.Get("Main") == false`,
`Toggles.SlotAllowed` blanks all eight slots with reason
`main off (master switch)` and leaves every other stored toggle physically
untouched; Main ON restores the previous per-slot policy. The companion applies
the same gate from the Ext3 mirror (`bit 0`) and returns before every send path
— rotation, auto-target, auto-interact and queued user presses — with
`holding (addon Main off - rotation suspended)`. A missing/stale mirror fails
open for old addons. No cell, checksum, layout or version-nibble change: the
Ext3 mask already carries the bit, and the wire stays 51 cells on the Exp
addon (43 on the stable bridge).

`/mdb toggles`, `/mdb <key> on|off`, `/mdb all on|off`, `/mdb overlay on|off`
and `/mdb why heal` are command-surface only; `/mdb status` gains a trailing
`toggles=N/13 ON; OFF: ...` field.

#### v3.x racial toggles (no wire change)

Racial abilities are **ordinary catalog rows tagged `scope: "Racial"`**, not a
new wire concept: there is no race field, no new cell and no version bump.
Each row carries a normal Purpose, so the existing providers route it exactly
like a class ability — Offensive (`MajorOffensive`/`MinorOffensive` → the
Offensive toggle/slot), Defensive (`DefensiveMinor`/`Absorb` → the Defensive
toggle/slot), SelfHeal (`purpose: SelfHeal` → the SelfHeal toggle) or Utility
(CrowdControl → manual/Never). The generator appends the racial ids to **every**
class/spec's `offensive` and `defensiveMinor` `MDB.Extras` lists, and the
bridge's existing **known-spell filter** is the race selector: a character that
is not that race has no resolvable keybind/spell for the id, so the walk simply
skips it. No client race detection is performed and `UnitRace` is never read.
CC/mobility racials (War Stomp, Quaking Palm, Wing Buffet, Haymaker, Bull Rush)
stay `neverAutomatic`/Manual; passives and travel-only racials are not
catalogued. Toggles remain slot-based and unchanged.

### Flags (slot cells 1-8)

| Bit | Meaning |
| :--- | :--- |
| 0 | Shift held/required |
| 1 | Ctrl held/required |
| 2 | Alt held/required |
| 3 | slot valid (clear = ignore this cell even if id nonzero) |

### States (cell 9 R)

| Value | Meaning |
| :--- | :--- |
| 0 | idle (out of combat / no rotation) |
| 1 | active |
| 2 | paused (`/mdb off`, calibrate mode, or app pause) |
| 3 | no-target (companion may press `TargetKey` if enabled) |
| 4 | need-interact (companion may press `InteractKey` if enabled) |

### Status flags (cell 9 B)

| Bit | Meaning |
| :--- | :--- |
| 0 | in combat (`UnitAffectingCombat`, NeverSecret) |
| 1 | on GCD (`C_Spell.GetSpellCooldown(61304).isOnGCD`, NeverSecret) |
| 2 | valid attackable target exists |
| 3 | **context valid**: the v5 sensor block was computed this tick |

### Spell ids (cells 11-26)

Six nibbles per slot, MSB first: `id = (c0.R << 20) | (c0.G << 16) | (c0.B << 12) | (c1.R << 8) | (c1.G << 4) | c1.B`.
24-bit field, `0` = unknown identity (uncatalogued ability → the companion
falls back to generic per-slot policy). Ids are plain values from MaxDps's
own tables; they are never derived from a secret API return.

### Vitals (cell 27)

`hp%` = `R*16 + G` when `B bit0` is set and `R <= 6` (0-100); otherwise the
player HP is **UNKNOWN** (Blizzard secret value or no reading).

### Cast (cell 28)

- R: `0` none, `1` casting, `2` channeling, `15` unknown.
- G: remaining-time band, reserved (`15` = unknown; not safely observable).
- B: reserved.

Cast state comes from **arg-blind unit events** (`RegisterUnitEvent` on
dedicated player/target frames): the unit filter is applied by the game and
no event payload is ever read, so no tainted `UnitCastingInfo` return is
touched. **v2.1 watchdog:** a latch held longer than 15 s without a stop
event (a missed `STOP`) degrades to `15` = UNKNOWN, so the companion fails
open instead of holding the rotation behind a dead bit.

### Target (cell 29)

- R: bit0 = in melee (`CheckInteractDistance` follow index, scrubbed), bit1 =
  melee unknown, bits 2-3 reserved. No target reads as melee-unknown.
- G: target HP band `0..14` (~0-100% in 15 steps), `15` = unknown.
- B: bit0 = target casting, bit1 = cast state unknown, bit2 = interruptible,
  bit3 = not interruptible (from `UNIT_SPELLCAST_*` unit events).

  **v2.1 semantics:** both interruptibility events fire at cast start, so an
  explicit `not interruptible` is a per-cast fact — the bridge refuses to
  encode an interrupt slot for it. The target cast latch clears on
  `UNIT_SPELLCAST_STOP/FAILED/INTERRUPTED/CHANNEL_STOP`,
  `PLAYER_TARGET_CHANGED`, target loss and the 15 s watchdog (which reports
  the unknown bit instead of "casting").

  **Not transmitted:** enemy cast spell identity, school, cast target and
  remaining time. All of it is a secret for non-player units in Midnight
  12.x (`UnitCastingInfo` is `SecretWhenUnitSpellCastRestricted`) and
  combat-log events are not available to addons. Reflect/interrupt policies
  therefore key on "a cast is live / explicitly interruptible", never on
  which spell it is — an explicit, documented limitation.

  **v4.2.0 slot-6 CC semantics (no wire change):** the crowd-control pool
  reuses the Interrupt slot (cells 20-27 + the slot-6 tri-state range and the
  cast-state facts above) exactly as before. What changed is which curated
  rows the bridge is allowed to write there: only the single-target
  stun/silence interrupt fallback from the generated per-spec `cc` list
  (`cc = { 408 }` for every rogue spec). Blind/Gouge/Sap/Polymorph/Fear and
  every other break-on-damage row are manual-only and are never encoded into
  the slot by the companion. The companion still applies the casting-only and
  DR gates on its side, so an old bridge that writes a wider `cc` list cannot
  make the companion auto-cast manual CC.

### Range tri-state (cells 30-31)

Two bits per slot: `0` unknown, `1` in range, `2` out of range, `3` reserved.
The bridge probes `MaxDps:IsSpellInRange(spellId, "target")` for the
target-facing slots (Main, Offensive, Defensive, Interrupt, Mobility,
SelfHeal — the last since bridge 2.2, because melee self-heals such as
Impending Victory and Death Strike are attacks) through a pcall; a secret or
failed probe stays `0` (UNKNOWN). The two self-targeted item slots
(Consumable, Trinket) are never probed.

### Self-buff bits (cell 32)

Bit *i* = the suggested spell of slot *i+1* already has its own helpful aura
on the player (`AuraUtil.FindAuraBySpellID`), used to skip a redundant
re-press.

**v2.7 additive semantics (wire version stays 5):** cell 33 B bit1 = the
self-buff probe block is *valid* — the bridge ran every requested probe and
none failed or degraded (no `AuraUtil`, thrown call, or secret value). When
the bit is set, each bit above is a real Yes/No. When it is clear (including
every pre-2.7 encoder and the disabled empty frame), the whole block reads as
**UNKNOWN**; the companion's buff gate treats Unknown as "not active" — a
documented fail-open, because the trigger to consider an ability is MaxDps's
own recommendation and this gate only prevents waste. A failed probe can never
masquerade as "buff absent" on a current bridge, and a legacy bridge behaves
exactly as before (bit 0 everywhere → Unknown → same fail-open).

### Class / spec (cell 33)

R = class id, G = spec ordinal, B bit0 = class/spec valid, B bit1 = self-buff
probe block valid (v2.7 additive; see above). The id tables are generated
into `addon/MaxDpsCompanionBridge/Catalog.lua` from the companion's knowledge base
(`AbilityCatalog.ClassOrder` / `SpecOrder`); see docs/KNOWLEDGE.md. Wire ids
never renumber.

### Checksums + commit (cells 10 and 34)

- Cell 10: R = protocol version (5), G = XOR-sum of all R/G/B nibbles of
  cells 1-9 mod 16, B = commit (= heartbeat).
- Cell 34: R = 0, G = sum of all R/G/B nibbles of cells 11-33 mod 16,
  B = commit (= heartbeat).
- Mismatch or torn commit = drop the frame, keep previous state, count a
  decode error. Two checksums because a full 35-cell frame is too wide for
  one nibble to cover meaningfully.

## Calibration (54-step pattern)

`/mdb calibrate on` replaces the strip with a 54-step cycling pattern:
black, white, then one step per channel axis (16 steps × R/G/B = 48) plus
4 anchors. Slot cells 1-8 carry the flat level; the status cell (9) reports
state 2 (paused) so the companion holds. The learner averages cells 1-8.
`/mdb calibrate off` (or `/mdb off`) returns to normal rendering.

## Restricted content (Midnight secrets)

In combat / encounters / M+ / PvP Blizzard marks cooldown, cast and
sometimes health data as secret values. The bridge never compares or does
arithmetic on them:

- readiness uses NeverSecret `isActive` / `isOnGCD` / `isEnabled` booleans
  plus guarded Duration objects (`EvaluateRemainingDuration` on a curve);
- cast state comes from arg-blind unit events, not API returns;
- HP / range / aura probes run through `pcall` + `scrubsecretvalues`; any
  secret degrades to **UNKNOWN** (or "not active" for the buff probe) and the
  companion's policy treats UNKNOWN with its documented fallback;
- a probe that throws can never abort a frame: `SafeRead` contains getter
  failures and warns once.

## `/mdb` reference

| Command | Effect |
| :--- | :--- |
| `/mdb status` | offset, cell size, state, bound slots, class/spec + extras; bridge 3.0.0 adds `ext2=1 hpcurve=<on\|off> sh2=<id\|->` |
| `/mdb on` / `off` / `toggle` | enable / pause / flip |
| `/mdb offset <x> <y>` | strip origin, physical px from top-left |
| `/mdb cellsize <px>` | 1-64, default 8 (recommended 8) |
| `/mdb calibrate on\|off` | 54-step pattern show/hide |
| `/mdb heal` | per curated self-heal entry: known / ready / usable / key / why (explains the slot-8 + SelfHeal2 selection) |
| `/mdb toggles` | list the 13 in-game toggles (`N/13 ON; OFF: ...`, bridge 3.3.0) |
| `/mdb <key> [on\|off]` | main / offensive / defensive / consumable / trinket / interrupt / mobility / selfheal / solo / ooc / autotarget / autointeract / ttk |
| `/mdb all on\|off` | set all 13 in-game toggles |
| `/mdb overlay on\|off` | show/hide the compact in-game toggle overlay (`Ui.Overlay`) |
| `/mdb why heal` | explain a blank slot 8 / 3 from `MDB._LastBlank` (gate reason) |
| `/mdb hpcurve on\|off` | Ext2 HP-curve cell 35 on/off (SavedVariables, default on); off paints black and clears bit3 |
| `/mdb diag` | scrubbed MaxDps internals snapshot |
| `/mdb reset` | defaults + pattern cleared |

## Older windows (history)

The companion also decodes the pre-sensor windows so a stale in-game addon
degrades gracefully rather than going blind:

- **v4 (9 cells):** magic + Main/Off/Def/Cons/Trin/Int + status + version.
- **v1 (8 cells):** magic + 5 slots + status + version.

The engine re-decodes the legacy 9-cell and 8-cell windows out of the same
35-cell capture, so even an old addon drives the current companion. Those
frames carry no defensive urgency, cast state, target state or range
tri-state: every additive field is UNKNOWN and the policy falls back to its
documented pre-v2.3 behaviour. Bridge 3.0.0 encodes v5 + Ext2: the 40-cell
strip with the additive urgency nibbles and the Ext2 HP-curve / SelfHeal2
block (`addon/MaxDpsCompanionBridge/Bridge.lua`, `PROTOCOL_VERSION = 5`).

## Ext2 block (v3.0.0, additive)

Ext2 extends the strip to **40 cells**; cells 0-34 are the unchanged v5 core
and the version nibble stays `5`. A pre-3.0 encoder leaves cell 33 B bit2
clear, which the v3 decoder treats as "no extension" and ignores cells 35-39.

| Cell | Channel | Ext2 (v3.0.0) meaning |
| :--- | :--- | :--- |
| 33 | B | bit2 EXT2 PRESENT, bit3 HP CURVE ACTIVE (bit0 class/spec, bit1 buff block as before) |
| 28 | B | bits0-1 = SelfHeal2 range tri-state (0 unknown / 1 in / 2 out); was reserved `0` |
| 35 | R/G/B | HP curve: the bridge paints `SetVertexColor(color:GetRGBA())` where `color = UnitHealthPercent("player", false, MDB.HpCurve)` and the curve is linear `0.0 -> (0,1,0,1)`, `1.0 -> (1,0,0,1)`, so `R = hp fraction`, `G = 1-hp`, `B = 0`. The bridge never reads or compares the colour; no checksum covers it (Lua cannot read the value). If bit3 = 0 the cell is painted black. |
| 36 | R/G/B | SelfHeal2 key: vk hi \| vk lo \| flags (same semantics as slot cells 1-8) |
| 37-38 | 6 nibbles | SelfHeal2 24-bit spell id (identical packing to cells 11-26 and the extension checksum) |
| 39 | R/G/B | Ext2 checksum: `R = 0`, `G = sum(R,G,B nibbles of cells 36-38) mod 16`, `B = commit (= heartbeat)` |

**Decode rules (companion):**

- The extension is read only from a **40-cell capture**; a 35-cell addon
  decodes with `bit2 = 0` and cells 35-39 ignored. `ext2Present = bit2 &&
  cells.Length >= 40`.
- A cell-39 checksum failure (or a commit mismatch) drops **only SelfHeal2**;
  the frame, the v5 core and the HP curve are kept. SelfHeal2 also requires
  cell 36 flags bit3 (valid) to be set.
- The HP curve is valid iff `bit2 & bit3` **and** the nibble sum is in the
  accepted band `14 <= nR + nG <= 16`. Then `band = nR (0..15)`,
  `HpPct = round(nR*100/15)` and
  `HpPctUpper = min(100, round((nR+0.5)*100/15))`.
- The band check is deliberately tight because the bridge paints a linear
  two-channel ramp and never reads it; a corrupt cell can therefore never be
  trusted as a reading.
- The core checksums (cells 10 and 34) keep covering exactly cells 1-9 and
  11-33; they do not change.
- **Selection:** the bridge fills slot 8 (SelfHeal) with the first ready AND
  bound curated entry and Ext2 cells 36-38 with the **next distinct** ready +
  bound entry, each encoded as the variant the player actually knows.
- **HP precedence (companion):** plain cell 27 > curve cell 35 > unknown. A
  valid curve supplies fallback HP only when cell 27 is secret/unknown, and
  `HpPctUpper` is the band's top so the overheal guard never assumes the low
  end. If `[Intelligence] HpCurve=0`, the curve is ignored entirely.

## Ext3 block (v3.5, additive)

Ext3 extends the strip to **43 cells**; cells 0-39 are the unchanged v5 core +
Ext2 layout (including the cells 10/34/39 checksums) and the version nibble
stays `5`. A pre-Ext3 encoder leaves cell 28 B bit2 clear, which the decoder
treats as "no extension" and ignores cells 40-42. The block is a **14-bit mask
+ epoch + blocked nibble**, closed by its own checksum/commit cell.

| Cell | Channel | Ext3 (v3.5) meaning |
| :--- | :--- | :--- |
| 28 | B | bit2 EXT3 PRESENT (`0` = block absent, cells 40-42 ignored); bits0-1 are the Ext2 SelfHeal2 range tri-state (decoders mask with `& 0x3`) |
| 40 | R/G/B | mask bits 0-11: `R = bits 0-3`, `G = bits 4-7`, `B = bits 8-11` |
| 41 | R/G/B | `R = mask bits 12-13` (bits 0-1; bit2 = typing veto, v4.0.3; bit3 reserved), `G = epoch` (0-15), `B = blocked nibble` (0-15) |
| 42 | R/G/B | Ext3 checksum: `R = 0` (reserved), `G = sum(R,G,B nibbles of cells 40-41) mod 16`, `B = commit (= heartbeat)` |

**Decode rules (companion):**

- The block is read only from a **43-cell capture**: `ext3Present = cell 28 B
  bit2 && cells.Length >= 43`. A 40-cell Ext2 (or 35-cell core) addon decodes
  exactly as before; cells 40-42 are never read.
- `mask` is rebuilt as `m40.R | (m40.G << 4) | (m40.B << 8) | ((m41.R & 3) <<
  12)` — the 14-bit field, MSB nibbles last. `epoch` is cell 41 G and
  `blocked` is cell 41 B, both plain 0-15 values.
- A cell-42 checksum failure (or a torn commit) drops **only the Ext3 block**;
  the frame, the v5 core and Ext2 are kept — the same "drop only this block"
  rule as cell-39/Ext2.
- The cell-42 `R` channel is reserved and **ignored** (not asserted `0`), like
  cell 34 R and cell 39 R. An encoder writes `0` there; the decoder must not
  reject a frame because a reserved channel drifted.
- The core checksums (cells 10 and 34) and the Ext2 checksum (cell 39) keep
  covering exactly cells 1-9, 11-33 and 36-38; none of them change.
- The Ext3 block is additive over the **frozen** v5 contract: no cell, nibble
  lane, version nibble or checksum defined above is renumbered or moved.

## Ext4 block (v4.0.3, additive)

Ext4 extends the strip to **51 cells**; cells 0-42 are the unchanged v5 core +
Ext2 + Ext3 layout (including the cells 10/34/39/42 checksums) and the version
nibble stays `5`. A pre-Ext4 encoder leaves cell 28 B bit3 clear, which the
decoder treats as "no extension" and ignores cells 43-50. The block is a
**hotbar-map frame stream**: a header, up to two entry pairs, and a commit cell,
delivered as a self-describing frame sequence (a cycle = seq `0..N` with `N`
marked LAST).

| Cell | Channel | Ext4 (v4.0.3) meaning |
| :--- | :--- | :--- |
| 28 | B | bit3 EXT4 PRESENT (`0` = block absent, cells 43-50 ignored); bit2 Ext3 presence; bits0-1 Ext2 SelfHeal2 range (decoders mask with `& 0x3`) |
| 41 | R | bit2 TYPING VETO (chat/edit-box focus); read only when the Ext3 block is valid, bits0-1 stay the mask bits 12-13, bit3 reserved |
| 43 | R/G/B | header: `R = gen` (0-15), `G = seq` (0-15), `B = flags` (bit0 LAST, bit1 EMPTY, bits2-3 reserved) |
| 44-46 | 3 cells | entry A (below) |
| 47-49 | 3 cells | entry B (below) |
| 50 | R/G/B | commit: `R = 0` (reserved), `G = sum(R,G,B nibbles of cells 43-49) mod 16`, `B = commit (= heartbeat)` |

**Entry (3 cells):**

- Cell 1 = `(vk hi, vk lo, flags)` using the slot keystroke layout: Shift=1,
  Ctrl=2, Alt=4, Valid=8. **Valid=0 = pad entry** (no binding in that slot pair).
- Cell 2 = `(idTop, id19-16, id15-12)`: `idTop` bit3 = `isItem`, bit2 = `ready`,
  bits1-0 = id bits 21-20.
- Cell 3 = `(id11-8, id7-4, id3-0)`.
- Id is 22 bits:
  `id = ((idTop & 3) << 20) | (c2.G << 16) | (c2.B << 12) | (c3.R << 8) | (c3.G << 4) | c3.B`.
  The encoder skips and logs any id ≥ 2^22 (4,194,304).
- The `EMPTY` flag marks a seq-0 LAST frame that carries no entries.

**Decode rules (companion):**

- The block is read only from a **51-cell capture**: `ext4Present = cell 28 B
  bit3 && cells.Length >= 51`. A 43-cell Ext3 (or narrower) addon decodes
  exactly as before; cells 43-50 are never read.
- A cell-50 checksum failure **or a torn commit** (heartbeat mismatch) drops
  **only that Ext4 frame** (`BridgeFrame.Ext4 = null`); the frame, the v5 core,
  Ext2 and Ext3 are kept — the same "drop only this block" rule as
  cell-39/Ext2 and cell-42/Ext3.
- Pad entries (cell flags bit3 clear) are not surfaced; `Ext4Frame.Entries`
  holds 0-2 live entries. A stroke may map to several entries.
- `BridgeFrame.TypingActive` = `Ext3 valid && cell 41 R bit2`. An invalid or
  absent Ext3 block reads `false` (typing not detected → no veto).
- The cell-50 `R` channel is reserved and **ignored** (not asserted `0`), like
  cells 34/39/42 R. The core checksums (cells 10/34), the Ext2 checksum (39)
  and the Ext3 checksum (42) keep covering exactly cells 1-9, 11-33, 36-38 and
  40-41; none of them change. Cell 41 R bit2 lives inside the Ext3 checksum's
  scope (the sum covers the whole nibble), so an encoder that sets the typing
  bit must recompute cell 42 — the Ext3 mask still reads it as `mHi & 3`.
- The Ext4 block is additive over the **frozen** v5/Ext2/Ext3 contract: no
  cell, nibble lane, version nibble or checksum defined above is renumbered or
  moved.

**Assembly (Ext4Assembler, companion):**

- A cycle is `seq 0..N` (N = LAST); it starts only at `seq 0`, and seqs are
  contiguous within one generation. Repeated reads of one gen+seq are
  idempotent (a frame is held ≥2 paint ticks).
- A seq gap, a generation change or a torn frame discards the partial cycle and
  waits for the next `seq 0`.
- LAST swaps the map **atomically**: the previous map keeps serving until the
  new cycle closes.
- The map is dropped (Stale) after **5 s** with no completed cycle, or
  immediately (OldAddon) when a frame arrives without the Ext4 presence bit.
- `HotbarMap.Status` ∈ `Unavailable` / `OldAddon` / `Stale` / `Ok`;
  `HotbarMap.Lookup(stroke)` returns every entry bound to that stroke.

**Compatibility matrix:**

| In-game addon | Strip width | Presence bit | v4.0.3 companion | Pre-Ext4 companion |
| :--- | :--- | :--- | :--- | :--- |
| v1 | 8 | — | v1 decode (legacy window) | v1 decode |
| v4 | 9 | — | v4 decode | v4 decode |
| v5 core | 35 | — | v5 core | v5 core |
| v5 + Ext2 | 40 | cell 33 B bit2 | v5 core + Ext2 | v5 core (Ext2 ignored) |
| v5 + Ext3 | 43 | cell 28 B bit2 | v5 core + Ext2 + Ext3 (+ typing) | v5 core + Ext2 (Ext3 ignored) |
| v5 + Ext4 | 51 | cell 28 B bit3 | v5 core + Ext2 + Ext3 + Ext4 | v5 core + Ext2 + Ext3 (cells 43-50 ignored) |
| stale addon + current companion | ≤43 | bit3 = 0 | Ext4 absent; `HotbarMap.Status = OldAddon` | n/a |
| current addon + stale companion | 51 | bit3 = 1 | — | decodes the 43-cell prefix |

That table is the pre-4.6 contract; the Ext5 rows (including the important
"58-cell addon + pre-Ext5 companion" case, which is **not** rejected) are in the
Ext5 section's own compatibility matrix below.

## Ext5 block (4.6.0 "Cadence", additive)

Ext5 extends the strip to **58 cells**; cells 0-50 are the unchanged v5 core +
Ext2 + Ext3 + Ext4 layout (including the cells 10/34/39/42/50 checksums) and
the version nibble stays `5`. The block carries the inputs of the interrupt
insurance, GCD landing and opener release features: **target rank + delve
state, interruptibility, cast age + cast kind, the remaining GCD, and the full
CC entry (keystroke + spell id)**. Every field has a UNKNOWN code, so a
secret-degraded or unreadable probe reads as "no reading" and every consumer
keeps its pre-4.6 behaviour.

**Presence:** `cell 50 R bit0 == 1` **AND** the Ext4 presence bit (cell 28 B
bit3) **AND** a capture of at least **58 cells**. Cell 50 R was reserved-zero
and sits outside every checksum (`Bridge.lua` writes 0 there; `Ext4` decode
ignores it), so a pre-Ext5 addon renders 51 cells with the bit at 0 and a
pre-Ext5 companion never sees the block at all. Like Ext4, the Ext5 addon-side
DB key (`ext5`, default **false**) is the compatibility switch: with it off the
bridge still emits 51 cells.

**A pre-Ext5 companion is not rejected by a 58-cell addon.** Such a companion's
sampler/locator only knows widths 51/43/40/35, so it samples the first 51 cells
and decodes them as a normal Ext4 frame: cells 0-50 keep every checksum
(10/34/39/42/50) and the Ext5 presence bit lives in a reserved nibble outside
all of them. It simply never reads cells 51-57. The compatibility switch exists
for the opposite direction (a pre-Ext5 addon feeding a 4.6.0 companion, which
must stay byte-identical), and the opt-in default keeps a fresh install on the
frozen 51-cell frame.

**Cell map (cells 51-57, plus the presence nibble):**

| Cell | Channel | Ext5 (4.6.0) meaning |
| :--- | :--- | :--- |
| 50 | R | bit0 EXT5 PRESENT (`0` = block absent, cells 51-57 ignored); G/B stay the Ext4 checksum/heartbeat commit |
| 51 | R | bits0-2 target rank: 0 unknown, 1 trivial, 2 normal, 3 elite, 4 rare, 5 rareelite, 6 worldboss, 7 elite-skull (`UnitClassification == "elite"` and `UnitLevel == -1`); bits3 reserved |
| 51 | G | bits0-1 in-delve (`0` unknown, `1` no, `2` yes), bits2-3 reserved - **telemetry-only**: no companion decision rule reads it today |
| 51 | B | reserved `0` |
| 52 | R | bits0-1 interruptible (`0` unknown, `1` no, `2` yes), bits2-3 reserved |
| 52 | G | cast-age bucket 0-15: `0` unknown/none, `1..14` = `[(b-1)*250, b*250)` ms, `14` saturates (≥3250 ms), `15` reserved; only meaningful when the cast kind is cast/channel |
| 52 | B | bits0-1 cast kind (`0` unknown, `1` none, `2` cast, `3` channel), bits2-3 reserved |
| 53 | R | GCD-remaining bucket: `0` unknown, `1` not on GCD, `2..15` = `[(b-2)*100, (b-1)*100)` ms, `15` saturates (≥1300 ms) |
| 53 | G/B | reserved `0` |
| 54 | R/G/B | CC entry keystroke: `R = vk hi`, `G = vk lo`, `B = key flags` (bit0 Shift, bit1 Ctrl, bit2 Alt, **bit3 Valid**) - identical packing to Ext4 |
| 55 | R/G/B | CC spell id `idTop` (bits 21-20, decoder masks `& 3`), `id19-16`, `id15-12` |
| 56 | R/G/B | CC spell id `id11-8`, `id7-4`, `id3-0` |
| 57 | R/G/B | Ext5 checksum: `R = 0` (reserved), `G = sum(R,G,B nibbles of cells 51-56) mod 16`, `B = commit (= heartbeat)` |

**Decode rules (companion):**

- The block is read only from a **58-cell capture** with cell 50 R bit0 set:
  `ext5Present = cell 50 R bit0 && cell 28 B bit3 && cells.Length >= 58`. A
  51-cell Ext4 (or narrower) addon decodes exactly as before; cells 51-57 are
  never read.
- A cell-57 checksum failure **or a torn commit** (heartbeat mismatch) drops
  **only** Ext5 (`BridgeFrame.Ext5 = null`); the frame, the v5 core, Ext2,
  Ext3 and Ext4 are kept - the same "drop only this block" rule as
  cell-39/Ext2, cell-42/Ext3 and cell-50/Ext4. Symmetrically, a torn Ext4 frame
  does not drop a checksum-valid Ext5 block: each block owns its commit.
- **`Ext5Block.Unknown`** is the all-zero block. Field semantics on the decode
  side: `Rank` 0 = unknown (never "trivial"); `CastAgeMinMs` is -1 for bucket 0
  and 15 (unknown/none); `GcdRemainMinMs` is -1 for bucket 0 and 0 for bucket 1
  (not on GCD); `BossGrade` = rank 6 or 7 only; `CcValid` = a real keystroke
  AND a spell id > 0.
- **`Delve` (cell 51 G) is telemetry-only.** It is carried so a recorded tick
  can be labelled with the run type, and no decision rule reads it (the opener
  gate is `Rank`, nothing else). The field exists so the two surfaces agree on
  one wire layout; treating it as an input without a curated rule that says so
  would be exactly the silent drift this section forbids.
- **Consistency, not redundancy (interrupt insurance).** Cells 52 R/B duplicate
  the core cast triple so the insurance input is complete when the core nibbles
  are UNKNOWN. The companion therefore **cross-checks** them: a block whose
  `cast kind` is `1` (none) or whose `interruptible` is `1` (no) denies the
  insurance press even when the core triple is UNKNOWN. The two sources can
  never silently disagree in the firing direction.
- **Prediction bias (deviation D5, landing only).** `GcdRemainMinMs` is the
  bucket's LOWER bound, so the landing planner predicts the bucket's **upper
  edge** (`now + GcdRemainMinMs + 100 ms`) rather than the midpoint the approved
  plan named. The resulting 0-100 ms LATE bias is deliberate: a late pre-fire
  sits inside the client's ~400 ms spell-queue window and lands on the GCD,
  while an early one can be dropped. The learned lead (10-150 ms EWMA) trims the
  remaining transport latency; it never removes the bucket bias.
- **An invalid CC key forces `CcSpellId = 0`** (D4). The full CC entry is on
  the wire because a synthesized press needs a keystroke: when the addon could
  not offer a bindable CC it writes key flags bit3 = 0 and the decoder surfaces
  neither a stroke nor an id (never half an entry).
- Arithmetic is never applied before the addon has checked
  `C_Secrets.ShouldCooldownsBeSecret()`: a secret/unreadable probe encodes 0 in
  every field, which decodes as UNKNOWN.
- The cell-57 `R` channel is reserved and **ignored** (not asserted `0`), like
  cells 34/39/42/50 R. The core checksums (cells 10/34) and the Ext2/Ext3/Ext4
  checksums (39/42/50) keep covering exactly cells 1-9, 11-33, 36-38, 40-41
  and 43-49; none of them change.
- The Ext5 block is additive over the **frozen** v5/Ext2/Ext3/Ext4 contract: no
  cell, nibble lane, version nibble or checksum defined above is renumbered or
  moved.

**Deviations from the approved research plan (D1/D2/D4/D5):**

| # | Approved plan | Shipped | Why |
| :--- | :--- | :--- | :--- |
| D1 | Ext5 presence = cell 28 B bit4 | presence = **cell 50 R bit0** | Wire channels are 4-bit nibbles, so cell 28 B has no bit4. Cell 50 R is documented reserved-0 and is outside every checksum, so the bit costs no existing field. |
| D2 | cast **remaining** bucket | cast **age** bucket | `UnitCastingInfo` is never called (all returns are secret); only the event cast-start stamp (`TargetCastSince`) exists. A stun cancels a cast at any point, so age is sufficient for insurance. |
| D4 | Ext5 carries a CC index only | Ext5 carries the **full CC entry (key + spell id)** | A synthesized CC needs a keystroke; when the kick occupies slot 6 the CC key is absent. Reuses Ext4's proven key/id packing. |
| D5 | landing fires at the cell-53 **midpoint** | landing fires at the bucket's **upper edge** (midpoint + 100 ms) | The bucket is a 100 ms LOWER bound, so the midpoint is not a safe press target: the true remaining time can be up to 100 ms longer than the bucket's floor. The upper edge turns that ambiguity into a 0-100 ms LATE bias, which the client's ~400 ms spell queue absorbs, while an early press can be dropped outright. The 10-150 ms learned lead then trims the transport latency; it never removes the bucket bias. |

(The third deviation in the plan, **D3**, is companion-side only - the opener
gate is `BossGrade` (rank 6/7), not "elite + max HP" - and changes no wire
field.)

**Degrade matrix:**

| Case | Result |
| :--- | :--- |
| Ext5 addon key off (51 cells) | today's frame, byte-identical; `Ext5 == null` |
| Ext5 on, pre-Ext5 companion | **not rejected**: the pre-Ext5 companion samples its OWN 51-cell width (its `IsV5Length` has no 58) and decodes the 51-cell prefix - every checksum of cells 0-50 is intact and cell 50 R bit0 is outside all of them. Result: a normal 4.5.8 frame; `Ext5` is simply unknown to that app. Only a companion whose locator cannot lock onto the wider strip would report no pixel block, and the toggle stays opt-in. |
| Presence clear / capture < 58 | `Ext5 == null`; insurance, opener and cell-53 landing are inert, the estimator landing still works |
| Cell-57 checksum or heartbeat failure | drops **only** Ext5; core/Ext2/Ext3/Ext4 unaffected |
| Secret/degraded probe | every Ext5 field 0 (UNKNOWN); no arithmetic attempted on a secret value |

**Compatibility matrix (Ext5):**

| In-game addon | Strip width | Presence | 4.6.0 companion | Pre-Ext5 companion |
| :--- | :--- | :--- | :--- | :--- |
| v5 + Ext4 | 51 | cell 28 B bit3 | v5 + Ext2 + Ext3 + Ext4, `Ext5 == null` | v5 + Ext2 + Ext3 |
| v5 + Ext5 | 58 | cell 28 B bit3 **and** cell 50 R bit0 | v5 + Ext2 + Ext3 + Ext4 + Ext5 | **decodes the 51-cell prefix** (its samples are 51 cells wide; every checksum of cells 0-50 is intact, cell 50 R bit0 is outside all of them) - a normal Ext4 frame, Ext5 ignored. Not rejected. |

**Hosts:** the capture/locator width ladder is ONE shared list
(`PixelProtocol.V5Widths` = 58/51/43/40/35) consumed by the Windows sampler,
locator and colour learner and by the macOS sampler and scanner, so no host can
pin itself to an older width again. The shortest usable frame on the wire is
still the 35-cell v5 core.

