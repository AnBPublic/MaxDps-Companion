--- ============================ HEADER ============================
-- Builds a <texture> -> <binding string> map by walking whichever action bar
-- addon is loaded. This is the texture fallback for the Reader: the primary
-- path resolves spellID -> binding via MaxDps.Spells button HotKeys and the
-- action slot scan, and only falls back to this map when neither yields a
-- raw binding string.

local addonName, MDBX = ...;

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
  -- v1.3.0 zero-taint: the whole texture read runs under
  -- dropsecretaccess() containment — every icon/macro/slot return inside
  -- is provably plain, so plain `==` below cannot throw. Without
  -- containment, secret macro/action IDs detonate on compare (Sep-2026).
  -- A failure anywhere degrades to the un-resolved icon (fallback simply
  -- skips that button — HotKey/action-bar paths cover the spell).
  local Ok, Texture = pcall(function ()
    if type(dropsecretaccess) == "function" then dropsecretaccess(); end
    if not Button.icon or not Button.icon:IsShown() then return nil; end
    local Tex = Button.icon:GetTexture();
    if not Tex then return nil; end
    local Slot = Button.action or (Button.GetPagedID and Button:GetPagedID())
      or (_G.ActionButton_GetPagedID and _G.ActionButton_GetPagedID(Button));
    if Slot then
      local ActionType, ActionID = GetActionInfo(Slot);
      if ActionType == "macro" then
        local _, _, MacroSpellID = GetMacroSpell(ActionID);
        if not MacroSpellID then return Tex; end
        local MacroTexture = GetSpellTexture(MacroSpellID);
        if MacroTexture then Tex = MacroTexture; end
      end
    end
    return Tex;
  end);
  if Ok then return Texture; end
  return nil;
end

local function Rebuild ()
  wipe(Bindings);
  -- Reader caches spellID -> VK; bar contents just changed, drop it too.
  if MDBX._BindCache then wipe(MDBX._BindCache); end
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

--- ======= EVENT ROUTING (A1.1 + A1.8) =======
-- SPELL_UPDATE_COOLDOWN is a READINESS-ONLY signal. Reader.ResolveBinding
-- never reads cooldown state: ResolveBindingUncached reads the MaxDps overlay
-- HotKey text, the SpellFrame bindText, the action-bar scan (GetActionInfo /
-- GetBindingKey) and this file's texture map, and every call site
-- (WriteSlot, FirstFlagged, WalkCurated, ExtraCandidates) gates readiness
-- BEFORE it resolves a binding. A cached Miss therefore never depends on
-- cooldown state, so a cooldown event only bumps _BindRevision (Bridge
-- re-scans the slots) — it must not wipe the VK cache or dirty the 180-slot
-- texture walk.
-- Everything that CAN change which spell sits in a slot or which key is bound
-- takes the full path: cache wipe + texture map rebuild + revision bump,
-- coalesced to at most one wipe per 0.25 s, deferred 0.1 s because bars
-- repaint a frame late.
local FULL_DEBOUNCE = 0.25;
local FLUSH_DELAY = 0.1;
local LastFullAt = nil;
local FullPending = false;
local FlushScheduled = false;

local function NowSeconds ()
  if type(GetTime) ~= "function" then return nil; end
  local Ok, T = pcall(GetTime);
  if Ok and type(T) == "number" then return T; end
  return nil;
end

local function ApplyFullInvalidate ()
  FullPending = false;
  FlushScheduled = false;
  LastFullAt = NowSeconds() or LastFullAt;
  MDBX.InvalidateBindings();
end

--[[*
  * @function MDBX.BarsFlush
  * @desc Apply a scheduled full invalidation now (the C_Timer backstop, and
  *       the deterministic hook the offline harness drives).
  * @return boolean true when a pending wipe was applied
  *]]
function MDBX.BarsFlush ()
  if not FullPending then return false; end
  ApplyFullInvalidate();
  return true;
end

--[[*
  * @function MDBX.BarsEvent
  * @desc Route one bar-family event. Returns "revision" (cooldown-only, cheap),
  *       "full" (wipe scheduled), or "coalesced" (folded into the scheduled
  *       one so a burst costs a single wipe).
  *]]
function MDBX.BarsEvent (Event)
  if Event == "SPELL_UPDATE_COOLDOWN" then
    MDBX._BindRevision = (MDBX._BindRevision or 0) + 1;
    return "revision";
  end
  if FlushScheduled then
    FullPending = true;
    return "coalesced";
  end
  local Now = NowSeconds();
  local Delay = FLUSH_DELAY;
  if Now ~= nil and LastFullAt ~= nil then
    local Since = Now - LastFullAt;
    if Since < FULL_DEBOUNCE then
      local Wait = FULL_DEBOUNCE - Since;
      if Wait > Delay then Delay = Wait; end
    end
  end
  FlushScheduled = true;
  FullPending = true;
  if type(C_Timer) == "table" and type(C_Timer.After) == "function" then
    C_Timer.After(Delay, MDBX.BarsFlush);
  else
    MDBX.BarsFlush();   -- no scheduler (offline harness / ancient client)
  end
  return "full";
end

--[[*
  * @function MDBX.BindingForTexture
  * @desc Binding string bound to the action bar slot showing <Texture>, or nil.
  *]]
function MDBX.BindingForTexture (Texture)
  if not Texture then return nil; end
  if FullPending then ApplyFullInvalidate(); end
  if Dirty then Rebuild(); end
  return Bindings[tostring(Texture)];
end

function MDBX.InvalidateBindings ()
  Dirty = true;
  -- PERF (Stream 2, 3.4.0): bump the binding/readiness revision so
  -- Bridge.FrameKey invalidates the cached slot scan. Bars repaint a frame
  -- late, cooldown resets fire SPELL_UPDATE_COOLDOWN, and both change which
  -- candidate is ready/bound without any upstream table identity changing.
  MDBX._BindRevision = (MDBX._BindRevision or 0) + 1;
  -- The per-spell VK cache is wiped here so a bar / binding change is
  -- reflected on the very next tick, not whenever the texture fallback
  -- happens to run.
  if MDBX._BindCache then wipe(MDBX._BindCache); end
end

function MDBX.BindingCount ()
  if FullPending then ApplyFullInvalidate(); end
  if Dirty then Rebuild(); end
  local Count = 0;
  for _ in pairs(Bindings) do Count = Count + 1; end
  return Count;
end

do
  local Listener = CreateFrame("Frame");
  -- Events that can change slot contents or keybindings, including the
  -- bar-family events added in A1.8 (vehicle / override / bonus bars).
  local FullEvents = {
    "PLAYER_ENTERING_WORLD", "ACTIONBAR_SLOT_CHANGED", "ACTIONBAR_PAGE_CHANGED",
    "UPDATE_BINDINGS", "UPDATE_SHAPESHIFT_FORM", "PLAYER_SPECIALIZATION_CHANGED",
    "PLAYER_TALENT_UPDATE", "SPELLS_CHANGED", "UPDATE_MACROS",
    "UNIT_ENTERED_VEHICLE", "UNIT_EXITED_VEHICLE",
    "UPDATE_VEHICLE_ACTIONBAR", "UPDATE_OVERRIDE_ACTIONBAR",
    "UPDATE_BONUS_ACTIONBAR",
  };
  for i = 1, #FullEvents do Listener:RegisterEvent(FullEvents[i]); end
  -- Readiness-only: revision bump, never a cache/map wipe (A1.1).
  Listener:RegisterEvent("SPELL_UPDATE_COOLDOWN");
  Listener:SetScript("OnEvent", function (_, Event)
    MDBX.BarsEvent(Event);
  end);
end
