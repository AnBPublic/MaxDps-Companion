--- ============================ HEADER ============================
-- Settings integration for the bridge. Mirrors MaxDps's own options pattern
-- (Options.lua:AddCustomGlowOptions/AddSpellFrameOptions): a StdUi
-- PanelWithTitle with `.name` + `.parent = 'MaxDps'` and a Midnight
-- RegisterCanvasLayoutSubcategory registration, so 'MaxDps Bridge' lands
-- directly below the MaxDps panel as a subcategory.
--
-- Every external call is guarded: a missing StdUi, a renamed Blizzard API,
-- or MaxDps loading in a different order must never error.

local addonName, MDB = ...;

local PANEL_NAME = "MaxDps Bridge";

local function RefreshStatus (Frame)
  if not Frame or not Frame.StatusText then return; end
  local DB = MaxDpsBridgeDB;
  local Main = MDB.GetMainSpellID and MDB.GetMainSpellID() or nil;
  local Bound = 0;
  if MDB.BindingCount then
    local Ok, Count = pcall(MDB.BindingCount);
    if Ok and type(Count) == "number" then Bound = Count; end
  end
  Frame.StatusText:SetText(
    ("MaxDpsBridge v%s\nEnabled: %s\nCell size: %d px\nBound textures: %d\nMaxDps.Spell: %s\nNextSpell: %s")
    :format(MDB.VERSION or "?", tostring(DB and DB.Enabled),
      DB and DB.CellSize or 8, Bound, tostring(Main),
      _G.MaxDps and type(_G.MaxDps.NextSpell) or "no-engine"));
end

local function BuildControlsStdUi (Panel, StdUi)
  local DB = MaxDpsBridgeDB;
  StdUi:EasyLayout(Panel, { padding = { top = 40 } });

  local Header = StdUi:Label(Panel, "Pixel bridge", 14);
  pcall(StdUi.SetTextColor, StdUi, Header, "header");

  local Enabled = StdUi:Checkbox(Panel, "Enable bridge", 240, 24);
  Enabled:SetChecked(DB.Enabled and true or false);
  Enabled.OnValueChanged = function (_, Flag) DB.Enabled = (Flag and true or false); end;

  local CellSize = StdUi:SliderWithBox(Panel, 160, 48, DB.CellSize, 1, 64);
  CellSize:SetPrecision(0);
  StdUi:AddLabel(Panel, CellSize, "Cell size (px, 1 = single pixel)");
  CellSize.OnValueChanged = function (_, Value)
    DB.CellSize = math.max(1, math.min(64, math.floor(Value + 0.5)));
    if MDB.Layout then MDB.Layout(); end
  end

  local Status = StdUi:Label(Panel, "", 12);
  Panel.StatusText = Status;

  Panel:AddRow():AddElement(Header);
  Panel:AddRow():AddElement(Enabled);
  Panel:AddRow():AddElement(CellSize);
  Panel:AddRow():AddElement(Status);

  Panel:SetScript("OnShow", function (self)
    self:DoLayout();
    RefreshStatus(self);
  end);
end

local function BuildControlsFallback (Panel)
  local DB = MaxDpsBridgeDB;

  local Enabled = CreateFrame("CheckButton", nil, Panel, "InterfaceOptionsCheckButtonTemplate");
  Enabled:SetPoint("TOPLEFT", Panel, "TOPLEFT", 16, -48);
  Enabled.Text:SetText("Enable bridge");
  Enabled:SetChecked(DB.Enabled and true or false);
  Enabled:SetScript("OnClick", function (self)
    DB.Enabled = self:GetChecked() and true or false;
  end);

  local CellSize = CreateFrame("Slider", nil, Panel, "OptionsSliderTemplate");
  CellSize:SetPoint("TOPLEFT", Enabled, "BOTTOMLEFT", 0, -32);
  CellSize:SetWidth(160);
  CellSize:SetMinMaxValues(1, 64);
  CellSize:SetValueStep(1);
  CellSize:SetValue(DB.CellSize);
  CellSize:SetScript("OnValueChanged", function (_, Value)
    DB.CellSize = math.max(1, math.min(64, math.floor(Value + 0.5)));
    if MDB.Layout then MDB.Layout(); end
  end);
  CellSize.Low:SetText("1");
  CellSize.High:SetText("64");
  CellSize.Text:SetText("Cell size (px)");

  local Status = Panel:CreateFontString(nil, "OVERLAY", "GameFontNormal");
  Status:SetPoint("TOPLEFT", CellSize, "BOTTOMLEFT", 0, -32);
  Status:SetJustifyH("LEFT");
  Panel.StatusText = Status;
end

local function BuildPanel ()
  if MDB._OptionsBuilt then return; end
  MDB._OptionsBuilt = true;

  local Panel;
  local StdUi = _G.StdUi;
  local Ok, Result = pcall(function ()
    if StdUi and StdUi.PanelWithTitle then
      -- MaxDps's own sub-panel pattern: child of the MaxDps category so the
      -- bridge lands exactly below MaxDps in the settings tree.
      local Sub = StdUi:PanelWithTitle(nil, 100, 100, PANEL_NAME);
      Sub.name = "MaxDps Bridge";
      Sub.parent = "MaxDps";
      return Sub;
    end
    local Plain = CreateFrame("Frame", "MaxDpsBridgeOptions", UIParent);
    Plain.name = PANEL_NAME;
    Plain.parent = "MaxDps";
    local Title = Plain:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge");
    Title:SetPoint("TOPLEFT", Plain, "TOPLEFT", 16, -16);
    Title:SetText(PANEL_NAME);
    return Plain;
  end);
  if not Ok or not Result then return; end
  Panel = Result;
  Panel.name = "MaxDps Bridge";
  Panel.parent = "MaxDps";
  Panel:Hide();

  pcall(function ()
    if StdUi and StdUi.EasyLayout and StdUi.Checkbox then
      BuildControlsStdUi(Panel, StdUi);
    else
      BuildControlsFallback(Panel);
    end
  end);

  Panel:SetScript("OnShow", function (self) RefreshStatus(self); end);
  RefreshStatus(Panel);

  -- MaxDps's own pattern (Options.lua:393-398 + AddCustomGlowOptions:493-498):
  -- legacy InterfaceOptions_AddCategory, else RegisterCanvasLayoutCategory
  -- for top-level and RegisterCanvasLayoutSubcategory under the MaxDps
  -- settingsCategory for children (do not override the main category).
  if type(InterfaceOptions_AddCategory) == "function" then
    pcall(InterfaceOptions_AddCategory, Panel);
  elseif _G.Settings and type(_G.Settings.RegisterCanvasLayoutSubcategory) == "function" then
    pcall(function ()
      local MaxDps = _G.MaxDps;
      local Parent = MaxDps and MaxDps.settingsCategory or nil;
      if Parent then
        _G.Settings.RegisterCanvasLayoutSubcategory(Parent, Panel, Panel.name);
      elseif type(_G.Settings.RegisterCanvasLayoutCategory) == "function"
        and type(_G.Settings.RegisterAddOnCategory) == "function" then
        local Category = _G.Settings.RegisterCanvasLayoutCategory(Panel, Panel.name);
        _G.Settings.RegisterAddOnCategory(Category);
      end
    end);
  end
end

-- Hook after MaxDps builds its own options frame so the subcategory has a
-- parent category to attach to; build directly too, in case MaxDps already
-- built (or never builds) — then we fall back to a top-level category.
do
  local MaxDps = _G.MaxDps;
  if MaxDps and type(MaxDps.AddToBlizzardOptions) == "function"
    and type(hooksecurefunc) == "function" then
    pcall(hooksecurefunc, MaxDps, "AddToBlizzardOptions", function ()
      if MaxDpsBridgeDB then BuildPanel(); end
    end);
  end

  local Waiter = CreateFrame("Frame");
  Waiter:RegisterEvent("ADDON_LOADED");
  Waiter:RegisterEvent("PLAYER_LOGIN");
  Waiter:SetScript("OnEvent", function (self, Event, Arg1)
    if Event == "ADDON_LOADED" and Arg1 ~= addonName then return; end
    -- ADDON_LOADED fires before our own defaults exist on first run for the
    -- login event ordering; PLAYER_LOGIN is the safe point, ADDON_LOADED a
    -- retry for /reload flows. MaxDps builds lazily, so the hook above
    -- covers panels built after login; poll once more in case we raced it.
    if not MaxDpsBridgeDB then return; end
    local MaxDpsNow = _G.MaxDps;
    if MaxDpsNow and MaxDpsNow.optionsFrame then
      BuildPanel();
      self:UnregisterAllEvents();
    elseif Event == "PLAYER_LOGIN" then
      if C_Timer and C_Timer.After then
        C_Timer.After(5, function ()
          if _G.MaxDps and _G.MaxDps.optionsFrame then BuildPanel(); end
        end);
      end
    end
  end);
end
