--- ============================ HEADER ============================
-- SecretGuard (bridge 4.5.2) - stops the "Secret values are only allowed
-- during untainted execution" flood that Blizzard's assisted-combat frame
-- produces when a UI addon has tainted an action button.
--
-- 12.x Single-Button Assistant attaches an ActionBarButtonAssistedCombat
-- RotationFrame to the action button that shows the assisted rotation. Its
-- OnUpdate calls the parent's OnActionBarSlotChanged -> Update ->
-- ActionButton_UpdateCooldown and UpdatePressAndHoldAction at
-- assistedCombatIconUpdateRate (MaxDps sets 0.15 s). If that parent button
-- has been tainted by a bar addon, every tick throws from Blizzard's own
-- code:
--   ActionButton.lua:847  SetCooldown(... secret start/duration ...)
--   ActionButton.lua:521  SetAttribute (ADDON_ACTION_BLOCKED)
-- The observed session had 3619 copies of the first error, one per 0.15 s
-- assisted-frame tick, on OverrideActionBarButton2.
--
-- WoW does not run OnUpdate on hidden frames, so hiding the assisted frame
-- stops the tick and the flood. The C_AssistedCombat API is untouched:
-- MaxDps still gets the next spell and the companion still presses it.
--
-- Policy (check `/mcb guard`):
--   * secret cooldowns + an OVERRIDE/VEHICLE/EXTRA button -> hide the frame;
--   * secret cooldowns + a button that issecurevariable names as tainted ->
--     hide the frame and log the taint source;
--   * clean main/multi-bar buttons are left completely alone, so the normal
--     Single-Button Assistant keeps working outside the failing path.
-- `/mcb guard off` disables the guard; it is on by default. No wire change,
-- no protected calls, no writes to the action buttons themselves. Every
-- frame Hide is pcall-contained.
--
-- The guard is deliberately independent of the companion process: it keys on
-- the actual secret-cooldown predicate, not on whether the exe is sampling.

local addonName, MDCB = ...;

MDCB.SecretGuard = MDCB.SecretGuard or {};
local Guard = MDCB.SecretGuard;

local type, pcall, tostring, ipairs, pairs = type, pcall, tostring, ipairs, pairs;

-- Button families that can carry an AssistedCombatRotationFrame. Override +
-- Extra first: these are the vehicle/delve buttons the live failure hit.
local BUTTONS = {
  { Prefix = "OverrideActionBarButton", Count = 6, Override = true },
  { Prefix = "ExtraActionButton", Count = 1, Override = true },
  { Prefix = "ActionButton", Count = 12 },
  { Prefix = "MultiBarBottomLeftButton", Count = 12 },
  { Prefix = "MultiBarBottomRightButton", Count = 12 },
  { Prefix = "MultiBarRightButton", Count = 12 },
  { Prefix = "MultiBarLeftButton", Count = 12 },
  { Prefix = "MultiBar5Button", Count = 12 },
  { Prefix = "MultiBar6Button", Count = 12 },
  { Prefix = "MultiBar7Button", Count = 12 },
};

-- Frames this guard hid. Cleared (without forcing a Show) when secrets end or
-- the guard is turned off; Blizzard's next UpdateState owns the restore.
local Hidden = {};

-- 4.5.6: the guard is deliberately SILENT during normal play. Automatic
-- "[taint]" chat lines from 4.5.1 are gone (they were noise and a potential
-- hitch in combat). Only an explicit `/mcb guard` report speaks.
local function Echo (Message)
  if type(MDCB.Print) == "function" then
    MDCB.Print(Message);
    return;
  end;
  if DEFAULT_CHAT_FRAME and DEFAULT_CHAT_FRAME.AddMessage then
    DEFAULT_CHAT_FRAME:AddMessage("|cFF00D8FFMDCB|r: " .. Message);
  end;
end

local function IsInert ()
  return type(MDCB.IsInert) == "function" and MDCB.IsInert() == true;
end

local function Enabled ()
  local DB = type(MDCB.EnsureDB) == "function" and MDCB.EnsureDB() or nil;
  return not (DB and DB.TaintGuard == false);
end

--- True when the client is currently allowed to hand addons secret cooldown
--- values. `ShouldCooldownsBeSecret` is the exact 12.x predicate; the
--- action-cooldown probe is a fallback for a client/build where the predicate
--- is missing but the values are still secret.
local function SecretCooldowns ()
  if type(C_Secrets) == "table" and type(C_Secrets.ShouldCooldownsBeSecret) == "function" then
    local Ok, Secret = pcall(C_Secrets.ShouldCooldownsBeSecret);
    if Ok and Secret ~= nil then
      local OkTrue, IsTrue = pcall(function () return Secret == true; end);
      if OkTrue and IsTrue then return true; end;
    end;
  end;

  if type(issecretvalue) == "function"
    and type(C_ActionBar) == "table"
    and type(C_ActionBar.GetActionCooldown) == "function" then
    for Slot = 1, 12 do
      local Ok, Info = pcall(C_ActionBar.GetActionCooldown, Slot);
      if Ok and type(Info) == "table" then
        -- The result is normally a plain table with secret fields; indexing a
        -- fully secret table would throw, so keep the field read contained.
        local OkStart, Start = pcall(function () return Info.startTime; end);
        if OkStart and Start ~= nil then
          local OkS, Secret = pcall(issecretvalue, Start);
          if OkS and Secret == true then return true; end;
        end;
      end;
    end;
  end;

  return false;
end

--- issecurevariable(button, field) returns (secure, taintSource). A false
--- with a source names the addon that tainted that field. We only trust a
--- false that names a real source: some Blizzard frames report false for
--- fields that are not variables at all, and hiding on that would be wrong.
local TAINT_KEYS = {
  "AssistedCombatRotationFrame", "cooldown", "action", "spellID",
  -- Script fields catch the common bar-addon taint paths (GW2_UI does
  -- btn:SetScript("OnUpdate", nil) on every Blizzard bar button).
  "OnUpdate", "OnEvent", "OnShow", "OnHide",
};
local function TaintOf (Button)
  if type(issecurevariable) ~= "function" then return false, nil; end;
  for _, Key in ipairs(TAINT_KEYS) do
    local Ok, Secure, Source = pcall(issecurevariable, Button, Key);
    if Ok and Secure == false and type(Source) == "string" and Source ~= "" then
      return true, Source;
    end;
  end;
  return false, nil;
end

local function HasAssistedFrame (Button)
  if type(Button) ~= "table" then return nil; end;
  local Frame = Button.AssistedCombatRotationFrame;
  if type(Frame) ~= "table" or type(Frame.Hide) ~= "function" then return nil; end;
  return Frame;
end

--- Hiding a protected frame in combat is a blocked protected call, so never
--- attempt it. The assisted-combat frame is normally a plain child frame,
--- but a bar addon can re-template it.
local function CanHide (Frame)
  if type(Frame.IsProtected) == "function" then
    local Ok, Protected = pcall(Frame.IsProtected, Frame);
    if Ok and Protected == true then return false; end;
  end;
  return true;
end

--- Pure policy seam: pause only while cooldowns are secret AND the parent is
--- an override/vehicle button or the client named a taint source for it.
function Guard.ShouldPause (Secret, Override, Tainted)
  return Secret == true and (Override == true or Tainted == true);
end

--- One sweep. Safe to call from any event/ticker; every WoW call is pcall'd.
--- SILENT: this is the 4.5.6 contract. Only `/mcb guard` prints.
function Guard.Scan ()
  if IsInert() then return; end;

  if not Enabled() then
    for Frame in pairs(Hidden) do Hidden[Frame] = nil; end;
    return;
  end;

  local Secret = SecretCooldowns();
  if not Secret then
    -- Do not force-show: Blizzard re-shows the frame through its own
    -- UpdateState on the next action/slot update, which keeps the icon
    -- truthful even if the assisted action changed while it was hidden.
    for Frame in pairs(Hidden) do Hidden[Frame] = nil; end;
    return;
  end;

  for _, Spec in ipairs(BUTTONS) do
    for Index = 1, Spec.Count do
      local Button = _G[Spec.Prefix .. Index];
      local Frame = HasAssistedFrame(Button);
      if Frame then
        local Tainted = TaintOf(Button);
        if Guard.ShouldPause(Secret, Spec.Override, Tainted) then
          if Frame:IsShown() and CanHide(Frame) then
            local Ok = pcall(Frame.Hide, Frame);
            if Ok then Hidden[Frame] = true; end;
          end;
        end;
      end;
    end;
  end;
end

--- Compact status for /mcb diag and the Debug console.
function Guard.Status ()
  local Count = 0;
  for _ in pairs(Hidden) do Count = Count + 1; end;
  return ("secret=%s paused=%d guard=%s"):format(
    SecretCooldowns() and "yes" or "no", Count, Enabled() and "on" or "off");
end

--- Full `/mcb guard` report: names every assisted frame, its parent button,
--- the taint source when the client reports one, and the known bar addons.
function Guard.Report ()
  Echo("taint guard: " .. Guard.Status());
  local Rate = nil;
  if type(GetCVar) == "function" then
    local Ok, Value = pcall(GetCVar, "assistedCombatIconUpdateRate");
    if Ok then Rate = Value; end;
  end;
  Echo("assistedCombatIconUpdateRate: " .. tostring(Rate or "?"));

  local Seen = 0;
  for _, Spec in ipairs(BUTTONS) do
    for Index = 1, Spec.Count do
      local Name = Spec.Prefix .. Index;
      local Button = _G[Name];
      local Frame = HasAssistedFrame(Button);
      if Frame then
        Seen = Seen + 1;
        local Tainted, Source = TaintOf(Button);
        Echo(("  %s: shown=%s%s%s"):format(
          Name,
          Frame:IsShown() and "yes" or "no",
          Tainted and " tainted" or "",
          Source and (" by " .. Source) or ""));
      end;
    end;
  end;
  if Seen == 0 then Echo("  no assisted-combat frames on the standard bars"); end;

  local Known = {
    "GW2_UI", "ElvUI", "EllesmereUIActionBars", "MoveAny",
    "BetterBlizzFrames", "TalentTreeTweaks", "Bartender4", "Dominos",
  };
  local Loaded = {};
  for _, Name in ipairs(Known) do
    if type(IsAddOnLoaded) == "function" then
      local Ok, IsLoaded = pcall(IsAddOnLoaded, Name);
      if Ok and IsLoaded == true then Loaded[#Loaded + 1] = Name; end;
    end;
  end;
  Echo("bar addons loaded: " .. (#Loaded > 0 and table.concat(Loaded, ", ") or "none"));
  Echo("if the game still logs SetCooldown/secret errors, update the tainted bar addon or /mcb guard off.");
end

function Guard.SetEnabled (On)
  local DB = type(MDCB.EnsureDB) == "function" and MDCB.EnsureDB() or nil;
  if not DB then
    Echo("taint guard: database not ready - try again after login");
    return false;
  end;
  DB.TaintGuard = (On ~= false);
  if not DB.TaintGuard then
    for Frame in pairs(Hidden) do Hidden[Frame] = nil; end;
  end;
  Echo("[taint] guard " .. (DB.TaintGuard and "on" or "off"));
  return DB.TaintGuard;
end

-- Event + ticker plumbing. The ticker is cheap (one pass over the standard
-- button names, no frame enumeration) and catches an assisted frame that
-- Blizzard re-shows after an action/slot change.
local Listener = CreateFrame("Frame");
for _, Event in ipairs({
  "PLAYER_ENTERING_WORLD",
  "PLAYER_REGEN_ENABLED",
  "PLAYER_REGEN_DISABLED",
  "UPDATE_OVERRIDE_ACTIONBAR",
  "UPDATE_BONUS_ACTIONBAR",
  "UPDATE_VEHICLE_ACTIONBAR",
  "ACTIONBAR_PAGE_CHANGED",
  "PLAYER_SPECIALIZATION_CHANGED",
}) do
  Listener:RegisterEvent(Event);
end;
Listener:SetScript("OnEvent", function ()
  pcall(Guard.Scan);
end);

if C_Timer and C_Timer.NewTicker then
  MDCB.SecretGuardTicker = C_Timer.NewTicker(0.5, function ()
    pcall(Guard.Scan);
  end);
end
