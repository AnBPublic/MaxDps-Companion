-- MaxDpsBridgeExp MajorCooldowns.lua
-- Offensive-slot extras (MDBX.FlagOffensiveExtra) for the Exp bridge, plus the
-- HISTORY of the retired Main-slot denylist.
--
-- HISTORY: MDBX.MajorCDDeny existed 3.7.0-3.7.7 (T3 of docs/plans/2026-10-03-
-- custom-maxdps-fork.md) to keep stale / major-cooldown glows out of the MAIN
-- slot. It was REMOVED in 3.7.8 (user directive: MaxDps's own core rotation is
-- authoritative for MAIN, no exception) — whatever upstream (MaxDps +
-- MaxDps_<Class>) glows or picks now encodes as MAIN, former major-cooldown ids
-- included, with MainUsable as the only game-truth veto.
-- See docs/plans/2026-10-10-main-no-deny-3.7.8.md.
--
-- FlagOffensiveExtra is independent of that removal: it only routes the listed
-- ids to the Offensive candidate path; it never kept anything out of MAIN.
local _, MDBX = ...;

-- Non-vendor extras that CategoryOf must additionally classify as "offensive"
-- (Reader.lua CategoryOf tail). 228920 name-verified at
-- vendor/MaxDps/SpellData.lua:50.
MDBX.FlagOffensiveExtra = MDBX.FlagOffensiveExtra or {};
MDBX.FlagOffensiveExtra[228920] = true; -- Ravager (Warrior/Arms)

-- 375576 Divine Toll (Paladin/Retribution) is routed to the Offensive slot via
-- FlagOffensiveExtra while MAIN keeps it. It is a 60 s on-GCD core ability, so
-- Main encoding it is correct behavior. Holy and Protection also glow the same
-- id, so a global deny would wrongly strip Divine Toll from every Paladin
-- spec's rotation. The correct split is: it can still surface as the offensive
-- candidate, but Main keeps it.
MDBX.FlagOffensiveExtra[375576] = true; -- Divine Toll (Paladin/Ret; 60s core, glow cooldown)

-- 2026-10-07 offensive audit R1, batch 2 (Mage/Monk/Paladin/Priest/Rogue).
-- Same split as Divine Toll above: these are core short cooldowns, so Main
-- encoding them is correct. They are added to FlagOffensiveExtra (Offensive
-- candidate path) ONLY; Main keeps them.
MDBX.FlagOffensiveExtra[321507] = true; -- Touch of the Magi (Mage/Arcane; 45s core)
MDBX.FlagOffensiveExtra[84714]  = true; -- Frozen Orb (Mage/Frost; 60s core)
MDBX.FlagOffensiveExtra[205021] = true; -- Ray of Frost (Mage/Frost; 60s core)
MDBX.FlagOffensiveExtra[153595] = true; -- Comet Storm (Mage/Frost; 30s core)
MDBX.FlagOffensiveExtra[325153] = true; -- Exploding Keg (Monk/Brewmaster; 60s core)
MDBX.FlagOffensiveExtra[392983] = true; -- Strike of the Windlord (Monk/Windwalker; 30s core)
MDBX.FlagOffensiveExtra[343527] = true; -- Execution Sentence (Paladin/Retribution; 60s core)
MDBX.FlagOffensiveExtra[343721] = true; -- Final Reckoning (Paladin/Retribution; 60s core)
MDBX.FlagOffensiveExtra[255937] = true; -- Wake of Ashes (Paladin/Retribution; 30s core)
MDBX.FlagOffensiveExtra[263165] = true; -- Void Torrent (Priest/Shadow; 30s core)
MDBX.FlagOffensiveExtra[385627] = true; -- Kingsbane (Rogue/Assassination; 60s core)
MDBX.FlagOffensiveExtra[382245] = true; -- Cold Blood (Rogue/Assassination; 45s core)
MDBX.FlagOffensiveExtra[13877]  = true; -- Blade Flurry (Rogue/Outlaw; 30s core)
MDBX.FlagOffensiveExtra[315341] = true; -- Between the Eyes (Rogue/Outlaw; 30s core)
MDBX.FlagOffensiveExtra[185313] = true; -- Shadow Dance (Rogue/Subtlety; 60s core)
MDBX.FlagOffensiveExtra[212283] = true; -- Symbols of Death (Rogue/Subtlety; 30s core)
MDBX.FlagOffensiveExtra[280719] = true; -- Secret Technique (Rogue/Subtlety; 60s core)

-- ===== R1 extras (2026-10-07 full offensive audit, batch 1) =====
-- Every id below is R1: a 16-60 s APL-glowed CORE cooldown routed to the
-- Offensive slot via FlagOffensiveExtra (Reader CategoryOf tail). They stay in
-- Main because the APL already gates their timing. The companion fork
-- (custom/patches.json P-DATA-025+) un-comments the matching
-- vendor/MaxDps/Cooldowns.lua row; the flags here make CategoryOf recognise it
-- as offensive on the Exp bridge even before/without the fork.
MDBX.FlagOffensiveExtra[51271]  = true; -- Pillar of Frost (DeathKnight/Frost; 60s core)
MDBX.FlagOffensiveExtra[63560]  = true; -- Dark Transformation (DeathKnight/Unholy; 45s core)
MDBX.FlagOffensiveExtra[198013] = true; -- Eye Beam (DemonHunter/Havoc; 40s core)
MDBX.FlagOffensiveExtra[207407] = true; -- Soul Carver (DemonHunter/Vengeance; 60s core)
MDBX.FlagOffensiveExtra[204596] = true; -- Sigil of Flame (DemonHunter/Vengeance; 30s core)
MDBX.FlagOffensiveExtra[202770] = true; -- Fury of Elune (Druid/Balance; 60s core)
MDBX.FlagOffensiveExtra[202425] = true; -- Warrior of Elune (Druid/Balance; 45s core)
MDBX.FlagOffensiveExtra[274837] = true; -- Feral Frenzy (Druid/Feral; 45s core)
MDBX.FlagOffensiveExtra[370452] = true; -- Shattering Star (Evoker/Devastation; 20s core)
MDBX.FlagOffensiveExtra[395152] = true; -- Ebon Might (Evoker/Augmentation; 30s core)
MDBX.FlagOffensiveExtra[396286] = true; -- Upheaval (Evoker/Augmentation; 40s core)
MDBX.FlagOffensiveExtra[257044] = true; -- Rapid Fire (Hunter/Marksmanship; 20s core)
MDBX.FlagOffensiveExtra[260243] = true; -- Volley (Hunter/Marksmanship; 45s core)
MDBX.FlagOffensiveExtra[392060] = true; -- Wailing Arrow (Hunter/Marksmanship; 60s core)

-- ===== R1 extras (2026-10-07 full offensive audit, batch 3: Shaman/Warlock/Warrior) =====
-- Same R1 split: 16-60 s APL-glowed core cooldowns routed to the Offensive slot
-- via FlagOffensiveExtra (Reader CategoryOf tail). The APL already gates their
-- timing, so they stay in Main. The companion fork (custom/patches.json
-- P-DATA-060+) un-comments the matching vendor/MaxDps/Cooldowns.lua row.
MDBX.FlagOffensiveExtra[191634] = true; -- Stormkeeper (Shaman/Elemental; 60s core)
MDBX.FlagOffensiveExtra[375982] = true; -- Primordial Wave (Shaman/Elemental+Enhancement; 45s core)
MDBX.FlagOffensiveExtra[197214] = true; -- Sundering (Shaman/Enhancement; 40s core)
MDBX.FlagOffensiveExtra[386997] = true; -- Soul Rot (Warlock/Affliction; 60s core)
MDBX.FlagOffensiveExtra[278350] = true; -- Vile Taint (Warlock/Affliction; 30s core)
MDBX.FlagOffensiveExtra[205179] = true; -- Phantom Singularity (Warlock/Affliction; 45s core)
MDBX.FlagOffensiveExtra[264130] = true; -- Power Siphon (Warlock/Demonology; 30s core)
MDBX.FlagOffensiveExtra[267171] = true; -- Demonic Strength (Warlock/Demonology; 60s core)
MDBX.FlagOffensiveExtra[152108] = true; -- Cataclysm (Warlock/Destruction; 30s core)
MDBX.FlagOffensiveExtra[6353]   = true; -- Soul Fire (Warlock/Destruction; 45s core)
MDBX.FlagOffensiveExtra[196447] = true; -- Channel Demonfire (Warlock/Destruction; 25s core)
MDBX.FlagOffensiveExtra[260708] = true; -- Sweeping Strikes (Warrior/Arms; 60s core)

-- ===== R2 majors (2026-10-07 full offensive audit) =====
-- >60 s true majors: routed to the Offensive slot via FlagOffensiveExtra and
-- held for the burst window (abilities.json holdForBurst:true / minTtkSec:15).
-- The fork (P-DATA-060+) un-comments the vendor offensive row. They stay in
-- Main's reach (e.g. Army of the Dead Summon) exactly as the plan's work list
-- prescribes. P-DATA-060+.
MDBX.FlagOffensiveExtra[279302] = true; -- Frostwyrm's Fury (DeathKnight/Frost; 120s major)
MDBX.FlagOffensiveExtra[42650]  = true; -- Army of the Dead (DeathKnight/Unholy; 3min Summon)
MDBX.FlagOffensiveExtra[19574]  = true; -- Bestial Wrath (Hunter/Beast Mastery; 120s major)
MDBX.FlagOffensiveExtra[360966] = true; -- Spearhead (Hunter/Survival; 90s major)
MDBX.FlagOffensiveExtra[137639] = true; -- Storm, Earth, and Fire (Monk/Windwalker; 90s major)
MDBX.FlagOffensiveExtra[51690]  = true; -- Killing Spree (Rogue/Outlaw; 90s major)
MDBX.FlagOffensiveExtra[198067] = true; -- Fire Elemental (Shaman/Elemental; 150s major)
MDBX.FlagOffensiveExtra[192249] = true; -- Storm Elemental (Shaman/Elemental; 150s major)
MDBX.FlagOffensiveExtra[51533]  = true; -- Feral Spirit (Shaman/Enhancement; 90s major)
MDBX.FlagOffensiveExtra[111898] = true; -- Grimoire: Felguard (Warlock/Demonology; 120s major)
MDBX.FlagOffensiveExtra[227847] = true; -- Bladestorm (Warrior/Arms; 90s major)
MDBX.FlagOffensiveExtra[391528] = true; -- Convoke the Spirits (Druid/Balance+Feral+Guardian; 120s major)
