--- ============================ HEADER ============================
-- MaxDpsBridgeExp - insecure overlay (RICE-style replica surface).
--
-- Plain Frame + BackdropTemplate. NO secure templates, NO protected
-- attributes, NO RegisterForClicks automation: every pill is an ordinary
-- insecure Button that flips one in-game toggle via MDBX.Toggles. The
-- overlay is deliberately off the pixel path and can never drive gameplay.
--
-- Layout: Header (drag handle + lock + title) -> PillGrid (14 pills, 4
-- columns) -> Footer (active profile + status dot).
--
-- Redraw policy: a dirty flag plus a 0.2 s C_Timer ticker that exists ONLY
-- while the overlay is shown; OnHide cancels it. Structural rebuilds that
-- would run while InCombatLockdown() are queued and flushed on
-- PLAYER_REGEN_ENABLED. Show/Hide/SetScale/SetAlpha are insecure and run
-- immediately (they never touch a protected frame).
--
-- Icons: prefer the committed Interface\AddOns\MaxDpsBridgeExp\Exp\Assets\
-- <keylower>-64 TGA when present, else the per-key stock Interface\Icons art
-- in BLIZZARD_ICONS, else INV_Misc_QuestionMark (see Exp/Assets/README.md).
--
-- GW2_UI skin (OptionalDep): slices 1+2 restyle the frame, fonts and pills
-- with GW2 art. GW2 files are referenced by RUNTIME PATH STRING ONLY - never
-- copied or shipped (GW2_UI is all-rights-reserved with no LICENSE). Every
-- path is probed at runtime; when GW2_UI is absent or a texture fails to load
-- the stock BackdropTemplate chrome / Blizzard fonts stay in place.

local addonName, MDBX = ...;

local O = {};
MDBX.Overlay = O;

local FRAME_NAME = "MaxDpsBridgeExpOverlay";
local TICKER_PERIOD = 0.2;
local PILL_KEYS = {
  "Main", "Offensive", "Defensive", "Consumable", "Trinket", "Interrupt",
  "Mobility", "SelfHeal", "Solo", "OOC", "AutoTarget", "AutoInteract",
  "TTK", "CC",
};
local FALLBACK_ICON = "Interface\\Icons\\INV_Misc_QuestionMark";
local ICON_PATH = "Interface\\AddOns\\MaxDpsBridgeExp\\Exp\\Assets\\";
-- Per-key stock Blizzard art for the PRIMARY pill icon when the committed -64
-- TGA is absent (Assets\ ships only a README today). This keeps every pill on
-- real class art instead of the QuestionMark placeholder. Names are the
-- Interface\Icons file stems used directly on retail.
local BLIZZARD_ICONS = {
  Main = "INV_Sword_04",
  Offensive = "Ability_Warrior_OffensiveStance",
  Defensive = "Ability_Warrior_DefensiveStance",
  Consumable = "INV_Potion_51",
  Trinket = "INV_Jewelry_Trinket_01",
  Interrupt = "Ability_Kick",
  Mobility = "Ability_Rogue_Sprint",
  SelfHeal = "Spell_Holy_Heal",
  Solo = "Ability_Hunter_SniperTraining",
  OOC = "Ability_Stealth",
  AutoTarget = "Ability_Hunter_MarkedForDeath",
  AutoInteract = "INV_Misc_Gear_01",
  TTK = "INV_Misc_PocketWatch_01",
  CC = "Spell_Frost_FrostNova",
};

-- ---- slice 4 readability helpers (display only) --------------------------
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

-- ---- GW2_UI skin (slice 1+2, optional) -----------------------------------
-- Runtime path strings only (never copy GW2 files). GW2_UI is an OptionalDep;
-- when absent every probe below fails and the stock chrome is used unchanged.
-- "Loadable" is verified by painting the path onto a shown probe texture and
-- reading Texture:GetTexture(), which is nil for a missing texture on retail.
local GW2_ADDON = "GW2_UI";
local GW2_ROOT = "Interface\\AddOns\\GW2_UI\\";
local GW2_BG = GW2_ROOT .. "textures\\uistuff\\ui-tooltip-background.png";
local GW2_EDGE = GW2_ROOT .. "textures\\uistuff\\ui-tooltip-border.png";
local GW2_HOVER = GW2_ROOT .. "textures\\uistuff\\button_hover.png";
local GW2_GLOW = GW2_ROOT .. "textures\\uistuff\\glow.png";
local GW2_HEADER_FONT = GW2_ROOT .. "Fonts\\headlines.ttf";
local GW2_BODY_FONT = GW2_ROOT .. "Fonts\\menomonia.ttf";
local LIGHT_HEADER = { 1, 0.945, 0.82 };
local WINE_TICK = { 0.59, 0.20, 0.20 };

local GW2 = { probed = false, loaded = false, bg = false, edge = false,
              hover = false, hoverPath = nil, headerFont = false, bodyFont = false };
local ProbeFrame, ProbeTex;

local function EnsureProbe ()
  if ProbeFrame then return; end;
  ProbeFrame = CreateFrame("Frame", nil, UIParent);
  ProbeFrame:SetSize(2, 2);
  ProbeFrame:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 0, 0);
  ProbeFrame:SetAlpha(0.01);
  ProbeFrame:Show();
  ProbeTex = ProbeFrame:CreateTexture(nil, "BACKGROUND");
  ProbeTex:SetAllPoints();
end

local function TextureLoads (Path)
  if type(Path) ~= "string" then return false; end;
  if not pcall(EnsureProbe) or not ProbeTex then return false; end;
  -- GetTexture() only resolves a shown region, so keep the probe shown
  -- while probing (hidden again once Ensure finishes building the chrome).
  if ProbeFrame and not ProbeFrame:IsShown() then ProbeFrame:Show(); end;
  if not pcall(ProbeTex.SetTexture, ProbeTex, Path) then return false; end;
  return ProbeTex:GetTexture() ~= nil;
end

local function FontLoads (Path)
  if type(Path) ~= "string" then return false; end;
  local Ok, F = pcall(CreateFont, nil);
  if not Ok or not F then return false; end;
  if not pcall(F.SetFont, F, Path, 12, "OUTLINE") then return false; end;
  local Got = F:GetFont();
  return type(Got) == "string" and Got ~= "";
end

--- Probe GW2_UI once. All flags stay false without the addon so the fallback
-- chrome is used and no GW2 path is ever requested in vain.
function O.ResolveGw2 ()
  if GW2.probed then return GW2; end;
  GW2.probed = true;
  if MDBX.IsAddonLoaded then
    local Ok, Loaded = pcall(MDBX.IsAddonLoaded, GW2_ADDON);
    if Ok then GW2.loaded = (Loaded == true); end;
  end;
  if not GW2.loaded then return GW2; end;
  GW2.bg = TextureLoads(GW2_BG);
  GW2.edge = TextureLoads(GW2_EDGE);
  if TextureLoads(GW2_HOVER) then
    GW2.hover = true; GW2.hoverPath = GW2_HOVER;
  elseif TextureLoads(GW2_GLOW) then
    GW2.hover = true; GW2.hoverPath = GW2_GLOW;
  end;
  GW2.headerFont = FontLoads(GW2_HEADER_FONT);
  GW2.bodyFont = FontLoads(GW2_BODY_FONT);
  return GW2;
end

function O.Gw2Active () O.ResolveGw2(); return GW2.loaded == true; end
function O.Gw2Probe () O.ResolveGw2(); return GW2; end

--- GW2 fonts have no Blizzard fallback glyph table; apply only when the TTF
-- probed as loadable, else keep GameFontNormal / GameFontHighlight.
local function ApplyFont (FontString, Which)
  if not FontString then return false; end;
  if Which == "header" and GW2.headerFont then
    if pcall(FontString.SetFont, FontString, GW2_HEADER_FONT, 15, "OUTLINE") then
      FontString:SetShadowColor(0, 0, 0, 1);
      FontString:SetShadowOffset(1, -1);
      return true;
    end
  elseif Which == "body" and GW2.bodyFont then
    if pcall(FontString.SetFont, FontString, GW2_BODY_FONT, 12, "OUTLINE") then
      FontString:SetShadowColor(0, 0, 0, 1);
      FontString:SetShadowOffset(1, -1);
      return true;
    end
  elseif Which == "pill" and GW2.bodyFont then
    -- Slice 1: compact 11 px body face for short labels; keeps an 8-char
    -- label inside a 68 px pill (4 cols / 300 px) without truncation.
    if pcall(FontString.SetFont, FontString, GW2_BODY_FONT, 11, "OUTLINE") then
      FontString:SetShadowColor(0, 0, 0, 1);
      FontString:SetShadowOffset(1, -1);
      return true;
    end
  end;
  if FontString.SetFontObject then
    if Which == "header" then
      FontString:SetFontObject("GameFontNormal");
    elseif Which == "pill" then
      FontString:SetFontObject("GameFontHighlightSmall");
    else
      FontString:SetFontObject("GameFontHighlight");
    end;
  end;
  -- Slice 4: keep OUTLINE + shadow even on the GameFont fallback path (the GW2
  -- branch above already sets both), so pill/title text never loses its edge.
  if FontString.GetFont and FontString.SetFont then
    local Ok, Font, Size = pcall(FontString.GetFont, FontString);
    if Ok and Font and Size then pcall(FontString.SetFont, FontString, Font, Size, "OUTLINE"); end;
  end;
  if FontString.SetShadowColor then FontString:SetShadowColor(0, 0, 0, 1); end;
  if FontString.SetShadowOffset then FontString:SetShadowOffset(1, -1); end;
  return false;
end

local Frame, Header, Grid, Footer, FooterProfile, FooterDot, Ticker;
local Pills = {};
local Dirty = true;
local CombatQueue = nil;
local CombatWatcher = nil;

-- ---- DB ------------------------------------------------------------------
local DEFAULTS = {
  shown = false, locked = false, scale = 1.0, alpha = 1.0,
  point = "CENTER", x = 0, y = 0, pillCols = 4,
};

local function DB ()
  if type(_G.MaxDpsBridgeExpDB) ~= "table" then _G.MaxDpsBridgeExpDB = {}; end;
  local D = _G.MaxDpsBridgeExpDB;
  if type(D.overlay) ~= "table" then D.overlay = {}; end;
  for Key, Value in pairs(DEFAULTS) do
    if D.overlay[Key] == nil then D.overlay[Key] = Value; end;
  end;
  return D.overlay;
end

local function Clamp (Value, Lo, Hi, Fallback)
  if type(Value) ~= "number" or Value ~= Value then return Fallback; end;
  if Value < Lo then return Lo; end;
  if Value > Hi then return Hi; end;
  return Value;
end

-- ---- combat-deferred structural work -------------------------------------
local function FlushQueue ()
  local Queue = CombatQueue;
  CombatQueue = nil;
  if not Queue then return; end;
  for i = 1, #Queue do pcall(Queue[i]); end;
end

function O.EnsureCombatWatcher ()
  if CombatWatcher then return CombatWatcher; end;
  CombatWatcher = CreateFrame("Frame");
  CombatWatcher:RegisterEvent("PLAYER_REGEN_ENABLED");
  CombatWatcher:SetScript("OnEvent", function () FlushQueue(); end);
  return CombatWatcher;
end

--- Run Fn now, or queue it when InCombatLockdown() says the rebuild is not
-- safe. Flushed on PLAYER_REGEN_ENABLED.
function O.Defer (Fn)
  if type(Fn) ~= "function" then return; end;
  local InCombat = false;
  if type(InCombatLockdown) == "function" then
    local Ok, Value = pcall(InCombatLockdown);
    if Ok then InCombat = (Value == true); end;
  end;
  if InCombat then
    CombatQueue = CombatQueue or {};
    CombatQueue[#CombatQueue + 1] = Fn;
    O.EnsureCombatWatcher();
  else
    pcall(Fn);
  end;
end

-- ---- events that dirty the overlay ---------------------------------------
function O.SetDirty () Dirty = true; end

local function IconFor (Key)
  -- Priority: committed custom -64 TGA (written by tools/fetch_assets.ps1)
  -- first, then the per-key stock Interface\Icons art, then the ugly
  -- QuestionMark so an unmapped key is still visible but never blocks the row.
  -- Returns the chosen path plus a UsedFallback flag.
  local Custom = ICON_PATH .. Key:lower() .. "-64";
  if TextureLoads(Custom) then return Custom, false; end;
  local Name = BLIZZARD_ICONS[Key];
  if Name then
    local Blizz = "Interface\\Icons\\" .. Name;
    if TextureLoads(Blizz) then return Blizz, false; end;
  end;
  return FALLBACK_ICON, true;
end
-- Slice 3: the standalone config window reuses the same custom-TGA -> stock
-- Blizzard -> QuestionMark resolver and the same per-key art map so a toggle
-- row's icon matches its pill exactly.
O.IconFor = IconFor;
O.BLIZZARD_ICONS = BLIZZARD_ICONS;
O.FALLBACK_ICON = FALLBACK_ICON;
O.PILL_KEYS = PILL_KEYS;

-- ---- frame construction --------------------------------------------------
local function BuildHeader ()
  Header = CreateFrame("Frame", nil, Frame);
  Header:SetPoint("TOPLEFT", Frame, "TOPLEFT", 1, -1);
  Header:SetPoint("TOPRIGHT", Frame, "TOPRIGHT", -1, -1);
  Header:SetHeight(26);
  Header:EnableMouse(true);
  Header:RegisterForDrag("LeftButton");
  Header:SetScript("OnDragStart", function ()
    if Frame and not DB().locked then Frame:StartMoving(); end;
  end);
  Header:SetScript("OnDragStop", function ()
    if Frame then Frame:StopMovingOrSizing(); O.SavePosition(); end;
  end);

  local Title = Header:CreateFontString(nil, "OVERLAY", "GameFontNormal");
  Title:SetPoint("LEFT", 6, 0);
  Title:SetText("MaxDps Bridge Exp");
  ApplyFont(Title, "header");
  if GW2.loaded then
    Title:SetTextColor(LIGHT_HEADER[1], LIGHT_HEADER[2], LIGHT_HEADER[3], 1);
  end;

  local Lock = CreateFrame("Button", nil, Header);
  Lock:SetSize(24, 20);
  Lock:SetPoint("RIGHT", -3, 0);
  Lock:SetNormalFontObject("GameFontNormalSmall");
  Lock:SetText("L");
  Lock:SetScript("OnClick", function () O.SetLocked(not DB().locked); end);
  O.LockButton = Lock;
end

-- ---- pill paint (slice 2) ------------------------------------------------
--- Square GW2 button: dark fill + hover glow, full-saturation icon and light
-- label when ON; desaturated icon, dim label and a wine-red accent tick when
-- OFF. Hover uses the GW2 button_hover.png overlay when loadable, else a
-- brightness shift on the fill. Always insecure, never touches gameplay.
local function PaintPill (Pill, Hovered)
  if not Pill then return; end;
  local On = Pill.On == true;
  local H = Hovered == true;
  local UseTex = GW2.hover == true;
  local R, G, B;
  if On then
    if H and not UseTex then R, G, B = 0.15, 0.17, 0.21 else R, G, B = 0.09, 0.10, 0.12 end;
    if Pill.Icon then Pill.Icon:SetVertexColor(1, 1, 1, 1); end;
    if Pill.Label then Pill.Label:SetTextColor(0.93, 0.93, 0.88, 1); end;
    if Pill.Tick then Pill.Tick:Hide(); end;
  else
    if H and not UseTex then R, G, B = 0.12, 0.12, 0.15 else R, G, B = 0.06, 0.06, 0.07 end;
    if Pill.Icon then Pill.Icon:SetVertexColor(0.45, 0.45, 0.45, 1); end;
    if Pill.Label then Pill.Label:SetTextColor(0.66, 0.66, 0.68, 1); end;
    if Pill.Tick then Pill.Tick:Show(); end;
  end;
  if Pill.Bg then Pill.Bg:SetColorTexture(R, G, B, 0.95); end;
  if Pill.Hover then
    if H and UseTex then Pill.Hover:SetAlpha(0.55) else Pill.Hover:SetAlpha(0); end;
  end;
end

local function BuildPills ()
  Grid = CreateFrame("Frame", nil, Frame);
  Grid:SetPoint("TOPLEFT", Frame, "TOPLEFT", 8, -30);
  Grid:SetPoint("TOPRIGHT", Frame, "TOPRIGHT", -8, -30);
  Grid:SetHeight(Frame:GetHeight() - 62);

  Pills = {};
  for i = 1, #PILL_KEYS do
    local Key = PILL_KEYS[i];
    local Pill = CreateFrame("Button", nil, Grid);
    Pill:SetHeight(22);
    Pill.Key = Key;
    -- Square dark backdrop (sharp corners, GW2 button style). Covers the whole
    -- pill so a missing icon TGA never hides the row.
    Pill.Bg = Pill:CreateTexture(nil, "BACKGROUND");
    Pill.Bg:SetAllPoints();
    Pill.Bg:SetColorTexture(0.09, 0.10, 0.12, 0.95);
    -- GW2 hover glow overlay, alpha-gated in PaintPill; harmless when absent.
    Pill.Hover = Pill:CreateTexture(nil, "BORDER");
    Pill.Hover:SetAllPoints();
    if GW2.hoverPath then Pill.Hover:SetTexture(GW2.hoverPath); end;
    Pill.Hover:SetAlpha(0);
    -- Wine-red accent tick on the leading edge, shown while OFF.
    Pill.Tick = Pill:CreateTexture(nil, "ARTWORK");
    Pill.Tick:SetSize(2, 18);
    Pill.Tick:SetPoint("LEFT", 0, 0);
    Pill.Tick:SetColorTexture(WINE_TICK[1], WINE_TICK[2], WINE_TICK[3], 1);
    Pill.Tick:Hide();
    Pill.Icon = Pill:CreateTexture(nil, "ARTWORK");
    Pill.Icon:SetSize(14, 14);
    Pill.Icon:SetPoint("LEFT", 3, 0);
    -- Resolve custom -64 TGA -> per-key Blizzard map -> QuestionMark (IconFor)
    -- so a missing asset shows real class art and never blocks the row.
    local Path, UsedFallback = IconFor(Key);
    Pill.UsedFallback = UsedFallback == true;
    Pill.Icon:SetTexture(Path);
    Pill.Icon.Fallback = FALLBACK_ICON;
    -- Slice 1: compact GameFont object + short label; word-wrap off so the
    -- text is laid out on one line and LayoutPills sizes the pill to fit it.
    Pill.Label = Pill:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall");
    Pill.Label:SetPoint("LEFT", Pill.Icon, "RIGHT", 2, 0);
    Pill.Label:SetPoint("RIGHT", -2, 0);
    Pill.Label:SetJustifyH("LEFT");
    if Pill.Label.SetWordWrap then Pill.Label:SetWordWrap(false); end;
    if Pill.Label.SetNonSpaceWrap then Pill.Label:SetNonSpaceWrap(false); end;
    local Short = Key;
    if MDBX.Toggles and MDBX.Toggles.ShortLabel then Short = MDBX.Toggles.ShortLabel(Key); end;
    Pill.Label:SetText(Short);
    ApplyFont(Pill.Label, "pill");
    Pill.On = true;
    Pill.Hovered = false;
    Pill:RegisterForClicks("LeftButtonUp");
    Pill:SetScript("OnClick", function () O.FlipKey(Key); end);
    Pill:SetScript("OnEnter", function (self)
      self.Hovered = true;
      PaintPill(self, true);
      if GameTooltip then
        local Short = Key;
        if MDBX.Toggles and MDBX.Toggles.ShortLabel then Short = MDBX.Toggles.ShortLabel(Key); end;
        local Full = Key;
        if MDBX.Toggles and MDBX.Toggles.Label then Full = MDBX.Toggles.Label(Key); end;
        GameTooltip:SetOwner(self, "ANCHOR_RIGHT");
        -- Slice 4 tooltip: short label + full name + effect + bind + action.
        GameTooltip:SetText(tostring(Short) .. "  -  " .. tostring(Full), 1.00, 0.82, 0.36);
        GameTooltip:AddLine(EffectFor(Key), 0.90, 0.90, 0.88, true);
        local Bind = BindFor(Key);
        if Bind then
          GameTooltip:AddLine("Bind: " .. Bind, 0.55, 0.85, 1.00, false);
        else
          GameTooltip:AddLine("Bind: unbound", 0.55, 0.56, 0.60, false);
        end
        GameTooltip:AddLine("Click to toggle", 1.00, 0.82, 0.36, false);
        GameTooltip:Show();
      end
    end);
    Pill:SetScript("OnLeave", function (self)
      self.Hovered = false;
      PaintPill(self, false);
      if GameTooltip then GameTooltip:Hide(); end
    end);
    Pills[#Pills + 1] = Pill;
  end;
  O.LayoutPills();
end

local function BuildFooter ()
  Footer = CreateFrame("Frame", nil, Frame);
  Footer:SetPoint("BOTTOMLEFT", Frame, "BOTTOMLEFT", 8, 5);
  Footer:SetPoint("BOTTOMRIGHT", Frame, "BOTTOMRIGHT", -8, 5);
  Footer:SetHeight(18);

  FooterProfile = Footer:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall");
  FooterProfile:SetPoint("LEFT", 0, 0);
  ApplyFont(FooterProfile, "body");

  FooterDot = Footer:CreateTexture(nil, "ARTWORK");
  FooterDot:SetSize(8, 8);
  FooterDot:SetPoint("RIGHT", -2, 0);
  FooterDot:SetColorTexture(0.2, 0.9, 0.3, 1);
end

-- ---- frame chrome (slice 1) ----------------------------------------------
--- Prefer the GW2 ui-tooltip nine-slice backdrop. edgeSize 32 matches the
-- 256x64 ui-tooltip-border.png corner block (GW2 uses GW.Scale(32); see
-- GW2_UI/.../lfg.lua:1012 and Core/backdropTemplates.lua Default), with
-- insets {2,2,2,2}. The bg texture is left untinted (GW2 never tints the
-- Default template) so the art's own tone shows. Else keep the stock
-- WHITE8x8 BackdropTemplate. BackdropTemplate is the modern nine-slice
-- renderer; NineSliceUtil.ApplyLayout takes *atlas* layouts only (see
-- Blizzard_SharedXML/NineSlice.lua) so these custom PNGs route through it.
local function ApplyFrameBackdrop ()
  if not Frame then return; end;
  if GW2.loaded and GW2.bg and Frame.SetBackdrop then
    local Recipe = {
      bgFile = GW2_BG,
      edgeFile = GW2.edge and GW2_EDGE or nil,
      tile = false,
      tileSize = 0,
      edgeSize = 32,
      insets = { left = 2, right = 2, top = 2, bottom = 2 },
    };
    if pcall(Frame.SetBackdrop, Frame, Recipe) then
      Frame:SetBackdropColor(1, 1, 1, 1);
      if GW2.edge then Frame:SetBackdropBorderColor(1, 1, 1, 1); end;
      return;
    end
  end;
  if Frame.SetBackdrop then
    Frame:SetBackdrop({
      bgFile = "Interface\\Buttons\\WHITE8x8",
      edgeFile = "Interface\\Buttons\\WHITE8x8",
      edgeSize = 1,
    });
    Frame:SetBackdropColor(0.05, 0.06, 0.08, 0.95);
    Frame:SetBackdropBorderColor(0.30, 0.80, 1.00, 0.85);
  end
end

function O.Ensure ()
  DB();
  if Frame then return Frame; end;
  O.ResolveGw2();

  local Mixin = (type(BackdropTemplateMixin) ~= "nil") and "BackdropTemplate" or nil;
  local Ok, NewFrame = pcall(CreateFrame, "Frame", FRAME_NAME, UIParent, Mixin);
  if not Ok or not NewFrame then return nil; end;
  Frame = NewFrame;
  Frame:SetSize(300, 210);
  Frame:SetMovable(true);
  Frame:SetClampedToScreen(true);
  Frame:SetFrameStrata("MEDIUM");
  Frame:SetToplevel(false);
  ApplyFrameBackdrop();

  BuildHeader();
  BuildPills();
  BuildFooter();
  if ProbeFrame then ProbeFrame:Hide(); end;

  -- Event-driven dirty flag; all handlers are trivial and insecure.
  Frame:RegisterEvent("PLAYER_SPECIALIZATION_CHANGED");
  Frame:RegisterEvent("PLAYER_TALENT_UPDATE");
  Frame:RegisterEvent("PLAYER_REGEN_DISABLED");
  Frame:RegisterEvent("PLAYER_REGEN_ENABLED");
  Frame:SetScript("OnShow", function () O.Refresh(); O.EnsureTicker(); end);
  Frame:SetScript("OnHide", function ()
    if Ticker then Ticker:Cancel(); Ticker = nil; end;
  end);
  Frame:SetScript("OnEvent", function ()
    if MDBX.Toggles and MDBX.Toggles.Version then
      local V = MDBX.Toggles.Version();
      if O._LastToggleVersion ~= V then O._LastToggleVersion = V; O.SetDirty(); end;
    else
      O.SetDirty();
    end
  end);

  Frame:Hide();
  O.Refresh();
  return Frame;
end

function O.EnsureTicker ()
  if Ticker then return; end;
  if not (C_Timer and C_Timer.NewTicker) then return; end;
  Ticker = C_Timer.NewTicker(TICKER_PERIOD, function ()
    if Dirty then O.Refresh(); end;
  end);
end

-- ---- layout / refresh ----------------------------------------------------
function O.LayoutPills ()
  local Width = Frame and Frame:GetWidth() or 300;
  local Cols = math.floor(Clamp(DB().pillCols, 1, 7, 4));
  local Gut = 4;
  local PillW = math.floor((Width - 16 - (Cols - 1) * Gut) / Cols);
  if PillW < 40 then PillW = 40; end;
  -- Slice 1 guarantee: widen the pill to the longest short label so text is
  -- never clipped/truncated (icon 14 + 3 left + 2 gap + 3 right = 22 px chrome).
  local Required = 0;
  for i = 1, #Pills do
    local P = Pills[i];
    local LW = 0;
    if P.Label and P.Label.GetStringWidth then
      local OkW, W = pcall(P.Label.GetStringWidth, P.Label);
      if OkW and type(W) == "number" then LW = W; end;
    end;
    if LW <= 0 and P.Label and P.Label.GetText then
      local OkT, T = pcall(P.Label.GetText, P.Label);
      if OkT then LW = (#tostring(T or "")) * 6; end;
    end;
    local Need = 3 + 14 + 2 + LW + 3;
    if Need > Required then Required = Need; end;
  end;
  if Required > PillW then PillW = math.ceil(Required); end;
  for i = 1, #Pills do
    local Pill = Pills[i];
    local Col = (i - 1) % Cols;
    local Row = math.floor((i - 1) / Cols);
    Pill:SetWidth(PillW);
    Pill:ClearAllPoints();
    Pill:SetPoint("TOPLEFT", Grid, "TOPLEFT", Col * (PillW + Gut), -Row * (22 + Gut));
  end;
end

function O.Refresh ()
  Dirty = false;
  if not Frame then return; end;
  if MDBX.Toggles and MDBX.Toggles.Version then
    O._LastToggleVersion = MDBX.Toggles.Version();
  end;
  for i = 1, #Pills do
    local Pill = Pills[i];
    local On = true;
    if MDBX.Toggles and MDBX.Toggles.Get then On = (MDBX.Toggles.Get(Pill.Key) ~= false); end;
    Pill.On = On;
    PaintPill(Pill, Pill.Hovered == true);
  end;
  if FooterProfile then
    local Mode = nil;
    if MDBX.Profiles and MDBX.Profiles.ActiveMode then pcall(function () Mode = MDBX.Profiles.ActiveMode(); end); end
    if type(Mode) == "string" and Mode ~= "" then
      FooterProfile:SetText("profile: " .. Mode);
      FooterProfile:SetTextColor(0.93, 0.93, 0.88, 1);
    else
      -- Slice 4 empty state: never leave the footer blank.
      FooterProfile:SetText("profile: (none)");
      FooterProfile:SetTextColor(0.55, 0.56, 0.60, 1);
    end
  end;
  if FooterDot and MDBX.Toggles and MDBX.Toggles.AppEpoch then
    local Epoch = 0;
    pcall(function () Epoch = MDBX.Toggles.AppEpoch(); end);
    if type(Epoch) == "number" and Epoch ~= 0 then
      FooterDot:SetColorTexture(0.2, 0.9, 0.3, 1);   -- companion live
    else
      FooterDot:SetColorTexture(0.95, 0.65, 0.2, 1); -- no companion seen
    end
  end
end

-- ---- visibility / geometry ----------------------------------------------
function O.SavePosition ()
  if not Frame then return; end;
  local Point, _, _, X, Y = Frame:GetPoint(1);
  local D = DB();
  if type(Point) == "string" then D.point = Point; end;
  D.x = math.floor(tonumber(X) or D.x or 0);
  D.y = math.floor(tonumber(Y) or D.y or 0);
end

function O.Show ()
  local F = O.Ensure();
  if not F then return; end;
  local D = DB();
  D.shown = true;
  F:SetScale(Clamp(D.scale, 0.5, 2.0, 1.0));
  F:SetAlpha(Clamp(D.alpha, 0.2, 1.0, 1.0));
  F:ClearAllPoints();
  F:SetPoint(D.point or "CENTER", UIParent, D.point or "CENTER", D.x or 0, D.y or 0);
  F:Show();
  O.SetDirty();
  O.Refresh();
end

function O.Hide ()
  if Frame then Frame:Hide(); end;
  DB().shown = false;
  if Ticker then Ticker:Cancel(); Ticker = nil; end;
end

function O.Toggle ()
  if Frame and Frame:IsShown() then O.Hide(); else O.Show(); end;
end

function O.SetLocked (Locked)
  DB().locked = (Locked == true);
  local Button = O.LockButton;
  if Button and Button.SetText then Button:SetText(DB().locked and "L" or "U"); end;
  O.SetDirty();
  return DB().locked;
end

function O.IsLocked () return DB().locked == true; end
function O.GetScale () return Clamp(DB().scale, 0.5, 2.0, 1.0); end
function O.GetAlpha () return Clamp(DB().alpha, 0.2, 1.0, 1.0); end
function O.GetPillCols () return math.floor(Clamp(DB().pillCols, 1, 7, 4)); end

function O.SetScale (Value)
  local D = DB();
  D.scale = Clamp(tonumber(Value), 0.5, 2.0, D.scale or 1.0);
  if Frame then Frame:SetScale(D.scale); end;
  return D.scale;
end

function O.SetAlpha (Value)
  local D = DB();
  D.alpha = Clamp(tonumber(Value), 0.2, 1.0, D.alpha or 1.0);
  if Frame then Frame:SetAlpha(D.alpha); end;
  return D.alpha;
end

function O.SetPillCols (Value)
  local D = DB();
  D.pillCols = math.floor(Clamp(tonumber(Value), 1, 7, D.pillCols or 4));
  O.Defer(function () O.LayoutPills(); O.SetDirty(); end);
  return D.pillCols;
end

function O.FlipKey (Key)
  if MDBX.Toggles and MDBX.Toggles.Flip then
    MDBX.Toggles.Flip(Key);
    O.SetDirty();
  end
end

function O.Reset ()
  local D = DB();
  for Key, Value in pairs(DEFAULTS) do D[Key] = Value; end;
  if Frame then
    Frame:ClearAllPoints();
    Frame:SetPoint("CENTER", UIParent, "CENTER", 0, 0);
    Frame:SetScale(1.0);
    Frame:SetAlpha(1.0);
    O.LayoutPills();
  end
  O.SetDirty();
end
