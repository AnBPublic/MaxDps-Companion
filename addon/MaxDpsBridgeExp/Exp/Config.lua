--- ============================ HEADER ============================
-- MaxDpsBridgeExp - standalone config window (slices 2+3).
--
-- `/mdbx cfg` opens THIS frame. The real Settings canvas category registered
-- by Exp/Settings.lua stays available (`/mdbx settings` / the AddOns list);
-- this window is an additional, richer shell and does not replace it.
--
-- Shell: 860x560, draggable, Esc-closable (UISpecialFrames), insecure only.
-- Left nav (Toggles/Overlay/Profiles/Binds/Console/Debug) + a search box that
-- filters the toggle rows + a dashboard strip (Quick Reference slash list,
-- Status bridge/version/profile count). Every frame is ordinary and insecure;
-- no secure templates, no protected attributes, no gameplay automation.
--
-- Chrome deliberately references Blizzard's own tooltip nine-slice
-- (Interface\Tooltips\UI-Tooltip-Background tile 16 / UI-Tooltip-Border edge
-- 12, insets 3) and tints it gold itself. No third-party art is copied.
-- Fonts are GameFont objects only + OUTLINE/shadow:
--   Title  GameFontNormalLarge   Hero  GameFontNormalHuge2
--   Header GameFontNormal        Text  GameFontHighlight
--   Muted  GameFontDisableSmall
--
-- Combat-deferred rebuild: any structural (re)build runs now, or is queued on
-- PLAYER_REGEN_ENABLED when InCombatLockdown() is true - mirroring Overlay.
--
-- Slice 3 rows: icon (same Blizzard map as the overlay pill) + short label +
-- state pill (gold ON / dim OFF). Clicking flips the toggle through the SAME
-- shared MDBX.Toggles set path the overlay and SlotAllowed gate read, so
-- overlay-wins semantics are preserved. Overlay flips and window flips stay in
-- sync through the Toggles.Version dirty counter (event + 0.2 s ticker while
-- shown + refresh on show).

local addonName, MDBX = ...;

local C = {};
MDBX.Config = C;

local FRAME_NAME = "MaxDpsBridgeExpConfig";
local WIDTH, HEIGHT = 860, 560;
local GOLD = { 1.00, 0.82, 0.36 };   -- #FFD15C
local ACCENT = { 1.00, 0.78, 0.28 };  -- #FFC747
local TEXT = { 0.88, 0.88, 0.86 };
local MUTED = { 0.55, 0.56, 0.60 };
local OFF_DIM = { 0.28, 0.29, 0.32 };
-- Slice 4 readability (display only): pill text, row hover, shortcuts.
local ON_TEXT = { 0.10, 0.09, 0.05 };
local OFF_TEXT = { 0.74, 0.74, 0.77 };
local ROW_BG = { 0.10, 0.11, 0.14 };
local ROW_EDGE = { 0.22, 0.24, 0.28 };
local HOVER_BG = { 0.17, 0.19, 0.23 };
local EFFECT = {
  Main = "Primary rotational filler / spender.",
  Offensive = "Offensive cooldowns and burst abilities.",
  Defensive = "Defensive cooldowns and damage reduction.",
  Consumable = "Health potions and combat consumables.",
  Trinket = "On-use trinket effects.",
  Interrupt = "Interrupts an enemy cast.",
  Mobility = "Movement and gap-closer abilities.",
  SelfHeal = "Self-healing survival abilities.",
  Solo = "Hold survival slots unless grouped (emergency HP always allowed).",
  OOC = "Allow automation while out of combat.",
  AutoTarget = "Auto-select a target when none is set.",
  AutoInteract = "Auto-interact with objects and NPCs.",
  TTK = "Time-to-kill guard: skip long-dead targets.",
  CC = "Crowd-control appendix abilities.",
  Overlay = "Show or hide the in-game overlay.",
};

local function BindFor (Key)
  local Name = "MDBX_TOGGLE_" .. tostring(Key):upper();
  if type(_G.GetBindingKey) == "function" then
    local Ok, A = pcall(_G.GetBindingKey, Name);
    if Ok and type(A) == "string" and A ~= "" then return A; end;
  end;
  return nil;
end

local function EffectFor (Key)
  local E = EFFECT[Key];
  if type(E) == "string" and E ~= "" then return E; end;
  return "Toggles this companion behaviour.";
end

local NAV = { "Toggles", "Overlay", "Profiles", "Binds", "Console", "Debug" };
local NAV_INDEX = {};
for i = 1, #NAV do NAV_INDEX[NAV[i]] = i; end;

local Frame, Content, SearchBox, StatusBody, QuickBody, DebugBody;
local SearchHint, SearchClear;
local ConsoleText, ConsoleChild, ExportBox;
local NavButtons = {};
local Panes = {};
local ToggleRows = {};      -- slice-3 rows: { frame, key }
local Steppers = {};
local Built = false;
local Dirty = true;
local Selected = 1;
local Filter = "";
local Ticker, CombatQueue, CombatWatcher;

local function DB ()
  if type(_G.MaxDpsBridgeExpDB) ~= "table" then _G.MaxDpsBridgeExpDB = {}; end;
  local D = _G.MaxDpsBridgeExpDB;
  if type(D.ui) ~= "table" then D.ui = {}; end;
  if type(D.ui.config) ~= "table" then D.ui.config = {}; end;
  return D.ui.config;
end

-- ---- styling helpers -----------------------------------------------------
local function Style (Fs, Obj, Outline, R, G, B)
  -- Slice 4: every custom font string gets OUTLINE + a 1,-1 shadow so text
  -- stays legible over the tooltip-art chrome. The Outline arg is kept for
  -- call-site compatibility but is intentionally ignored (outline is always on).
  if not Fs then return; end;
  if Fs.SetFontObject then pcall(Fs.SetFontObject, Fs, Obj); end;
  if Fs.GetFont and Fs.SetFont then
    local Ok, Font, Size = pcall(Fs.GetFont, Fs);
    if Ok and Font and Size then pcall(Fs.SetFont, Fs, Font, Size, "OUTLINE"); end;
  end;
  if Fs.SetShadowColor then Fs:SetShadowColor(0, 0, 0, 1); end;
  if Fs.SetShadowOffset then Fs:SetShadowOffset(1, -1); end;
  if R then Fs:SetTextColor(R, G, B, 1); end;
end

local function TooltipBackdrop (F, BodyAlpha, BorderR, BorderG, BorderB)
  if not F then return; end;
  if F.SetBackdrop then
    F:SetBackdrop({
      bgFile = "Interface\\Tooltips\\UI-Tooltip-Background",
      edgeFile = "Interface\\Tooltips\\UI-Tooltip-Border",
      tile = true, tileSize = 16, edgeSize = 12,
      insets = { left = 3, right = 3, top = 3, bottom = 3 },
    });
    F:SetBackdropColor(0.06, 0.07, 0.09, BodyAlpha or 0.97);
    F:SetBackdropBorderColor(BorderR or GOLD[1], BorderG or GOLD[2], BorderB or GOLD[3], 1);
  end
end

local function CardBackdrop (F)
  if not F then return; end;
  if F.SetBackdrop then
    F:SetBackdrop({
      bgFile = "Interface\\Tooltips\\UI-Tooltip-Background",
      edgeFile = "Interface\\Tooltips\\UI-Tooltip-Border",
      tile = true, tileSize = 16, edgeSize = 12,
      insets = { left = 3, right = 3, top = 3, bottom = 3 },
    });
    F:SetBackdropColor(0.10, 0.11, 0.14, 0.95);
    F:SetBackdropBorderColor(0.42, 0.34, 0.16, 0.9);
  end
end

local function Label (Parent, Text, X, Y, FontObject, R, G, B)
  local Fs = Parent:CreateFontString(nil, "OVERLAY", FontObject or "GameFontHighlight");
  Fs:SetPoint("TOPLEFT", X, Y);
  Fs:SetText(Text);
  Fs:SetJustifyH("LEFT");
  Style(Fs, FontObject or "GameFontHighlight", false, R, G, B);
  return Fs;
end

-- ---- combat-deferred rebuild ---------------------------------------------
local function InCombat ()
  if type(InCombatLockdown) == "function" then
    local Ok, V = pcall(InCombatLockdown);
    if Ok then return V == true; end;
  end;
  return false;
end

local function FlushQueue ()
  local Q = CombatQueue;
  CombatQueue = nil;
  if not Q then return; end;
  for i = 1, #Q do pcall(Q[i]); end;
end

local function EnsureWatcher ()
  if CombatWatcher then return; end;
  CombatWatcher = CreateFrame("Frame");
  CombatWatcher:RegisterEvent("PLAYER_REGEN_ENABLED");
  CombatWatcher:SetScript("OnEvent", FlushQueue);
end

function C.Defer (Fn)
  if type(Fn) ~= "function" then return; end;
  if InCombat() then
    CombatQueue = CombatQueue or {};
    CombatQueue[#CombatQueue + 1] = Fn;
    EnsureWatcher();
  else
    pcall(Fn);
  end;
end

-- ---- dirty / sync ---------------------------------------------------------
function C.MarkDirty () Dirty = true; end
function C.SetDirty () Dirty = true; end

local function ToggleVersion ()
  if MDBX.Toggles and MDBX.Toggles.Version then
    local Ok, V = pcall(MDBX.Toggles.Version);
    if Ok and type(V) == "number" then return V; end;
  end;
  return nil;
end

local function GetOn (Key)
  if MDBX.Toggles and MDBX.Toggles.Get then return MDBX.Toggles.Get(Key) ~= false; end;
  return true;
end

local function SetOn (Key, On)
  if MDBX.Toggles and MDBX.Toggles.Set then return MDBX.Toggles.Set(Key, On); end;
  return On;
end

-- ---- search / filter ------------------------------------------------------
local function RegisterRow (Pane, Row, Text)
  if type(Pane._rows) ~= "table" then Pane._rows = {}; end;
  Pane._rows[#Pane._rows + 1] = { frame = Row, text = string.lower(tostring(Text or "")) };
end

local function ApplyFilter ()
  local Pane = Panes[Selected];
  if not Pane or type(Pane._rows) ~= "table" then return; end;
  local Needle = Filter:lower();
  for i = 1, #Pane._rows do
    local Entry = Pane._rows[i];
    if Needle == "" or Entry.text:find(Needle, 1, true) then
      Entry.frame:Show();
    else
      Entry.frame:Hide();
    end;
  end;
end

function C.SetFilter (Text)
  Filter = tostring(Text or "");
  ApplyFilter();
end

-- ---- row paint ------------------------------------------------------------
local function PaintRow (Row)
  if not Row then return; end;
  local On = GetOn(Row.Key);
  Row.On = On;
  if Row.Icon then
    if On then Row.Icon:SetVertexColor(1, 1, 1, 1);
    else Row.Icon:SetVertexColor(0.42, 0.42, 0.45, 1); end;
  end;
  if Row.Label then
    if On then Row.Label:SetTextColor(TEXT[1], TEXT[2], TEXT[3], 1);
    else Row.Label:SetTextColor(MUTED[1], MUTED[2], MUTED[3], 1); end;
  end;
  if Row.PillBg then
    -- Slice 4: ON uses the #FFC747 accent with dark text; OFF stays dim with
    -- a brighter label so both states keep readable contrast.
    if On then Row.PillBg:SetColorTexture(ACCENT[1], ACCENT[2], ACCENT[3], 0.95);
    else Row.PillBg:SetColorTexture(OFF_DIM[1], OFF_DIM[2], OFF_DIM[3], 0.95); end;
  end;
  if Row.PillText then
    if On then Row.PillText:SetTextColor(ON_TEXT[1], ON_TEXT[2], ON_TEXT[3], 1);
    else Row.PillText:SetTextColor(OFF_TEXT[1], OFF_TEXT[2], OFF_TEXT[3], 1); end;
    Row.PillText:SetText(On and "ON" or "OFF");
  end;
end

local function MakeToggleRow (Parent, Key)
  local Mixin = (type(BackdropTemplateMixin) ~= "nil") and "BackdropTemplate" or nil;
  local Row = CreateFrame("Button", nil, Parent, Mixin);
  Row:SetSize(320, 30);
  Row.Key = Key;
  if Row.SetBackdrop then
    Row:SetBackdrop({
      bgFile = "Interface\\Buttons\\WHITE8x8",
      edgeFile = "Interface\\Buttons\\WHITE8x8",
      edgeSize = 1,
    });
    Row:SetBackdropColor(ROW_BG[1], ROW_BG[2], ROW_BG[3], 0.85);
    Row:SetBackdropBorderColor(ROW_EDGE[1], ROW_EDGE[2], ROW_EDGE[3], 0.9);
  end;

  Row.Icon = Row:CreateTexture(nil, "ARTWORK");
  Row.Icon:SetSize(16, 16);
  Row.Icon:SetPoint("LEFT", 6, 0);
  if MDBX.Overlay and MDBX.Overlay.IconFor then
    local Ok, Path = pcall(MDBX.Overlay.IconFor, Key);
    if Ok and Path then Row.Icon:SetTexture(Path); end;
  end;

  Row.Pill = CreateFrame("Frame", nil, Row);
  Row.Pill:SetSize(46, 18);
  Row.Pill:SetPoint("RIGHT", -6, 0);
  Row.PillBg = Row.Pill:CreateTexture(nil, "BACKGROUND");
  Row.PillBg:SetAllPoints();
  Row.PillBg:SetColorTexture(ACCENT[1], ACCENT[2], ACCENT[3], 0.95);
  Row.PillText = Row.Pill:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall");
  Row.PillText:SetPoint("CENTER", 0, 0);
  Style(Row.PillText, "GameFontNormalSmall", false, ON_TEXT[1], ON_TEXT[2], ON_TEXT[3]);
  Row.PillText:SetText("ON");

  Row.Label = Row:CreateFontString(nil, "OVERLAY", "GameFontHighlight");
  Row.Label:SetPoint("LEFT", Row.Icon, "RIGHT", 6, 0);
  Row.Label:SetPoint("RIGHT", Row.Pill, "LEFT", -6, 0);
  Row.Label:SetJustifyH("LEFT");
  Style(Row.Label, "GameFontHighlight", true, TEXT[1], TEXT[2], TEXT[3]);
  local Short = Key;
  if MDBX.Toggles and MDBX.Toggles.ShortLabel then Short = MDBX.Toggles.ShortLabel(Key); end;
  Row.Label:SetText(Short);

  Row:RegisterForClicks("LeftButtonUp");
  Row:SetScript("OnClick", function (self)
    if not self.Key then return; end;
    -- Same shared MDBX.Toggles path the overlay pills and SlotAllowed use.
    if MDBX.Toggles and MDBX.Toggles.Flip then MDBX.Toggles.Flip(self.Key); end;
    if MDBX.Overlay and MDBX.Overlay.SetDirty then pcall(MDBX.Overlay.SetDirty); end;
    C.MarkDirty();
    PaintRow(self);
  end);
  Row:SetScript("OnEnter", function (self)
    -- Slice 4: brightness shift + gold border on hover.
    if self.SetBackdropColor then self:SetBackdropColor(HOVER_BG[1], HOVER_BG[2], HOVER_BG[3], 0.95); end;
    if self.SetBackdropBorderColor then self:SetBackdropBorderColor(GOLD[1], GOLD[2], GOLD[3], 1); end;
    if GameTooltip then
      local Short = self.Key;
      if MDBX.Toggles and MDBX.Toggles.ShortLabel then Short = MDBX.Toggles.ShortLabel(self.Key); end;
      local Full = self.Key;
      if MDBX.Toggles and MDBX.Toggles.Label then Full = MDBX.Toggles.Label(self.Key); end;
      GameTooltip:SetOwner(self, "ANCHOR_RIGHT");
      -- Slice 4 tooltip: short label + full name + effect + bind state + action.
      GameTooltip:SetText(tostring(Short) .. "  -  " .. tostring(Full), GOLD[1], GOLD[2], GOLD[3]);
      GameTooltip:AddLine(EffectFor(self.Key), 0.90, 0.90, 0.88, true);
      local Bind = BindFor(self.Key);
      if Bind then
        GameTooltip:AddLine("Bind: " .. Bind, 0.55, 0.85, 1.00, false);
      else
        GameTooltip:AddLine("Bind: unbound", MUTED[1], MUTED[2], MUTED[3], false);
      end
      GameTooltip:AddLine("Click to toggle", GOLD[1], GOLD[2], GOLD[3], false);
      GameTooltip:Show();
    end;
  end);
  Row:SetScript("OnLeave", function (self)
    if self.SetBackdropColor then self:SetBackdropColor(ROW_BG[1], ROW_BG[2], ROW_BG[3], 0.85); end;
    if self.SetBackdropBorderColor then self:SetBackdropBorderColor(ROW_EDGE[1], ROW_EDGE[2], ROW_EDGE[3], 0.9); end;
    if GameTooltip then GameTooltip:Hide(); end;
  end);

  PaintRow(Row);
  return Row;
end

-- ---- dashboard ------------------------------------------------------------
local function BuildDashboard ()
  local function MakeCard (X, Title)
    local Card = CreateFrame("Frame", nil, Frame, (type(BackdropTemplateMixin) ~= "nil") and "BackdropTemplate" or nil);
    Card:SetSize(322, 84);
    Card:SetPoint("TOPLEFT", X, -44);
    CardBackdrop(Card);
    local Head = Card:CreateFontString(nil, "OVERLAY", "GameFontNormal");
    Head:SetPoint("TOPLEFT", 8, -6);
    Style(Head, "GameFontNormal", true, GOLD[1], GOLD[2], GOLD[3]);
    Head:SetText(Title);
    local Body = Card:CreateFontString(nil, "OVERLAY", "GameFontHighlight");
    Body:SetPoint("TOPLEFT", 8, -24);
    Body:SetPoint("BOTTOMRIGHT", -8, 6);
    Body:SetJustifyH("LEFT");
    Body:SetJustifyV("TOP");
    Style(Body, "GameFontHighlight", false, TEXT[1], TEXT[2], TEXT[3]);
    return Card, Body;
  end
  local _, Quick = MakeCard(176, "Quick Reference");
  QuickBody = Quick;
  local _, Status = MakeCard(506, "Status");
  StatusBody = Status;
end

function C.RefreshDashboard ()
  if QuickBody then
    QuickBody:SetText(table.concat({
      "/mdbx cfg            this window",
      "/mdbx settings       AddOns options page",
      "/mdbx show|hide|toggle",
      "/mdbx t <key> [on|off]",
      "/mdbx profile <Global|Spec|Talent>",
      "/mdbx export | import <string>",
      "/mdbx console | debug | help",
    }, "\n"));
  end;
  if StatusBody then
    local Epoch = 0;
    if MDBX.Toggles and MDBX.Toggles.AppEpoch then
      pcall(function () Epoch = MDBX.Toggles.AppEpoch() or 0; end);
    end;
    local Mask = 0;
    if MDBX.Toggles and MDBX.Toggles.EffectiveMask then
      pcall(function () Mask = MDBX.Toggles.EffectiveMask() or 0; end);
    end;
    local Mode = "(none)";
    local Count = 0;
    if MDBX.Profiles then
      if MDBX.Profiles.ActiveMode then pcall(function () Mode = MDBX.Profiles.ActiveMode(); end); end;
      if MDBX.Profiles.MODES then Count = #MDBX.Profiles.MODES; end;
    end;
    if type(Mode) ~= "string" or Mode == "" then Mode = "(none)"; end;
    local Live = (type(Epoch) == "number" and Epoch ~= 0) and "live" or "waiting";
    StatusBody:SetText(("bridge:   %s\nversion:  %s\nprofile:  %s (%d stores)\nmask:     0x%04X")
      :format(Live, tostring(MDBX.VERSION), tostring(Mode), Count, Mask));
  end;
end

-- ---- panes ----------------------------------------------------------------
local function NewPane (Index)
  local Pane = CreateFrame("Frame", nil, Content);
  Pane:SetAllPoints(Content);
  Pane:Hide();
  Panes[Index] = Pane;
  return Pane;
end

local function BuildToggles (Pane)
  local Keys = {};
  if MDBX.Profiles and MDBX.Profiles.ToggleKeys then
    local Ok, K = pcall(MDBX.Profiles.ToggleKeys);
    if Ok and type(K) == "table" then Keys = K; end;
  end;
  if #Keys == 0 and MDBX.Overlay and MDBX.Overlay.PILL_KEYS then Keys = MDBX.Overlay.PILL_KEYS; end;
  local ColW, RowH, Gap = 330, 30, 8;
  for i = 1, #Keys do
    local Key = Keys[i];
    local Col = (i - 1) % 2;
    local RowIndex = math.floor((i - 1) / 2);
    local Row = MakeToggleRow(Pane, Key);
    Row:SetPoint("TOPLEFT", Col * ColW, -RowIndex * (RowH + Gap));
    ToggleRows[#ToggleRows + 1] = Row;
    local Text = Key .. " " .. tostring(MDBX.Toggles and MDBX.Toggles.Label(Key) or Key)
      .. " " .. tostring(MDBX.Toggles and MDBX.Toggles.ShortLabel(Key) or Key);
    RegisterRow(Pane, Row, Text);
  end;
  local Note = Label(Pane, "Click a row to flip the in-game toggle. "
    .. "Overlay pills and these rows share one state (overlay-wins).",
    0, -(math.ceil(#Keys / 2)) * (RowH + Gap) - 4, "GameFontDisableSmall",
    MUTED[1], MUTED[2], MUTED[3]);
  RegisterRow(Pane, Note, "note footer");
end

local function MakeStepper (Parent, Text, X, Y, Min, Max, Step, Getter, Setter)
  local Row = CreateFrame("Frame", nil, Parent);
  Row:SetSize(640, 26);
  Row:SetPoint("TOPLEFT", X, Y);
  Label(Row, Text, 0, -4, "GameFontHighlight", TEXT[1], TEXT[2], TEXT[3]);
  local Value = Label(Row, "?", 220, -4, "GameFontHighlight", GOLD[1], GOLD[2], GOLD[3]);
  local Minus = CreateFrame("Button", nil, Row, "UIPanelButtonTemplate");
  Minus:SetSize(24, 20);
  Minus:SetPoint("TOPLEFT", 260, 0);
  Minus:SetText("-");
  local Plus = CreateFrame("Button", nil, Row, "UIPanelButtonTemplate");
  Plus:SetSize(24, 20);
  Plus:SetPoint("TOPLEFT", 288, 0);
  Plus:SetText("+");
  local function Sync ()
    local V = Getter and Getter() or Min;
    Value:SetText(tostring(V));
    Minus:SetEnabled(V > Min);
    Plus:SetEnabled(V < Max);
  end
  local function Nudge (Delta)
    local V = (Getter and Getter() or Min) + Delta;
    if V < Min then V = Min; end;
    if V > Max then V = Max; end;
    if Setter then Setter(V); end;
    Sync();
  end
  Minus:SetScript("OnClick", function () Nudge(-Step); end);
  Plus:SetScript("OnClick", function () Nudge(Step); end);
  Row.Refresh = Sync;
  Steppers[#Steppers + 1] = Row;
  Sync();
  return Row;
end

local function BuildOverlay (Pane)
  local Y = 0;
  MakeStepper(Pane, "Scale (x100)", 0, Y, 50, 200, 10,
    function () return math.floor((MDBX.Overlay and MDBX.Overlay.GetScale and MDBX.Overlay.GetScale() or 1.0) * 100); end,
    function (V) if MDBX.Overlay then MDBX.Overlay.SetScale(V / 100); end end);
  Y = Y - 30;
  MakeStepper(Pane, "Alpha (x100)", 0, Y, 20, 100, 10,
    function () return math.floor((MDBX.Overlay and MDBX.Overlay.GetAlpha and MDBX.Overlay.GetAlpha() or 1.0) * 100); end,
    function (V) if MDBX.Overlay then MDBX.Overlay.SetAlpha(V / 100); end end);
  Y = Y - 30;
  MakeStepper(Pane, "Pill columns", 0, Y, 1, 7, 1,
    function () return (MDBX.Overlay and MDBX.Overlay.GetPillCols and MDBX.Overlay.GetPillCols()) or 4; end,
    function (V) if MDBX.Overlay then MDBX.Overlay.SetPillCols(V); end end);
  Y = Y - 40;
  local Btn = CreateFrame("Button", nil, Pane, "UIPanelButtonTemplate");
  Btn:SetSize(120, 22);
  Btn:SetPoint("TOPLEFT", 0, Y);
  Btn:SetText("Reset overlay");
  Btn:SetScript("OnClick", function ()
    if MDBX.Overlay and MDBX.Overlay.Reset then MDBX.Overlay.Reset(); end;
    C.RefreshPanes();
  end);
end

local function BuildProfiles (Pane)
  local Y = 0;
  local Modes = (MDBX.Profiles and MDBX.Profiles.MODES) or {};
  if #Modes == 0 then
    Label(Pane, "(no profiles available)", 0, 0, "GameFontDisableSmall",
      MUTED[1], MUTED[2], MUTED[3]);
  end;
  local X = 0;
  for i = 1, #Modes do
    local Mode = Modes[i];
    local Btn = CreateFrame("Button", nil, Pane, "UIPanelButtonTemplate");
    Btn:SetSize(90, 22);
    Btn:SetPoint("TOPLEFT", X, Y);
    Btn:SetText("Apply " .. Mode);
    Btn:SetScript("OnClick", function ()
      if MDBX.Profiles then MDBX.Profiles.Apply(Mode); end;
      MDBX.Print("profile " .. Mode .. " applied");
      C.MarkDirty();
      C.RefreshPanes();
    end);
    X = X + 96;
  end;
  Y = Y - 34;
  Label(Pane, "Export / import (versioned)", 0, Y, "GameFontNormal", GOLD[1], GOLD[2], GOLD[3]);
  Y = Y - 20;
  ExportBox = CreateFrame("EditBox", nil, Pane, "InputBoxTemplate");
  ExportBox:SetSize(430, 22);
  ExportBox:SetPoint("TOPLEFT", 0, Y);
  ExportBox:SetAutoFocus(false);
  ExportBox:SetText("");
  Y = Y - 30;
  local Export = CreateFrame("Button", nil, Pane, "UIPanelButtonTemplate");
  Export:SetSize(90, 22); Export:SetPoint("TOPLEFT", 0, Y); Export:SetText("Export");
  Export:SetScript("OnClick", function ()
    if MDBX.Profiles and MDBX.Profiles.Export then ExportBox:SetText(MDBX.Profiles.Export()); end
  end);
  local Import = CreateFrame("Button", nil, Pane, "UIPanelButtonTemplate");
  Import:SetSize(90, 22); Import:SetPoint("TOPLEFT", 96, Y); Import:SetText("Import");
  Import:SetScript("OnClick", function ()
    local Ok, Msg = MDBX.Profiles.Import(ExportBox:GetText());
    MDBX.Print((Ok and "imported: " or "import failed: ") .. tostring(Msg));
    C.MarkDirty();
  end);
end

local function BuildBinds (Pane)
  local Y = 0;
  Label(Pane, "Bound actions (bind in the Keybindings panel)", 0, Y, "GameFontNormal", GOLD[1], GOLD[2], GOLD[3]);
  Y = Y - 22;
  local Names = { "Overlay" };
  if MDBX.Profiles and MDBX.Profiles.ToggleKeys then
    local K = MDBX.Profiles.ToggleKeys();
    for i = 1, #K do Names[#Names + 1] = K[i]; end;
  end;
  if #Names == 0 then
    Label(Pane, "  (no bindings registered)", 0, Y, "GameFontDisableSmall",
      MUTED[1], MUTED[2], MUTED[3]);
    Y = Y - 17;
  end;
  for i = 1, #Names do
    local BindName = "MDBX_TOGGLE_" .. string.upper(Names[i]);
    local Bind = BindFor(Names[i]);
    local Suffix = Bind and ("   [" .. Bind .. "]") or "   [unbound]";
    Label(Pane, "  " .. BindName .. "   -   " .. Names[i] .. Suffix, 0, Y,
      "GameFontHighlight", TEXT[1], TEXT[2], TEXT[3]);
    Y = Y - 17;
  end;
  local Btn = CreateFrame("Button", nil, Pane, "UIPanelButtonTemplate");
  Btn:SetSize(160, 22);
  Btn:SetPoint("TOPLEFT", 0, Y - 6);
  Btn:SetText("Open Keybindings");
  Btn:SetScript("OnClick", function ()
    if type(_G.ToggleKeyBindings) == "function" then
      _G.ToggleKeyBindings();
    elseif type(_G.InterfaceOptionsFrame_OpenToCategory) == "function" then
      pcall(_G.InterfaceOptionsFrame_OpenToCategory, "Keybindings");
    else
      MDBX.Print("open the Keybindings panel from the game menu");
    end
  end);
end

local function BuildConsole (Pane)
  Label(Pane, "Console (ring, last 200 lines)", 0, 0, "GameFontNormal", GOLD[1], GOLD[2], GOLD[3]);
  local Scroll = CreateFrame("ScrollFrame", nil, Pane, "UIPanelScrollFrameTemplate");
  Scroll:SetPoint("TOPLEFT", 0, -20);
  Scroll:SetSize(620, 300);
  ConsoleChild = CreateFrame("Frame", nil, Scroll);
  ConsoleChild:SetWidth(600);
  ConsoleChild:SetHeight(1);
  Scroll:SetScrollChild(ConsoleChild);
  ConsoleText = ConsoleChild:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall");
  ConsoleText:SetPoint("TOPLEFT", 0, 0);
  ConsoleText:SetWidth(600);
  ConsoleText:SetJustifyH("LEFT");
  ConsoleText:SetJustifyV("TOP");
  local Clear = CreateFrame("Button", nil, Pane, "UIPanelButtonTemplate");
  Clear:SetSize(80, 22);
  Clear:SetPoint("TOPLEFT", 0, -328);
  Clear:SetText("Clear");
  Clear:SetScript("OnClick", function ()
    MDBX.Console.Clear();
    C.RefreshConsole();
  end);
  C.RefreshConsole();
end

local function BuildDebug (Pane)
  DebugBody = Label(Pane, "", 0, 0, "GameFontHighlight", TEXT[1], TEXT[2], TEXT[3]);
  DebugBody:SetJustifyV("TOP");
  C.RefreshDebug();
end

-- ---- build ----------------------------------------------------------------
local function BuildNav ()
  for i = 1, #NAV do
    local Index = i;
    local Btn = CreateFrame("Button", nil, Frame, "UIPanelButtonTemplate");
    Btn:SetSize(150, 30);
    Btn:SetPoint("TOPLEFT", 14, -44 - (i - 1) * 34);
    Btn:SetText(NAV[i]);
    Btn:SetScript("OnClick", function () C.Select(Index); end);
    NavButtons[i] = Btn;
  end;
end

local function UpdateSearchChrome ()
  if not SearchBox then return; end;
  local Text = SearchBox:GetText() or "";
  local Focused = false;
  if SearchBox.HasFocus then
    local Ok, V = pcall(SearchBox.HasFocus, SearchBox);
    if Ok then Focused = (V == true); end;
  end;
  if SearchHint then
    if Text == "" and not Focused then SearchHint:Show(); else SearchHint:Hide(); end;
  end;
  if SearchClear then
    if Text ~= "" then SearchClear:Show(); else SearchClear:Hide(); end;
  end;
end

local function BuildSearch ()
  SearchBox = CreateFrame("EditBox", nil, Frame, "InputBoxTemplate");
  SearchBox:SetSize(630, 22);
  SearchBox:SetPoint("TOPLEFT", 176, -134);
  SearchBox:SetAutoFocus(false);
  -- Slice 4: placeholder text and a clear button (display only).
  SearchHint = Frame:CreateFontString(nil, "OVERLAY", "GameFontDisableSmall");
  SearchHint:SetPoint("LEFT", SearchBox, "LEFT", 8, 0);
  SearchHint:SetText("Search toggles...");
  Style(SearchHint, "GameFontDisableSmall", true, MUTED[1], MUTED[2], MUTED[3]);

  SearchClear = CreateFrame("Button", nil, Frame, "UIPanelButtonTemplate");
  SearchClear:SetSize(22, 22);
  SearchClear:SetPoint("LEFT", SearchBox, "RIGHT", 4, 0);
  SearchClear:SetText("X");
  SearchClear:SetScript("OnClick", function ()
    SearchBox:SetText("");
    C.SetFilter("");
    if SearchBox.ClearFocus then SearchBox:ClearFocus(); end;
    UpdateSearchChrome();
  end);
  SearchClear:Hide();

  SearchBox:SetScript("OnTextChanged", function (self)
    C.SetFilter(self:GetText());
    UpdateSearchChrome();
  end);
  SearchBox:SetScript("OnEditFocusGained", function ()
    if SearchHint then SearchHint:Hide(); end;
  end);
  SearchBox:SetScript("OnEditFocusLost", function ()
    UpdateSearchChrome();
  end);
  SearchBox:SetScript("OnEscapePressed", function (self)
    self:SetText("");
    C.SetFilter("");
    self:ClearFocus();
    UpdateSearchChrome();
  end);
  UpdateSearchChrome();
end

function C.Ensure ()
  if Built then return Frame; end;
  DB();
  local Mixin = (type(BackdropTemplateMixin) ~= "nil") and "BackdropTemplate" or nil;
  local Ok, NewFrame = pcall(CreateFrame, "Frame", FRAME_NAME, UIParent, Mixin);
  if not Ok or not NewFrame then return nil; end;
  Frame = NewFrame;
  Frame:SetSize(WIDTH, HEIGHT);
  Frame:SetFrameStrata("DIALOG");
  Frame:SetToplevel(true);
  Frame:SetMovable(true);
  Frame:SetClampedToScreen(true);
  Frame:EnableMouse(true);
  if Frame.SetHitRectInsets then Frame:SetHitRectInsets(0, 0, 0, 0); end;
  TooltipBackdrop(Frame, 0.97);

  -- Esc-closable: named frame in UISpecialFrames (insecure, standard).
  if type(_G.UISpecialFrames) == "table" then
    local Found = false;
    for i = 1, #_G.UISpecialFrames do
      if _G.UISpecialFrames[i] == FRAME_NAME then Found = true; end;
    end;
    if not Found then table.insert(_G.UISpecialFrames, FRAME_NAME); end;
  end;

  -- Title bar (drag handle).
  local TitleBar = CreateFrame("Frame", nil, Frame);
  TitleBar:SetPoint("TOPLEFT", 6, -6);
  TitleBar:SetPoint("TOPRIGHT", -34, -6);
  TitleBar:SetHeight(26);
  TitleBar:EnableMouse(true);
  TitleBar:RegisterForDrag("LeftButton");
  TitleBar:SetScript("OnDragStart", function () Frame:StartMoving(); end);
  TitleBar:SetScript("OnDragStop", function ()
    Frame:StopMovingOrSizing();
    local Point, _, _, X, Y = Frame:GetPoint(1);
    local D = DB();
    if type(Point) == "string" then D.point = Point; end;
    D.x = math.floor(tonumber(X) or 0);
    D.y = math.floor(tonumber(Y) or 0);
  end);
  local Title = TitleBar:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge");
  Title:SetPoint("LEFT", 2, 0);
  Style(Title, "GameFontNormalLarge", true, GOLD[1], GOLD[2], GOLD[3]);
  Title:SetText("MaxDps Bridge Exp  -  Configuration");

  local Close = CreateFrame("Button", nil, Frame, "UIPanelCloseButton");
  Close:SetPoint("TOPRIGHT", -8, -8);

  -- Content region + dashboard + search.
  Content = CreateFrame("Frame", nil, Frame);
  Content:SetPoint("TOPLEFT", 176, -162);
  Content:SetPoint("BOTTOMRIGHT", -14, 14);

  BuildDashboard();
  BuildSearch();
  BuildNav();

  NewPane(NAV_INDEX.Toggles);  BuildToggles(Panes[NAV_INDEX.Toggles]);
  NewPane(NAV_INDEX.Overlay);  BuildOverlay(Panes[NAV_INDEX.Overlay]);
  NewPane(NAV_INDEX.Profiles); BuildProfiles(Panes[NAV_INDEX.Profiles]);
  NewPane(NAV_INDEX.Binds);    BuildBinds(Panes[NAV_INDEX.Binds]);
  NewPane(NAV_INDEX.Console);  BuildConsole(Panes[NAV_INDEX.Console]);
  NewPane(NAV_INDEX.Debug);    BuildDebug(Panes[NAV_INDEX.Debug]);

  Frame:RegisterEvent("PLAYER_REGEN_ENABLED");
  Frame:RegisterEvent("PLAYER_SPECIALIZATION_CHANGED");
  Frame:RegisterEvent("PLAYER_TALENT_UPDATE");
  Frame:SetScript("OnShow", function ()
    C.Refresh();
    C.EnsureTicker();
  end);
  Frame:SetScript("OnHide", function ()
    if Ticker then Ticker:Cancel(); Ticker = nil; end;
  end);
  Frame:SetScript("OnEvent", function ()
    C.MarkDirty();
  end);

  Built = true;
  C.Select(Selected or 1);
  Frame:Hide();
  return Frame;
end

function C.EnsureTicker ()
  if Ticker then return; end;
  if not (C_Timer and C_Timer.NewTicker) then return; end;
  Ticker = C_Timer.NewTicker(0.2, function ()
    if Dirty then C.Refresh(); end;
  end);
end

-- ---- selection / refresh --------------------------------------------------
function C.Select (Index)
  C.Ensure();
  if type(Index) ~= "number" then Index = 1; end;
  Index = math.floor(Index);
  if Index < 1 then Index = 1; end;
  if Index > #NAV then Index = #NAV; end;
  Selected = Index;
  DB().lastTab = Index;
  for i = 1, #Panes do
    if Panes[i] then
      if i == Index then Panes[i]:Show() else Panes[i]:Hide(); end;
    end;
  end;
  for i = 1, #NavButtons do
    local Btn = NavButtons[i];
    if Btn and Btn.SetEnabled then Btn:SetEnabled(i ~= Index); end;
  end;
  ApplyFilter();
  return Index;
end

function C.RefreshRows ()
  for i = 1, #ToggleRows do PaintRow(ToggleRows[i]); end;
end

function C.RefreshPanes ()
  for i = 1, #Steppers do if Steppers[i].Refresh then pcall(Steppers[i].Refresh); end; end;
  C.RefreshConsole();
  C.RefreshDebug();
end

function C.RefreshConsole ()
  if not ConsoleText then return; end;
  ConsoleText:SetText(MDBX.Console.Join());
  if ConsoleChild then
    local H = ConsoleText:GetStringHeight() or 0;
    if H < 1 then H = 1; end;
    ConsoleChild:SetHeight(H + 8);
  end;
end

function C.RefreshDebug ()
  if not DebugBody then return; end;
  local Epoch, Mask, Blocked = 0, 0, 0;
  if MDBX.Toggles then
    if MDBX.Toggles.AppEpoch then pcall(function () Epoch = MDBX.Toggles.AppEpoch() or 0; end); end;
    if MDBX.Toggles.EffectiveMask then pcall(function () Mask = MDBX.Toggles.EffectiveMask() or 0; end); end;
    if MDBX.Toggles.AppBlocked then pcall(function () Blocked = MDBX.Toggles.AppBlocked() or 0; end); end;
  end;
  local Lines = {
    ("effective mask: 0x%04X"):format(Mask),
    ("app epoch: %s"):format(tostring(Epoch)),
    ("blocked nibble: %s"):format(tostring(Blocked)),
    ("addon version: %s"):format(tostring(MDBX.VERSION)),
    ("companion seen: %s"):format((type(Epoch) == "number" and Epoch ~= 0) and "yes" or "no"),
    ("console lines: %d"):format(#MDBX.Console.Lines),
  };
  DebugBody:SetText(table.concat(Lines, "\n"));
end

function C.Refresh ()
  Dirty = false;
  C._LastVersion = ToggleVersion();
  C.RefreshRows();
  C.RefreshDashboard();
  C.RefreshPanes();
end

-- ---- visibility -----------------------------------------------------------
function C.Show ()
  C.Ensure();
  if not Frame then return; end;
  local D = DB();
  Frame:ClearAllPoints();
  Frame:SetPoint(D.point or "CENTER", UIParent, D.point or "CENTER", D.x or 0, D.y or 0);
  Frame:Show();
  C.MarkDirty();
  C.Refresh();
end

function C.Hide ()
  if Frame then Frame:Hide(); end;
end

function C.Toggle ()
  if Frame and Frame:IsShown() then C.Hide(); else C.Show(); end;
end

function C.IsShown () return Frame and Frame:IsShown() == true; end

function C.Open ()
  if MDBX.IsInert and MDBX.IsInert() then return; end;
  if InCombat() then
    C.Defer(function () C.Show(); end);
    MDBX.Print("config window queued until combat ends");
    return;
  end;
  C.Show();
end
