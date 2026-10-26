--- ============================ HEADER ============================
-- MaxDpsCompanionBridge - slash surface and keybinding entry points.
--
-- Registers /mcb with /mdb and /mdbx as aliases: the legacy MaxDpsBridge that
-- owned /mdb is archived under addon/legacy and disabled, and the aliases keep
-- old habits working. Commands:
--   show|hide|toggle lock|unlock scale <n> alpha <n> reset
--   t <key> [on|off]  profile <Global|Spec|Talent>
--   export  import <string>  console  debug [on|off]  cfg  help
--
-- Every action routes through the already-guarded modules; no protected Lua,
-- no secure attributes, no gameplay automation. When the legacy bridge is
-- loaded this addon is inert and this file registers nothing.
--
-- MDCBBinding(key) is the global Bindings.xml executes for
-- MDCB_TOGGLE_OVERLAY and the 14 MDCB_TOGGLE_<KEY> actions.

local addonName, MDCB = ...;

-- ---- localization strings (Bindings.xml lookups) -------------------------
BINDING_HEADER_MDCB = "MaxDps Companion Bridge";
BINDING_NAME_MDCB_TOGGLE_OVERLAY = "Toggle overlay";
BINDING_NAME_MDCB_TOGGLE_MAIN = "Toggle Main";
BINDING_NAME_MDCB_TOGGLE_OFFENSIVE = "Toggle Offensive";
BINDING_NAME_MDCB_TOGGLE_DEFENSIVE = "Toggle Defensive";
BINDING_NAME_MDCB_TOGGLE_CONSUMABLE = "Toggle Consumable";
BINDING_NAME_MDCB_TOGGLE_TRINKET = "Toggle Trinket";
BINDING_NAME_MDCB_TOGGLE_INTERRUPT = "Toggle Interrupt";
BINDING_NAME_MDCB_TOGGLE_MOBILITY = "Toggle Mobility";
BINDING_NAME_MDCB_TOGGLE_SELFHEAL = "Toggle Self-heal";
BINDING_NAME_MDCB_TOGGLE_SOLO = "Toggle Solo";
BINDING_NAME_MDCB_TOGGLE_OOC = "Toggle Out of combat";
BINDING_NAME_MDCB_TOGGLE_AUTOTARGET = "Toggle Auto-target";
BINDING_NAME_MDCB_TOGGLE_AUTOINTERACT = "Toggle Auto-interact";
BINDING_NAME_MDCB_TOGGLE_TTK = "Toggle TTK guard";
BINDING_NAME_MDCB_TOGGLE_CC = "Toggle Crowd control";

-- ---- keybinding entry point ----------------------------------------------
local function AllKeys ()
  if MDCB.Profiles and MDCB.Profiles.ToggleKeys then return MDCB.Profiles.ToggleKeys(); end;
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
function MDCBBinding (Key)
  if type(Key) ~= "string" then return; end;
  if MDCB.IsInert and MDCB.IsInert() then return; end;
  if Key == "Overlay" then
    if MDCB.Overlay then MDCB.Overlay.Toggle(); end;
    return;
  end;
  local Canon = CanonKey(Key);
  if Canon and MDCB.Toggles and MDCB.Toggles.Flip then
    MDCB.Toggles.Flip(Canon);
    if MDCB.Overlay then MDCB.Overlay.SetDirty(); end;
  end;
end

-- ---- slash handler -------------------------------------------------------
local function Help ()
  MDCB.Print("commands:");
  MDCB.Print("  /mcb show | hide | toggle            overlay visibility");
  MDCB.Print("  /mcb lock | unlock                  drag lock");
  MDCB.Print("  /mcb scale <0.5-2> | alpha <0.2-1>   overlay geometry");
  MDCB.Print("  /mcb reset                          overlay defaults");
  MDCB.Print("  /mcb t <key> [on|off]               flip one toggle");
  MDCB.Print("  /mcb guard [on|off]                 taint guard (secret-cooldown errors)");
  MDCB.Print("  /mcb profile <Global|Spec|Talent>   apply a stored profile");
  MDCB.Print("  /mcb export | import <string>       versioned profile string");
  MDCB.Print("  /mcb cfg                            standalone config window");
  MDCB.Print("  /mcb settings                       AddOns options page");
  MDCB.Print("  /mcb console | debug [on|off]       panels");
  MDCB.Print("  /mcb <diag|status|heal|...>         bridge diagnostics");
  MDCB.Print("  /mcb help");
end

local function RuntimeHandler (Command, Arg1, Rest)
  if Command == "" or Command == "help" then
    Help();
  elseif Command == "show" then
    if MDCB.Overlay then MDCB.Overlay.Show(); end;
  elseif Command == "hide" then
    if MDCB.Overlay then MDCB.Overlay.Hide(); end;
  elseif Command == "toggle" then
    if MDCB.Overlay then MDCB.Overlay.Toggle(); end;
  elseif Command == "lock" then
    if MDCB.Overlay then MDCB.Overlay.SetLocked(true); end;
    MDCB.Print("overlay locked");
  elseif Command == "unlock" then
    if MDCB.Overlay then MDCB.Overlay.SetLocked(false); end;
    MDCB.Print("overlay unlocked");
  elseif Command == "scale" then
    if MDCB.Overlay then MDCB.Print(("scale %.2f"):format(MDCB.Overlay.SetScale(tonumber(Arg1)))); end;
  elseif Command == "alpha" then
    if MDCB.Overlay then MDCB.Print(("alpha %.2f"):format(MDCB.Overlay.SetAlpha(tonumber(Arg1)))); end;
  elseif Command == "reset" then
    if MDCB.Overlay then MDCB.Overlay.Reset(); end;
    MDCB.Print("overlay reset");
  elseif Command == "t" then
    local Key, State = Rest:match("^(%S+)%s*(%S*)$");
    if not Key then MDCB.Print("usage: /mcb t <key> [on|off]"); return; end;
    if not CanonKey(Key) then MDCB.Print("unknown toggle " .. Key); return; end;
    Key = CanonKey(Key);
    local On = (State == "on") and true or ((State == "off") and false or nil);
    if On == nil then
      MDCB.Toggles.Flip(Key);
    else
      MDCB.Toggles.Set(Key, On);
    end;
    if MDCB.Overlay then MDCB.Overlay.SetDirty(); end;
    MDCB.Print(("%s is %s"):format(MDCB.Toggles.Label(Key), MDCB.Toggles.Get(Key) and "on" or "off"));
  elseif Command == "profile" then
    local Mode = MDCB.Profiles and MDCB.Profiles.ValidMode and MDCB.Profiles.ValidMode(Arg1);
    if not Mode then MDCB.Print("usage: /mcb profile <Global|Spec|Talent>"); return; end;
    MDCB.Profiles.Apply(Mode);
    MDCB.Print("profile " .. Mode .. " applied");
  elseif Command == "export" then
    if MDCB.Profiles then MDCB.Print(MDCB.Profiles.Export()); end;
  elseif Command == "import" then
    if MDCB.Profiles then
      local Ok, Msg = MDCB.Profiles.Import(Rest);
      MDCB.Print((Ok and "imported: " or "import failed: ") .. tostring(Msg));
    end;
  elseif Command == "guard" then
    -- 4.5.1: `guard` reports the tainted-button / secret-cooldown state;
    -- `guard on|off` toggles the assisted-combat pause.
    if MDCB.SecretGuard then
      local State = (Arg1 or ""):lower();
      if State == "on" then
        MDCB.SecretGuard.SetEnabled(true);
      elseif State == "off" then
        MDCB.SecretGuard.SetEnabled(false);
      else
        MDCB.SecretGuard.Report();
      end;
    else
      MDCB.Print("taint guard not loaded");
    end;
  elseif Command == "console" then
    if MDCB.Settings then MDCB.Settings.Open("Console"); end;
  elseif Command == "debug" then
    local D = MDCB.EnsureDB();
    if Arg1 == "on" then D.debug = true; elseif Arg1 == "off" then D.debug = false; end;
    MDCB.Print("debug " .. (D.debug and "on" or "off"));
  elseif Command == "cfg" then
    -- Slice 2: /mcb cfg opens the NEW standalone window (MDCB.Config); the
    -- Settings API canvas page stays reachable via `/mcb settings`.
    if MDCB.Config and MDCB.Config.Open then
      MDCB.Config.Open();
    elseif MDCB.Settings then
      MDCB.Settings.Open("Pause");
    end
  elseif Command == "settings" or Command == "options" then
    if MDCB.Settings then MDCB.Settings.Open("Settings"); end
  elseif MDCB.HandleCommand then
    -- Bridge diagnostics (diag/status/heal/version/all/why/...) stay available.
    MDCB.HandleCommand((Command .. " " .. (Rest or "")):gsub("%s+$", ""));
  else
    Help();
  end;
end

local function Handler (Input)
  if MDCB.IsInert and MDCB.IsInert() then
    MDCB.Print("inert: stable MaxDpsBridge is loaded");
    return;
  end;
  local Trimmed = (Input or ""):gsub("^%s+", ""):gsub("%s+$", "");
  local Command, Rest = Trimmed:match("^(%S*)%s*(.*)$");
  Command = (Command or ""):lower();
  Rest = Rest or "";
  local Arg1 = Rest:match("^(%S*)") or "";
  RuntimeHandler(Command, Arg1, Rest);
end

MDCB.SlashHandler = Handler;

if not (MDCB.IsInert and MDCB.IsInert()) then
  SLASH_MDCB1 = "/mcb";
  SLASH_MDCB2 = "/mdb";    -- legacy alias (archived MaxDpsBridge owned /mdb)
  SLASH_MDCB3 = "/mdbx";   -- former exp alias
  SlashCmdList["MDCB"] = Handler;
end
