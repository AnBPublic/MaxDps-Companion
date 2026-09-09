local SINGLE_BUTTON_ASSISTANT_SPELL_ID = 1229376

local function IsConsolePortActionButton(button)
    if not button or not button.GetAttribute then
        return false
    end

    if not CPAPI or not CPAPI.ActionButtonGUID then
        return false
    end

    local succeeded, isConsolePortButton = pcall(
        button.GetAttribute,
        button,
        CPAPI.ActionButtonGUID
    )

    return succeeded and isConsolePortButton == true
end

local function ContainsButton(buttons, targetButton)
    for _, button in ipairs(buttons) do
        if button == targetButton then
            return true
        end
    end

    return false
end

local function RestoreConsolePortButton(maxDps, spellId, button)
    if not spellId or spellId == SINGLE_BUTTON_ASSISTANT_SPELL_ID then
        return
    end

    if not IsConsolePortActionButton(button) or type(maxDps.Spells) ~= "table" then
        return
    end

    local buttons = maxDps.Spells[spellId]
    if not buttons then
        buttons = {}
        maxDps.Spells[spellId] = buttons
    end

    if not ContainsButton(buttons, button) then
        table.insert(buttons, button)
    end
end

if MaxDps and type(MaxDps.AddButton) == "function" then
    hooksecurefunc(MaxDps, "AddButton", RestoreConsolePortButton)
end
