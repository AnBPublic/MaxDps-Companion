# Lightfoot 4.8.0 RC1 — deployment and performance handover

**Date:** 2026-11-02. **WoW:** Retail Midnight 12.1. **Build:** Windows
WinUI 3 companion + in-game `MaxDpsCompanionBridge`. **Codename:** Lightfoot.

## Exact GitHub release assets

Release page:
https://github.com/AnBPublic/MaxDps-Companion/releases/tag/v4.8.0-rc.1

- Windows companion: `MaxDpsCompanion-4.8.0-Lightfoot-win-x64.zip`
- Addon: `MaxDpsCompanionBridge-4.8.0.zip`
- Source branch: https://github.com/AnBPublic/MaxDps-Companion/tree/release/v4.8.0-lightfoot
- CI: https://github.com/AnBPublic/MaxDps-Companion/actions/workflows/ci.yml

Only install **after both assets exist on the successful CI prerelease**. If
publication fails, the successful branch Actions run's `MaxDpsCompanion-release-bundles`
artifact is the fallback; it expires after 30 days. Do NOT use `master`,
the previous 4.7 ZIPs, or a stale `dist/` directory as the update source.

## PC agent installation procedure

1. Stop the running companion and exit World of Warcraft.
2. Back up the ENTIRE current companion folder, especially `settings.ini`,
   and `_retail_/Interface/AddOns/MaxDpsCompanionBridge`. Keep the backup
   separate and label it `Sentinel-4.7.0`.
3. Download and extract the Windows ZIP into a **new directory**
   (e.g., `MaxDpsCompanion-Lightfoot-4.8.0`); check that
   `MaxDpsCompanion.exe` and `MaxDpsCompanion.pri` both exist.
4. Copy the existing personal `settings.ini` from the 4.7 backup into
   the new folder. Never overwrite it with a packaged default or reset
   per-ability OFF modes, presets, cooldown settings or calibration.
5. Extract the addon ZIP to
   `<WoW install>/_retail_/Interface/AddOns/MaxDpsCompanionBridge/`.
   The `.toc` must be immediately inside this folder, not nested twice.
   Keep official MaxDps (and its class modules), GW2_UI and other addons
   untouched. The archived `MaxDpsBridge` stays disabled if present.
6. Start 4.8 Companion, launch WoW and verify title
   `MaxDPS Companion v4.8.0 Lightfoot`, bridge version 4.8.0 in AddOns,
   and `/mcb status` protocol 5, correct class/spec and healthy heartbeat.
   Existing optional Ext5 setting remains as configured (change requires
   `/reload`).
7. Test key delivery, core rotations, offensive/defensive cooldowns, per-ability
   OFF and recovery after an unsuccessful press. If broken, stop both apps
   and restore matching 4.7 companion + bridge backups together.

## Performance verification (MANDATORY for an FPS claim)

CI tests validate regression and packaging only. They do not measure real
WoW FPS/GPU stutter. Compare on the same PC, driver, graphics settings,
display resolution, FPS cap and FG setting, spec, combat situation, addon
configuration, camera and environment. Run matched 60–120 s trials of
4.7 Sentinel and 4.8 Lightfoot, with the companion both ON and OFF.
Warm up before measuring. Prefer repeat runs and median results.

Collect (PresentMon/CapFrameX or equivalent):
- Average FPS, 1% low FPS, frame-time p95/p99, worst spikes.
- `MaxDpsCompanion.exe` CPU time / working set and WoW process CPU/GPU usage.
- Key cadence and missed Main suggestions; frame/heartbeat loss and
  policy holds; any impact from adaptive polling or optional Ext5.

A code-level reduction in allocations or reads is NOT a measured gain.
Report baseline and new values, exact game context, and whether changes
are statistically repeatable. Do not turn off intelligence as a hidden
workaround; only vary one setting per A/B diagnostic and restore it.

## Change inventory

- `app/MaxDpsCompanion/ScreenSampler.cs`: removes per-cell closures and
  indirect delegate calls; direct five-point median from existing DIB.
  One BitBlt remains and returned Color[] retains snapshot semantics.
- `addon/MaxDpsCompanionBridge/Reader.lua`: curated candidate walks append
  directly into the final capped array, and FrameKey reuses current-tick HP.
- `addon/MaxDpsCompanionBridge/Bridge.lua`: one shared Solo ladder table.
- Version identifiers, `ReleaseNotes.cs`, `docs/CHANGELOG.md`, CI and
  application/bridge metadata updated. Retains complete Sentinel behavior
  for DPS and tanks, all 13 classes / 40 cataloged specs, and protocol v5.
- Live retail verification OWED. Account-policy/third-party automation risks
  are not removed by performance optimization.

## Copyable next-agent instruction

Download the two exact `v4.8.0-rc.1` assets from the GitHub release above
ONLY after checking successful CI. Read this handover, back up matching 4.7
companion/bridge and preserve settings.ini. Install both 4.8 ZIPs into the
appropriate PC/retail-addon folders; leave official MaxDps alone. Verify
4.8.0 Lightfoot identity and /mcb status. Then run a controlled 4.7 vs 4.8
FPS/frametime A/B and report the actual metrics or any blockers. Do not
claim live validation, merge into master, or erase settings.
