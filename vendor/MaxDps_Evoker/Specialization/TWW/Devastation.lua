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

local Devastation = {}

local trinket_1_buffs = false
local trinket_2_buffs = false
local weapon_buffs = false
local weapon_sync = false
local weapon_stat_value = false
local trinket_1_sync = false
local trinket_2_sync = false
local trinket_1_manual = false
local trinket_2_manual = false
local trinket_1_ogcd_cast = false
local trinket_2_ogcd_cast = false
local trinket_1_exclude = false
local trinket_2_exclude = false
local trinket_priority = false
local damage_trinket_priority = false
local r1_cast_time = false
local dr_prep_time = 6
local dr_prep_time_aoe = 4
local can_extend_dr = false
local has_external_pi = false
local can_use_empower = true
local next_dragonrage = 0
local pool_for_id = false
function Devastation:precombat()
    weapon_buffs = MaxDps:CheckEquipped('Bestinslots')
    if MaxDps:CheckEquipped('Bestinslots') then
        weapon_sync = 1
    else
        weapon_sync = 0.5
    end
    weapon_stat_value = MaxDps:CheckEquipped('Bestinslots') and 1 or 0 * 5142 * 15
    r1_cast_time = 1.0 * SpellHaste
    dr_prep_time = 6
    dr_prep_time_aoe = 4
    can_extend_dr = false
    has_external_pi = cooldown[classtable.InvokePowerInfusion0].duration >0
    if not talents[classtable.Animosity] or not talents[classtable.Dragonrage] then
        can_use_empower = true
    end
    if (MaxDps:CheckSpellUsable(classtable.VerdantEmbrace, 'VerdantEmbrace')) and (talents[classtable.ScarletAdaptation]) and cooldown[classtable.VerdantEmbrace].ready and not UnitAffectingCombat('player') then
        MaxDps:GlowCooldown(classtable.VerdantEmbrace, cooldown[classtable.VerdantEmbrace].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.Hover, 'Hover')) and (talents[classtable.Slipstream]) and cooldown[classtable.Hover].ready and not UnitAffectingCombat('player') then
        MaxDps:GlowCooldown(classtable.Hover, cooldown[classtable.Hover].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.Hover, 'Hover')) and (talents[classtable.Slipstream]) and cooldown[classtable.Hover].ready and not UnitAffectingCombat('player') then
        MaxDps:GlowCooldown(classtable.Hover, cooldown[classtable.Hover].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.Firestorm, 'Firestorm') and talents[classtable.Firestorm]) and (talents[classtable.Firestorm] and ( not talents[classtable.Engulf] or not talents[classtable.RubyEmbers] )) and cooldown[classtable.Firestorm].ready and not UnitAffectingCombat('player') then
        if not setSpell then setSpell = classtable.Firestorm end
    end
    if (MaxDps:CheckSpellUsable(classtable.LivingFlame, 'LivingFlame')) and (not talents[classtable.Firestorm] or talents[classtable.Engulf] and talents[classtable.RubyEmbers]) and cooldown[classtable.LivingFlame].ready and not UnitAffectingCombat('player') then
        if not setSpell then setSpell = classtable.LivingFlame end
    end
end
function Devastation:aoe()
    if (MaxDps:CheckSpellUsable(classtable.ShatteringStar, 'ShatteringStar')) and (( cooldown[classtable.Dragonrage].ready and talents[classtable.ArcaneVigor] or talents[classtable.EternitysSpan] and targets <= 3 ) and not talents[classtable.Engulf]) and cooldown[classtable.ShatteringStar].ready then
        if not setSpell then setSpell = classtable.ShatteringStar end
    end
    if (MaxDps:CheckSpellUsable(classtable.Hover, 'Hover')) and (math.huge <6 and not buff[classtable.HoverBuff].up and gcd >= 0.5 and ( buff[classtable.MassDisintegrateStacksBuff].up and talents[classtable.MassDisintegrate] or targets <= 4 )) and cooldown[classtable.Hover].ready then
        MaxDps:GlowCooldown(classtable.Hover, cooldown[classtable.Hover].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.Firestorm, 'Firestorm') and talents[classtable.Firestorm]) and (buff[classtable.SnapfireBuff].up and not talents[classtable.FeedtheFlames]) and cooldown[classtable.Firestorm].ready then
        if not setSpell then setSpell = classtable.Firestorm end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeepBreath, 'DeepBreath')) and (talents[classtable.Maneuverability] and talents[classtable.MeltArmor] and not cooldown[classtable.FireBreath].ready and not cooldown[classtable.EternitySurge].ready or talents[classtable.FeedtheFlames] and talents[classtable.Engulf] and talents[classtable.ImminentDestruction]) and cooldown[classtable.DeepBreath].ready then
        MaxDps:GlowCooldown(classtable.DeepBreath, cooldown[classtable.DeepBreath].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.Firestorm, 'Firestorm') and talents[classtable.Firestorm]) and (talents[classtable.FeedtheFlames] and ( not talents[classtable.Engulf] or cooldown[classtable.Engulf].remains >4 or cooldown[classtable.Engulf].charges == 0 or ( next_dragonrage <= cooldown[classtable.Firestorm].remains * 1.2 or not talents[classtable.Dragonrage] ) )) and cooldown[classtable.Firestorm].ready then
        if not setSpell then setSpell = classtable.Firestorm end
    end
    if (talents[classtable.Dragonrage] and cooldown[classtable.Dragonrage].ready and ( talents[classtable.Iridescence] or talents[classtable.ScorchingEmbers] ) and not talents[classtable.Engulf]) then
        Devastation:fb()
    end
    if (MaxDps:CheckSpellUsable(classtable.TiptheScales, 'TiptheScales')) and (( not talents[classtable.Dragonrage] or buff[classtable.DragonrageBuff].up ) and ( cooldown[classtable.FireBreath].remains <= cooldown[classtable.EternitySurge].remains or ( cooldown[classtable.EternitySurge].remains <= cooldown[classtable.FireBreath].remains and talents[classtable.FontofMagic] ) and not talents[classtable.Engulf] )) and cooldown[classtable.TiptheScales].ready then
        MaxDps:GlowCooldown(classtable.TiptheScales, cooldown[classtable.TiptheScales].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.ShatteringStar, 'ShatteringStar')) and (( cooldown[classtable.Dragonrage].ready and talents[classtable.ArcaneVigor] or talents[classtable.EternitysSpan] and targets <= 3 ) and talents[classtable.Engulf]) and cooldown[classtable.ShatteringStar].ready then
        if not setSpell then setSpell = classtable.ShatteringStar end
    end
    if (MaxDps:CheckSpellUsable(classtable.Dragonrage, 'Dragonrage') and talents[classtable.Dragonrage]) and (ttd >= 32 or targets >= 3 and ttd >= 15 or ttd <30) and cooldown[classtable.Dragonrage].ready then
        MaxDps:GlowCooldown(classtable.Dragonrage, cooldown[classtable.Dragonrage].ready)
    end
    if (( not talents[classtable.Dragonrage] or buff[classtable.DragonrageBuff].up or cooldown[classtable.Dragonrage].remains >dr_prep_time_aoe or not talents[classtable.Animosity] or talents[classtable.FlameSiphon] ) and ( ttd >= 8 or talents[classtable.MassDisintegrate] )) then
        Devastation:fb()
    end
    if (( not talents[classtable.Dragonrage] or buff[classtable.DragonrageBuff].up or cooldown[classtable.Dragonrage].remains >dr_prep_time_aoe or not talents[classtable.Animosity] ) and ( not buff[classtable.JackpotBuff].up or not (MaxDps.tier and MaxDps.tier[33].count >= 4) or talents[classtable.MassDisintegrate] )) then
        Devastation:es()
    end
    if (MaxDps:CheckSpellUsable(classtable.DeepBreath, 'DeepBreath')) and (not buff[classtable.DragonrageBuff].up and EssenceDeficit >3) and cooldown[classtable.DeepBreath].ready then
        MaxDps:GlowCooldown(classtable.DeepBreath, cooldown[classtable.DeepBreath].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.ShatteringStar, 'ShatteringStar')) and (( buff[classtable.EssenceBurstBuff].count <1 and talents[classtable.ArcaneVigor] or talents[classtable.EternitysSpan] and targets <= 3 or (MaxDps.tier and MaxDps.tier[33].count >= 4) and buff[classtable.JackpotBuff].count <2 ) and ( not talents[classtable.Engulf] or cooldown[classtable.Engulf].remains <4 or cooldown[classtable.Engulf].charges >0 )) and cooldown[classtable.ShatteringStar].ready then
        if not setSpell then setSpell = classtable.ShatteringStar end
    end
    if (MaxDps:CheckSpellUsable(classtable.Engulf, 'Engulf') and talents[classtable.Engulf]) and (( debuff[classtable.FireBreathDamageDeBuff].remains >= 1 + 2 * ( (cooldown[classtable.EngulfDamage].duration - cooldown[classtable.EngulfDamage].remains <1) and 1 or 0) ) and ( next_dragonrage >= cooldown[classtable.Engulf].remains * 1.2 or not talents[classtable.Dragonrage] )) and cooldown[classtable.Engulf].ready then
        MaxDps:GlowCooldown(classtable.Engulf, cooldown[classtable.Engulf].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.Pyre, 'Pyre')) and (buff[classtable.ChargedBlastBuff].count >= 12 and ( cooldown[classtable.Dragonrage].remains >gcd * 4 or not talents[classtable.Dragonrage] )) and cooldown[classtable.Pyre].ready then
        if not setSpell then setSpell = classtable.Pyre end
    end
    if (MaxDps:CheckSpellUsable(classtable.Disintegrate, 'Disintegrate')) and (buff[classtable.MassDisintegrateStacksBuff].up and talents[classtable.MassDisintegrate] and ( not pool_for_id or buff[classtable.MassDisintegrateStacksBuff].remains <= buff[classtable.MassDisintegrateStacksBuff].count * ( ( classtable and classtable.Disintegrate and GetSpellInfo(classtable.Disintegrate).castTime /1000 or 0) + 0.1 ) )) and cooldown[classtable.Disintegrate].ready then
        if not setSpell then setSpell = classtable.Disintegrate end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeepBreath, 'DeepBreath')) and (talents[classtable.ImminentDestruction] and not buff[classtable.EssenceBurstBuff].up) and cooldown[classtable.DeepBreath].ready then
        MaxDps:GlowCooldown(classtable.DeepBreath, cooldown[classtable.DeepBreath].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.Pyre, 'Pyre')) and (( targets >= 4 - ( buff[classtable.ImminentDestructionBuff].up and 1 or 0) or talents[classtable.Volatility] or talents[classtable.ScorchingEmbers] and MaxDps:DebuffCounter(classtable.FireBreathDamageDeBuff) >= targets * 0.75 ) and ( cooldown[classtable.Dragonrage].remains >gcd * 4 or not talents[classtable.Dragonrage] or not talents[classtable.ChargedBlast] ) and not pool_for_id and ( not buff[classtable.MassDisintegrateStacksBuff].up or buff[classtable.EssenceBurstBuff].count == 2 or buff[classtable.EssenceBurstBuff].count == 1 and Essence >= ( 3 - buff[classtable.ImminentDestructionBuff].duration ) or Essence >= ( 5 - buff[classtable.ImminentDestructionBuff].upMath * 2 ) )) and cooldown[classtable.Pyre].ready then
        if not setSpell then setSpell = classtable.Pyre end
    end
    if (MaxDps:CheckSpellUsable(classtable.LivingFlame, 'LivingFlame')) and (( not talents[classtable.Burnout] or buff[classtable.BurnoutBuff].up or cooldown[classtable.FireBreath].remains <= gcd * 5 or buff[classtable.ScarletAdaptationBuff].up or buff[classtable.AncientFlameBuff].up ) and buff[classtable.LeapingFlamesBuff].up and ( not buff[classtable.EssenceBurstBuff].up and EssenceDeficit >1 or cooldown[classtable.FireBreath].remains <= gcd * 3 and buff[classtable.EssenceBurstBuff].count <1 )) and cooldown[classtable.LivingFlame].ready then
        if not setSpell then setSpell = classtable.LivingFlame end
    end
    if (MaxDps:CheckSpellUsable(classtable.Disintegrate, 'Disintegrate')) and (( math.huge >2 or buff[classtable.HoverBuff].up ) and not pool_for_id and ( targets <= 4 or buff[classtable.MassDisintegrateStacksBuff].up )) and cooldown[classtable.Disintegrate].ready then
        if not setSpell then setSpell = classtable.Disintegrate end
    end
    if (MaxDps:CheckSpellUsable(classtable.LivingFlame, 'LivingFlame')) and (talents[classtable.Snapfire] and buff[classtable.BurnoutBuff].up) and cooldown[classtable.LivingFlame].ready then
        if not setSpell then setSpell = classtable.LivingFlame end
    end
    if (MaxDps:CheckSpellUsable(classtable.Firestorm, 'Firestorm') and talents[classtable.Firestorm]) and cooldown[classtable.Firestorm].ready then
        if not setSpell then setSpell = classtable.Firestorm end
    end
    if (MaxDps:CheckSpellUsable(classtable.LivingFlame, 'LivingFlame')) and (talents[classtable.Snapfire] and not talents[classtable.EngulfingBlaze]) and cooldown[classtable.LivingFlame].ready then
        if not setSpell then setSpell = classtable.LivingFlame end
    end
    if (MaxDps:CheckSpellUsable(classtable.AzureStrike, 'AzureStrike')) and (max(targethealthPerc)) and cooldown[classtable.AzureStrike].ready then
        if not setSpell then setSpell = classtable.AzureStrike end
    end
end
function Devastation:es()
    if (MaxDps:CheckSpellUsable(classtable.EternitySurge, 'EternitySurge')) and (targets <= 1 + (talents[classtable.EternitysSpan] and talents[classtable.EternitysSpan] or 0) or ( can_extend_dr and talents[classtable.Animosity] or talents[classtable.MassDisintegrate] ) and targets >( 3 + (talents[classtable.FontofMagic] and talents[classtable.FontofMagic] or 0) + 4 * (talents[classtable.EternitysSpan] and talents[classtable.EternitysSpan] or 0) ) or buff[classtable.DragonrageBuff].remains <1.75 * SpellHaste and buff[classtable.DragonrageBuff].remains >= 1 * SpellHaste and talents[classtable.Animosity] and can_extend_dr) and cooldown[classtable.EternitySurge].ready then
        if not setSpell then setSpell = classtable.EternitySurge end
        MaxDps.FrameData.empowerLevel[classtable.EternitySurge] = 1
    end
    if (MaxDps:CheckSpellUsable(classtable.EternitySurge, 'EternitySurge')) and (targets <= 2 + 2 * (talents[classtable.EternitysSpan] and talents[classtable.EternitysSpan] or 0) or buff[classtable.DragonrageBuff].remains <2.5 * SpellHaste and buff[classtable.DragonrageBuff].remains >= 1.75 * SpellHaste and talents[classtable.Animosity] and can_extend_dr) and cooldown[classtable.EternitySurge].ready then
        if not setSpell then setSpell = classtable.EternitySurge end
        MaxDps.FrameData.empowerLevel[classtable.EternitySurge] = 2
    end
    if (MaxDps:CheckSpellUsable(classtable.EternitySurge, 'EternitySurge')) and (targets <= 3 + 3 * (talents[classtable.EternitysSpan] and talents[classtable.EternitysSpan] or 0) or not talents[classtable.FontofMagic] and talents[classtable.MassDisintegrate] or buff[classtable.DragonrageBuff].remains <= 3.25 * SpellHaste and buff[classtable.DragonrageBuff].remains >= 2.5 * SpellHaste and talents[classtable.Animosity] and can_extend_dr) and cooldown[classtable.EternitySurge].ready then
        if not setSpell then setSpell = classtable.EternitySurge end
        MaxDps.FrameData.empowerLevel[classtable.EternitySurge] = 3
    end
    if (MaxDps:CheckSpellUsable(classtable.EternitySurge, 'EternitySurge')) and (talents[classtable.MassDisintegrate] or targets <= 4 + 4 * (talents[classtable.EternitysSpan] and talents[classtable.EternitysSpan] or 0)) and cooldown[classtable.EternitySurge].ready then
        if not setSpell then setSpell = classtable.EternitySurge end
        MaxDps.FrameData.empowerLevel[classtable.EternitySurge]  = 4
    end
end
function Devastation:fb()
    if (MaxDps:CheckSpellUsable(classtable.FireBreath, 'FireBreath')) and (talents[classtable.ScorchingEmbers] and ( cooldown[classtable.Engulf].remains <= ( classtable and classtable.FireBreath and GetSpellInfo(classtable.FireBreath).castTime /1000 or 0) + 0.5 or cooldown[classtable.Engulf].ready ) and talents[classtable.Engulf] and 14 <= ttd) and cooldown[classtable.FireBreath].ready then
        if not setSpell then setSpell = classtable.FireBreath end
        MaxDps.FrameData.empowerLevel[classtable.FireBreath]  = 2
    end
    if (MaxDps:CheckSpellUsable(classtable.FireBreath, 'FireBreath')) and (talents[classtable.ScorchingEmbers] and ( cooldown[classtable.Engulf].remains <= ( classtable and classtable.FireBreath and GetSpellInfo(classtable.FireBreath).castTime /1000 or 0) + 0.5 or cooldown[classtable.Engulf].ready ) and talents[classtable.Engulf] and ( 8 <= ttd or not talents[classtable.FontofMagic] )) and cooldown[classtable.FireBreath].ready then
        if not setSpell then setSpell = classtable.FireBreath end
        MaxDps.FrameData.empowerLevel[classtable.FireBreath]  = 3
    end
    if (MaxDps:CheckSpellUsable(classtable.FireBreath, 'FireBreath')) and (talents[classtable.ScorchingEmbers] and ( cooldown[classtable.Engulf].remains <= ( classtable and classtable.FireBreath and GetSpellInfo(classtable.FireBreath).castTime /1000 or 0) + 0.5 or cooldown[classtable.Engulf].ready ) and talents[classtable.Engulf] and talents[classtable.FontofMagic]) and cooldown[classtable.FireBreath].ready then
        if not setSpell then setSpell = classtable.FireBreath end
        MaxDps.FrameData.empowerLevel[classtable.FireBreath]  = 4
    end
    if (MaxDps:CheckSpellUsable(classtable.FireBreath, 'FireBreath')) and (( ( buff[classtable.DragonrageBuff].remains <1.75 * SpellHaste and buff[classtable.DragonrageBuff].remains >= 1 * SpellHaste ) and talents[classtable.Animosity] and can_extend_dr or targets == 1 ) and 20 <= ttd) and cooldown[classtable.FireBreath].ready then
        if not setSpell then setSpell = classtable.FireBreath end
        MaxDps.FrameData.empowerLevel[classtable.FireBreath]  = 1
    end
    if (MaxDps:CheckSpellUsable(classtable.FireBreath, 'FireBreath')) and (( ( buff[classtable.DragonrageBuff].remains <2.5 * SpellHaste and buff[classtable.DragonrageBuff].remains >= 1.75 * SpellHaste ) and talents[classtable.Animosity] and can_extend_dr or talents[classtable.ScorchingEmbers] or targets >= 2 ) and 14 <= ttd) and cooldown[classtable.FireBreath].ready then
        if not setSpell then setSpell = classtable.FireBreath end
        MaxDps.FrameData.empowerLevel[classtable.FireBreath]  = 2
    end
    if (MaxDps:CheckSpellUsable(classtable.FireBreath, 'FireBreath')) and (not talents[classtable.FontofMagic] or ( ( buff[classtable.DragonrageBuff].remains <= 3.25 * SpellHaste and buff[classtable.DragonrageBuff].remains >= 2.5 * SpellHaste ) and talents[classtable.Animosity] and can_extend_dr or talents[classtable.ScorchingEmbers] ) and 8 <= ttd) and cooldown[classtable.FireBreath].ready then
        if not setSpell then setSpell = classtable.FireBreath end
        MaxDps.FrameData.empowerLevel[classtable.FireBreath]  = 3
    end
    if (MaxDps:CheckSpellUsable(classtable.FireBreath, 'FireBreath')) and (max(targethealthPerc)) and cooldown[classtable.FireBreath].ready then
        if not setSpell then setSpell = classtable.FireBreath end
        MaxDps.FrameData.empowerLevel[classtable.FireBreath]  = 4
    end
end
function Devastation:green()
    if (MaxDps:CheckSpellUsable(classtable.EmeraldBlossom, 'EmeraldBlossom')) and cooldown[classtable.EmeraldBlossom].ready then
        MaxDps:GlowCooldown(classtable.EmeraldBlossom, cooldown[classtable.EmeraldBlossom].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.VerdantEmbrace, 'VerdantEmbrace')) and cooldown[classtable.VerdantEmbrace].ready then
        MaxDps:GlowCooldown(classtable.VerdantEmbrace, cooldown[classtable.VerdantEmbrace].ready)
    end
end
function Devastation:st()
    if (MaxDps:CheckSpellUsable(classtable.Dragonrage, 'Dragonrage') and talents[classtable.Dragonrage]) and cooldown[classtable.Dragonrage].ready then
        MaxDps:GlowCooldown(classtable.Dragonrage, cooldown[classtable.Dragonrage].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.Hover, 'Hover')) and (math.huge <6 and not buff[classtable.HoverBuff].up and gcd >= 0.5 or talents[classtable.Slipstream] and gcd >= 0.5) and cooldown[classtable.Hover].ready then
        MaxDps:GlowCooldown(classtable.Hover, cooldown[classtable.Hover].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.TiptheScales, 'TiptheScales')) and (buff[classtable.DragonrageBuff].up and cooldown[classtable.FireBreath].remains <= cooldown[classtable.EternitySurge].remains) and cooldown[classtable.TiptheScales].ready then
        MaxDps:GlowCooldown(classtable.TiptheScales, cooldown[classtable.TiptheScales].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.ShatteringStar, 'ShatteringStar')) and (( buff[classtable.EssenceBurstBuff].count <1 or not talents[classtable.ArcaneVigor] )) and cooldown[classtable.ShatteringStar].ready then
        if not setSpell then setSpell = classtable.ShatteringStar end
    end
    if (MaxDps:CheckSpellUsable(classtable.FireBreath, 'FireBreath')) and (( talents[classtable.ScorchingEmbers] and talents[classtable.Engulf] and cooldown[classtable.Engulf].remains <= ( classtable and classtable.FireBreath and GetSpellInfo(classtable.FireBreath).castTime /1000 or 0) + 0.5 ) and can_use_empower and cooldown[classtable.Engulf].fullRecharge <= cooldown[classtable.FireBreath].duration + 4) and cooldown[classtable.FireBreath].ready then
        if not setSpell then setSpell = classtable.FireBreath end
        MaxDps.FrameData.empowerLevel[classtable.FireBreath]  = 4
    end
    if (MaxDps:CheckSpellUsable(classtable.FireBreath, 'FireBreath')) and (talents[classtable.Engulf] and talents[classtable.FulminousRoar] and can_use_empower) and cooldown[classtable.FireBreath].ready then
        if not setSpell then setSpell = classtable.FireBreath end
        MaxDps.FrameData.empowerLevel[classtable.FireBreath]  = 1
    end
    if (MaxDps:CheckSpellUsable(classtable.FireBreath, 'FireBreath')) and (can_use_empower and not buff[classtable.DragonrageBuff].up) and cooldown[classtable.FireBreath].ready then
        if not setSpell then setSpell = classtable.FireBreath end
        MaxDps.FrameData.empowerLevel[classtable.FireBreath]  = 2
    end
    if (MaxDps:CheckSpellUsable(classtable.FireBreath, 'FireBreath')) and (can_use_empower) and cooldown[classtable.FireBreath].ready then
        if not setSpell then setSpell = classtable.FireBreath end
        MaxDps.FrameData.empowerLevel[classtable.FireBreath] = 1
    end
    if (MaxDps:CheckSpellUsable(classtable.Engulf, 'Engulf') and talents[classtable.Engulf]) and (( debuff[classtable.FireBreathDamageDeBuff].remains >1 ) and ( debuff[classtable.LivingFlameDamageDeBuff].remains >1 or not talents[classtable.RubyEmbers] ) and ( debuff[classtable.EnkindleDeBuff].remains >1 or not talents[classtable.Enkindle] ) and ( not talents[classtable.Iridescence] or buff[classtable.IridescenceRedBuff].up ) and ( not talents[classtable.ScorchingEmbers] or debuff[classtable.FireBreathDamageDeBuff].duration <= 6 or ttd <= 30 ) and ( debuff[classtable.ShatteringStarDeBuff].remains >1 or cooldown[classtable.Engulf].fullRecharge <cooldown[classtable.ShatteringStar].remains or talents[classtable.ScorchingEmbers] )) and cooldown[classtable.Engulf].ready then
        MaxDps:GlowCooldown(classtable.Engulf, cooldown[classtable.Engulf].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.EternitySurge, 'EternitySurge')) and (( not talents[classtable.PowerSwell] or buff[classtable.PowerSwellBuff].remains <= ( classtable and classtable.EternitySurge and GetSpellInfo(classtable.EternitySurge).castTime /1000 or 0) or not talents[classtable.MassDisintegrate] ) and targets == 2 and not talents[classtable.EternitysSpan] and can_use_empower) and cooldown[classtable.EternitySurge].ready then
        if not setSpell then setSpell = classtable.EternitySurge end
        MaxDps.FrameData.empowerLevel[classtable.EternitySurge] = 2
    end
    if (MaxDps:CheckSpellUsable(classtable.EternitySurge, 'EternitySurge')) and (( not talents[classtable.PowerSwell] or buff[classtable.PowerSwellBuff].remains <= ( classtable and classtable.EternitySurge and GetSpellInfo(classtable.EternitySurge).castTime /1000 or 0) or not talents[classtable.MassDisintegrate] ) and can_use_empower) and cooldown[classtable.EternitySurge].ready then
        if not setSpell then setSpell = classtable.EternitySurge end
        MaxDps.FrameData.empowerLevel[classtable.EternitySurge] = 1
    end
    if (MaxDps:CheckSpellUsable(classtable.LivingFlame, 'LivingFlame')) and (buff[classtable.DragonrageBuff].up and buff[classtable.DragonrageBuff].remains <( 1 - buff[classtable.EssenceBurstBuff].count ) * gcd and buff[classtable.BurnoutBuff].up) and cooldown[classtable.LivingFlame].ready then
        if not setSpell then setSpell = classtable.LivingFlame end
    end
    if (MaxDps:CheckSpellUsable(classtable.AzureStrike, 'AzureStrike')) and (buff[classtable.DragonrageBuff].up and buff[classtable.DragonrageBuff].remains <( 1 - buff[classtable.EssenceBurstBuff].count ) * gcd) and cooldown[classtable.AzureStrike].ready then
        if not setSpell then setSpell = classtable.AzureStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.Firestorm, 'Firestorm') and talents[classtable.Firestorm]) and (buff[classtable.SnapfireBuff].up or targets >= 2) and cooldown[classtable.Firestorm].ready then
        if not setSpell then setSpell = classtable.Firestorm end
    end
    if (MaxDps:CheckSpellUsable(classtable.DeepBreath, 'DeepBreath')) and (talents[classtable.ImminentDestruction] or talents[classtable.MeltArmor] or talents[classtable.Maneuverability]) and cooldown[classtable.DeepBreath].ready then
        MaxDps:GlowCooldown(classtable.DeepBreath, cooldown[classtable.DeepBreath].ready)
    end
    if (MaxDps:CheckSpellUsable(classtable.Disintegrate, 'Disintegrate')) and (( math.huge >2 or buff[classtable.HoverBuff].up ) and buff[classtable.MassDisintegrateStacksBuff].up and talents[classtable.MassDisintegrate] and not pool_for_id) and cooldown[classtable.Disintegrate].ready then
        if not setSpell then setSpell = classtable.Disintegrate end
    end
    if (MaxDps:CheckSpellUsable(classtable.Pyre, 'Pyre')) and (talents[classtable.Snapfire] and targets >= 2 and (talents[classtable.Volatility] and talents[classtable.Volatility] or 0) >= 2 and ( not talents[classtable.AzureCelerity] or talents[classtable.FeedtheFlames] )) and cooldown[classtable.Pyre].ready then
        if not setSpell then setSpell = classtable.Pyre end
    end
    if (MaxDps:CheckSpellUsable(classtable.Disintegrate, 'Disintegrate')) and (( math.huge >2 or buff[classtable.HoverBuff].up ) and not pool_for_id) and cooldown[classtable.Disintegrate].ready then
        if not setSpell then setSpell = classtable.Disintegrate end
    end
    if (talents[classtable.AncientFlame] and not buff[classtable.AncientFlameBuff].up and not buff[classtable.ShatteringStarBuff].up and talents[classtable.ScarletAdaptation] and not buff[classtable.DragonrageBuff].up and not buff[classtable.BurnoutBuff].up and talents[classtable.EngulfingBlaze]) then
        Devastation:green()
    end
    if (MaxDps:CheckSpellUsable(classtable.LivingFlame, 'LivingFlame')) and (buff[classtable.BurnoutBuff].up or buff[classtable.LeapingFlamesBuff].up or buff[classtable.AncientFlameBuff].up) and cooldown[classtable.LivingFlame].ready then
        if not setSpell then setSpell = classtable.LivingFlame end
    end
    if (MaxDps:CheckSpellUsable(classtable.AzureStrike, 'AzureStrike')) and (targets >= 2 and not talents[classtable.Snapfire]) and cooldown[classtable.AzureStrike].ready then
        if not setSpell then setSpell = classtable.AzureStrike end
    end
    if (MaxDps:CheckSpellUsable(classtable.LivingFlame, 'LivingFlame')) and cooldown[classtable.LivingFlame].ready then
        if not setSpell then setSpell = classtable.LivingFlame end
    end
    if (MaxDps:CheckSpellUsable(classtable.AzureStrike, 'AzureStrike')) and cooldown[classtable.AzureStrike].ready then
        if not setSpell then setSpell = classtable.AzureStrike end
    end
end
function Devastation:trinkets()
end


local function ClearCDs()
    MaxDps:GlowCooldown(classtable.VerdantEmbrace, false)
    MaxDps:GlowCooldown(classtable.Quell, false)
    MaxDps:GlowCooldown(classtable.DeepBreath, false)
    MaxDps:GlowCooldown(classtable.TiptheScales, false)
    MaxDps:GlowCooldown(classtable.Dragonrage, false)
    MaxDps:GlowCooldown(classtable.Engulf, false)
    MaxDps:GlowCooldown(classtable.EmeraldBlossom, false)
    MaxDps:GlowCooldown(classtable.Hover, false)
end

function Devastation:callaction()
    next_dragonrage = min(cooldown[classtable.Dragonrage].remains , max( ( cooldown[classtable.EternitySurge].remains - 8 ) , ( cooldown[classtable.FireBreath].remains - 8 ) ))
    if talents[classtable.ImminentDestruction] then
        pool_for_id = cooldown[classtable.DeepBreath].remains <7 and EssenceDeficit >= 1 and not buff[classtable.EssenceBurstBuff].up and ( math.huge >= cooldown[classtable.DeepBreath].remains * 0.4 or talents[classtable.MeltArmor] and talents[classtable.Maneuverability] or targets >= 3 )
    end
    if talents[classtable.Animosity] then
        can_extend_dr = buff[classtable.DragonrageBuff].up and ( buff[classtable.DragonrageBuff].duration + 20000 / 1000 - (buff[classtable.DragonrageBuff].duration - buff[classtable.DragonrageBuff].remains) - buff[classtable.DragonrageBuff].remains ) >0
    end
    if talents[classtable.Animosity] and talents[classtable.Dragonrage] then
        can_use_empower = cooldown[classtable.Dragonrage].remains >= gcd * dr_prep_time
    end
    if (MaxDps:CheckSpellUsable(classtable.Quell, 'Quell')) and (UnitCastingInfo('target') and select(8,UnitCastingInfo('target')) == false) and cooldown[classtable.Quell].ready then
        MaxDps:GlowCooldown(classtable.Quell, ( select(8,UnitCastingInfo('target')) ~= nil and not select(8,UnitCastingInfo('target')) or select(7,UnitChannelInfo('target')) ~= nil and not select(7,UnitChannelInfo('target'))) )
    end
    Devastation:trinkets()
    if (targets >= 3) then
        Devastation:aoe()
    end
    Devastation:st()
end
function Evoker:Devastation()
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
    if not MaxDps.FrameData.empowerLevel then
        MaxDps.FrameData.empowerLevel = {}
    end
    Essence = UnitPower('player', EssencePT)
    EssenceMax = UnitPowerMax('player', EssencePT)
    EssenceDeficit = EssenceMax - Essence
    EssenceRegen = GetPowerRegenForPowerType(Enum.PowerType.Essence)
    EssenceTimeToMax = EssenceDeficit / EssenceRegen
    --for spellId in pairs(MaxDps.Flags) do
    --    self.Flags[spellId] = false
    --    self:ClearGlowIndependent(spellId, spellId)
    --end
    classtable.DragonrageBuff = 375087
    classtable.EssenceBurstBuff = 392268
    classtable.HoverBuff = 358267
    classtable.MassDisintegrateStacksBuff = 436336
    classtable.SnapfireBuff = 370818
    classtable.JackpotBuff = 1217769
    classtable.ChargedBlastBuff = 370454
    classtable.ImminentDestructionBuff = 411055
    classtable.BurnoutBuff = 375802
    classtable.ScarletAdaptationBuff = 372470
    classtable.AncientFlameBuff = 375583
    classtable.LeapingFlamesBuff = 370901
    classtable.IridescenceRedBuff = 386353
    classtable.PowerSwellBuff = 376850
    classtable.ShatteringStarBuff = 370452
    classtable.BloodlustBuff = 2825
    classtable.SpymastersReportBuff = 451199
    classtable.TiptheScalesBuff = 370553
    classtable.ShatteringStarDeBuff = 370452
    classtable.FireBreathDamageDeBuff = 357209
    classtable.LivingFlameDamageDeBuff = 361500
    classtable.EnkindleDeBuff = 444017
    classtable.BombardmentsDeBuff = 434473
    classtable.MeltArmorDeBuff = 441172

    local function debugg()
        talents[classtable.Animosity] = 1
        talents[classtable.Dragonrage] = 1
        talents[classtable.ScarletAdaptation] = 1
        talents[classtable.Slipstream] = 1
        talents[classtable.Firestorm] = 1
        talents[classtable.Engulf] = 1
        talents[classtable.RubyEmbers] = 1
        talents[classtable.ImminentDestruction] = 1
        talents[classtable.ArcaneVigor] = 1
        talents[classtable.EternitysSpan] = 1
        talents[classtable.MassDisintegrate] = 1
        talents[classtable.FeedtheFlames] = 1
        talents[classtable.Maneuverability] = 1
        talents[classtable.MeltArmor] = 1
        talents[classtable.Iridescence] = 1
        talents[classtable.ScorchingEmbers] = 1
        talents[classtable.FontofMagic] = 1
        talents[classtable.FlameSiphon] = 1
        talents[classtable.Volatility] = 1
        talents[classtable.ChargedBlast] = 1
        talents[classtable.Burnout] = 1
        talents[classtable.Snapfire] = 1
        talents[classtable.EngulfingBlaze] = 1
        talents[classtable.FulminousRoar] = 1
        talents[classtable.Enkindle] = 1
        talents[classtable.PowerSwell] = 1
        talents[classtable.AzureCelerity] = 1
        talents[classtable.AncientFlame] = 1
    end


    --if MaxDps.db.global.debugMode then
    --   debugg()
    --end

    setSpell = nil
    ClearCDs()

    Devastation:precombat()

    Devastation:callaction()
    if setSpell then return setSpell end
end
