local _, addonTable = ...
local DeathKnight = addonTable.DeathKnight
local MaxDps = _G.MaxDps
if not MaxDps then return end
local setSpell

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
local GetSpellCount = C_Spell.GetSpellCastCount

local ManaPT = Enum.PowerType.Mana
local RagePT = Enum.PowerType.Rage
local FocusPT = Enum.PowerType.Focus
local EnergyPT = Enum.PowerType.Energy
local ComboPointsPT = Enum.PowerType.ComboPoints
local RunesPT = Enum.PowerType.Runes
local RunicPowerPT = Enum.PowerType.RunicPower
local SoulShardsPT = Enum.PowerType.SoulShards
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
local classtable
local LibRangeCheck = LibStub('LibRangeCheck-3.0', true)

local Runes
local RuneBlood
local RuneFrost
local RuneUnholy
local RunicPower
local RunicPowerMax
local RunicPowerDeficit

local Unholy = {}



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


local function GetTotemDuration(name)
    for index=1,MAX_TOTEMS do
        local arg1, totemName, startTime, duration, icon = GetTotemInfo(index)
        local est_dur = math.floor(startTime+duration-GetTime())
        if (totemName == name and est_dur and est_dur > 0) then return est_dur else return 0 end
    end
end


function Unholy:precombat()
    if (MaxDps:CheckSpellUsable(classtable.UnholyPresence, 'UnholyPresence')) and (not buff[classtable.UnholyPresence].up) and cooldown[classtable.UnholyPresence].ready and not UnitAffectingCombat('player') then
        if not setSpell then setSpell = classtable.UnholyPresence end
    end
    if (MaxDps:CheckSpellUsable(classtable.RaiseDead, 'RaiseDead')) and (not UnitExists ( 'pet' ) and (talents[classtable.MasterofGhouls] and true or false)) and cooldown[classtable.RaiseDead].ready and not UnitAffectingCombat('player') then
        if not setSpell then setSpell = classtable.RaiseDead end
    end
    if (MaxDps:CheckSpellUsable(classtable.ArmyoftheDead, 'ArmyoftheDead')) and cooldown[classtable.ArmyoftheDead].ready and not UnitAffectingCombat('player') then
        if not setSpell then setSpell = classtable.ArmyoftheDead end
    end
    if (MaxDps:CheckSpellUsable(classtable.HornofWinter, 'HornofWinter')) and cooldown[classtable.HornofWinter].ready and not UnitAffectingCombat('player') then
        if not setSpell then setSpell = classtable.HornofWinter end
    end
end


local function ClearCDs()
    MaxDps:GlowCooldown(classtable.MindFreeze, false)
end

function Unholy:callaction()
    if (MaxDps:CheckSpellUsable(classtable.MindFreeze, 'MindFreeze')) and cooldown[classtable.MindFreeze].ready then
        MaxDps:GlowCooldown(classtable.MindFreeze, ( select(8,UnitCastingInfo('target')) ~= nil and not select(8,UnitCastingInfo('target')) or select(7,UnitChannelInfo('target')) ~= nil and not select(7,UnitChannelInfo('target'))) )
    end
    --if (MaxDps:CheckSpellUsable(classtable.DarkTransformation, 'DarkTransformation')) and (UnitExists ( 'pet' ) and ttd >= 15 or targets >1) and cooldown[classtable.DarkTransformation].ready then
    --    if not setSpell then setSpell = classtable.DarkTransformation end
    --end
    if (MaxDps:CheckSpellUsable(classtable.ScourgeStrike, 'ScourgeStrike')) and (ttd <3 and targets <2) and cooldown[classtable.ScourgeStrike].ready then
        if not setSpell then setSpell = classtable.ScourgeStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.FesteringStrike, 'FesteringStrike')) and (ttd <3 and targets <2) and cooldown[classtable.FesteringStrike].ready then
        if not setSpell then setSpell = classtable.FesteringStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathCoil, 'DeathCoil')) and (ttd <3 and targets <2) and cooldown[classtable.DeathCoil].ready then
        if not setSpell then setSpell = classtable.DeathCoil end
    end
    if (MaxDps:CheckSpellUsable(classtable.UnholyFrenzy, 'UnholyFrenzy')) and (buff[classtable.UnholyStrengthBuff].up) and cooldown[classtable.UnholyFrenzy].ready then
        if not setSpell then setSpell = classtable.UnholyFrenzy end
    end
    if (MaxDps:CheckSpellUsable(classtable.Outbreak, 'Outbreak')) and (buff[classtable.UnholyStrengthBuff].up and buff[classtable.SynapseSpringsBuff].up) and cooldown[classtable.Outbreak].ready then
        if not setSpell then setSpell = classtable.Outbreak end
    end
    if (MaxDps:CheckSpellUsable(classtable.SummonGargoyle, 'SummonGargoyle')) and (buff[classtable.UnholyStrengthBuff].up and ( cooldown[classtable.UnholyFrenzy].ready or buff[classtable.UnholyFrenzyBuff].up or MaxDps:Bloodlust(1) )) and cooldown[classtable.SummonGargoyle].ready then
        if not setSpell then setSpell = classtable.SummonGargoyle end
    end
    if (MaxDps:CheckSpellUsable(classtable.Outbreak, 'Outbreak')) and (timeInCombat >15 and ( not debuff[classtable.FrostFeverDeBuff].up or not debuff[classtable.BloodPlagueDeBuff].up )) and cooldown[classtable.Outbreak].ready then
        if not setSpell then setSpell = classtable.Outbreak end
    end
    if (MaxDps:CheckSpellUsable(classtable.IcyTouch, 'IcyTouch')) and (not debuff[classtable.FrostFeverDeBuff].up) and cooldown[classtable.IcyTouch].ready then
        if not setSpell then setSpell = classtable.IcyTouch end
    end
    if (MaxDps:CheckSpellUsable(classtable.PlagueStrike, 'PlagueStrike')) and (not debuff[classtable.BloodPlagueDeBuff].up) and cooldown[classtable.PlagueStrike].ready then
        if not setSpell then setSpell = classtable.PlagueStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.Pestilence, 'Pestilence')) and (debuff[classtable.FrostFeverDeBuff].up and debuff[classtable.BloodPlagueDeBuff].up and ( MaxDps:DebuffCounter(classtable.FrostFeverDeBuff) + MaxDps:DebuffCounter(classtable.BloodPlagueDeBuff) <targets * 2 )) and cooldown[classtable.Pestilence].ready then
        if not setSpell then setSpell = classtable.Pestilence end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathandDecay, 'DeathandDecay')) and (not (GetUnitSpeed('player') >0) and ttd >5 or targets >1) and cooldown[classtable.DeathandDecay].ready then
        if not setSpell then setSpell = classtable.DeathandDecay end
    end
    if (MaxDps:CheckSpellUsable(classtable.BloodBoil, 'BloodBoil')) and (targets >2) and cooldown[classtable.BloodBoil].ready then
        if not setSpell then setSpell = classtable.BloodBoil end
    end
    if (MaxDps:CheckSpellUsable(classtable.FesteringStrike, 'FesteringStrike')) and (DeathKnight:TimeToRunesCata(2,'Blood') <= 1 and DeathKnight:TimeToRunesCata(2,'Frost') <= 1) and cooldown[classtable.FesteringStrike].ready then
        if not setSpell then setSpell = classtable.FesteringStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.ScourgeStrike, 'ScourgeStrike')) and (DeathKnight:TimeToRunesCata(2,'Unholy') <= 1) and cooldown[classtable.ScourgeStrike].ready then
        if not setSpell then setSpell = classtable.ScourgeStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.BloodStrike, 'BloodStrike')) and (DeathKnight:TimeToRunesCata(2,'Blood') <= 1 and not IsSpellKnownOrOverridesKnown(classtable.FesteringStrike)) and cooldown[classtable.BloodStrike].ready then
        if not setSpell then setSpell = classtable.BloodStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathCoil, 'DeathCoil')) and (not cooldown[classtable.SummonGargoyle].ready and not buff[classtable.RunicCorruptionBuff].up and not MaxDps:Bloodlust(1)) and cooldown[classtable.DeathCoil].ready then
        if not setSpell then setSpell = classtable.DeathCoil end
    end
    if (MaxDps:CheckSpellUsable(classtable.IcyTouch, 'IcyTouch')) and (DeathKnight:TimeToRunesCata(2,'Frost') <= 1 and ( not IsSpellKnownOrOverridesKnown(classtable.FesteringStrike) or targets >2 )) and cooldown[classtable.IcyTouch].ready then
        if not setSpell then setSpell = classtable.IcyTouch end
    end
    if (MaxDps:CheckSpellUsable(classtable.ScourgeStrike, 'ScourgeStrike')) and cooldown[classtable.ScourgeStrike].ready then
        if not setSpell then setSpell = classtable.ScourgeStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathCoil, 'DeathCoil')) and (RunicPower >= 44 and ( not buff[classtable.DarkTransformationBuff].up and buff[classtable.ShadowInfusionBuff].count <5 ) or ( buff[classtable.SuddenDoomBuff].up and buff[classtable.SuddenDoomBuff].remains <buff[classtable.DarkTransformationBuff].remains )) and cooldown[classtable.DeathCoil].ready then
        if not setSpell then setSpell = classtable.DeathCoil end
    end
    if (MaxDps:CheckSpellUsable(classtable.FesteringStrike, 'FesteringStrike')) and cooldown[classtable.FesteringStrike].ready then
        if not setSpell then setSpell = classtable.FesteringStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.BloodStrike, 'BloodStrike')) and (not IsSpellKnownOrOverridesKnown(classtable.FesteringStrike)) and cooldown[classtable.BloodStrike].ready then
        if not setSpell then setSpell = classtable.BloodStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathCoil, 'DeathCoil')) and (not cooldown[classtable.SummonGargoyle].ready or buff[classtable.SuddenDoomBuff].up or RunicPower >= 100) and cooldown[classtable.DeathCoil].ready then
        if not setSpell then setSpell = classtable.DeathCoil end
    end
    if (MaxDps:CheckSpellUsable(classtable.BloodTap, 'BloodTap')) and (DeathKnight:RuneTypeCount('Blood') == 0 and DeathKnight:TimeToRunesCata(1,'Blood') >2) and cooldown[classtable.BloodTap].ready then
        if not setSpell then setSpell = classtable.BloodTap end
    end
    if (MaxDps:CheckSpellUsable(classtable.HornofWinter, 'HornofWinter')) and (not buff[classtable.HornofWinterBuff].up) and cooldown[classtable.HornofWinter].ready then
        if not setSpell then setSpell = classtable.HornofWinter end
    end
    if (MaxDps:CheckSpellUsable(classtable.EmpowerRuneWeapon, 'EmpowerRuneWeapon')) and (DeathKnight:RuneTypeCount('Blood') == 0 and DeathKnight:RuneTypeCount('Frost') == 0 and DeathKnight:RuneTypeCount('Unholy') == 0) and cooldown[classtable.EmpowerRuneWeapon].ready then
        if not setSpell then setSpell = classtable.EmpowerRuneWeapon end
    end
end
function DeathKnight:Unholy()
    fd = MaxDps.FrameData
    ttd = (fd.timeToDie and fd.timeToDie) or 500
    timeShift = fd.timeShift
    gcd = fd.gcd
    cooldown = fd.cooldown
    buff = fd.buff
    debuff = fd.debuff
    talents = fd.talents
    targets = MaxDps:SmartAoe()
    Mana = UnitPower('player', ManaPT)
    ManaMax = UnitPowerMax('player', ManaPT)
    ManaDeficit = ManaMax - Mana
    targetHP = UnitHealth('target')
    targetmaxHP = UnitHealthMax('target')
    targethealthPerc = (targetHP >0 and targetmaxHP >0 and (targetHP / targetmaxHP) * 100) or 100
    curentHP = UnitHealth('player')
    maxHP = UnitHealthMax('player')
    healthPerc = (curentHP / maxHP) * 100
    timeInCombat = MaxDps.combatTime or 0
    classtable = MaxDps.SpellTable
    SpellHaste = UnitSpellHaste('player')
    SpellCrit = GetCritChance()
    Runes = UnitPower('player', RunesPT)
    RuneBlood = UnitPower('player', RuneBloodPT)
    RuneFrost = UnitPower('player', RuneFrostPT)
    RuneUnholy = UnitPower('player', RuneUnholyPT)
    RunicPower = UnitPower('player', RunicPowerPT)
    RunicPowerMax = UnitPowerMax('player', RunicPowerPT)
    RunicPowerDeficit = RunicPowerMax - RunicPower
    --classtable.WoundSpender = IsSpellKnownOrOverridesKnown(classtable.ScourgeStrike) and C_Spell.GetSpellInfo(classtable.ScourgeStrike) and ( C_Spell.GetSpellInfo(C_Spell.GetSpellInfo(classtable.ScourgeStrike).name).spellID ) --55090
    --classtable.FesteringScythe = 458128
    --if buff[classtable.FesteringScytheBuff].up then
    --    classtable.FesteringStrike = classtable.FesteringScythe
    --else
    --    classtable.FesteringStrike = 85948
    --end
    --for spellId in pairs(MaxDps.Flags) do
    --    self.Flags[spellId] = false
    --    self:ClearGlowIndependent(spellId, spellId)
    --end
    classtable.PresenceBuff = 48263
    classtable.UnholyStrengthBuff = 53365
    classtable.UnholyFrenzyBuff = 49016
    classtable.RunicCorruptionBuff = 51460
    classtable.DarkTransformationBuff = 63560
    classtable.ShadowInfusionBuff = 91342
    classtable.SuddenDoomBuff = 81340
    classtable.HornofWinterBuff = 57330
    classtable.FrostFeverDeBuff = 55095
    classtable.BloodPlagueDeBuff = 55078
    classtable.UnholyPresence = 48265
    classtable.RaiseDead = 46584
    classtable.ArmyoftheDead = 42650
    classtable.HornofWinter = 57330
    classtable.MindFreeze = 47528
    classtable.DarkTransformation = 63560
    classtable.ScourgeStrike = 55090
    classtable.FesteringStrike = 85948
    classtable.DeathCoil = 47541
    classtable.UnholyFrenzy = 49016
    classtable.Outbreak = 77575
    classtable.SummonGargoyle = 49206
    classtable.IcyTouch = 45477
    classtable.PlagueStrike = 45462
    classtable.Pestilence = 50842
    classtable.DeathandDecay = 43265
    classtable.BloodBoil = 48721
    classtable.BloodStrike = 45902
    classtable.BloodTap = 45529
    classtable.EmpowerRuneWeapon = 47568

    local function debugg()
        talents[classtable.MasterofGhouls] = 1
    end


    --if MaxDps.db.global.debugMode then
    --   debugg()
    --end

    setSpell = nil
    ClearCDs()

    Unholy:precombat()

    Unholy:callaction()
    if setSpell then return setSpell end
end
