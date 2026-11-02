# MaxDPS Companion v5.0.0 "Ascendant" — deployment and QA

**Target:** WoW Retail Midnight **12.1.5** (addon interface **120105**),
Windows x64 WinUI3 plus WoW Bridge, 51/58-cell unchanged v5 protocol.

## Authoritative release and source

- Branch: https://github.com/AnBPublic/MaxDps-Companion/tree/release/v5.0.0-ascendant
- RC: https://github.com/AnBPublic/MaxDps-Companion/releases/tag/v5.0.0-rc.1
- Companion: `MaxDpsCompanion-5.0.0-Ascendant-win-x64.zip`
- Addon: `MaxDpsCompanionBridge-5.0.0.zip`
- Changes: `docs/CHANGELOG.md`
- Research and known gaps: `docs/research/ASCENDANT-12.1.5-READINESS.md`
- Tests: https://github.com/AnBPublic/MaxDps-Companion/actions/workflows/ci.yml

**Never install until BOTH matching ZIPs are uploaded and the exact
branch build (Windows tests, Lua tests, macOS and audit) is green.**

## Changes to verify in WoW

- Original MaxDps Main remains authoritative on DPS and tanks.
- v5 measured sustained HP drop can enable minor mitigation when the
  MaxDps defensive candidate's own buff is confirmed inactive. All
  major and emergency safety gates remain intact. Unknown health and
  hidden combat secrets must never generate fabricated decisions.
- v5 **opt-in Burst** preset with measured short TTK may conserve
  >=90s major offensives in the per-spec curated candidate list.
  MaxDps Main is unchanged; non-listed offensives, boss-grade
  targets and executes bypass. Because the wire has no explicit
  offensive-source bit, a MaxDps-offered spell also listed in the
  curated pool CAN be conserved by this optional Burst rule.
- Per-ability user OFF means no automatic use, regardless of urgency.
  Solo mode promotes self-sustain; Normal/Group is conservative.
- Existing 4.8/4.9 capture optimizations, calibration, heartbeat,
  keybindings and cooldown rejection recovery must remain correct.
- Actual class/spec coverage is INVENTORY only until live-tested;
  12.1-era source spell data is not magically updated for 12.1.5.

## Installer agent: exact order

1. Exit WoW and the companion process.
2. Back up existing `4.9 Slipstream` application directory, including
   `settings.ini`, and
   `_retail_/Interface/AddOns/MaxDpsCompanionBridge/` plus WoW saved
   variables. Label these backups clearly for rollback.
3. Download both verified `v5.0.0-rc.1` GitHub release archives.
   Unzip the Windows app into its **own NEW** directory; confirm
   `MaxDpsCompanion.exe` and `MaxDpsCompanion.pri`. Restore personal
   `settings.ini` only; never mix legacy DLLs or executables.
4. Install the bridge so
   `_retail_/Interface/AddOns/MaxDpsCompanionBridge/MaxDpsCompanionBridge.toc`
   is a real file (no duplicated directory nesting). Keep official
   MaxDps untouched and the old `MaxDpsBridge` disabled.
5. Launch. Check window title `MaxDPS Companion v5.0.0
   Ascendant`, AddOns version 5.0.0 / interface 120105,
   `/mcb status`, healthy active frame heartbeat, class/spec,
   talents, bindings and preserved Solo/Normal and OFF settings.
6. Smoke-test Main/Offensive/Defensive/SelfHeal/Mobility/Interrupt,
   target range, chat focus hold, cooldown rejection, missing spell,
   unlearned talent, boss combat and low health. Preserve a rollback
   path; restore both 4.9 app and addon if a new anomaly appears.

## Live QA matrix — mandatory, not already passed

Every one of 33 DPS/tank specs: early-level build where applicable,
different available talent/hero builds, freshly 90 geared state,
full MaxDps-led Main, execute, offensive, sustain, interrupts, personal
OFF, spec swap and reload. Do not assume a catalog record proves the
spell is learned, usable or performant. Healers have inventory but the
project does not implement full ally-targeted healing and is not
claimed to do so.

For each relevant role/spec test **world/outdoor, Delves (including
challenging tiers), Labyrinth, normal/heroic dungeons, Mythic+,
normal/heroic raid and realistic boss trials**; collect actual failures.
Secret combat values, healer ally state, encounter mechanics and
movement cannot be reliably inferred if not exposed. No automated
input should be treated as compliant with Blizzard's current rules
without separate user verification.

Compare A) WoW alone, B) v4.9 Slipstream, C) v5 Ascendant with identical
game, graphics/HDR/FG/resolution/driver and repeatable test scenes.
Use PresentMon/CapFrameX, multiple 60–120s warm runs, avg FPS,
1% low FPS, p95/p99 frame time, >50ms spikes, companion CPU/GPU, missed
and duplicate casts and time-to-react. Publish actual comparative
measurements, not estimates. Keep PR draft until approval.

## Limitations

- Current addon snapshot contains **12.1** curated abilities and
  **MaxDps 11.3.49** older sources, and patch-target TOC
  120105 does NOT certify all 12.1.5 skill/talent changes.
- No claims of 100% perfect raid/Mythic+ or every Delve tier.
  Completion depends on movement, positioning, class balance,
  gear and mechanics as well as ability advice.
- MMO combat input automation may breach game terms. This artifact
  is a testing release; verify account risk before enabling it.

## Current upstream-source follow-up

As of 2026-11-01, CurseForge lists official MaxDps Rotation Helper
**v11.3.58 for Retail 12.1.5**. This source repository retains
v11.3.49: any agent claiming 100% talent support must first obtain,
audit, regenerate and test the actual v11.3.58 source and verify the
12.1.5 class changes. Do **not** simply update catalog/version strings
or claim full v5 certification on the basis of existing 12.1 rows.
