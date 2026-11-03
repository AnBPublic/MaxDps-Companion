# MaxDPS Companion v6.0.0 "Northstar" RC2 — deployment agent handover

## Post-RC2 source development (not in the published RC2 ZIPs)

On `feature/northstar-12-1-5-vendor-reconcile`, the generated
cooldown/interrupt inventory was reconciled with official upstream MaxDps
**11.3.58** (target 12.1.5 / Interface 120105). Exactly one source
ID changed: Frost DK Breath of Sindragosa `152279 → 1249658`.
The retail `SpellData.lua` inventory has the same 8,070 class/spec/token/ID
entries in both upstream versions. Source integrity guards and regression
tests were added; detailed evidence is in
`docs/research/MAXDPS-UPSTREAM-12.1.5.md`.

This is an **unreleased feature branch**. Existing RC2 downloads remain
unchanged. Its policy curation is STILL based on 12.1/11.3.49; it is not
verified against all 12.1.5 talents or content. CI and live retail checks are
OWED before a new candidate can be recommended.



**Current version:** Windows WinUI3 6.0.0 / WoW Retail addon 6.0.0.
**Game:** WoW Midnight 12.1.5 (Interface **120105**).
**Wire:** protocol **5** (51 core/optional 58 Ext5 cells), no new
capture protocol. Strict Main default is a behavior change.

## Authoritative downloads and source

- Source: https://github.com/AnBPublic/MaxDps-Companion/tree/release/v6.0.0-northstar
- RC2 Release: https://github.com/AnBPublic/MaxDps-Companion/releases/tag/v6.0.0-rc.2
- App ZIP: `MaxDpsCompanion-6.0.0-Northstar-win-x64.zip`
- WoW addon ZIP: `MaxDpsCompanionBridge-6.0.0.zip`
- Full changelog: `docs/CHANGELOG.md`
- Acceptance and all-ability coverage:
  `docs/V6-REQUIREMENTS-AND-ABILITY-INTELLIGENCE.md`
- GitHub CI:
  https://github.com/AnBPublic/MaxDps-Companion/actions/workflows/ci.yml
- Draft PR: https://github.com/AnBPublic/MaxDps-Companion/pull/27

**Do NOT download until BOTH RC2 ZIPs are present on the actual
GitHub release and Windows / C# / Lua / Mac / ability audit all PASS.**
The prior v5.0.0 RC4 should remain installed until then.

## Exact safe installation for a local Windows agent

1. Exit the WoW client and stop MaxDpsCompanion.exe completely.
2. Back up current **v5.0.0 Ascendant RC4** application directory
   including `settings.ini`, `_retail_/Interface/AddOns/
   MaxDpsCompanionBridge/`, WoW SavedVariables and any custom
   `ability-overrides.json`. Do not delete the user's configuration,
   ability OFF switches or color profile.
3. Download both **v6.0.0 RC2** archives from the URL above and
   verify the names. Extract the Windows archive into a NEW folder
   so old DLLs cannot be mixed; confirm
   `MaxDpsCompanion.exe` and `MaxDpsCompanion.pri`.
4. Copy personal `settings.ini` and supported override files into
   the fresh app folder. Never overwrite curated application JSON
   files with old DLLs or generated defaults.
5. Extract the WoW ZIP into
   `_retail_/Interface/AddOns/MaxDpsCompanionBridge/`.
   The `MaxDpsCompanionBridge.toc` must be directly inside that
   directory (not in a duplicated subfolder). Leave the official
   MaxDps addon and its spec rotations untouched. Keep the old
   `MaxDpsBridge` disabled (mutual exclusion).
6. Start WoW and app; verify title `MaxDPS Companion v6.0.0
   Northstar`, TOC 120105, addon 6.0.0, `/mcb status`, protocol 5,
   calibrated pixel frame heartbeat, class/spec and bindings.
7. **Safety behavior has changed:** with default
   `AllowMainFallback=false`, a strictly power-starved Main that
   MaxDps cannot replace now stays EMPTY rather than choosing a
   static alternate. If this adversely affects Fury/Arms/Ret
   leveling, first capture evidence. `/mdb mainfallback on`
   explicitly re-enables the old fallback; `/mdb mainfallback off`
   returns to strict. Never enable it silently.
8. Confirm `[Window] AllowBackgroundKeys` no longer defeats the
   hard focus stop. Alt-tab while active and while a target/interact
   is queued. Game chat focus must hold all sends; the app must NOT
   type into other windows.
9. Test a manually OFF ability and its talent replacement
   (e.g. Victory Rush/Impending Victory), interrupt and defensives
   under simulated pressure, Solo/Group, spec switch and reload.
10. Produce the per-ability decision contract report (offline):
    `MaxDpsCompanion.exe --ability-contracts=all-abilities.tsv`.
    Check every spell for source owner, incomplete research and
    older-than-12.1.5 metadata. This is an audit list, not
    evidence of complete live correctness.
11. Roll back BOTH app and bridge to v5 RC4 if a regression occurs.
    Keep source PR draft and don't merge until user approval.

## Required controlled acceptance — still OWED

Use the 8-situation table and all-content class/spec matrix in
`docs/V6-REQUIREMENTS-AND-ABILITY-INTELLIGENCE.md`. For real
gameplay, test DPS and tank specs at playable early levels through
90, multiple talent sets, Delves/Labyrinth, world, dungeons/M+ and
raid difficulties. Test unknown/unusable/bound and chat-focus
gates, failed sends and power starvation. All 7 healer specs are
only inventoried, not certified full ally-target healing.

Capture 60–120s warmed repeated WoW-alone vs v5 vs v6 trials at
identical resolution, refresh, HDR, Frame Generation, location,
spec and combat scene. Report mean FPS, 1% lows, p95/p99 frame
latency, >50ms spikes, CPU and GPU of WoW/companion and
missing/double spells.

No all-spell/encounter/FPS success can be proven from CI. The
embedded vendor/curation layer is older 12.1/MaxDps 11.3.49,
while v11.3.58 supports Retail 12.1.5; no current-talent truth
should be invented from the old registry. The game hides some
combat states. Confirm Blizzard input automation/account terms
separately before use. Native performance may still be limited
by GDI synchronization; 4.9 thin-strip capture remains.
