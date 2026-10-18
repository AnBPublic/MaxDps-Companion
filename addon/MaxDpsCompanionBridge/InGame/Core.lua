--- ============================ HEADER ============================
-- MaxDpsCompanionBridge - in-game-config companion bridge (Core).
--
-- Owns the addon identity, the shared chat printer, the console ring buffer
-- and the MUTUAL-EXCLUSION guard. It is a separate addon from the legacy
-- MaxDpsBridge on purpose: it never edits, loads or shadows the legacy
-- addon. If the legacy bridge is loaded, this addon stays fully inert (no
-- pixel frame, no strip ticker, no slash command) and prints one warning.
--
-- Load order (InGame.xml): Toggles, Catalog, MajorCooldowns, Keymap, Bars,
-- MainFallback, Reader, Bridge, Core, Profiles, Overlay, Settings, Slash.
-- Every file is parsed before ADDON_LOADED fires, so Bridge.lua's bootstrap
-- can consult MDCB.IsInert() (defined below at file scope) even though this
-- file is loaded after Bridge.lua.
--
-- Read-only w.r.t. gameplay: no protected Lua, no secure templates, no
-- CastSpell/TargetUnit/InteractUnit/UseAction. The companion still reads the
-- same 43-cell pixel strip; nothing about the wire changes here.

local addonName, MDCB = ...;

MDCB = MDCB or {};
MDCB.ADDON = addonName or "MaxDpsCompanionBridge";
MDCB.VERSION = MDCB.VERSION or "3.7.1-exp";
MDCB.CODENAME = MDCB.CODENAME or "Nightblade";
MDCB.PREFIX = "MDCB";

--- Shared chat printer. All in-game modules and the slash surface route
-- through this so the message prefix is single-sourced.
local function Print (Message)
  if type(DEFAULT_CHAT_FRAME) == "table" and DEFAULT_CHAT_FRAME.AddMessage then
    DEFAULT_CHAT_FRAME:AddMessage("|cFF00D8FF" .. MDCB.PREFIX .. "|r: " .. tostring(Message));
  end
end
MDCB.Print = Print;

-- ---- console ring buffer (200 lines, Settings tab 5 + /mcb console) -----
local CONSOLE_MAX = 200;
MDCB.Console = MDCB.Console or { MAX = CONSOLE_MAX, Lines = {} };

function MDCB.Console.Add (Line)
  local C = MDCB.Console;
  C.Lines[#C.Lines + 1] = tostring(Line);
  while #C.Lines > C.MAX do table.remove(C.Lines, 1); end
  if MDCB.Settings and MDCB.Settings.NotifyConsole then
    pcall(MDCB.Settings.NotifyConsole);
  end
end

function MDCB.Console.Join ()
  local C = MDCB.Console;
  if #C.Lines == 0 then return "(console empty)"; end
  return table.concat(C.Lines, "\n");
end

function MDCB.Console.Clear ()
  MDCB.Console.Lines = {};
end

--- Shared print + console mirror for status/diagnostic output.
function MDCB.Log (Message)
  MDCB.Console.Add(Message);
  Print(Message);
end

-- ---- mutual exclusion guard ----------------------------------------------
-- C_AddOns.IsAddOnLoaded (12.x) with the legacy IsAddOnLoaded fallback.
-- Every lookup is pcall-contained; on any doubt the answer is "not loaded"
-- so this addon fails OPEN to active (the player explicitly enabled it).
function MDCB.IsAddonLoaded (Name)
  if type(Name) ~= "string" then return false; end;
  if _G.C_AddOns and type(_G.C_AddOns.IsAddOnLoaded) == "function" then
    local Ok, Value = pcall(_G.C_AddOns.IsAddOnLoaded, Name);
    if Ok then return Value == true; end;
  end;
  if type(_G.IsAddOnLoaded) == "function" then
    local Ok, Value = pcall(_G.IsAddOnLoaded, Name);
    if Ok then return Value == true; end;
  end;
  return false;
end

--- True while the stable MaxDpsBridge is loaded. Bridge.lua's bootstrap and
-- Slash.lua both consult this; once true it stays true for the session.
function MDCB.IsInert ()
  if MDCB.Inert == true then return true; end;
  if MDCB.IsAddonLoaded("MaxDpsBridge") then
    MDCB.Inert = true;
    return true;
  end;
  return false;
end

--- One-way transition to inert. Idempotent: the warning prints once and the
-- live addon (pixel frame, /mcb) is torn down on the first call only. Invoked
-- from this addon's ADDON_LOADED pass (stable already loaded) AND from the
-- "MaxDpsBridge" ADDON_LOADED branch / guard ticker below (stable loaded
-- AFTER this addon) so both orders end in the same fully-inert state.
function MDCB.EnterInert ()
  if MDCB.InertWarned then return true; end;
  MDCB.InertWarned = true;
  MDCB.Inert = true;
  if MDCB.GuardTicker then
    MDCB.GuardTicker:Cancel();
    MDCB.GuardTicker = nil;
  end;
  -- Bridge.lua is parsed before this file and owns the teardown; pcall so a
  -- missing/failed hook can never leave a half-live fork.
  if MDCB.Deactivate then pcall(MDCB.Deactivate); end;
  Print("stable MaxDpsBridge is loaded - staying inert. Disable the "
    .. "stable bridge in the AddOns list (per character) and /reload to "
    .. "use MaxDps Companion Bridge.");
  return true;
end

MDCB.Inert = false;

-- ---- DB bootstrap marker -------------------------------------------------
-- Bridge.lua seeds the runtime defaults on its ADDON_LOADED pass; this only
-- guarantees the top-level table exists so the other modules can key off it.
function MDCB.EnsureDB ()
  if type(_G.MaxDpsCompanionBridgeDB) ~= "table" then _G.MaxDpsCompanionBridgeDB = {}; end;
  return _G.MaxDpsCompanionBridgeDB;
end

local Loader = CreateFrame("Frame");
Loader:RegisterEvent("ADDON_LOADED");
Loader:SetScript("OnEvent", function (self, Event, Arg1)
  if Event ~= "ADDON_LOADED" then return; end

  -- Stable loaded AFTER this fork: transition to inert now (tears down the
  -- pixel frame/OnUpdate and the /mcb registration).
  if Arg1 == "MaxDpsBridge" then
    MDCB.EnterInert();
    return;
  end

  if Arg1 ~= MDCB.ADDON then return; end

  if MDCB.IsInert() then
    MDCB.EnterInert();
    return;
  end

  MDCB.EnsureDB();
  if MDCB.Profiles and MDCB.Profiles.Ensure then pcall(MDCB.Profiles.Ensure); end;
  if MDCB.Overlay and MDCB.Overlay.Ensure then pcall(MDCB.Overlay.Ensure); end;
  if MDCB.Settings and MDCB.Settings.Ensure then pcall(MDCB.Settings.Ensure); end;
  MDCB.Console.Add(("loaded v%s (protocol v5). /mcb help"):format(tostring(MDCB.VERSION)));
end);

-- Periodic belt-and-suspenders for load/reload orders that can miss the
-- "MaxDpsBridge" ADDON_LOADED edge. Cheap (one C_AddOns query every few
-- seconds) and self-cancels on the first inert transition.
if C_Timer and C_Timer.NewTicker then
  MDCB.GuardTicker = C_Timer.NewTicker(5, function ()
    if not MDCB.Inert and MDCB.IsAddonLoaded("MaxDpsBridge") then
      MDCB.EnterInert();
    end
  end);
end
