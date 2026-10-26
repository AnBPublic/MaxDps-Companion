--- ============================ HEADER ============================
-- SecretGuard (bridge 4.5.4) - stops the "Secret values are only allowed
-- during untainted execution" flood that Blizzard's own action-bar code raises
-- when a UI addon has tainted an action button while 12.x cooldowns are secret.
--
-- Two paths reach the same throw:
--
--   1. Assisted-combat frame OnUpdate (12.x Single-Button Assistant). The
--      ActionBarButtonAssistedCombatRotationFrame attached to the button calls
--      the parent's OnActionBarSlotChanged -> Update ->
--      ActionButton_UpdateCooldown at assistedCombatIconUpdateRate (MaxDps sets
--      0.15 s). Observed live: 3619 copies, one per assisted-frame tick.
--   2. The button's own SPELL_UPDATE_ICON handler. Blizzard_ActionBar/
--      Shared/ActionButton.lua:1026 OnEvent -> :575 ActionBarActionButtonMixin
--      Update -> :842 ActionButton_UpdateCooldown -> :847
--      cooldown:SetCooldown(start, duration, modRate). When the button is
--      tainted, the client refuses to hand the cooldown frame secret
--      start/duration/modRate and throws before the swipe is drawn. Observed
--      live: 3686 copies from MultiBarBottomRightButton2Cooldown.
--
-- Hiding the assisted frame alone cannot stop path 2: the button's own event
-- handler runs whether or not the assisted frame is shown. 4.5.7 therefore
-- adds a cooldown shim on the frames the client already reports as tainted:
--
--   * the normal cooldown, the charge cooldown and the loss-of-control
--     cooldown of a button are wrapped (Original -> GuardedSetCooldown);
--   * the wrapper calls the real SetCooldown unchanged for plain arguments and
--     only sanitises while the guard is active and at least one of the first
--     three arguments is secret: it then calls cooldown:Clear() (or
--     SetCooldown(0, 0) when Clear is missing) instead of ever passing a
--     secret value into Blizzard's frame code;
--   * clean buttons are never wrapped, so the normal Single-Button Assistant
--     and Blizzard's cooldown sweep keep working outside the failing path.
--
-- Taint is decided once per cache generation: issecurevariable(button, field)
-- for AssistedCombatRotationFrame/cooldown/action/spellID and the script
-- fields, plus up to three parent frames (SetScript(..., nil) may leave no Lua
-- field behind, and GW2_UI parents the bars on an addon-created frame, so the
-- ancestor walk also asks issecurevariable(_G, ancestorName)). 4.5.8 adds the
-- GW2_UI `gw_` marker scan (keys only - a marker value can be secret) because
-- GW2_UI styles the main bar IN PLACE: ActionButton1 keeps Blizzard's
-- MainActionBar parent and its script field can look clean. A source that is
-- this addon is ignored - our own writes must never trigger the shims. The
-- cache is dropped on a secret flip, on ADDON_LOADED and on every registered
-- bar event, never per tick; clean verdicts also expire after 10 s so a late
-- `gw_` marker is picked up. If one button in a family is tainted, the whole
-- family is shimmed (GW2_UI styles a bar in one pass); a clean family is left
-- alone.
--
-- Policy (check `/mcb guard`):
--   * secret cooldowns + an OVERRIDE/VEHICLE/EXTRA button -> shim + hide;
--   * secret cooldowns + a button the client names as tainted (button or
--     ancestor, or a GW2_UI `gw_` marker) -> shim + hide, and the taint source
--     is reported;
--   * if one button in a family is tainted, the whole family is shimmed;
--   * a clean family is left completely alone.
-- `/mcb guard off` disables the guard and leaves the wrappers in place, where
-- they pass every call straight through so the stock behavior (including the
-- original error) returns. No wire change, no protected calls, no writes to
-- protected frames, and no third-party addon is patched. Every probe and every
-- sanitised fallback (Clear/SetCooldown/Hide/GetParent/GetName/issecurevariable)
-- is pcall-contained; a plain-argument pass-through calls the real method
-- directly.
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

-- Module state (4.5.7). Mirrors Enabled(); refreshed by Scan and SetEnabled.
Guard.Active = false;
-- Button -> true: the taint/shim decision for this cache generation is made.
local Checked = setmetatable({}, { __mode = "k" });
-- Button -> GetTime() of the last clean decision. GW2_UI may add its gw_
-- markers after a clean scan, so a clean verdict expires; a tainted/shimmed
-- verdict stays until the existing invalidation events.
local CleanAt = setmetatable({}, { __mode = "k" });
-- Frame -> { Tainted, Source, Done, AncestorTainted, AncestorSource }: the
-- memoised issecurevariable verdict (own fields and, per button, the 3-level
-- ancestor walk), so the 0.5 s ticker never re-asks the client.
local TaintMemo = setmetatable({}, { __mode = "k" });
-- Cooldown -> original SetCooldown. Never cleared: once wrapped, a frame keeps
-- pointing at GuardedSetCooldown and passes through while the guard is off.
local Original = setmetatable({}, { __mode = "k" });
-- Last SecretCooldowns() answer, to detect a secret flip.
local LastSecret;

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

--- A taint source only counts when it names somebody else. Our own writes (and
--- the client's empty/unknown source) must never trigger a shim.
local function ThirdParty (Source)
  return type(Source) == "string" and Source ~= "" and Source ~= addonName;
end

--- issecurevariable(button, field) returns (secure, taintSource). A false with
--- a third-party source names the addon that tainted that field. We only trust
--- a false that names a real source: some Blizzard frames report false for
--- fields that are not variables at all, and wrapping on that would be wrong.
local TAINT_KEYS = {
  "AssistedCombatRotationFrame", "cooldown", "action", "spellID",
  -- Script fields catch the common bar-addon taint paths (GW2_UI does
  -- btn:SetScript("OnUpdate", nil) on every Blizzard bar button).
  "OnUpdate", "OnEvent", "OnShow", "OnHide",
};

local function MemoOf (Frame)
  local Entry = TaintMemo[Frame];
  if not Entry then
    Entry = { Tainted = false, Source = nil };
    TaintMemo[Frame] = Entry;
  end;
  return Entry;
end

--- GW2_UI (and the Gw2 family) mark their frames with `gw_` keys. The main
--- action bar is styled IN PLACE, so ActionButton1 keeps Blizzard's
--- MainActionBar parent and its script field can come back clean from
--- issecurevariable even though GW2_UI replaced its OnUpdate handler. Iterate
--- KEYS only - a gw_ value can be secret and even a nil check may throw in
--- insecure code.
local function HasThirdPartyMarker (Frame)
  if type(Frame) ~= "table" then return false; end;
  local Ok, Found = pcall(function ()
    for Key in next, Frame do
      if type(Key) == "string" and Key:sub(1, 3) == "gw_" then return true; end;
    end;
    return false;
  end);
  return Ok and Found == true;
end

local function TaintOf (Frame)
  if type(Frame) ~= "table" then return false, nil; end;
  local Cached = TaintMemo[Frame];
  if Cached and Cached.Done then return Cached.Tainted, Cached.Source; end;
  local Entry = MemoOf(Frame);
  Entry.Done = true;
  if type(issecurevariable) == "function" then
    for _, Key in ipairs(TAINT_KEYS) do
      local Ok, Secure, Source = pcall(issecurevariable, Frame, Key);
      if Ok and Secure == false and ThirdParty(Source) then
        Entry.Tainted, Entry.Source = true, Source;
        return true, Source;
      end;
    end;
  end;
  if HasThirdPartyMarker(Frame) then
    Entry.Tainted, Entry.Source = true, "GW2_UI";
    return true, "GW2_UI";
  end;
  return false, nil;
end

--- Taint inherited from a bar addon that re-parents or re-scripts the button.
--- SetScript(..., nil) may leave no Lua field behind, so the script-field scan
--- above can come back clean; the addon-created parent frame (GW2_UI's
--- fmMultibar) is the reliable signal. Walks up to three parents, asking both
--- issecurevariable(parent, field) and issecurevariable(_G, parentName).
--- Memoised per button for the same cache generation as TaintOf.
local function AncestorTaint (Button)
  if type(Button) ~= "table" or type(Button.GetParent) ~= "function" then return false, nil; end;
  if type(issecurevariable) ~= "function" then return false, nil; end;
  local Entry = MemoOf(Button);
  if Entry.AncestorDone then return Entry.AncestorTainted == true, Entry.AncestorSource; end;

  local Tainted, Source = false, nil;
  local Frame = Button;
  for _ = 1, 3 do
    local OkParent, Parent = pcall(Frame.GetParent, Frame);
    if not OkParent or type(Parent) ~= "table" then break; end;
    Frame = Parent;
    local ParentTainted, ParentSource = TaintOf(Frame);
    if ParentTainted then
      Tainted, Source = true, ParentSource;
      break;
    end;
    if type(Frame.GetName) == "function" then
      local OkName, Name = pcall(Frame.GetName, Frame);
      if OkName and type(Name) == "string" and Name ~= "" then
        local OkSecure, Secure, TaintSource = pcall(issecurevariable, _G, Name);
        if OkSecure and Secure == false and ThirdParty(TaintSource) then
          Tainted, Source = true, TaintSource;
          break;
        end;
      end;
    end;
  end;

  Entry.AncestorDone = true;
  Entry.AncestorTainted = Tainted;
  Entry.AncestorSource = Source;
  return Tainted, Source;
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

--- Pure policy seam for the assisted frame: pause only while cooldowns are
--- secret AND the parent is an override/vehicle button or the client named a
--- taint source for it.
function Guard.ShouldPause (Secret, Override, Tainted)
  return Secret == true and (Override == true or Tainted == true);
end

--- Pure policy seam for the cooldown shim: identical rule, named separately so
--- the two paths can diverge without touching the hide policy.
function Guard.ShouldShim (Secret, Override, Tainted)
  return Secret == true and (Override == true or Tainted == true);
end

-- ======================= cooldown shim (4.5.7) =======================

local COOLDOWN_KEYS = { "cooldown", "chargeCooldown", "lossOfControlCooldown" };

--- Drop every memoised taint decision. Original is deliberately kept: the
--- wrappers stay installed and pass through while the guard is off or clean.
function Guard.InvalidateCache ()
  for Button in pairs(Checked) do Checked[Button] = nil; end;
  for Button in pairs(CleanAt) do CleanAt[Button] = nil; end;
  for Frame in pairs(TaintMemo) do TaintMemo[Frame] = nil; end;
end

local function HasShim (Button)
  if type(Button) ~= "table" then return false; end;
  for _, Key in ipairs(COOLDOWN_KEYS) do
    local Cooldown = Button[Key];
    if type(Cooldown) == "table" and Original[Cooldown] ~= nil then return true; end;
  end;
  return false;
end

local function SecretArg (Value)
  local Ok, IsSecret = pcall(issecretvalue, Value);
  if not Ok then return false; end;
  local OkTrue, Secret = pcall(function () return IsSecret == true; end);
  return OkTrue and Secret == true;
end

function Guard.ShimCount ()
  local Count = 0;
  for _ in pairs(Original) do Count = Count + 1; end;
  return Count;
end

--- Wrapper installed on a tainted button's cooldown frames. A secret
--- start/duration/modRate never reaches Blizzard's cooldown:SetCooldown
--- (ActionButton.lua:847) while the guard is active: the cooldown is cleared
--- instead of throwing. Plain calls pass through untouched, and no error is
--- ever propagated back into Blizzard's UpdateCooldown.
function Guard.GuardedSetCooldown (self, ...)
  if self == nil then return; end;
  local Function = Original[self];
  if Guard.Active and type(issecretvalue) == "function" then
    local Start, Duration, ModRate = ...;
    if SecretArg(Start) or SecretArg(Duration) or SecretArg(ModRate) then
      if type(self.Clear) == "function" then
        if pcall(self.Clear, self) then return; end;
      end;
      if Function then pcall(Function, self, 0, 0); end;
      return;
    end;
  end;
  if Function == nil then return; end;
  -- Transparent pass-through: `/mcb guard off` must restore stock behavior,
  -- including the original error, not hide it behind a pcall.
  return Function(self, ...);
end

--- Wrap the three cooldown frames a button can own. Idempotent: a frame is
--- wrapped once per session (Original is the guard), and a wrapper somebody
--- else installed after us is left alone. Returns the number wrapped now.
function Guard.InstallCooldownShim (Button)
  if type(Button) ~= "table" then return 0; end;
  local Installed = 0;
  for _, Key in ipairs(COOLDOWN_KEYS) do
    local Cooldown = Button[Key];
    if type(Cooldown) == "table" and type(Cooldown.SetCooldown) == "function" then
      if Original[Cooldown] == nil and Cooldown.SetCooldown ~= Guard.GuardedSetCooldown then
        Original[Cooldown] = Cooldown.SetCooldown;
        local Ok = pcall(function () Cooldown.SetCooldown = Guard.GuardedSetCooldown; end);
        if Ok then
          Installed = Installed + 1;
        else
          Original[Cooldown] = nil;
        end;
      end;
    end;
  end;
  return Installed;
end

--- One sweep. Safe to call from any event/ticker; every WoW call is pcall'd.
--- SILENT: this is the 4.5.6 contract. Only `/mcb guard` prints.
function Guard.Scan ()
  if IsInert() then return; end;

  Guard.Active = Enabled();
  if not Guard.Active then
    for Frame in pairs(Hidden) do Hidden[Frame] = nil; end;
    return;
  end;

  local Secret = SecretCooldowns();
  if Secret ~= LastSecret then
    -- A secret flip is the only thing that changes the answer for buttons we
    -- already judged; this is where the taint cache is rebuilt.
    Guard.InvalidateCache();
    LastSecret = Secret;
  end;

  if not Secret then
    -- Do not force-show: Blizzard re-shows the frame through its own
    -- UpdateState on the next action/slot update, which keeps the icon
    -- truthful even if the assisted action changed while it was hidden.
    -- The wrappers stay installed and pass plain (and secret) args through.
    for Frame in pairs(Hidden) do Hidden[Frame] = nil; end;
    return;
  end;

  -- A clean verdict is only a snapshot: GW2_UI may add its gw_ markers after
  -- the scan that judged the button clean. Expire clean decisions after 10 s;
  -- tainted/shimmed decisions stay cached until an invalidation event.
  local Now = type(GetTime) == "function" and GetTime() or 0;
  for _, At in pairs(CleanAt) do
    if Now - At >= 10 then
      Guard.InvalidateCache();
      break;
    end;
  end;

  for _, Spec in ipairs(BUTTONS) do
    local Buttons = {};
    local Tainted = false;

    -- GW2_UI styles all 12 buttons of a bar in one pass. Decide the family
    -- once, then apply the same answer to every button in it so a clean
    -- verdict on one sibling cannot leave another sibling unprotected.
    for Index = 1, Spec.Count do
      local Button = _G[Spec.Prefix .. Index];
      if type(Button) == "table" then
        Buttons[#Buttons + 1] = Button;
        local ButtonTainted = TaintOf(Button);
        if not ButtonTainted then ButtonTainted = AncestorTaint(Button); end;
        if ButtonTainted then Tainted = true; end;
      end;
    end;

    local Shim = Guard.ShouldShim(Secret, Spec.Override, Tainted);
    for _, Button in ipairs(Buttons) do
      if Shim then
        if not HasShim(Button) then Guard.InstallCooldownShim(Button); end;
        Checked[Button] = true;
        CleanAt[Button] = nil;
      elseif not Checked[Button] then
        Checked[Button] = true;
        CleanAt[Button] = Now;
      end;

      local Frame = HasAssistedFrame(Button);
      if Frame and Guard.ShouldPause(Secret, Spec.Override, Tainted) then
        if Frame:IsShown() and CanHide(Frame) then
          local Ok = pcall(Frame.Hide, Frame);
          if Ok then Hidden[Frame] = true; end;
        end;
      end;
    end;
  end;
end

--- Compact status for /mcb diag and the Debug console.
function Guard.Status ()
  local Count = 0;
  for _ in pairs(Hidden) do Count = Count + 1; end;
  return ("secret=%s paused=%d shims=%d guard=%s"):format(
    SecretCooldowns() and "yes" or "no", Count, Guard.ShimCount(),
    Guard.Active and "on" or "off");
end

--- Full `/mcb guard` report: names every assisted frame, its parent button,
--- the cooldown shims, the taint source when the client reports one, and the
--- known bar addons. On demand only - nothing here runs during play.
function Guard.Report ()
  Echo("taint guard: " .. Guard.Status());
  local Rate = nil;
  if type(GetCVar) == "function" then
    local Ok, Value = pcall(GetCVar, "assistedCombatIconUpdateRate");
    if Ok then Rate = Value; end;
  end;
  Echo("assistedCombatIconUpdateRate: " .. tostring(Rate or "?"));

  local Seen = 0;
  local Shimmed = {};
  for _, Spec in ipairs(BUTTONS) do
    for Index = 1, Spec.Count do
      local Name = Spec.Prefix .. Index;
      local Button = _G[Name];
      if type(Button) == "table" then
        local Frame = HasAssistedFrame(Button);
        local Shim = HasShim(Button);
        if Frame then
          Seen = Seen + 1;
          local Tainted, Source = TaintOf(Button);
          if not Tainted then Tainted, Source = AncestorTaint(Button); end;
          Echo(("  %s: shown=%s%s%s%s"):format(
            Name,
            Frame:IsShown() and "yes" or "no",
            Shim and " shim=yes" or "",
            Tainted and " tainted" or "",
            Source and (" by " .. Source) or ""));
        elseif Shim then
          -- A shimmed button without an assisted frame is exactly the
          -- SPELL_UPDATE_ICON path (ActionButton.lua:847): worth naming.
          Shimmed[#Shimmed + 1] = Name;
        end;
      end;
    end;
  end;
  if Seen == 0 then Echo("  no assisted-combat frames on the standard bars"); end;
  if #Shimmed > 0 then
    Echo("  cooldown shims without an assisted frame: " .. table.concat(Shimmed, ", "));
  end;

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
  Guard.Active = DB.TaintGuard;
  if not Guard.Active then
    -- Wrappers stay installed and pass every call through while the guard is
    -- off; only the hidden assisted frames are released.
    for Frame in pairs(Hidden) do Hidden[Frame] = nil; end;
  end;
  Echo("[taint] guard " .. (Guard.Active and "on" or "off"));
  return Guard.Active;
end

-- Event + ticker plumbing. The ticker is cheap (one pass over the standard
-- button names, no frame enumeration, no repeated issecurevariable calls: the
-- taint verdicts are memoised) and catches an assisted frame that Blizzard
-- re-shows after an action/slot change. Every event starts a fresh cache
-- generation (ADDON_LOADED at login is where the bar addons finish tainting);
-- ADDON_LOADED only invalidates, so a login with many addons does not run a
-- full taint scan per addon - the 0.5 s ticker / PLAYER_ENTERING_WORLD scans.
local Listener = CreateFrame("Frame");
for _, Event in ipairs({
  "ADDON_LOADED",
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
Listener:SetScript("OnEvent", function (_, Event)
  Guard.InvalidateCache();
  if Event == "ADDON_LOADED" then return; end;
  pcall(Guard.Scan);
end);

if C_Timer and C_Timer.NewTicker then
  MDCB.SecretGuardTicker = C_Timer.NewTicker(0.5, function ()
    pcall(Guard.Scan);
  end);
end
