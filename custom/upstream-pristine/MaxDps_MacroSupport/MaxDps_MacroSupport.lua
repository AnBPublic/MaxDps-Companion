-- MaxDps Macro Support
-- Companion patch addon for MaxDps Rotation Helper.
-- v1.18.1: Dual Retail 12.0.7 / 12.1.0 compatibility metadata/status update.
-- Keeps v1.17.0 options, debug gating, clear-glows, warnings, changelog, and license notes.
-- Author: Daxomault. Some code was written with assistance from ChatGPT.
-- This addon does not cast spells. It only helps MaxDps find and glow actionbar macro buttons.

local MDMS_CommandHandler
SLASH_MAXDPSMACROSUPPORT1 = "/mdms"
SLASH_MAXDPSMACROSUPPORT2 = "/maxdpsmacro"
SLASH_MAXDPSMACROSUPPORT3 = "/mdmacro"
SlashCmdList.MAXDPSMACROSUPPORT = function(msg)
    if type(MDMS_CommandHandler) == "function" then
        return MDMS_CommandHandler(msg)
    end

    print("MaxDps Macro Support: slash command layer loaded; patch layer is not installed yet. Try /reload, then /mdms loaded.")
end

local ADDON_NAME = ...
local Patch = CreateFrame("Frame")

local VERSION = "1.18.1"
local VERIFIED_RETAIL_CLIENT = "12.0.7 / 12.1.0"
local VERIFIED_INTERFACE = 120100
local VERIFIED_INTERFACE_TEXT = "120007, 120100"
local LOWEST_VERIFIED_INTERFACE = 120007
local HIGHEST_VERIFIED_INTERFACE = 120100
local VERIFIED_INTERFACES = {
    [120007] = true,
    [120100] = true,
}
local MAX_SCAN_SLOTS = 240
local AUTO_SCAN_DELAY = 0.75
local MACRO_UPDATE_SCAN_DELAY = 1.5

local installed = false
local printedLoadMessage = false
local missingMaxDpsWarned = false
local optionsPanelCreated = false
local optionsPanel = nil
local optionsCategory = nil
local originalGlowSpell = nil
local originalGlowClear = nil
local ScheduleScan
local scanDirty = true
local scanScheduled = false
local macroUIScanPending = false
local macroFrameHooked = false

local AutoMappings = {}      -- [spellID] = { [button] = true }
local ManualRuntime = {}     -- [spellID] = { [button] = true }
local BodyCache = {}         -- [sourceKey] = { hash = number, spells = table, label = string }
local LastMacroSlots = {}    -- [slot] = macroID
local TrackedNextGlowButtons = {} -- [button] = true; buttons MaxDps glowed with id "next"
local lastDebugLines = {}

local GetSpellInfoCompat = C_Spell and C_Spell.GetSpellInfo or _G.GetSpellInfo
local GetSpellNameCompat = C_Spell and C_Spell.GetSpellName or _G.GetSpellInfo
local GetMacroInfoCompat = _G.GetMacroInfo
local GetMacroSpellCompat = _G.GetMacroSpell
local GetActionInfoCompat = _G.GetActionInfo
local GetActionTextCompat = _G.GetActionText
local HasActionCompat = _G.HasAction

local macroCommands = {
    cast = true,
    castsequence = true,
    castrandom = true,
    randomcast = true,
    use = true,
    userandom = true,
    randomuse = true,
}

local scanStats = {
    lastReason = "none",
    lastScanMs = 0,
    lastScanTime = 0,
    slotsScanned = 0,
    hotbarMacros = 0,
    candidateButtons = 0,
    hotbarButtons = 0,
    bodiesParsed = 0,
    cacheHits = 0,
    autoMappings = 0,
    manualMappings = 0,
    removedMappings = 0,
    directGlows = 0,
    staleGlowsCleared = 0,
}

local function AddDebugLine(line)
    lastDebugLines[#lastDebugLines + 1] = line
    if #lastDebugLines > 50 then
        table.remove(lastDebugLines, 1)
    end
end

local function ResetStats(reason)
    scanStats.lastReason = reason or scanStats.lastReason or "unknown"
    scanStats.lastScanMs = 0
    scanStats.slotsScanned = 0
    scanStats.hotbarMacros = 0
    scanStats.candidateButtons = 0
    scanStats.hotbarButtons = 0
    scanStats.bodiesParsed = 0
    scanStats.cacheHits = 0
    scanStats.autoMappings = 0
    scanStats.manualMappings = 0
    scanStats.removedMappings = 0
    scanStats.directGlows = 0
    scanStats.staleGlowsCleared = 0
    lastDebugLines = {}
end

local function Trim(value)
    if value == nil then
        return nil
    end

    return tostring(value):match("^%s*(.-)%s*$")
end

local function SafeCall(func, ...)
    if type(func) ~= "function" then
        return nil
    end

    local ok, a, b, c, d, e, f, g, h = pcall(func, ...)
    if ok then
        return a, b, c, d, e, f, g, h
    end

    AddDebugLine("safe call failed: " .. tostring(a))
    return nil
end

local function GetTimeSafe()
    return SafeCall(_G.GetTime) or 0
end

local function DebugProfileStopSafe()
    return SafeCall(_G.debugprofilestop) or 0
end

local function InCombatLockdownSafe()
    return SafeCall(_G.InCombatLockdown) and true or false
end

local function GetSpellInfoSafe(token)
    if token == nil then
        return nil
    end

    return SafeCall(GetSpellInfoCompat, token)
end

local function GetSpellNameSafe(spellID)
    if not spellID then
        return nil
    end

    local value = SafeCall(GetSpellNameCompat, spellID)
    if type(value) == "table" then
        return value.name
    end

    return value
end

local function FindBaseSpellByIDSafe(spellID)
    return SafeCall(_G.FindBaseSpellByID, spellID)
end

local function FindSpellOverrideByIDSafe(spellID)
    return SafeCall(_G.FindSpellOverrideByID, spellID)
end

local function RequestSpellDataSafe(spellID)
    if spellID and C_Spell and type(C_Spell.RequestLoadSpellData) == "function" then
        SafeCall(C_Spell.RequestLoadSpellData, spellID)
    end
end

local function EnsureDB()
    MaxDps_MacroSupportDB = MaxDps_MacroSupportDB or {}
    MaxDps_MacroSupportDB.manualBindings = MaxDps_MacroSupportDB.manualBindings or {}
    if MaxDps_MacroSupportDB.autoScanEnabled == nil then
        MaxDps_MacroSupportDB.autoScanEnabled = true
    end
    MaxDps_MacroSupportDB.performanceMode = MaxDps_MacroSupportDB.performanceMode or "normal"
    if MaxDps_MacroSupportDB.debugEnabled == nil then
        MaxDps_MacroSupportDB.debugEnabled = false
    end
    if MaxDps_MacroSupportDB.firstRunMessageShown == nil then
        MaxDps_MacroSupportDB.firstRunMessageShown = false
    end
    return MaxDps_MacroSupportDB
end

local function DebugCommandsEnabled()
    local db = EnsureDB()
    return db.debugEnabled == true or db.performanceMode == "debug"
end

local function AutoScanEnabled()
    local db = EnsureDB()
    return db.autoScanEnabled ~= false and db.performanceMode ~= "safe"
end

local function SetAutoScanEnabled(enabled)
    local db = EnsureDB()
    db.autoScanEnabled = enabled and true or false
    if enabled and db.performanceMode == "safe" then
        db.performanceMode = "normal"
    end
end

local function SetDebugEnabled(enabled)
    local db = EnsureDB()
    db.debugEnabled = enabled and true or false
end

local function IsMacroUIOpen()
    local macroFrame = _G.MacroFrame
    return macroFrame and type(macroFrame.IsShown) == "function" and SafeCall(macroFrame.IsShown, macroFrame) and true or false
end

local function SimpleHash(value)
    value = tostring(value or "")
    local hash = 5381
    for i = 1, #value do
        hash = (hash * 33 + value:byte(i)) % 2147483647
    end
    return hash
end

local function TableCount(tbl)
    local count = 0
    if type(tbl) == "table" then
        for _ in pairs(tbl) do
            count = count + 1
        end
    end
    return count
end

local function CopySet(tbl)
    local copy = {}
    if type(tbl) == "table" then
        for k, v in pairs(tbl) do
            copy[k] = v
        end
    end
    return copy
end

local function IsEquipmentSlotUse(command, token)
    if command ~= "use" and command ~= "userandom" and command ~= "randomuse" then
        return false
    end

    local slot = tonumber(token)
    return slot ~= nil and slot >= 1 and slot <= 19
end

local function ResolveSpellID(token, command)
    token = Trim(token)
    if not token or token == "" then
        return nil
    end

    token = token:gsub("^!", "")
    token = token:gsub("^[Ss][Pp][Ee][Ll][Ll]:", "")
    token = Trim(token)
    if not token or token == "" then
        return nil
    end

    local lowered = token:lower()
    if lowered == "null" or lowered == "none" then
        return nil
    end

    if IsEquipmentSlotUse(command, token) then
        return nil
    end

    local numericToken = tonumber(token)

    -- Numeric /cast and /castsequence tokens are spell IDs. Do not require spell data
    -- to be loaded before registering them.
    if numericToken and command ~= "use" and command ~= "userandom" and command ~= "randomuse" then
        RequestSpellDataSafe(numericToken)
        return numericToken
    end

    local lookupValue = numericToken or token
    local spellInfo, _, _, _, _, _, legacySpellID = GetSpellInfoSafe(lookupValue)

    if type(spellInfo) == "table" then
        return spellInfo.spellID
    end

    if spellInfo then
        return legacySpellID or numericToken
    end

    if not numericToken then
        local compact = token:gsub("%s+", " ")
        if compact ~= token then
            spellInfo, _, _, _, _, _, legacySpellID = GetSpellInfoSafe(compact)
            if type(spellInfo) == "table" then
                return spellInfo.spellID
            end
            if spellInfo then
                return legacySpellID
            end
        end
    end

    return nil
end

local function RemoveMacroConditionals(value)
    value = value or ""
    while value:find("%b[]") do
        value = value:gsub("%b[]", "")
    end
    return value
end

local function StripCastSequenceOptions(token)
    token = Trim(token)
    if not token then
        return nil
    end

    -- Handles reset=target, reset=combat/target/5, reset=10, etc. If users add
    -- other key=value sequence options, this strips those too from the first token.
    while token:match("^%s*[%a_][%w_%-]*=") do
        token = token:gsub("^%s*[%a_][%w_%-]*=%S+%s*", "", 1)
        token = Trim(token)
        if not token or token == "" then
            return nil
        end
    end

    return token
end

local function AddResolvedSpell(spells, token, command)
    token = Trim(token)
    if not token or token == "" then
        return false
    end

    if command == "castsequence" then
        token = StripCastSequenceOptions(token)
    end

    if not token or token == "" then
        return false
    end

    local spellID = ResolveSpellID(token, command)
    if spellID then
        spells[spellID] = token
        return true
    end

    return false
end

local function AddSpellTokensFromBranch(spells, command, branch)
    branch = RemoveMacroConditionals(branch)
    branch = Trim(branch)
    if not branch or branch == "" then
        return
    end

    if command == "castsequence" or command == "castrandom" or command == "randomcast" or command == "userandom" or command == "randomuse" then
        for token in branch:gmatch("[^,]+") do
            AddResolvedSpell(spells, token, command)
        end
    else
        AddResolvedSpell(spells, branch, command)
    end
end

local function ParseMacroBodyRaw(body)
    local spells = {}
    if not body then
        return spells
    end

    for line in tostring(body):gmatch("[^\r\n]+") do
        local command, args = line:match("^%s*/(%S+)%s*(.*)$")
        if command and args then
            command = command:lower()
            if command == "cast!" then
                command = "cast"
            end

            if macroCommands[command] then
                for branch in args:gmatch("[^;]+") do
                    AddSpellTokensFromBranch(spells, command, branch)
                end
            end
        end
    end

    return spells
end

local function ParseMacroBodyCached(body, sourceKey, sourceLabel)
    if not body or body == "" then
        return {}
    end

    sourceKey = sourceKey or ("body:" .. tostring(sourceLabel or "unknown"))
    local hash = SimpleHash(body)
    local cached = BodyCache[sourceKey]
    if cached and cached.hash == hash then
        scanStats.cacheHits = scanStats.cacheHits + 1
        return cached.spells
    end

    local spells = ParseMacroBodyRaw(body)
    BodyCache[sourceKey] = {
        hash = hash,
        spells = spells,
        label = sourceLabel,
    }

    scanStats.bodiesParsed = scanStats.bodiesParsed + 1
    if next(spells) then
        AddDebugLine("parsed " .. tostring(sourceLabel or sourceKey) .. " -> " .. tostring(TableCount(spells)) .. " spell(s)")
    end

    return spells
end

local function GetMacroInfoByIDOrName(value)
    if not value or not GetMacroInfoCompat then
        return nil, nil, nil
    end

    return SafeCall(GetMacroInfoCompat, value)
end

local function GetMacroBodyFromMacroID(macroID, actionSlot)
    local macroName, icon, body = GetMacroInfoByIDOrName(macroID)
    if body then
        return macroName, body, "macroID:" .. tostring(macroID), "GetMacroInfo(" .. tostring(macroID) .. ")"
    end

    if actionSlot and GetActionTextCompat then
        local actionText = SafeCall(GetActionTextCompat, actionSlot)
        if actionText then
            local name2, _, body2 = GetMacroInfoByIDOrName(actionText)
            if body2 then
                return name2 or actionText, body2, "slotName:" .. tostring(actionSlot) .. ":" .. tostring(actionText), "GetMacroInfo(GetActionText(" .. tostring(actionSlot) .. "))"
            end
        end
    end

    if macroName and macroName ~= macroID then
        local name3, _, body3 = GetMacroInfoByIDOrName(macroName)
        if body3 then
            return name3 or macroName, body3, "macroName:" .. tostring(macroName), "GetMacroInfo(name " .. tostring(macroName) .. ")"
        end
    end

    return macroName, nil, "macroID:" .. tostring(macroID), "GetMacroInfo(" .. tostring(macroID) .. ")"
end

local function GetAllSpellsFromMacro(macroID, actionSlot)
    local spells = {}
    if not macroID then
        return spells
    end

    local baseSpell = GetMacroSpellCompat and SafeCall(GetMacroSpellCompat, macroID)
    if baseSpell then
        local spellID = ResolveSpellID(baseSpell, "cast")
        if spellID then
            spells[spellID] = tostring(baseSpell)
        end
    end

    local macroName, body, sourceKey, sourceLabel = GetMacroBodyFromMacroID(macroID, actionSlot)
    local parsed = ParseMacroBodyCached(body, sourceKey, sourceLabel)
    for spellID, token in pairs(parsed) do
        spells[spellID] = token
    end

    if next(spells) then
        AddDebugLine("macro " .. tostring(macroID) .. " " .. tostring(macroName or "?") .. " -> " .. tostring(TableCount(spells)) .. " spell(s)")
    end

    return spells
end

local macroTextAttributeKeys = {
    "macrotext",
    "macrotext1",
    "macrotext2",
    "macrotext3",
    "macrotext4",
    "macrotext5",
    "macrotext6",
    "macrotext7",
    "macrotext8",
    "macrotext9",
    "macrotext10",
    "macrotext11",
    "macrotext12",
    "*macrotext*",
    "helpbutton1-macrotext",
    "harmbutton1-macrotext",
    "alt-macrotext1",
    "ctrl-macrotext1",
    "shift-macrotext1",
}

local macroAttributeKeys = {
    "macro",
    "macro1",
    "macro2",
    "macro3",
    "macro4",
    "macro5",
    "macro6",
    "macro7",
    "macro8",
    "macro9",
    "macro10",
    "macro11",
    "macro12",
    "*macro*",
}

local function SafeGetAttribute(button, key)
    if not button or type(button.GetAttribute) ~= "function" then
        return nil
    end
    return SafeCall(button.GetAttribute, button, key)
end

local function SafeButtonMethod(button, methodName)
    if not button or type(button[methodName]) ~= "function" then
        return nil
    end
    return SafeCall(button[methodName], button)
end

local function AsNumber(value)
    if value == nil then
        return nil
    end

    local number = tonumber(value)
    if number and number > 0 then
        return number
    end

    return nil
end

local function GetActionSlotFromButton(button)
    if not button then
        return nil
    end

    return AsNumber(SafeGetAttribute(button, "action"))
        or AsNumber(button.action)
        or AsNumber(button.id)
        or AsNumber(button._state_action)
        or AsNumber(SafeButtonMethod(button, "GetPagedID"))
        or AsNumber(SafeButtonMethod(button, "CalculateAction"))
        or AsNumber(SafeButtonMethod(button, "GetActionID"))
        or AsNumber(SafeButtonMethod(button, "GetAction"))
        or AsNumber(SafeButtonMethod(button, "GetActionSlot"))
end

local function GetButtonName(button)
    if button and type(button.GetName) == "function" then
        return SafeCall(button.GetName, button)
    end
    return nil
end

local function GetMacroIDFromActionSlot(slot)
    if not slot or not GetActionInfoCompat then
        return nil
    end

    if HasActionCompat and not SafeCall(HasActionCompat, slot) then
        return nil
    end

    local actionType, actionID = SafeCall(GetActionInfoCompat, slot)
    if actionType == "macro" then
        return actionID
    end

    return nil
end

local function ButtonHasMacroText(button)
    if not button then
        return false
    end

    for _, key in ipairs(macroTextAttributeKeys) do
        local body = SafeGetAttribute(button, key)
        if type(body) == "string" and body ~= "" then
            return true
        end
    end

    return false
end

local function ButtonIsMacroType(button)
    if not button then
        return false
    end

    local type0 = SafeGetAttribute(button, "type")
    local type1 = SafeGetAttribute(button, "type1")
    local type2 = SafeGetAttribute(button, "type2")
    return type0 == "macro" or type1 == "macro" or type2 == "macro"
end

local function CollectHotbarMacroSlots()
    local slots = {}
    local count = 0

    for slot = 1, MAX_SCAN_SLOTS do
        scanStats.slotsScanned = scanStats.slotsScanned + 1
        local macroID = GetMacroIDFromActionSlot(slot)
        if macroID then
            slots[slot] = macroID
            LastMacroSlots[slot] = macroID
            count = count + 1
        else
            LastMacroSlots[slot] = nil
        end
    end

    scanStats.hotbarMacros = count
    return slots, count
end

local function AddUniqueButton(maxdps, spellID, button)
    if not maxdps or not spellID or not button or type(maxdps.AddButton) ~= "function" then
        return false
    end

    maxdps.Spells = maxdps.Spells or {}
    local list = maxdps.Spells[spellID]
    if type(list) == "table" then
        for _, existing in pairs(list) do
            if existing == button then
                return false
            end
        end
    end

    maxdps:AddButton(spellID, button)
    return true
end

local function RememberMapping(map, spellID, button)
    if not spellID or not button then
        return
    end

    map[spellID] = map[spellID] or {}
    map[spellID][button] = true
end

local function AddSpellAndAliases(maxdps, spellID, button, map, statKey)
    if not spellID or not button then
        return false
    end

    local added = false
    local seen = {}

    local function add(id)
        id = tonumber(id)
        if not id or seen[id] then
            return
        end

        seen[id] = true
        RequestSpellDataSafe(id)
        if AddUniqueButton(maxdps, id, button) then
            added = true
            if statKey then
                scanStats[statKey] = (scanStats[statKey] or 0) + 1
            end
        end
        if map then
            RememberMapping(map, id, button)
        end
    end

    add(spellID)
    add(FindBaseSpellByIDSafe(spellID))
    add(FindSpellOverrideByIDSafe(spellID))

    local name = GetSpellNameSafe(spellID)
    if name then
        local info = GetSpellInfoSafe(name)
        if type(info) == "table" then
            add(info.spellID)
        end
    end

    return added
end


local function HideNextGlowOnButton(maxdps, button)
    if not maxdps or not button or type(maxdps.HideGlow) ~= "function" then
        return false
    end

    SafeCall(maxdps.HideGlow, maxdps, button, "next")
    TrackedNextGlowButtons[button] = nil
    scanStats.staleGlowsCleared = (scanStats.staleGlowsCleared or 0) + 1
    return true
end

local function ClearTrackedNextGlows(maxdps)
    local cleared = 0
    for button in pairs(TrackedNextGlowButtons) do
        if HideNextGlowOnButton(maxdps, button) then
            cleared = cleared + 1
        end
    end
    TrackedNextGlowButtons = {}
    return cleared
end

local function ClearKnownMacroGlows(maxdps)
    local cleared = ClearTrackedNextGlows(maxdps)

    if maxdps and type(maxdps.Spells) == "table" then
        local seen = {}
        local function clearMap(map)
            if type(map) ~= "table" then
                return
            end
            for spellID, buttons in pairs(map) do
                if type(buttons) == "table" then
                    for button in pairs(buttons) do
                        if button and not seen[button] then
                            seen[button] = true
                            if HideNextGlowOnButton(maxdps, button) then
                                cleared = cleared + 1
                            end
                        end
                    end
                end
            end
        end
        clearMap(AutoMappings)
        clearMap(ManualRuntime)
    end

    return cleared
end

local function TrackCurrentNextGlows(maxdps)
    TrackedNextGlowButtons = {}

    if not maxdps or type(maxdps.SpellsGlowing) ~= "table" or type(maxdps.Spells) ~= "table" then
        return 0
    end

    local tracked = 0
    for spellID, value in pairs(maxdps.SpellsGlowing) do
        if value == 1 and type(maxdps.Spells[spellID]) == "table" then
            for _, button in pairs(maxdps.Spells[spellID]) do
                if button and not TrackedNextGlowButtons[button] then
                    TrackedNextGlowButtons[button] = true
                    tracked = tracked + 1
                end
            end
        end
    end

    return tracked
end

local function RefreshCurrentNextGlow(maxdps)
    ClearTrackedNextGlows(maxdps)

    if maxdps and maxdps.Spell and type(originalGlowSpell) == "function" then
        SafeCall(originalGlowSpell, maxdps, maxdps.Spell)
        TrackCurrentNextGlows(maxdps)
    end
end

local function RemoveButtonFromSpellList(maxdps, spellID, button)
    if not maxdps or not maxdps.Spells or not spellID or not button then
        return 0
    end

    local list = maxdps.Spells[spellID]
    if type(list) ~= "table" then
        return 0
    end

    local removed = 0
    for i = #list, 1, -1 do
        if list[i] == button then
            table.remove(list, i)
            removed = removed + 1
        end
    end

    if removed > 0 then
        HideNextGlowOnButton(maxdps, button)
    end

    if next(list) == nil then
        maxdps.Spells[spellID] = nil
    end

    return removed
end

local function ClearMappingTableFromMaxDps(maxdps, map)
    local removed = 0
    if not maxdps or type(map) ~= "table" then
        return 0
    end

    for spellID, buttons in pairs(map) do
        for button in pairs(buttons) do
            removed = removed + RemoveButtonFromSpellList(maxdps, spellID, button)
        end
    end

    return removed
end

local function ClearAutoMappings(maxdps)
    local removed = ClearMappingTableFromMaxDps(maxdps, AutoMappings)
    AutoMappings = {}
    scanStats.removedMappings = scanStats.removedMappings + removed
end

local function ReapplyMappingTable(maxdps, map)
    local count = 0
    if not maxdps or type(map) ~= "table" then
        return 0
    end

    for spellID, buttons in pairs(map) do
        for button in pairs(buttons) do
            if AddUniqueButton(maxdps, spellID, button) then
                count = count + 1
            end
        end
    end

    return count
end

local function SpellIDsMatch(a, b)
    if not a or not b then
        return false
    end

    if a == b then
        return true
    end

    if FindBaseSpellByIDSafe(a) == b or FindSpellOverrideByIDSafe(a) == b then
        return true
    end

    if FindBaseSpellByIDSafe(b) == a or FindSpellOverrideByIDSafe(b) == a then
        return true
    end

    local nameA = GetSpellNameSafe(a)
    local nameB = GetSpellNameSafe(b)
    return nameA and nameB and nameA == nameB
end

local function GetButtonsForSpell(maxdps, spellID)
    local buttons = {}
    if not maxdps or not maxdps.Spells or not spellID then
        return buttons
    end

    local seen = {}
    local function addFrom(id)
        if id and type(maxdps.Spells[id]) == "table" then
            for _, button in pairs(maxdps.Spells[id]) do
                if button and not seen[button] then
                    seen[button] = true
                    buttons[#buttons + 1] = button
                end
            end
        end
    end

    addFrom(spellID)
    addFrom(FindBaseSpellByIDSafe(spellID))
    addFrom(FindSpellOverrideByIDSafe(spellID))

    local searchName = GetSpellNameSafe(spellID)
    if searchName then
        for knownSpellID, list in pairs(maxdps.Spells) do
            if knownSpellID ~= spellID and type(list) == "table" and GetSpellNameSafe(knownSpellID) == searchName then
                for _, button in pairs(list) do
                    if button and not seen[button] then
                        seen[button] = true
                        buttons[#buttons + 1] = button
                    end
                end
            end
        end
    end

    return buttons
end

local function SpellHasButton(maxdps, spellID)
    return #GetButtonsForSpell(maxdps, spellID) > 0
end

local function GlowRegisteredMacroButtons(maxdps, spellID)
    local buttons = GetButtonsForSpell(maxdps, spellID)
    if #buttons == 0 or not maxdps or type(maxdps.Glow) ~= "function" then
        return false
    end

    maxdps.SpellsGlowing = maxdps.SpellsGlowing or {}
    for _, button in ipairs(buttons) do
        SafeCall(maxdps.Glow, maxdps, button, "next", nil, "normal")
        TrackedNextGlowButtons[button] = true
    end
    maxdps.SpellsGlowing[spellID] = 1
    scanStats.directGlows = scanStats.directGlows + #buttons
    return true
end

local function ForEachKnownActionButton(callback)
    if type(callback) ~= "function" then
        return
    end

    local seen = {}
    local function visit(button)
        if not button or seen[button] then
            return
        end
        seen[button] = true
        callback(button)
    end

    if LibStub then
        local ok, _, libs = pcall(LibStub.IterateLibraries, LibStub)
        if ok and libs then
            for libName in pairs(libs) do
                if type(libName) == "string" and libName:match("^LibActionButton%-1%.0") then
                    local lib = LibStub(libName, true)
                    if lib and type(lib.GetAllButtons) == "function" then
                        local buttons = SafeCall(lib.GetAllButtons, lib)
                        if buttons then
                            for button in pairs(buttons) do
                                visit(button)
                            end
                        end
                    end
                end
            end
        end
    end

    local prefixes = {
        "ActionButton",
        "MultiBarBottomLeftButton",
        "MultiBarBottomRightButton",
        "MultiBarRightButton",
        "MultiBarLeftButton",
        "MultiBar5Button",
        "MultiBar6Button",
        "MultiBar7Button",
        "StanceButton",
        "PetActionButton",
        "BT4Button",
        "BT4PetButton",
        "BT4StanceButton",
        "DominosActionButton",
        "DominosStanceButton",
        "ElvUI_Bar1Button",
        "ElvUI_Bar2Button",
        "ElvUI_Bar3Button",
        "ElvUI_Bar4Button",
        "ElvUI_Bar5Button",
        "ElvUI_Bar6Button",
        "ElvUI_StanceBarButton",
        "ButtonForge",
        "EABButton",
        "NeuronActionBar1_ActionButton",
        "NeuronActionBar2_ActionButton",
        "NeuronActionBar3_ActionButton",
        "NeuronActionBar4_ActionButton",
        "NeuronActionBar5_ActionButton",
        "NeuronActionBar6_ActionButton",
        "NeuronActionBar7_ActionButton",
        "NeuronActionBar8_ActionButton",
        "DragonflightUIActionButton",
    }

    for _, prefix in ipairs(prefixes) do
        for i = 1, 300 do
            visit(_G[prefix .. i])
        end
    end

    local bars = {
        "DragonflightUIActionbarFrame1",
        "DragonflightUIActionbarFrame2",
        "DragonflightUIActionbarFrame3",
        "DragonflightUIActionbarFrame4",
        "DragonflightUIActionbarFrame5",
        "DragonflightUIActionbarFrame6",
        "DragonflightUIActionbarFrame7",
        "DragonflightUIActionbarFrame8",
    }

    for _, barName in ipairs(bars) do
        local bar = _G[barName]
        if type(bar) == "table" and type(bar.buttonTable) == "table" then
            for _, button in pairs(bar.buttonTable) do
                visit(button)
            end
        end
    end
end

local function RegisterSpellsFromSet(maxdps, spells, button, onlySpellID, map, statKey)
    local added = 0
    if type(spells) ~= "table" then
        return 0
    end

    for spellID in pairs(spells) do
        if not onlySpellID or SpellIDsMatch(spellID, onlySpellID) then
            if AddSpellAndAliases(maxdps, spellID, button, map, statKey) then
                added = added + 1
            end
            if onlySpellID then
                AddSpellAndAliases(maxdps, onlySpellID, button, map, statKey)
            end
        end
    end

    return added
end

local function AddMacroTextButton(maxdps, button, slot, onlySpellID, map, statKey)
    local added = 0
    local buttonName = GetButtonName(button) or tostring(button)

    for _, key in ipairs(macroTextAttributeKeys) do
        local body = SafeGetAttribute(button, key)
        if type(body) == "string" and body ~= "" then
            local sourceKey = "macrotext:" .. tostring(slot or buttonName) .. ":" .. tostring(key)
            local spells = ParseMacroBodyCached(body, sourceKey, "button attribute " .. tostring(key))
            added = added + RegisterSpellsFromSet(maxdps, spells, button, onlySpellID, map, statKey)
        end
    end

    return added
end

local function AddSavedMacroButton(maxdps, macroID, button, slot, onlySpellID, map, statKey)
    if not macroID then
        return 0
    end

    local spells = GetAllSpellsFromMacro(macroID, slot)
    return RegisterSpellsFromSet(maxdps, spells, button, onlySpellID, map, statKey)
end

local function ButtonIsHotbarMacroButton(button, macroSlots)
    local slot = GetActionSlotFromButton(button)
    if slot and macroSlots[slot] then
        return true, slot, macroSlots[slot]
    end

    -- Fallback for actionbar addon buttons that are secure macro buttons with inline
    -- macrotext but do not expose a normal action slot. We only do this for buttons
    -- from known actionbar collections, not from EnumerateFrames.
    if ButtonIsMacroType(button) and ButtonHasMacroText(button) then
        return true, nil, SafeGetAttribute(button, "macro")
    end

    return false, slot, nil
end

local function ScanHotbarMacroButtons(maxdps, onlySpellID)
    local found = 0
    if not maxdps then
        return 0
    end

    local macroSlots = CollectHotbarMacroSlots()

    ForEachKnownActionButton(function(button)
        scanStats.candidateButtons = scanStats.candidateButtons + 1
        local isHotbarMacro, slot, macroID = ButtonIsHotbarMacroButton(button, macroSlots)
        if not isHotbarMacro then
            return
        end

        scanStats.hotbarButtons = scanStats.hotbarButtons + 1
        local added = 0

        if macroID then
            added = added + AddSavedMacroButton(maxdps, macroID, button, slot, onlySpellID, AutoMappings, "autoMappings")
        end

        -- Important workaround from v1.9: some actionbar addons expose the usable
        -- macro body only through secure macrotext attributes.
        added = added + AddMacroTextButton(maxdps, button, slot, onlySpellID, AutoMappings, "autoMappings")

        -- Some bar addons keep a saved macro ID/name in attributes even when the action
        -- slot API does not expose the body.
        if slot or ButtonIsMacroType(button) then
            for _, key in ipairs(macroAttributeKeys) do
                local attrMacro = SafeGetAttribute(button, key)
                if attrMacro and attrMacro ~= macroID then
                    added = added + AddSavedMacroButton(maxdps, attrMacro, button, slot, onlySpellID, AutoMappings, "autoMappings")
                end
            end
        end

        if added > 0 then
            found = found + 1
        end
    end)

    return found
end

local function ResolveSpellText(text)
    text = Trim(text)
    if not text or text == "" then
        return nil
    end

    local spellID = tonumber(text)
    if not spellID then
        local info, _, _, _, _, _, legacySpellID = GetSpellInfoSafe(text)
        if type(info) == "table" then
            spellID = info.spellID
        elseif info then
            spellID = legacySpellID
        end
    end

    if spellID then
        RequestSpellDataSafe(spellID)
    end

    return spellID
end

local function ButtonMatchesBinding(button, binding)
    if not button or type(binding) ~= "table" then
        return false
    end

    local buttonName = GetButtonName(button)
    if binding.buttonName and buttonName == binding.buttonName then
        return true
    end

    local slot = GetActionSlotFromButton(button)
    if binding.slot and slot and tonumber(binding.slot) == tonumber(slot) then
        return true
    end

    if binding.macroID then
        local macroID = slot and GetMacroIDFromActionSlot(slot) or nil
        if macroID and tostring(macroID) == tostring(binding.macroID) then
            return true
        end
    end

    if binding.macroName and slot and GetActionTextCompat then
        local actionText = SafeCall(GetActionTextCompat, slot)
        if actionText and tostring(actionText) == tostring(binding.macroName) then
            return true
        end
    end

    return false
end

local function ApplyManualBindings(maxdps, onlySpellID)
    local db = EnsureDB()
    local count = 0
    if not onlySpellID then
        ManualRuntime = {}
    end

    ForEachKnownActionButton(function(button)
        for spellKey, binding in pairs(db.manualBindings) do
            local spellID = tonumber(spellKey)
            if spellID and (not onlySpellID or SpellIDsMatch(spellID, onlySpellID)) and ButtonMatchesBinding(button, binding) then
                if AddSpellAndAliases(maxdps, spellID, button, ManualRuntime, "manualMappings") then
                    count = count + 1
                end
                if onlySpellID then
                    AddSpellAndAliases(maxdps, onlySpellID, button, ManualRuntime, "manualMappings")
                end
            end
        end
    end)

    return count
end

local function ReapplyCachedMappings(maxdps)
    local count = 0
    count = count + ReapplyMappingTable(maxdps, AutoMappings)
    count = count + ReapplyMappingTable(maxdps, ManualRuntime)
    return count
end

local function FullMacroScan(maxdps, onlySpellID, force, reason)
    if not maxdps then
        return 0
    end

    if not force then
        if not AutoScanEnabled() then
            AddDebugLine("auto scan skipped: autoscan disabled")
            return 0
        end
        if not scanDirty and not onlySpellID then
            ReapplyCachedMappings(maxdps)
            return 0
        end
        if IsMacroUIOpen() then
            macroUIScanPending = true
            AddDebugLine("auto scan deferred: Macro UI is open")
            return 0
        end
        if InCombatLockdownSafe() then
            AddDebugLine("auto scan deferred: combat lockdown")
            return 0
        end
    end

    ResetStats(reason or (force and "manual" or "auto"))
    local startMs = DebugProfileStopSafe()

    if not onlySpellID then
        ClearAutoMappings(maxdps)
    end
    local found = ScanHotbarMacroButtons(maxdps, onlySpellID)
    found = found + ApplyManualBindings(maxdps, onlySpellID)

    if not onlySpellID and (scanStats.removedMappings or 0) > 0 then
        -- If a glowing macro spell moved to another hotkey, MaxDps may no longer know
        -- about the old button and its normal GlowClear cannot hide that stale overlay.
        -- Clear the tracked old glow now, then re-glow the current recommendation if known.
        RefreshCurrentNextGlow(maxdps)
    end

    scanDirty = false
    scanScheduled = false
    scanStats.lastScanMs = DebugProfileStopSafe() - startMs
    scanStats.lastScanTime = GetTimeSafe()

    return found
end

ScheduleScan = function(reason, delay)
    scanDirty = true

    if not AutoScanEnabled() then
        AddDebugLine("scan marked dirty; autoscan disabled" .. (reason and (" (" .. tostring(reason) .. ")") or ""))
        return
    end

    if IsMacroUIOpen() then
        macroUIScanPending = true
        AddDebugLine("scan deferred while Macro UI is open" .. (reason and (" (" .. tostring(reason) .. ")") or ""))
        return
    end

    if scanScheduled then
        return
    end

    scanScheduled = true
    C_Timer.After(delay or AUTO_SCAN_DELAY, function()
        scanScheduled = false
        if not MaxDps then
            return
        end
        if IsMacroUIOpen() then
            macroUIScanPending = true
            AddDebugLine("scheduled scan deferred: Macro UI opened")
            return
        end
        if InCombatLockdownSafe() then
            AddDebugLine("scheduled scan deferred: combat lockdown")
            return
        end
        FullMacroScan(MaxDps, nil, false, reason or "scheduled")
    end)
end

local function HookMacroFrameIfAvailable()
    if macroFrameHooked then
        return
    end

    local macroFrame = _G.MacroFrame
    if not macroFrame or type(macroFrame.HookScript) ~= "function" then
        return
    end

    macroFrameHooked = true
    SafeCall(macroFrame.HookScript, macroFrame, "OnHide", function()
        if macroUIScanPending then
            macroUIScanPending = false
            ScheduleScan("MacroFrame_OnHide", MACRO_UPDATE_SCAN_DELAY)
        end
    end)
end

local function GetMouseFocusCompat()
    if type(_G.GetMouseFoci) == "function" then
        local value = SafeCall(_G.GetMouseFoci)
        if type(value) == "table" then
            return value[1]
        elseif value then
            return value
        end
    end

    if type(C_UI) == "table" and type(C_UI.GetMouseFocus) == "function" then
        local value = SafeCall(C_UI.GetMouseFocus)
        if value then
            return value
        end
    end

    if type(_G.GetMouseFocus) == "function" then
        return SafeCall(_G.GetMouseFocus)
    end

    return nil
end

local function FindActionButtonFromFrame(frame)
    local safety = 0
    while frame and safety < 12 do
        safety = safety + 1
        local slot = GetActionSlotFromButton(frame)
        if slot or ButtonIsMacroType(frame) or ButtonHasMacroText(frame) then
            return frame
        end

        if type(frame.GetParent) == "function" then
            frame = SafeCall(frame.GetParent, frame)
        else
            frame = nil
        end
    end

    return nil
end

local function BindSpellToMouseover(spellToken)
    local spellID = ResolveSpellText(spellToken)
    if not spellID then
        print("Usage: /mdms bind <spell id or spell name>")
        return
    end

    local button = FindActionButtonFromFrame(GetMouseFocusCompat())
    if not button then
        print("MaxDps Macro Support: mouse over the macro actionbar button, then run /mdms bind " .. tostring(spellID) .. ".")
        return
    end

    local slot = GetActionSlotFromButton(button)
    local macroID = slot and GetMacroIDFromActionSlot(slot) or SafeGetAttribute(button, "macro")
    local macroName = nil
    if macroID then
        macroName = SafeCall(GetMacroInfoCompat, macroID)
    elseif slot and GetActionTextCompat then
        macroName = SafeCall(GetActionTextCompat, slot)
    end

    local db = EnsureDB()
    db.manualBindings[tostring(spellID)] = {
        buttonName = GetButtonName(button),
        slot = slot,
        macroID = macroID,
        macroName = macroName,
    }

    ApplyManualBindings(MaxDps, spellID)
    print("MaxDps Macro Support: bound spell " .. tostring(spellID) .. " to button=" .. tostring(GetButtonName(button) or "?") .. ", slot=" .. tostring(slot or "?") .. ", macro=" .. tostring(macroName or macroID or "?") .. ".")
end

local function BindSpellToSlot(spellToken, slotToken)
    local spellID = ResolveSpellText(spellToken)
    local slot = tonumber(slotToken)
    if not spellID or not slot then
        print("Usage: /mdms bindslot <spell id> <action slot>")
        return
    end

    local macroID = GetMacroIDFromActionSlot(slot)
    if not macroID then
        print("MaxDps Macro Support: action slot " .. tostring(slot) .. " is not a macro.")
        return
    end

    local macroName = SafeCall(GetMacroInfoCompat, macroID)
    local db = EnsureDB()
    db.manualBindings[tostring(spellID)] = {
        slot = slot,
        macroID = macroID,
        macroName = macroName,
    }

    ApplyManualBindings(MaxDps, spellID)
    print("MaxDps Macro Support: bound spell " .. tostring(spellID) .. " to action slot " .. tostring(slot) .. ".")
end

local function BindSpellToMacroName(spellToken, macroName)
    local spellID = ResolveSpellText(spellToken)
    macroName = Trim(macroName)
    if not spellID or not macroName or macroName == "" then
        print("Usage: /mdms bindmacro <spell id> <macro name>")
        return
    end

    local db = EnsureDB()
    db.manualBindings[tostring(spellID)] = { macroName = macroName }
    ApplyManualBindings(MaxDps, spellID)
    print("MaxDps Macro Support: bound spell " .. tostring(spellID) .. " to macro named " .. tostring(macroName) .. ".")
end

local function PrintBindings()
    local db = EnsureDB()
    local any = false
    for spellKey, binding in pairs(db.manualBindings) do
        any = true
        local spellID = tonumber(spellKey)
        local spellName = spellID and GetSpellNameSafe(spellID) or nil
        print("MaxDps Macro Support: " .. tostring(spellName or "Unknown Spell") .. " (" .. tostring(spellKey) .. ") -> button=" .. tostring(binding.buttonName or "?") .. ", slot=" .. tostring(binding.slot or "?") .. ", macro=" .. tostring(binding.macroName or binding.macroID or "?") .. ".")
    end

    if not any then
        print("MaxDps Macro Support: no manual bindings saved.")
    end
end

local function CollectSpellAliasIDs(spellID)
    local ids = {}
    local seen = {}
    local function add(id)
        id = tonumber(id)
        if id and not seen[id] then
            seen[id] = true
            ids[#ids + 1] = id
        end
    end

    add(spellID)
    add(FindBaseSpellByIDSafe(spellID))
    add(FindSpellOverrideByIDSafe(spellID))

    local name = GetSpellNameSafe(spellID)
    if name then
        local info = GetSpellInfoSafe(name)
        if type(info) == "table" then
            add(info.spellID)
        end
    end

    return ids
end

local function RemoveManualBindingMappings(spellID, binding)
    if not MaxDps or not MaxDps.Spells or not spellID or type(binding) ~= "table" then
        return 0
    end

    local removed = 0
    local aliasIDs = CollectSpellAliasIDs(spellID)

    ForEachKnownActionButton(function(button)
        if ButtonMatchesBinding(button, binding) then
            for _, id in ipairs(aliasIDs) do
                removed = removed + RemoveButtonFromSpellList(MaxDps, id, button)
            end
        end
    end)

    return removed
end

local function ClearAllBindings()
    local db = EnsureDB()
    local count = 0
    local removed = 0

    for spellKey, binding in pairs(db.manualBindings) do
        count = count + 1
        removed = removed + RemoveManualBindingMappings(tonumber(spellKey), binding)
    end

    db.manualBindings = {}
    ManualRuntime = {}
    ScheduleScan("manual-bindings-cleared", AUTO_SCAN_DELAY)
    print("MaxDps Macro Support: cleared " .. tostring(count) .. " manual binding(s); removed " .. tostring(removed) .. " cached mapping(s).")
end

local function ClearBinding(spellText)
    spellText = Trim(spellText)
    if not spellText or spellText == "" then
        print("Usage: /mdms unbind <spell id>  or  /mdms unbind all")
        return
    end

    local lowered = spellText:lower()
    if lowered == "all" or lowered == "clear" or lowered == "reset" then
        ClearAllBindings()
        return
    end

    local spellID = ResolveSpellText(spellText)
    if not spellID then
        print("Usage: /mdms unbind <spell id>  or  /mdms unbind all")
        return
    end

    local db = EnsureDB()
    local key = tostring(spellID)
    local binding = db.manualBindings[key]
    if not binding then
        print("MaxDps Macro Support: no manual binding exists for " .. tostring(spellID) .. ".")
        return
    end

    local removed = RemoveManualBindingMappings(spellID, binding)
    db.manualBindings[key] = nil
    ManualRuntime = {}
    ScheduleScan("manual-binding-cleared", AUTO_SCAN_DELAY)
    print("MaxDps Macro Support: cleared manual binding for " .. tostring(spellID) .. "; removed " .. tostring(removed) .. " cached mapping(s).")
end

local function PrintDebugLines()
    if #lastDebugLines == 0 then
        print("MaxDps Macro Support: no debug lines captured yet.")
        return
    end

    print("MaxDps Macro Support: recent debug lines:")
    for _, line in ipairs(lastDebugLines) do
        print("  " .. line)
    end
end

local function PrintMacroDump()
    local total = 0
    local macroSlots = CollectHotbarMacroSlots()
    for slot, macroID in pairs(macroSlots) do
        total = total + 1
        local macroName = SafeCall(GetMacroInfoCompat, macroID)
        local spells = GetAllSpellsFromMacro(macroID, slot)
        local list = {}
        for spellID, token in pairs(spells) do
            list[#list + 1] = tostring(spellID) .. "(" .. tostring(token) .. ")"
        end
        table.sort(list)
        print("MaxDps Macro Support: slot " .. tostring(slot) .. " macro=" .. tostring(macroName or macroID) .. " parsed spells=" .. table.concat(list, ", "))
    end

    if total == 0 then
        print("MaxDps Macro Support: no macro action slots found in slots 1-" .. tostring(MAX_SCAN_SLOTS) .. ".")
    end
end

local function PrintRawMacroDump()
    local total = 0
    local macroSlots = CollectHotbarMacroSlots()

    for slot, macroID in pairs(macroSlots) do
        total = total + 1
        local macroName, body, sourceKey, sourceLabel = GetMacroBodyFromMacroID(macroID, slot)
        local actionText = GetActionTextCompat and SafeCall(GetActionTextCompat, slot) or nil
        print("MaxDps Macro Support: RAW slot " .. tostring(slot) .. " macro=" .. tostring(macroName or macroID) .. " id=" .. tostring(macroID) .. " actionText=" .. tostring(actionText or "nil") .. " source=" .. tostring(sourceLabel or "nil"))

        if body then
            for line in tostring(body):gmatch("[^\r\n]+") do
                print("  line: " .. tostring(line))
                local command, args = line:match("^%s*/(%S+)%s*(.*)$")
                if command and args then
                    local normalizedCommand = command:lower()
                    print("    command=" .. tostring(normalizedCommand) .. " args=" .. tostring(args))
                    if macroCommands[normalizedCommand] then
                        local branchIndex = 0
                        for branch in args:gmatch("[^;]+") do
                            branchIndex = branchIndex + 1
                            local strippedBranch = RemoveMacroConditionals(branch)
                            print("    branch " .. tostring(branchIndex) .. " raw=" .. tostring(branch) .. " stripped=" .. tostring(strippedBranch))
                            if normalizedCommand == "castsequence" or normalizedCommand == "castrandom" or normalizedCommand == "randomcast" or normalizedCommand == "userandom" or normalizedCommand == "randomuse" then
                                local tokenIndex = 0
                                for token in strippedBranch:gmatch("[^,]+") do
                                    tokenIndex = tokenIndex + 1
                                    local cleanedToken = normalizedCommand == "castsequence" and StripCastSequenceOptions(token) or Trim(token)
                                    local spellID = ResolveSpellID(cleanedToken, normalizedCommand)
                                    print("      token " .. tostring(tokenIndex) .. " raw=" .. tostring(token) .. " cleaned=" .. tostring(cleanedToken) .. " resolved=" .. tostring(spellID or "nil"))
                                end
                            else
                                local cleanedToken = Trim(strippedBranch)
                                local spellID = ResolveSpellID(cleanedToken, normalizedCommand)
                                print("      token raw=" .. tostring(strippedBranch) .. " cleaned=" .. tostring(cleanedToken) .. " resolved=" .. tostring(spellID or "nil"))
                            end
                        end
                    else
                        print("    ignored command")
                    end
                else
                    print("    not a slash command line")
                end
            end
        else
            print("  no saved macro body returned by GetMacroInfo/GetActionText. If this is a secure macrotext button, use /mdms hover while mousing over it.")
        end
    end

    if total == 0 then
        print("MaxDps Macro Support: no macro action slots found in slots 1-" .. tostring(MAX_SCAN_SLOTS) .. ".")
    end
end

local function PrintMouseoverAttributeDump()
    local frame = GetMouseFocusCompat()
    if not frame then
        print("MaxDps Macro Support: no mouseover frame found.")
        return
    end

    print("MaxDps Macro Support: mouseover attribute dump. Hover the actual macro action button for best results.")
    local keys = {
        "type", "type1", "type2", "action", "macro", "macro1", "macro2",
        "macrotext", "macrotext1", "macrotext2", "*macrotext*",
        "unit", "spell", "item", "button", "checkselfcast", "checkfocuscast",
    }

    local printedAny = false
    local depth = 0
    while frame and depth < 8 do
        depth = depth + 1
        local frameName = GetButtonName(frame) or tostring(frame)
        local slot = GetActionSlotFromButton(frame)
        local macroID = slot and GetMacroIDFromActionSlot(slot) or nil
        local actionText = slot and GetActionTextCompat and SafeCall(GetActionTextCompat, slot) or nil
        print("  frame " .. tostring(depth) .. ": " .. tostring(frameName) .. " slot=" .. tostring(slot or "nil") .. " macroID=" .. tostring(macroID or "nil") .. " actionText=" .. tostring(actionText or "nil"))

        for _, key in ipairs(keys) do
            local value = SafeGetAttribute(frame, key)
            if value ~= nil then
                printedAny = true
                local display = tostring(value):gsub("\r", "\\r"):gsub("\n", "\\n")
                if #display > 220 then
                    display = display:sub(1, 220) .. "..."
                end
                print("    " .. tostring(key) .. " = " .. display)
            end
        end

        if type(frame.GetParent) == "function" then
            frame = SafeCall(frame.GetParent, frame)
        else
            frame = nil
        end
    end

    if not printedAny then
        print("MaxDps Macro Support: no known secure action attributes found on the mouseover frame or parents.")
    end
end

local function PrintCompatibilityWarnings()
    if not MaxDps then
        print("|cffff5555MaxDps Macro Support warning:|r MaxDps was not found. Install and enable MaxDPS Rotation Helper.")
        return
    end

    if not installed then
        print("|cffffaa00MaxDps Macro Support warning:|r the patch layer is not installed yet. Try /reload. If this continues, MaxDPS may have changed its internals.")
    end

    if installed and not originalGlowSpell then
        print("|cffffaa00MaxDps Macro Support warning:|r could not hook MaxDps:GlowSpell(). Macro detection may work, but direct glow fallback may be limited.")
    end

    if installed and not originalGlowClear then
        print("|cffffaa00MaxDps Macro Support warning:|r could not hook MaxDps:GlowClear(). Stale glow cleanup may be limited.")
    end
end

local function GetClientCompatibilityLine()
    local version, build, date, tocVersion = nil, nil, nil, nil

    if type(_G.GetBuildInfo) == "function" then
        version, build, date, tocVersion = SafeCall(_G.GetBuildInfo)
    end

    local status = "verified"
    if type(tocVersion) == "number" then
        if VERIFIED_INTERFACES[tocVersion] then
            status = "verified"
        elseif tocVersion > HIGHEST_VERIFIED_INTERFACE then
            status = "newer-than-verified"
        elseif tocVersion < LOWEST_VERIFIED_INTERFACE then
            status = "older-than-verified"
        else
            status = "between-verified-interfaces"
        end
    else
        status = "unknown"
    end

    return "Client: running=" .. tostring(version or "unknown") .. " interface=" .. tostring(tocVersion or "unknown") .. ", verified=" .. VERIFIED_RETAIL_CLIENT .. " interfaces=" .. VERIFIED_INTERFACE_TEXT .. ", status=" .. status
end

local function PrintStatus()
    local db = EnsureDB()
    local mode = tostring(db.performanceMode or "normal")
    local autoScanText = AutoScanEnabled() and "On" or "Off"
    local debugText = DebugCommandsEnabled() and "On" or "Off"

    print("MaxDps Macro Support v" .. VERSION)
    print("  " .. GetClientCompatibilityLine())
    print("  State: MaxDps=" .. tostring(MaxDps ~= nil) .. ", patch=" .. tostring(installed) .. ", mode=" .. mode .. ", autoscan=" .. autoScanText .. ", debug=" .. debugText .. ", hotbarOnly=true")
    print("  Queue: dirty=" .. tostring(scanDirty) .. ", scheduled=" .. tostring(scanScheduled) .. ", MacroUIOpen=" .. tostring(IsMacroUIOpen()) .. ", pendingMacroScan=" .. tostring(macroUIScanPending))
    print("  Last scan: reason=" .. tostring(scanStats.lastReason) .. ", ms=" .. string.format("%.2f", tonumber(scanStats.lastScanMs) or 0) .. ", slots=" .. tostring(scanStats.slotsScanned) .. ", hotbarMacros=" .. tostring(scanStats.hotbarMacros) .. ", candidateButtons=" .. tostring(scanStats.candidateButtons) .. ", hotbarButtons=" .. tostring(scanStats.hotbarButtons))
    print("  Cache: parsedBodies=" .. tostring(scanStats.bodiesParsed) .. ", cacheHits=" .. tostring(scanStats.cacheHits) .. ", bodyCacheEntries=" .. tostring(TableCount(BodyCache)))
    print("  Mappings: autoSpellIDs=" .. tostring(TableCount(AutoMappings)) .. ", manualBindings=" .. tostring(TableCount(db.manualBindings)) .. ", removedLastScan=" .. tostring(scanStats.removedMappings))
    print("  Glow: trackedNextGlows=" .. tostring(TableCount(TrackedNextGlowButtons)) .. ", staleGlowsCleared=" .. tostring(scanStats.staleGlowsCleared or 0) .. ", directGlows=" .. tostring(scanStats.directGlows or 0))
    PrintCompatibilityWarnings()
end


local function SetCheckButtonText(check, text)
    if not check then
        return
    end
    if check.Text then
        check.Text:SetText(text)
    elseif _G[check:GetName() and (check:GetName() .. "Text") or ""] then
        _G[check:GetName() .. "Text"]:SetText(text)
    end
end

local function CreateOptionsPanel()
    if optionsPanelCreated then
        return optionsPanel
    end

    optionsPanelCreated = true
    local panel = CreateFrame("Frame", "MaxDpsMacroSupportOptionsPanel")
    optionsPanel = panel
    panel.name = "MaxDPS Macro Support"

    panel:SetScript("OnShow", function(self)
        if self.initialized then
            local db = EnsureDB()
            if self.autoCheck then self.autoCheck:SetChecked(AutoScanEnabled()) end
            if self.debugCheck then self.debugCheck:SetChecked(DebugCommandsEnabled()) end
            if self.safeCheck then self.safeCheck:SetChecked(db.performanceMode == "safe") end
            return
        end

        self.initialized = true
        local db = EnsureDB()

        local title = self:CreateFontString(nil, "ARTWORK", "GameFontNormalLarge")
        title:SetPoint("TOPLEFT", 16, -16)
        title:SetText("MaxDPS Macro Support")

        local subtitle = self:CreateFontString(nil, "ARTWORK", "GameFontHighlightSmall")
        subtitle:SetPoint("TOPLEFT", title, "BOTTOMLEFT", 0, -8)
        subtitle:SetWidth(560)
        subtitle:SetJustifyH("LEFT")
        subtitle:SetText("Companion patch for MaxDPS Rotation Helper. Scans only hotbar macros, caches parsed macro bodies, and helps MaxDPS glow macro buttons that contain recommended spells.")

        local autoCheck = CreateFrame("CheckButton", nil, self, "InterfaceOptionsCheckButtonTemplate")
        autoCheck:SetPoint("TOPLEFT", subtitle, "BOTTOMLEFT", 0, -20)
        autoCheck:SetChecked(AutoScanEnabled())
        SetCheckButtonText(autoCheck, "Enable automatic hotbar rescans")
        autoCheck:SetScript("OnClick", function(button)
            SetAutoScanEnabled(button:GetChecked())
            if button:GetChecked() then
                ScheduleScan("options-autoscan", AUTO_SCAN_DELAY)
            end
        end)
        self.autoCheck = autoCheck

        local safeCheck = CreateFrame("CheckButton", nil, self, "InterfaceOptionsCheckButtonTemplate")
        safeCheck:SetPoint("TOPLEFT", autoCheck, "BOTTOMLEFT", 0, -6)
        safeCheck:SetChecked(db.performanceMode == "safe")
        SetCheckButtonText(safeCheck, "Performance safe mode: manual scans only")
        safeCheck:SetScript("OnClick", function(button)
            local checked = button:GetChecked()
            local current = EnsureDB()
            if checked then
                current.performanceMode = "safe"
                current.autoScanEnabled = false
                if self.autoCheck then self.autoCheck:SetChecked(false) end
            else
                current.performanceMode = "normal"
                current.autoScanEnabled = true
                if self.autoCheck then self.autoCheck:SetChecked(true) end
                ScheduleScan("options-normal", AUTO_SCAN_DELAY)
            end
        end)
        self.safeCheck = safeCheck

        local debugCheck = CreateFrame("CheckButton", nil, self, "InterfaceOptionsCheckButtonTemplate")
        debugCheck:SetPoint("TOPLEFT", safeCheck, "BOTTOMLEFT", 0, -6)
        debugCheck:SetChecked(DebugCommandsEnabled())
        SetCheckButtonText(debugCheck, "Enable debug commands and diagnostics")
        debugCheck:SetScript("OnClick", function(button)
            SetDebugEnabled(button:GetChecked())
        end)
        self.debugCheck = debugCheck

        local scanButton = CreateFrame("Button", nil, self, "UIPanelButtonTemplate")
        scanButton:SetSize(130, 24)
        scanButton:SetPoint("TOPLEFT", debugCheck, "BOTTOMLEFT", 0, -20)
        scanButton:SetText("Scan Now")
        scanButton:SetScript("OnClick", function()
            if MaxDps then
                local found = FullMacroScan(MaxDps, nil, true, "options-scan")
                print("MaxDps Macro Support: scan complete. hotbar macro buttons=" .. tostring(found) .. ", scanMs=" .. string.format("%.2f", tonumber(scanStats.lastScanMs) or 0) .. ".")
            else
                PrintCompatibilityWarnings()
            end
        end)

        local clearGlowButton = CreateFrame("Button", nil, self, "UIPanelButtonTemplate")
        clearGlowButton:SetSize(130, 24)
        clearGlowButton:SetPoint("LEFT", scanButton, "RIGHT", 8, 0)
        clearGlowButton:SetText("Clear Glows")
        clearGlowButton:SetScript("OnClick", function()
            local cleared = ClearKnownMacroGlows(MaxDps)
            print("MaxDps Macro Support: cleared " .. tostring(cleared) .. " tracked macro glow(s).")
        end)

        local clearBindingsButton = CreateFrame("Button", nil, self, "UIPanelButtonTemplate")
        clearBindingsButton:SetSize(150, 24)
        clearBindingsButton:SetPoint("LEFT", clearGlowButton, "RIGHT", 8, 0)
        clearBindingsButton:SetText("Clear Bindings")
        clearBindingsButton:SetScript("OnClick", function()
            ClearAllBindings()
        end)

        local statusButton = CreateFrame("Button", nil, self, "UIPanelButtonTemplate")
        statusButton:SetSize(130, 24)
        statusButton:SetPoint("TOPLEFT", scanButton, "BOTTOMLEFT", 0, -8)
        statusButton:SetText("Print Status")
        statusButton:SetScript("OnClick", PrintStatus)

        local note = self:CreateFontString(nil, "ARTWORK", "GameFontDisableSmall")
        note:SetPoint("TOPLEFT", statusButton, "BOTTOMLEFT", 0, -18)
        note:SetWidth(560)
        note:SetJustifyH("LEFT")
        note:SetText("This addon does not cast spells or automate gameplay. Some code was written with assistance from ChatGPT; Daxomault is the project author.")
    end)

    if Settings and type(Settings.RegisterCanvasLayoutCategory) == "function" and type(Settings.RegisterAddOnCategory) == "function" then
        local category = Settings.RegisterCanvasLayoutCategory(panel, panel.name)
        optionsCategory = category
        Settings.RegisterAddOnCategory(category)
    elseif type(_G.InterfaceOptions_AddCategory) == "function" then
        _G.InterfaceOptions_AddCategory(panel)
    end

    return panel
end

local function OpenOptionsPanel()
    CreateOptionsPanel()

    if Settings and type(Settings.OpenToCategory) == "function" and optionsCategory then
        local id = optionsCategory.ID
        if not id and type(optionsCategory.GetID) == "function" then
            id = optionsCategory:GetID()
        end
        SafeCall(Settings.OpenToCategory, id or optionsCategory)
        return
    end

    if type(_G.InterfaceOptionsFrame_OpenToCategory) == "function" and optionsPanel then
        SafeCall(_G.InterfaceOptionsFrame_OpenToCategory, optionsPanel)
        SafeCall(_G.InterfaceOptionsFrame_OpenToCategory, optionsPanel)
        return
    end

    print("MaxDps Macro Support: options panel registered. Open Game Menu > Options > AddOns > MaxDPS Macro Support.")
end

local function PrintSpellStatus(msg)
    local spellID = ResolveSpellText(msg)
    if not spellID then
        print("Usage: /mdms <spell name or spell id>")
        return
    end

    local found = FullMacroScan(MaxDps, spellID, true, "spell-test")
    local mapped = SpellHasButton(MaxDps, spellID)
    local name = GetSpellNameSafe(spellID) or "Unknown"
    print("MaxDps Macro Support: " .. name .. " (" .. tostring(spellID) .. ") mapped=" .. tostring(mapped) .. ", matching macro/button hits=" .. tostring(found) .. ", scanMs=" .. string.format("%.2f", tonumber(scanStats.lastScanMs) or 0) .. ".")
    PrintDebugLines()
end

local function InstallSlashCommands()
    MDMS_CommandHandler = function(msg)
        msg = Trim(msg) or ""

        if msg == "help" or msg == "?" then
            print("MaxDps Macro Support v" .. VERSION .. " commands:")
            print("  /mdms scan - force hotbar-slot-only rescan")
            print("  /mdms status - show friendly status, warnings, timing, cache, and mapping counts")
            print("  /mdms options - open the addon options panel")
            print("  /mdms autoscan off|on - disable/enable automatic rescans")
            print("  /mdms perf safe|normal|debug - set performance mode")
            print("  /mdms debug on|off - enable/disable expensive debug commands")
            print("  /mdms clearglows - clear tracked macro next-glows")
            print("  /mdms macros - show parsed saved macro spells on hotbars (debug mode)")
            print("  /mdms rawmacros - show raw saved macro lines/tokens (debug mode)")
            print("  /mdms hover - inspect mouseover button attributes (debug mode)")
            print("  /mdms bind <spellID> - bind mouseover macro button")
            print("  /mdms bindslot <spellID> <slot> - bind by action slot")
            print("  /mdms bindmacro <spellID> <macro name> - bind by macro name")
            print("  /mdms unbind <spellID> - remove one manual binding")
            print("  /mdms unbind all - remove all manual bindings")
            print("  /mdms glow <spellID> - test glow")
            print("  /mdms bindings - list manual bindings")
            print("  /mdms loaded - verify slash command layer")
            return
        end

        if msg == "loaded" then
            print("MaxDps Macro Support: slash layer loaded. MaxDps loaded=" .. tostring(MaxDps ~= nil) .. ", patch installed=" .. tostring(installed) .. ", version=" .. VERSION .. ".")
            return
        end

        if msg == "status" then
            PrintStatus()
            return
        end

        if msg == "options" or msg == "option" or msg == "config" or msg == "settings" then
            OpenOptionsPanel()
            return
        end

        if msg == "debug on" then
            SetDebugEnabled(true)
            print("MaxDps Macro Support: debug commands enabled. Expensive diagnostics remain manual only.")
            return
        end

        if msg == "debug off" then
            SetDebugEnabled(false)
            if EnsureDB().performanceMode == "debug" then
                EnsureDB().performanceMode = "normal"
            end
            print("MaxDps Macro Support: debug commands disabled.")
            return
        end

        if msg == "autoscan off" then
            SetAutoScanEnabled(false)
            scanScheduled = false
            print("MaxDps Macro Support: automatic scans disabled. Use /mdms scan after changing macro buttons.")
            return
        end

        if msg == "autoscan on" then
            SetAutoScanEnabled(true)
            print("MaxDps Macro Support: automatic scans enabled. Scans are hotbar-only, debounced, and deferred while the Macro UI is open.")
            ScheduleScan("autoscan-on", AUTO_SCAN_DELAY)
            return
        end

        if msg:match("^perf%s+") then
            local mode = Trim(msg:gsub("^perf%s+", ""))
            local db = EnsureDB()
            if mode == "safe" then
                db.performanceMode = "safe"
                db.autoScanEnabled = false
                scanScheduled = false
                print("MaxDps Macro Support: performance mode set to safe. Automatic scans are off; use /mdms scan manually.")
            elseif mode == "normal" then
                db.performanceMode = "normal"
                db.autoScanEnabled = true
                print("MaxDps Macro Support: performance mode set to normal. Hotbar changes schedule debounced rescans.")
                ScheduleScan("perf-normal", AUTO_SCAN_DELAY)
            elseif mode == "debug" then
                db.performanceMode = "debug"
                db.autoScanEnabled = true
                db.debugEnabled = true
                print("MaxDps Macro Support: performance mode set to debug. Normal scans are still hotbar-only; debug commands are enabled.")
                ScheduleScan("perf-debug", AUTO_SCAN_DELAY)
            else
                print("Usage: /mdms perf safe|normal|debug")
            end
            return
        end

        if not MaxDps then
            print("MaxDps Macro Support: MaxDps is not loaded.")
            return
        end

        if msg == "clearglows" or msg == "clearglow" or msg == "clear glows" then
            local cleared = ClearKnownMacroGlows(MaxDps)
            print("MaxDps Macro Support: cleared " .. tostring(cleared) .. " tracked macro glow(s).")
            return
        end

        if msg == "" or msg == "scan" then
            local found = FullMacroScan(MaxDps, nil, true, "manual-scan")
            print("MaxDps Macro Support: scan complete. hotbar macro buttons=" .. tostring(found) .. ", scanMs=" .. string.format("%.2f", tonumber(scanStats.lastScanMs) or 0) .. ", slots=" .. tostring(scanStats.slotsScanned) .. ", hotbarMacros=" .. tostring(scanStats.hotbarMacros) .. ", candidateButtons=" .. tostring(scanStats.candidateButtons) .. ", parsedBodies=" .. tostring(scanStats.bodiesParsed) .. ", cacheHits=" .. tostring(scanStats.cacheHits) .. ".")
            return
        end

        if msg == "debug" or msg == "dump" then
            if DebugCommandsEnabled() then
                PrintDebugLines()
            else
                print("MaxDps Macro Support: debug commands are disabled. Use /mdms debug on to enable diagnostics.")
            end
            return
        end

        if msg == "hover" or msg == "mouseover" or msg == "attrs" or msg == "attributes" then
            if DebugCommandsEnabled() then
                PrintMouseoverAttributeDump()
            else
                print("MaxDps Macro Support: debug commands are disabled. Use /mdms debug on to enable hover diagnostics.")
            end
            return
        end

        if msg == "rawmacros" or msg == "rawmacro" or msg == "tokens" then
            if DebugCommandsEnabled() then
                PrintRawMacroDump()
            else
                print("MaxDps Macro Support: debug commands are disabled. Use /mdms debug on to enable raw macro diagnostics.")
            end
            return
        end

        if msg == "macros" or msg == "macrodump" then
            if DebugCommandsEnabled() then
                PrintMacroDump()
            else
                print("MaxDps Macro Support: debug commands are disabled. Use /mdms debug on to enable macro diagnostics.")
            end
            return
        end

        if msg == "bindings" then
            PrintBindings()
            return
        end

        if msg == "clearbinds" or msg == "clearbindings" or msg == "unbind all" or msg == "clearbind all" then
            ClearAllBindings()
            return
        end

        if msg:match("^unbind%s+") then
            ClearBinding(Trim(msg:gsub("^unbind%s+", "")))
            return
        end

        if msg:match("^clearbind%s+") then
            ClearBinding(Trim(msg:gsub("^clearbind%s+", "")))
            return
        end

        if msg:match("^bindslot%s+") then
            local spellToken, slotToken = msg:match("^bindslot%s+(%S+)%s+(%S+)")
            BindSpellToSlot(spellToken, slotToken)
            return
        end

        if msg:match("^bindmacro%s+") then
            local spellToken, macroName = msg:match("^bindmacro%s+(%S+)%s+(.+)$")
            BindSpellToMacroName(spellToken, macroName)
            return
        end

        if msg:match("^bind%s+") then
            BindSpellToMouseover(Trim(msg:gsub("^bind%s+", "")))
            return
        end

        if msg:match("^glow%s+") then
            local rest = Trim(msg:gsub("^glow%s+", ""))
            local spellID = ResolveSpellText(rest)
            if spellID then
                FullMacroScan(MaxDps, spellID, true, "manual-glow")
                local ok = GlowRegisteredMacroButtons(MaxDps, spellID)
                print("MaxDps Macro Support: manual glow for " .. tostring(spellID) .. " success=" .. tostring(ok) .. ".")
                PrintDebugLines()
            else
                print("Usage: /mdms glow <spell name or spell id>")
            end
            return
        end

        PrintSpellStatus(msg)
    end

    SlashCmdList.MAXDPSMACROSUPPORT = MDMS_CommandHandler
end

local function Install()
    if installed or not MaxDps then
        return
    end

    installed = true
    EnsureDB()

    MaxDps.GetAllSpellsFromMacro = GetAllSpellsFromMacro
    MaxDps.AddMacroButton = function(self, macroID, button)
        return AddSavedMacroButton(self, macroID, button, GetActionSlotFromButton(button), nil, AutoMappings, "autoMappings") > 0
    end

    if type(MaxDps.AddStandardButton) == "function" then
        hooksecurefunc(MaxDps, "AddStandardButton", function(self, button)
            -- Cheap hook: only inspect this one button if it is a hotbar macro button.
            local macroSlots = CollectHotbarMacroSlots()
            local isHotbarMacro, slot, macroID = ButtonIsHotbarMacroButton(button, macroSlots)
            if isHotbarMacro then
                if macroID then
                    AddSavedMacroButton(self, macroID, button, slot, nil, AutoMappings, "autoMappings")
                end
                AddMacroTextButton(self, button, slot, nil, AutoMappings, "autoMappings")
            end
        end)
    end

    if type(MaxDps.Fetch) == "function" then
        hooksecurefunc(MaxDps, "Fetch", function(self)
            -- MaxDps may clear/rebuild its button table. Reapply cached mappings first;
            -- if a hotbar event marked us dirty, schedule a cheap hotbar-only scan.
            ReapplyCachedMappings(self)
            if scanDirty then
                ScheduleScan("MaxDps.Fetch", AUTO_SCAN_DELAY)
            end
        end)
    end

    if type(MaxDps.GlowClear) == "function" and not originalGlowClear then
        originalGlowClear = MaxDps.GlowClear
        MaxDps.GlowClear = function(self)
            local result = SafeCall(originalGlowClear, self)
            -- MaxDps clears with its current Spells table. If a macro button moved,
            -- the old button may no longer be in that table, so clear our tracked
            -- next-glow buttons too.
            ClearTrackedNextGlows(self)
            return result
        end
    end

    if type(MaxDps.GlowSpell) == "function" and not originalGlowSpell then
        originalGlowSpell = MaxDps.GlowSpell
        MaxDps.GlowSpell = function(self, spellID)
            -- Never parse macros here. GlowSpell can fire while pressing actions.
            -- Only reapply already-cached mappings, then let MaxDps continue normally.
            if spellID and not SpellHasButton(self, spellID) then
                ReapplyCachedMappings(self)
            end
            local result = originalGlowSpell(self, spellID)
            TrackCurrentNextGlows(self)
            return result
        end
    end

    InstallSlashCommands()
    CreateOptionsPanel()
    ScheduleScan("login", 1.0)

    local db = EnsureDB()
    if not printedLoadMessage and DEFAULT_CHAT_FRAME and not db.firstRunMessageShown then
        printedLoadMessage = true
        db.firstRunMessageShown = true
        DEFAULT_CHAT_FRAME:AddMessage("|cff33ff99MaxDps Macro Support:|r v" .. VERSION .. " enabled. Use /mdms status, /mdms scan, or /mdms options. This addon does not cast spells.")
    end
end

InstallSlashCommands()
CreateOptionsPanel()

Patch:RegisterEvent("ADDON_LOADED")
Patch:RegisterEvent("PLAYER_LOGIN")
Patch:RegisterEvent("UPDATE_MACROS")
Patch:RegisterEvent("ACTIONBAR_SLOT_CHANGED")
Patch:RegisterEvent("ACTIONBAR_PAGE_CHANGED")
Patch:RegisterEvent("UPDATE_BONUS_ACTIONBAR")
Patch:RegisterEvent("UPDATE_OVERRIDE_ACTIONBAR")
Patch:RegisterEvent("UPDATE_VEHICLE_ACTIONBAR")
Patch:RegisterEvent("UPDATE_POSSESS_BAR")
Patch:RegisterEvent("UPDATE_SHAPESHIFT_FORM")
Patch:RegisterEvent("PET_BAR_UPDATE")
Patch:RegisterEvent("PLAYER_SPECIALIZATION_CHANGED")
Patch:RegisterEvent("PLAYER_REGEN_ENABLED")
Patch:SetScript("OnEvent", function(_, event, addonName)
    if event == "ADDON_LOADED" then
        if addonName == "Blizzard_MacroUI" then
            HookMacroFrameIfAvailable()
        end
        if addonName == "MaxDps" or addonName == ADDON_NAME then
            Install()
        end
        return
    end

    if event == "PLAYER_LOGIN" then
        Install()
        HookMacroFrameIfAvailable()
        if not MaxDps and not missingMaxDpsWarned and DEFAULT_CHAT_FRAME then
            missingMaxDpsWarned = true
            DEFAULT_CHAT_FRAME:AddMessage("|cffff5555MaxDps Macro Support:|r MaxDPS Rotation Helper was not found. Install/enable MaxDPS, then /reload.")
        end
        return
    end

    if event == "UPDATE_MACROS" then
        Install()
        if IsMacroUIOpen() then
            macroUIScanPending = true
            return
        end
        ScheduleScan("UPDATE_MACROS", MACRO_UPDATE_SCAN_DELAY)
        return
    end

    if event == "ACTIONBAR_SLOT_CHANGED"
        or event == "ACTIONBAR_PAGE_CHANGED"
        or event == "UPDATE_BONUS_ACTIONBAR"
        or event == "UPDATE_OVERRIDE_ACTIONBAR"
        or event == "UPDATE_VEHICLE_ACTIONBAR"
        or event == "UPDATE_POSSESS_BAR"
        or event == "UPDATE_SHAPESHIFT_FORM"
        or event == "PET_BAR_UPDATE" then
        Install()
        ScheduleScan(event, AUTO_SCAN_DELAY)
        return
    end

    if event == "PLAYER_SPECIALIZATION_CHANGED" then
        Install()
        BodyCache = {}
        ScheduleScan("PLAYER_SPECIALIZATION_CHANGED", 1.0)
        return
    end

    if event == "PLAYER_REGEN_ENABLED" then
        if scanDirty then
            ScheduleScan("PLAYER_REGEN_ENABLED", AUTO_SCAN_DELAY)
        end
        return
    end
end)
