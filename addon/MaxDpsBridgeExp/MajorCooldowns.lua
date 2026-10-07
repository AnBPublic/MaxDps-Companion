-- MaxDpsBridgeExp MajorCooldowns.lua
-- T3 of docs/plans/2026-10-03-custom-maxdps-fork.md (bridge-side primary fix).
--
-- WHY: between engine ticks MaxDps.Spell is stale and SpellsGlowing can still
-- carry the id the previous tick glowed — for a major offensive/on-use CD that
-- means GetMainSpellID re-encodes a 2-3 min cooldown as the MAIN rotation pick
-- and the companion holds the whole rotation on it. This table is the
-- bridge-side denylist: GetMainSpellID skips every denied id in the
-- SpellsGlowing scan and rejects a denied MaxDps.Spell, falling through to the
-- next allowed source / nil.
--
-- NO DEADLOCK ON EMPTY MAIN (architect Q1): the Offensive slot is a SEPARATE
-- candidate path (Reader.lua GetOffensiveCandidate -> FirstFlagged("offensive")
-- over MaxDps.Flags, Reader.lua:2014-2024) and does not depend on
-- GetMainSpellID. ActionScheduler.cs:543-575 only reaches Hold(PolicyHold) /
-- Hold(NoCandidate) when NO candidate of ANY slot survives; Main simply being
-- empty makes the Main slot absent (`bySlot[i] is not { Count: > 0 }`), it does
-- not veto the Offensive candidate. A hold is non-latching: every tick is
-- re-evaluated, so the moment the offensive CD is ready/policy-Use it fires
-- with no extra delay. Denying a Main pick therefore cannot strand the
-- rotation; it removes exactly one wrong key.
--
-- ID PROVENANCE: every id below was vendor-verified by spell NAME against
-- vendor/MaxDps/SpellData.lua and vendor/MaxDps/Cooldowns.lua (v11.3.49). The
-- live 12.1 mapping is OWED to a retail run — static != live; offline cannot
-- confirm the current client ids. "move" = vendor id that the plan says moved
-- in 12.1 (kept as a belt-and-braces guard against a stale glow); "correct" =
-- major-CD id that did not move but must still be excluded from the MAIN slot.
local _, MDBX = ...
MDBX.MajorCDDeny = MDBX.MajorCDDeny or {};
local D = MDBX.MajorCDDeny;

-- ===== Needs move (stale vendor ids per the 12.1 species) =====
D[190319] = true; -- Combustion (Mage/Fire)
D[365350] = true; -- Arcane Surge (Mage/Arcane)
D[12051]  = true; -- Evocation (Mage)
D[384352] = true; -- Doom Winds (Shaman/Enhancement)
D[196277] = true; -- Implosion (Warlock/Demonology)
D[391109] = true; -- Dark Ascension (Priest/Shadow)
D[207289] = true; -- Unholy Assault (DeathKnight/Unholy)
D[258860] = true; -- Essence Break (DemonHunter/Havoc)
D[262161] = true; -- Warbreaker (Warrior)
D[357210] = true; -- Deep Breath (Evoker/Preservation)
D[343294] = true; -- Soul Reaper (DeathKnight)
D[194844] = true; -- Bonestorm (DeathKnight/Blood)
D[342817] = true; -- Glaive Tempest (DemonHunter)
D[258925] = true; -- Fel Barrage (DemonHunter)
D[50334]  = true; -- Berserk (Druid/Guardian)
D[200851] = true; -- Rage of the Sleeper (Druid/Guardian)
D[382266] = true; -- Fire Breath (Evoker)
D[382411] = true; -- Eternity Surge (Evoker/Devastation)
D[114165] = true; -- Holy Prism (Paladin/Holy)
D[200174] = true; -- Mindbender (Priest/Shadow)
D[385952] = true; -- Shield Charge (Warrior/Protection)

-- ===== Already correct (major CDs; ids unchanged, still excluded from MAIN) =====
D[107574] = true; -- Avatar (Warrior)
D[1719]   = true; -- Recklessness (Warrior)
D[228260] = true; -- Void Eruption (Priest/Shadow)
D[191427] = true; -- Metamorphosis (DemonHunter/Vengeance)
D[265187] = true; -- Summon Demonic Tyrant (Warlock/Demonology)
D[34433]  = true; -- Shadowfiend (Priest/Shadow)
D[288613] = true; -- Trueshot (Hunter/Marksmanship)
D[12472]  = true; -- Icy Veins (Mage/Frost)
D[194223] = true; -- Celestial Alignment (Druid/Balance)
D[102560] = true; -- Incarnation: Chosen of Elune (Druid/Balance)
D[102543] = true; -- Incarnation: Avatar of Ashamane (Druid/Feral)
D[114050] = true; -- Ascendance (Shaman/Elemental)
D[114051] = true; -- Ascendance (Shaman/Enhancement)
D[1122]   = true; -- Summon Infernal (Warlock/Destruction)
D[121471] = true; -- Shadow Blades (Rogue)

-- ===== Reinstated / newly-denied (2026-10-04 arms-fury-exec-fix) =====
-- 167105 Colossus Smash is NO LONGER denied: the 12.1 stale-id guess was
-- wrong — the id is a ~45 s core Arms rotation button, not a 2-3 min major.
-- spell-verification.json:4044 confirms id 167105 = "Colossus Smash" (verified
-- true against the live DB2 export), so Main may encode it again.
--
-- 228920 Ravager is a Warrior/Arms major (Main must never carry it). Vendor
-- name-verified at vendor/MaxDps/SpellData.lua:50 ["Ravager"] = 228920 (the
-- retail classSpellData ["Warrior"]["Arms"] table opens at SpellData.lua:43).
-- It was previously kept out of the Offensive slot only because it is absent
-- from MaxDps.classCooldowns; FlagOffensiveExtra (below) routes it there.
D[228920] = true; -- Ravager (Warrior/Arms)

-- Non-vendor extras that CategoryOf must additionally classify as "offensive"
-- (Reader.lua CategoryOf tail). 228920 name-verified at
-- vendor/MaxDps/SpellData.lua:50.
MDBX.FlagOffensiveExtra = MDBX.FlagOffensiveExtra or {};
MDBX.FlagOffensiveExtra[228920] = true; -- Ravager (Warrior/Arms)

-- 375576 Divine Toll (Paladin/Retribution) is DELIBERATELY NOT added to
-- MajorCDDeny (D) above. It is a 60 s on-GCD core ability, so Main encoding it
-- is correct behavior, not the stale-glow failure MajorCDDeny exists to fix.
-- Holy and Protection also glow the same id, so a global deny would wrongly
-- strip Divine Toll from every Paladin spec's rotation. Routing it to the
-- Offensive slot via FlagOffensiveExtra (without denying it from Main) is the
-- correct split: it can still surface as the offensive candidate, but Main
-- keeps it. Do NOT add D[375576].
MDBX.FlagOffensiveExtra[375576] = true; -- Divine Toll (Paladin/Ret; 60s core, glow cooldown)

-- 2026-10-07 offensive audit R1, batch 2 (Mage/Monk/Paladin/Priest/Rogue).
-- Same split as Divine Toll above: these are core short cooldowns, so Main
-- encoding them is correct. They are added to FlagOffensiveExtra (Offensive
-- candidate path) ONLY -- deliberately NOT to D (MajorCDDeny). Do NOT add D[].
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
-- Offensive slot via FlagOffensiveExtra (Reader CategoryOf tail). NONE is added
-- to D[] (MajorCDDeny): R1 abilities stay in Main because the APL already gates
-- their timing — denying them would wrongly strip a correct Main pick. The
-- companion fork (custom/patches.json P-DATA-025+) un-comments the matching
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
-- via FlagOffensiveExtra (Reader CategoryOf tail). NONE is added to D[]
-- (MajorCDDeny): the APL already gates their timing, so denying them would
-- wrongly strip a correct Main pick. The companion fork (custom/patches.json
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
-- The fork (P-DATA-060+) un-comments the vendor offensive row. D[] (MajorCDDeny)
-- is added ONLY for the stale-glow candidates below, where the APL may still
-- glow the major while Main could re-encode it; the remaining R2 ids are left in
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

-- R2 stale-glow deny candidates (the plan's explicit D[] set).
D[279302] = true; -- Frostwyrm's Fury (DeathKnight/Frost)
D[19574]  = true; -- Bestial Wrath (Hunter/Beast Mastery)
D[198067] = true; -- Fire Elemental (Shaman/Elemental)
D[192249] = true; -- Storm Elemental (Shaman/Elemental)
D[51533]  = true; -- Feral Spirit (Shaman/Enhancement)
D[111898] = true; -- Grimoire: Felguard (Warlock/Demonology)
D[391528] = true; -- Convoke the Spirits (Druid/Balance+Feral+Guardian)
