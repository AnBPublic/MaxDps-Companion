# Architecture — MaxDps-Companion

## Pipeline

```
MaxDps engine (vendor/, read-only)
  └─ MaxDps.Spell (current suggestion per category)
       │
       ▼
MaxDpsBridge addon  — 8-cell pixel strip (protocol v1)
  cell0 magic · 1 Main · 2 CD · 3 Interrupt · 4 Defensive ·
  5 Consumable · 6 state+heartbeat · 7 ver+checksum+commit
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
  addon/MaxDpsBridge/        bridge addon (8-cell encoder, /mdb commands)
  app/MaxDpsCompanion/       WinForms companion (sampler → PostMessage)
    ThisAssembly.Gen.cs      build stamp (git HEAD + date, title bar)
  docs/PROTOCOL.md           normative 8-cell spec
  vendor/                    pinned upstream MaxDps* snapshot (read-only)
    pin-versions.txt         expected folder names
  build.ps1                  kill → stamp → publish → verify → list
  install-addon.ps1          copy bridge → Interface\AddOns
  settings.ini               tracked default (local copy lives in dist\)
  dist/                      publish output (ignored except .gitkeep)
  VERSION.txt                1.0.0 + upstream pin + Interface 120100
```

## Key invariants

- Protocol v1 is fixed at 8 cells; any change bumps cell 7 R and
  `docs/PROTOCOL.md` first.
- Bridge encodes, companion decodes; rotation intelligence lives only in
  upstream MaxDps.
- `settings.ini` keys mirror `AppSettings` sections 1:1; adding a key
  means updating both plus the README table.
