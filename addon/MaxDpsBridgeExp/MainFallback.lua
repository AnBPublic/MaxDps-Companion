--- ============================ HEADER ============================
-- MainFallback: the deliberate, user-approved exception to the bridge's
-- "only encode what MaxDps already suggests" rule (plan
-- docs/plans/2026-10-03-no-downtime-main.md, Q1 = NO empty Main). When every
-- non-denied glowing Main candidate is power-starved, Reader.GetMainSpellID
-- returns the first castable entry here instead of an EMPTY slot. Currently
-- covers Warrior Fury/Arms and Paladin Retribution; each is the same
-- user-approved, per-spec exception to the bridge-never-invents rule.
--
-- RULES
--   * Baseline rotational fillers ONLY: no power-gated casts (a filler that
--     itself costs the missing resource would just encode EMPTY again).
--   * Every id is verified by spell NAME against vendor/MaxDps/SpellData.lua
--     (and Cooldowns.lua); never invented from memory.
--   * Specs not listed here are intentionally EMPTY -> Reader returns nil and
--     the Main slot stays blank rather than pressing an unverified key.
--
-- Keyed by numeric specID (MaxDps.idtospec id) and by the readable
-- "CLASS:Spec" key ClassSpec produces. Reader tries specID first, then the
-- string key, then a class-level list (none populated yet).
local addonName, MDBX = ...;

-- Fury: Bloodthirst (23881) is the cornerstone rage GENERATOR — it costs no
-- Rage, so it is castable precisely when Rampage (184367, 80 Rage) is
-- power-starved. Verified by name against the read-only vendor tree:
--   vendor/MaxDps/SpellData.lua:217   ["Bloodthirst"] = 23881
--   vendor/MaxDps/SpellData.lua:352   ["Bloodthirst"] = 23881 (Fury table)
--   vendor/MaxDps/spell_durations.lua:886   [23881] ...
--   vendor/MaxDps_Warrior/Specialization/TWW/Fury.lua:246
--       CheckSpellUsable(classtable.Bloodthirst, 'Bloodthirst')
--   vendor/MaxDps_Warrior/Specialization/TWW/Fury.lua:433
--       classtable = MaxDps.SpellTable
local Fury = { 23881 };

-- Arms: Mortal Strike (12294) then Overpower (7384). Both are baseline Arms
-- rotational buttons, cost Rage (Mortal Strike 30 / Overpower 10) so at least
-- one is castable when the engine's Main glow was denied or power-starved.
-- Verified by name against the read-only vendor tree:
--   vendor/MaxDps/SpellData.lua:43   ["Arms"] retail classSpellData table
--   vendor/MaxDps/SpellData.lua:146  ["MortalStrike"] = 12294
--   vendor/MaxDps/SpellData.lua:123  ["Overpower"] = 7384
--   vendor/MaxDps_Warrior/Specialization/TWW/Arms.lua:183
--       CheckSpellUsable(classtable.MortalStrike, 'MortalStrike')
--   vendor/MaxDps_Warrior/Specialization/TWW/Arms.lua:192
--       CheckSpellUsable(classtable.Overpower, 'Overpower')
local Arms = { 12294, 7384 };

-- Retribution: Judgment (20271, 30 yd) first, then Blade of Justice (184575),
-- then Crusader Strike (35395). All three are baseline rotational builders
-- that cost no Holy Power, so at least one is castable when the engine's Main
-- glow was denied or power-starved (e.g. Templar's Verdict / Divine Storm
-- needing Holy Power). The list is preference order.
--
-- Deliberately EXCLUDED:
--   * talent/form variants 407480 and 406647 (Judgment / Blade of Justice
--     talent swaps) — same abilities, different ids; not baseline.
--   * Holy Power finishers 85256 (Templar's Verdict) and 53385 (Divine Storm)
--     — they SPEND the missing resource and would encode EMPTY again.
-- Verified by name against the read-only vendor tree:
--   vendor/MaxDps/SpellData.lua:1073  ["CrusaderStrike"] = 35395
--   vendor/MaxDps/SpellData.lua:1131  ["BladeofJustice"] = 184575
--   vendor/MaxDps/SpellData.lua:1219  ["Judgment"] = 20271
local Ret = { 20271, 184575, 35395 };

MDBX.MainFallback = {
  [72] = Fury,              -- Fury specID
  ["WARRIOR:Fury"] = Fury,  -- classSpecKey
  [71] = Arms,              -- Arms specID
  ["WARRIOR:Arms"] = Arms,  -- classSpecKey
  [70] = Ret,               -- Retribution specID
  ["PALADIN:Retribution"] = Ret,  -- classSpecKey
};
