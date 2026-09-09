# Handover — MaxDps-Companion

## Status: v1.0.0 built (static + smoke verified, live E2E outstanding)

Commit `ac84240`: 8-cell `MaxDpsBridge` addon + `MaxDpsCompanion` .NET8
WinForms app (PRP chrome, Aethys engine), vendor snapshot of upstream
MaxDps v11.3.43 + all class modules, `build.ps1` publish green,
`install-addon.ps1` deployed to retail `_retail_\Interface\AddOns`.

## Validated

- `dotnet build -c Release`: 0 warnings, 0 errors.
- `--ui-smoke-test` exit 0; `--ui-snapshot` renders hero card clean
  (status + 7 toggles + strip + link + 5 buttons).
- Bridge installed at retail AddOns\MaxDpsBridge (7 files); upstream
  MaxDps* folders untouched.
- Start Menu shortcut `MaxDPS Companion.lnk` created (taskbar: right-click
  the running app → Pin to taskbar; assembly identity is set).
- BNet launch: `battlenet://WoW/` protocol first, exe fallback minimized;
  remembered-account login, no credentials anywhere.

## Outstanding (needs retail run)

1. `/mdb status` in game + strip decode (`link alive`).
2. `Calibrate colors` learn + saved profile separation.
3. Main/CD/Interrupt/Defensive key presses in-game; AutoTarget/Interact
   kill-switches default OFF.
4. GitHub: create `AnBPublic/MaxDps-Companion`, set origin, push.

## Perf profile (this release)

- Strip 8x1 px, paint-cached Lua, 20Hz update; sampler 8x1 BitBlt +
  centre-pixel read (~1ms/tick), drift-free sleep pacing (~2% core);
  UI 250ms timer with change-gated Invalidate (zero repaint at rest).
