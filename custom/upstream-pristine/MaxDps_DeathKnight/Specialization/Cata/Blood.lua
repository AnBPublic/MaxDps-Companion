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
local ibf_damage
local rt_damage
local vb_damage

local Blood = {}



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


function Blood:precombat()
    if (MaxDps:CheckSpellUsable(classtable.BloodPresence, 'BloodPresence')) and (not buff[classtable.BloodPresenceBuff].up) and cooldown[classtable.BloodPresence].ready and not UnitAffectingCombat('player') then
        if not setSpell then setSpell = classtable.BloodPresence end
    end
    if (MaxDps:CheckSpellUsable(classtable.BoneShield, 'BoneShield')) and cooldown[classtable.BoneShield].ready and not UnitAffectingCombat('player') then
        if not setSpell then setSpell = classtable.BoneShield end
    end
    if (MaxDps:CheckSpellUsable(classtable.ArmyoftheDead, 'ArmyoftheDead')) and cooldown[classtable.ArmyoftheDead].ready and not UnitAffectingCombat('player') then
        if not setSpell then setSpell = classtable.ArmyoftheDead end
    end
    if (MaxDps:CheckSpellUsable(classtable.RaiseDead, 'RaiseDead')) and (not UnitExists ( 'pet' )) and cooldown[classtable.RaiseDead].ready and not UnitAffectingCombat('player') then
        if not setSpell then setSpell = classtable.RaiseDead end
    end
    if (MaxDps:CheckSpellUsable(classtable.HornofWinter, 'HornofWinter')) and cooldown[classtable.HornofWinter].ready and not UnitAffectingCombat('player') then
        if not setSpell then setSpell = classtable.HornofWinter end
    end
end
function Blood:aoe()
    if (MaxDps:CheckSpellUsable(classtable.DeathandDecay, 'DeathandDecay')) and cooldown[classtable.DeathandDecay].ready then
        if not setSpell then setSpell = classtable.DeathandDecay end
    end
    if (MaxDps:CheckSpellUsable(classtable.Outbreak, 'Outbreak')) and (debuff[classtable.BloodPlagueDeBuff].remains <= 1 or debuff[classtable.FrostFeverDeBuff].remains <= 1) and cooldown[classtable.Outbreak].ready then
        if not setSpell then setSpell = classtable.Outbreak end
    end
    if (MaxDps:CheckSpellUsable(classtable.Pestilence, 'Pestilence')) and (debuff[classtable.FrostFeverDeBuff].up and debuff[classtable.BloodPlagueDeBuff].up and ( MaxDps:DebuffCounter(classtable.FrostFeverDeBuff) + MaxDps:DebuffCounter(classtable.BloodPlagueDeBuff) <targets * 2 )) and cooldown[classtable.Pestilence].ready then
        if not setSpell then setSpell = classtable.Pestilence end
    end
    if (MaxDps:CheckSpellUsable(classtable.HeartStrike, 'HeartStrike')) and (targets <= 5) and cooldown[classtable.HeartStrike].ready then
        if not setSpell then setSpell = classtable.HeartStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.BloodBoil, 'BloodBoil')) and (targets >5 or buff[classtable.CrimsonScourgeBuff].up) and cooldown[classtable.BloodBoil].ready then
        if not setSpell then setSpell = classtable.BloodBoil end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathStrike, 'DeathStrike')) and (healthPerc <= 50 or not buff[classtable.BloodShieldBuff].up) and cooldown[classtable.DeathStrike].ready then
        if not setSpell then setSpell = classtable.DeathStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.IcyTouch, 'IcyTouch')) and (not debuff[classtable.FrostFeverDeBuff].up and ttd >10) and cooldown[classtable.IcyTouch].ready then
        if not setSpell then setSpell = classtable.IcyTouch end
    end
    if (MaxDps:CheckSpellUsable(classtable.PlagueStrike, 'PlagueStrike')) and (not debuff[classtable.BloodPlagueDeBuff].up and ttd >10) and cooldown[classtable.PlagueStrike].ready then
        if not setSpell then setSpell = classtable.PlagueStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathStrike, 'DeathStrike')) and cooldown[classtable.DeathStrike].ready then
        if not setSpell then setSpell = classtable.DeathStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.RuneStrike, 'RuneStrike')) and cooldown[classtable.RuneStrike].ready then
        if not setSpell then setSpell = classtable.RuneStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.HornofWinter, 'HornofWinter')) and cooldown[classtable.HornofWinter].ready then
        if not setSpell then setSpell = classtable.HornofWinter end
    end
    if (MaxDps:CheckSpellUsable(classtable.RaiseDead, 'RaiseDead')) and (not UnitExists ( 'pet' )) and cooldown[classtable.RaiseDead].ready then
        if not setSpell then setSpell = classtable.RaiseDead end
    end
end


local function ClearCDs()
    MaxDps:GlowCooldown(classtable.MindFreeze, false)
    MaxDps:GlowCooldown(classtable.EmpowerRuneWeapon, false)
end

function Blood:callaction()
    if (MaxDps:CheckSpellUsable(classtable.MindFreeze, 'MindFreeze')) and cooldown[classtable.MindFreeze].ready then
        MaxDps:GlowCooldown(classtable.MindFreeze, ( select(8,UnitCastingInfo('target')) ~= nil and not select(8,UnitCastingInfo('target')) or select(7,UnitChannelInfo('target')) ~= nil and not select(7,UnitChannelInfo('target'))) )
    end
    if (MaxDps:CheckSpellUsable(classtable.DancingRuneWeapon, 'DancingRuneWeapon')) and cooldown[classtable.DancingRuneWeapon].ready then
        if not setSpell then setSpell = classtable.DancingRuneWeapon end
    end
    if (MaxDps:CheckSpellUsable(classtable.IceboundFortitude, 'IceboundFortitude')) and (healthPerc <= 30) and cooldown[classtable.IceboundFortitude].ready then
        if not setSpell then setSpell = classtable.IceboundFortitude end
    end
    if (MaxDps:CheckSpellUsable(classtable.Outbreak, 'Outbreak')) and (debuff[classtable.BloodPlagueDeBuff].remains <= 1 or debuff[classtable.FrostFeverDeBuff].remains <= 1) and cooldown[classtable.Outbreak].ready then
        if not setSpell then setSpell = classtable.Outbreak end
    end
    if (MaxDps:CheckSpellUsable(classtable.BoneShield, 'BoneShield')) and (not buff[classtable.BoneShieldBuff].up) and cooldown[classtable.BoneShield].ready then
        if not setSpell then setSpell = classtable.BoneShield end
    end
    if (MaxDps:CheckSpellUsable(classtable.VampiricBlood, 'VampiricBlood')) and (healthPerc <= 50) and cooldown[classtable.VampiricBlood].ready then
        if not setSpell then setSpell = classtable.VampiricBlood end
    end
    if (MaxDps:CheckSpellUsable(classtable.RuneTap, 'RuneTap')) and (buff[classtable.WilloftheNecropolisBuff].up) and cooldown[classtable.RuneTap].ready then
        if not setSpell then setSpell = classtable.RuneTap end
    end
    if (MaxDps:CheckSpellUsable(classtable.EmpowerRuneWeapon, 'EmpowerRuneWeapon')) and (healthPerc <= 50 and not cooldown[classtable.DeathStrike].ready) and cooldown[classtable.EmpowerRuneWeapon].ready then
        --if not setSpell then setSpell = classtable.EmpowerRuneWeapon end
        MaxDps:GlowCooldown(classtable.EmpowerRuneWeapon, true)
    end
    if (targets >1) then
        Blood:aoe()
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathStrike, 'DeathStrike')) and (DeathKnight:RuneTypeCount('Frost') == 2 or DeathKnight:RuneTypeCount('Unholy') == 2 or healthPerc <= 50 or not buff[classtable.BloodShieldBuff].up) and cooldown[classtable.DeathStrike].ready then
        if not setSpell then setSpell = classtable.DeathStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.RuneStrike, 'RuneStrike')) and (RunicPower >= RunicPowerMax - 10) and cooldown[classtable.RuneStrike].ready then
        if not setSpell then setSpell = classtable.RuneStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.BloodTap, 'BloodTap')) and (( DeathKnight:RuneTypeCount('Frost') == 1 or DeathKnight:RuneTypeCount('Unholy') == 1 or DeathKnight:RuneTypeCount('Death') == 1 ) and not cooldown[classtable.DeathStrike].ready) and cooldown[classtable.BloodTap].ready then
        if not setSpell then setSpell = classtable.BloodTap end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathStrike, 'DeathStrike')) and cooldown[classtable.DeathStrike].ready then
        if not setSpell then setSpell = classtable.DeathStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.RuneTap, 'RuneTap')) and (healthPerc <= 80 and DeathKnight:RuneTypeCount('Blood') == 2 and DeathKnight:RuneTypeDeathCount('Blood') == 0) and cooldown[classtable.RuneTap].ready then
        if not setSpell then setSpell = classtable.RuneTap end
    end
    if (MaxDps:CheckSpellUsable(classtable.BloodBoil, 'BloodBoil')) and (buff[classtable.CrimsonScourgeBuff].up) and cooldown[classtable.BloodBoil].ready then
        if not setSpell then setSpell = classtable.BloodBoil end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathPact, 'DeathPact')) and ((GetTotemDuration("Risen Ghoul") <5 and GetTotemDuration("Risen Ghoul") >1) or (healthPerc <= 30 and GetTotemDuration("Risen Ghoul") >1)) and cooldown[classtable.DeathPact].ready then
        if not setSpell then setSpell = classtable.DeathPact end
    end
    if (MaxDps:CheckSpellUsable(classtable.HeartStrike, 'HeartStrike')) and (DeathKnight:RuneTypeCount('Blood') == 2 and DeathKnight:RuneTypeDeathCount('Blood') == 0) and cooldown[classtable.HeartStrike].ready then
        if not setSpell then setSpell = classtable.HeartStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.IcyTouch, 'IcyTouch')) and (not debuff[classtable.FrostFeverDeBuff].up) and cooldown[classtable.IcyTouch].ready then
        if not setSpell then setSpell = classtable.IcyTouch end
    end
    if (MaxDps:CheckSpellUsable(classtable.PlagueStrike, 'PlagueStrike')) and (not debuff[classtable.BloodPlagueDeBuff].up) and cooldown[classtable.PlagueStrike].ready then
        if not setSpell then setSpell = classtable.PlagueStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.RuneStrike, 'RuneStrike')) and cooldown[classtable.RuneStrike].ready then
        if not setSpell then setSpell = classtable.RuneStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.HornofWinter, 'HornofWinter')) and cooldown[classtable.HornofWinter].ready then
        if not setSpell then setSpell = classtable.HornofWinter end
    end
    if (MaxDps:CheckSpellUsable(classtable.RaiseDead, 'RaiseDead')) and (not UnitExists ( 'pet' )) and cooldown[classtable.RaiseDead].ready then
        if not setSpell then setSpell = classtable.RaiseDead end
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
    ibf_damage = maxHP * ( 40 ) * 0.01
    rt_damage = maxHP * ( 30 ) * 0.01
    vb_damage = maxHP * ( 50 ) * 0.01
    --for spellId in pairs(MaxDps.Flags) do
    --    self.Flags[spellId] = false
    --    self:ClearGlowIndependent(spellId, spellId)
    --end
    classtable.BloodPresenceBuff = 48263
    classtable.BoneShieldBuff = 49222
    classtable.WilloftheNecropolisBuff = 96171
    classtable.BloodShieldBuff = 77535
    classtable.CrimsonScourgeBuff = 81141
    classtable.BloodPlagueDeBuff = 55078
    classtable.FrostFeverDeBuff = 55095
    classtable.BloodPresence = 48263
    classtable.BoneShield = 49222
    classtable.ArmyoftheDead = 42650
    classtable.RaiseDead = 46584
    classtable.HornofWinter = 57330
    classtable.DeathandDecay = 43265
    classtable.Outbreak = 77575
    classtable.Pestilence = 50842
    classtable.HeartStrike = 55050
    classtable.BloodBoil = 48721
    classtable.DeathStrike = 49998
    classtable.IcyTouch = 45477
    classtable.PlagueStrike = 45462
    classtable.RuneStrike = 56815
    classtable.MindFreeze = 47528
    classtable.DancingRuneWeapon = 49028
    classtable.IceboundFortitude = 48792
    classtable.VampiricBlood = 55233
    classtable.RuneTap = 48982
    classtable.EmpowerRuneWeapon = 47568
    classtable.BloodTap = 45529
    classtable.DeathPact = 48743

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
