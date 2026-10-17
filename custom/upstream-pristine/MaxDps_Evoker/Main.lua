local addonName, addonTable = ...;
_G[addonName] = addonTable;

if not MaxDps then return end

--- @type MaxDps
local MaxDps = MaxDps;
local Evoker = MaxDps:NewModule('Evoker');
addonTable.Evoker = Evoker;

Evoker.spellMeta = {
	__index = function(t, k)
		print('Spell Key ' .. k .. ' not found!');
	end
}

function Evoker:Enable()
	if MaxDps.Spec == 1 then
		MaxDps.NextSpell = Evoker.Devastation;
		MaxDps:Print(MaxDps.Colors.Info .. 'Evoker Devastation', "info");
	elseif MaxDps.Spec == 2 then
		MaxDps.NextSpell = Evoker.Preservation;
		MaxDps:Print(MaxDps.Colors.Info .. 'Evoker Preservation', "info");
	elseif MaxDps.Spec == 3 then
		MaxDps.NextSpell = Evoker.Augmentation;
		MaxDps:Print(MaxDps.Colors.Info .. 'Evoker Augmentation', "info");
	end

	return true;
end