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


function Frost:precombat()
    --if (MaxDps:CheckSpellUsable(classtable.Presence, 'Presence')) and (not MaxDps:FindBuffAuraData(classtable.PresenceBuff).up) and cooldown[classtable.Presence].ready then
    --    if not setSpell then setSpell = classtable.Presence end
    --end
    if (MaxDps:CheckSpellUsable(classtable.HornofWinter, 'HornofWinter')) and (not buff[classtable.HornofWinterBuff].up) and cooldown[classtable.HornofWinter].ready then
        MaxDps:GlowCooldown(classtable.HornofWinter, cooldown[classtable.HornofWinter].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.ArmyoftheDead, 'ArmyoftheDead')) and cooldown[classtable.ArmyoftheDead].ready then
        MaxDps:GlowCooldown(classtable.ArmyoftheDead, cooldown[classtable.ArmyoftheDead].ready)
    end
end


local function ClearCDs()
    MaxDps:GlowCooldown(classtable.HornofWinter, false)
    MaxDps:GlowCooldown(classtable.ArmyoftheDead, false)
    MaxDps:GlowCooldown(classtable.PillarofFrost, false)
    MaxDps:GlowCooldown(classtable.RaiseDead, false)
    MaxDps:GlowCooldown(classtable.EmpowerRuneWeapon, false)
    MaxDps:GlowCooldown(classtable.BloodTap, false)
    MaxDps:GlowCooldown(classtable.SoulReaper, false)
end

function Frost:Aoe()
    -- Howling Blast
    if MaxDps:CheckSpellUsable(classtable.HowlingBlast, 'HowlingBlast') and cooldown[classtable.HowlingBlast].ready then
        if not setSpell then setSpell = classtable.HowlingBlast end
    end

    -- Death and Decay
    if MaxDps:CheckSpellUsable(classtable.DeathandDecay, 'DeathandDecay') and cooldown[classtable.DeathandDecay].ready then
        if not setSpell then setSpell = classtable.DeathandDecay end
    end

    -- Pillar of Frost
    if MaxDps:CheckSpellUsable(classtable.PillarofFrost, 'PillarofFrost') and cooldown[classtable.PillarofFrost].ready then
        MaxDps:GlowCooldown(classtable.PillarofFrost, true)
    end

    -- Wild Mushroom: Plague (only if Symbiosis from Druid)
    --if MaxDps:CheckSpellUsable(classtable.WildMushroomPlague, 'WildMushroomPlague') and buff[classtable.SymbiosisBuff] and cooldown[classtable.WildMushroomPlague].ready then
    --    if not setSpell then setSpell = classtable.WildMushroomPlague end
    --end

    -- Frost Strike
    if MaxDps:CheckSpellUsable(classtable.FrostStrike, 'FrostStrike') and cooldown[classtable.FrostStrike].ready then
        if not setSpell then setSpell = classtable.FrostStrike end
    end

    -- Plague Strike (for longer-living targets)
    if MaxDps:CheckSpellUsable(classtable.PlagueStrike, 'PlagueStrike') and ttd > 10 and cooldown[classtable.PlagueStrike].ready then
        if not setSpell then setSpell = classtable.PlagueStrike end
    end

    -- Pestilence (for disease spreading on long-lived packs)
    if MaxDps:CheckSpellUsable(classtable.Pestilence, 'Pestilence') and ttd > 10 and cooldown[classtable.Pestilence].ready then
        if not setSpell then setSpell = classtable.Pestilence end
    end

    -- Obliterate
    if MaxDps:CheckSpellUsable(classtable.Obliterate, 'Obliterate') and cooldown[classtable.Obliterate].ready then
        if not setSpell then setSpell = classtable.Obliterate end
    end

    -- Horn of Winter as filler
    if MaxDps:CheckSpellUsable(classtable.HornofWinter, 'HornofWinter') and cooldown[classtable.HornofWinter].ready then
        if not setSpell then setSpell = classtable.HornofWinter end
    end
end

function Frost:Single()
    -- Execute Phase: Soul Reaper on cooldown if target will reach 35% HP within 5s
    if (MaxDps:CheckSpellUsable(classtable.SoulReaper, 'SoulReaper')) and (targethealthPerc < 40) and cooldown[classtable.SoulReaper].ready then
        MaxDps:GlowCooldown(classtable.SoulReaper, cooldown[classtable.SoulReaper].ready)
    end

    -- Apply diseases: Outbreak or Plague Strike if missing
    if (MaxDps:CheckSpellUsable(classtable.Outbreak, 'Outbreak')) and (debuff[classtable.BloodPlagueDeBuff].remains < 3) and cooldown[classtable.Outbreak].ready then
        if not setSpell then setSpell = classtable.Outbreak end
    end
    if (MaxDps:CheckSpellUsable(classtable.PlagueStrike, 'PlagueStrike')) and (not debuff[classtable.BloodPlagueDeBuff].up) and cooldown[classtable.PlagueStrike].ready then
        if not setSpell then setSpell = classtable.PlagueStrike end
    end

    -- Frost Strike: with Killing Machine
    if (MaxDps:CheckSpellUsable(classtable.FrostStrike, 'FrostStrike')) and buff[classtable.KillingMachineBuff].up and cooldown[classtable.FrostStrike].ready then
        if not setSpell then setSpell = classtable.FrostStrike end
    end

    -- Pillar of Frost with other cooldowns
    if (MaxDps:CheckSpellUsable(classtable.PillarofFrost, 'PillarofFrost')) and cooldown[classtable.PillarofFrost].ready then
        MaxDps:GlowCooldown(classtable.PillarofFrost, cooldown[classtable.PillarofFrost].ready)
    end

    -- Obliterate: no Killing Machine, free Unholy runes, diseases won't expire
    if (MaxDps:CheckSpellUsable(classtable.Obliterate, 'Obliterate')) and (not buff[classtable.KillingMachineBuff].up) and (DeathKnight:RuneTypeCount("Unholy", true) >= 1) and (debuff[classtable.BloodPlagueDeBuff].remains > 3) and cooldown[classtable.Obliterate].ready then
        if not setSpell then setSpell = classtable.Obliterate end
    end

    -- Howling Blast: Rime proc or Death/Frost rune available
    if (MaxDps:CheckSpellUsable(classtable.HowlingBlast, 'HowlingBlast')) and (buff[classtable.RimeBuff].up or DeathKnight:RuneTypeCount("Frost") >= 1 or DeathKnight:RuneTypeCount("Death") >= 1) and cooldown[classtable.HowlingBlast].ready then
        if not setSpell then setSpell = classtable.HowlingBlast end
    end

    -- Frost Strike: fallback when no runes or high Runic Power
    if (MaxDps:CheckSpellUsable(classtable.FrostStrike, 'FrostStrike')) and (DeathKnight:Runes() == 0 or RunicPower >= 40) and cooldown[classtable.FrostStrike].ready then
        if not setSpell then setSpell = classtable.FrostStrike end
    end

    -- Blood Tap: no runes or overcapping stacks
    if (MaxDps:CheckSpellUsable(classtable.BloodTap, 'BloodTap') and talents[classtable.BloodTap]) and ((DeathKnight:Runes() == 0) or (buff[classtable.BloodChargeBuff].count >= 5)) and cooldown[classtable.BloodTap].ready then
        MaxDps:GlowCooldown(classtable.BloodTap, cooldown[classtable.BloodTap].ready)
    end

    -- Plague Leech: use when Outbreak is ready or Rime proc + Blood Plague low
    if (MaxDps:CheckSpellUsable(classtable.PlagueLeech, 'PlagueLeech') and talents[classtable.PlagueLeech]) and ((cooldown[classtable.Outbreak].remains < 1) or (buff[classtable.RimeBuff].up and debuff[classtable.BloodPlagueDeBuff].remains < 3)) and cooldown[classtable.PlagueLeech].ready then
        if not setSpell then setSpell = classtable.PlagueLeech end
    end

    -- Horn of Winter: filler
    if (MaxDps:CheckSpellUsable(classtable.HornofWinter, 'HornofWinter')) and cooldown[classtable.HornofWinter].ready then
        MaxDps:GlowCooldown(classtable.HornofWinter, cooldown[classtable.HornofWinter].ready)
    end
end

function Frost:callaction()
    if targets > 2 then
        Frost:Aoe()
    end
    Frost:Single()
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
    --Mana = UnitPower('player', ManaPT)
    --ManaMax = UnitPowerMax('player', ManaPT)
    --ManaDeficit = ManaMax - Mana
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

    classtable.FrostStrike = 49143
    classtable.Presence = 48266
    classtable.PresenceBuff = 48266

    classtable.FrostFeverDeBuff = 55095
    classtable.BloodPlagueDeBuff = 55078
    classtable.RimeBuff = 59052
    classtable.KillingMachineBuff = 51124
    classtable.MoguPowerPotionBuff = 447200
    classtable.BloodChargeBuff = 114851
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

    Frost:precombat()

    Frost:callaction()
    if setSpell then return setSpell end
end
