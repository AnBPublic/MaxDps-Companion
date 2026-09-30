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

MDB.CATALOG_REVISION = 4;

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
-- defensiveMinor: short-cooldown (Minor/None) gap-fill candidates for
--   the Defensive slot's Orange tier (v3.0.0); majors still need Red.
-- defensiveMajor: Major-tier gap-fill candidates for the v3.3.0 Solo
--   escalation ladder (Solo at/below the major HP band only).
-- immunity: full-immunity candidates for the v3.3.0 Solo ladder bottom
--   band (Solo at/below the immunity HP band only; never in groups).
-- offensive: curated major offensive gap-fill candidates (shared burst
--   first, spec-specific second); used only when MaxDps names no bound
--   offensive. The companion detects this source by id membership.
-- cc: auto-eligible curated crowd-control candidates (v3.4.0). The
--   bridge reuses the Interrupt slot for these ONLY when MaxDps names
--   no ready+bound interrupt (never while a live interrupt is pending).
--   No wire source bit exists; the companion's CrowdControlGate is the
--   authority on USE/HOLD. MaxDps-owned stuns are never emitted here.
MDB.Extras = {
  ["DEATHKNIGHT"] = { ["Blood"] = { mobility = { 49576, 48265, 212552 }, selfHeal = { 45470, 48743 }, offensive = { 47568, 194844 }, defensive = { 48707, 48792, 51052, 55233, 49039, 194679, 219809, 48743, 45470 }, defensiveMinor = { 49039, 194679, 219809 }, defensiveMajor = { 48707, 48792, 51052, 55233 }, cc = { 111673, 221562 }, }, ["Frost"] = { mobility = { 49576, 48265 }, selfHeal = { 45470, 48743 }, offensive = { 47568 }, defensive = { 48707, 48792, 51052, 49039, 48743, 45470 }, defensiveMinor = { 49039 }, defensiveMajor = { 48707, 48792, 51052 }, cc = { 111673, 221562 }, }, ["Unholy"] = { mobility = { 49576, 48265 }, selfHeal = { 45470, 48743 }, offensive = { 47568, 49206, 343294, 207289 }, defensive = { 48707, 48792, 51052, 49039, 48743, 45470 }, defensiveMinor = { 49039 }, defensiveMajor = { 48707, 48792, 51052 }, cc = { 111673, 221562 }, },
  },
  ["DEMONHUNTER"] = { ["Devourer"] = { mobility = { 195072 }, defensive = { 196718, 198589 }, defensiveMinor = { 198589 }, defensiveMajor = { 196718 }, immunity = { 196555 }, cc = { 217832 }, }, ["Havoc"] = { mobility = { 195072 }, offensive = { 191427, 370965, 342817, 258860 }, defensive = { 196718, 198589 }, defensiveMinor = { 198589 }, defensiveMajor = { 196718 }, immunity = { 196555 }, cc = { 217832 }, }, ["Vengeance"] = { mobility = { 189110 }, selfHeal = { 263648 }, offensive = { 187827, 370965, 212084 }, defensive = { 187827, 196718, 204021, 212084, 203720, 263648, 320341 }, defensiveMinor = { 203720, 263648, 320341 }, defensiveMajor = { 187827, 196718, 204021, 212084 }, cc = { 217832 }, },
  },
  ["DRUID"] = { ["Balance"] = { mobility = { 1850, 102401 }, selfHeal = { 8936, 22842, 108238 }, offensive = { 102560, 194223 }, defensive = { 22812, 22842, 108238, 8936 }, defensiveMinor = { 22812 }, cc = { 339, 33786 }, }, ["Feral"] = { mobility = { 1850, 102401 }, selfHeal = { 8936, 22842, 108238 }, offensive = { 106951, 102543, 5217 }, defensive = { 61336, 22812, 22842, 108238, 8936 }, defensiveMinor = { 22812 }, defensiveMajor = { 61336 }, cc = { 339, 33786 }, }, ["Guardian"] = { mobility = { 1850, 102401 }, selfHeal = { 8936, 22842, 108238 }, offensive = { 50334, 102558, 200851 }, defensive = { 61336, 22812, 192081, 22842, 108238, 8936 }, defensiveMinor = { 22812, 192081 }, defensiveMajor = { 61336 }, cc = { 339, 33786 }, }, ["Restoration"] = { mobility = { 1850, 102401 }, selfHeal = { 8936, 22842, 108238 }, offensive = { 391528 }, defensive = { 22812, 22842, 108238, 8936 }, defensiveMinor = { 22812 }, cc = { 339, 33786 }, },
  },
  ["EVOKER"] = { ["Augmentation"] = { mobility = { 358267 }, selfHeal = { 361469, 360995 }, offensive = { 403631, 370553 }, defensive = { 363916, 374348, 374227, 360995, 361469 }, defensiveMinor = { 374227 }, defensiveMajor = { 363916, 374348 }, cc = { 360806, 372048 }, }, ["Devastation"] = { mobility = { 358267 }, selfHeal = { 361469, 360995 }, offensive = { 375087, 357210, 404977 }, defensive = { 363916, 374348, 374227, 360995, 361469 }, defensiveMinor = { 374227 }, defensiveMajor = { 363916, 374348 }, cc = { 360806, 372048 }, }, ["Preservation"] = { mobility = { 358267 }, selfHeal = { 361469, 360995, 363534 }, offensive = { 357210, 370553 }, defensive = { 363916, 374348, 374227, 360995, 363534, 361469 }, defensiveMinor = { 374227 }, defensiveMajor = { 363916, 374348 }, cc = { 360806, 372048 }, },
  },
  ["HUNTER"] = { ["Beast Mastery"] = { mobility = { 781 }, selfHeal = { 109304 }, offensive = { 321530, 359844 }, defensive = { 281195, 388035, 109304 }, defensiveMajor = { 281195, 388035 }, immunity = { 186265 }, cc = { 187650 }, }, ["Marksmanship"] = { mobility = { 781 }, selfHeal = { 109304 }, offensive = { 288613 }, defensive = { 281195, 109304 }, defensiveMajor = { 281195 }, immunity = { 186265 }, cc = { 187650 }, }, ["Survival"] = { mobility = { 781, 190925 }, selfHeal = { 109304 }, offensive = { 360952 }, defensive = { 281195, 109304 }, defensiveMajor = { 281195 }, immunity = { 186265 }, cc = { 187650 }, },
  },
  ["MAGE"] = { ["Arcane"] = { mobility = { 1953 }, selfHeal = { 235450 }, offensive = { 365350 }, defensive = { 342245, 55342, 235450 }, defensiveMinor = { 55342, 235450 }, defensiveMajor = { 342245 }, immunity = { 45438, 110959 }, cc = { 118 }, }, ["Fire"] = { mobility = { 1953 }, selfHeal = { 235313 }, offensive = { 190319 }, defensive = { 342245, 55342, 235313 }, defensiveMinor = { 55342, 235313 }, defensiveMajor = { 342245 }, immunity = { 45438 }, cc = { 118 }, }, ["Frost"] = { mobility = { 1953 }, selfHeal = { 11426 }, offensive = { 12472 }, defensive = { 342245, 11426, 55342 }, defensiveMinor = { 11426, 55342 }, defensiveMajor = { 342245 }, immunity = { 45438 }, cc = { 118 }, },
  },
  ["MONK"] = { ["Brewmaster"] = { mobility = { 109132, 115008, 101643 }, selfHeal = { 322101, 116670 }, offensive = { 387184 }, defensive = { 115176, 115203, 122278, 122783, 132578, 322507, 119582, 322101, 116670 }, defensiveMinor = { 119582, 322507 }, defensiveMajor = { 115176, 115203, 122278, 122783, 132578 }, cc = { 115078, 116844 }, }, ["Mistweaver"] = { mobility = { 109132, 115008, 101643 }, selfHeal = { 322101, 116670, 124682 }, offensive = { 387184 }, defensive = { 122278, 122783, 243435, 322101, 116670, 124682 }, defensiveMajor = { 122278, 122783, 243435 }, cc = { 115078, 116844 }, }, ["Windwalker"] = { mobility = { 109132, 115008, 101643 }, selfHeal = { 322101, 116670 }, offensive = { 123904, 387184 }, defensive = { 122278, 122470, 122783, 243435, 322101, 116670 }, defensiveMajor = { 122278, 122470, 122783, 243435 }, cc = { 115078, 116844 }, },
  },
  ["PALADIN"] = { ["Holy"] = { mobility = { 190784 }, selfHeal = { 85673, 19750, 633 }, offensive = { 31884, 216331, 375576, 114165 }, defensive = { 31821, 498, 184662, 210294, 633, 19750, 85673 }, defensiveMinor = { 498, 184662, 210294 }, defensiveMajor = { 31821 }, immunity = { 642, 1022 }, cc = { 853, 20066 }, }, ["Protection"] = { mobility = { 190784 }, selfHeal = { 85673, 19750, 633 }, offensive = { 31884, 389539 }, defensive = { 31850, 86659, 498, 53600, 633, 19750, 85673 }, defensiveMinor = { 498, 53600 }, defensiveMajor = { 31850, 86659 }, immunity = { 642, 1022, 204018 }, cc = { 853, 20066 }, }, ["Retribution"] = { mobility = { 190784 }, selfHeal = { 85673, 19750, 633 }, offensive = { 31884 }, defensive = { 184662, 403876, 633, 19750, 85673 }, defensiveMinor = { 184662, 403876 }, immunity = { 642, 1022 }, cc = { 853, 20066 }, },
  },
  ["PRIEST"] = { ["Discipline"] = { mobility = { 121536 }, selfHeal = { 19236, 2061, 373481 }, offensive = { 34433, 123040 }, defensive = { 62618, 17, 586, 19236, 373481, 2061 }, defensiveMinor = { 17, 586 }, defensiveMajor = { 62618 }, cc = { 8122 }, }, ["Holy"] = { mobility = { 121536 }, selfHeal = { 19236, 2061, 373481 }, offensive = { 34433, 372760, 88625 }, defensive = { 2050, 34861, 64901, 17, 586, 19236, 373481, 2061 }, defensiveMinor = { 17, 586 }, defensiveMajor = { 2050, 34861, 64901 }, cc = { 8122 }, }, ["Shadow"] = { mobility = { 121536 }, selfHeal = { 19236, 2061, 373481, 15286 }, offensive = { 34433, 200174, 391109 }, defensive = { 47585, 17, 586, 15286, 19236, 373481, 2061 }, defensiveMinor = { 17, 586 }, defensiveMajor = { 47585 }, cc = { 8122 }, },
  },
  ["ROGUE"] = { ["Assassination"] = { mobility = { 36554, 1856, 2983 }, selfHeal = { 185311 }, offensive = { 360194, 381623 }, defensive = { 5277, 212182, 1966, 185311 }, defensiveMinor = { 1966 }, defensiveMajor = { 5277, 212182 }, immunity = { 31224 }, cc = { 1776, 2094, 6770 }, }, ["Outlaw"] = { mobility = { 36554, 1856, 2983 }, selfHeal = { 185311 }, offensive = { 13750, 315508, 381623 }, defensive = { 5277, 212182, 1966, 185311 }, defensiveMinor = { 1966 }, defensiveMajor = { 5277, 212182 }, immunity = { 31224 }, cc = { 1776, 2094, 6770 }, }, ["Subtlety"] = { mobility = { 36554, 1856, 2983 }, selfHeal = { 185311 }, offensive = { 121471, 381623 }, defensive = { 5277, 212182, 1966, 185311 }, defensiveMinor = { 1966 }, defensiveMajor = { 5277, 212182 }, immunity = { 31224 }, cc = { 1776, 2094, 6770 }, },
  },
  ["SHAMAN"] = { ["Elemental"] = { mobility = { 192063 }, selfHeal = { 8004, 5394 }, offensive = { 114050 }, defensive = { 108271, 198103, 5394, 8004 }, defensiveMajor = { 108271, 198103 }, cc = { 51514 }, }, ["Enhancement"] = { mobility = { 58875, 192063 }, selfHeal = { 8004, 5394 }, offensive = { 384352, 114051 }, defensive = { 108271, 198103, 5394, 8004 }, defensiveMajor = { 108271, 198103 }, cc = { 51514 }, }, ["Restoration"] = { mobility = { 192063 }, selfHeal = { 8004, 77472, 5394 }, offensive = { 375982, 114052 }, defensive = { 98008, 108271, 198103, 5394, 8004, 77472 }, defensiveMajor = { 98008, 108271, 198103 }, cc = { 51514 }, },
  },
  ["WARLOCK"] = { ["Affliction"] = { mobility = { 48020 }, selfHeal = { 234153, 6789 }, offensive = { 205180 }, defensive = { 104773, 108416, 6262, 6789, 234153 }, defensiveMinor = { 108416 }, defensiveMajor = { 104773 }, cc = { 710, 1098, 5782 }, }, ["Demonology"] = { mobility = { 48020 }, selfHeal = { 234153, 6789 }, offensive = { 265187, 196277 }, defensive = { 104773, 108416, 6262, 6789, 234153 }, defensiveMinor = { 108416 }, defensiveMajor = { 104773 }, cc = { 710, 1098, 5782 }, }, ["Destruction"] = { mobility = { 48020 }, selfHeal = { 234153, 6789 }, offensive = { 1122 }, defensive = { 104773, 108416, 6262, 6789, 234153 }, defensiveMinor = { 108416 }, defensiveMajor = { 104773 }, cc = { 710, 1098, 5782 }, },
  },
  ["WARRIOR"] = { ["Arms"] = { mobility = { 100, 6544 }, selfHeal = { 202168, 34428 }, offensive = { 107574, 228920, 262161 }, defensive = { 871, 97462, 118038, 23920, 34428, 202168 }, defensiveMinor = { 23920 }, defensiveMajor = { 871, 97462, 118038 }, cc = { 5246, 107570, 46968 }, }, ["Fury"] = { mobility = { 100, 6544 }, selfHeal = { 202168, 34428, 184364 }, offensive = { 107574, 228920, 1719 }, defensive = { 97462, 184364, 23920, 34428, 202168 }, defensiveMinor = { 23920 }, defensiveMajor = { 97462, 184364 }, cc = { 5246, 107570, 46968 }, }, ["Protection"] = { mobility = { 100, 6544, 198304 }, selfHeal = { 202168, 34428, 190456 }, offensive = { 107574, 228920, 385952 }, defensive = { 871, 12975, 97462, 2565, 23920, 190456, 34428, 202168 }, defensiveMinor = { 2565, 23920, 190456 }, defensiveMajor = { 871, 12975, 97462 }, cc = { 5246, 107570, 46968 }, },
  },
}
