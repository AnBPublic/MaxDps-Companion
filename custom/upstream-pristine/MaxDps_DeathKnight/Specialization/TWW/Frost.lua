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

local Frost = {}

local trinket_1_sync = false
local trinket_2_sync = false
local trinket_1_buffs = false
local trinket_2_buffs = false
local trinket_1_duration = 0
local trinket_2_duration = 0
local trinket_priority = 2
local damage_trinket_priority = 2
local trinket_1_manual = false
local trinket_2_manual = false
local cooldown_check = false
local fwf_buffs = false
local rune_pooling = false
local rp_pooling = false
local frostscythe_prio = 4
local breath_of_sindragosa_check = false


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


function Frost:precombat()
    if MaxDps:HasOnUseEffect('13') and (talents[classtable.PillarofFrost] and not talents[classtable.BreathofSindragosa] and (math.fmod(MaxDps:CheckTrinketCooldownDuration('13') , cooldown[classtable.PillarofFrost].duration) == 0) or talents[classtable.BreathofSindragosa] and (math.fmod(cooldown[classtable.BreathofSindragosa].duration , MaxDps:CheckTrinketCooldownDuration('13')) == 0)) then
        trinket_1_sync = 1
    else
        trinket_1_sync = 0.5
    end
    if MaxDps:HasOnUseEffect('14') and (talents[classtable.PillarofFrost] and not talents[classtable.BreathofSindragosa] and (math.fmod(MaxDps:CheckTrinketCooldownDuration('14') , cooldown[classtable.PillarofFrost].duration) == 0) or talents[classtable.BreathofSindragosa] and (math.fmod(cooldown[classtable.BreathofSindragosa].duration , MaxDps:CheckTrinketCooldownDuration('14')) == 0)) then
        trinket_2_sync = 1
    else
        trinket_2_sync = 0.5
    end
    trinket_1_buffs = MaxDps:HasOnUseEffect('13') and not MaxDps:CheckTrinketNames('ImprovisedSeaforiumPacemaker') and (MaxDps:HasOnUseEffect('13') or MaxDps:HasBuffEffect('13', 'strength') or MaxDps:HasBuffEffect('13', 'mastery') or MaxDps:HasBuffEffect('13', 'versatility') or MaxDps:HasBuffEffect('13', 'haste') or MaxDps:HasBuffEffect('13', 'crit'))
    trinket_2_buffs = MaxDps:HasOnUseEffect('14') and not MaxDps:CheckTrinketNames('ImprovisedSeaforiumPacemaker') and (MaxDps:HasOnUseEffect('14') or MaxDps:HasBuffEffect('14', 'strength') or MaxDps:HasBuffEffect('14', 'mastery') or MaxDps:HasBuffEffect('14', 'versatility') or MaxDps:HasBuffEffect('14', 'haste') or MaxDps:HasBuffEffect('14', 'crit'))
    trinket_1_duration = MaxDps:CheckTrinketBuffDuration('13', 'any')
    trinket_2_duration = MaxDps:CheckTrinketBuffDuration('14', 'any')
    if not trinket_1_buffs and trinket_2_buffs and (MaxDps:HasOnUseEffect('14') or not MaxDps:HasOnUseEffect('13')) or trinket_2_buffs and ((MaxDps:CheckTrinketCooldownDuration('14')%trinket_2_duration)*(1.5 + (MaxDps:HasBuffEffect('14', 'strength') and 1 or 0))*(trinket_2_sync)*(1+((MaxDps:CheckTrinketItemLevel('14') - MaxDps:CheckTrinketItemLevel('13'))%100)))>((MaxDps:CheckTrinketCooldownDuration('13')%trinket_1_duration)*(1.5 + (MaxDps:HasBuffEffect('13', 'strength') and 1 or 0))*(trinket_1_sync)*(1+((MaxDps:CheckTrinketItemLevel('13') - MaxDps:CheckTrinketItemLevel('14'))%100))) then
        trinket_priority = 2
    else
        trinket_priority = 1
    end
    if not trinket_1_buffs and not trinket_2_buffs and MaxDps:CheckTrinketItemLevel('14') >= MaxDps:CheckTrinketItemLevel('13') then
        damage_trinket_priority = 2
    else
        damage_trinket_priority = 1
    end
    trinket_1_manual = MaxDps:CheckTrinketNames('UnyieldingNetherprism')
    trinket_2_manual = MaxDps:CheckTrinketNames('UnyieldingNetherprism')
end
function Frost:aoe()
    if (MaxDps:CheckSpellUsable(classtable.Frostscythe, 'Frostscythe')) and ((buff[classtable.KillingMachineBuff].count == 2 or (buff[classtable.KillingMachineBuff].up and Runes >= 3)) and targets >= frostscythe_prio) and cooldown[classtable.Frostscythe].ready then
        if not setSpell then setSpell = classtable.Frostscythe end
    end
    if (MaxDps:CheckSpellUsable(classtable.Obliterate, 'Obliterate')) and (buff[classtable.KillingMachineBuff].count == 2 or (buff[classtable.KillingMachineBuff].up and Runes >= 3)) and cooldown[classtable.Obliterate].ready then
        if not setSpell then setSpell = classtable.Obliterate end
    end
    if (MaxDps:CheckSpellUsable(classtable.HowlingBlast, 'HowlingBlast')) and (buff[classtable.RimeBuff].up and talents[classtable.FrostboundWill] or not debuff[classtable.FrostFeverDeBuff].up) and cooldown[classtable.HowlingBlast].ready then
        if not setSpell then setSpell = classtable.HowlingBlast end
    end
    if (MaxDps:CheckSpellUsable(classtable.FrostStrike, 'FrostStrike')) and (debuff[classtable.RazoriceDeBuff].count == 5 and buff[classtable.FrostbaneBuff].up) and cooldown[classtable.FrostStrike].ready then
        if not setSpell then setSpell = classtable.FrostStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.FrostStrike, 'FrostStrike')) and (debuff[classtable.RazoriceDeBuff].count == 5 and talents[classtable.ShatteringBlade] and targets <5 and not rp_pooling and not talents[classtable.Frostbane]) and cooldown[classtable.FrostStrike].ready then
        if not setSpell then setSpell = classtable.FrostStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.Frostscythe, 'Frostscythe')) and (buff[classtable.KillingMachineBuff].up and not rune_pooling and targets >= frostscythe_prio) and cooldown[classtable.Frostscythe].ready then
        if not setSpell then setSpell = classtable.Frostscythe end
    end
    if (MaxDps:CheckSpellUsable(classtable.Obliterate, 'Obliterate')) and (buff[classtable.KillingMachineBuff].up and not rune_pooling) and cooldown[classtable.Obliterate].ready then
        if not setSpell then setSpell = classtable.Obliterate end
    end
    if (MaxDps:CheckSpellUsable(classtable.HowlingBlast, 'HowlingBlast')) and (buff[classtable.RimeBuff].up) and cooldown[classtable.HowlingBlast].ready then
        if not setSpell then setSpell = classtable.HowlingBlast end
    end
    if (MaxDps:CheckSpellUsable(classtable.GlacialAdvance, 'GlacialAdvance')) and (not rp_pooling) and cooldown[classtable.GlacialAdvance].ready then
        if not setSpell then setSpell = classtable.GlacialAdvance end
    end
    if (MaxDps:CheckSpellUsable(classtable.Frostscythe, 'Frostscythe')) and (not rune_pooling and not (talents[classtable.Obliteration] and buff[classtable.PillarofFrostBuff].up) and targets >= frostscythe_prio) and cooldown[classtable.Frostscythe].ready then
        if not setSpell then setSpell = classtable.Frostscythe end
    end
    if (MaxDps:CheckSpellUsable(classtable.Obliterate, 'Obliterate')) and (not rune_pooling and not (talents[classtable.Obliteration] and buff[classtable.PillarofFrostBuff].up)) and cooldown[classtable.Obliterate].ready then
        if not setSpell then setSpell = classtable.Obliterate end
    end
    if (MaxDps:CheckSpellUsable(classtable.HowlingBlast, 'HowlingBlast')) and (not buff[classtable.KillingMachineBuff].up and (talents[classtable.Obliteration] and buff[classtable.PillarofFrostBuff].up)) and cooldown[classtable.HowlingBlast].ready then
        if not setSpell then setSpell = classtable.HowlingBlast end
    end
end
function Frost:single_target()
    if (MaxDps:CheckSpellUsable(classtable.Obliterate, 'Obliterate')) and (buff[classtable.KillingMachineBuff].count == 2 or (buff[classtable.KillingMachineBuff].up and Runes >= 3)) and cooldown[classtable.Obliterate].ready then
        if not setSpell then setSpell = classtable.Obliterate end
    end
    if (MaxDps:CheckSpellUsable(classtable.HowlingBlast, 'HowlingBlast')) and (buff[classtable.RimeBuff].up and talents[classtable.FrostboundWill]) and cooldown[classtable.HowlingBlast].ready then
        if not setSpell then setSpell = classtable.HowlingBlast end
    end
    if (MaxDps:CheckSpellUsable(classtable.FrostStrike, 'FrostStrike')) and (debuff[classtable.RazoriceDeBuff].count == 5 and talents[classtable.ShatteringBlade] and not rp_pooling) and cooldown[classtable.FrostStrike].ready then
        if not setSpell then setSpell = classtable.FrostStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.HowlingBlast, 'HowlingBlast')) and (buff[classtable.RimeBuff].up) and cooldown[classtable.HowlingBlast].ready then
        if not setSpell then setSpell = classtable.HowlingBlast end
    end
    if (MaxDps:CheckSpellUsable(classtable.FrostStrike, 'FrostStrike')) and (not talents[classtable.ShatteringBlade] and not rp_pooling and RunicPowerDeficit <30) and cooldown[classtable.FrostStrike].ready then
        if not setSpell then setSpell = classtable.FrostStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.Obliterate, 'Obliterate')) and (buff[classtable.KillingMachineBuff].up and not rune_pooling) and cooldown[classtable.Obliterate].ready then
        if not setSpell then setSpell = classtable.Obliterate end
    end
    if (MaxDps:CheckSpellUsable(classtable.FrostStrike, 'FrostStrike')) and (not rp_pooling) and cooldown[classtable.FrostStrike].ready then
        if not setSpell then setSpell = classtable.FrostStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.Obliterate, 'Obliterate')) and (not rune_pooling and not (talents[classtable.Obliteration] and buff[classtable.PillarofFrostBuff].up)) and cooldown[classtable.Obliterate].ready then
        if not setSpell then setSpell = classtable.Obliterate end
    end
    if (MaxDps:CheckSpellUsable(classtable.HowlingBlast, 'HowlingBlast')) and (not buff[classtable.KillingMachineBuff].up and (talents[classtable.Obliteration] and buff[classtable.PillarofFrostBuff].up)) and cooldown[classtable.HowlingBlast].ready then
        if not setSpell then setSpell = classtable.HowlingBlast end
    end
end


local function ClearCDs()
    MaxDps:GlowCooldown(classtable.MindFreeze, false)
    MaxDps:GlowCooldown(classtable.unyielding_netherprism, false)
    MaxDps:GlowCooldown(classtable.trinket1, false)
    MaxDps:GlowCooldown(classtable.trinket2, false)
    MaxDps:GlowCooldown(classtable.FrostwyrmsFury, false)
    MaxDps:GlowCooldown(classtable.PillarofFrost, false)
    MaxDps:GlowCooldown(classtable.BreathofSindragosa, false)
    MaxDps:GlowCooldown(classtable.ReapersMark, false)
    MaxDps:GlowCooldown(classtable.RaiseDead, false)
    MaxDps:GlowCooldown(classtable.EmpowerRuneWeapon, false)
end

function Frost:callaction()
    if (MaxDps:CheckSpellUsable(classtable.MindFreeze, 'MindFreeze')) and cooldown[classtable.MindFreeze].ready then
        MaxDps:GlowCooldown(classtable.MindFreeze, ( select(8,UnitCastingInfo('target')) ~= nil and not select(8,UnitCastingInfo('target')) or select(7,UnitChannelInfo('target')) ~= nil and not select(7,UnitChannelInfo('target'))) )
    end
    cooldown_check = (talents[classtable.PillarofFrost] and buff[classtable.PillarofFrostBuff].up) or not talents[classtable.PillarofFrost] or ttd <20
    fwf_buffs = (buff[classtable.PillarofFrostBuff].remains <gcd or (buff[classtable.UnholyStrengthBuff].up and buff[classtable.UnholyStrengthBuff].remains <gcd) or ((talents[classtable.Bonegrinder] and talents[classtable.Bonegrinder] or 0) == 2 and buff[classtable.BonegrinderFrostBuff].up and buff[classtable.BonegrinderFrostBuff].remains <gcd)) and (targets >1 or debuff[classtable.RazoriceDeBuff].count == 5 or talents[classtable.ShatteringBlade])
    rune_pooling = (MaxDps.ActiveHeroTree == 'deathbringer') and cooldown[classtable.ReapersMark].remains <6 and Runes <3
    rp_pooling = talents[classtable.BreathofSindragosa] and cooldown[classtable.BreathofSindragosa].remains <4*gcd and RunicPower <60+(35 + 5*buff[classtable.IcyOnslaughtBuff].upMath)-(10 * Runes)
    if (MaxDps and MaxDps.ActiveHeroTree == 'rideroftheapocalypse' and MaxDps.tier and MaxDps.tier[34].count >= 4 and 1 or 0) >0 and not (talents[classtable.CleavingStrikes] and buff[classtable.RemorselessWinterBuff].up) then
        frostscythe_prio = 4
    else
        frostscythe_prio = 3
    end
    breath_of_sindragosa_check = talents[classtable.BreathofSindragosa] and (cooldown[classtable.BreathofSindragosa].remains >20 or (cooldown[classtable.BreathofSindragosa].ready and RunicPower>=(60 - 20*(MaxDps.ActiveHeroTree == 'deathbringer' and 1 or 0))))
    if (MaxDps:CheckSpellUsable(classtable.trinket1, 'trinket1')) and (not (MaxDps:CheckTrinketCastTime('13') >0) and trinket_1_buffs and not trinket_1_manual and buff[classtable.PillarofFrostBuff].up and (not MaxDps:HasOnUseEffect('14') or MaxDps:CheckTrinketCooldown('14') or trinket_priority == 1)) and cooldown[classtable.trinket1].ready then
        MaxDps:GlowCooldown(classtable.trinket1, cooldown[classtable.trinket1].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.trinket2, 'trinket2')) and (not (MaxDps:CheckTrinketCastTime('14') >0) and trinket_2_buffs and not trinket_2_manual and buff[classtable.PillarofFrostBuff].up and (not MaxDps:HasOnUseEffect('13') or MaxDps:CheckTrinketCooldown('13') or trinket_priority == 2)) and cooldown[classtable.trinket2].ready then
        MaxDps:GlowCooldown(classtable.trinket2, cooldown[classtable.trinket2].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.trinket1, 'trinket1')) and (MaxDps:CheckTrinketCastTime('13') >0 and (not (MaxDps.ActiveHeroTree == 'rideroftheapocalypse') or not cooldown[classtable.FrostwyrmsFury].ready) and trinket_1_buffs and not trinket_1_manual and cooldown[classtable.PillarofFrost].remains <MaxDps:CheckTrinketCastTime('13') and (not talents[classtable.BreathofSindragosa] or breath_of_sindragosa_check) and (not MaxDps:HasOnUseEffect('14') or MaxDps:CheckTrinketCooldown('14') or trinket_priority == 1) or trinket_1_duration >= ttd) and cooldown[classtable.trinket1].ready then
        MaxDps:GlowCooldown(classtable.trinket1, cooldown[classtable.trinket1].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.trinket2, 'trinket2')) and (MaxDps:CheckTrinketCastTime('14') >0 and (not (MaxDps.ActiveHeroTree == 'rideroftheapocalypse') or not cooldown[classtable.FrostwyrmsFury].ready) and trinket_2_buffs and not trinket_2_manual and cooldown[classtable.PillarofFrost].remains <MaxDps:CheckTrinketCastTime('14') and (not talents[classtable.BreathofSindragosa] or breath_of_sindragosa_check) and (not MaxDps:HasOnUseEffect('13') or MaxDps:CheckTrinketCooldown('13') or trinket_priority == 2) or trinket_2_duration >= ttd) and cooldown[classtable.trinket2].ready then
        MaxDps:GlowCooldown(classtable.trinket2, cooldown[classtable.trinket2].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.trinket1, 'trinket1')) and (not trinket_1_buffs and not trinket_1_manual and (damage_trinket_priority == 1 or (not MaxDps:HasOnUseEffect('14') or MaxDps:CheckTrinketCooldown('14'))) and ((MaxDps:CheckTrinketCastTime('13') >0 and (not talents[classtable.BreathofSindragosa] or not buff[classtable.BreathofSindragosaBuff].up) and not buff[classtable.PillarofFrostBuff].up or not (MaxDps:CheckTrinketCastTime('13') >0)) and (not trinket_2_buffs or cooldown[classtable.PillarofFrost].remains >20) or not talents[classtable.PillarofFrost]) or ttd <15) and cooldown[classtable.trinket1].ready then
        MaxDps:GlowCooldown(classtable.trinket1, cooldown[classtable.trinket1].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.trinket2, 'trinket2')) and (not trinket_2_buffs and not trinket_2_manual and (damage_trinket_priority == 2 or (not MaxDps:HasOnUseEffect('13') or MaxDps:CheckTrinketCooldown('13'))) and ((MaxDps:CheckTrinketCastTime('14') >0 and (not talents[classtable.BreathofSindragosa] or not buff[classtable.BreathofSindragosaBuff].up) and not buff[classtable.PillarofFrostBuff].up or not (MaxDps:CheckTrinketCastTime('14') >0)) and (not trinket_1_buffs or cooldown[classtable.PillarofFrost].remains >20) or not talents[classtable.PillarofFrost]) or ttd <15) and cooldown[classtable.trinket2].ready then
        MaxDps:GlowCooldown(classtable.trinket2, cooldown[classtable.trinket2].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.main_hand, 'main_hand')) and (buff[classtable.PillarofFrostBuff].up or (buff[classtable.BreathofSindragosaBuff].up and not cooldown[classtable.PillarofFrost].ready) or (trinket_1_buffs and trinket_2_buffs and (MaxDps:CheckTrinketCooldown('13') <cooldown[classtable.PillarofFrost].remains or MaxDps:CheckTrinketCooldown('14') <cooldown[classtable.PillarofFrost].remains) and cooldown[classtable.PillarofFrost].remains >20) or ttd <15) and cooldown[classtable.main_hand].ready then
        if not setSpell then setSpell = classtable.main_hand end
    end
    if (MaxDps:CheckSpellUsable(classtable.RemorselessWinter, 'RemorselessWinter')) and (not talents[classtable.FrozenDominion] and (targets >1 or talents[classtable.GatheringStorm]) or (buff[classtable.GatheringStormBuff].count == 10 and buff[classtable.RemorselessWinterBuff].remains <gcd) and ttd >10) and cooldown[classtable.RemorselessWinter].ready then
        if not setSpell then setSpell = classtable.RemorselessWinter end
    end
    if (MaxDps:CheckSpellUsable(classtable.FrostwyrmsFury, 'FrostwyrmsFury')) and ((MaxDps.ActiveHeroTree == 'rideroftheapocalypse') and talents[classtable.ApocalypseNow] and (cooldown[classtable.PillarofFrost].remains <gcd or ttd <20) and not talents[classtable.BreathofSindragosa]) and cooldown[classtable.FrostwyrmsFury].ready then
        MaxDps:GlowCooldown(classtable.FrostwyrmsFury, cooldown[classtable.FrostwyrmsFury].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.FrostwyrmsFury, 'FrostwyrmsFury')) and ((MaxDps.ActiveHeroTree == 'rideroftheapocalypse') and talents[classtable.ApocalypseNow] and (cooldown[classtable.PillarofFrost].remains <gcd or ttd <20) and talents[classtable.BreathofSindragosa] and RunicPower >= 60) and cooldown[classtable.FrostwyrmsFury].ready then
        MaxDps:GlowCooldown(classtable.FrostwyrmsFury, cooldown[classtable.FrostwyrmsFury].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.PillarofFrost, 'PillarofFrost') and talents[classtable.PillarofFrost]) and (not talents[classtable.BreathofSindragosa] and (not (MaxDps.ActiveHeroTree == 'deathbringer') or Runes >= 2) or ttd <20) and cooldown[classtable.PillarofFrost].ready then
        MaxDps:GlowCooldown(classtable.PillarofFrost, cooldown[classtable.PillarofFrost].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.PillarofFrost, 'PillarofFrost') and talents[classtable.PillarofFrost]) and (talents[classtable.BreathofSindragosa] and breath_of_sindragosa_check and (not (MaxDps.ActiveHeroTree == 'deathbringer') or Runes >= 2)) and cooldown[classtable.PillarofFrost].ready then
        MaxDps:GlowCooldown(classtable.PillarofFrost, cooldown[classtable.PillarofFrost].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.BreathofSindragosa, 'BreathofSindragosa') and talents[classtable.BreathofSindragosa]) and (not buff[classtable.BreathofSindragosaBuff].up and (buff[classtable.PillarofFrostBuff].up or ttd <20)) and cooldown[classtable.BreathofSindragosa].ready then
        MaxDps:GlowCooldown(classtable.BreathofSindragosa, cooldown[classtable.BreathofSindragosa].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.ReapersMark, 'ReapersMark')) and (buff[classtable.PillarofFrostBuff].up or cooldown[classtable.PillarofFrost].remains >5 or ttd <20) and cooldown[classtable.ReapersMark].ready then
        MaxDps:GlowCooldown(classtable.ReapersMark, cooldown[classtable.ReapersMark].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.FrostwyrmsFury, 'FrostwyrmsFury')) and (not talents[classtable.ApocalypseNow] and targets == 1 and (talents[classtable.PillarofFrost] and buff[classtable.PillarofFrostBuff].up and not talents[classtable.Obliteration] or not talents[classtable.PillarofFrost]) and (not (targets >1) or math.huge >cooldown[classtable.FrostwyrmsFury].duration+(targets>1 and MaxDps:MaxAddDuration() or 0)) and fwf_buffs or ttd <3) and cooldown[classtable.FrostwyrmsFury].ready then
        MaxDps:GlowCooldown(classtable.FrostwyrmsFury, cooldown[classtable.FrostwyrmsFury].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.FrostwyrmsFury, 'FrostwyrmsFury')) and (not talents[classtable.ApocalypseNow] and targets >= 2 and (talents[classtable.PillarofFrost] and buff[classtable.PillarofFrostBuff].up or (targets >1) and (targets >1) and math.huge <cooldown[classtable.PillarofFrost].remains-math.huge - (targets>1 and MaxDps:MaxAddDuration() or 0)) and fwf_buffs) and cooldown[classtable.FrostwyrmsFury].ready then
        MaxDps:GlowCooldown(classtable.FrostwyrmsFury, cooldown[classtable.FrostwyrmsFury].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.FrostwyrmsFury, 'FrostwyrmsFury')) and (not talents[classtable.ApocalypseNow] and talents[classtable.Obliteration] and (talents[classtable.PillarofFrost] and buff[classtable.PillarofFrostBuff].up and not twoh_check() or not buff[classtable.PillarofFrostBuff].up and twoh_check() and not cooldown[classtable.PillarofFrost].ready or not talents[classtable.PillarofFrost]) and fwf_buffs and (not (targets >1) or math.huge >cooldown[classtable.FrostwyrmsFury].duration+(targets>1 and MaxDps:MaxAddDuration() or 0))) and cooldown[classtable.FrostwyrmsFury].ready then
        MaxDps:GlowCooldown(classtable.FrostwyrmsFury, cooldown[classtable.FrostwyrmsFury].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.RaiseDead, 'RaiseDead')) and (not UnitExists('pet')) and cooldown[classtable.RaiseDead].ready then
        MaxDps:GlowCooldown(classtable.RaiseDead, cooldown[classtable.RaiseDead].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.SoulReaper, 'SoulReaper')) and (talents[classtable.ReaperofSouls] and buff[classtable.ReaperofSoulsBuff].up and buff[classtable.KillingMachineBuff].count <2) and cooldown[classtable.SoulReaper].ready then
        if not setSpell then setSpell = classtable.SoulReaper end
    end
    if (MaxDps:CheckSpellUsable(classtable.EmpowerRuneWeapon, 'EmpowerRuneWeapon')) and ((Runes <2 or not buff[classtable.KillingMachineBuff].up) and RunicPower <35+((talents[classtable.IcyOnslaught] and talents[classtable.IcyOnslaught] or 0) * buff[classtable.IcyOnslaughtBuff].count*5) and MaxDps:CooldownConsolidated(61304).remains <0.5) and cooldown[classtable.EmpowerRuneWeapon].ready then
        MaxDps:GlowCooldown(classtable.EmpowerRuneWeapon, cooldown[classtable.EmpowerRuneWeapon].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.EmpowerRuneWeapon, 'EmpowerRuneWeapon')) and (cooldown[classtable.EmpowerRuneWeapon].fullRecharge <= 6 and buff[classtable.KillingMachineBuff].count <1+(1 * (talents[classtable.KillingStreak] and talents[classtable.KillingStreak] or 0)) and MaxDps:CooldownConsolidated(61304).remains <0.5) and cooldown[classtable.EmpowerRuneWeapon].ready then
        MaxDps:GlowCooldown(classtable.EmpowerRuneWeapon, cooldown[classtable.EmpowerRuneWeapon].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.ArcanePulse, 'ArcanePulse')) and (cooldown_check) and cooldown[classtable.ArcanePulse].ready then
        if not setSpell then setSpell = classtable.ArcanePulse end
    end
    if (targets >= 3) then
        Frost:aoe()
    end
    Frost:single_target()
end
function DeathKnight:Frost()
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
    --for spellId in pairs(MaxDps.Flags) do
    --    self.Flags[spellId] = false
    --    self:ClearGlowIndependent(spellId, spellId)
    --end
    classtable.PillarofFrostBuff = 51271
    classtable.UnholyStrengthBuff = 53365
    classtable.BonegrinderFrostBuff = 377103
    classtable.IcyOnslaughtBuff = 1230273
    classtable.RemorselessWinterBuff = 196770
    classtable.LatentEnergyBuff = 0
    classtable.BreathofSindragosaBuff = 152279
    classtable.GatheringStormBuff = 194912
    classtable.ReaperofSoulsBuff = 469172
    classtable.KillingMachineBuff = 51124
    classtable.RimeBuff = 59052
    classtable.FrostbaneBuff = 1229310
    classtable.RazoriceDeBuff = 51714
    classtable.ChainsofIceTrollbaneSlowDeBuff = 444826
    classtable.FrostFeverDeBuff = 55095
    classtable.ArcanePulse = 260369

    local function debugg()
        talents[classtable.BreathofSindragosa] = 1
        talents[classtable.PillarofFrost] = 1
        talents[classtable.GatheringStorm] = 1
        talents[classtable.ApocalypseNow] = 1
        talents[classtable.Obliteration] = 1
        talents[classtable.ReaperofSouls] = 1
        talents[classtable.FrostboundWill] = 1
        talents[classtable.ShatteringBlade] = 1
        talents[classtable.Frostbane] = 1
    end


    --if MaxDps.db.global.debugMode then
    --   debugg()
    --end

    setSpell = nil
    ClearCDs()

    Frost:precombat()

    Frost:callaction()
    if setSpell then return setSpell end
end
