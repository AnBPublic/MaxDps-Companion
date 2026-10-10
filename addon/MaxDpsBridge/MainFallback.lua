--- ============================ HEADER ============================
-- MainFallback: the deliberate, user-approved exception to the bridge's
-- "only encode what MaxDps already suggests" rule (plan
-- docs/plans/2026-10-03-no-downtime-main.md, Q1 = NO empty Main). When every
-- non-denied glowing Main candidate is power-starved, Reader.GetMainSpellID
-- returns the first castable entry here instead of an EMPTY slot.
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
local addonName, MDB = ...;

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

MDB.MainFallback = {
  [72] = Fury,              -- Fury specID
  ["WARRIOR:Fury"] = Fury,  -- classSpecKey
};
