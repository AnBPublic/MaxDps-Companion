--- ============================ HEADER ============================
-- MaxDpsBridgeExp - experimental in-game-config fork (Core).
--
-- Owns the addon identity, the shared chat printer, the console ring buffer
-- and the MUTUAL-EXCLUSION guard. The fork is a separate addon on purpose:
-- it never edits, loads or shadows the stable MaxDpsBridge. If the stable
-- bridge is loaded, this addon stays fully inert (no pixel frame, no strip
-- ticker, no slash command) and prints one warning.
--
-- Load order (Exp.xml): Toggles, Catalog, MajorCooldowns, Keymap, Bars,
-- MainFallback, Reader, Bridge, Core, Profiles, Overlay, Settings, Slash.
-- Every file is parsed before ADDON_LOADED fires, so Bridge.lua's bootstrap
-- can consult MDBX.IsInert() (defined below at file scope) even though this
-- file is loaded after Bridge.lua.
--
-- Read-only w.r.t. gameplay: no protected Lua, no secure templates, no
-- CastSpell/TargetUnit/InteractUnit/UseAction. The companion still reads the
-- same 43-cell pixel strip; nothing about the wire changes here.

local addonName, MDBX = ...;

MDBX = MDBX or {};
MDBX.ADDON = addonName or "MaxDpsBridgeExp";
MDBX.VERSION = MDBX.VERSION or "3.7.1-exp";
MDBX.PREFIX = "MDBX";

--- Shared chat printer. All Exp modules and the slash surface route through
-- this so the message prefix is single-sourced.
local function Print (Message)
  if type(DEFAULT_CHAT_FRAME) == "table" and DEFAULT_CHAT_FRAME.AddMessage then
    DEFAULT_CHAT_FRAME:AddMessage("|cFF00D8FF" .. MDBX.PREFIX .. "|r: " .. tostring(Message));
  end
end
MDBX.Print = Print;

-- ---- console ring buffer (200 lines, Settings tab 5 + /mdbx console) -----
local CONSOLE_MAX = 200;
MDBX.Console = MDBX.Console or { MAX = CONSOLE_MAX, Lines = {} };

function MDBX.Console.Add (Line)
  local C = MDBX.Console;
  C.Lines[#C.Lines + 1] = tostring(Line);
  while #C.Lines > C.MAX do table.remove(C.Lines, 1); end
  if MDBX.Settings and MDBX.Settings.NotifyConsole then
    pcall(MDBX.Settings.NotifyConsole);
  end
end

function MDBX.Console.Join ()
  local C = MDBX.Console;
  if #C.Lines == 0 then return "(console empty)"; end
  return table.concat(C.Lines, "\n");
end

function MDBX.Console.Clear ()
  MDBX.Console.Lines = {};
end

--- Shared print + console mirror for status/diagnostic output.
function MDBX.Log (Message)
  MDBX.Console.Add(Message);
  Print(Message);
end

-- ---- mutual exclusion guard ----------------------------------------------
-- C_AddOns.IsAddOnLoaded (12.x) with the legacy IsAddOnLoaded fallback.
-- Every lookup is pcall-contained; on any doubt the answer is "not loaded"
-- so the fork fails OPEN to active (the player explicitly enabled it).
function MDBX.IsAddonLoaded (Name)
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
function MDBX.IsInert ()
  if MDBX.Inert == true then return true; end;
  if MDBX.IsAddonLoaded("MaxDpsBridge") then
    MDBX.Inert = true;
    return true;
  end;
  return false;
end

--- One-way transition to inert. Idempotent: the warning prints once and the
-- live fork (pixel frame, /mdbx) is torn down on the first call only. Invoked
-- from the Exp ADDON_LOADED pass (stable already loaded) AND from the
-- "MaxDpsBridge" ADDON_LOADED branch / guard ticker below (stable loaded
-- AFTER Exp) so both orders end in the same fully-inert state.
function MDBX.EnterInert ()
  if MDBX.InertWarned then return true; end;
  MDBX.InertWarned = true;
  MDBX.Inert = true;
  if MDBX.GuardTicker then
    MDBX.GuardTicker:Cancel();
    MDBX.GuardTicker = nil;
  end;
  -- Bridge.lua is parsed before this file and owns the teardown; pcall so a
  -- missing/failed hook can never leave a half-live fork.
  if MDBX.Deactivate then pcall(MDBX.Deactivate); end;
  Print("stable MaxDpsBridge is loaded - staying inert. Disable the "
    .. "stable bridge in the AddOns list (per character) and /reload to "
    .. "use the experimental fork.");
  return true;
end

MDBX.Inert = false;

-- ---- DB bootstrap marker -------------------------------------------------
-- Bridge.lua seeds the runtime defaults on its ADDON_LOADED pass; this only
-- guarantees the top-level table exists so the other modules can key off it.
function MDBX.EnsureDB ()
  if type(_G.MaxDpsBridgeExpDB) ~= "table" then _G.MaxDpsBridgeExpDB = {}; end;
  return _G.MaxDpsBridgeExpDB;
end

local Loader = CreateFrame("Frame");
Loader:RegisterEvent("ADDON_LOADED");
Loader:SetScript("OnEvent", function (self, Event, Arg1)
  if Event ~= "ADDON_LOADED" then return; end

  -- Stable loaded AFTER this fork: transition to inert now (tears down the
  -- pixel frame/OnUpdate and the /mdbx registration).
  if Arg1 == "MaxDpsBridge" then
    MDBX.EnterInert();
    return;
  end

  if Arg1 ~= MDBX.ADDON then return; end

  if MDBX.IsInert() then
    MDBX.EnterInert();
    return;
  end

  MDBX.EnsureDB();
  if MDBX.Profiles and MDBX.Profiles.Ensure then pcall(MDBX.Profiles.Ensure); end;
  if MDBX.Overlay and MDBX.Overlay.Ensure then pcall(MDBX.Overlay.Ensure); end;
  if MDBX.Settings and MDBX.Settings.Ensure then pcall(MDBX.Settings.Ensure); end;
  MDBX.Console.Add(("loaded v%s (protocol v5). /mdbx help"):format(tostring(MDBX.VERSION)));
end);

-- Periodic belt-and-suspenders for load/reload orders that can miss the
-- "MaxDpsBridge" ADDON_LOADED edge. Cheap (one C_AddOns query every few
-- seconds) and self-cancels on the first inert transition.
if C_Timer and C_Timer.NewTicker then
  MDBX.GuardTicker = C_Timer.NewTicker(5, function ()
    if not MDBX.Inert and MDBX.IsAddonLoaded("MaxDpsBridge") then
      MDBX.EnterInert();
    end
  end);
end
