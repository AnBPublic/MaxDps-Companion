local _, addonTable = ...
local DeathKnight = addonTable.DeathKnight
local MaxDps = _G.MaxDps
if not MaxDps then return end
local LibStub = LibStub
local setSpell

local ceil = ceil
local floor = floor
local fmod = fmod
local format = format
local max = max
local min = min
local pairs = pairs
local select = select
local strsplit = strsplit
local GetTime = GetTime

local UnitAffectingCombat = UnitAffectingCombat
local UnitCastingInfo = UnitCastingInfo
local UnitChannelInfo = UnitChannelInfo
local UnitClass = UnitClass
local UnitExists = UnitExists
local UnitGUID = UnitGUID
local UnitName = UnitName
local UnitSpellHaste = UnitSpellHaste
local UnitThreatSituation = UnitThreatSituation
local UnitPower = UnitPower
local UnitHealth = UnitHealth
local UnitAura = C_UnitAuras.GetAuraDataByIndex
local UnitAuraByName = C_UnitAuras.GetAuraDataBySpellName
local UnitHealthMax = UnitHealthMax
local UnitPowerMax = UnitPowerMax
local SpellHaste
local SpellCrit
local GetSpellInfo = C_Spell.GetSpellInfo
local GetSpellCooldown = C_Spell.GetSpellCooldown
local GetSpellCastCount = C_Spell.GetSpellCastCount
local GetUnitSpeed = GetUnitSpeed
local GetCritChance = GetCritChance
local GetInventoryItemLink = GetInventoryItemLink
local GetItemInfo = C_Item.GetItemInfo
local GetItemSpell = C_Item.GetItemSpell
local GetNamePlates = C_NamePlate.GetNamePlates and C_NamePlate.GetNamePlates or GetNamePlates
local GetPowerRegenForPowerType = GetPowerRegenForPowerType
local GetSpellName = C_Spell and C_Spell.GetSpellName and C_Spell.GetSpellName or GetSpellInfo
local GetTotemInfo = GetTotemInfo
local IsStealthed = IsStealthed
local IsCurrentSpell = C_Spell and C_Spell.IsCurrentSpell
local CombatLogGetCurrentEventInfo = CombatLogGetCurrentEventInfo

local ManaPT = Enum.PowerType.Mana
local RagePT = Enum.PowerType.Rage
local FocusPT = Enum.PowerType.Focus
local EnergyPT = Enum.PowerType.Energy
local ComboPointsPT = Enum.PowerType.ComboPoints
local RunesPT = Enum.PowerType.Runes
local RunicPowerPT = Enum.PowerType.RunicPower
local SoulShardsPT = Enum.PowerType.SoulShards
local DemonicFuryPT = Enum.PowerType.DemonicFury
local BurningEmbersPT = Enum.PowerType.BurningEmbers
local LunarPowerPT = Enum.PowerType.LunarPower
local HolyPowerPT = Enum.PowerType.HolyPower
local MaelstromPT = Enum.PowerType.Maelstrom
local ChiPT = Enum.PowerType.Chi
local InsanityPT = Enum.PowerType.Insanity
local ArcaneChargesPT = Enum.PowerType.ArcaneCharges
local FuryPT = Enum.PowerType.Fury
local PainPT = Enum.PowerType.Pain
local EssencePT = Enum.PowerType.Essence
local RuneBloodPT = Enum.PowerType.RuneBlood
local RuneFrostPT = Enum.PowerType.RuneFrost
local RuneUnholyPT = Enum.PowerType.RuneUnholy

local fd
local ttd
local timeShift
local gcd
local cooldown
local buff
local debuff
local talents
local targets
local targetHP
local targetmaxHP
local targethealthPerc
local curentHP
local maxHP
local healthPerc
local timeInCombat
local className, classFilename, classId = UnitClass('player')
local classtable
local LibRangeCheck = LibStub('LibRangeCheck-3.0', true)

local Runes
local RunesMax
local RunesDeficit
local RunesPerc
local RunesRegen
local RunesRegenCombined
local RunesTimeToMax
local RunicPower
local RunicPowerMax
local RunicPowerDeficit
local RunicPowerPerc
local RunicPowerRegen
local RunicPowerRegenCombined
local RunicPowerTimeToMax
local RuneBlood
local RuneFrost
local RuneUnholy
local ibf_damage
local rt_damage
local vb_damage

local Blood = {}

local bone_shield_refresh_value = 7
local rp_deficit_threshold = 0


local function twoh_check()
   local leftwep = GetInventoryItemLink('player',16)
   local leftwepSubType = leftwep and select(13, C_Item.GetItemInfo(leftwep))
   local rightwep = GetInventoryItemLink('player',17)
   local rightwepSubType = rightwep and select(13, C_Item.GetItemInfo(rightwep))
   if leftwepSubType == (1 or 5 or 6 or 8) then
      return true
   end
end


local function wep_rune_check(type)
    local MHitemLink=GetInventoryItemLink('player',16)
    local OHitemLink=GetInventoryItemLink('player',17)
    local MHenchant
    local OHenchant
    if MHitemLink ~= nil then
        local _,_,enchant=strsplit(':',MHitemLink)
        if enchant ~= nil and enchant ~= '' then
            if enchant == '3368' then
                MHenchant = 'Rune of the Fallen Crusader'
            elseif enchant == '6243' then
                MHenchant = 'Rune of Hysteria'
            elseif enchant == '3370' then
                MHenchant = 'Rune of Razorice'
            elseif enchant == '6241' then
                MHenchant = 'Rune of Sanguination'
            elseif enchant == '6242' then
                MHenchant = 'Rune of Spellwarding'
            elseif enchant == '6245' then
                MHenchant = 'Rune of the Apocalypse'
            elseif enchant == '3847' then
                MHenchant = 'Rune of the Stoneskin Gargoyle'
            elseif enchant == '6244' then
                MHenchant = 'Rune of Unending Thirst'
            else
                MHenchant = 'Unknown Enchant - ' .. enchant
            end
        end
    end
    if OHitemLink ~= nil then
        local _,_,enchant=strsplit(':',OHitemLink)
        if enchant ~= nil and enchant ~= '' then
            if enchant == '3368' then
                OHenchant = 'Rune of the Fallen Crusader'
            elseif enchant == '6243' then
                OHenchant = 'Rune of Hysteria'
            elseif enchant == '3370' then
                OHenchant = 'Rune of Razorice'
            elseif enchant == '6241' then
                OHenchant = 'Rune of Sanguination'
            elseif enchant == '6242' then
                OHenchant = 'Rune of Spellwarding'
            elseif enchant == '6245' then
                OHenchant = 'Rune of the Apocalypse'
            elseif enchant == '3847' then
                OHenchant = 'Rune of the Stoneskin Gargoyle'
            elseif enchant == '6244' then
                OHenchant = 'Rune of Unending Thirst'
            else
                OHenchant = 'Unknown Enchant - ' .. enchant
            end
        end
    end
    if (MHenchant or OHenchant) == type then return true end
    return false
end


function Blood:precombat()
    if (MaxDps:CheckSpellUsable(classtable.DeathsCaress, 'DeathsCaress')) and cooldown[classtable.DeathsCaress].ready and not UnitAffectingCombat('player') then
        if not setSpell then setSpell = classtable.DeathsCaress end
    end
    bone_shield_refresh_value = 7
end
function Blood:deathbringer()
    if (MaxDps:CheckSpellUsable(classtable.RuneTap, 'RuneTap')) and (Runes >4) and cooldown[classtable.RuneTap].ready then
        MaxDps:GlowCooldown(classtable.RuneTap, cooldown[classtable.RuneTap].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.Bonestorm, 'Bonestorm')) and (buff[classtable.BoneShieldBuff].count >= 5 and buff[classtable.DeathandDecayBuff].up) and cooldown[classtable.Bonestorm].ready then
        if not setSpell then setSpell = classtable.Bonestorm end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathStrike, 'DeathStrike')) and (RunicPowerDeficit <rp_deficit_threshold or (buff[classtable.DancingRuneWeaponBuff].up and RunicPowerDeficit<(rp_deficit_threshold + 6))) and cooldown[classtable.DeathStrike].ready then
        if not setSpell then setSpell = classtable.DeathStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.SoulReaper, 'SoulReaper')) and (targets <= 2 and buff[classtable.ReaperofSoulsBuff].up and ttd>(debuff[classtable.SoulReaperDeBuff].remains + 5)) and cooldown[classtable.SoulReaper].ready then
        if not setSpell then setSpell = classtable.SoulReaper end
    end
    if (MaxDps:CheckSpellUsable(classtable.SoulReaper, 'SoulReaper')) and (targets <= 2 and MaxDps:GetTimeToPct(35) <5 and ttd>(debuff[classtable.SoulReaperDeBuff].remains + 5)) and cooldown[classtable.SoulReaper].ready then
        if not setSpell then setSpell = classtable.SoulReaper end
    end
    if (MaxDps:CheckSpellUsable(classtable.ReapersMark, 'ReapersMark')) and cooldown[classtable.ReapersMark].ready then
        MaxDps:GlowCooldown(classtable.ReapersMark, cooldown[classtable.ReapersMark].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.BloodBoil, 'BloodBoil')) and (buff[classtable.DancingRuneWeaponBuff].up and not (buff[classtable.BloodPlagueDeBuff].count >0)) and cooldown[classtable.BloodBoil].ready then
        if not setSpell then setSpell = classtable.BloodBoil end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathandDecay, 'DeathandDecay')) and (not buff[classtable.DeathandDecayBuff].up) and cooldown[classtable.DeathandDecay].ready then
        if not setSpell then setSpell = classtable.DeathandDecay end
    end
    if (MaxDps:CheckSpellUsable(classtable.Marrowrend, 'Marrowrend')) and (buff[classtable.ExterminateBuff].up or (buff[classtable.BoneShieldBuff].count <5 and not debuff[classtable.BonestormDeBuff].up)) and cooldown[classtable.Marrowrend].ready then
        if not setSpell then setSpell = classtable.Marrowrend end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathStrike, 'DeathStrike')) and cooldown[classtable.DeathStrike].ready then
        if not setSpell then setSpell = classtable.DeathStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.Tombstone, 'Tombstone')) and (buff[classtable.BoneShieldBuff].count >= 8 and buff[classtable.DeathandDecayBuff].up and cooldown[classtable.DancingRuneWeapon].remains >= 25) and cooldown[classtable.Tombstone].ready then
        MaxDps:GlowCooldown(classtable.Tombstone, cooldown[classtable.Tombstone].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.Blooddrinker, 'Blooddrinker')) and (not buff[classtable.DancingRuneWeaponBuff].up and targets <= 2 and buff[classtable.CoagulopathyBuff].remains >3) and cooldown[classtable.Blooddrinker].ready then
        MaxDps:GlowCooldown(classtable.Blooddrinker, cooldown[classtable.Blooddrinker].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.Consumption, 'Consumption')) and cooldown[classtable.Consumption].ready then
        MaxDps:GlowCooldown(classtable.Consumption, cooldown[classtable.Consumption].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.BloodBoil, 'BloodBoil')) and cooldown[classtable.BloodBoil].ready then
        if not setSpell then setSpell = classtable.BloodBoil end
    end
    if (MaxDps:CheckSpellUsable(classtable.HeartStrike, 'HeartStrike')) and (buff[classtable.CoagulopathyBuff].count <5) and cooldown[classtable.HeartStrike].ready then
        if not setSpell then setSpell = classtable.HeartStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.HeartStrike, 'HeartStrike')) and cooldown[classtable.HeartStrike].ready then
        if not setSpell then setSpell = classtable.HeartStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.SoulReaper, 'SoulReaper')) and (buff[classtable.ReaperofSoulsBuff].up) and cooldown[classtable.SoulReaper].ready then
        if not setSpell then setSpell = classtable.SoulReaper end
    end
end
function Blood:san_drw()
    if (MaxDps:CheckSpellUsable(classtable.HeartStrike, 'HeartStrike')) and (buff[classtable.EssenceoftheBloodQueenBuff].remains <1.5 and buff[classtable.EssenceoftheBloodQueenBuff].up) and cooldown[classtable.HeartStrike].ready then
        if not setSpell then setSpell = classtable.HeartStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.Bonestorm, 'Bonestorm')) and (buff[classtable.BoneShieldBuff].count >= 5 and buff[classtable.DeathandDecayBuff].up) and cooldown[classtable.Bonestorm].ready then
        if not setSpell then setSpell = classtable.Bonestorm end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathStrike, 'DeathStrike')) and (RunicPowerDeficit <36) and cooldown[classtable.DeathStrike].ready then
        if not setSpell then setSpell = classtable.DeathStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.BloodBoil, 'BloodBoil')) and (not (buff[classtable.BloodPlagueDeBuff].count >0)) and cooldown[classtable.BloodBoil].ready then
        if not setSpell then setSpell = classtable.BloodBoil end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathandDecay, 'DeathandDecay')) and ((targets <= 3 and buff[classtable.CrimsonScourgeBuff].up) or (targets >3 and not buff[classtable.DeathandDecayBuff].up)) and cooldown[classtable.DeathandDecay].ready then
        if not setSpell then setSpell = classtable.DeathandDecay end
    end
    if (MaxDps:CheckSpellUsable(classtable.HeartStrike, 'HeartStrike')) and cooldown[classtable.HeartStrike].ready then
        if not setSpell then setSpell = classtable.HeartStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathStrike, 'DeathStrike')) and cooldown[classtable.DeathStrike].ready then
        if not setSpell then setSpell = classtable.DeathStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.Consumption, 'Consumption')) and cooldown[classtable.Consumption].ready then
        MaxDps:GlowCooldown(classtable.Consumption, cooldown[classtable.Consumption].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.BloodBoil, 'BloodBoil')) and cooldown[classtable.BloodBoil].ready then
        if not setSpell then setSpell = classtable.BloodBoil end
    end
end
function Blood:sanlayn()
    if (MaxDps:CheckSpellUsable(classtable.BloodBoil, 'BloodBoil')) and ((not buff[classtable.BoneShieldBuff].up or buff[classtable.BoneShieldBuff].remains <1.5 or buff[classtable.BoneShieldBuff].count <= 1) and targets >= 2) and cooldown[classtable.BloodBoil].ready then
        if not setSpell then setSpell = classtable.BloodBoil end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathsCaress, 'DeathsCaress')) and (not buff[classtable.BoneShieldBuff].up or buff[classtable.BoneShieldBuff].remains <1.5 or buff[classtable.BoneShieldBuff].count <= 1) and cooldown[classtable.DeathsCaress].ready then
        if not setSpell then setSpell = classtable.DeathsCaress end
    end
    if (MaxDps:CheckSpellUsable(classtable.BloodBoil, 'BloodBoil')) and (debuff[classtable.BloodPlagueDeBuff].remains <3) and cooldown[classtable.BloodBoil].ready then
        if not setSpell then setSpell = classtable.BloodBoil end
    end
    if (MaxDps:CheckSpellUsable(classtable.HeartStrike, 'HeartStrike')) and ((buff[classtable.EssenceoftheBloodQueenBuff].remains <1.5 and buff[classtable.EssenceoftheBloodQueenBuff].up and buff[classtable.VampiricStrikeBuff].up)) and cooldown[classtable.HeartStrike].ready then
        if not setSpell then setSpell = classtable.HeartStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.Bonestorm, 'Bonestorm')) and (buff[classtable.BoneShieldBuff].count >= 5 and buff[classtable.DeathandDecayBuff].up) and cooldown[classtable.Bonestorm].ready then
        if not setSpell then setSpell = classtable.Bonestorm end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathStrike, 'DeathStrike')) and (RunicPowerDeficit <20) and cooldown[classtable.DeathStrike].ready then
        if not setSpell then setSpell = classtable.DeathStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.Marrowrend, 'Marrowrend')) and (buff[classtable.BoneShieldBuff].count <6 and not debuff[classtable.BonestormDeBuff].up) and cooldown[classtable.Marrowrend].ready then
        if not setSpell then setSpell = classtable.Marrowrend end
    end
    if (MaxDps:CheckSpellUsable(classtable.Consumption, 'Consumption')) and (buff[classtable.InflictionofSorrowBuff].up and buff[classtable.DeathandDecayBuff].up) and cooldown[classtable.Consumption].ready then
        MaxDps:GlowCooldown(classtable.Consumption, cooldown[classtable.Consumption].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.HeartStrike, 'HeartStrike')) and ((buff[classtable.InflictionofSorrowBuff].up or buff[classtable.VampiricStrikeBuff].up) and buff[classtable.DeathandDecayBuff].up) and cooldown[classtable.HeartStrike].ready then
        if not setSpell then setSpell = classtable.HeartStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.SoulReaper, 'SoulReaper')) and (targets <= 2 and MaxDps:GetTimeToPct(35) <5 and ttd>(debuff[classtable.SoulReaperDeBuff].remains + 5)) and cooldown[classtable.SoulReaper].ready then
        if not setSpell then setSpell = classtable.SoulReaper end
    end
    if (MaxDps:CheckSpellUsable(classtable.BloodBoil, 'BloodBoil')) and (buff[classtable.BoneShieldBuff].count <6 and not debuff[classtable.BonestormDeBuff].up and targets >= 2) and cooldown[classtable.BloodBoil].ready then
        if not setSpell then setSpell = classtable.BloodBoil end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathsCaress, 'DeathsCaress')) and (buff[classtable.BoneShieldBuff].count <6 and not debuff[classtable.BonestormDeBuff].up) and cooldown[classtable.DeathsCaress].ready then
        if not setSpell then setSpell = classtable.DeathsCaress end
    end
    if (MaxDps:CheckSpellUsable(classtable.Tombstone, 'Tombstone')) and (buff[classtable.BoneShieldBuff].count >= 6 and buff[classtable.DeathandDecayBuff].up and cooldown[classtable.DancingRuneWeapon].remains >= 25) and cooldown[classtable.Tombstone].ready then
        MaxDps:GlowCooldown(classtable.Tombstone, cooldown[classtable.Tombstone].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathandDecay, 'DeathandDecay')) and ((targets <= 3 and buff[classtable.CrimsonScourgeBuff].up) or (targets >3 and not buff[classtable.DeathandDecayBuff].up)) and cooldown[classtable.DeathandDecay].ready then
        if not setSpell then setSpell = classtable.DeathandDecay end
    end
    if (MaxDps:CheckSpellUsable(classtable.Blooddrinker, 'Blooddrinker')) and (targets <= 2 and buff[classtable.CoagulopathyBuff].remains >3) and cooldown[classtable.Blooddrinker].ready then
        MaxDps:GlowCooldown(classtable.Blooddrinker, cooldown[classtable.Blooddrinker].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.HeartStrike, 'HeartStrike')) and (buff[classtable.VampiricStrikeBuff].up) and cooldown[classtable.HeartStrike].ready then
        if not setSpell then setSpell = classtable.HeartStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathStrike, 'DeathStrike')) and cooldown[classtable.DeathStrike].ready then
        if not setSpell then setSpell = classtable.DeathStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.HeartStrike, 'HeartStrike')) and (Runes >= 2) and cooldown[classtable.HeartStrike].ready then
        if not setSpell then setSpell = classtable.HeartStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.Consumption, 'Consumption')) and cooldown[classtable.Consumption].ready then
        MaxDps:GlowCooldown(classtable.Consumption, cooldown[classtable.Consumption].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.BloodBoil, 'BloodBoil')) and cooldown[classtable.BloodBoil].ready then
        if not setSpell then setSpell = classtable.BloodBoil end
    end
    if (MaxDps:CheckSpellUsable(classtable.HeartStrike, 'HeartStrike')) and cooldown[classtable.HeartStrike].ready then
        if not setSpell then setSpell = classtable.HeartStrike end
    end
end


local function ClearCDs()
    MaxDps:GlowCooldown(classtable.MindFreeze, false)
    MaxDps:GlowCooldown(classtable.tome_of_lights_devotion, false)
    MaxDps:GlowCooldown(classtable.unyielding_netherprism, false)
    MaxDps:GlowCooldown(classtable.bestinslots, false)
    MaxDps:GlowCooldown(classtable.VampiricBlood, false)
    MaxDps:GlowCooldown(classtable.RaiseDead, false)
    MaxDps:GlowCooldown(classtable.BloodTap, false)
    MaxDps:GlowCooldown(classtable.DancingRuneWeapon, false)
    MaxDps:GlowCooldown(classtable.RuneTap, false)
    MaxDps:GlowCooldown(classtable.ReapersMark, false)
    MaxDps:GlowCooldown(classtable.Tombstone, false)
    MaxDps:GlowCooldown(classtable.Blooddrinker, false)
    MaxDps:GlowCooldown(classtable.Consumption, false)
end

function Blood:callaction()
    if (MaxDps:CheckSpellUsable(classtable.MindFreeze, 'MindFreeze')) and cooldown[classtable.MindFreeze].ready then
        MaxDps:GlowCooldown(classtable.MindFreeze, ( select(8,UnitCastingInfo('target')) ~= nil and not select(8,UnitCastingInfo('target')) or select(7,UnitChannelInfo('target')) ~= nil and not select(7,UnitChannelInfo('target'))) )
    end
    if (MaxDps:CheckSpellUsable(classtable.tome_of_lights_devotion, 'tome_of_lights_devotion')) and (buff[classtable.InnerResilienceBuff].up) and cooldown[classtable.tome_of_lights_devotion].ready then
        MaxDps:GlowCooldown(classtable.tome_of_lights_devotion, cooldown[classtable.tome_of_lights_devotion].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.unyielding_netherprism, 'unyielding_netherprism')) and (cooldown[classtable.DancingRuneWeapon].remains <1 or MaxDps:boss() and ttd <= 20) and cooldown[classtable.unyielding_netherprism].ready then
        MaxDps:GlowCooldown(classtable.unyielding_netherprism, cooldown[classtable.unyielding_netherprism].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.bestinslots, 'bestinslots')) and cooldown[classtable.bestinslots].ready then
        MaxDps:GlowCooldown(classtable.bestinslots, cooldown[classtable.bestinslots].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.VampiricBlood, 'VampiricBlood')) and ((UnitThreatSituation('player') == 2 or UnitThreatSituation('player') == 3) and MaxDps.incoming_damage_5 >= vb_damage and not (buff[classtable.DancingRuneWeaponBuff].up or buff[classtable.IceboundFortitudeBuff].up)) and cooldown[classtable.VampiricBlood].ready then
        MaxDps:GlowCooldown(classtable.VampiricBlood, cooldown[classtable.VampiricBlood].ready)
    end
    rp_deficit_threshold = 15+(10 * (talents[classtable.RelishInBlood] and talents[classtable.RelishInBlood] or 0))+(3 * (talents[classtable.RunicAttenuation] and talents[classtable.RunicAttenuation] or 0))+(targets * (talents[classtable.Heartbreaker] and talents[classtable.Heartbreaker] or 0)*2)
    if (MaxDps:CheckSpellUsable(classtable.RaiseDead, 'RaiseDead')) and (not UnitExists('pet')) and cooldown[classtable.RaiseDead].ready then
        MaxDps:GlowCooldown(classtable.RaiseDead, cooldown[classtable.RaiseDead].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.BloodTap, 'BloodTap')) and ((Runes <= 2 and DeathKnight:TimeToRunes(3) >gcd and cooldown[classtable.BloodTap].charges >= 1.8)) and cooldown[classtable.BloodTap].ready then
        MaxDps:GlowCooldown(classtable.BloodTap, cooldown[classtable.BloodTap].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.BloodTap, 'BloodTap')) and ((Runes <= 1 and DeathKnight:TimeToRunes(3) >gcd)) and cooldown[classtable.BloodTap].ready then
        MaxDps:GlowCooldown(classtable.BloodTap, cooldown[classtable.BloodTap].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathsCaress, 'DeathsCaress')) and (buff[classtable.BoneShieldBuff].remains <gcd*2) and cooldown[classtable.DeathsCaress].ready then
        if not setSpell then setSpell = classtable.DeathsCaress end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathStrike, 'DeathStrike')) and (buff[classtable.CoagulopathyBuff].up and buff[classtable.CoagulopathyBuff].remains <= gcd) and cooldown[classtable.DeathStrike].ready then
        if not setSpell then setSpell = classtable.DeathStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathandDecay, 'DeathandDecay')) and (not buff[classtable.DeathandDecayBuff].up) and cooldown[classtable.DeathandDecay].ready then
        if not setSpell then setSpell = classtable.DeathandDecay end
    end
    if (MaxDps:CheckSpellUsable(classtable.BloodBoil, 'BloodBoil')) and (debuff[classtable.BloodPlagueDeBuff].remains <gcd*2) and cooldown[classtable.BloodBoil].ready then
        if not setSpell then setSpell = classtable.BloodBoil end
    end
    if (MaxDps:CheckSpellUsable(classtable.DancingRuneWeapon, 'DancingRuneWeapon')) and cooldown[classtable.DancingRuneWeapon].ready then
        MaxDps:GlowCooldown(classtable.DancingRuneWeapon, cooldown[classtable.DancingRuneWeapon].ready)
    end
    if ((MaxDps.ActiveHeroTree == 'deathbringer')) then
        Blood:deathbringer()
    end
    if ((MaxDps.ActiveHeroTree == 'sanlayn') and buff[classtable.DancingRuneWeaponBuff].up) then
        Blood:san_drw()
    end
    if ((MaxDps.ActiveHeroTree == 'sanlayn')) then
        Blood:sanlayn()
    end
end
function DeathKnight:Blood()
    fd = MaxDps.FrameData
    ttd = (fd.timeToDie and fd.timeToDie) or 500
    timeShift = fd.timeShift
    gcd = fd.gcd
    cooldown = fd.cooldown
    buff = fd.buff
    debuff = fd.debuff
    talents = fd.talents
    targets = MaxDps:SmartAoe()
    targetHP = UnitHealth('target')
    targetmaxHP = UnitHealthMax('target')
    targethealthPerc = (targetHP >0 and targetmaxHP >0 and (targetHP / targetmaxHP) * 100) or 100
    curentHP = UnitHealth('player')
    maxHP = UnitHealthMax('player')
    healthPerc = (curentHP / maxHP) * 100
    timeInCombat = MaxDps.combatTime or 0
    classtable = MaxDps.SpellTable
    local trinket1ID = GetInventoryItemID('player', 13)
    local trinket2ID = GetInventoryItemID('player', 14)
    local MHID = GetInventoryItemID('player', 16)
    classtable.trinket1 = (trinket1ID and select(2,GetItemSpell(trinket1ID)) ) or 0
    classtable.trinket2 = (trinket2ID and select(2,GetItemSpell(trinket2ID)) ) or 0
    classtable.main_hand = (MHID and select(2,GetItemSpell(MHID)) ) or 0
    Runes = UnitPower('player', RunesPT)
    RunesMax = UnitPowerMax('player', RunesPT)
    RunesDeficit = RunesMax - Runes
    RunesPerc = (Runes / RunesMax) * 100
    RunesRegen = GetPowerRegenForPowerType(RunesPT)
    RunesTimeToMax = RunesDeficit / RunesRegen
    RunicPower = UnitPower('player', RunicPowerPT)
    RunicPowerMax = UnitPowerMax('player', RunicPowerPT)
    RunicPowerDeficit = RunicPowerMax - RunicPower
    RunicPowerPerc = (RunicPower / RunicPowerMax) * 100
    RunicPowerRegen = GetPowerRegenForPowerType(RunicPowerPT)
    RunicPowerTimeToMax = RunicPowerDeficit / RunicPowerRegen
    SpellHaste = UnitSpellHaste('player')
    SpellCrit = GetCritChance()
    ibf_damage = maxHP * ( 40 ) * 0.01
    rt_damage = maxHP * ( 30 ) * 0.01
    vb_damage = maxHP * ( 50 ) * 0.01
    --for spellId in pairs(MaxDps.Flags) do
    --    self.Flags[spellId] = false
    --    self:ClearGlowIndependent(spellId, spellId)
    --end
    classtable.InnerResilienceBuff = 0
    classtable.DancingRuneWeaponBuff = 81256
    classtable.IceboundFortitudeBuff = 48792
    classtable.BoneShieldBuff = 195181
    classtable.CoagulopathyBuff = 391481
    classtable.DeathandDecayBuff = 188290
    classtable.ReaperofSoulsBuff = 469172
    classtable.ExterminateBuff = 441416
    classtable.EssenceoftheBloodQueenBuff = 433925
    classtable.CrimsonScourgeBuff = 81141
    classtable.VampiricStrikeBuff = 433899
    classtable.InflictionofSorrowBuff = 460049
    classtable.BloodPlagueDeBuff = 55078
    classtable.SoulReaperDeBuff = 343294
    classtable.BonestormDeBuff = 194844
    classtable.BloodBoilDeBuff = 0

    local function debugg()
    end


    --if MaxDps.db.global.debugMode then
    --   debugg()
    --end

    setSpell = nil
    ClearCDs()

    Blood:precombat()

    Blood:callaction()
    if setSpell then return setSpell end
end
