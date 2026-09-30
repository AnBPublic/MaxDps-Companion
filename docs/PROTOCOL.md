# Pixel protocol v5 + Ext2/Ext3 (additive; 35/40/43-cell) — MaxDpsBridge ↔ MaxDpsCompanion

Normative spec. Bridge encodes, companion decodes. The frame is the **35-cell
v5 core** plus the **additive Ext2 block** (cells 35-39) and the **additive
Ext3 block** (cells 40-42), so a bridge that ships Ext3 renders a **43-cell**
strip, left to right, each `CellSize` physical pixels square.
The app samples the centre pixel (5-tap median at ≥2 px cells).

> **Compatibility contract (Sep-2026 outage lesson):** the companion MUST
> decode the previous protocol versions too. It decodes **v5/Ext3 (43 cells),
> v5/Ext2 (40 cells), v5/v6 (35 cells, v6 accepted forward-compatible), v4
> (9 cells) and v1 (8 cells)**; the in-game addon encodes the widest layout it
> ships (Ext2 = 40 cells; Ext3 = 43). The addon goes stale whenever the user
> runs a new exe without `install-addon.ps1` + `/reload`. Rule for every
> protocol bump: **companion decodes N and the older supported widths; addon
> encodes N**, and the app warns (never hard-fails) on skew so recalibrate is
> the repair path. The defensive-urgency block, Ext2 and Ext3 are all
> **additive**: a pre-2.3 encoder leaves the three urgency nibbles at `0` =
> UNKNOWN, a pre-3.0 encoder leaves cell 33 B bit2 clear (cells 35-39 ignored),
> and a pre-Ext3 encoder leaves cell 28 B bit2 clear (cells 40-42 ignored). The
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
| 28 | cast state | remaining band | Ext2: SelfHeal2 range bits0-1; Ext3: presence bit2; bits3 reserved | player cast/channel (+ alternate range + Ext3 presence) |
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
(`addon/MaxDpsBridge/Toggles.lua` + `Panel.lua`): 8 per-slot toggles (slots
1-8) plus Solo / Out-of-combat / Auto-target / Auto-interact / TTK. The addon
only ever **restricts** what the companion already decided —
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
- **TTK OFF** forces the target HP band (cell 29 G) to `15` = UNKNOWN; the
  melee flag and cast/interrupt flags are unchanged. Collateral: the
  companion's execute and other target-band consumers (including the v3.2.0
  TTK gates) go blind exactly as they do for a hidden band.

`/mdb toggles`, `/mdb <key> on|off`, `/mdb all on|off`, `/mdb overlay on|off`
and `/mdb why heal` are command-surface only; `/mdb status` gains a trailing
`toggles=N/13 ON; OFF: ...` field.

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
into `addon/MaxDpsBridge/Catalog.lua` from the companion's knowledge base
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
block (`addon/MaxDpsBridge/Bridge.lua`, `PROTOCOL_VERSION = 5`).

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
| 41 | R/G/B | `R = mask bits 12-13` (bits 0-1; bits 2-3 reserved), `G = epoch` (0-15), `B = blocked nibble` (0-15) |
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
