# Agent handover: MaxDPS Companion 5.0.0 "Vanguard" RC1

**Target:** WoW Retail Midnight 12.1.5 (`120105`)
· **Windows host:** WinUI3, x64
· **Bridge addon:** `MaxDpsCompanionBridge`
· **Wire protocol:** existing v5/Ext2/Ext3/Ext4 + optional Ext5
(51/58 cells; application 5.0.0 does not mean protocol version 6).

## Canonical source and download

- Source: https://github.com/AnBPublic/MaxDps-Companion/tree/release/v5.0.0-vanguard
- GitHub release: https://github.com/AnBPublic/MaxDps-Companion/releases/tag/v5.0.0-rc.1
- Exact release files, after green CI:
  - `MaxDpsCompanion-5.0.0-Vanguard-win-x64.zip`
  - `MaxDpsCompanionBridge-5.0.0.zip`
- Full changelog: `docs/CHANGELOG.md`
- Cross-spec and all-content acceptance checklist:
  `docs/V5-COVERAGE-AND-VALIDATION.md`
- Run CI: https://github.com/AnBPublic/MaxDps-Companion/actions/workflows/ci.yml

**Only install after the release tag points to the current tested branch
commit and both artifacts are actually uploaded.** Do not install
anything from `master` (which lags the release branches).

## What has changed versus 4.9 Slipstream

- Shared plain-HP trend protection (both Solo and Group major defensive).
  Measured losses rather than a guessed cast/encounter mechanic;
  only existing eligible major DR candidates. Unknown/secret HP is
  not converted into an early-activation decision.
- Early Solo self-sustain if a **measured** decline indicates near-term
  critical HP and the spell's own HP ceiling is met.
- 40-spec coverage census with honest live-verified/delegated/pending
  distinction and explicit DPS/tank/healer roles. It does **not** replace
  MaxDps or prove every 12.1.5 talent.
- WoW addon `.toc` interface updated to `120105`; app and bridge 5.0.0
  version strings; all 4.7, 4.8 and 4.9 fixes retained.
- Original vendor snapshot **MaxDps 11.3.49 / patch 12.1** retained.
  Current 12.1.5 class/talent and upstream changes require separate
  source refresh before full parity can be asserted.

## Agent deployment steps (Windows)

1. **Stop** both WoW and `MaxDpsCompanion.exe`. Back up the complete
   4.9 Slipstream app folder, `settings.ini`, any learned color profile,
   the installed bridge directory and WoW SavedVariables.
2. Download both v5.0.0 RC1 ZIPs from the canonical release page.
   Verify ZIP extraction, non-zero payloads and version strings;
   do not mix v4.9 DLLs with v5 executable files.
3. Unzip the Windows package to a **clean new folder**; check
   `MaxDpsCompanion.exe` and `MaxDpsCompanion.pri` exist.
   Preserve and restore personal settings without overwriting the
   freshly built application binaries.
4. Unzip `MaxDpsCompanionBridge` to
   `<WoW>/_retail_/Interface/AddOns/MaxDpsCompanionBridge/`.
   `MaxDpsCompanionBridge.toc` must be directly inside that folder.
   Official `MaxDps` must be preserved; disable the obsolete
   `MaxDpsBridge` to avoid the mutual-exclusion guard.
5. Launch WoW and the companion; confirm title
   `MaxDPS Companion v5.0.0 Vanguard`,
   addon v5.0.0, WoW interface `120105`, protocol 5,
   healthy heartbeat and correct class/spec, bindings, toggles.
6. Check no error on login, spec swap, talent swap, new character
   level-up, UI reload, zone entry, death/respawn, logout/login,
   monitor resolution/HDR and foreground changes.
7. Compare Solo ON to Solo OFF in the same enemy situation, with
   hard per-ability OFF on a dangerous movement/immune skill.
   Test DPS and tank specs in Delves, outdoor, 5-player dungeons
   and raid content according to the matrix.
8. Record capture/rotation failures with time-stamped logs and
   actual game version. Avoid forcing missing/unknown sensors.
9. Roll back **both** binary and addon to backed-up 4.9 if any
   unexpected send, broken macro binding or repeated frame
   stall occurs. Never discard the original settings.

## Controlled game-acceptance / benchmarking

Read `docs/V5-COVERAGE-AND-VALIDATION.md` before testing.
Test **WoW alone**, **4.9 companion** and **5.0 companion** under the
same hardware, settings, talent build and repeatable 60–120s scene.
Record FPS, 1% lows, p95/p99 frametimes, >50ms frame spikes,
Companion CPU/GPU, failure/recovery events, all user OFF cases,
Main rotation continuity and the effect of enabling Solo.
For each of the 33 DPS/tank specs, collect evidence at low and high
level, fresh talents, at least one real dungeon/raid and a higher
Delve tier; prioritize level 1–90 transitions and live 12.1.5
changed talents. Healers require separate clinical/group-healing
design before any autonomous healer claim.

**Do NOT describe v5 as perfect/full 12.1.5 talent parity.**
Static registration and clean tests are not live verification, and
this build does not establish permission for automated gameplay.

## Copyable instruction for the local testing agent

> Download BOTH v5 Vanguard RC1 ZIPs from the published GitHub release
> after green CI. Back up my entire 4.9 Slipstream app/bridge and
> settings.ini; install the v5 pair in fresh directories. Leave official
> MaxDps and per-ability OFF settings unchanged. Confirm interface
> 120105, addon/app 5.0.0 and protocol 5. Complete the seven acceptance
> gates and separate Solo/Group/Delve/world/dungeon/raid and 1-90
> tests in V5-COVERAGE-AND-VALIDATION.md. Report failures and actual FPS,
> 1% lows and p99 frametimes. Roll back both components on regressions;
> do not merge the draft PR until live game testing passes.
