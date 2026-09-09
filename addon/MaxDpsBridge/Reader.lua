--- ============================ HEADER ============================
-- READ-ONLY readout of the live MaxDps engine. Never calls protected Lua,
-- never drives gameplay; it only reports what MaxDps already suggests.
--
--   Main       = MaxDps.Spell (number spellID, set by Core:InvokeNextSpell)
--   Cooldown   = first MaxDps.Flags[spellID]==true that is not interrupt /
--                defensive / consumable, while enableCooldowns is on
--   Interrupt  = Flags entry set via MaxDps:GlowInteruptMidnight
--   Defensive  = Flags entry set via MaxDps:GlowDefensiveHPMidnight
--   Consumable = flagged spellID that appears in MaxDps.ItemSpells values
--
-- Category tracking hooks the Midnight glow entry points because every path
-- funnels through GlowIndependent(spellId, spellId, ...) with identical ids,
-- so Flags alone cannot tell cooldown apart from interrupt/defensive.
--
-- ResolveBinding(spellID) priority:
--   1. MaxDps.Spells[spellID][i].HotKey:GetText() (skip byte-226 range text),
--      expanded back to a raw binding string
--   2. action slot scan 1-180 + ACTIONBUTTON/MULTIACTIONBAR mapping
--      (mirrors SpellFrame.lua GetKeybindForSpell)
--   3. MaxDpsSpellFrame.bindText, when visible (main spell only)
--   4. spell texture -> Bars.lua texture map fallback

local addonName, MDB = ...;

local InterruptSet = {};  -- spellID -> true, via GlowInteruptMidnight
local DefensiveSet = {};  -- spellID -> true, via GlowDefensiveHPMidnight
local CooldownSet = {};   -- spellID -> true, via GlowCooldownMidnight/GlowCooldown

local function MaxDpsEngine ()
  local Direct = _G.MaxDps;
  if Direct and (Direct.Spells or Direct.Flags or Direct.GlowIndependent) then
    return Direct;
  end
  -- MaxDps publishes itself as _G["MaxDps"] from Core.lua; fall back to the
  -- AceAddon registry when load order leaves the global unset.
  if type(LibStub) == "table" and LibStub.GetAddon then
    local Ok, Engine = pcall(LibStub.GetAddon, LibStub, "MaxDps", true);
    if Ok then return Engine; end
  end
  return Direct;
end

--- ======= CATEGORY HOOKS =======

local function SyncSet (Set, SpellID)
  local MaxDps = MaxDpsEngine();
  if MaxDps and MaxDps.Flags and MaxDps.Flags[SpellID] == true then
    Set[SpellID] = true;
  else
    Set[SpellID] = nil;
  end
end

local function WipeCache ()
  if MDB._BindCache then wipe(MDB._BindCache); end
end

function MDB.EnsureHooks ()
  if MDB._ReaderHooked then return; end
  local MaxDps = MaxDpsEngine();
  if not MaxDps then return; end
  if type(hooksecurefunc) ~= "function" then MDB._ReaderHooked = true; return; end

  local function TryHook (Method, Set)
    if type(MaxDps[Method]) == "function" then
      pcall(hooksecurefunc, MaxDps, Method, function (_, SpellID)
        if Set then
          if type(SpellID) == "number" then SyncSet(Set, SpellID); end
--- ======= DIAGNOSTICS =======

local function DiagPrint (Message)
  DEFAULT_CHAT_FRAME:AddMessage("|cFF00D8FFMDB|r: " .. Message);
end

function MDB.Diag ()
  local MDPS = MaxDpsEngine();
  if not MDPS then DiagPrint("no MaxDps engine"); return; end
    local Count, Shown = 0, 0;
    local WithHotKey, HotKeyText = 0, "-";
    if MDPS.Spells then
      for SpellID, Buttons in pairs(MDPS.Spells) do
        if type(Buttons) == "table" then
          Count = Count + 1;
          for i = 1, #Buttons do
            local Button = Buttons[i];
            local HotKey = Button and Button.HotKey;
            if not HotKey and Button and Button.GetName then
              local Name = Button:GetName();
              if Name then HotKey = _G[Name .. "HotKey"]; end
            end
            if HotKey and HotKey.GetText then
              local Ok, Text = pcall(HotKey.GetText, HotKey);
              if Ok and type(Text) == "string" and Text ~= "" and string.byte(Text) ~= 226 then
                WithHotKey = WithHotKey + 1;
                if HotKeyText == "-" then HotKeyText = tostring(SpellID) .. "=" .. Text; end
              end
            end
          end
        end
      end
    end
    local BarHit = {};
    for _, Prefix in ipairs({ "ActionButton", "MultiBarBottomLeftButton", "MultiBarBottomRightButton",
      "MultiBarRightButton", "MultiBarLeftButton", "ElvUI_Bar1Button", "BT4Button" }) do
      if _G[Prefix .. "1"] then BarHit[#BarHit + 1] = Prefix .. "1"; end
    end
    local SlotHit = 0;
    for Slot = 1, 180 do
      local ActionType = GetActionInfo(Slot);
      if ActionType == "spell" then SlotHit = SlotHit + 1; end
    end
    DiagPrint(("diag spells=%d withHotKey=%d e.g.%s bars={%s} spellSlots=%d/180")
      :format(Count, WithHotKey, HotKeyText, table.concat(BarHit, ","), SlotHit));
  else
          -- MaxDps:Fetch rebuilds Spells/Flags/ItemSpells wholesale.
          wipe(InterruptSet);
          wipe(DefensiveSet);
          wipe(CooldownSet);
        end
        WipeCache();
      end);
    end
  end

  TryHook("GlowInteruptMidnight", InterruptSet);
  TryHook("GlowDefensiveHPMidnight", DefensiveSet);
  TryHook("GlowCooldownMidnight", CooldownSet);
  TryHook("GlowCooldown", CooldownSet);
  TryHook("Fetch", nil);
  MDB._ReaderHooked = true;
end

--- ======= ENGINE ENSURE =======

-- MaxDps class modules are LoadOnDemand and the rotation only starts on
-- game events (login, combat, target change). On a fresh idle login
-- NextSpell can stay nil with nothing scheduled, so there is no suggestion
-- to encode. EnsureEngine performs exactly the init the login event path
-- performs (LOADING_SCREEN_DISABLED -> UpdateSpellsAndTalents +
-- InitRotations + EnableRotation), minus starting timers: LoadAddOn the
-- class module, InitRotations to pick the spec function, Fetch to scan the
-- bars. Everything is pcall-guarded and read-only w.r.t. gameplay: no
-- casts, no targeting, no protected calls. Without this the bridge (and
-- the companion) would sit idle until first combat with no way to
-- calibrate or verify the link in town.
function MDB.EnsureEngine ()
  local MaxDps = MaxDpsEngine();
  if not MaxDps then return; end

  if type(MaxDps.NextSpell) ~= "function" then
    -- 1. Demand-load the class module, exactly like LoadModule does.
    local ClassFile = select(2, UnitClass("player"));
    local ClassName = nil;
    if MaxDps.ClassId and MaxDps.Classes then
      local _, _, ClassId = UnitClass("player");
      if ClassId and MaxDps.Classes[ClassId] then ClassName = MaxDps.Classes[ClassId]; end
    end
    if not ClassName and ClassFile then
      ClassName = strupper(strsub(ClassFile, 1, 1)) .. strlower(strsub(ClassFile, 2));
    end
    if ClassName and type(LoadAddOn) == "function" then
      pcall(LoadAddOn, "MaxDps_" .. ClassName);
    end
    -- 2. Pick the spec rotation function (sets NextSpell, no timers).
    if type(MaxDps.InitRotations) == "function" then
      pcall(MaxDps.InitRotations, MaxDps, true);
    end
  end

  -- 3. Scan the bars so spellID -> HotKey resolution works even before
  -- MaxDps's own Fetch ran (it only runs while rotationEnabled).
  if (not MaxDps.Spells or not next(MaxDps.Spells))
    and type(MaxDps.Fetch) == "function" then
    pcall(MaxDps.Fetch, MaxDps, "MaxDpsBridge");
  end

  -- 4. Build FrameData so the retail rotation function can run read-only.
  -- Hunter:BeastMastery indexes MaxDps.FrameData.ACSpells on entry; without
  -- PrepareFrameData (normally run inside InvokeNextSpell) the pcall in
  -- GetMainSpellID would die on a nil index and look like "no suggestion".
  if (not MaxDps.FrameData or not MaxDps.FrameData.ACSpells) then
    if type(MaxDps.PrepareFrameData) == "function" then
      pcall(MaxDps.PrepareFrameData, MaxDps);
    end
    if type(MaxDps.UpdateAuraData) == "function" then
      pcall(MaxDps.UpdateAuraData, MaxDps);
    end
  end
end

--- ======= SLOT READOUT =======

function MDB.GetMainSpellID ()
  local MaxDps = MaxDpsEngine();
  if not MaxDps then return nil; end
  MDB.EnsureEngine();
  local SpellID = MaxDps.Spell;
  if type(SpellID) == "number" and SpellID ~= 0 then return SpellID; end
  -- Classic path: the class function returns the spellID directly.
  if type(MaxDps.NextSpell) == "function" then
    local Ok, Res = pcall(MaxDps.NextSpell, MaxDps);
    if Ok and type(Res) == "number" and Res ~= 0 then return Res; end
  end
  -- Calibrate-pattern readout also needs the assisted-combat answer, so
  -- run the class glow pass first (fills Flags/InterruptSet/DefensiveSet
  -- even while idle), exactly like InvokeNextSpell does minus the glow
  -- of the main spell onto the bars. Pure queries + glow overlays only.
  if type(MaxDps.NextSpell) == "function" then
    pcall(MaxDps.NextSpell, MaxDps);
  end
  -- Retail Midnight path (Core.lua:791-797): the class function only glows
  -- cooldowns/interrupts and returns nothing; the main spell comes from the
  -- assisted-combat API gated by CheckSpellUsable. Query-only, no gameplay.
  if _G.C_AssistedCombat and type(_G.C_AssistedCombat.GetNextCastSpell) == "function" then
    local Ok, Next = pcall(_G.C_AssistedCombat.GetNextCastSpell, false);
    if Ok and type(Next) == "number" and Next ~= 0 then
      if type(MaxDps.CheckSpellUsable) == "function" then
        local SpellName = nil;
        if _G.C_Spell and type(_G.C_Spell.GetSpellName) == "function" then
          local OkName, Name = pcall(_G.C_Spell.GetSpellName, Next);
          if OkName then SpellName = Name; end
        end
        local OkUse, Usable = pcall(MaxDps.CheckSpellUsable, MaxDps, Next, SpellName);
        if OkUse and Usable then return Next; end
      else
        return Next;
      end
    end
  end
  return nil;
end

-- Smallest flagged spellID in Set that is still flagged and on the bars.
-- Stale entries (Flags cleared by DestroyAllOverlays/Fetch) are pruned here.
local function FirstFlagged (Set)
  local MaxDps = MaxDpsEngine();
  local Flags = MaxDps and MaxDps.Flags;
  local Spells = MaxDps and MaxDps.Spells;
  if not Flags or not Spells then return nil; end
  local Best = nil;
  for SpellID in pairs(Set) do
    if Flags[SpellID] == true and Spells[SpellID] then
      if not Best or SpellID < Best then Best = SpellID; end
    else
      Set[SpellID] = nil;
    end
  end
  return Best;
end

function MDB.GetInterruptSpellID ()
  return FirstFlagged(InterruptSet);
end

function MDB.GetDefensiveSpellID ()
  local MaxDps = MaxDpsEngine();
  if not (MaxDps and MaxDps.db and MaxDps.db.global and MaxDps.db.global.enableDefensives) then
    return nil;
  end
  return FirstFlagged(DefensiveSet);
end

local function ItemSpellIDs ()
  local MaxDps = MaxDpsEngine();
  local Out = {};
  if MaxDps and MaxDps.ItemSpells then
    for _, ItemSpellID in pairs(MaxDps.ItemSpells) do
      if type(ItemSpellID) == "number" then Out[ItemSpellID] = true; end
    end
  end
  return Out;
end

function MDB.GetConsumableSpellID ()
  local MaxDps = MaxDpsEngine();
  local Flags = MaxDps and MaxDps.Flags;
  local Spells = MaxDps and MaxDps.Spells;
  if not Flags or not Spells then return nil; end
  local Items = ItemSpellIDs();
  local Best = nil;
  for SpellID, On in pairs(Flags) do
    if On == true and Items[SpellID] and Spells[SpellID] then
      if not Best or SpellID < Best then Best = SpellID; end
    end
  end
  return Best;
end

function MDB.GetCooldownSpellID ()
  local MaxDps = MaxDpsEngine();
  local Flags = MaxDps and MaxDps.Flags;
  local Spells = MaxDps and MaxDps.Spells;
  if not Flags or not Spells then return nil; end
  if not (MaxDps.db and MaxDps.db.global and MaxDps.db.global.enableCooldowns) then
    return nil;
  end
  local Items = ItemSpellIDs();
  local Best = nil;
  for SpellID, On in pairs(Flags) do
    if On == true and type(SpellID) == "number" and Spells[SpellID]
      and not InterruptSet[SpellID] and not DefensiveSet[SpellID]
      and not Items[SpellID] then
      if not Best or SpellID < Best then Best = SpellID; end
    end
  end
  return Best;
end

--- ======= BINDING RESOLUTION =======

-- SpellFrame.lua shortens raw bindings for display (SHIFT- -> S-,
-- NUMPAD -> N, BUTTON4 -> MB4, ...). HotKey/bindText reads come back
-- shortened, so expand back to the raw form ParseBinding expects.
function MDB.ExpandHotKey (Text)
  if type(Text) ~= "string" or Text == "" then return nil; end
  if string.byte(Text) == 226 then return nil; end  -- range glyph etc.
  local Key = strupper(strtrim(Text));

  -- Split trailing modifiers so short key names can be expanded behind them
  -- ("S-N1" -> SHIFT- + NUMPAD1). Done with plain strsub in a loop:
  -- Lua 5.1 patterns have no (?:...) non-capturing groups.
  local Prefix = "";
  local Stripped = true;
  while Stripped do
    Stripped = false;
    if strsub(Key, 1, 7) == "SHIFT-" then
      Prefix = Prefix .. "SHIFT-"; Key = strsub(Key, 8); Stripped = true;
    elseif strsub(Key, 1, 5) == "CTRL-" then
      Prefix = Prefix .. "CTRL-"; Key = strsub(Key, 6); Stripped = true;
    elseif strsub(Key, 1, 4) == "ALT-" then
      Prefix = Prefix .. "ALT-"; Key = strsub(Key, 5); Stripped = true;
    elseif strsub(Key, 1, 2) == "S-" then
      Prefix = Prefix .. "SHIFT-"; Key = strsub(Key, 3); Stripped = true;
    elseif strsub(Key, 1, 2) == "C-" then
      Prefix = Prefix .. "CTRL-"; Key = strsub(Key, 3); Stripped = true;
    elseif strsub(Key, 1, 2) == "A-" then
      Prefix = Prefix .. "ALT-"; Key = strsub(Key, 3); Stripped = true;
    end
  end
  local Base = Key;
  -- Expand ShortenKeybind tokens back to raw. Raw full names (BUTTON4,
  -- MOUSEWHEELUP, NUMPADPLUS, MIDDLE MOUSE, ...) pass through untouched.
  -- Only exact short tokens are rewritten, so substrings inside longer
  -- names can never collide.
  Base = Base:gsub("MIDDLE MOUSE", "BUTTON3");
  Base = Base:gsub("LMB", "BUTTON1");
  Base = Base:gsub("RMB", "BUTTON2");
  Base = Base:gsub("MB3", "BUTTON3");
  Base = Base:gsub("MB4", "BUTTON4");
  Base = Base:gsub("MB5", "BUTTON5");
  Base = Base:gsub("MWU", "MOUSEWHEELUP");
  Base = Base:gsub("MWD", "MOUSEWHEELDOWN");

  if Base == "M3" then
    Base = "BUTTON3";
  elseif Base == "N+" then
    Base = "NUMPADPLUS";
  elseif Base == "N-" then
    Base = "NUMPADMINUS";
  elseif Base == "N*" then
    Base = "NUMPADMULTIPLY";
  elseif Base == "N/" then
    Base = "NUMPADDIVIDE";
  elseif Base == "NDECIMAL" or Base == "N." then
    Base = "NUMPADDECIMAL";
  elseif strmatch(Base, "^N[0-9]$") then
    Base = "NUMPAD" .. strsub(Base, 2, 2);
  elseif Base == "+" then
    -- SpellFrame collapses NUMPADPLUS to "+" and bare PLUS to "+";
    -- NUMPADPLUS is the common case (Keymap has no bare-PLUS entry),
    -- so map "+" there.
    Base = "NUMPADPLUS";
  elseif Base == "-" then
    Base = "NUMPADMINUS";
  end

  return Prefix .. Base;
end

local function HotKeyBinding (SpellID)
  local MaxDps = MaxDpsEngine();
  local Buttons = MaxDps and MaxDps.Spells and MaxDps.Spells[SpellID];
  if not Buttons then return nil; end
  for i = 1, #Buttons do
    local Button = Buttons[i];
    local HotKey = Button and Button.HotKey;
    if not HotKey and Button and Button.GetName then
      local Name = Button:GetName();
      if Name then HotKey = _G[Name .. "HotKey"]; end
    end
    if HotKey and HotKey.GetText then
      local Ok, Text = pcall(HotKey.GetText, HotKey);
      if Ok and type(Text) == "string" and Text ~= "" and string.byte(Text) ~= 226 then
        local VirtualKey, Modifiers = MDB.ParseBinding(MDB.ExpandHotKey(Text));
        if VirtualKey then return VirtualKey, Modifiers; end
      end
    end
  end
  return nil;
end

-- Mirrors SpellFrame.lua FindSpellOnActionBar (slots 1-180, id or name match).
local function FindSpellOnActionBar (SpellID)
  if not SpellID then return nil; end
  local SearchName = nil;
  if C_Spell and C_Spell.GetSpellName then
    local Ok, Name = pcall(C_Spell.GetSpellName, SpellID);
    if Ok then SearchName = Name; end
  end
  for Slot = 1, 180 do
    local ActionType, ID = GetActionInfo(Slot);
    if ActionType == "spell" and ID then
      if ID == SpellID then return Slot; end
      if SearchName and C_Spell and C_Spell.GetSpellName then
        local Ok, SlotName = pcall(C_Spell.GetSpellName, ID);
        if Ok and SlotName and SlotName == SearchName then return Slot; end
      end
    end
  end
  return nil;
end

-- Mirrors SpellFrame.lua GetKeybindForSpell: slot -> binding command.
local function GetKeybindForSpell (SpellID)
  local Slot = FindSpellOnActionBar(SpellID);
  if not Slot then return nil; end
  local Button = ((Slot - 1) % 12) + 1;
  local Command = nil;
  if Slot <= 12 then
    Command = "ACTIONBUTTON" .. Button;
  elseif Slot <= 24 then
    Command = "MULTIACTIONBAR1BUTTON" .. Button;
  elseif Slot <= 36 then
    Command = "MULTIACTIONBAR3BUTTON" .. Button;
  elseif Slot <= 48 then
    Command = "MULTIACTIONBAR4BUTTON" .. Button;
  elseif Slot <= 60 then
    Command = "MULTIACTIONBAR2BUTTON" .. Button;
  elseif Slot <= 72 then
    Command = "MULTIACTIONBAR1BUTTON" .. Button;
  elseif Slot <= 156 then
    Command = "MULTIACTIONBAR5BUTTON" .. Button;
  elseif Slot <= 168 then
    Command = "MULTIACTIONBAR6BUTTON" .. Button;
  elseif Slot <= 180 then
    Command = "MULTIACTIONBAR7BUTTON" .. Button;
  end
  if not Command then return nil; end
  return GetBindingKey(Command);
end

local function SpellFrameBinding (SpellID)
  local Frame = _G.MaxDpsSpellFrame;
  if not Frame or not Frame.IsVisible or not Frame:IsVisible() then return nil; end
  if SpellID ~= MDB.GetMainSpellID() then return nil; end
  if not Frame.bindText or not Frame.bindText.GetText then return nil; end
  local Ok, Text = pcall(Frame.bindText.GetText, Frame.bindText);
  if not Ok then return nil; end
  return MDB.ParseBinding(MDB.ExpandHotKey(Text));
end

local function TextureBinding (SpellID)
  local Texture = nil;
  if C_Spell and C_Spell.GetSpellTexture then
    local Ok, Tex = pcall(C_Spell.GetSpellTexture, SpellID);
    if Ok then Texture = Tex; end
  end
  if not Texture and type(GetSpellTexture) == "function" then
    local Ok, Tex = pcall(GetSpellTexture, SpellID);
    if Ok then Texture = Tex; end
  end
  if not Texture then return nil; end
  return MDB.ParseBinding(MDB.BindingForTexture(Texture));
end

local function ResolveBindingUncached (SpellID)
  local VirtualKey, Modifiers = HotKeyBinding(SpellID);
  if VirtualKey then return VirtualKey, Modifiers; end

  VirtualKey, Modifiers = MDB.ParseBinding(GetKeybindForSpell(SpellID));
  if VirtualKey then return VirtualKey, Modifiers; end

  VirtualKey, Modifiers = SpellFrameBinding(SpellID);
  if VirtualKey then return VirtualKey, Modifiers; end

  return TextureBinding(SpellID);
end

-- Results are cached per spellID; the cache is wiped by bar updates
-- (via MDB.InvalidateBindings) and by MaxDps:Fetch/category hooks.
function MDB.ResolveBinding (SpellID)
  if type(SpellID) ~= "number" or SpellID == 0 then return nil, 0; end
  MDB._BindCache = MDB._BindCache or {};
  local Cached = MDB._BindCache[SpellID];
  if Cached then
    if Cached.Miss then return nil, 0; end
    return Cached.VK, Cached.Mods;
  end
  local VirtualKey, Modifiers = ResolveBindingUncached(SpellID);
  if VirtualKey then
    MDB._BindCache[SpellID] = { VK = VirtualKey, Mods = Modifiers or 0 };
  else
    MDB._BindCache[SpellID] = { Miss = true };
  end
  return VirtualKey, Modifiers or 0;
end
