local addonName, addonTable = ...
_G[addonName] = addonTable

--- @type MaxDps
if not MaxDps then return end

local MaxDps = MaxDps
local GetTime = GetTime
local GetRuneCooldown = GetRuneCooldown
local GetInventoryItemLink = GetInventoryItemLink
local DeathKnight = MaxDps:NewModule('DeathKnight')
addonTable.DeathKnight = DeathKnight

COMMON = {
	AbominationLimbTalent 	= 383269,
	DeathAndDecay         	= 43265,
	DeathAndDecayBuff     	= 374271,
	DeathCoil		 	 	= 47541,
	DeathsDue             	= 324128,
	DeathsDueBuff         	= 324165,
	DeathStrike				= 49998,
	EmpowerRuneWeapon		= 47568,
	IcyTalons				= 194879,
	RaiseDead             	= 46585,
	SacrificialPact       	= 327574,
	SoulReaper 			 	= 343294,
}

DeathKnight.spellMeta = {
	__index = function(t, k)
		print('Spell Key ' .. k .. ' not found!')
	end
}

DeathKnight.weaponRunes = {
	Hysteria 			 = 6243,
	Razorice 			 = 3370,
	Sanguination 		 = 6241,
	Spellwarding 		 = 6242,
	TheApocalypse 		 = 6245,
	TheFallenCrusader 	 = 3368,
	TheStoneskinGargoyle = 3847,
	UndendingThirst 	 = 6244,
}

DeathKnight.hasEnchant = {}

function DeathKnight:Enable()
	if MaxDps:IsRetailWow() then
	    DeathKnight:InitializeDatabase()
	    DeathKnight:CreateConfig()
	end
	DeathKnight:InitializeWeaponRunes()

	if MaxDps.Spec == 1 then
		MaxDps.NextSpell = DeathKnight.Blood
		MaxDps:Print(MaxDps.Colors.Info .. 'Death Knight Blood', "info")
	elseif MaxDps.Spec == 2 then
		MaxDps.NextSpell = DeathKnight.Frost
		MaxDps:Print(MaxDps.Colors.Info .. 'Death Knight Frost', "info")
	elseif MaxDps.Spec == 3 then
		MaxDps.NextSpell = DeathKnight.Unholy
		MaxDps:Print(MaxDps.Colors.Info .. 'Death Knight Unholy', "info")
	end

	return true
end

function DeathKnight:InitializeWeaponRunes()
	DeathKnight.hasEnchant = {}

	local mainHand = GetInventoryItemLink('player', 16)
	if mainHand ~= nil then
		local _, _, eid = strsplit(":", string.match(mainHand, "item[%-?%d:]+"))
		eid = tonumber(eid)
		if eid then
			DeathKnight.hasEnchant[tonumber(eid)] = true
		end
	end

	local offhand = GetInventoryItemLink('player', 17)
	if offhand ~= nil then
		local _, _, eid = strsplit(":", string.match(offhand, "item[%-?%d:]+"))
		eid = tonumber(eid)
		if eid then
			DeathKnight.hasEnchant[tonumber(eid)] = true
		end
	end
end

function DeathKnight:Runes(timeShift)
	local count = 0
	local time = GetTime()
	if not timeShift then
		timeShift = 0
	end

	for i = 1, 10 do
		local start, duration, runeReady = GetRuneCooldown(i)
		if start and start > 0 then
			local rcd = duration + start - time
			if rcd < timeShift then
				count = count + 1
			end
		elseif runeReady then
			count = count + 1
		end
	end
	return count
end

function DeathKnight:TimeToRunes(desiredRunes)
	local time = GetTime()

	if desiredRunes == 0 then
		return 0
	end

	if desiredRunes > 6 then
		return 99999
	end

	local runes = {}
	local readyRuneCount = 0
	local duration = 1
	local refresh
    for i = 1, 6 do
        _, refresh, _ = GetRuneCooldown(i)
        if type(refresh) == "number" and refresh > 0 then
            duration = refresh
        end
    end
	for i = 1, 6 do
		local start, _, runeReady = GetRuneCooldown(i)
		if type(start) ~= "number" and not runeReady then
			start = GetTime()
		end
                if type(start) ~= "number" and runeReady then
                    start = 0
                end
		runes[i] = {
			start = start,
			duration = duration
		}
		if runeReady then
			readyRuneCount = readyRuneCount + 1
		end
	end

	if readyRuneCount >= desiredRunes then
		return 0
	end

	-- Sort the table by remaining cooldown time, ascending
	table.sort(runes, function(l,r)
		if l == nil then
			return true
		elseif r == nil then
			return false
		else
			return l.duration + l.start < r.duration + r.start
		end
	end)

	-- How many additional runes need to come off cooldown before we hit our desired count?
	local neededRunes = desiredRunes - readyRuneCount

	-- If it's three or fewer (since three runes regenerate at a time), take the remaining regen time of the Nth rune
	if neededRunes <= 3 then
		local rune = runes[desiredRunes]
		return rune.duration + rune.start - time
	end

	-- Otherwise, we need to wait for the slowest of our three regenerating runes, plus the full regen time needed for the remaining rune(s)
	local rune = runes[readyRuneCount + 3]
	return rune.duration + rune.start - time + rune.duration
end

function DeathKnight:TimeToRunesCata(desiredAmount,runeType)
    local runeCount = 0
    local totalTime = 0
    local runeCooldowns = {}
	--1 : RUNETYPE_BLOOD
    --2 : RUNETYPE_CHROMATIC
    --3 : RUNETYPE_FROST
    --4 : RUNETYPE_DEATH
	if runeType == "Blood" then
		runeType = 1
	end
	if runeType == "Unholy" then -- ("CHROMATIC" refers to Unholy runes)
		runeType = 2
	end
	if runeType == "Frost" then
		runeType = 3
	end
	if runeType == "Death" then
		runeType = 4
	end

    -- Iterate over the possible rune IDs (assuming there are 6 runes total, 2 for each type)
    for runeId = 1, 6 do
        local runeTypeForId = GetRuneType(runeId)

        -- Check if the rune matches the desired rune type
        if runeTypeForId == runeType or runeTypeForId == 4 then
            table.insert(runeCooldowns, select(2,GetRuneCooldown(runeId)))  -- Store the cooldown of matching runes
        end
    end

    -- Sort the cooldowns in ascending order to prioritize the quickest available runes
    table.sort(runeCooldowns)

    -- Calculate the total time until the desired amount of runes are accumulated
    while runeCount < desiredAmount do
        if runeCooldowns[runeCount + 1] then
            totalTime = totalTime + runeCooldowns[runeCount + 1]  -- Add the cooldown time for the next available rune
            runeCount = runeCount + 1
        else
            break
        end
    end

    return totalTime
end

function DeathKnight:RuneTypeCount(runeType, skipDeathRunes)
	--1 : RUNETYPE_BLOOD
    --2 : RUNETYPE_CHROMATIC
    --3 : RUNETYPE_FROST
    --4 : RUNETYPE_DEATH
	if runeType == "Blood" then
		runeType = 1
	end
	if runeType == "Unholy" then -- ("CHROMATIC" refers to Unholy runes)
		runeType = 2
	end
	if runeType == "Frost" then
		runeType = 3
	end
	if runeType == "Death" then
		runeType = 4
	end
	local count = 0
	for i = 1, 6 do
		local runeTypeForId = GetRuneType(i)
		if runeTypeForId == runeType or (not skipDeathRunes and runeTypeForId == 4) then
		    local start, _, runeReady = GetRuneCooldown(i)
			if runeReady then
				count = count + 1
			end
		end
	end
	return count
end

function DeathKnight:RuneTypeDeathCount(runeType)
	--1 : RUNETYPE_BLOOD
    --2 : RUNETYPE_CHROMATIC
    --3 : RUNETYPE_FROST
    --4 : RUNETYPE_DEATH
	if runeType == "Blood" then
		runeType = 1
	end
	if runeType == "Unholy" then -- ("CHROMATIC" refers to Unholy runes)
		runeType = 2
	end
	if runeType == "Frost" then
		runeType = 3
	end
	if runeType == "Death" then
		runeType = 4
	end
	local count = 0
	if runeType == 1 then
	    for i = 1, 2 do
			if GetRuneType(i) == 4 then
				count = count + 1
			end
	    end
    end
	if runeType == 2 then
	    for i = 3, 4 do
			if GetRuneType(i) == 4 then
				count = count + 1
			end
	    end
    end
	if runeType == 3 then
	    for i = 5, 6 do
			if GetRuneType(i) == 4 then
				count = count + 1
			end
	    end
    end
	return count
end