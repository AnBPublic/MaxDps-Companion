# Sentinel v4.7.0 — agent deployment handover

Date: 2026-11-01 · Windows companion and Retail 12.1.x addon · Codename **Sentinel**.

## Branch and identity

- Source: https://github.com/AnBPublic/MaxDps-Companion/tree/release/v4.7.0-sentinel
- GitHub Actions: https://github.com/AnBPublic/MaxDps-Companion/actions/workflows/ci.yml
- Identity: `MaxDpsCompanion.exe` v4.7.0 **Sentinel**; in-game addon
  `MaxDpsCompanionBridge` v4.7.0 Sentinel.
- Wire: unchanged protocol v5 (51 cells normally, opt-in Ext5 = 58). The
  existing MaxDps vendor addon is **not replaced or modified**.
- This is a release candidate until retail testing; **do not claim live
  verification** from green GitHub CI. The versioned source and CI ZIPs are
  nevertheless usable for an explicit user-controlled test deployment.

## Download both ZIPs from GitHub

1. Open the GitHub Actions link above. Select the newest **successful CI**
   run whose branch is `release/v4.7.0-sentinel`.
2. Under **Artifacts**, download `MaxDpsCompanion-release-bundles`.
   GitHub login is normally required for Actions artifact downloads.
3. Extract the artifact container to find:
   - `MaxDpsCompanion-4.7.0-Sentinel-win-x64.zip`
   - `MaxDpsCompanionBridge-4.7.0.zip`
4. These names refer to the **actual CI-built** artifacts, not files committed
   under `dist/`; the repo does not track a Windows binary. Artifacts are
   retained for **30 days**. Do not install an archive from a failed run.

## Windows deployment (agent instructions)

1. Stop `MaxDpsCompanion.exe` and close WoW first.
2. Back up the existing working `dist/` folder and the existing
   `_retail_/Interface/AddOns/MaxDpsCompanionBridge` folder.
3. Extract the Windows archive into a **new** folder (e.g.
   `MaxDpsCompanion-Sentinel`), not on top of a running executable. Preserve
   your own `settings.ini` from your previous `dist` folder; do not let any
   default file overwrite personal settings.
4. Extract the bridge ZIP so the resulting WoW addon path is
   `<WoW>/_retail_/Interface/AddOns/MaxDpsCompanionBridge/MaxDpsCompanionBridge.toc`.
   Avoid a doubled nested folder. Keep the installed official MaxDps addon.
   Disable the archived legacy `MaxDpsBridge` per character if present.
5. Run the **new** `MaxDpsCompanion.exe` from the extracted folder and launch
   Retail. Verify the program title says `v4.7.0 Sentinel`, the in-game
   AddOns list shows bridge 4.7.0, and `/mcb status` reports the expected
   protocol/class/spec/bridge identity.
6. If Ext5 was previously enabled it remains opt-in; `/mcb ext5 on` requires
   `/reload`. Do not change it for the Sentinel update alone.
7. Roll back by restoring the backed-up companion and bridge folders
   together. Leave the official MaxDps addon unchanged.

## What Sentinel changes

- Shared observed-health-loss window (plain valid HP only) permits an
  earlier **major** defensive at high pressure, never skips OFF policies.
- Existing MaxDps-pinned, tracked and visibly absent minor tank mitigation
  may be maintained while healthy, without autonomous candidate generation.
- Defensive absorbs on the SelfHeal transport use mitigation policy.
- Failed GCD presses retract speculative mitigation/burst memory.
- No changes to MaxDps-owned main rotation, Lua behavior, protocol, or vendor.

## Offline / live verification

- CI must show Windows Release build, xUnit, Lua harness, Lua syntax, ability
  audit and macOS Core/Mac build successful; artifact packaging must succeed.
- Retail tests are **OWED**: check an Arms Warrior and Protection Warrior as
  DPS/tank representatives; repeat with at least one other class; inspect
  defensives with 100% HP (no spam), buff present/absent, rapid HP loss,
  secret/UNKNOWN HP, OFF per-spell user policy, rejected key press.
- For the broader 40-spec catalog, automated tests verify shared provider
  behavior and invariants; **this is not a claim of live gameplay validation
  for every specialization**.
- Save a telemetry recording for each condition and compare replay verdicts;
  baseline main rotation must never be suppressed by cooldown intelligence.

## Agent instruction

Use the **current** `release/v4.7.0-sentinel` code and the **successful** CI
artifacts, not older releases or old `dist` executables. Do not silently
merge or push to master, overwrite settings, rewrite the vendor, or claim
runtime E2E without retail observations.
