# Pixel protocol v2 — MaxDpsBridge ↔ MaxDpsCompanion

Normative spec. Bridge encodes, companion decodes. 9 cells, left to right,
each `CellSize` physical pixels square. The app samples the centre pixel.

> **Compatibility contract (Sep-2026 outage lesson):** the companion MUST
> decode the previous protocol version too (v1: 8 cells, slots 1-5, status
> 6, version 7). The in-game addon goes stale whenever the user runs a new
> exe without `install-addon.ps1` + `/reload` — the Sep-2026 v2-only
> `Classify` rejected the live v1 pattern outright and calibration read
> "no pattern on screen" although the pattern WAS visible. Rule for all
> future protocol bumps: **companion decodes N and N-1, addon encodes N**,
> and the app warns (never hard-fails) on a version skew so recalibrate
> itself is the repair path.

Slot naming mirrors the in-game Spell Frame categories (MaxDps `Options.lua`
/ `SpellFrame.lua`): offensive = `classCooldowns` offensive bucket,
defensive = `GlowDefensiveHPMidnight`, consumable = `MaxDps.Consumables`
potions, trinket = other `ItemSpells` (equipped on-use trinkets). Interrupt
has no Spell Frame row and stays its own slot.

## Nibble encoding

Every channel carries one nibble: `byte = nibble * 17`
(0, 17, 34, …, 255). Half-step headroom against gamma scaling and
resampling. Decode: `nibble = round(channel / 17)`, tolerance ±8 unless a
`[Color]` profile says otherwise.

## Cells

| Cell | R | G | B | Meaning |
| :--- | :--- | :--- | :--- | :--- |
| 0 | 15 | 0 | 15 | magic: presence + alignment + calibration reference (pure magenta) |
| 1 | Main id hi | Main id lo | flags | next spell to cast |
| 2 | Offensive id hi | Offensive id lo | flags | offensive suggestion (Spell Frame "Show offensive spells") |
| 3 | Interrupt id hi | Interrupt id lo | flags | interrupt suggestion (no Spell Frame row) |
| 4 | Defensive id hi | Defensive id lo | flags | defensive suggestion (Spell Frame "Show defensive spells") |
| 5 | Consumable id hi | Consumable id lo | flags | potion suggestion (Spell Frame "Show consumable spells") |
| 6 | Trinket id hi | Trinket id lo | flags | on-use trinket suggestion (Spell Frame "Show trinket spells") |
| 7 | state | heartbeat | version (2) | engine state + liveness |
| 8 | version (2) | checksum | commit | integrity + calibrate id |

Spell id = 12-bit MaxDps spell index (`hi` = id >> 4, `lo` = id & 0xF).
`0x000` = no suggestion in this slot.

### Flags (cells 1–5 B)

| Bit | Meaning |
| :--- | :--- |
| 0 | Shift held/required |
| 1 | Ctrl held/required |
| 2 | Alt held/required |
| 3 | slot valid (clear = ignore this cell even if id nonzero) |

Bits 4–7 reserved, must be zero.

### States (cell 6 R)

| Value | Meaning |
| :--- | :--- |
| 0 | idle (out of combat / no rotation) |
| 1 | active |
| 2 | paused (`/mdb off`, calibrate mode, or app pause) |
| 3 | no-target (companion may press `TargetKey` if enabled) |
| 4 | need-interact (companion may press `InteractKey` if enabled) |

Heartbeat (cell 6 G): increments every addon frame, wraps 0–15. A value
frozen for >500 ms means the addon stopped rendering — treat as link lost.
Cell 6 B mirrors the protocol version (1).

### Checksum + commit (cell 8)

- R: protocol version (2).
- G: checksum = XOR of all R/G/B nibbles of cells 1–7. Mismatch = drop the
  frame, keep previous state, count a decode error.
- B: commit/calibrate counter — increments on every `/mdb calibrate` run so
  the app can tell a fresh calibration from a stale strip.

## Calibration (54-step pattern)

`/mdb calibrate on` replaces the strip with a 54-step cycling pattern:
black, white, then one step per channel axis (16 steps × R/G/B = 48) plus
4 anchors. All six slot cells carry the flat level. The companion
learns per-channel black/white ranges into `[Color]` and the bridge reports
state 2 (paused) until `/mdb calibrate off`. Learning completes when black,
white, and ≥1 step per axis are known — normally a few seconds. `/mdb off`
also clears a stuck pattern.

## Gamma / HDR notes

- Cell 0 doubles as the legacy black/white reference: its G channel is this
  screen's real black, its R/B the real white.
- With a learned `[Color]` profile every channel normalises against its own
  measured range, covering ReShade / RenoDX / RTX HDR / OS calibration shifts.
- Without a profile the decoder uses the legacy fixed ±8 tolerance around
  the `nibble*17` ladder — fine on SDR, unreliable under HDR.

## Restricted content (Midnight secrets)

In combat / encounters / M+ / PvP, Blizzard marks cooldown and cast data as
secret values. The bridge never compares or does arithmetic on them:
readiness uses only NeverSecret `isActive` / `isOnGCD` / `isEnabled`
booleans plus guarded charge counts, and any secret (spell ID, charge
count, cast flag) degrades fail-open — an empty slot or a trusted
suggestion, never a Lua error. `MaxDps:CooldownConsolidated` and
`C_Spell.GetSpellCooldownDuration` are deliberately unused (their math
touches secrets from tainted execution). Consequence: when a charge count
is secret at 0 charges, the companion may press once into an empty charge;
the game ignores that press.

## `/mdb` reference

| Command | Effect |
| :--- | :--- |
| `/mdb status` | offset, cell size, state, bound slots |
| `/mdb on` / `/mdb off` / `/mdb toggle` | enable / pause / flip |
| `/mdb offset <x> <y>` | strip origin, physical px from top-left |
| `/mdb cellsize <px>` | 2–64, default 4 |
| `/mdb calibrate on\|off` | 54-step pattern show/hide |
| `/mdb reset` | defaults + pattern cleared |
