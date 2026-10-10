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

local Frost = {}



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


function Frost:precombat()
    if (MaxDps:CheckSpellUsable(classtable.UnholyPresence, 'UnholyPresence')) and (not buff[classtable.UnholyPresenceBuff].up) and cooldown[classtable.UnholyPresence].ready and not UnitAffectingCombat('player') then
        if not setSpell then setSpell = classtable.UnholyPresence end
    end
    if (MaxDps:CheckSpellUsable(classtable.SummonGargoyle, 'SummonGargoyle')) and cooldown[classtable.SummonGargoyle].ready and not UnitAffectingCombat('player') then
        if not setSpell then setSpell = classtable.SummonGargoyle end
    end
    if (MaxDps:CheckSpellUsable(classtable.HornofWinter, 'HornofWinter')) and cooldown[classtable.HornofWinter].ready and not UnitAffectingCombat('player') then
        if not setSpell then setSpell = classtable.HornofWinter end
    end
end


local function ClearCDs()
    MaxDps:GlowCooldown(classtable.EmpowerRuneWeapon, false)
end

function Frost:callaction()
    if (MaxDps:CheckSpellUsable(classtable.EmpowerRuneWeapon, 'EmpowerRuneWeapon')) and (DeathKnight:RuneTypeCount('Blood') == 0 and DeathKnight:RuneTypeCount('Frost') == 0 and DeathKnight:RuneTypeCount('Unholy') == 0) and cooldown[classtable.EmpowerRuneWeapon].ready then
        --if not setSpell then setSpell = classtable.EmpowerRuneWeapon end
        MaxDps:GlowCooldown(classtable.EmpowerRuneWeapon, true)
    end
    if (MaxDps:CheckSpellUsable(classtable.PillarofFrost, 'PillarofFrost')) and (buff[classtable.UnholyStrengthBuff].up) and cooldown[classtable.PillarofFrost].ready then
        if not setSpell then setSpell = classtable.PillarofFrost end
    end
    if (MaxDps:CheckSpellUsable(classtable.FrostStrike, 'FrostStrike')) and (ttd <= 3) and cooldown[classtable.FrostStrike].ready then
        if not setSpell then setSpell = classtable.FrostStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.Obliterate, 'Obliterate')) and (ttd <= 3) and cooldown[classtable.Obliterate].ready then
        if not setSpell then setSpell = classtable.Obliterate end
    end
    if (MaxDps:CheckSpellUsable(classtable.HowlingBlast, 'HowlingBlast')) and (ttd <= 3) and cooldown[classtable.HowlingBlast].ready then
        if not setSpell then setSpell = classtable.HowlingBlast end
    end
    if (MaxDps:CheckSpellUsable(classtable.RaiseDead, 'RaiseDead')) and (not UnitExists ( 'pet' ) and buff[classtable.PillarofFrostBuff].up and buff[classtable.UnholyStrengthBuff].up and buff[classtable.SynapseSpringsBuff].up) and cooldown[classtable.RaiseDead].ready then
        if not setSpell then setSpell = classtable.RaiseDead end
    end
    if (MaxDps:CheckSpellUsable(classtable.BloodTap, 'BloodTap')) and (DeathKnight:RuneTypeCount('Death') <= 1 and DeathKnight:TimeToRunesCata(1,'Blood') >5.5) and cooldown[classtable.BloodTap].ready then
        if not setSpell then setSpell = classtable.BloodTap end
    end
    if (MaxDps:CheckSpellUsable(classtable.FrostStrike, 'FrostStrike')) and (RunicPower >= 105) and cooldown[classtable.FrostStrike].ready then
        if not setSpell then setSpell = classtable.FrostStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.Outbreak, 'Outbreak')) and (not debuff[classtable.FrostFeverDeBuff].up or not debuff[classtable.BloodPlagueDeBuff].up) and cooldown[classtable.Outbreak].ready then
        if not setSpell then setSpell = classtable.Outbreak end
    end
    if (MaxDps:CheckSpellUsable(classtable.HowlingBlast, 'HowlingBlast')) and (buff[classtable.FreezingFogBuff].up) and cooldown[classtable.HowlingBlast].ready then
        if not setSpell then setSpell = classtable.HowlingBlast end
    end
    if (MaxDps:CheckSpellUsable(classtable.HowlingBlast, 'HowlingBlast')) and (not debuff[classtable.FrostFeverDeBuff].up) and cooldown[classtable.HowlingBlast].ready then
        if not setSpell then setSpell = classtable.HowlingBlast end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathandDecay, 'DeathandDecay')) and (not (GetUnitSpeed('player') >0) and ttd >5 and targets >1) and cooldown[classtable.DeathandDecay].ready then
        if not setSpell then setSpell = classtable.DeathandDecay end
    end
    if (MaxDps:CheckSpellUsable(classtable.PlagueStrike, 'PlagueStrike')) and (not debuff[classtable.BloodPlagueDeBuff].up and DeathKnight:RuneTypeCount('Unholy') == 2) and cooldown[classtable.PlagueStrike].ready then
        if not setSpell then setSpell = classtable.PlagueStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.FrostStrike, 'FrostStrike')) and (RunicPower >100 and DeathKnight:RuneTypeCount('Frost') == 0 and DeathKnight:RuneTypeCount('Death') == 0 and DeathKnight:RuneTypeCount('Unholy') >= 1) and cooldown[classtable.FrostStrike].ready then
        if not setSpell then setSpell = classtable.FrostStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.Obliterate, 'Obliterate')) and (debuff[classtable.BloodPlagueDeBuff].up and DeathKnight:RuneTypeCount('Unholy') == 2) and cooldown[classtable.Obliterate].ready then
        if not setSpell then setSpell = classtable.Obliterate end
    end
    if (MaxDps:CheckSpellUsable(classtable.FrostStrike, 'FrostStrike')) and (buff[classtable.KillingMachineBuff].up) and cooldown[classtable.FrostStrike].ready then
        if not setSpell then setSpell = classtable.FrostStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.HornofWinter, 'HornofWinter')) and (not buff[classtable.HornofWinterBuff].up) and cooldown[classtable.HornofWinter].ready then
        if not setSpell then setSpell = classtable.HornofWinter end
    end
    if (MaxDps:CheckSpellUsable(classtable.HowlingBlast, 'HowlingBlast')) and cooldown[classtable.HowlingBlast].ready then
        if not setSpell then setSpell = classtable.HowlingBlast end
    end
    if (MaxDps:CheckSpellUsable(classtable.RaiseDead, 'RaiseDead')) and (not UnitExists ( 'pet' ) and buff[classtable.PillarofFrostBuff].up and buff[classtable.UnholyStrengthBuff].up) and cooldown[classtable.RaiseDead].ready then
        if not setSpell then setSpell = classtable.RaiseDead end
    end
    if (MaxDps:CheckSpellUsable(classtable.FrostStrike, 'FrostStrike')) and cooldown[classtable.FrostStrike].ready then
        if not setSpell then setSpell = classtable.FrostStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.HornofWinter, 'HornofWinter')) and cooldown[classtable.HornofWinter].ready then
        if not setSpell then setSpell = classtable.HornofWinter end
    end
    if (MaxDps:CheckSpellUsable(classtable.PlagueStrike, 'PlagueStrike')) and (DeathKnight:RuneTypeCount('Frost') == 0 and DeathKnight:RuneTypeCount('Death') == 0 and DeathKnight:RuneTypeCount('Unholy') >= 1 and DeathKnight:TimeToRunesCata(1,'Frost') >2.5 and DeathKnight:TimeToRunesCata(1,'Blood') >2.5) and cooldown[classtable.PlagueStrike].ready then
        if not setSpell then setSpell = classtable.PlagueStrike end
    end
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
    --for spellId in pairs(MaxDps.Flags) do
    --    self.Flags[spellId] = false
    --    self:ClearGlowIndependent(spellId, spellId)
    --end
    classtable.UnholyPresenceBuff = 48265
    classtable.UnholyStrengthBuff = 53365
    classtable.PillarofFrostBuff = 51271
    classtable.FreezingFogBuff = 59052
    classtable.KillingMachineBuff = 51124
    classtable.HornofWinterBuff = 57330
    classtable.FrostFeverDeBuff = 55095
    classtable.BloodPlagueDeBuff = 55078
    classtable.UnholyPresence = 48265
    classtable.SummonGargoyle = 49206
    classtable.HornofWinter = 57330
    classtable.EmpowerRuneWeapon = 47568
    classtable.PillarofFrost = 51271
    classtable.FrostStrike = 49143
    classtable.Obliterate = 49020
    classtable.HowlingBlast = 49184
    classtable.RaiseDead = 46584
    classtable.BloodTap = 45529
    classtable.Outbreak = 77575
    classtable.DeathandDecay = 43265
    classtable.PlagueStrike = 45462

    local function debugg()
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
