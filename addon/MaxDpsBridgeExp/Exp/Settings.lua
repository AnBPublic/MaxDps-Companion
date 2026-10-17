--- ============================ HEADER ============================
-- MaxDpsBridgeExp - in-game Settings page.
--
-- Registers a real Settings canvas category (Settings.RegisterCanvasLayout-
-- Category + Settings.RegisterAddOnCategory, 10.x+) and falls back to the
-- legacy InterfaceOptions_AddCategory when that API is absent. Six tabs:
-- Pause / Rotation / Binds / Settings / Console / Debug.
--
-- The top pill row is READ-ONLY local state (APP / SOURCE / GAME PICTURE /
-- FRAMES / ROTATION) - it never writes gameplay state. Every control is an
-- ordinary insecure widget; nothing here is on the pixel path and nothing
-- calls protected Lua. Console renders the Core ring buffer (200 lines).
--
-- Open with `/mdbx cfg` or the AddOns/Interface options list.

local addonName, MDBX = ...;

local S = {};
MDBX.Settings = S;

local CATEGORY_NAME = "MaxDps Bridge Exp";
local TABS = { "Pause", "Rotation", "Binds", "Settings", "Console", "Debug" };
local TAB_INDEX = {};
for i = 1, #TABS do TAB_INDEX[TABS[i]] = i; end;

local Frame, Content, TabButtons, Panes, ConsoleText, ConsoleScroll;
local Built = false;

local FALLBACK_FONT = "GameFontNormalSmall";
local TAB_DEFAULTS = { autoPause = false, pauseKey = "" };

local function DB ()
  if type(_G.MaxDpsBridgeExpDB) ~= "table" then _G.MaxDpsBridgeExpDB = {}; end;
  local D = _G.MaxDpsBridgeExpDB;
  if type(D.ui) ~= "table" then D.ui = { lastTab = 1, consoleMax = 200 }; end;
  if type(D.ui.lastTab) ~= "number" then D.ui.lastTab = 1; end;
  for Key, Value in pairs(TAB_DEFAULTS) do
    if D[Key] == nil then D[Key] = Value; end;
  end;
  return D;
end

local function Label (Parent, Text, X, Y, Font)
  local FS = Parent:CreateFontString(nil, "OVERLAY", Font or FALLBACK_FONT);
  FS:SetPoint("TOPLEFT", X, Y);
  FS:SetText(Text);
  FS:SetJustifyH("LEFT");
  return FS;
end

local function MakeCheck (Parent, Text, X, Y, Getter, Setter)
  local CB = CreateFrame("CheckButton", nil, Parent, "InterfaceOptionsCheckButtonTemplate");
  CB:SetPoint("TOPLEFT", X, Y);
  local Fs = CB:CreateFontString(nil, "OVERLAY", FALLBACK_FONT);
  Fs:SetPoint("LEFT", CB, "RIGHT", 4, 0);
  Fs:SetText(Text);
  Fs:SetJustifyH("LEFT");
  CB:SetScript("OnClick", function (self)
    if Setter then Setter(self:GetChecked() and true or false); end;
  end);
  CB.Refresh = function ()
    if Getter then CB:SetChecked(Getter() and true or false); end;
  end;
  CB:Refresh();
  S.Checks = S.Checks or {};
  S.Checks[#S.Checks + 1] = CB;
  return CB;
end

local function MakeButton (Parent, Text, Width, X, Y, OnClick)
  local Btn = CreateFrame("Button", nil, Parent, "UIPanelButtonTemplate");
  Btn:SetSize(Width or 110, 22);
  Btn:SetPoint("TOPLEFT", X, Y);
  Btn:SetText(Text);
  Btn:SetScript("OnClick", function () if OnClick then pcall(OnClick); end end);
  return Btn;
end

local function MakeStepper (Parent, Text, X, Y, Min, Max, Step, Getter, Setter)
  local Row = CreateFrame("Frame", nil, Parent);
  Row:SetSize(300, 24);
  Row:SetPoint("TOPLEFT", X, Y);
  Label(Row, Text, 0, -4);
  local Value = Label(Row, "?", 190, -4);
  local Minus = CreateFrame("Button", nil, Row, "UIPanelButtonTemplate");
  Minus:SetSize(22, 20);
  Minus:SetPoint("TOPLEFT", 210, 0);
  Minus:SetText("-");
  local Plus = CreateFrame("Button", nil, Row, "UIPanelButtonTemplate");
  Plus:SetSize(22, 20);
  Plus:SetPoint("TOPLEFT", 234, 0);
  Plus:SetText("+");
  local function Sync ()
    local V = Getter and Getter() or Min;
    Value:SetText(tostring(V));
    Minus:SetEnabled(V > Min);
    Plus:SetEnabled(V < Max);
  end
  Minus:SetScript("OnClick", function ()
    local V = (Getter and Getter() or Min) - Step;
    if V < Min then V = Min; end;
    if Setter then Setter(V); end;
    Sync();
  end);
  Plus:SetScript("OnClick", function ()
    local V = (Getter and Getter() or Min) + Step;
    if V > Max then V = Max; end;
    if Setter then Setter(V); end;
    Sync();
  end);
  Row.Refresh = Sync;
  Sync();
  S.Steppers = S.Steppers or {};
  S.Steppers[#S.Steppers + 1] = Row;
  return Row;
end

-- ---- panes ---------------------------------------------------------------
local function NewPane (Index)
  local Pane = CreateFrame("Frame", nil, Content);
  Pane:SetAllPoints(Content);
  Pane:Hide();
  Panes[Index] = Pane;
  return Pane;
end

local function TopPills (Pane)
  local Names = { "APP", "SOURCE", "GAME PICTURE", "FRAMES", "ROTATION" };
  local X = 0;
  for i = 1, #Names do
    local Pill = CreateFrame("Frame", nil, Pane, BackdropTemplateMixin and "BackdropTemplate" or nil);
    Pill:SetSize(100, 20);
    Pill:SetPoint("TOPLEFT", X, 0);
    if Pill.SetBackdrop then
      Pill:SetBackdrop({ bgFile = "Interface\\Buttons\\WHITE8x8", edgeFile = "Interface\\Buttons\\WHITE8x8", edgeSize = 1 });
      Pill:SetBackdropColor(0.10, 0.12, 0.16, 1);
      Pill:SetBackdropBorderColor(0.25, 0.55, 0.75, 0.8);
    end
    local FS = Pill:CreateFontString(nil, "OVERLAY", FALLBACK_FONT);
    FS:SetPoint("CENTER", 0, 0);
    FS:SetText(Names[i]);
    FS:SetTextColor(0.7, 0.8, 0.9, 1);
    X = X + 104;
  end;
  local Note = Label(Pane, "(read-only local state)", 0, -24);
  Note:SetTextColor(0.6, 0.6, 0.6, 1);
  return 34;
end

local function BuildPause (Pane)
  local Y = -TopPills(Pane);
  MakeCheck(Pane, "Auto-pause while out of combat", 0, Y,
    function () return DB().autoPause == true; end,
    function (V) DB().autoPause = V; end);
  Y = Y - 28;
  Label(Pane, "Pause key: " .. tostring(DB().pauseKey or "(unset)"), 0, Y);
  Y = Y - 32;
  MakeButton(Pane, "Pause rotation", 120, 0, Y, function () if MDBX.SetEnabled then MDBX.SetEnabled(false); end end);
  MakeButton(Pane, "Resume rotation", 120, 126, Y, function () if MDBX.SetEnabled then MDBX.SetEnabled(true); end end);
end

local function BuildRotation (Pane)
  local Y = -TopPills(Pane);
  Label(Pane, "In-game toggles (authoritative; overlay-wins)", 0, Y);
  Y = Y - 20;
  local Keys = {};
  if MDBX.Profiles and MDBX.Profiles.ToggleKeys then Keys = MDBX.Profiles.ToggleKeys(); end;
  local Col = 0;
  for i = 1, #Keys do
    local Key = Keys[i];
    MakeCheck(Pane, MDBX.Toggles and MDBX.Toggles.Label(Key) or Key,
      (Col == 0) and 0 or 150, Y,
      function () return MDBX.Toggles.Get(Key) ~= false; end,
      function (V) MDBX.Toggles.Set(Key, V); if MDBX.Overlay then MDBX.Overlay.SetDirty(); end end);
    if Col == 1 then Y = Y - 24; Col = 0; else Col = 1; end;
  end;
  if Col == 1 then Y = Y - 24; end;
  Y = Y - 8;
  Label(Pane, "Per-spec sliders", 0, Y);
  Y = Y - 20;
  if MDBX.Profiles and MDBX.Profiles.SliderDefs then
    for i = 1, #MDBX.Profiles.SliderDefs do
      local Def = MDBX.Profiles.SliderDefs[i];
      MakeStepper(Pane, Def.Label, 0, Y, Def.Min, Def.Max, Def.Step,
        function () return MDBX.Profiles.GetSlider(Def.Key); end,
        function (V) MDBX.Profiles.SetSlider(Def.Key, V); end);
      Y = Y - 26;
    end;
  end;
end

local function BuildBinds (Pane)
  local Y = -TopPills(Pane);
  Label(Pane, "Bound actions (bind in the Keybindings panel)", 0, Y);
  Y = Y - 22;
  local Names = { "Overlay" };
  if MDBX.Profiles and MDBX.Profiles.ToggleKeys then
    local K = MDBX.Profiles.ToggleKeys();
    for i = 1, #K do Names[#Names + 1] = K[i]; end;
  end;
  for i = 1, #Names do
    Label(Pane, "  MDBX_TOGGLE_" .. string.upper(Names[i]) .. "  -  " .. Names[i], 0, Y);
    Y = Y - 18;
  end;
  MakeButton(Pane, "Open Keybindings", 150, 0, Y - 6, function ()
    if type(_G.ToggleKeyBindings) == "function" then
      _G.ToggleKeyBindings();
    elseif type(_G.InterfaceOptionsFrame_OpenToCategory) == "function" then
      pcall(_G.InterfaceOptionsFrame_OpenToCategory, "Keybindings");
    else
      MDBX.Print("open the Keybindings panel from the game menu");
    end
  end);
end

local function BuildSettings (Pane)
  local Y = -TopPills(Pane);
  Label(Pane, "Overlay geometry", 0, Y);
  Y = Y - 22;
  MakeStepper(Pane, "Scale (x100)", 0, Y, 50, 200, 10,
    function () return math.floor((MDBX.Overlay and MDBX.Overlay.GetScale and MDBX.Overlay.GetScale() or 1.0) * 100); end,
    function (V) if MDBX.Overlay then MDBX.Overlay.SetScale(V / 100); end end);
  Y = Y - 26;
  MakeStepper(Pane, "Alpha (x100)", 0, Y, 20, 100, 10,
    function () return math.floor((MDBX.Overlay and MDBX.Overlay.GetAlpha and MDBX.Overlay.GetAlpha() or 1.0) * 100); end,
    function (V) if MDBX.Overlay then MDBX.Overlay.SetAlpha(V / 100); end end);
  Y = Y - 26;
  MakeStepper(Pane, "Pill columns", 0, Y, 1, 7, 1,
    function () return (MDBX.Overlay and MDBX.Overlay.GetPillCols and MDBX.Overlay.GetPillCols()) or 4; end,
    function (V) if MDBX.Overlay then MDBX.Overlay.SetPillCols(V); end end);
  Y = Y - 34;
  Label(Pane, "Active profile store", 0, Y);
  Y = Y - 20;
  local Modes = (MDBX.Profiles and MDBX.Profiles.MODES) or { "Global", "Spec", "Talent" };
  local X = 0;
  for i = 1, #Modes do
    local Mode = Modes[i];
    MakeButton(Pane, Mode, 80, X, Y, function ()
      if MDBX.Profiles then MDBX.Profiles.Apply(Mode); end;
      MDBX.Print("profile " .. Mode .. " applied");
      if Panes and Panes[TAB_INDEX.Settings] then S.RefreshPanes(); end
    end);
    X = X + 86;
  end;
  Y = Y - 30;
  Label(Pane, "Export / import (versioned)", 0, Y);
  Y = Y - 20;
  local Box = CreateFrame("EditBox", nil, Pane, "InputBoxTemplate");
  Box:SetSize(330, 22);
  Box:SetPoint("TOPLEFT", 0, Y);
  Box:SetAutoFocus(false);
  Box:SetText("");
  S.ExportBox = Box;
  Y = Y - 28;
  MakeButton(Pane, "Export", 90, 0, Y, function ()
    if MDBX.Profiles then Box:SetText(MDBX.Profiles.Export()); end
  end);
  MakeButton(Pane, "Import", 90, 96, Y, function ()
    local Ok, Msg = MDBX.Profiles.Import(Box:GetText());
    MDBX.Print((Ok and "" or "import failed: ") .. tostring(Msg));
  end);
end

local function BuildConsole (Pane)
  local Y = -TopPills(Pane);
  Label(Pane, "Console (ring, last 200 lines)", 0, Y);
  ConsoleScroll = CreateFrame("ScrollFrame", nil, Pane, "UIPanelScrollFrameTemplate");
  ConsoleScroll:SetPoint("TOPLEFT", 0, Y - 20);
  ConsoleScroll:SetSize(430, 300);
  local Child = CreateFrame("Frame", nil, ConsoleScroll);
  Child:SetWidth(410);
  Child:SetHeight(1);
  ConsoleScroll:SetScrollChild(Child);
  ConsoleText = Child:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall");
  ConsoleText:SetPoint("TOPLEFT", 0, 0);
  ConsoleText:SetWidth(410);
  ConsoleText:SetJustifyH("LEFT");
  ConsoleText:SetJustifyV("TOP");
  S.ConsoleChild = Child;
  MakeButton(Pane, "Clear", 80, 0, Y - 328, function ()
    MDBX.Console.Clear();
    S.RefreshConsole();
  end);
  S.RefreshConsole();
end

local function BuildDebug (Pane)
  local Y = -TopPills(Pane);
  local Info = Label(Pane, "", 0, Y);
  Info:SetJustifyV("TOP");
  S.DebugText = Info;
  MakeCheck(Pane, "Verbose debug logging", 0, Y - 120,
    function () return DB().debug == true; end,
    function (V) DB().debug = V; end);
  S.RefreshDebug();
end

-- ---- build / register ----------------------------------------------------
function S.Ensure ()
  DB();
  if Built then return Frame; end;

  Frame = CreateFrame("Frame", "MaxDpsBridgeExpSettings", UIParent);
  Frame:SetSize(480, 440);
  Frame:Hide();

  local Title = Frame:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge");
  Title:SetPoint("TOPLEFT", 8, -8);
  Title:SetText(CATEGORY_NAME);

  -- Tab strip.
  TabButtons = {};
  local X = 0;
  for i = 1, #TABS do
    local Index = i;
    local Btn = CreateFrame("Button", nil, Frame, "UIPanelButtonTemplate");
    Btn:SetSize(78, 22);
    Btn:SetPoint("TOPLEFT", X, -30);
    Btn:SetText(TABS[i]);
    Btn:SetScript("OnClick", function () S.ShowTab(Index); end);
    TabButtons[i] = Btn;
    X = X + 80;
  end;

  Content = CreateFrame("Frame", nil, Frame);
  Content:SetPoint("TOPLEFT", 8, -58);
  Content:SetPoint("BOTTOMRIGHT", -8, 8);
  Panes = {};

  NewPane(1); BuildPause(Panes[1]);
  NewPane(2); BuildRotation(Panes[2]);
  NewPane(3); BuildBinds(Panes[3]);
  NewPane(4); BuildSettings(Panes[4]);
  NewPane(5); BuildConsole(Panes[5]);
  NewPane(6); BuildDebug(Panes[6]);

  Built = true;

  -- Register the canvas category (10.x+) with the legacy fallback.
  local Registered = false;
  if _G.Settings and type(_G.Settings.RegisterCanvasLayoutCategory) == "function"
    and type(_G.Settings.RegisterAddOnCategory) == "function" then
    local Ok, Cat = pcall(_G.Settings.RegisterCanvasLayoutCategory, Frame, CATEGORY_NAME);
    if Ok and Cat then
      pcall(_G.Settings.RegisterAddOnCategory, Cat);
      S.Category = Cat;
      Registered = true;
    end;
  end;
  if not Registered then
    Frame.name = CATEGORY_NAME;
    if type(_G.InterfaceOptions_AddCategory) == "function" then
      pcall(_G.InterfaceOptions_AddCategory, Frame);
      S.LegacyRegistered = true;
    end;
  end;

  S.ShowTab(DB().ui.lastTab or 1);
  return Frame;
end

function S.ShowTab (Index)
  S.Ensure();
  if type(Index) ~= "number" then Index = 1; end;
  Index = math.floor(Index);
  if Index < 1 then Index = 1; end;
  if Index > #TABS then Index = #TABS; end;
  for i = 1, #Panes do
    if Panes[i] then
      if i == Index then Panes[i]:Show() else Panes[i]:Hide(); end;
    end;
  end;
  DB().ui.lastTab = Index;
  return Index;
end

function S.RefreshPanes ()
  for i = 1, #Panes do
    local Pane = Panes[i];
    if Pane and Pane.Refresh then pcall(Pane.Refresh); end;
  end;
  if S.Checks then
    for i = 1, #S.Checks do pcall(S.Checks[i].Refresh); end;
  end;
  if S.Steppers then
    for i = 1, #S.Steppers do if S.Steppers[i].Refresh then pcall(S.Steppers[i].Refresh); end; end;
  end;
  S.RefreshDebug();
end

function S.RefreshConsole ()
  if not ConsoleText then return; end;
  ConsoleText:SetText(MDBX.Console.Join());
  if S.ConsoleChild then
    local H = ConsoleText:GetStringHeight() or 0;
    if H < 1 then H = 1; end;
    S.ConsoleChild:SetHeight(H + 8);
  end;
end

function S.RefreshDebug ()
  if not S.DebugText then return; end;
  local Lines = {};
  local Epoch, Mask, Blocked = 0, 0, 0;
  if MDBX.Toggles then
    if MDBX.Toggles.AppEpoch then pcall(function () Epoch = MDBX.Toggles.AppEpoch() or 0; end); end;
    if MDBX.Toggles.EffectiveMask then pcall(function () Mask = MDBX.Toggles.EffectiveMask() or 0; end); end;
    if MDBX.Toggles.AppBlocked then pcall(function () Blocked = MDBX.Toggles.AppBlocked() or 0; end); end;
  end;
  Lines[#Lines + 1] = ("effective mask: 0x%04X"):format(Mask);
  Lines[#Lines + 1] = ("app epoch: %s"):format(tostring(Epoch));
  Lines[#Lines + 1] = ("blocked nibble: %s"):format(tostring(Blocked));
  Lines[#Lines + 1] = ("addon version: %s"):format(tostring(MDBX.VERSION));
  Lines[#Lines + 1] = ("companion seen: %s"):format((type(Epoch) == "number" and Epoch ~= 0) and "yes" or "no");
  Lines[#Lines + 1] = ("console lines: %d"):format(#MDBX.Console.Lines);
  S.DebugText:SetText(table.concat(Lines, "\n"));
end

function S.NotifyConsole ()
  if ConsoleText then S.RefreshConsole(); end;
end

function S.Open (Tab)
  S.Ensure();
  if type(Tab) == "string" then Tab = TAB_INDEX[Tab]; end;
  if type(Tab) == "number" then S.ShowTab(Tab); end;
  if S.Category and _G.Settings and type(_G.Settings.OpenToCategory) == "function" then
    local Id = S.Category.ID or S.Category
    pcall(_G.Settings.OpenToCategory, Id);
  elseif Frame and type(_G.InterfaceOptionsFrame_OpenToCategory) == "function" then
    pcall(_G.InterfaceOptionsFrame_OpenToCategory, Frame);
  elseif Frame then
    Frame:Show();
  end;
  S.RefreshPanes();
  S.RefreshConsole();
  S.RefreshDebug();
  return Frame;
end
