# MaxDPS Companion (Retail Midnight 12.1)

Pixel bridge driver for [kaminaris MaxDps](https://www.curseforge.com/wow/addons/maxdps) **v11.3.43**.
No memory read, no injection, no OCR. It reads the spell MaxDps is currently
suggesting and presses that spell's **player-bound key** via `PostMessage`
into the WoW window only.

Two pieces:

| Piece | What it does |
| :--- | :--- |
| `MaxDpsBridge` (addon, `addon/MaxDpsBridge/`) | Queries the MaxDps rotation engine (`MaxDps.Spell`) each frame and encodes the suggested spells into an 8-cell strip of flat-coloured pixels in the corner of the screen. |
| `MaxDpsCompanion.exe` (desktop app, `app/MaxDpsCompanion/`) | Samples those pixels (~8–20x/second via `CopyFromScreen`), decodes the spell slots, and replays the player's own keybinds into the attached game window only (`PostMessage` `WM_KEYDOWN`/`WM_KEYUP`). |

The pixel strip is the whole interface between them. Upstream MaxDps stays
read-only in `vendor/`; the bridge and companion contain zero rotation
intelligence.

## Protocol v1 (8 cells)

Each cell is `CellSize` physical pixels square. Every channel carries one
nibble encoded as `nibble * 17` (0, 17, …, 255). The app samples the centre
pixel of each cell. Full spec: `docs/PROTOCOL.md`.

| Cell | R | G | B | Meaning |
| :--- | :--- | :--- | :--- | :--- |
| 0 | 15 | 0 | 15 | magic (presence / alignment / calibration reference) |
| 1 | Main spell id hi | id lo | flags | next spell to cast |
| 2 | CD spell id hi | id lo | flags | cooldown suggestion |
| 3 | Interrupt spell id hi | id lo | flags | interrupt suggestion |
| 4 | Defensive spell id hi | id lo | flags | defensive suggestion |
| 5 | Consumable spell id hi | id lo | flags | potion / trinket suggestion |
| 6 | state | heartbeat | version | engine state + liveness |
| 7 | version | checksum | commit | protocol ver + integrity + calibrate id |

Cell 7: R = protocol version (1), G = checksum (XOR of cells 1–6 channels),
B = calibrate/commit counter.

States (cell 6 R): `0` idle, `1` active, `2` paused, `3` no-target,
`4` need-interact. Heartbeat (cell 6 G) increments every frame; a frozen
value means the addon stopped rendering.

## In-game commands (`/mdb`)

| Command | Effect |
| :--- | :--- |
| `/mdb status` | Offset, cell size, state, bound slots. |
| `/mdb on` / `/mdb off` / `/mdb toggle` | Enable / pause the bridge. |
| `/mdb offset <x> <y>` | Move the strip in physical pixels from top-left. |
| `/mdb cellsize <px>` | Resize cells (2–64). |
| `/mdb calibrate on\|off` | Show the 54-step calibration pattern (reports paused while on). |
| `/mdb reset` | Back to defaults (clears calibrate mode). |

## Settings (`settings.ini`)

Sits next to the exe; copied there by `build.ps1` on first publish and never
overwritten afterwards.

```ini
[Bridge]
CellSize=8
OffsetX=0
OffsetY=0

[Window]
ProcessName=Wow
RequireForeground=1
AllowBackgroundKeys=1

[Pause]
Button=Pause

[Spells]
spell1=1
spell2=1
spell3=1
spell4=1
spell5=0

[Timing]
PollIntervalMs=50
MinKeyIntervalMs=120
KeyPressMs=25

[Targeting]
AutoTargetEnabled=0
CombatOnly=1
TargetKey=Tab

[Interact]
InteractEnabled=0
InteractKey=F

[Color]

[Launch]
BNetPath=
```

| Section | Key | Meaning |
| :--- | :--- | :--- |
| `Bridge` | `CellSize`, `OffsetX`, `OffsetY` | Strip geometry; match `/mdb status`. |
| `Window` | `ProcessName` | Game process without `.exe` (`Wow`). |
| `Window` | `RequireForeground` | `1` = mouse/interact keys only send while focused. |
| `Window` | `AllowBackgroundKeys` | `1` = spell keys send while in background. |
| `Pause` | `Button` | Global pause hotkey (`Pause`, `F9`, …). |
| `Spells` | `spell1`…`spell5` | Which slots may fire: Main, CD, Interrupt, Defensive, Consumable (off by default — fire manually). |
| `Timing` | `PollIntervalMs` / `MinKeyIntervalMs` / `KeyPressMs` | Sample rate / gap between inputs / hold length. |
| `Targeting` | `AutoTargetEnabled`, `CombatOnly`, `TargetKey` | Press `TargetKey` when bridge reports no-target. Never a movement key. |
| `Interact` | `InteractEnabled`, `InteractKey` | Press `InteractKey` when bridge reports need-interact (state 4). |
| `Color` | (empty = unlearned) | Learned display-chain profile from `/mdb calibrate`. |
| `Launch` | `BNetPath` | Empty = auto-detect Battle.net. **Launch Game** uses the launcher's remembered account — no credentials stored anywhere. |

## Build

```powershell
.\build.ps1
```

Publishes `MaxDpsCompanion.exe` into `dist\` (Release, win-x64,
framework-dependent single file). Kills any running copy first so a locked
exe cannot silently survive, stamps commit + date into the title bar.

## Install

```powershell
.\install-addon.ps1
.\build.ps1
```

`install-addon.ps1` copies `addon\MaxDpsBridge` into the retail
`Interface\AddOns` folder (default
`A:\Games\BattleNet\World of Warcraft\_retail_\Interface\AddOns`). It never
touches the upstream `MaxDps*` folders. Then in game: `/reload`, `/mdb status`.

Version locations: addon `MaxDpsBridge.toc` (`## Version:`) + app title bar
(`ThisAssembly.Gen.cs` stamp shown in the window title).

## Safety limits

- Client must be **windowed or borderless windowed**, and stay **visible**
  (not minimized, not fully covered) — GDI sampling goes blind otherwise.
- Keys are posted to the game window handle only; if the window is lost,
  nothing is sent.
- Mouse-bound slots still need window focus (no per-window mouse message
  equivalent).
- Expect ~50–120 ms pixel-to-key latency; this is a suggestion follower,
  not a frame-perfect bot.
- MaxDps is a **single-spell engine** — cell 1 (Main) is the rotation;
  cells 2–5 are situational extras, most users leave Consumable off.

## Layout

```
MaxDps-Companion/
  addon/MaxDpsBridge/      bridge addon (other agent owns *.lua)
  app/MaxDpsCompanion/     C# .NET 8 source (other agent owns *.cs)
  docs/PROTOCOL.md         full 8-cell pixel protocol
  vendor/                  pinned upstream MaxDps snapshot (read-only)
  build.ps1                publish exe into dist\
  install-addon.ps1        copy bridge addon into Interface\AddOns
  settings.ini             default settings
  HANDOVER.md              status / next steps
  ARCHITECTURE.md          pipeline + file map
  VERSION.txt              repo version + upstream pin
```
