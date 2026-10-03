-- MaxDpsBridge MajorCooldowns.lua
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
local _, MDB = ...
MDB.MajorCDDeny = MDB.MajorCDDeny or {};
local D = MDB.MajorCDDeny;

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
D[167105] = true; -- Colossus Smash (Warrior/Arms)
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
