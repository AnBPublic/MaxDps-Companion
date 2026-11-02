# MaxDPS Companion 4.9.0 "Slipstream" RC1 — deployment handover

**Date:** 2026-11-02 · **Retail:** Midnight 12.1 · **Host:** Windows x64
WinUI3 + MaxDpsCompanionBridge · **Protocol:** v5/Ext2/Ext3/Ext4
(51 cells), optional Ext5 (58 cells).

## Authoritative GitHub release

- Source branch: https://github.com/AnBPublic/MaxDps-Companion/tree/release/v4.9.0-slipstream
- Release page: https://github.com/AnBPublic/MaxDps-Companion/releases/tag/v4.9.0-rc.1
- Windows archive: `MaxDpsCompanion-4.9.0-Slipstream-win-x64.zip`
- In-game bridge archive: `MaxDpsCompanionBridge-4.9.0.zip`
- CI workflow: https://github.com/AnBPublic/MaxDps-Companion/actions/workflows/ci.yml
- Draft PR: https://github.com/AnBPublic/MaxDps-Companion/pull/24
- Full history: `docs/CHANGELOG.md`.

**Release CI must be green and BOTH ZIPs must be present and uploaded**.
If either file is unavailable, do not fabricate it, install from `master`
or reuse 4.8 assets. GitHub Actions run artifacts are a secondary
30-day fallback only if the final commit matches exactly.

## Changes to understand before installation

Windows uses one BitBlt per poll, same timer/capture surface/color profile,
but copies only the pixel rows needed for the original five-tap RGB median:
- Cell size >=3: 3 rows (centre-1, centre, centre+1);
- Cell size 2: 2 rows; 1: 1 row.
- Default 8px, 58-cell strip: copied pixels 3712 -> 1392, -62.5%
  transferred *area*; no second capture, no data discarded from sampling.
- The per-frame 58-cell protocol and every MaxDps Main/ability decision,
  Solo, cooldown policy, HP/stagger and per-ability OFF choice are untouched.

This does **not** establish 62.5% lower capture latency, WoW FPS, or GPU
utilization. The remaining GDI/DWM frame synchronization can dominate.

## Local Windows agent installation

1. Exit WoW and stop the running MaxDpsCompanion.exe.
2. Back up existing companion directory and personal `settings.ini`,
   and `<WoW install>/_retail_/Interface/AddOns/MaxDpsCompanionBridge/`.
   Label the backup `4.8-Lightfoot-RC2` and preserve all saved preferences.
3. Download both version **4.9.0 RC1** ZIP files from the release URL.
   Confirm filenames, no stale RC, and ZIP integrity.
4. Unpack the Windows ZIP in a NEW directory. Verify
   `MaxDpsCompanion.exe` and `MaxDpsCompanion.pri` at the expected
   locations. Copy the 4.8 personal `settings.ini` to the new directory.
   Never mix old application DLLs with new ones.
5. Unpack the in-game bridge ZIP to
   `<WoW install>/_retail_/Interface/AddOns/MaxDpsCompanionBridge/`.
   Ensure `MaxDpsCompanionBridge.toc` is directly in that directory,
   rather than a duplicate `MaxDpsCompanionBridge/MaxDpsCompanionBridge`
   nesting. Official MaxDps and its class rotations are untouched. The
   archived legacy `MaxDpsBridge` must stay disabled.
6. Launch new companion and then WoW. Verify app title
   `MaxDPS Companion v4.9.0 Slipstream`, addon version 4.9.0 in the
   AddOns screen, `/mcb status` healthy and protocol 5, correct character
   spec, persistent heartbeat and same saved user policy.
7. Verify Main presses, interrupts, defensive/offensive utility, per-ability
   OFF handling, failed-cast recovery and no capture errors. In particular,
   the reduced-height copy must still decode correctly under **HDR**,
   UI scaling, Windowed Fullscreen and high refresh. Do NOT silently change
   poll interval or reconfigure existing settings.
8. Roll back BOTH app and bridge to backed-up 4.8 if capture/rotation breaks.

## Controlled FPS/frame-time acceptance (still OWED)

On the same PC and driver, with the same resolution/HDR/Frame Generation,
WoW settings/addons, character/spec and repeatable location/route, compare:
A) WoW alone, B) 4.8 Lightfoot with companion enabled, C) 4.9 Slipstream
with companion enabled. Repeat warm 60-120s idle + busy-combat trials
several times. Use PresentMon/CapFrameX or similar; log average FPS, 1%
lows, p95/p99 frametime, frequency of >50ms spikes, WoW GPU/CPU usage
and MaxDpsCompanion.exe CPU time. Also verify no stale/lost bridge frames,
rotation delay, missing cooldowns or unplanned ability sends. Summarize
medians and scene controls. Do not claim real FPS or GPU reduction before
these measurements.

## Instruction for the next local-install agent

Read this runbook and docs/CHANGELOG.md. Download ONLY both artifacts
from v4.9.0-rc.1 once uploaded after green CI, back up 4.8 Lightfoot and
settings.ini, install 4.9 Slipstream Windows app and WoW addon as a pair,
do not alter MaxDps vendor or user's settings, verify version/heartbeat/
rotation, then conduct the controlled A/B measurement above and report
actual fps, 1% lows, p99 spikes, process CPU and GPU use. Preserve a
revert path and do NOT merge the draft PR without approval.
