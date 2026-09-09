
local _, addonTable = ...

--- @type MaxDps
if not MaxDps then return end

local Evoker = addonTable.Evoker
local MaxDps = MaxDps
local UnitPower = UnitPower
local UnitHealth = UnitHealth
local UnitAura = C_UnitAuras.GetAuraDataByIndex
local GetSpellDescription = GetSpellDescription
local GetSpellPowerCost = C_Spell.GetSpellPowerCost
local UnitHealthMax = UnitHealthMax
local UnitPowerMax = UnitPowerMax
local PowerTypeEssence = Enum.PowerType.Essence

local fd
local cooldown
local buff
local debuff
local talents
local targets
local essence
local targetHP
local targetmaxHP
local targethealthPerc
local curentHP
local maxHP
local healthPerc

local className, classFilename, classId = UnitClass('player')
local currentSpec = GetSpecialization()
local currentSpecName = currentSpec and select(2, GetSpecializationInfo(currentSpec)) or "None"
local classtable

--setmetatable(classtable, Warrior.spellMeta)

function Evoker:Augmentation()
    fd = MaxDps.FrameData
    cooldown = fd.cooldown
    buff = fd.buff
    debuff = fd.debuff
    talents = fd.talents
    targets = MaxDps:SmartAoe()
    essence = UnitPower('player', PowerTypeEssence)
    targetHP = UnitHealth('target')
    targetmaxHP = UnitHealthMax('target')
    targethealthPerc = (targetHP / targetmaxHP) * 100
    curentHP = UnitHealth('player')
    maxHP = UnitHealthMax('player')
    healthPerc = (curentHP / maxHP) * 100
    classtable = MaxDps.SpellTable
    classtable.AncientFlameBuff = 375583
    classtable.EssenceBurstBuff = 392268
    classtable.LeapingFlamesBuff = 370901

    if talents[classtable.TiptheScales] then
        MaxDps:GlowCooldown(classtable.TiptheScales, cooldown[classtable.TiptheScales].ready)
    end

    if targets > 1  then
        return Evoker:AugmentationMultiTarget()
    end
    return Evoker:AugmentationSingleTarget()
end

--optional abilities list

--Single-Target Rotation
function Evoker:AugmentationSingleTarget()
    --Use Prescience on the next targets, you would like to receive Ebon Might if you will cap on Prescience charges within the next 3 globals and use all charges if you will recast Ebon Might within the next 3 globals. Do not cast Prescience if you will overcap by more than 1 stack of Trembling Earth.
    if talents[classtable.Prescience] and cooldown[classtable.Prescience].charges >= 2 then
        return classtable.Prescience
    end
    --Use Ebon Might if Ebon Might's remaining duration is less than 5.5s. This is slightly longer than the pandemic duration, but this leads to more overall uptime by having more Ebon Might casts.
    if talents[classtable.EbonMight] and cooldown[classtable.EbonMight].ready then
        return classtable.EbonMight
    end
    --Cast Breath of Eons if Ebon Might is up or has less than 4 seconds remaining on its cooldown.
    if talents[classtable.BreathofEons] and talents[classtable.EbonMight] and cooldown[classtable.EbonMight].duration >= 5 and cooldown[classtable.BreathofEons].ready then
        return classtable.BreathofEons
    end
    --Cast Living Flame if you have Leaping Flames and the cooldown of Fire Breath is up.
    if buff[classtable.LeapingFlamesBuff].up and cooldown[classtable.FireBreath].ready and cooldown[classtable.LivingFlame].ready then
        return classtable.LivingFlame
    end
    --Cast Fire Breath at empower rank 4 if Ebon Might is up.
    if talents[classtable.EbonMight] and cooldown[classtable.EbonMight].duration >= 5 and cooldown[classtable.FireBreath].ready then
        return classtable.FireBreath
    end
    --Cast Upheaval at empower rank 1 if Ebon Might is up.
    if talents[classtable.EbonMight] and cooldown[classtable.EbonMight].duration >= 5 and cooldown[classtable.Upheaval].ready then
        return classtable.Upheaval
    end
    --Use Eruption if Ebon Might is up or you are capped on Essence or Essence Burst.t.
    if talents[classtable.EbonMight] and (cooldown[classtable.EbonMight].duration >= 5 or essence == 5 or (talents[classtable.EssenceBurst] and buff[classtable.EssenceBurstBuff].up)) and cooldown[classtable.Eruption].ready then
        return classtable.Eruption
    end
    --Cast Verdant Embrace if Ebon Might is down and you do not have Ancient Flame
    if talents[classtable.VerdantEmbrace] and not cooldown[classtable.EbonMight].ready and talents[classtable.AncientFlame] and not buff[classtable.AncientFlameBuff].up and cooldown[classtable.VerdantEmbrace].ready then
        return classtable.VerdantEmbrace
    end
    --Cast Living Flame.
    if cooldown[classtable.LivingFlame].ready then
        return classtable.LivingFlame
    end
    --If you need to move while casting, use Hover. If you have no charges of Hover left, then use Azure Strike.
    if talents[classtable.AzureStrike] and cooldown[classtable.AzureStrike].ready then
        return classtable.AzureStrike
    end
end

--Multiple-Target Rotation
function Evoker:AugmentationMultiTarget()
    --Use Prescience on the next targets, you would like to receive Ebon Might if you will cap on Prescience charges within the next 3 globals and use all charges if you will recast Ebon Might within the next 3 globals. Do not cast Prescience if you will overcap by more than 1 stack of Trembling Earth.
    if talents[classtable.Prescience] and cooldown[classtable.Prescience].charges >= 2 then
        return classtable.Prescience
    end
    --Use Ebon Might if Ebon Might's remaining duration is less than 5.5s. This is slightly longer than the pandemic duration, but this leads to more overall uptime by having more Ebon Might casts.
    if talents[classtable.EbonMight] and cooldown[classtable.EbonMight].ready then
        return classtable.EbonMight
    end
    --Cast Breath of Eons if Ebon Might is up or has less than 4 seconds remaining on its cooldown.
    if talents[classtable.BreathofEons] and talents[classtable.EbonMight] and cooldown[classtable.EbonMight].duration >= 5 and cooldown[classtable.BreathofEons].ready then
        return classtable.BreathofEons
    end
    --Cast Living Flame if you have Leaping Flames and the cooldown of Fire Breath is up.
    if buff[classtable.LeapingFlamesBuff].up and cooldown[classtable.FireBreath].ready and cooldown[classtable.LivingFlame].ready then
        return classtable.LivingFlame
    end
    --Cast Fire Breath at empower rank 4 if Ebon Might is up.
    if talents[classtable.EbonMight] and cooldown[classtable.EbonMight].duration >= 5 and cooldown[classtable.FireBreath].ready then
        return classtable.FireBreath
    end
    --Cast Upheaval at empower rank 1 if Ebon Might is up.
    if talents[classtable.EbonMight] and cooldown[classtable.EbonMight].duration >= 5 and cooldown[classtable.Upheaval].ready then
        return classtable.Upheaval
    end
    --Use Eruption if Ebon Might is up or you are capped on Essence or Essence Burst.t.
    if talents[classtable.EbonMight] and (cooldown[classtable.EbonMight].duration >= 5 or essence == 5 or (talents[classtable.EssenceBurst] and buff[classtable.EssenceBurstBuff].up)) and cooldown[classtable.Eruption].ready then
        return classtable.Eruption
    end
    --Cast Verdant Embrace if Ebon Might is down and you do not have Ancient Flame
    if talents[classtable.VerdantEmbrace] and not cooldown[classtable.EbonMight].ready and talents[classtable.AncientFlame] and not buff[classtable.AncientFlameBuff].up and cooldown[classtable.VerdantEmbrace].ready then
        return classtable.VerdantEmbrace
    end
    --Cast Living Flame.
    if cooldown[classtable.LivingFlame].ready then
        return classtable.LivingFlame
    end
    --If you need to move while casting, use Hover. If you have no charges of Hover left, then use Azure Strike.
    if talents[classtable.AzureStrike] and cooldown[classtable.AzureStrike].ready then
        return classtable.AzureStrike
    end
end
