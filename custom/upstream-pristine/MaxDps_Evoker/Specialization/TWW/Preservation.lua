local _, addonTable = ...
local Evoker = addonTable.Evoker
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
local className, classFilename, classId = UnitClass('player')
local currentSpec = GetSpecialization()
local currentSpecName = currentSpec and select(2, GetSpecializationInfo(currentSpec)) or 'None'
local classtable
local LibRangeCheck = LibStub('LibRangeCheck-3.0', true)

local Essence
local EssenceMax
local EssenceDeficit
local EssenceTimeToMax
local Mana
local ManaMax
local ManaDeficit

local Preservation = {}

function Preservation:precombat()
    if (MaxDps:CheckSpellUsable(classtable.BlessingoftheBronze, 'BlessingoftheBronze')) and cooldown[classtable.BlessingoftheBronze].ready and not UnitAffectingCombat('player') then
        if not setSpell then setSpell = classtable.BlessingoftheBronze end
    end
end


local function ClearCDs()
    MaxDps:GlowCooldown(classtable.Quell, false)
end

function Preservation:callaction()
    if (MaxDps:CheckSpellUsable(classtable.Quell, 'Quell')) and cooldown[classtable.Quell].ready then
        MaxDps:GlowCooldown(classtable.Quell, ( select(8,UnitCastingInfo('target')) ~= nil and not select(8,UnitCastingInfo('target')) or select(7,UnitChannelInfo('target')) ~= nil and not select(7,UnitChannelInfo('target'))) )
    end
    --if (MaxDps:CheckSpellUsable(classtable.CauterizingFlame, 'CauterizingFlame')) and cooldown[classtable.CauterizingFlame].ready then
    --    if not setSpell then setSpell = classtable.CauterizingFlame end
    --end
    if (MaxDps:CheckSpellUsable(classtable.Unravel, 'Unravel')) and cooldown[classtable.Unravel].ready then
        if not setSpell then setSpell = classtable.Unravel end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeepBreath, 'DeepBreath')) and cooldown[classtable.DeepBreath].ready then
        if not setSpell then setSpell = classtable.DeepBreath end
    end
    if (MaxDps:CheckSpellUsable(classtable.FireBreath, 'FireBreath')) and (ttd >14 + ( classtable and classtable.FireBreath and GetSpellInfo(classtable.FireBreath).castTime /1000 or 0)) and cooldown[classtable.FireBreath].ready then
        if not setSpell then setSpell = classtable.FireBreath end
    end
    if (MaxDps:CheckSpellUsable(classtable.FireBreath, 'FireBreath')) and (ttd >8 + ( classtable and classtable.FireBreath and GetSpellInfo(classtable.FireBreath).castTime /1000 or 0)) and cooldown[classtable.FireBreath].ready then
        if not setSpell then setSpell = classtable.FireBreath end
    end
    if (MaxDps:CheckSpellUsable(classtable.FireBreath, 'FireBreath')) and (ttd >2 + ( classtable and classtable.FireBreath and GetSpellInfo(classtable.FireBreath).castTime /1000 or 0)) and cooldown[classtable.FireBreath].ready then
        if not setSpell then setSpell = classtable.FireBreath end
    end
    if (MaxDps:CheckSpellUsable(classtable.FireBreath, 'FireBreath')) and ((MaxDps.Spells[classtable.FireBreath] and cooldown[classtable.FireBreath].ready and targets >=3) or ttd >( classtable and classtable.FireBreath and GetSpellInfo(classtable.FireBreath).castTime /1000 or 0)) and cooldown[classtable.FireBreath].ready then
        if not setSpell then setSpell = classtable.FireBreath end
    end
    if (MaxDps:CheckSpellUsable(classtable.Hover, 'Hover')) and ((GetUnitSpeed('player') >0) and not buff[classtable.HoverBuff].up and false) and cooldown[classtable.Hover].ready then
        if not setSpell then setSpell = classtable.Hover end
    end
    if (MaxDps:CheckSpellUsable(classtable.Disintegrate, 'Disintegrate')) and (buff[classtable.EssenceBurstBuff].up and ( not (GetUnitSpeed('player') >0) or buff[classtable.HoverBuff].remains >( classtable and classtable.Disintegrate and GetSpellInfo(classtable.Disintegrate).castTime /1000 or 0) ) or EssenceTimeToMax <( classtable and classtable.Disintegrate and GetSpellInfo(classtable.Disintegrate).castTime /1000 or 0) and false) and cooldown[classtable.Disintegrate].ready then
        if not setSpell then setSpell = classtable.Disintegrate end
    end
    if (MaxDps:CheckSpellUsable(classtable.AzureStrike, 'AzureStrike')) and (targets >2) and cooldown[classtable.AzureStrike].ready then
        if not setSpell then setSpell = classtable.AzureStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.LivingFlame, 'LivingFlame')) and (not (GetUnitSpeed('player') >0) or buff[classtable.HoverBuff].remains >( classtable and classtable.LivingFlame and GetSpellInfo(classtable.LivingFlame).castTime /1000 or 0)) and cooldown[classtable.LivingFlame].ready then
        if not setSpell then setSpell = classtable.LivingFlame end
    end
    if (MaxDps:CheckSpellUsable(classtable.AzureStrike, 'AzureStrike')) and ((GetUnitSpeed('player') >0) and not buff[classtable.HoverBuff].up) and cooldown[classtable.AzureStrike].ready then
        if not setSpell then setSpell = classtable.AzureStrike end
    end
end
function Evoker:Preservation()
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
    Essence = UnitPower('player', EssencePT)
    EssenceMax = UnitPowerMax('player', EssencePT)
    EssenceDeficit = EssenceMax - Essence
    EssenceRegen = GetPowerRegenForPowerType(Enum.PowerType.Essence)
    EssenceTimeToMax = EssenceDeficit / EssenceRegen
    --for spellId in pairs(MaxDps.Flags) do
    --    self.Flags[spellId] = false
    --    self:ClearGlowIndependent(spellId, spellId)
    --end
    classtable.HoverBuff = classtable and classtable.Hover or 0
    classtable.EssenceBurstBuff = 369299--369297

    local function debugg()
    end


    --if MaxDps.db.global.debugMode then
    --   debugg()
    --end

    setSpell = nil
    ClearCDs()

    Preservation:precombat()

    Preservation:callaction()
    if setSpell then return setSpell end
end
