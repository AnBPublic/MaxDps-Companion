-- ============================ GENERATED FILE =============================
-- Catalog.lua - generated from the companion's ability knowledge base
-- (app/MaxDpsCompanion/Knowledge/*.json). DO NOT EDIT BY HAND.
--
-- Regenerate:  MaxDpsCompanion.exe --gen-catalog
-- Verified by: tests/MaxDpsCompanion.Tests/CatalogLuaSyncTests.cs
--
-- Contents: class/spec numeric ids (protocol v5 cell 33) and the
-- curated Mobility / SelfHeal spell lists the companion-only slots use.
-- ========================================================================

local _, MDB = ...;

MDB.CATALOG_REVISION = 3;

-- Class ids: fixed alphabetical order (wire format; never reorder).
MDB.ClassIds = { ["DEATHKNIGHT"]=1, ["DEMONHUNTER"]=2, ["DRUID"]=3, ["EVOKER"]=4, ["HUNTER"]=5, ["MAGE"]=6, ["MONK"]=7, ["PALADIN"]=8, ["PRIEST"]=9, ["ROGUE"]=10, ["SHAMAN"]=11, ["WARLOCK"]=12, ["WARRIOR"]=13, }

-- Spec ordinals per class (wire format; never reorder).
MDB.SpecIds = {
  ["DEATHKNIGHT"] = { ["Blood"]=1, ["Frost"]=2, ["Unholy"]=3, },
  ["DEMONHUNTER"] = { ["Devourer"]=1, ["Havoc"]=2, ["Vengeance"]=3, },
  ["DRUID"] = { ["Balance"]=1, ["Feral"]=2, ["Guardian"]=3, ["Restoration"]=4, },
  ["EVOKER"] = { ["Augmentation"]=1, ["Devastation"]=2, ["Preservation"]=3, },
  ["HUNTER"] = { ["Beast Mastery"]=1, ["Marksmanship"]=2, ["Survival"]=3, },
  ["MAGE"] = { ["Arcane"]=1, ["Fire"]=2, ["Frost"]=3, },
  ["MONK"] = { ["Brewmaster"]=1, ["Mistweaver"]=2, ["Windwalker"]=3, },
  ["PALADIN"] = { ["Holy"]=1, ["Protection"]=2, ["Retribution"]=3, },
  ["PRIEST"] = { ["Discipline"]=1, ["Holy"]=2, ["Shadow"]=3, },
  ["ROGUE"] = { ["Assassination"]=1, ["Outlaw"]=2, ["Subtlety"]=3, },
  ["SHAMAN"] = { ["Elemental"]=1, ["Enhancement"]=2, ["Restoration"]=3, },
  ["WARLOCK"] = { ["Affliction"]=1, ["Demonology"]=2, ["Destruction"]=3, },
  ["WARRIOR"] = { ["Arms"]=1, ["Fury"]=2, ["Protection"]=3, },
}

-- Spell variants: id -> alias ids the bridge may meet on a bar or in a
-- macro (override/base/alias resolution). Symmetric; sorted by id.
MDB.SpellAliases = {
  [19647] = { 119910 },
  [34428] = { 202168 },
  [119910] = { 19647 },
  [202168] = { 34428 },
}

-- Curated companion-only slot candidates, in preference order.
-- mobility: gap closers + movement; the policy decides USE/HOLD.
-- selfHeal: self-sustain candidates; Solo mode only.
-- defensive: Red-urgency gap-fill candidates for the Defensive slot
--   (derived from the vendor per-spec defensive lists, Major first, 
--   immunities excluded); used only when MaxDps names no bound defensive.
MDB.Extras = {
  ["DEATHKNIGHT"] = { ["Blood"] = { mobility = { 49576, 48265, 212552 }, selfHeal = { 45470, 48743 }, defensive = { 48707, 48792, 51052, 55233, 49039, 194679, 219809, 48743, 45470 }, }, ["Frost"] = { mobility = { 49576, 48265 }, selfHeal = { 45470, 48743 }, defensive = { 48707, 48792, 51052, 49039, 48743, 45470 }, }, ["Unholy"] = { mobility = { 49576, 48265 }, selfHeal = { 45470, 48743 }, defensive = { 48707, 48792, 51052, 49039, 48743, 45470 }, },
  },
  ["DEMONHUNTER"] = { ["Devourer"] = { mobility = { 195072 }, defensive = { 196718, 198589 }, }, ["Havoc"] = { mobility = { 195072 }, defensive = { 196718, 198589 }, }, ["Vengeance"] = { mobility = { 189110 }, selfHeal = { 263648 }, defensive = { 187827, 196718, 204021, 212084, 203720, 263648, 320341 }, },
  },
  ["DRUID"] = { ["Balance"] = { mobility = { 1850, 102401 }, selfHeal = { 8936, 22842, 108238 }, defensive = { 22812, 22842, 108238, 8936 }, }, ["Feral"] = { mobility = { 1850, 102401 }, selfHeal = { 8936, 22842, 108238 }, defensive = { 61336, 22812, 22842, 108238, 8936 }, }, ["Guardian"] = { mobility = { 1850, 102401 }, selfHeal = { 8936, 22842, 108238 }, defensive = { 61336, 22812, 192081, 22842, 108238, 8936 }, }, ["Restoration"] = { mobility = { 1850, 102401 }, selfHeal = { 8936, 22842, 108238 }, defensive = { 22812, 22842, 108238, 8936 }, },
  },
  ["EVOKER"] = { ["Augmentation"] = { mobility = { 358267 }, selfHeal = { 361469, 360995 }, defensive = { 363916, 374348, 374227, 360995, 361469 }, }, ["Devastation"] = { mobility = { 358267 }, selfHeal = { 361469, 360995 }, defensive = { 363916, 374348, 374227, 360995, 361469 }, }, ["Preservation"] = { mobility = { 358267 }, selfHeal = { 361469, 360995 }, defensive = { 363916, 374348, 374227, 360995, 363534, 361469 }, },
  },
  ["HUNTER"] = { ["Beast Mastery"] = { mobility = { 781 }, selfHeal = { 109304 }, defensive = { 281195, 388035, 109304 }, }, ["Marksmanship"] = { mobility = { 781 }, selfHeal = { 109304 }, defensive = { 281195, 109304 }, }, ["Survival"] = { mobility = { 781, 190925 }, selfHeal = { 109304 }, defensive = { 281195, 109304 }, },
  },
  ["MAGE"] = { ["Arcane"] = { mobility = { 1953 }, defensive = { 342245, 55342, 235450 }, }, ["Fire"] = { mobility = { 1953 }, defensive = { 342245, 235313 }, }, ["Frost"] = { mobility = { 1953 }, defensive = { 342245, 11426 }, },
  },
  ["MONK"] = { ["Brewmaster"] = { mobility = { 109132, 115008, 101643 }, selfHeal = { 322101, 116670 }, defensive = { 115176, 115203, 122278, 122783, 132578, 322507, 119582, 322101, 116670 }, }, ["Mistweaver"] = { mobility = { 109132, 115008, 101643 }, selfHeal = { 322101, 116670, 124682 }, defensive = { 122278, 122783, 243435, 322101, 116670, 124682 }, }, ["Windwalker"] = { mobility = { 109132, 115008, 101643 }, selfHeal = { 322101, 116670 }, defensive = { 122278, 122470, 122783, 243435, 322101, 116670 }, },
  },
  ["PALADIN"] = { ["Holy"] = { mobility = { 190784 }, selfHeal = { 85673, 19750, 633 }, defensive = { 31821, 498, 184662, 210294, 633, 19750, 85673 }, }, ["Protection"] = { mobility = { 190784 }, selfHeal = { 85673, 19750, 633 }, defensive = { 31850, 86659, 498, 53600, 633, 19750, 85673 }, }, ["Retribution"] = { mobility = { 190784 }, selfHeal = { 85673, 19750, 633 }, defensive = { 184662, 403876, 633, 19750, 85673 }, },
  },
  ["PRIEST"] = { ["Discipline"] = { mobility = { 121536 }, selfHeal = { 19236, 2061 }, defensive = { 62618, 17, 586, 19236, 373481, 2061 }, }, ["Holy"] = { mobility = { 121536 }, selfHeal = { 19236, 2061 }, defensive = { 2050, 34861, 64901, 17, 586, 19236, 2061 }, }, ["Shadow"] = { mobility = { 121536 }, selfHeal = { 19236, 2061 }, defensive = { 47585, 17, 586, 15286, 19236, 2061 }, },
  },
  ["ROGUE"] = { ["Assassination"] = { mobility = { 36554, 1856, 2983 }, selfHeal = { 185311 }, defensive = { 5277, 212182, 1966, 185311 }, }, ["Outlaw"] = { mobility = { 36554, 1856, 2983 }, selfHeal = { 185311 }, defensive = { 5277, 212182, 1966, 185311 }, }, ["Subtlety"] = { mobility = { 36554, 1856, 2983 }, selfHeal = { 185311 }, defensive = { 5277, 212182, 1966, 185311 }, },
  },
  ["SHAMAN"] = { ["Elemental"] = { mobility = { 192063 }, selfHeal = { 8004 }, defensive = { 108271, 198103, 5394, 8004 }, }, ["Enhancement"] = { mobility = { 58875, 192063 }, selfHeal = { 8004 }, defensive = { 108271, 198103, 5394, 8004 }, }, ["Restoration"] = { mobility = { 192063 }, selfHeal = { 8004, 77472 }, defensive = { 98008, 108271, 198103, 8004, 77472 }, },
  },
  ["WARLOCK"] = { ["Affliction"] = { mobility = { 48020 }, selfHeal = { 234153, 6789 }, defensive = { 104773, 108416, 6262, 6789, 234153 }, }, ["Demonology"] = { mobility = { 48020 }, selfHeal = { 234153, 6789 }, defensive = { 104773, 108416, 6262, 6789, 234153 }, }, ["Destruction"] = { mobility = { 48020 }, selfHeal = { 234153, 6789 }, defensive = { 104773, 108416, 6262, 6789, 234153 }, },
  },
  ["WARRIOR"] = { ["Arms"] = { mobility = { 100, 6544 }, selfHeal = { 202168, 34428 }, defensive = { 871, 97462, 118038, 23920, 34428, 202168 }, }, ["Fury"] = { mobility = { 100, 6544 }, selfHeal = { 202168, 34428, 184364 }, defensive = { 97462, 184364, 23920, 34428, 202168 }, }, ["Protection"] = { mobility = { 100, 6544, 198304 }, selfHeal = { 202168, 34428, 190456 }, defensive = { 871, 12975, 97462, 2565, 23920, 190456, 34428, 202168 }, },
  },
}
