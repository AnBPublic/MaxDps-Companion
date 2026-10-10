# 3.5.1 "Holdfast" Release Spec

Date: 2026-10-01

## Scope

Release 3.5.1 for **both** app and bridge. `InstallDoctor` compares app vs.
bridge versions and warns on mismatch, so both must move together.

- `PROTOCOL` stays at version **5** (unchanged wire contract).
- This spec **supersedes the no-bump decision** (3.5.0 was previously frozen
  without a codename; 3.5.1 is now a real release).

## Version bumps

| Target | Location | Change |
|---|---|---|
| csproj | `app/MaxDpsCompanion/MaxDpsCompanion.csproj:20` | `3.5.1` + add `<Codename>Holdfast</Codename>` |
| TOC | `addon/MaxDpsBridge/MaxDpsBridge.toc:5` | `3.5.1` |
| Bridge.lua | `addon/MaxDpsBridge/Bridge.lua:78` | `3.5.1` |
| VERSION.txt | `dist/VERSION.txt` (x2) | line 1 bare `3.5.1`; codename/changelog line 2 |
| README | `README.md` | version + codename |
| ARCHITECTURE | `ARCHITECTURE.md:17,394,863-864` | version/codename refs |
| HANDOVER | `HANDOVER.md` | reverse the no-bump decision |
| UI docs | `docs/UI.md` | version/codename refs |

## Wiring changes

- `app/MaxDpsCompanion/Native.cs`: expose `Codename` and
  `DisplayVersion` = `"v3.5.1 Holdfast"`; expose `BuildVersion`.
- `app/MaxDpsCompanion/MainForm.cs:705`: window title updated.
- `app/MaxDpsCompanion/MainForm.cs:714-724`: **remove** the BuildVersion
  label from the title; move hash/time into the Advanced Diagnostics
  Identity card via `Native.BuildVersion`.
- Leave `MainForm.cs:223` and `MainForm.cs:1911` untouched.
- `build.ps1` unchanged.

## Tests

- Title equals `"MaxDPS Companion v3.5.1 Holdfast"` with **no hash**.
- `AppVersion == "3.5.1"`.

## Verification

Run: `build.ps1`, `dotnet test`, `luac -p`, secret harness, ability audit,
and `rg 3.5.0` (only historical hits allowed). **Rebuild dist is mandatory.**

## Live checks (OWED — retail only)

- Window title visual confirmation.
- `/mdb` reports 3.5.1.

> Static (parses/builds) != automated test != live in-game E2E. The live
> items stay OWED until run in a real client.
