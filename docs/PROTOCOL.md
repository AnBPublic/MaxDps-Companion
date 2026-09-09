# Pixel protocol v1 — MaxDpsBridge ↔ MaxDpsCompanion

Normative spec. Bridge encodes, companion decodes. 8 cells, left to right,
each `CellSize` physical pixels square. The app samples the centre pixel.

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
| 2 | CD id hi | CD id lo | flags | cooldown suggestion |
| 3 | Interrupt id hi | Interrupt id lo | flags | interrupt suggestion |
| 4 | Defensive id hi | Defensive id lo | flags | defensive suggestion |
| 5 | Consumable id hi | Consumable id lo | flags | potion / trinket |
| 6 | state | heartbeat | version (1) | engine state + liveness |
| 7 | version (1) | checksum | commit | integrity + calibrate id |

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

### Checksum + commit (cell 7)

- R: protocol version (1).
- G: checksum = XOR of all R/G/B nibbles of cells 1–6. Mismatch = drop the
  frame, keep previous state, count a decode error.
- B: commit/calibrate counter — increments on every `/mdb calibrate` run so
  the app can tell a fresh calibration from a stale strip.

## Calibration (54-step pattern)

`/mdb calibrate on` replaces the strip with 54 cells: black, white, then one
step per channel axis (16 steps × R/G/B = 48) plus 4 anchors. The companion
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

## `/mdb` reference

| Command | Effect |
| :--- | :--- |
| `/mdb status` | offset, cell size, state, bound slots |
| `/mdb on` / `/mdb off` / `/mdb toggle` | enable / pause / flip |
| `/mdb offset <x> <y>` | strip origin, physical px from top-left |
| `/mdb cellsize <px>` | 2–64, default 4 |
| `/mdb calibrate on\|off` | 54-step pattern show/hide |
| `/mdb reset` | defaults + pattern cleared |
