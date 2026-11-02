# v5 Ascendant: patch 12.1.5 ability and encounter readiness audit

Date: 2026-11-02.

## External sources

- Official 12.1.5 notes: https://worldofwarcraft.blizzard.com/en-us/news/24304162/
- Patch interface `120105` and API deltas:
  https://warcraft.wiki.gg/wiki/Patch_12.1.5
  https://warcraft.wiki.gg/wiki/Patch_12.1.5/API_changes
- Blizzard addon secrets:
  https://news.blizzard.com/en-us/article/24246290/combat-philosophy-and-addon-disarmament-in-midnight
- New gameplay: The Unbinding of Kith'ix raid, Labyrinth of Kindo'jan,
  outdoor Aqir Invasions, Keystone Mythic, Delves and Season 2 refresh.

## Proven source lineage

- `vendor-abilities.json`: extracted from pinned MaxDps **11.3.49**.
- `class-spells.json`: older MaxDps classSpellData tokens; verified
  by the pinned `spell-verification.json` DB2 export.
- `abilities.json`: curated **12.1**, interface **120100**,
  verified **2026-09-28**. It remains so in v5.
- v5 addon TOC `120105` means 12.1.5 *interface compatibility*,
  NOT complete 12.1.5 skill/talent correctness.
- Runtime scanner knows about 40 specs (33 DPS/tank, 7 healer),
  distinguishes row existence from delegated, manual, incomplete
  and older-than-runtime-source entries. It cannot certify every
  hero talent or live raid mechanic without live observations.

## Seven requirement gates and honest limits

1. Rotation: Main is strictly MaxDps-led; not a hand-written
   second APL, no false proc/castability knowledge. Unlearned and
   unbound spells are filtered in the addon.
2. Survival: observed rapid and sustained plain-HP pressure;
   minor early mitigation only if own buff confirmed absent.
   Major, immunity and out-of-range still governed conservatively.
3. Cooldowns: when **measured** TTK is shorter than a long
   companion gap-fill major's threshold, conserve it. Boss/execute
   and direct MaxDps recommendation bypass. Unknown TTK is not
   inferred from enemy health bands.
4. Interrupt/utility: current range and cast sensors, manual CC/
   purge/dispel/ally utility where no reliable game signal exists.
5. User modes: explicit Solo/Normal + per-ability ON/OFF controls
   are preserved. These policy modes apply across outdoor, dungeons
   and raids; not an automatic per-encounter solution.
6. Runtime overhead: 4.9 cropped GDI screen copy plus v5 bounded
   pressure scan; actual FPS requires hardware benchmark.
7. Complete specs: all 40 inventoried, 33 DPS/tank in actual scope;
   healer ally-target rotation is NOT solved, and no live all-talent
   verification has been performed.

## Blocking QA before any "100% across the game" declaration

- Re-extract current 12.1.5 MaxDps class files and DB2 records;
  reconcile added/removed spells, talent variants, hero talents,
  missing ranks and changed cooldowns with verified source IDs.
- Test every 33 DPS/tank spec at multiple levels 1–90 (as
  applicable), talent loadouts, 12.1.5 encounters, multiple UI/HDR
  modes and different learned-spell sets.
- Evaluate real raid/M+ group utility, mechanic handling and FPS.
  Record limitations from WoW's unobservable secret states.
- Validate any combat-input automation separately against game
  policy; never interpret passing tests as permission.

## Newly checked current vendor source — 2026-11-02

CurseForge's live Retail download list explicitly identifies **MaxDps
Rotation Helper v11.3.58** as a release for **12.1.5**, uploaded
2026-11-01:
https://www.curseforge.com/wow/addons/maxdps-rotation-helper/files/all?gameVersionTypeId=517&page=1

The embedded vendor pin **11.3.49** is therefore superseded. Do NOT
claim an exhaustive talent, cooldown, proc or ability audit for v5
until the complete 11.3.58 spell/rotation source is freshly extracted,
diffed for all 40 specs, passed through the DB2/curation generator and
live-tested. Devourer changes in 12.1.5 include Collapsing Star,
Void-Scarred interactions, Soulforged Blades and Voidpurge:
https://worldofwarcraft.blizzard.com/en-us/news/24304162/

This v5 candidate deliberately preserves MaxDps authority instead of
guessing new talent mechanics. Updating the live official MaxDps addon
on the user's machine is distinct from re-pinning this Companion's
embedded cooldown and catalog sources.
