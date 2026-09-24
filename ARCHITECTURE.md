# Architecture — MaxDps-Companion

## Pipeline

```
MaxDps engine (vendor/, read-only)
  └─ MaxDps.Spell (current suggestion per category)
       │
       ▼
MaxDpsBridge addon  — 9-cell pixel strip (protocol v2, bridge 1.2.0)
  cell0 magic · 1 Main · 2 Offensive · 3 Interrupt · 4 Defensive ·
  5 Consumable · 6 Trinket · 7 state+heartbeat · 8 ver+checksum+commit
  (Spell Frame naming: Show offensive / defensive / consumable / trinket)
  slots gated: only ready spells encode (cooldown/usable/charges;
  interrupt slots need a live interruptible cast)
       │  (flat colours, top-left corner overlay)
       ▼
MaxDpsCompanion.exe — CopyFromScreen sample @ PollIntervalMs
  decode nibbles → map spell id to player keybind
       │
       ▼
PostMessage WM_KEYDOWN/WM_KEYUP → WoW window only
  (player-bound keys; mouse/interact gated on foreground)
```

No memory read, no injection, no OCR at any stage.

## File map

```
MaxDps-Companion/
  addon/MaxDpsBridge/        bridge addon (9-cell encoder, /mdb commands)
  app/MaxDpsCompanion/       WinForms companion (sampler → PostMessage)
    ThisAssembly.Gen.cs      build stamp (git HEAD + date, title bar)
  docs/PROTOCOL.md           normative 9-cell spec
  vendor/                    pinned upstream MaxDps* snapshot (read-only)
    pin-versions.txt         expected folder names
  build.ps1                  kill → stamp → publish → verify → list
  install-addon.ps1          copy bridge → Interface\AddOns
  settings.ini               tracked default (local copy lives in dist\)
  dist/                      publish output (ignored except .gitkeep)
  VERSION.txt                1.2.0 + upstream pin + Interface 120100
```

## Key invariants

- Protocol v2 is fixed at 9 cells; any change bumps cell 8 R and
  `docs/PROTOCOL.md` first.
- Compatibility contract (Sep-2026 calibrate outage): the companion
  decodes protocol N and N-1 (`PixelProtocol.Decode` v2+v1,
  `ColorLearner.Classify` 9-cell+8-cell, `BlockLocator.Verify` both
  windows, engine `Tick` + calibrate `SampleBoth`/`PatternVisible` probe
  both widths). The addon encodes N only. Version skew warns, never
  hard-fails — recalibrate is the repair path. See `docs/PROTOCOL.md`.
- Bridge encodes, companion decodes; rotation intelligence lives only in
  upstream MaxDps.
- `settings.ini` keys mirror `AppSettings` sections 1:1; adding a key
  means updating both plus the README table.
