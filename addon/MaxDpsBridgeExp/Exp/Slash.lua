--- ============================ HEADER ============================
-- MaxDpsBridgeExp - slash surface and keybinding entry points.
--
-- Registers /mdbx ONLY (never /mdb; the stable bridge owns that and the two
-- addons are mutually exclusive). Commands:
--   show|hide|toggle lock|unlock scale <n> alpha <n> reset
--   t <key> [on|off]  profile <Global|Spec|Talent>
--   export  import <string>  console  debug [on|off]  cfg  help
--
-- Every action routes through the already-guarded modules; no protected Lua,
-- no secure attributes, no gameplay automation. When the stable bridge is
-- loaded the fork is inert and this file registers nothing.
--
-- MDBXBinding(key) is the global Bindings.xml executes for
-- MDBX_TOGGLE_OVERLAY and the 14 MDBX_TOGGLE_<KEY> actions.

local addonName, MDBX = ...;

-- ---- localization strings (Bindings.xml lookups) -------------------------
BINDING_HEADER_MDBX = "MaxDps Bridge Exp";
BINDING_NAME_MDBX_TOGGLE_OVERLAY = "Toggle overlay";
BINDING_NAME_MDBX_TOGGLE_MAIN = "Toggle Main";
BINDING_NAME_MDBX_TOGGLE_OFFENSIVE = "Toggle Offensive";
BINDING_NAME_MDBX_TOGGLE_DEFENSIVE = "Toggle Defensive";
BINDING_NAME_MDBX_TOGGLE_CONSUMABLE = "Toggle Consumable";
BINDING_NAME_MDBX_TOGGLE_TRINKET = "Toggle Trinket";
BINDING_NAME_MDBX_TOGGLE_INTERRUPT = "Toggle Interrupt";
BINDING_NAME_MDBX_TOGGLE_MOBILITY = "Toggle Mobility";
BINDING_NAME_MDBX_TOGGLE_SELFHEAL = "Toggle Self-heal";
BINDING_NAME_MDBX_TOGGLE_SOLO = "Toggle Solo";
BINDING_NAME_MDBX_TOGGLE_OOC = "Toggle Out of combat";
BINDING_NAME_MDBX_TOGGLE_AUTOTARGET = "Toggle Auto-target";
BINDING_NAME_MDBX_TOGGLE_AUTOINTERACT = "Toggle Auto-interact";
BINDING_NAME_MDBX_TOGGLE_TTK = "Toggle TTK guard";
BINDING_NAME_MDBX_TOGGLE_CC = "Toggle Crowd control";

-- ---- keybinding entry point ----------------------------------------------
local function AllKeys ()
  if MDBX.Profiles and MDBX.Profiles.ToggleKeys then return MDBX.Profiles.ToggleKeys(); end;
  return {};
end

local function CanonKey (Key)
  if type(Key) ~= "string" then return nil; end;
  local Keys = AllKeys();
  for i = 1, #Keys do
    if Keys[i]:lower() == Key:lower() then return Keys[i]; end;
  end;
  return nil;
end

--- Global executed by Bindings.xml. "Overlay" toggles the frame; every other
-- key flips the matching in-game toggle (insecure, overlay-wins).
function MDBXBinding (Key)
  if type(Key) ~= "string" then return; end;
  if MDBX.IsInert and MDBX.IsInert() then return; end;
  if Key == "Overlay" then
    if MDBX.Overlay then MDBX.Overlay.Toggle(); end;
    return;
  end;
  local Canon = CanonKey(Key);
  if Canon and MDBX.Toggles and MDBX.Toggles.Flip then
    MDBX.Toggles.Flip(Canon);
    if MDBX.Overlay then MDBX.Overlay.SetDirty(); end;
  end;
end

-- ---- slash handler -------------------------------------------------------
local function Help ()
  MDBX.Print("commands:");
  MDBX.Print("  /mdbx show | hide | toggle            overlay visibility");
  MDBX.Print("  /mdbx lock | unlock                  drag lock");
  MDBX.Print("  /mdbx scale <0.5-2> | alpha <0.2-1>   overlay geometry");
  MDBX.Print("  /mdbx reset                          overlay defaults");
  MDBX.Print("  /mdbx t <key> [on|off]               flip one toggle");
  MDBX.Print("  /mdbx profile <Global|Spec|Talent>   apply a stored profile");
  MDBX.Print("  /mdbx export | import <string>       versioned profile string");
  MDBX.Print("  /mdbx cfg                            standalone config window");
  MDBX.Print("  /mdbx settings                       AddOns options page");
  MDBX.Print("  /mdbx console | debug [on|off]       panels");
  MDBX.Print("  /mdbx <diag|status|heal|...>         bridge diagnostics");
  MDBX.Print("  /mdbx help");
end

local function RuntimeHandler (Command, Arg1, Rest)
  if Command == "" or Command == "help" then
    Help();
  elseif Command == "show" then
    if MDBX.Overlay then MDBX.Overlay.Show(); end;
  elseif Command == "hide" then
    if MDBX.Overlay then MDBX.Overlay.Hide(); end;
  elseif Command == "toggle" then
    if MDBX.Overlay then MDBX.Overlay.Toggle(); end;
  elseif Command == "lock" then
    if MDBX.Overlay then MDBX.Overlay.SetLocked(true); end;
    MDBX.Print("overlay locked");
  elseif Command == "unlock" then
    if MDBX.Overlay then MDBX.Overlay.SetLocked(false); end;
    MDBX.Print("overlay unlocked");
  elseif Command == "scale" then
    if MDBX.Overlay then MDBX.Print(("scale %.2f"):format(MDBX.Overlay.SetScale(tonumber(Arg1)))); end;
  elseif Command == "alpha" then
    if MDBX.Overlay then MDBX.Print(("alpha %.2f"):format(MDBX.Overlay.SetAlpha(tonumber(Arg1)))); end;
  elseif Command == "reset" then
    if MDBX.Overlay then MDBX.Overlay.Reset(); end;
    MDBX.Print("overlay reset");
  elseif Command == "t" then
    local Key, State = Rest:match("^(%S+)%s*(%S*)$");
    if not Key then MDBX.Print("usage: /mdbx t <key> [on|off]"); return; end;
    if not CanonKey(Key) then MDBX.Print("unknown toggle " .. Key); return; end;
    Key = CanonKey(Key);
    local On = (State == "on") and true or ((State == "off") and false or nil);
    if On == nil then
      MDBX.Toggles.Flip(Key);
    else
      MDBX.Toggles.Set(Key, On);
    end;
    if MDBX.Overlay then MDBX.Overlay.SetDirty(); end;
    MDBX.Print(("%s is %s"):format(MDBX.Toggles.Label(Key), MDBX.Toggles.Get(Key) and "on" or "off"));
  elseif Command == "profile" then
    local Mode = MDBX.Profiles and MDBX.Profiles.ValidMode and MDBX.Profiles.ValidMode(Arg1);
    if not Mode then MDBX.Print("usage: /mdbx profile <Global|Spec|Talent>"); return; end;
    MDBX.Profiles.Apply(Mode);
    MDBX.Print("profile " .. Mode .. " applied");
  elseif Command == "export" then
    if MDBX.Profiles then MDBX.Print(MDBX.Profiles.Export()); end;
  elseif Command == "import" then
    if MDBX.Profiles then
      local Ok, Msg = MDBX.Profiles.Import(Rest);
      MDBX.Print((Ok and "imported: " or "import failed: ") .. tostring(Msg));
    end;
  elseif Command == "console" then
    if MDBX.Settings then MDBX.Settings.Open("Console"); end;
  elseif Command == "debug" then
    local D = MDBX.EnsureDB();
    if Arg1 == "on" then D.debug = true; elseif Arg1 == "off" then D.debug = false; end;
    MDBX.Print("debug " .. (D.debug and "on" or "off"));
  elseif Command == "cfg" then
    -- Slice 2: /mdbx cfg opens the NEW standalone window (MDBX.Config); the
    -- Settings API canvas page stays reachable via `/mdbx settings`.
    if MDBX.Config and MDBX.Config.Open then
      MDBX.Config.Open();
    elseif MDBX.Settings then
      MDBX.Settings.Open("Pause");
    end
  elseif Command == "settings" or Command == "options" then
    if MDBX.Settings then MDBX.Settings.Open("Settings"); end
  elseif MDBX.HandleCommand then
    -- Bridge diagnostics (diag/status/heal/version/all/why/...) stay available.
    MDBX.HandleCommand((Command .. " " .. (Rest or "")):gsub("%s+$", ""));
  else
    Help();
  end;
end

local function Handler (Input)
  if MDBX.IsInert and MDBX.IsInert() then
    MDBX.Print("inert: stable MaxDpsBridge is loaded");
    return;
  end;
  local Trimmed = (Input or ""):gsub("^%s+", ""):gsub("%s+$", "");
  local Command, Rest = Trimmed:match("^(%S*)%s*(.*)$");
  Command = (Command or ""):lower();
  Rest = Rest or "";
  local Arg1 = Rest:match("^(%S*)") or "";
  RuntimeHandler(Command, Arg1, Rest);
end

MDBX.SlashHandler = Handler;

if not (MDBX.IsInert and MDBX.IsInert()) then
  SLASH_MDBX1 = "/mdbx";
  SlashCmdList["MDBX"] = Handler;
end
