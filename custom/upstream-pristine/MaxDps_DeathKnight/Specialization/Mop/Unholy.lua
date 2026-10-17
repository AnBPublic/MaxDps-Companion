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
    local MHenchant = ""
    local OHenchant = ""
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


function Unholy:precombat()
    --if (MaxDps:CheckSpellUsable(classtable.Presence, 'Presence')) and (not MaxDps:FindBuffAuraData(classtable.PresenceBuff).up) and cooldown[classtable.Presence].ready then
    --    if not setSpell then setSpell = classtable.Presence end
    --end
    if (MaxDps:CheckSpellUsable(classtable.HornofWinter, 'HornofWinter')) and (not buff[classtable.HornofWinterBuff].up) and cooldown[classtable.HornofWinter].ready then
        MaxDps:GlowCooldown(classtable.HornofWinter, cooldown[classtable.HornofWinter].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.ArmyoftheDead, 'ArmyoftheDead')) and cooldown[classtable.ArmyoftheDead].ready then
        MaxDps:GlowCooldown(classtable.ArmyoftheDead, cooldown[classtable.ArmyoftheDead].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.RaiseDead, 'RaiseDead')) and (not UnitExists ( 'pet' )) and cooldown[classtable.RaiseDead].ready then
        MaxDps:GlowCooldown(classtable.RaiseDead, cooldown[classtable.RaiseDead].ready)
    end
end


local function ClearCDs()
    MaxDps:GlowCooldown(classtable.HornofWinter, false)
    MaxDps:GlowCooldown(classtable.ArmyoftheDead, false)
    MaxDps:GlowCooldown(classtable.RaiseDead, false)
    MaxDps:GlowCooldown(classtable.SummonGargoyle, false)
    MaxDps:GlowCooldown(classtable.DarkTransformation, false)
    MaxDps:GlowCooldown(classtable.EmpowerRuneWeapon, false)
    MaxDps:GlowCooldown(classtable.BloodTap, false)
    MaxDps:GlowCooldown(classtable.UnholyFrenzy, false)
end

local function numDepletedRunes(skipDeath)
    local depleted = 0
    for slot = 1, 6 do
        local runeTypeForId = GetRuneType(slot)
        if (skipDeath and runeTypeForId == 4) or (not skipDeath) then
            local _, _, runeReady = GetRuneCooldown(slot)
            if not runeReady then
                depleted = depleted + 1
            end
        end
    end
    return depleted
end

function Unholy:Aoe()
    -- Death and Decay
    if MaxDps:CheckSpellUsable(classtable.DeathandDecay, 'DeathandDecay') and cooldown[classtable.DeathandDecay].ready then
        if not setSpell then setSpell = classtable.DeathandDecay end
    end

    -- Blood Boil (only if Blood or Death runes are available)
    if MaxDps:CheckSpellUsable(classtable.BloodBoil, 'BloodBoil')
        and (DeathKnight:RuneTypeCount("Blood") > 0 or DeathKnight:RuneTypeCount("Death") > 0)
        and cooldown[classtable.BloodBoil].ready then
        if not setSpell then setSpell = classtable.BloodBoil end
    end

    -- Dark Transformation
    if MaxDps:CheckSpellUsable(classtable.DarkTransformation, 'DarkTransformation') and cooldown[classtable.DarkTransformation].ready then
        MaxDps:GlowCooldown(classtable.DarkTransformation, true)
    end

    -- Death Coil when no runes are available
    if MaxDps:CheckSpellUsable(classtable.DeathCoil, 'DeathCoil')
        and DeathKnight:RuneTypeCount("Blood") == 0 and DeathKnight:RuneTypeCount("Frost") == 0 and DeathKnight:RuneTypeCount("Unholy") == 0 and DeathKnight:RuneTypeCount("Death") == 0
        and cooldown[classtable.DeathCoil].ready then
        if not setSpell then setSpell = classtable.DeathCoil end
    end

    -- Icy Touch to convert excess Frost Runes to Death Runes
    if MaxDps:CheckSpellUsable(classtable.IcyTouch, 'IcyTouch')
        and DeathKnight:RuneTypeCount("Frost") > 0
        and cooldown[classtable.IcyTouch].ready then
        if not setSpell then setSpell = classtable.IcyTouch end
    end

    -- Scourge Strike when only Unholy runes and no Runic Power
    if MaxDps:CheckSpellUsable(classtable.ScourgeStrike, 'ScourgeStrike')
        and DeathKnight:RuneTypeCount("Unholy") > 0 and DeathKnight:RuneTypeCount("Blood") == 0 and DeathKnight:RuneTypeCount("Frost") == 0 and DeathKnight:RuneTypeCount("Death") == 0
        and RunicPower < 20 and cooldown[classtable.ScourgeStrike].ready then
        if not setSpell then setSpell = classtable.ScourgeStrike end
    end

    -- Wild Mushroom: Plague (only with Symbiosis from Druid)
    --if MaxDps:CheckSpellUsable(classtable.WildMushroomPlague, 'WildMushroomPlague')
    --    and buff[classtable.SymbiosisBuff]
    --    and cooldown[classtable.WildMushroomPlague].ready then
    --    if not setSpell then setSpell = classtable.WildMushroomPlague end
    --end
end

function Unholy:Single()
    if (MaxDps:CheckSpellUsable(classtable.UnholyFrenzy, 'UnholyFrenzy')) and (timeInCombat >= 4) and cooldown[classtable.UnholyFrenzy].ready then
        MaxDps:GlowCooldown(classtable.UnholyFrenzy, true)
    end
    if (MaxDps:CheckSpellUsable(classtable.Outbreak, 'Outbreak')) and (debuff[classtable.FrostFeverDeBuff].remains <3 or debuff[classtable.BloodPlagueDeBuff].remains <3) and cooldown[classtable.Outbreak].ready then
        if not setSpell then setSpell = classtable.Outbreak end
    end
    if (MaxDps:CheckSpellUsable(classtable.UnholyBlight, 'UnholyBlight') and talents[classtable.UnholyBlight]) and ((talents[classtable.UnholyBlight] and true or false) and ( debuff[classtable.FrostFeverDeBuff].remains <3 or debuff[classtable.BloodPlagueDeBuff].remains <3 )) and cooldown[classtable.UnholyBlight].ready then
        if not setSpell then setSpell = classtable.UnholyBlight end
    end
    if (MaxDps:CheckSpellUsable(classtable.PlagueStrike, 'PlagueStrike')) and ( (UnitLevel('player') >= 68 and not debuff[classtable.BloodPlagueDeBuff].up or not debuff[classtable.FrostFeverDeBuff].up) or (not debuff[classtable.BloodPlagueDeBuff].up) ) and cooldown[classtable.PlagueStrike].ready then
        if not setSpell then setSpell = classtable.PlagueStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.IcyTouch, 'IcyTouch')) and (not debuff[classtable.FrostFeverDeBuff].up) and cooldown[classtable.IcyTouch].ready then
        if not setSpell then setSpell = classtable.IcyTouch end
    end
    if (MaxDps:CheckSpellUsable(classtable.PlagueLeech, 'PlagueLeech') and talents[classtable.PlagueLeech]) and (numDepletedRunes(true) >= 2 and DeathKnight:RuneTypeCount("Death") == 0) and ((talents[classtable.PlagueLeech] and true or false) and ( debuff[classtable.FrostFeverDeBuff].up or debuff[classtable.BloodPlagueDeBuff].up ) and ( cooldown[classtable.Outbreak].remains <1 )) and cooldown[classtable.PlagueLeech].ready then
        if not setSpell then setSpell = classtable.PlagueLeech end
    end
    if (MaxDps:CheckSpellUsable(classtable.SummonGargoyle, 'SummonGargoyle')) and cooldown[classtable.SummonGargoyle].ready then
        MaxDps:GlowCooldown(classtable.SummonGargoyle, cooldown[classtable.SummonGargoyle].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.DarkTransformation, 'DarkTransformation')) and (UnitExists ( 'pet' )) and cooldown[classtable.DarkTransformation].ready then
        MaxDps:GlowCooldown(classtable.DarkTransformation, cooldown[classtable.DarkTransformation].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.EmpowerRuneWeapon, 'EmpowerRuneWeapon')) and (ttd <= 60 and buff[classtable.MoguPowerPotionBuff].up) and cooldown[classtable.EmpowerRuneWeapon].ready then
        MaxDps:GlowCooldown(classtable.EmpowerRuneWeapon, cooldown[classtable.EmpowerRuneWeapon].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.ScourgeStrike, 'ScourgeStrike')) and (DeathKnight:RuneTypeCount("Unholy") == 2 and RunicPower <90) and cooldown[classtable.ScourgeStrike].ready then
        if not setSpell then setSpell = classtable.ScourgeStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.FesteringStrike, 'FesteringStrike')) and (DeathKnight:RuneTypeCount("Blood") == 2 and DeathKnight:RuneTypeCount("Frost") == 2 and RunicPower <90) and cooldown[classtable.FesteringStrike].ready then
        if not setSpell then setSpell = classtable.FesteringStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathCoil, 'DeathCoil')) and (RunicPower >90) and cooldown[classtable.DeathCoil].ready then
        if not setSpell then setSpell = classtable.DeathCoil end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathCoil, 'DeathCoil')) and (buff[classtable.SuddenDoomBuff].up) and cooldown[classtable.DeathCoil].ready then
        if not setSpell then setSpell = classtable.DeathCoil end
    end
    if (MaxDps:CheckSpellUsable(classtable.BloodTap, 'BloodTap') and talents[classtable.BloodTap]) and ((talents[classtable.BloodTap] and buff[classtable.BloodChargeBuff].count >=5  or false)) and cooldown[classtable.BloodTap].ready then
        MaxDps:GlowCooldown(classtable.BloodTap, cooldown[classtable.BloodTap].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.ScourgeStrike, 'ScourgeStrike')) and cooldown[classtable.ScourgeStrike].ready then
        if not setSpell then setSpell = classtable.ScourgeStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.FesteringStrike, 'FesteringStrike')) and cooldown[classtable.FesteringStrike].ready then
        if not setSpell then setSpell = classtable.FesteringStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathCoil, 'DeathCoil')) and (cooldown[classtable.SummonGargoyle].remains >8) and cooldown[classtable.DeathCoil].ready then
        if not setSpell then setSpell = classtable.DeathCoil end
    end
    if (MaxDps:CheckSpellUsable(classtable.HornofWinter, 'HornofWinter')) and cooldown[classtable.HornofWinter].ready then
        MaxDps:GlowCooldown(classtable.HornofWinter, cooldown[classtable.HornofWinter].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.EmpowerRuneWeapon, 'EmpowerRuneWeapon')) and cooldown[classtable.EmpowerRuneWeapon].ready then
        MaxDps:GlowCooldown(classtable.EmpowerRuneWeapon, cooldown[classtable.EmpowerRuneWeapon].ready)
    end
end

function Unholy:callaction()
    if (MaxDps:CheckSpellUsable(classtable.SoulReaper, 'SoulReaper')) and (targethealthPerc < 35) and cooldown[classtable.SoulReaper].ready then
        MaxDps:GlowCooldown(classtable.SoulReaper, cooldown[classtable.SoulReaper].ready)
    end
    if targets > 2 then
        Unholy:Aoe()
    end
    Unholy:Single()
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
    classtable.WoundSpender = IsSpellKnownOrOverridesKnown(classtable.ScourgeStrike) and C_Spell.GetSpellInfo(classtable.ScourgeStrike) and ( C_Spell.GetSpellInfo(C_Spell.GetSpellInfo(classtable.ScourgeStrike).name).spellID ) --55090
    classtable.FesteringScythe = 458128
    if buff[classtable.FesteringScytheBuff].up then
        classtable.FesteringStrike = classtable.FesteringScythe
    else
        classtable.FesteringStrike = 85948
    end
    --for spellId in pairs(MaxDps.Flags) do
    --    self.Flags[spellId] = false
    --    self:ClearGlowIndependent(spellId, spellId)
    --end

    classtable.Presence = 48265
    classtable.PresenceBuff = 48265

    classtable.BloodChargeBuff = 114851
    classtable.FrostFeverDeBuff = 55095
    classtable.BloodPlagueDeBuff = 55078
    classtable.MoguPowerPotionBuff = 447200
    classtable.SuddenDoomBuff = 81340
    classtable.HornofWinterBuff = 57330

    local function debugg()
        talents[classtable.UnholyBlight] = 1
        talents[classtable.PlagueLeech] = 1
        talents[classtable.BloodTap] = 1
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
