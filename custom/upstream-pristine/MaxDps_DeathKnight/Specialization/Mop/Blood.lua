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
local RunesPT = Enum.PowerType.Runes
local RunicPowerPT = Enum.PowerType.RunicPower

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
local RunicPower
local RunicPowerMax
local RunicPowerDeficit

local Blood = {}

function Blood:precombat()
    if (MaxDps:CheckSpellUsable(classtable.HornofWinter, 'HornofWinter')) and cooldown[classtable.HornofWinter].ready then
        MaxDps:GlowCooldown(classtable.HornofWinter, cooldown[classtable.HornofWinter].ready)
    end
end

local function ClearCDs()
    MaxDps:GlowCooldown(classtable.HornofWinter, false)
    MaxDps:GlowCooldown(classtable.DeathandDecay, false)
    MaxDps:GlowCooldown(classtable.BoneShield, false)
    MaxDps:GlowCooldown(classtable.VampiricBlood, false)
    MaxDps:GlowCooldown(classtable.RuneTap, false)
    MaxDps:GlowCooldown(classtable.DancingRuneWeapon, false)
    MaxDps:GlowCooldown(classtable.SoulReaper, false)
end

function Blood:single()
    -- Single Target Priority
    if (MaxDps:CheckSpellUsable(classtable.DeathStrike, 'DeathStrike')) and (DeathKnight:RuneTypeCount('Frost') >= 1 or DeathKnight:RuneTypeCount('Unholy') >= 1) and cooldown[classtable.DeathStrike].ready then
        if not setSpell then setSpell = classtable.DeathStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.Outbreak, 'Outbreak')) and (debuff[classtable.FrostFeverDeBuff].remains <3 or debuff[classtable.BloodPlagueDeBuff].remains <3) and cooldown[classtable.Outbreak].ready then
        if not setSpell then setSpell = classtable.Outbreak end
    end
    if (MaxDps:CheckSpellUsable(classtable.RuneStrike, 'RuneStrike')) and (RunicPower >= RunicPowerMax - 20) and cooldown[classtable.RuneStrike].ready then
        if not setSpell then setSpell = classtable.RuneStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathStrike, 'DeathStrike')) and (DeathKnight:RuneTypeCount('Death') >= 1 and healthPerc < 80) and cooldown[classtable.DeathStrike].ready then
        if not setSpell then setSpell = classtable.DeathStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.SoulReaper, 'SoulReaper')) and (RunicPower >= 10 and DeathKnight:RuneTypeCount('Blood') >= 1 and targethealthPerc < 35) and cooldown[classtable.SoulReaper].ready then
        MaxDps:GlowCooldown(classtable.SoulReaper, cooldown[classtable.SoulReaper].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.HeartStrike, 'HeartStrike')) and (DeathKnight:RuneTypeCount('Blood') >= 1  or targethealthPerc < 35) and cooldown[classtable.HeartStrike].ready then
        if not setSpell then setSpell = classtable.HeartStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathandDecay, 'DeathandDecay')) and (buff[classtable.CrimsonScourgeBuff].up and debuff[classtable.FrostFeverDeBuff].remains >15) and cooldown[classtable.DeathandDecay].ready then
        if not setSpell then setSpell = classtable.DeathandDecay end
    end
    if (MaxDps:CheckSpellUsable(classtable.BloodBoil, 'BloodBoil')) and (buff[classtable.CrimsonScourgeBuff].up and debuff[classtable.FrostFeverDeBuff].remains <15) and cooldown[classtable.BloodBoil].ready then
        if not setSpell then setSpell = classtable.BloodBoil end
    end
    if (MaxDps:CheckSpellUsable(classtable.HornofWinter, 'HornofWinter')) and cooldown[classtable.HornofWinter].ready then
        MaxDps:GlowCooldown(classtable.HornofWinter, cooldown[classtable.HornofWinter].ready)
    end
end

function Blood:aoe()
    if (MaxDps:CheckSpellUsable(classtable.DeathandDecay, 'DeathandDecay')) and cooldown[classtable.DeathandDecay].ready then
        if not setSpell then setSpell = classtable.DeathandDecay end
    end
    if (MaxDps:CheckSpellUsable(classtable.Outbreak, 'Outbreak')) and (debuff[classtable.FrostFeverDeBuff].remains <3 or debuff[classtable.BloodPlagueDeBuff].remains <3) and cooldown[classtable.Outbreak].ready then
        if not setSpell then setSpell = classtable.Outbreak end
    end
    if (MaxDps:CheckSpellUsable(classtable.Pestilence, 'Pestilence') and not talents[classtable.RoilingBlood]) and (debuff[classtable.FrostFeverDeBuff].remains >2 or debuff[classtable.BloodPlagueDeBuff].remains >2 and (MaxDps:DebuffCounter(classtable.FrostFeverDeBuff) < targets or MaxDps:DebuffCounter(classtable.BloodPlagueDeBuff) < targets) ) and cooldown[classtable.Pestilence].ready then
        if not setSpell then setSpell = classtable.Pestilence end
    end
    if (MaxDps:CheckSpellUsable(classtable.BloodBoil, 'BloodBoil')) and (buff[classtable.CrimsonScourgeBuff].up) and cooldown[classtable.BloodBoil].ready then
        if not setSpell then setSpell = classtable.BloodBoil end
    end
    if (MaxDps:CheckSpellUsable(classtable.BloodBoil, 'BloodBoil')) and cooldown[classtable.BloodBoil].ready then
        if not setSpell then setSpell = classtable.BloodBoil end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeathStrike, 'DeathStrike')) and (Runes > 0) and cooldown[classtable.DeathStrike].ready then
        if not setSpell then setSpell = classtable.DeathStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.RuneStrike, 'RuneStrike')) and (RunicPower >= RunicPowerMax - 20) and cooldown[classtable.RuneStrike].ready then
        if not setSpell then setSpell = classtable.RuneStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.HornofWinter, 'HornofWinter')) and cooldown[classtable.HornofWinter].ready then
        MaxDps:GlowCooldown(classtable.HornofWinter, cooldown[classtable.HornofWinter].ready)
    end
end

function Blood:callaction()
    MaxDps:GlowCooldown(classtable.VampiricBlood, cooldown[classtable.VampiricBlood].ready)
    MaxDps:GlowCooldown(classtable.RuneTap, cooldown[classtable.RuneTap].ready)
    MaxDps:GlowCooldown(classtable.DancingRuneWeapon, cooldown[classtable.DancingRuneWeapon].ready and ttd > 12)
    MaxDps:GlowCooldown(classtable.BoneShield, (cooldown[classtable.BoneShield].ready and (not buff[classtable.BoneShield].up or (buff[classtable.BoneShield].up and buff[classtable.BoneShield].remains < 3) or buff[classtable.BoneShield].count <= 1) ) )
    --if (MaxDps:CheckSpellUsable(classtable.BloodPresence, 'BloodPresence')) and (not MaxDps:FindBuffAuraData(classtable.BloodPresenceBuff).up) and cooldown[classtable.BloodPresence].ready then
    --    if not setSpell then setSpell = classtable.BloodPresence end
    --end
    if targets > 1 then
        Blood:aoe()
    end
    Blood:single()
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
    Runes = UnitPower('player', RunesPT)
    RunicPower = UnitPower('player', RunicPowerPT)
    RunicPowerMax = UnitPowerMax('player', RunicPowerPT)
    RunicPowerDeficit = RunicPowerMax - RunicPower
    classtable = MaxDps.SpellTable

    classtable.DeathStrike = 49998
    classtable.Outbreak = 77575
    classtable.RuneStrike = 56815
    classtable.HeartStrike = 55050
    classtable.DeathandDecay = 43265
    classtable.BloodBoil = 48721
    classtable.HornofWinter = 57330
    classtable.Pestilence = 50842
    classtable.FrostFeverDeBuff = 55095
    classtable.BloodPlagueDeBuff = 55078
    classtable.CrimsonScourgeBuff = 81141
    classtable.DeathRuneBuff = 54637
    classtable.BloodRuneBuff = 54638
    classtable.BloodPresenceBuff = 48263

    setSpell = nil
    ClearCDs()

    Blood:precombat()
    Blood:callaction()
    if setSpell then return setSpell end
end