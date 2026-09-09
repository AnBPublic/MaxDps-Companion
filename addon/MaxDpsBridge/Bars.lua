--- ============================ HEADER ============================
-- Builds a <texture> -> <binding string> map by walking whichever action bar
-- addon is loaded. This is the texture fallback for the Reader: the primary
-- path resolves spellID -> binding via MaxDps.Spells button HotKeys and the
-- action slot scan, and only falls back to this map when neither yields a
-- raw binding string.

local addonName, MDB = ...;

local pairs = pairs;
local tostring = tostring;

local Bindings = {};  -- [textureKey] = binding string
local Dirty = true;

-- Blizzard's default bar buttons carry a binding command that does not follow
-- from the frame name, so it has to be spelled out.
local DefaultBindingCommand = {
  ["ActionButton"]              = "ACTIONBUTTON",
  ["MultiBarBottomLeftButton"]  = "MULTIACTIONBAR1BUTTON",
  ["MultiBarBottomRightButton"] = "MULTIACTIONBAR2BUTTON",
  ["MultiBarRightButton"]       = "MULTIACTIONBAR3BUTTON",
  ["MultiBarLeftButton"]        = "MULTIACTIONBAR4BUTTON",
};

-- DiabolicUI names its buttons EngineBar<1-5>Button<1-12> and stores the
-- binding command on the button itself (SetBindingAction), so GetKeyBind()
-- is authoritative there.
local BarSets = {
  Diabolic = { {"EngineBar1Button", 12}, {"EngineBar2Button", 12}, {"EngineBar3Button", 12},
               {"EngineBar4Button", 12}, {"EngineBar5Button", 12} },
  Bartender = { {"BT4Button", 120} },
  ElvUI = { {"ElvUI_Bar1Button", 12}, {"ElvUI_Bar2Button", 12}, {"ElvUI_Bar3Button", 12},
            {"ElvUI_Bar4Button", 12}, {"ElvUI_Bar5Button", 12}, {"ElvUI_Bar6Button", 12} },
  Default = { {"ActionButton", 12}, {"MultiBarBottomLeftButton", 12}, {"MultiBarBottomRightButton", 12},
              {"MultiBarRightButton", 12}, {"MultiBarLeftButton", 12} },
};

local function ActiveBarSet ()
  -- Probe real button frames, not addon presence: modern ElvUI skins the
  -- Blizzard buttons (no ElvUI_Bar1Button frames), so presence alone picks
  -- an empty set and the texture map stays at bound=0.
  if _G.EngineBar1Button1 then return BarSets.Diabolic; end
  if _G.BT4Button1 then return BarSets.Bartender; end
  if _G.ElvUI_Bar1Button1 then return BarSets.ElvUI; end
  return BarSets.Default;
end

--[[*
  * @function ButtonBinding
  * @desc Best-effort binding string for an action button, whatever addon owns it.
  *]]
local function ButtonBinding (Button, ButtonName, Prefix, Index)
  -- DiabolicUI and other Engine-derived buttons.
  if Button.GetKeyBind then
    local Binding = Button:GetKeyBind();
    if Binding and Binding ~= "" then return Binding; end
  end
  -- Bartender4.
  if Button.config and Button.config.keyBoundTarget then
    local Binding = GetBindingKey(Button.config.keyBoundTarget);
    if Binding and Binding ~= "" then return Binding; end
  end
  -- Blizzard default bars.
  local Command = DefaultBindingCommand[Prefix];
  if Command then
    local Binding = GetBindingKey(Command .. Index);
    if Binding and Binding ~= "" then return Binding; end
  end
  -- Anything bound through the click-cast mechanism.
  local Binding = GetBindingKey("CLICK " .. ButtonName .. ":LeftButton");
  if Binding and Binding ~= "" then return Binding; end
  return nil;
end

--[[*
  * @function ButtonTexture
  * @desc The icon a button actually shows. Macros resolve to the spell they cast
  *       so that a macro'd ability still matches the suggested spell.
  *]]
local function ButtonTexture (Button)
  if not Button.icon or not Button.icon:IsShown() then return nil; end

  local Texture = Button.icon:GetTexture();
  if not Texture then return nil; end

  local Slot = Button.action or (Button.GetPagedID and Button:GetPagedID())
    or (_G.ActionButton_GetPagedID and _G.ActionButton_GetPagedID(Button));
  if Slot then
    local ActionType, ActionID = GetActionInfo(Slot);
    if ActionType == "macro" then
      local _, _, MacroSpellID = GetMacroSpell(ActionID);
      if not MacroSpellID then return nil; end
      Texture = GetSpellTexture(MacroSpellID);
    end
  end

  return Texture;
end

local function Rebuild ()
  wipe(Bindings);
  -- Reader caches spellID -> VK; bar contents just changed, drop it too.
  if MDB._BindCache then wipe(MDB._BindCache); end
  local Bars = ActiveBarSet();
  for i = 1, #Bars do
    local Prefix, Count = Bars[i][1], Bars[i][2];
    for j = 1, Count do
      local ButtonName = Prefix .. j;
      local Button = _G[ButtonName];
      if Button then
        local Texture = ButtonTexture(Button);
        if Texture then
          local Binding = ButtonBinding(Button, ButtonName, Prefix, j);
          -- First bar wins, matching the visual priority of the bars.
          if Binding and not Bindings[tostring(Texture)] then
            Bindings[tostring(Texture)] = Binding;
          end
        end
      end
    end
  end
  Dirty = false;
end

--[[*
  * @function MDB.BindingForTexture
  * @desc Binding string bound to the action bar slot showing <Texture>, or nil.
  *]]
function MDB.BindingForTexture (Texture)
  if not Texture then return nil; end
  if Dirty then Rebuild(); end
  return Bindings[tostring(Texture)];
end

function MDB.InvalidateBindings ()
  Dirty = true;
end

function MDB.BindingCount ()
  if Dirty then Rebuild(); end
  local Count = 0;
  for _ in pairs(Bindings) do Count = Count + 1; end
  return Count;
end

do
  local Listener = CreateFrame("Frame");
  Listener:RegisterEvent("PLAYER_ENTERING_WORLD");
  Listener:RegisterEvent("ACTIONBAR_SLOT_CHANGED");
  Listener:RegisterEvent("ACTIONBAR_PAGE_CHANGED");
  Listener:RegisterEvent("UPDATE_BINDINGS");
  Listener:RegisterEvent("UPDATE_SHAPESHIFT_FORM");
  Listener:RegisterEvent("PLAYER_SPECIALIZATION_CHANGED");
  Listener:RegisterEvent("PLAYER_TALENT_UPDATE");
  Listener:RegisterEvent("LEARNED_SPELL_IN_TAB");
  Listener:RegisterEvent("UPDATE_MACROS");
  Listener:SetScript("OnEvent", function ()
    -- Bars repaint a frame late, so defer instead of reading stale icons.
    C_Timer.After(0.1, MDB.InvalidateBindings);
  end);
end
