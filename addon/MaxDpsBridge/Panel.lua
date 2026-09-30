--- ============================ HEADER ============================
-- In-game 13-toggle settings panel + compact draggable overlay (bridge 3.3.0).
-- Prompt: docs/plans/2026-09-29-ingame-toggles.md (UI section).
--
-- Plain frames only: BackdropTemplate + CheckButton. No StdUi dependency, no
-- secure/protected calls, combat-safe -- nothing here runs on the pixel
-- update path; it only reads/writes MaxDpsBridgeDB and paints its own frames.
--
--   * Settings.RegisterCanvasLayoutCategory with InterfaceOptions_AddCategory
--     fallback. A thin slash wrapper makes `/mdb toggles` open this panel and
--     `/mdb overlay on|off` show/hide the overlay immediately (Bridge.lua's
--     stub still owns the DB write; Panel.lua only honors the result).
--   * Overlay: small movable frame (SetMovable + LeftButton drag +
--     ClampedToScreen), 13 compact buttons that are RED when the toggle is
--     OFF, position saved in DB.Ui, right-click resets, default hidden and
--     never allowed to overlap the pixel strip. Honors DB.Ui.Overlay on login.
--   * Toggle authority stays in Toggles.lua; a missing key reads as ON. Every
--     game-facing call is pcall-guarded and a failure degrades to "leave it
--     alone" -- the panel can never mute the rotation by itself.

local addonName, MDB = ...;

local PANEL_NAME = "MaxDps Bridge Toggles";
local OVERLAY_NAME = "MaxDpsBridgeOverlay";

local OVERLAY_COLS = 4;
local BTN_W, BTN_H = 38, 20;
local BTN_GAP = 2;
local OVERLAY_HEADER_H = 18;

-- Default overlay anchor (Bridge.Defaults.Ui). Kept as literals because
-- Defaults is a local in Bridge.lua; these are the frozen values.
local UI_DEFAULT_POINT = "CENTER";
local UI_DEFAULT_X = 0;
local UI_DEFAULT_Y = 0;

-- UI palette (GW2_UI / Enhance-QoL-ish dark surfaces).
local BG_R, BG_G, BG_B = 0.06, 0.06, 0.07;
local ON_R, ON_G, ON_B = 0.10, 0.32, 0.14;
local OFF_R, OFF_G, OFF_B = 0.45, 0.07, 0.07;
local ACCENT = { 0.00, 0.85, 1.00 };

local PANEL_BACKDROP = {
  bgFile = "Interface\\Buttons\\WHITE8x8",
  edgeFile = "Interface\\Tooltips\\UI-Tooltip-Border",
  edgeSize = 12,
  insets = { left = 3, right = 3, top = 3, bottom = 3 },
};
local OVERLAY_BACKDROP = {
  bgFile = "Interface\\Buttons\\WHITE8x8",
  edgeFile = "Interface\\Tooltips\\UI-Tooltip-Border",
  edgeSize = 10,
  insets = { left = 2, right = 2, top = 2, bottom = 2 },
};
local BTN_BACKDROP = {
  bgFile = "Interface\\Buttons\\WHITE8x8",
  edgeFile = "Interface\\Tooltips\\UI-Tooltip-Border",
  edgeSize = 8,
  insets = { left = 1, right = 1, top = 1, bottom = 1 },
};

-- OFF effects, straight from the spec's gate table. Shown on hover so the
-- user knows exactly what an OFF costs before clicking.
local OFF_EFFECT = {
  Main = "the main rotation slot is left blank; the companion falls back.",
  Offensive = "the offensive slot is left blank.",
  Defensive = "the defensive slot is left blank (defensive urgency clears).",
  Consumable = "the consumable slot is left blank.",
  Trinket = "the trinket slot is left blank.",
  Interrupt = "the interrupt slot is left blank.",
  Mobility = "the mobility slot is left blank.",
  SelfHeal = "the self-heal slot and the SelfHeal2 block are blanked.",
  Solo = "defensive + self-heal are blanked while ungrouped (emergency HP <= 35% is still allowed).",
  OOC = "all 8 slots are blanked while out of combat.",
  AutoTarget = "the companion is never asked to acquire a target.",
  AutoInteract = "the companion is never asked to interact.",
  TTK = "target HP band is forced UNKNOWN; execute/band consumers go blind.",
};

-- Group veto labels (Stream 1 §1.3). The addon can only restrict by whole
-- category; the companion's per-ability ON/OFF veto stays its own authority.
-- Missing key = ON, exactly like the 13 toggles.
local VETO_GROUPS = { "SelfHeal", "Defensive", "Immunity" };
local VETO_LABELS = {
  SelfHeal = "Self-heal veto",
  Defensive = "Defensive veto",
  Immunity = "Immunity veto",
};
local VETO_EFFECT = {
  SelfHeal = "every self-heal candidate is blanked.",
  Defensive = "the defensive slot is blanked.",
  Immunity = "the defensive slot is blanked (the addon cannot tell an immunity apart from a major).",
};

local ABBR = {
  Main = "Mn", Offensive = "Of", Defensive = "De", Consumable = "Co",
  Trinket = "Tr", Interrupt = "In", Mobility = "Mo", SelfHeal = "Sh",
  Solo = "So", OOC = "OOC", AutoTarget = "AT", AutoInteract = "AI", TTK = "TTK",
};

local SECTIONS = {
  { Header = "Spells", Keys = {
    "Main", "Offensive", "Defensive", "Consumable",
    "Trinket", "Interrupt", "Mobility", "SelfHeal",
  } },
  { Header = "Behaviour", Keys = { "Solo", "OOC", "AutoTarget", "AutoInteract", "TTK" } },
};

-- Forward locals: helpers below close over these.
local Panel, Overlay, MinimapButton;
local CheckboxByKey = {};
local VetoCheckboxByKey = {};
local OverlayButtonByKey = {};
local BuildPanel, BuildOverlay, RefreshPanel, RefreshOverlayButtons, ApplyOverlay;

--- ======= TOGGLE SHIMS (dynamic: survive load-order surprises) =======

local function Keys ()
  local T = MDB.Toggles;
  if T and T.Keys then return T.Keys(); end
  return {};
end

local function IsOn (Key)
  local T = MDB.Toggles;
  if T and T.Get then return T.Get(Key); end
  return true;
end

local function Store (Key, On)
  local T = MDB.Toggles;
  if T and T.Set then return T.Set(Key, On); end
  return On ~= false;
end

local function VetoGet (Group)
  local T = MDB.Toggles;
  if T and T.Veto then return T.Veto(Group); end
  return true;
end

local function VetoStore (Group, On)
  local T = MDB.Toggles;
  if T and T.SetVeto then return T.SetVeto(Group, On); end
  return On ~= false;
end

local function VetoKeys ()
  local T = MDB.Toggles;
  if T and T.VetoKeys then return T.VetoKeys(); end
  return {};
end

local function Print (Text)
  if type(DEFAULT_CHAT_FRAME) == "table" and type(DEFAULT_CHAT_FRAME.AddMessage) == "function" then
    pcall(DEFAULT_CHAT_FRAME.AddMessage, DEFAULT_CHAT_FRAME, "|cFF33FF99MaxDps Bridge|r: " .. tostring(Text));
  end
end

local function Label (Key)
  local T = MDB.Toggles;
  if T and T.Label then return T.Label(Key); end
  return Key;
end

local function Db ()
  local DB = _G.MaxDpsBridgeDB;
  if type(DB) == "table" then return DB; end
  return nil;
end

local function Ui ()
  local DB = Db();
  if not DB then return nil; end
  if type(DB.Ui) ~= "table" then DB.Ui = {}; end
  return DB.Ui;
end

--- ======= TOOLTIPS =======

local function ShowToggleTip (Owner, Key)
  if type(GameTooltip) ~= "table" and type(GameTooltip) ~= "userdata" then return; end
  GameTooltip:SetOwner(Owner, "ANCHOR_RIGHT");
  GameTooltip:SetText(Label(Key) .. " toggle", 1, 1, 1);
  GameTooltip:AddLine("OFF: " .. (OFF_EFFECT[Key] or "the slot is left blank."), 1.0, 0.55, 0.55, true);
  GameTooltip:AddLine("effective = companion AND addon; an addon OFF wins.",
    0.70, 0.70, 0.70, true);
  GameTooltip:Show();
end

local function ShowVetoTip (Owner, Group)
  if type(GameTooltip) ~= "table" and type(GameTooltip) ~= "userdata" then return; end
  GameTooltip:SetOwner(Owner, "ANCHOR_RIGHT");
  GameTooltip:SetText((VETO_LABELS[Group] or Group) .. " toggle", 1, 1, 1);
  GameTooltip:AddLine("OFF: " .. (VETO_EFFECT[Group] or "every candidate in the group is blanked."), 1.0, 0.55, 0.55, true);
  GameTooltip:AddLine("effective = companion AND addon; an addon OFF wins. Missing key = ON.",
    0.70, 0.70, 0.70, true);
  GameTooltip:Show();
end

local function HideTip ()
  if type(GameTooltip) == "table" or type(GameTooltip) == "userdata" then
    GameTooltip:Hide();
  end
end

--- ======= PANEL =======

local function SectionHeader (Parent, Text, Y)
  local FS = Parent:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge");
  FS:SetPoint("TOPLEFT", Parent, "TOPLEFT", 16, Y);
  FS:SetText(Text);
  FS:SetTextColor(ACCENT[1], ACCENT[2], ACCENT[3]);
  return FS;
end

local function MakeCheckbox (Parent, Key, Y)
  local Check = CreateFrame("CheckButton", nil, Parent, "InterfaceOptionsCheckButtonTemplate");
  Check:SetPoint("TOPLEFT", Parent, "TOPLEFT", 18, Y);
  Check.Key = Key;
  if Check.Text then
    Check.Text:SetText(Label(Key));
    Check.Text:SetFontObject("GameFontNormalSmall");
  end
  Check:SetScript("OnEnter", function (self) ShowToggleTip(self, Key); end);
  Check:SetScript("OnLeave", HideTip);
  Check:SetScript("OnClick", function (self)
    Store(Key, self:GetChecked() and true or false);
    if ApplyOverlay then ApplyOverlay(); end
    RefreshOverlayButtons();
  end);
  CheckboxByKey[Key] = Check;
  return Check;
end

local function MakeVetoCheckbox (Parent, Group, Y)
  local Check = CreateFrame("CheckButton", nil, Parent, "InterfaceOptionsCheckButtonTemplate");
  Check:SetPoint("TOPLEFT", Parent, "TOPLEFT", 18, Y);
  Check.Group = Group;
  if Check.Text then
    Check.Text:SetText(VETO_LABELS[Group] or Group);
    Check.Text:SetFontObject("GameFontNormalSmall");
  end
  Check:SetScript("OnEnter", function (self) ShowVetoTip(self, Group); end);
  Check:SetScript("OnLeave", HideTip);
  Check:SetScript("OnClick", function (self)
    VetoStore(Group, self:GetChecked() and true or false);
    if ApplyOverlay then ApplyOverlay(); end
    RefreshOverlayButtons();
  end);
  VetoCheckboxByKey[Group] = Check;
  return Check;
end

local function MakeButton (Parent, Text, Width, X, Y, OnClick)
  local B = CreateFrame("Button", nil, Parent, "UIPanelButtonTemplate");
  B:SetSize(Width, 22);
  B:SetPoint("TOPLEFT", Parent, "TOPLEFT", X, Y);
  B:SetText(Text);
  B:SetScript("OnClick", OnClick);
  return B;
end

local function MakeUiCheckbox (Parent, Text, Y, GetValue, SetValue)
  local Check = CreateFrame("CheckButton", nil, Parent, "InterfaceOptionsCheckButtonTemplate");
  Check:SetPoint("TOPLEFT", Parent, "TOPLEFT", 18, Y);
  if Check.Text then
    Check.Text:SetText(Text);
    Check.Text:SetFontObject("GameFontNormalSmall");
  end
  Check:SetScript("OnClick", function (self)
    SetValue(self:GetChecked() and true or false);
  end);
  Check.Refresh = function (self) self:SetChecked(GetValue() and true or false); end;
  return Check;
end

local UiCheckboxes = {};

RefreshPanel = function ()
  local KeysList = Keys();
  for i = 1, #KeysList do
    local C = CheckboxByKey[KeysList[i]];
    if C then C:SetChecked(IsOn(KeysList[i]) and true or false); end
  end
  for i = 1, #VETO_GROUPS do
    local C = VetoCheckboxByKey[VETO_GROUPS[i]];
    if C then C:SetChecked(VetoGet(VETO_GROUPS[i]) and true or false); end
  end
  for i = 1, #UiCheckboxes do
    local C = UiCheckboxes[i];
    if C and C.Refresh then C:Refresh(); end
  end
end

local function RegisterPanel ()
  if MDB._TogglesPanelRegistered then return; end
  if _G.Settings and type(_G.Settings.RegisterCanvasLayoutCategory) == "function" then
    local Ok, Category = pcall(_G.Settings.RegisterCanvasLayoutCategory, Panel, PANEL_NAME);
    if Ok and Category then
      MDB._TogglesCategory = Category;
      if type(_G.Settings.RegisterAddOnCategory) == "function" then
        pcall(_G.Settings.RegisterAddOnCategory, Category);
      end
      MDB._TogglesPanelRegistered = true;
      return;
    end
  end
  if type(InterfaceOptions_AddCategory) == "function" then
    pcall(InterfaceOptions_AddCategory, Panel);
    MDB._TogglesPanelRegistered = true;
  end
end

BuildPanel = function ()
  if Panel then return Panel; end
  local P = CreateFrame("Frame", "MaxDpsBridgeTogglesPanel", UIParent, "BackdropTemplate");
  P:SetSize(360, 806);
  P:SetBackdrop(PANEL_BACKDROP);
  P:SetBackdropColor(BG_R, BG_G, BG_B, 0.96);
  P:SetBackdropBorderColor(0.25, 0.25, 0.30, 1);
  P.name = PANEL_NAME;
  P:Hide();
  Panel = P;

  local Title = P:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge");
  Title:SetPoint("TOPLEFT", P, "TOPLEFT", 16, -14);
  Title:SetText("MaxDps Bridge -- in-game toggles");
  Title:SetTextColor(1, 1, 1);

  local Hint = P:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall");
  Hint:SetPoint("TOPLEFT", Title, "BOTTOMLEFT", 0, -4);
  Hint:SetText("Addon OFF always wins. Missing key = ON.");
  Hint:SetTextColor(0.65, 0.65, 0.65);

  -- All on / All off.
  MakeButton(P, "All on", 80, 18, -58, function ()
    local KeysList = Keys();
    for i = 1, #KeysList do Store(KeysList[i], true); end
    RefreshPanel();
    RefreshOverlayButtons();
  end);
  MakeButton(P, "All off", 80, 104, -58, function ()
    local KeysList = Keys();
    for i = 1, #KeysList do Store(KeysList[i], false); end
    RefreshPanel();
    RefreshOverlayButtons();
  end);

  local Y = -92;
  for s = 1, #SECTIONS do
    SectionHeader(P, SECTIONS[s].Header, Y);
    Y = Y - 26;
    local SectionKeys = SECTIONS[s].Keys;
    for i = 1, #SectionKeys do
      MakeCheckbox(P, SectionKeys[i], Y);
      Y = Y - 26;
    end
    Y = Y - 8;
  end

  -- Group vetoes (restrict-only; same effective = companion AND addon rule).
  SectionHeader(P, "Group veto (restrict-only)", Y);
  Y = Y - 26;
  for i = 1, #VETO_GROUPS do
    MakeVetoCheckbox(P, VETO_GROUPS[i], Y);
    Y = Y - 26;
  end
  Y = Y - 8;

  -- Crowd control (v3.4.0 CC appendix). A 14th restrict-only toggle that is
  -- NOT part of the 13 canonical Keys(); missing = ON, addon OFF wins. Off the
  -- pixel path: it only writes MaxDpsBridgeDB.Toggles.CC.
  SectionHeader(P, "Crowd control (opt-in, restrict-only)", Y);
  Y = Y - 26;
  local CCCheck = MakeUiCheckbox(P, "Allow crowd control (addon side)",
    Y,
    function ()
      local T = MDB.Toggles;
      if T and T.IsCC then return T.IsCC(); end
      return true;
    end,
    function (On)
      local T = MDB.Toggles;
      if T and T.SetCC then T.SetCC(On); end
    end);
  UiCheckboxes[#UiCheckboxes + 1] = CCCheck;
  Y = Y - 34;

  -- Overlay section.
  SectionHeader(P, "Overlay", Y);
  Y = Y - 28;
  local ShowOverlay = MakeUiCheckbox(P, "Show overlay", Y,
    function () local U = Ui(); return U and U.Overlay or false; end,
    function (On)
      local U = Ui();
      if U then U.Overlay = On; end
      if ApplyOverlay then ApplyOverlay(); end
    end);
  UiCheckboxes[#UiCheckboxes + 1] = ShowOverlay;
  Y = Y - 26;
  local MinimapCheck = MakeUiCheckbox(P, "Minimap button", Y,
    function () local U = Ui(); return U and U.Minimap or false; end,
    function (On)
      local U = Ui();
      if U then U.Minimap = On; end
      if MDB.ApplyMinimap then MDB.ApplyMinimap(); end
    end);
  UiCheckboxes[#UiCheckboxes + 1] = MinimapCheck;

  -- Version footer.
  local Footer = P:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall");
  Footer:SetPoint("BOTTOMLEFT", P, "BOTTOMLEFT", 16, 14);
  Footer:SetText(("MaxDpsBridge v%s  |  pixel strip untouched"):format(tostring(MDB.VERSION or "?")));
  Footer:SetTextColor(0.55, 0.55, 0.60);

  P:SetScript("OnShow", RefreshPanel);
  RegisterPanel();
  RefreshPanel();
  return P;
end

-- Open the settings panel (`/mdb toggles`).
function MDB.OpenToggles ()
  BuildPanel();
  local Category = MDB._TogglesCategory;
  if _G.Settings and type(_G.Settings.OpenToCategory) == "function" then
    local Id = Category;
    if type(Category) == "table" and type(Category.GetID) == "function" then
      local OkId, V = pcall(Category.GetID, Category);
      if OkId then Id = V; end
    end
    if pcall(_G.Settings.OpenToCategory, Id) then return; end
    if pcall(_G.Settings.OpenToCategory, PANEL_NAME) then return; end
  end
  if type(InterfaceOptionsFrame_OpenToCategory) == "function" then
    -- Legacy frame needs the double call (known Blizzard quirk).
    pcall(InterfaceOptionsFrame_OpenToCategory, Panel);
    pcall(InterfaceOptionsFrame_OpenToCategory, Panel);
  end
end

--- ======= OVERLAY =======

local function OverlayColor (Button, On)
  if On then
    Button:SetBackdropColor(ON_R, ON_G, ON_B, 1);
    Button:SetBackdropBorderColor(0.30, 0.60, 0.35, 1);
  else
    Button:SetBackdropColor(OFF_R, OFF_G, OFF_B, 1);
    Button:SetBackdropBorderColor(0.80, 0.25, 0.25, 1);
  end
end

local function RectOverlap (aL, aB, aW, aH, bL, bB, bW, bH)
  return aL < bL + bW and aL + aW > bL and aB < bB + bH and aB + aH > bB;
end

-- True when the overlay does not intrude into the pixel-strip rectangle.
local function ClearOfStrip ()
  if not Overlay then return true; end
  local Strip = _G.MaxDpsBridge_Block;
  if not Strip then return true; end
  local OkO, ol, ob, ow, oh = pcall(Overlay.GetRect, Overlay);
  local OkS, sl, sb, sw, sh = pcall(Strip.GetRect, Strip);
  if not (OkO and OkS) then return true; end
  if type(ol) ~= "number" or type(sl) ~= "number" then return true; end
  return not RectOverlap(ol, ob, ow, oh, sl, sb, sw, sh);
end

local function ApplyOverlayPosition ()
  if not Overlay then return; end
  local U = Ui();
  if not U then return; end
  local Point = U.Point;
  if type(Point) ~= "string" or Point == "" then Point = UI_DEFAULT_POINT; end
  local X = U.X; if type(X) ~= "number" then X = UI_DEFAULT_X; end
  local Y = U.Y; if type(Y) ~= "number" then Y = UI_DEFAULT_Y; end
  Overlay:ClearAllPoints();
  Overlay:SetPoint(Point, UIParent, Point, X, Y);
  -- Hard guarantee: never sit on the pixel strip. Snap home when we would.
  if not ClearOfStrip() then
    U.Point, U.X, U.Y = UI_DEFAULT_POINT, UI_DEFAULT_X, UI_DEFAULT_Y;
    Overlay:ClearAllPoints();
    Overlay:SetPoint(UI_DEFAULT_POINT, UIParent, UI_DEFAULT_POINT, UI_DEFAULT_X, UI_DEFAULT_Y);
  end
end

local function ResetOverlayPosition ()
  local U = Ui();
  if U then
    U.Point, U.X, U.Y = UI_DEFAULT_POINT, UI_DEFAULT_X, UI_DEFAULT_Y;
  end
  ApplyOverlayPosition();
end

local function MakeOverlayButton (Parent, Key, Col, Row)
  local B = CreateFrame("Button", nil, Parent, "BackdropTemplate");
  B:SetSize(BTN_W, BTN_H);
  B:SetPoint("TOPLEFT", Parent, "TOPLEFT",
    BTN_GAP + Col * (BTN_W + BTN_GAP),
    -(OVERLAY_HEADER_H + BTN_GAP + Row * (BTN_H + BTN_GAP)));
  B:SetBackdrop(BTN_BACKDROP);
  local FS = B:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall");
  FS:SetPoint("CENTER");
  FS:SetText(ABBR[Key] or Key);
  B.Label = FS;
  B.Key = Key;
  B:RegisterForClicks("LeftButtonUp", "RightButtonUp");
  B:SetScript("OnEnter", function (self) ShowToggleTip(self, Key); end);
  B:SetScript("OnLeave", HideTip);
  B:SetScript("OnClick", function (self, Mouse)
    if Mouse == "RightButton" then ResetOverlayPosition(); return; end
    Store(Key, not IsOn(Key));
    OverlayColor(self, IsOn(Key));
    RefreshPanel();
  end);
  OverlayButtonByKey[Key] = B;
  return B;
end

RefreshOverlayButtons = function ()
  local KeysList = Keys();
  for i = 1, #KeysList do
    local B = OverlayButtonByKey[KeysList[i]];
    if B then OverlayColor(B, IsOn(KeysList[i])); end
  end
end

BuildOverlay = function ()
  if Overlay then return Overlay; end
  local O = CreateFrame("Frame", OVERLAY_NAME, UIParent, "BackdropTemplate");
  local Rows = math.ceil(#Keys() / OVERLAY_COLS);
  if Rows < 1 then Rows = 4; end
  O:SetSize(OVERLAY_COLS * BTN_W + (OVERLAY_COLS + 1) * BTN_GAP,
    OVERLAY_HEADER_H + BTN_GAP + Rows * BTN_H + (Rows - 1) * BTN_GAP + BTN_GAP);
  O:SetBackdrop(OVERLAY_BACKDROP);
  O:SetBackdropColor(BG_R, BG_G, BG_B, 0.92);
  O:SetBackdropBorderColor(0.30, 0.30, 0.35, 1);
  O:SetMovable(true);
  O:SetClampedToScreen(true);
  O:SetFrameStrata("MEDIUM");
  O:EnableMouse(true);
  O:RegisterForDrag("LeftButton");
  O:SetScript("OnDragStart", function (self) self:StartMoving(); end);
  O:SetScript("OnDragStop", function (self)
    self:StopMovingOrSizing();
    local Point, _, _, X, Y = self:GetPoint(1);
    local U = Ui();
    if U then
      U.Point = Point or UI_DEFAULT_POINT;
      U.X = math.floor((X or 0) + 0.5);
      U.Y = math.floor((Y or 0) + 0.5);
    end
    if not ClearOfStrip() then ResetOverlayPosition(); end
  end);
  -- Frame (not Button): no RegisterForClicks; OnMouseUp handles right-click reset.
  O:SetScript("OnMouseUp", function (_, Mouse)
    if Mouse == "RightButton" then ResetOverlayPosition(); end
  end);

  local Header = O:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall");
  Header:SetPoint("TOPLEFT", O, "TOPLEFT", 5, -3);
  Header:SetText("Toggles  (right-click resets)");
  Header:SetTextColor(ACCENT[1], ACCENT[2], ACCENT[3]);

  local KeysList = Keys();
  for i = 1, #KeysList do
    local Col = (i - 1) % OVERLAY_COLS;
    local Row = math.floor((i - 1) / OVERLAY_COLS);
    MakeOverlayButton(O, KeysList[i], Col, Row);
  end

  Overlay = O;
  ApplyOverlayPosition();
  RefreshOverlayButtons();
  O:Hide();
  return O;
end

ApplyOverlay = function ()
  BuildOverlay();
  if not Overlay then return; end
  local U = Ui();
  if not U then return; end
  if U.Overlay then
    ApplyOverlayPosition();
    Overlay:Show();
    RefreshOverlayButtons();
    RefreshPanel();
  else
    Overlay:Hide();
  end
end

--- ======= MINIMAP BUTTON (optional launcher) =======

local function ApplyMinimap ()
  local U = Ui();
  if not U then return; end
  if U.Minimap then
    if not MinimapButton then
      if not _G.Minimap then return; end
      local B = CreateFrame("Button", "MaxDpsBridgeMinimapButton", _G.Minimap);
      B:SetSize(20, 20);
      B:SetFrameStrata("MEDIUM");
      B:SetPoint("BOTTOMLEFT", _G.Minimap, "BOTTOMLEFT", 8, 8);
      local Icon = B:CreateTexture(nil, "BACKGROUND");
      Icon:SetTexture("Interface\\Icons\\INV_Misc_QuestionMark");
      Icon:SetAllPoints(B);
      B:SetHighlightTexture("Interface\\Minimap\\UI-Minimap-ZoomButton-Highlight");
      B:RegisterForClicks("LeftButtonUp", "RightButtonUp");
      B:SetScript("OnClick", function (_, Mouse)
        if Mouse == "RightButton" then
          local D = Ui();
          if D then D.Minimap = false; end
          if MDB.ApplyMinimap then MDB.ApplyMinimap(); end
        else
          MDB.OpenToggles();
        end
      end);
      B:SetScript("OnEnter", function (self)
        GameTooltip:SetOwner(self, "ANCHOR_RIGHT");
        GameTooltip:SetText("MaxDps Bridge", 1, 1, 1);
        GameTooltip:AddLine("Click: open in-game toggles");
        GameTooltip:AddLine("Right-click: hide this button", 0.7, 0.7, 0.7, true);
        GameTooltip:Show();
      end);
      B:SetScript("OnLeave", HideTip);
      MinimapButton = B;
    end
    MinimapButton:Show();
  elseif MinimapButton then
    MinimapButton:Hide();
  end
end

MDB.ApplyMinimap = ApplyMinimap;
MDB.ApplyOverlay = ApplyOverlay;
MDB.RefreshOverlayButtons = RefreshOverlayButtons;
MDB.RefreshPanelCheckboxes = RefreshPanel;

--- ======= SLASH WRAPPER =======
-- Bridge.lua owns /mdb (and persists DB.Ui.Overlay as a stub). We only add
-- side effects: `/mdb toggles` opens this panel, every other command re-syncs
-- the overlay so `/mdb overlay on|off` shows/hides immediately.

local function InstallSlashWrapper ()
  if MDB._SlashWrapped then return; end
  local List = _G.SlashCmdList;
  if type(List) ~= "table" then return; end
  local Base = List["MAXDPSBRIDGE"];
  if type(Base) ~= "function" then return; end
  MDB._SlashWrapped = true;
  List["MAXDPSBRIDGE"] = function (Input)
    Base(Input);
    local Command, Arg1 = strsplit(" ", strlower(strtrim(Input or "")));
    if Command == "toggles" then
      MDB.OpenToggles();
    else
      -- Stream 1 §1.4 copy: Bridge owns the `why heal` blank reasons; this
      -- appends the unknown-HP note (an unreadable HP is never a blank).
      if Command == "why" and strlower(strtrim(Arg1 or "")) == "heal" then
        Print("unknown HP = allowed (Solo never blanks on an unreadable HP; missing key = ON)");
      end
      ApplyOverlay();
    end
  end
end

--- ======= BOOTSTRAP =======

do
  local Loader = CreateFrame("Frame");
  Loader:RegisterEvent("ADDON_LOADED");
  Loader:RegisterEvent("PLAYER_LOGIN");
  Loader:SetScript("OnEvent", function (self, Event, Arg1)
    if Event == "ADDON_LOADED" then
      if Arg1 ~= addonName then return; end
      -- Bridge.lua's loader was registered first, so MaxDpsBridgeDB and the
      -- /mdb handler exist by now; guard anyway and let PLAYER_LOGIN retry.
      if Db() then
        BuildOverlay();
        ApplyOverlay();
      end
      InstallSlashWrapper();
      return;
    end
    -- PLAYER_LOGIN: honor DB.Ui.Overlay / DB.Ui.Minimap on login and build
    -- the panel so it is ready before the first `/mdb toggles`.
    BuildPanel();
    BuildOverlay();
    ApplyOverlay();
    ApplyMinimap();
    InstallSlashWrapper();
    self:UnregisterAllEvents();
  end);
end
