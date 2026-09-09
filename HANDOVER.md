# Handover — MaxDps-Companion

## Status: v1.0.0 scaffold (static only)

Repo-level scaffold landed: README, AGENTS, ARCHITECTURE, docs/PROTOCOL,
build/install scripts, default settings.ini, vendor pin list, VERSION.
Bridge (`addon/MaxDpsBridge/*.lua`) and app (`app/MaxDpsCompanion/*.cs`)
implementation is owned by other agents and not yet reviewed here.

## Validated

- Scaffold files present; PowerShell syntax to be checked with
  `pwsh -NoProfile -Command` parse pass.
- Nothing else: no build run, no retail run.

## Outstanding

1. Bridge addon implementation + `/mdb status` screenshot in retail.
2. Companion `dotnet publish` green + `dist\MaxDpsCompanion.exe` fresh check.
3. Live E2E: strip detected, Main key pressed in-game, pause/state flow.
4. `vendor/` snapshot via robocopy at install time (lead; do NOT copy the
   ~8.6 MB through agent context).

## Next steps for lead

- Review scaffold, `git init` already done — set origin to
  `github.com/AnBPublic/MaxDps-Companion.git` when ready, then first commit.
- Robocopy upstream `MaxDps*` folders into `vendor/` at install time.
- Schedule retail E2E before any v1.1 claim.
