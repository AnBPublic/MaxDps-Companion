-- Offline secret-safety harness for the MaxDpsBridge addon (Reader + Bridge).
--
-- Run:  lua tests/secret_harness.lua   (from the repo root, Lua 5.4)
--
-- Simulates Midnight 12.x secret semantics: "secret" values throw on
-- compare (<, <=, ==), arithmetic, and boolean tests, and issecretvalue
-- returns true for them. Secret numbers cannot be simulated exactly (Lua
-- numbers carry no metatable and type() must stay "number"), so the
-- harness proves the two things that matter for the live bug:
--   1. the gate branches only on NeverSecret booleans when they exist, and
--   2. every path that could touch a secret either never reaches it or is
--      pcall-contained and fails open — asserted via a throwing clock.
local PASS, FAIL = 0, 0
local function check(name, cond)
  if cond then PASS = PASS + 1; print("PASS: " .. name)
  else FAIL = FAIL + 1; print("FAIL: " .. name) end
end

-- ================= secrets =================
local SMT = {}
function SMT.__lt() error("attempt to compare (secret value)", 2) end
function SMT.__le() error("attempt to compare (secret value)", 2) end
function SMT.__eq() error("attempt to compare (secret value)", 2) end
function SMT.__add() error("attempt to perform arithmetic on (secret value)", 2) end
function SMT.__sub() error("attempt to perform arithmetic on (secret value)", 2) end
function SMT.__mul() error("attempt to perform arithmetic on (secret value)", 2) end
function SMT.__mod() error("attempt to perform arithmetic on (secret value)", 2) end
function SMT.__unm() error("attempt to perform arithmetic on (secret value)", 2) end
function SMT.__len() error("attempt to get length of (secret value)", 2) end
function SMT.__tostring() error("attempt to convert (secret value) to string", 2) end
local function S(v) return setmetatable({ v = v }, SMT) end

issecretvalue = function(v) return type(v) == "table" and getmetatable(v) == SMT end
canaccessvalue = function(v) return not issecretvalue(v) end
-- The real scrub API: secret passes through as nil, plain values unchanged.
scrubsecretvalues = function(v)
  if type(v) == "table" and getmetatable(v) == SMT then return nil end
  return v
end

-- ================= WoW stubs =================
local RealGetTime = function() return 1000.0 end
GetTime = RealGetTime
function wipe(t) for k in pairs(t) do t[k] = nil end end
function hooksecurefunc() end
function strtrim(s) return (s:gsub("^%s*(.-)%s*$", "%1")) end
function strsub(s, a, b) return s:sub(a, b) end
function strupper(s) return s:upper() end
function strlower(s) return s:lower() end
function strlen(s) return #s end
function strfind(s, p) return s:find(p) end
function strmatch(s, p) return s:match(p) end
function strsplit(sep, s, limit)
  local out = {}
  s = tostring(s or "")
  local start = 1
  while true do
    if limit and #out >= limit - 1 then out[#out + 1] = s:sub(start); break end
    local a, b = s:find(sep, start, true)
    if not a then out[#out + 1] = s:sub(start); break end
    out[#out + 1] = s:sub(start, a - 1)
    start = b + 1
  end
  return table.unpack(out)
end
function format(f, ...) return string.format(f, ...) end
LibStub = nil
ChatLog = {}
DEFAULT_CHAT_FRAME = { AddMessage = function(_, Message) ChatLog[#ChatLog + 1] = tostring(Message) end }
function ClearChat () for i = #ChatLog, 1, -1 do ChatLog[i] = nil end end
function CreateColor(r, g, b, a) return { r = r, g = g, b = b, a = a } end
Enum = { LuaCurveType = { Linear = 1 } }
bit = {
  rshift = function(a, b) return math.floor(a / 2 ^ b) end,
  lshift = function(a, b) return (a * 2 ^ b) % 4294967296 end,
  band = function(a, b)
    local r, p = 0, 1
    while a > 0 and b > 0 do
      if a % 2 == 1 and b % 2 == 1 then r = r + p end
      a = math.floor(a / 2); b = math.floor(b / 2); p = p * 2
    end
    return r
  end,
  bor = function(...)
    local Args = { ... }
    local r = 0
    for a = 1, #Args do
      local x, p = Args[a], 1
      while x > 0 do
        if x % 2 == 1 then r = r + p end
        x = math.floor(x / 2); p = p * 2
      end
    end
    return r
  end,
}
SlashCmdList = {}
-- A1.1/A1.7: C_Timer.After QUEUES the callback instead of dropping it, so the
-- offline harness can drive deferred/coalesced work deterministically with
-- runDeferred() (the client's frame scheduler is replaced by an explicit
-- flush, which no other section calls).
local Deferred = {}
C_Timer = { After = function(_, Fn) Deferred[#Deferred + 1] = Fn end }
local function runDeferred ()
  local Pending = Deferred
  Deferred = {}
  for i = 1, #Pending do
    local Ok, Err = pcall(Pending[i])
    if not Ok then print("  deferred error: " .. tostring(Err)) end
  end
  return #Pending
end

-- frames
local AnonCount = 0
local AllFrames = {}
local Textures = {}
local ColorWrites = {}   -- texture id -> SetColorTexture call count (A1.5 pin)
local VertexWrites = {}  -- texture id -> SetVertexColor call count (A1.5 pin)
Vertex = {}   -- texture id -> first SetVertexColor arg (secret passthrough)
local TextureCount = 0
local function FakeFrame(name)
  local f = { _scripts = {}, _textures = {} }
  f.RegisterEvent = function(self, event)
    self._events = self._events or {}
    self._events[event] = true
  end
  f.RegisterAllEvents = function() end
  f.UnregisterAllEvents = function() end
  f.RegisterUnitEvent = function(self, event, unit)
    self._unitEvents = self._unitEvents or {}
    self._unitEvents[event .. ":" .. unit] = true
  end
  f.SetScript = function(self, s, fn) self._scripts[s] = fn end
  f.SetFrameStrata = function() end
  f.SetFrameLevel = function() end
  f.EnableMouse = function() end
  f.CreateTexture = function(self)
    TextureCount = TextureCount + 1
    local id = TextureCount
    local t = { _id = id }
    t.SetColorTexture = function(self2, r, g, b, a)
      Textures[self2._id] = { r, g, b }
      ColorWrites[self2._id] = (ColorWrites[self2._id] or 0) + 1
    end
    t.SetVertexColor = function(self2, r, g, b, a)
      Vertex[self2._id] = r
      VertexWrites[self2._id] = (VertexWrites[self2._id] or 0) + 1
    end
    t.SetAlpha = function() end
    t.ClearAllPoints = function() end
    t.SetPoint = function() end
    t.SetSize = function() end
    t.Show = function() end
    t.Hide = function() end
    return t
  end
  f.SetScale = function() end
  f.GetEffectiveScale = function() return 1 end
  f.ClearAllPoints = function() end
  f.SetPoint = function() end
  f.SetSize = function(self, w, h) self._size = { w, h } end
  -- A1.10 pin hooks: a frame only reports a size when a test sets _w / _h
  -- (real frames always answer; the default harness frame answers nil so the
  -- Layout clamp is skipped exactly like a client without a laid-out UIParent).
  f.GetWidth = function(self) return self._w end
  f.GetHeight = function(self) return self._h end
  f.SetWidth = function() end
  f.SetHeight = function() end
  f.SetColorTexture = function() end
  f.SetAlpha = function() end
  f.Show = function() end
  f.Hide = function() end
  if name then _G[name] = f end
  AllFrames[#AllFrames + 1] = f
  return f
end

local function FireUnitEvent(unit, event)
  for _, f in ipairs(AllFrames) do
    if f._unitEvents and f._unitEvents[event .. ":" .. unit] and f._scripts.OnEvent then
      f._scripts.OnEvent(f, event)
    end
  end
end

local function FirePlainEvent(event, arg1, arg2)
  for _, f in ipairs(AllFrames) do
    if f._events and f._events[event] and f._scripts.OnEvent then
      f._scripts.OnEvent(f, event, arg1, arg2)
    end
  end
end

function CreateFrame(_, name)
  if not name then
    AnonCount = AnonCount + 1
    name = "AnonFrame" .. AnonCount
  end
  return FakeFrame(name)
end
UIParent = FakeFrame("UIParent")
MaxDpsBridgeDB = {}

-- unit/action APIs
function UnitExists() return true end
function UnitIsDead() return false end
function UnitCanAttack() return true end
function UnitGUID() return "guid-1" end
function UnitCastingInfo() return nil end
function UnitChannelInfo() return nil end
function UnitClass() return "Warrior", "WARRIOR", 1 end
function GetActionInfo() return nil end
function GetBindingKey() return nil end
function GetSpellTexture() return nil end
function CheckInteractDistance() return false end
-- Default: out of combat, so the #nocombat CheckInteractDistance probe runs.
function InCombatLockdown() return false end
function UnitHealth() return 80 end
function UnitHealthMax() return 100 end
GetSpecialization = function() return 1 end
GetSpecializationInfo = function() return 71 end
AuraUtil = nil

-- Ext2 (v3.0.0) variant APIs: identity stubs; individual tests override them.
function FindBaseSpellByID(id) return id end
function FindSpellOverrideByID(id) return nil end
function GetOverrideSpell(id) return nil end
function GetMacroSpell(id) return nil end
C_SpellBook = nil
IsPlayerSpell = nil
UnitHealthPercent = nil

-- ================= MaxDps engine stub =================
C_Spell = {}
-- default: non-charge spell, plain no-cooldown table
C_Spell.GetSpellCharges = function() return nil end
C_Spell.GetSpellCooldown = function()
  return { startTime = 0, duration = 0, isEnabled = true, isActive = false, isOnGCD = false }
end
C_Spell.IsSpellUsable = function() return true, false end

-- Duration objects (the documented secret-blind cooldown path): the reader
-- evaluates remaining time through a curve, so the harness controls
-- "seconds remaining" directly.
C_CurveUtil = {
  CreateColorCurve = function()
    return { SetType = function() end, AddPoint = function() end }
  end,
}
local RemainingBySpell = {}
C_Spell.GetSpellCooldownDuration = function(spellId)
  local remaining = RemainingBySpell[spellId]
  if remaining == nil then remaining = 0 end
  return { EvaluateRemainingDuration = function() return remaining end }
end

MaxDps = {
  idtospec = { [71] = "Arms", [72] = "Fury", [73] = "Protection" },
  Spells = { [185358] = {} },
  Flags = { [185358] = true },
  ItemSpells = {},
  db = { global = { enableCooldowns = true, enableDefensives = true } },
  Spell = 185358, NextSpell = nil, FrameData = {},
  CheckSpellUsable = function() return true end,
  CooldownConsolidated = function() error("CooldownConsolidated must not be called from the bridge") end,
  PrepareFrameData = function() end, UpdateAuraData = function() end, Fetch = function() end,
}
_G.MaxDps = MaxDps
C_AssistedCombat = nil

-- ================= load the addon =================
local MDB = {}
_G.MaxDpsBridge = MDB
assert(loadfile("addon/legacy/MaxDpsBridge/Catalog.lua"))("MaxDpsBridge", MDB)
assert(loadfile("addon/legacy/MaxDpsBridge/MajorCooldowns.lua"))("MaxDpsBridge", MDB)
-- Workstream C emits the real MDB.SpellAliases in Catalog.lua; this worktree
-- stubs it so the variant path is exercised before C is merged.
MDB.SpellAliases = { [202168] = { 34428 } }
assert(loadfile("addon/legacy/MaxDpsBridge/Keymap.lua"))("MaxDpsBridge", MDB)
assert(loadfile("addon/legacy/MaxDpsBridge/Bars.lua"))("MaxDpsBridge", MDB)
-- Q1 fallback registry loads before Reader (Reader references MDB.MainFallback).
assert(loadfile("addon/legacy/MaxDpsBridge/MainFallback.lua"))("MaxDpsBridge", MDB)
assert(loadfile("addon/legacy/MaxDpsBridge/Reader.lua"))("MaxDpsBridge", MDB)
-- 3.3.0 toggle policy loads before Bridge (Bridge builds its slash key map at
-- load time from MDB.Toggles.Keys); pure logic, no frames.
assert(loadfile("addon/legacy/MaxDpsBridge/Toggles.lua"))("MaxDpsBridge", MDB)
assert(loadfile("addon/legacy/MaxDpsBridge/Bridge.lua"))("MaxDpsBridge", MDB)
assert(type(MDB.IsSpellReady) == "function")
assert(type(MDB.IsInterruptReady) == "function")
assert(type(MDB.GetMobilitySpellID) == "function")
assert(type(MDB.GetPlayerHpPct) == "function")

-- boot the bridge: fire ADDON_LOADED like the client would, then take the
-- OnUpdate handler the bootstrap installed on the strip frame.
-- Find the frame that registered ADDON_LOADED (Bars.lua now also creates
-- anonymous frames, so AnonFrame1 is not necessarily the bridge loader).
local Loader
for _, f in ipairs(AllFrames) do
  if f._events and f._events["ADDON_LOADED"] and f._scripts and f._scripts.OnEvent then
    Loader = f; break
  end
end
assert(Loader, "loader frame missing")
Loader._scripts.OnEvent(Loader, "ADDON_LOADED", "MaxDpsBridge")
local Strip = _G.MaxDpsBridge_Block
assert(Strip, "strip frame missing")
local Update = Strip._scripts.OnUpdate
assert(type(Update) == "function", "OnUpdate not wired")

-- ================= load the Exp addon (CURRENT client) =================
-- MaxDpsBridge stable is LEGACY/frozen: its MajorCooldowns/MainFallback no
-- longer carry the 2026-10-04/06 routing (Ravager/Divine Toll
-- FlagOffensiveExtra, Arms/Ret MainFallback). Those behaviours are pinned
-- against the CURRENT client, MaxDpsCompanionBridge, loaded into its own table
-- (MDCB) so the frozen stable files stay untouched. Only the runtime modules
-- the Reader needs are loaded; the Exp frame/overlay modules are not.
MDCB = _G.MaxDpsCompanionBridge or {}
_G.MaxDpsCompanionBridge = MDCB
assert(loadfile("addon/MaxDpsCompanionBridge/Catalog.lua"))("MaxDpsCompanionBridge", MDCB)
assert(loadfile("addon/MaxDpsCompanionBridge/MajorCooldowns.lua"))("MaxDpsCompanionBridge", MDCB)
assert(loadfile("addon/MaxDpsCompanionBridge/Keymap.lua"))("MaxDpsCompanionBridge", MDCB)
assert(loadfile("addon/MaxDpsCompanionBridge/MainFallback.lua"))("MaxDpsCompanionBridge", MDCB)
MDCB.SpellAliases = MDCB.SpellAliases or { [202168] = { 34428 } }
assert(loadfile("addon/MaxDpsCompanionBridge/Reader.lua"))("MaxDpsCompanionBridge", MDCB)
-- 4.5.1 taint guard: pure policy + WoW-frame plumbing (the harness's
-- CreateFrame stub gives it a listener frame).
assert(loadfile("addon/MaxDpsCompanionBridge/SecretGuard.lua"))("MaxDpsCompanionBridge", MDCB)
assert(type(MDCB.GetMainSpellID) == "function")
assert(type(MDCB.GetOffensiveCandidate) == "function")
assert(type(MDCB.SecretGuard) == "table")

-- 4.5.1 taint-guard policy: clean main-bar assisted frames are never hidden;
-- override buttons and any button the client names as tainted are paused only
-- while cooldowns are secret.
do
  local G = MDCB.SecretGuard;
  check("4.5.1 guard: clean main bar stays alone", G.ShouldPause(true, false, false) == false)
  check("4.5.1 guard: secret + override pauses", G.ShouldPause(true, true, false) == true)
  check("4.5.1 guard: secret + tainted main pauses", G.ShouldPause(true, false, true) == true)
  check("4.5.1 guard: not secret never pauses", G.ShouldPause(false, true, true) == false)

  -- Full sweep: an override assisted frame is hidden while the predicate is
  -- true, and a clean main-bar frame is untouched.
  local function FakeAssisted ()
    return {
      Shown = true,
      IsShown = function(self) return self.Shown end,
      Hide = function(self) self.Shown = false end,
    };
  end
  local OverrideFrame, MainFrame = FakeAssisted(), FakeAssisted();
  _G.OverrideActionBarButton2 = { AssistedCombatRotationFrame = OverrideFrame };
  _G.ActionButton1 = { AssistedCombatRotationFrame = MainFrame };
  local SavedSecrets = C_Secrets;
  C_Secrets = { ShouldCooldownsBeSecret = function() return true end };
  G.Scan();
  check("4.5.1 guard: override assisted frame is paused", OverrideFrame.Shown == false)
  check("4.5.1 guard: clean main assisted frame stays", MainFrame.Shown == true)
  C_Secrets = { ShouldCooldownsBeSecret = function() return false end };
  G.Scan();
  C_Secrets = SavedSecrets;
  _G.OverrideActionBarButton2, _G.ActionButton1 = nil, nil;
end

-- 4.5.7 cooldown shim: hiding the assisted frame cannot stop Blizzard's own
-- SPELL_UPDATE_ICON path (ActionButton.lua:1026 -> :847 cooldown:SetCooldown),
-- which throws on secret start/duration/modRate once a bar addon tainted the
-- button. The guard wraps the three cooldown frames of a button the client
-- reports as tainted and sanitises secret arguments; clean buttons keep
-- Blizzard's method untouched and the guard stays silent.
do
  local G = MDCB.SecretGuard;
  local AddonName = "MaxDpsCompanionBridge";

  -- Recording fake cooldown: SetCooldown keeps every call, Clear is optional.
  local function FakeCooldown (WithClear)
    local Cooldown = { Calls = {} };
    Cooldown.SetCooldown = function (self, Start, Duration, ModRate)
      self.Calls[#self.Calls + 1] = { Start, Duration, ModRate };
      self.SecretArgs = issecretvalue(Start) or issecretvalue(Duration)
        or issecretvalue(ModRate);
    end
    if WithClear then
      Cooldown.Clear = function (self) self.Cleared = (self.Cleared or 0) + 1 end
    end
    return Cooldown
  end

  local SavedSecrets, SavedSecure = C_Secrets, issecurevariable;
  local SavedEnsure, SavedActive = MDCB.EnsureDB, G.Active;
  local function SecretOn ()
    C_Secrets = { ShouldCooldownsBeSecret = function () return true end };
  end
  local function SecretOff ()
    C_Secrets = { ShouldCooldownsBeSecret = function () return false end };
  end

  -- (a) secret arguments are sanitised: Clear() when the frame has one, never
  -- the original SetCooldown with a secret value.
  G.Active = true;
  local Cooldown = FakeCooldown(true);
  local Button = { cooldown = Cooldown };
  check("4.5.7 shim: install wraps the button cooldown",
    G.InstallCooldownShim(Button) == 1 and Cooldown.SetCooldown == G.GuardedSetCooldown);
  Button.cooldown:SetCooldown(S(1000), S(30), S(1));
  check("4.5.7 shim: secret start/duration/modRate -> Clear, original untouched",
    Cooldown.Cleared == 1 and #Cooldown.Calls == 0 and Cooldown.SecretArgs == nil);
  Button.cooldown:SetCooldown(S(1000), 30, 1);
  check("4.5.7 shim: secret start alone -> Clear",
    Cooldown.Cleared == 2 and #Cooldown.Calls == 0);
  Button.cooldown:SetCooldown(1000, S(30), 1);
  check("4.5.7 shim: secret duration alone -> Clear",
    Cooldown.Cleared == 3 and #Cooldown.Calls == 0);
  Button.cooldown:SetCooldown(1000, 30, S(1));
  check("4.5.7 shim: secret modRate alone -> Clear",
    Cooldown.Cleared == 4 and #Cooldown.Calls == 0);

  -- (b) plain arguments pass straight through, 3-arg and 2-arg shapes.
  Button.cooldown:SetCooldown(1000, 30, 1);
  check("4.5.7 shim: plain 3-arg call passes through",
    #Cooldown.Calls == 1 and Cooldown.Calls[1][1] == 1000
    and Cooldown.Calls[1][2] == 30 and Cooldown.Calls[1][3] == 1
    and Cooldown.SecretArgs == false);
  Button.cooldown:SetCooldown(1000, 5);
  check("4.5.7 shim: plain 2-arg call passes through with 2 args",
    #Cooldown.Calls == 2 and Cooldown.Calls[2][1] == 1000
    and Cooldown.Calls[2][2] == 5 and Cooldown.Calls[2][3] == nil);

  -- (c) guard off: every call passes through, secret or not.
  G.Active = false;
  Button.cooldown:SetCooldown(S(1000), S(30), S(1));
  check("4.5.7 shim: guard off passes secret args through",
    #Cooldown.Calls == 3 and Cooldown.Cleared == 4 and Cooldown.SecretArgs == true);
  G.Active = true;

  -- (c2) guard off is transparent, not a silent error sink: the original
  -- SetCooldown error must reach the caller (this is how stock behavior
  -- returns when the user opts out with /mcb guard off).
  local ThrowCooldown = {
    SetCooldown = function () error("secret args rejected", 0) end,
  };
  local ThrowButton = { cooldown = ThrowCooldown };
  G.InstallCooldownShim(ThrowButton);
  G.Active = false;
  local OkThrow, ErrThrow = pcall(
    ThrowButton.cooldown.SetCooldown, ThrowButton.cooldown, S(1), S(2), S(3));
  check("4.5.7 shim: guard off does not swallow the original error",
    OkThrow == false and tostring(ErrThrow):find("secret args rejected", 1, true) ~= nil);
  G.Active = true;

  -- (d) a frame without Clear falls back to SetCooldown(0, 0).
  local NoClearCooldown = FakeCooldown(false);
  local NoClearButton = { cooldown = NoClearCooldown };
  G.InstallCooldownShim(NoClearButton);
  NoClearButton.cooldown:SetCooldown(S(1000), S(30), S(1));
  check("4.5.7 shim: no Clear falls back to SetCooldown(0, 0)",
    #NoClearCooldown.Calls == 1 and #NoClearCooldown.Calls[1] == 2
    and NoClearCooldown.Calls[1][1] == 0 and NoClearCooldown.Calls[1][2] == 0
    and NoClearCooldown.SecretArgs == false);

  -- (e) idempotent install, foreign wrapper wins.
  local Base = G.ShimCount();
  local TwiceCooldown = FakeCooldown(true);
  local TwiceButton = { cooldown = TwiceCooldown };
  G.InstallCooldownShim(TwiceButton);
  check("4.5.7 shim: installing twice leaves the original saved once",
    G.ShimCount() == Base + 1 and G.InstallCooldownShim(TwiceButton) == 0
    and G.ShimCount() == Base + 1);
  local Foreign = function () end;
  TwiceCooldown.SetCooldown = Foreign;
  G.InstallCooldownShim(TwiceButton);
  check("4.5.7 shim: a foreign wrapper set after install is not overwritten",
    TwiceCooldown.SetCooldown == Foreign);

  -- (f) Scan: a clean main-bar button is never wrapped while secrets are
  -- active; an override button (the vehicle/delve path) always is.
  issecurevariable = function () return true end;
  SecretOn();
  local CleanCooldown = FakeCooldown(true);
  _G.ActionButton1 = { cooldown = CleanCooldown };
  local OverrideCooldown = FakeCooldown(true);
  _G.OverrideActionBarButton1 = { cooldown = OverrideCooldown };
  G.InvalidateCache();
  Base = G.ShimCount();
  G.Scan();
  check("4.5.7 shim: clean main button gets no shim while secret",
    CleanCooldown.SetCooldown ~= G.GuardedSetCooldown);
  check("4.5.7 shim: override button gets a shim while secret",
    OverrideCooldown.SetCooldown == G.GuardedSetCooldown and G.ShimCount() == Base + 1);

  SecretOff();
  local LooseOverrideCooldown = FakeCooldown(true);
  _G.OverrideActionBarButton1 = { cooldown = LooseOverrideCooldown };
  G.InvalidateCache();
  G.Scan();
  check("4.5.7 shim: not secret never shims an override button",
    LooseOverrideCooldown.SetCooldown ~= G.GuardedSetCooldown);

  -- (g) a bar-tainted button gets all three cooldown frames wrapped.
  -- The scratch override button above is dropped first: override buttons are
  -- shimmed on their own, which would blur the per-frame count below.
  _G.OverrideActionBarButton1 = nil;
  SecretOn();
  local TaintedCooldown = FakeCooldown(true);
  local ChargeCooldown = FakeCooldown(true);
  local LocCooldown = FakeCooldown(true);
  local TaintedButton = {
    cooldown = TaintedCooldown,
    chargeCooldown = ChargeCooldown,
    lossOfControlCooldown = LocCooldown,
  };
  _G.ActionButton2 = TaintedButton;
  issecurevariable = function (Frame)
    if Frame == TaintedButton then return false, "GW2_UI" end;
    return true;
  end;
  G.InvalidateCache();
  G.Scan();
  check("4.5.7 shim: tainted button wraps normal/charge/loss-of-control frames",
    TaintedCooldown.SetCooldown == G.GuardedSetCooldown
    and ChargeCooldown.SetCooldown == G.GuardedSetCooldown
    and LocCooldown.SetCooldown == G.GuardedSetCooldown);

  -- (h) a button parented on an addon-created frame (GW2_UI's fmMultibar) is
  -- tainted through the named ancestor even when its own fields look clean.
  local Bar = { _name = "GW2_UI_Bar" };
  Bar.GetName = function (self) return self._name end;
  local BarredCooldown = FakeCooldown(true);
  local BarredButton = { cooldown = BarredCooldown };
  BarredButton.GetParent = function () return Bar end;
  _G.ActionButton3 = BarredButton;
  issecurevariable = function (Frame, Key)
    if Frame == _G and Key == "GW2_UI_Bar" then return false, "GW2_UI" end;
    return true;
  end;
  G.InvalidateCache();
  G.Scan();
  check("4.5.7 shim: named third-party ancestor triggers a shim",
    BarredCooldown.SetCooldown == G.GuardedSetCooldown);

  -- (i) a taint source that is this addon is ignored (our own writes).
  local OwnCooldown = FakeCooldown(true);
  _G.ActionButton4 = { cooldown = OwnCooldown };
  issecurevariable = function () return false, AddonName end;
  G.InvalidateCache();
  G.Scan();
  check("4.5.7 shim: our own addon name is not a taint source",
    OwnCooldown.SetCooldown ~= G.GuardedSetCooldown);

  -- (j) status exposes the shim count.
  local StatusLine = G.Status();
  check("4.5.7 shim: status contains shims=",
    StatusLine:find("shims=", 1, true) ~= nil);
  check("4.5.7 shim: status counts the installed shims",
    tonumber(StatusLine:match("shims=(%d+)")) == G.ShimCount());

  -- (k) the taint verdict is memoised: a second Scan in the same generation
  -- asks the client nothing, and a secret flip opens the cache again.
  local Probes = 0;
  issecurevariable = function () Probes = Probes + 1; return true end;
  local CacheCooldown = FakeCooldown(true);
  _G.ActionButton5 = { cooldown = CacheCooldown };
  SecretOn();
  G.InvalidateCache();
  G.Scan();
  local FirstProbes = Probes;
  G.Scan();
  check("4.5.7 shim: a cached scan makes no new issecurevariable calls",
    FirstProbes > 0 and Probes == FirstProbes);
  SecretOff();
  G.Scan();
  SecretOn();
  G.Scan();
  check("4.5.7 shim: a secret flip resumes the taint checks", Probes > FirstProbes);

  -- (l) SetEnabled flips the module state, keeps the wrappers and prints one
  -- confirmation line (manual command only).
  local StubDB = { TaintGuard = true };
  MDCB.EnsureDB = function () return StubDB end;
  ClearChat();
  check("4.5.7 shim: SetEnabled(off) keeps the installed shims",
    G.SetEnabled(false) == false and G.Active == false and G.ShimCount() > 0);
  local Line = ChatLog[#ChatLog] or "";
  check("4.5.7 shim: /mcb guard off prints one confirmation line",
    #ChatLog == 1 and Line:find("guard off", 1, true) ~= nil);
  check("4.5.7 shim: SetEnabled(on) resumes the guard",
    G.SetEnabled(true) == true and G.Active == true and StubDB.TaintGuard == true);

  -- (m) GW2_UI styles the main bar in place: ActionButton1 keeps Blizzard's
  -- MainActionBar parent, and SetScript(..., nil) leaves no issecurevariable
  -- field. The gw_ keys are the signal, and the whole ActionButton family gets
  -- the shim (GW2_UI styles all 12 buttons in one pass).
  SecretOn();
  issecurevariable = function () return true end;
  local GwCooldown = FakeCooldown(true);
  local PeerCooldown = FakeCooldown(true);
  _G.ActionButton6 = { cooldown = GwCooldown, gw_ShowMacroName = true };
  _G.ActionButton7 = { cooldown = PeerCooldown };
  G.InvalidateCache();
  G.Scan();
  check("4.5.8 shim: GW2_UI gw_ marker triggers a main-bar shim",
    GwCooldown.SetCooldown == G.GuardedSetCooldown);
  check("4.5.8 shim: a tainted family covers its clean sibling",
    PeerCooldown.SetCooldown == G.GuardedSetCooldown);

  -- (n) the parent bar's gw_Buttons key is enough when the button itself has
  -- no marker (the MainActionBar case).
  local GwBar = { gw_Buttons = {} };
  local GwBarCooldown = FakeCooldown(true);
  local GwBarButton = { cooldown = GwBarCooldown };
  GwBarButton.GetParent = function () return GwBar end;
  _G.MultiBarLeftButton1 = GwBarButton;
  issecurevariable = function () return true end;
  G.InvalidateCache();
  G.Scan();
  check("4.5.8 shim: parent gw_Buttons triggers the family shim",
    GwBarCooldown.SetCooldown == G.GuardedSetCooldown);

  -- (o) a family with no marker and a clean issecurevariable stays untouched.
  local CleanRightCooldown = FakeCooldown(true);
  _G.MultiBarRightButton1 = { cooldown = CleanRightCooldown };
  issecurevariable = function () return true end;
  G.InvalidateCache();
  G.Scan();
  check("4.5.8 shim: a clean family stays untouched",
    CleanRightCooldown.SetCooldown ~= G.GuardedSetCooldown);

  -- (p) the marker scan reads keys only; a secret gw_ value must not throw.
  local SecretMarkerCooldown = FakeCooldown(true);
  _G.MultiBar6Button1 = { cooldown = SecretMarkerCooldown, gw_secret = S(1) };
  issecurevariable = function () return true end;
  G.InvalidateCache();
  local OkSecretMarker = pcall(G.Scan);
  check("4.5.8 shim: gw_ key scan ignores a secret value",
    OkSecretMarker and SecretMarkerCooldown.SetCooldown == G.GuardedSetCooldown);

  -- (q) a marker added after a clean verdict is picked up after the 10 s TTL.
  local SavedGetTime = GetTime;
  local Now = 1000;
  GetTime = function () return Now end;
  local TtlCooldown = FakeCooldown(true);
  _G.MultiBar5Button1 = { cooldown = TtlCooldown };
  issecurevariable = function () return true end;
  G.InvalidateCache();
  G.Scan();
  check("4.5.8 shim: clean family is remembered",
    TtlCooldown.SetCooldown ~= G.GuardedSetCooldown);
  _G.MultiBar5Button1.gw_ShowMacroName = true;
  Now = 1005;
  G.Scan();
  check("4.5.8 shim: clean verdict is cached for 10 s",
    TtlCooldown.SetCooldown ~= G.GuardedSetCooldown);
  Now = 1011;
  G.Scan();
  check("4.5.8 shim: late gw_ marker is picked up after the TTL",
    TtlCooldown.SetCooldown == G.GuardedSetCooldown);
  GetTime = SavedGetTime;

  C_Secrets, issecurevariable = SavedSecrets, SavedSecure;
  MDCB.EnsureDB, G.Active = SavedEnsure, SavedActive;
  G.InvalidateCache();
  _G.ActionButton1, _G.ActionButton2, _G.ActionButton3 = nil, nil, nil;
  _G.ActionButton4, _G.ActionButton5 = nil, nil;
  _G.ActionButton6, _G.ActionButton7 = nil, nil;
  _G.OverrideActionBarButton1 = nil;
  _G.MultiBarLeftButton1, _G.MultiBarRightButton1 = nil, nil;
  _G.MultiBar5Button1, _G.MultiBar6Button1 = nil, nil;
end

-- ================= 1. restricted cooldown shapes =================
-- active real cooldown: isActive=true, isOnGCD=false; the Duration object
-- reports 30 s remaining (the live path).
RemainingBySpell[185358] = 30
C_Spell.GetSpellCooldown = function()
  return { startTime = S(999), duration = S(30), isEnabled = true, isActive = true, isOnGCD = false }
end
local ok, ready = pcall(MDB.IsSpellReady, 185358)
check("restricted active CD: no throw", ok)
check("restricted active CD: not ready", ok and ready == false)

-- GCD only: isActive=true, isOnGCD=true -> forgiven
C_Spell.GetSpellCooldown = function()
  return { startTime = S(999), duration = S(1.5), isEnabled = true, isActive = true, isOnGCD = true }
end
ok, ready = pcall(MDB.IsSpellReady, 185358)
check("restricted GCD-only: no throw", ok)
check("restricted GCD-only: ready (forgiven)", ok and ready == true)

-- no cooldown: isActive=false
RemainingBySpell[185358] = 0
C_Spell.GetSpellCooldown = function()
  return { startTime = S(0), duration = S(0), isEnabled = true, isActive = false, isOnGCD = false }
end
ok, ready = pcall(MDB.IsSpellReady, 185358)
check("restricted no-CD: ready", ok and ready == true)

-- ================= 2. secret spell IDs =================
local secretID = S(185358)
ok, ready = pcall(MDB.IsSpellReady, secretID)
check("secret spellID: no throw", ok)
check("secret spellID: not encodable", ok and ready == false)
ok = pcall(MDB.ResolveBinding, secretID)
local okVK, vk = pcall(MDB.ResolveBinding, secretID)
check("secret spellID ResolveBinding: no throw, nil", okVK and vk == nil)
ok, ready = pcall(MDB.IsInterruptReady, secretID)
check("secret spellID IsInterruptReady: no throw, false", ok and ready == false)

-- Charges are deliberately not read (currentCharges is secret in combat):
-- the cooldown path decides. A live 5 s cooldown must not fire.
C_Spell.GetSpellCharges = function()
  return { currentCharges = 0, maxCharges = 2, cooldownStartTime = 975, cooldownDuration = 30, isActive = true }
end
C_Spell.GetSpellCooldown = function()
  return { startTime = S(999), duration = S(30), isEnabled = true, isActive = true, isOnGCD = false }
end
RemainingBySpell[185358] = 5
ok, ready = pcall(MDB.IsSpellReady, 185358)
check("live 5s cooldown left: not ready", ok and ready == false)
RemainingBySpell[185358] = 0
C_Spell.GetSpellCharges = function() return nil end
C_Spell.GetSpellCooldown = function()
  return { startTime = 0, duration = 0, isEnabled = true, isActive = false, isOnGCD = false }
end

-- ================= 4. duration path is pcall-contained =================
-- legacy table shape (no isActive) + a clock that throws: the reader must
-- still decide from the Duration object without ever calling GetTime.
C_Spell.GetSpellCooldown = function()
  return { startTime = 990, duration = 20, isEnabled = true }
end
RemainingBySpell[185358] = 10
GetTime = function() error("simulated secret arithmetic throw") end
ok, ready = pcall(MDB.IsSpellReady, 185358)
GetTime = RealGetTime
check("numeric path throw contained: no throw", ok)
check("Duration 10s left: not ready", ok and ready == false)
RemainingBySpell[185358] = 0

-- the throw must not poison the next plain call
C_Spell.GetSpellCooldown = function()
  return { startTime = 0, duration = 0, isEnabled = true, isActive = false, isOnGCD = false }
end
ok, ready = pcall(MDB.IsSpellReady, 185358)
check("plain no-CD after throw: ready", ok and ready == true)

-- ================= 5. interrupt gate =================
-- The v1.3.0 design NEVER calls UnitCastingInfo / UnitChannelInfo: the gate
-- trusts upstream's own live-interruptible-cast verdict (the category only
-- goes dirty when GlowInteruptMidnight fires for a real cast). Prove no call
-- happens and that the verdict fails open.
UnitCastingInfo = function() error("UnitCastingInfo must not be called") end
UnitChannelInfo = function() error("UnitChannelInfo must not be called") end
ok, ready = pcall(MDB.IsInterruptReady, 185358)
check("interrupt gate never reads cast APIs", ok)
check("interrupt gate fails open on cast state", ok and ready == true)

-- v2.1 interruptibility veto: the state is in the event NAME (no payload is
-- ever read), so the sensor value is a plain boolean. An explicit
-- NOT_INTERRUPTIBLE must veto the slot; unknown fails open.
FireUnitEvent("target", "UNIT_SPELLCAST_START")
FireUnitEvent("target", "UNIT_SPELLCAST_NOT_INTERRUPTIBLE")
ok, ready = pcall(MDB.IsInterruptReady, 185358)
check("interrupt veto: not-interruptible -> not ready", ok and ready == false)
FireUnitEvent("target", "UNIT_SPELLCAST_INTERRUPTIBLE")
ok, ready = pcall(MDB.IsInterruptReady, 185358)
check("interrupt veto: interruptible -> ready", ok and ready == true)
FireUnitEvent("target", "UNIT_SPELLCAST_STOP")
ok, ready = pcall(MDB.IsInterruptReady, 185358)
check("interrupt veto: unknown after stop -> ready (fail open)", ok and ready == true)
UnitCastingInfo = function() return nil end
UnitChannelInfo = function() return nil end

-- ================= 6. secret engine state =================
MaxDps.Flags[secretID] = true  -- secret key in Flags (hostile case)
ok = pcall(function() MDB.EnsureHooks() end)
check("EnsureHooks with secret flag key: no throw", ok)
ok = pcall(MDB.GetCooldownSpellID)
check("GetCooldownSpellID with secret flag key: no throw", ok)
MaxDps.Flags[secretID] = nil
MaxDps.Spell = S(999999)
ok, ready = pcall(MDB.GetMainSpellID)
check("secret MaxDps.Spell: no throw, no suggestion", ok and ready == nil)
MaxDps.Spell = 185358

-- ================= 6b. T3 major-cooldown denylist =================
-- A major cooldown must never be encoded as the MAIN pick: GetMainSpellID
-- skips denied ids in the SpellsGlowing scan and rejects a denied
-- MaxDps.Spell, while a non-denied pick is untouched.
check("T3 MajorCDDeny table present", type(MDB.MajorCDDeny) == "table")
check("T3 Combustion (needs-move) is denied", MDB.MajorCDDeny[190319] == true)
check("T3 Avatar (already-correct) is denied", MDB.MajorCDDeny[107574] == true)
check("T3 non-major id is not denied", MDB.MajorCDDeny[185358] ~= true)

-- (a) 3.7.8: a former-deny glow is now the MAIN pick — MaxDps's core rotation
-- is authoritative and the only veto is MainUsable (here usable), so the glow
-- encodes directly instead of falling through to the Arms filler.
MaxDps.SpellsGlowing = { [190319] = 1 }
MaxDps.Spell = nil
ok, ready = pcall(MDCB.GetMainSpellID)
check("Exp former-deny glow only -> encoded as MAIN",
  ok and ready == 190319 and ready ~= 12294)

-- (b) denied glow LOWER than a kept glow: without the denylist the scan would
-- return the denied 1719; with it the non-denied 185358 wins.
MaxDps.SpellsGlowing = { [1719] = 1, [185358] = 1 }
MaxDps.Spell = nil
ok, ready = pcall(MDB.GetMainSpellID)
check("T3 denied glow skipped, non-denied kept", ok and ready == 185358)

-- (c) a former-deny MaxDps.Spell is likewise encoded as MAIN (no deny), while a
-- non-denied Spell is still kept.
MaxDps.SpellsGlowing = nil
MaxDps.Spell = 107574
ok, ready = pcall(MDCB.GetMainSpellID)
check("Exp former-deny MaxDps.Spell -> encoded as MAIN",
  ok and ready == 107574 and ready ~= 12294)
MaxDps.Spell = 185358
ok, ready = pcall(MDB.GetMainSpellID)
check("T3 non-denied MaxDps.Spell kept", ok and ready == 185358)

-- restore the glow state later sections expect
MaxDps.SpellsGlowing = { [185358] = 1 }

-- ================= 7. Bridge hot loop =================
-- restricted shapes on every slot + secret target flags: Update must not
-- throw and must still paint a decodable frame.
C_Spell.GetSpellCooldown = function()
  return { startTime = S(999), duration = S(30), isEnabled = true, isActive = true, isOnGCD = false }
end
C_Spell.GetSpellCharges = function()
  return { currentCharges = S(0), maxCharges = 2,
           cooldownStartTime = S(999), cooldownDuration = S(30), isActive = true }
end
UnitCastingInfo = function() return "Fireball", nil, 1, 0, 5000, false, "Cast-1", S(true), 133 end
local okUpdate, errUpdate = pcall(Update, Strip, 0.06)
check("Bridge Update under restrictions: no throw", okUpdate)
if not okUpdate then print("  update error: " .. tostring(errUpdate)) end
check("Bridge Update: no readout warning fired", MDB._ReadoutWarned ~= true)
UnitCastingInfo = function() return nil end

-- containment: a getter that throws must not error the loop and must warn once
local realGetMain = MDB.GetMainSpellID
MDB.GetMainSpellID = function() error("simulated internal failure") end
local okContained = pcall(Update, Strip, 0.06)
check("Bridge Update contains getter throw", okContained)
check("Bridge Update warned exactly once", MDB._ReadoutWarned == true)
MDB.GetMainSpellID = realGetMain

-- ================= 8. unrestricted regression =================
C_Spell.GetSpellCooldown = function()
  return { startTime = 0, duration = 0, isEnabled = true, isActive = false, isOnGCD = false }
end
C_Spell.GetSpellCharges = function() return nil end
okUpdate = pcall(Update, Strip, 0.06)
check("Bridge Update unrestricted: no throw", okUpdate)
ok, ready = pcall(MDB.GetMainSpellID)
check("unrestricted main spell resolves", ok and ready == 185358)

-- ================= 9. protocol v5 encode =================
-- Warrior/Arms so the generated Catalog extras (Charge 100, Heroic Leap 6544)
-- resolve; Spells carry HotKey texts so ResolveBinding succeeds.
MaxDps.Spells = {
  [185358] = { { HotKey = { GetText = function() return "1" end } } },
  [100] = { { HotKey = { GetText = function() return "2" end } } },
}
  MaxDps.Flags = { [185358] = true }
  MaxDps.Spell = 185358
  MaxDps.SpellsGlowing = { [185358] = 1 }
  MaxDps.NextSpell = nil
-- Binding results are cached per spell id; earlier sections cached misses
-- against the old Spells table, so drop the cache for this section.
MDB._BindCache = {}
C_Spell.GetSpellCooldown = function()
  return { startTime = 0, duration = 0, isEnabled = true, isActive = false, isOnGCD = false }
end
C_Spell.GetSpellCharges = function() return nil end
UnitHealth = function() return 80 end
UnitHealthMax = function() return 100 end
CheckInteractDistance = function() return false end

local function Nib (cellIndex)
  local c = Textures[cellIndex + 1]
  if not c then return 0, 0, 0 end
  return math.floor(c[1] * 15 + 0.5), math.floor(c[2] * 15 + 0.5), math.floor(c[3] * 15 + 0.5)
end

local function IdAt (cellA, cellB)
  local r0, g0, b0 = Nib(cellA)
  local r1, g1, b1 = Nib(cellB)
  return r0 * 2 ^ 20 + g0 * 2 ^ 16 + b0 * 2 ^ 12 + r1 * 2 ^ 8 + g1 * 2 ^ 4 + b1
end

local okV5 = pcall(Update, Strip, 0.06)
check("v5 Update: no throw", okV5)

-- Bridge.lua keeps the v5 wire version nibble (ADDITIVE urgency block): the
-- new nibbles live in reserved cells 31/32 that the version-5 extension
-- checksum (cells 11-33) already covered, and a pre-2.3 companion exe still
-- decodes the frame. Only this version expectation reverted from the earlier
-- v6 bump; the checksums are unchanged.
local verNib = Nib(10)
check("v5 version nibble stays 5 (additive urgency)", verNib == 5)
local coreSum = 0
for i = 1, 9 do local r, g, b = Nib(i); coreSum = coreSum + r + g + b end
local _, coreCs, coreCommit = Nib(10)
local _, heartbeat = Nib(9)
check("v5 core checksum", (coreSum % 16) == coreCs)
check("v5 core commit == heartbeat", coreCommit == heartbeat)

local extSum = 0
for i = 11, 33 do local r, g, b = Nib(i); extSum = extSum + r + g + b end
local _, extCs, extCommit = Nib(34)
check("v5 extension checksum", (extSum % 16) == extCs)
check("v5 extension commit == heartbeat", extCommit == heartbeat)

local mhi, mlo, mflags = Nib(1)
check("v5 main slot encoded (VK 0x31)", bit.band(mflags, 8) == 8 and (mhi * 16 + mlo) == 0x31)
check("v5 main spell id round-trips", IdAt(11, 12) == 185358)

local _, _, sflags = Nib(7)
check("v5 mobility slot encoded (Charge)", bit.band(sflags, 8) == 8)
check("v5 mobility spell id is Charge", IdAt(23, 24) == 100)

-- v2.2 SELF-SUSTAIN EXTRAS. A ready-but-UNBOUND self-heal (talent not on any
-- bar / no MaxDps button) must stay empty: encoding it would make the
-- companion press a key the player never bound. A bound one must encode with
-- its spell id and the new slot-8 range probe.
local uhi, ulo, uflags = Nib(8)
check("unbound self-heal slot stays empty", bit.band(uflags, 8) == 0)
check("unbound self-heal is not offered", MDB.GetSelfHealSpellID() == nil)

MaxDps.Spells[202168] = { { HotKey = { GetText = function() return "H" end } } }
MaxDps.IsSpellInRange = function() return 1 end
MDB._BindCache = {}
pcall(Update, Strip, 0.06)
local hhi, hlo, hflags = Nib(8)
check("v5 self-heal slot encodes bound IV (VK 0x48)", bit.band(hflags, 8) == 8 and (hhi * 16 + hlo) == 0x48)
check("v5 self-heal spell id round-trips", IdAt(25, 26) == 202168)
local r31 = Nib(31)
check("v5 self-heal range probe encodes (slot 8 in range)", math.floor(r31 / 4) % 4 == 1)

local vh, vl, vf = Nib(27)
check("v5 vitals hp 80%", vh == 5 and vl == 0 and vf == 1)

local tmelee, thp, tcast = Nib(29)
check("v5 target out of melee", tmelee == 0)
check("v5 target hp band", thp == 12)   -- 80% -> floor(80*15/100) = 12
check("v5 target cast unknown without events", bit.band(tcast, 2) == 2)

-- player cast sensors (arg-blind unit events)
FireUnitEvent("player", "UNIT_SPELLCAST_START")
pcall(Update, Strip, 0.06)
check("v5 player casting encoded", (Nib(28)) == 1)
FireUnitEvent("player", "UNIT_SPELLCAST_STOP")
pcall(Update, Strip, 0.06)
check("v5 player cast cleared", (Nib(28)) == 0)
FireUnitEvent("player", "UNIT_SPELLCAST_CHANNEL_START")
pcall(Update, Strip, 0.06)
check("v5 player channeling encoded", (Nib(28)) == 2)
FireUnitEvent("player", "UNIT_SPELLCAST_CHANNEL_STOP")
pcall(Update, Strip, 0.06)

-- v2.1 watchdog: a missed STOP must degrade the latch to UNKNOWN (fail open),
-- never hold the rotation forever.
FireUnitEvent("player", "UNIT_SPELLCAST_START")
local realClock = GetTime
GetTime = function() return 1000.0 + 20 end
local watchdogState = MDB.GetCastState()
GetTime = realClock
check("cast watchdog degrades stale latch to UNKNOWN", watchdogState == 15)
check("cast watchdog clears the latch", MDB.GetCastState() == 0)
pcall(Update, Strip, 0.06)

-- target cast sensors
FireUnitEvent("target", "UNIT_SPELLCAST_START")
FireUnitEvent("target", "UNIT_SPELLCAST_INTERRUPTIBLE")
pcall(Update, Strip, 0.06)
local _, _, tflags = Nib(29)
check("v5 target casting + interruptible", bit.band(tflags, 1) == 1 and bit.band(tflags, 4) == 4)
FireUnitEvent("target", "UNIT_SPELLCAST_STOP")
pcall(Update, Strip, 0.06)
local _, _, tflags2 = Nib(29)
check("v5 target cast cleared", bit.band(tflags2, 1) == 0)

-- class/spec cell (WARRIOR=13, Arms=1)
local kh, kg, kf = Nib(33)
-- bit0 class/spec valid; bit2 EXT2 present is expected in a 3.0.0 frame.
check("v5 class/spec ids", kh == 13 and kg == 1 and bit.band(kf, 1) == 1)
check("v5 Ext2 present bit set", bit.band(kf, 4) == 4)

-- v2.7: no AuraUtil -> every self-buff probe degrades -> the block-valid bit
-- (cell33 B bit1) stays clear and no buff bit is a silent "active"; the
-- companion reads UNKNOWN and keeps its documented fail-open.
local _, _, buffFlags0 = Nib(33)
local br0, bg0 = Nib(32)
check("v2.7 buff probe block invalid without AuraUtil", bit.band(buffFlags0, 2) == 0)
check("v2.7 no buff bits without a probe", br0 == 0 and bg0 == 0)

-- A working probe marks the block valid and sets the per-slot bit (slot 1 is
-- the Main suggestion 185358), while class/spec validity is preserved.
AuraUtil = { FindAuraBySpellID = function(spellId)
  if spellId == 185358 then return { spellId = spellId } end
  return nil
end }
pcall(Update, Strip, 0.06)
local kh1, kg1, kf1 = Nib(33)
local br1 = Nib(32)
check("v2.7 buff probe block valid bit set", bit.band(kf1, 2) == 2)
check("v2.7 class/spec validity preserved", bit.band(kf1, 1) == 1 and kh1 == 13 and kg1 == 1)
check("v2.7 active self-buff encoded per slot", bit.band(br1, 1) == 1)

-- A throwing probe invalidates the whole block again (Unknown, not "absent").
AuraUtil = { FindAuraBySpellID = function() error("secret") end }
pcall(Update, Strip, 0.06)
local _, _, kf2 = Nib(33)
check("v2.7 failed probe clears the valid bit", bit.band(kf2, 2) == 0)
AuraUtil = nil

-- v2.1: target channel stop and target switch must clear the tracked cast
-- state (a stale "casting" bit would gate Spell Reflection on nothing).
FireUnitEvent("target", "UNIT_SPELLCAST_CHANNEL_START")
FireUnitEvent("target", "UNIT_SPELLCAST_INTERRUPTIBLE")
pcall(Update, Strip, 0.06)
local _, _, chflags = Nib(29)
check("v5 target channel + interruptible", bit.band(chflags, 1) == 1 and bit.band(chflags, 4) == 4)
FireUnitEvent("target", "UNIT_SPELLCAST_CHANNEL_STOP")
pcall(Update, Strip, 0.06)
local _, _, chflags2 = Nib(29)
check("v5 target channel stop clears cast bit", bit.band(chflags2, 1) == 0)
FireUnitEvent("target", "UNIT_SPELLCAST_START")
FirePlainEvent("PLAYER_TARGET_CHANGED")
pcall(Update, Strip, 0.06)
local _, _, tfchanged = Nib(29)
check("v5 target change clears cast state", bit.band(tfchanged, 1) == 0)

-- v2.1: a secret/failed CheckInteractDistance must leave melee UNKNOWN (2),
-- never collapse to a confirmed out-of-melee (0) that would fire a gap closer.
FirePlainEvent("PLAYER_TARGET_CHANGED")   -- clear the v3.6 per-target cache
CheckInteractDistance = function() return S(true) end
local secretMelee = select(1, MDB.GetTargetContext())
CheckInteractDistance = function() return false end
check("secret melee probe stays UNKNOWN", secretMelee == 2)

-- v3.6: one shared, event-gated melee probe. CheckInteractDistance is
-- #nocombat-restricted; ADDON_ACTION_BLOCKED is the breaker of last resort.
do
  local SavedCID = CheckInteractDistance
  local SavedICL = InCombatLockdown
  local SavedUAC = UnitAffectingCombat
  local SavedTime = GetTime
  local function ResetProbe() FirePlainEvent("PLAYER_TARGET_CHANGED") end

  -- (a) every gate blocks the call: server combat event, lockdown, and the
  -- post-combat 0.5 s cool-off all leave melee UNKNOWN (2) with 0 stub calls.
  local Calls = 0
  InCombatLockdown = function() return false end
  UnitAffectingCombat = function() return false end
  CheckInteractDistance = function() Calls = Calls + 1; return true end
  FirePlainEvent("PLAYER_REGEN_DISABLED")
  local mA1 = select(1, MDB.GetTargetContext())
  check("v3.6 (a1) regen-disabled -> 0 calls, melee UNKNOWN", mA1 == 2 and Calls == 0)
  FirePlainEvent("PLAYER_REGEN_ENABLED")
  Calls = 0
  local mA2 = select(1, MDB.GetTargetContext())
  check("v3.6 (a2) post-combat cool-off -> 0 calls", mA2 == 2 and Calls == 0)
  InCombatLockdown = function() return true end
  Calls = 0
  local mA3 = select(1, MDB.GetTargetContext())
  check("v3.6 (a3) InCombatLockdown -> 0 calls", mA3 == 2 and Calls == 0)
  InCombatLockdown = function() return false end

  -- past the cool-off: the probe resumes and calls exactly once.
  GetTime = function() return 1001.0 end
  ResetProbe()
  Calls = 0
  local mOk = select(1, MDB.GetTargetContext())
  check("v3.6 (a4) probe resumes after cool-off", mOk == 1 and Calls == 1)

  -- (b) UnitAffectingCombat=true while InCombatLockdown=false must not call.
  UnitAffectingCombat = function() return true end
  ResetProbe()
  Calls = 0
  local mB = select(1, MDB.GetTargetContext())
  check("v3.6 (b) UnitAffectingCombat blocks -> 0 calls, melee UNKNOWN",
    mB == 2 and Calls == 0)
  UnitAffectingCombat = function() return false end

  -- (c) 100 calls in 0.25 s hit the per-target cache: exactly one stub call.
  ResetProbe()
  Calls = 0
  CheckInteractDistance = function() Calls = Calls + 1; return false end
  for _ = 1, 100 do MDB.GetTargetContext() end
  check("v3.6 (c) 100 calls in 0.25s -> 1 stub call", Calls == 1)

  -- (d) a target change clears the cache, so the next probe calls again.
  Calls = 0
  CheckInteractDistance = function() Calls = Calls + 1; return true end
  ResetProbe()
  MDB.GetTargetContext()
  ResetProbe()
  MDB.GetTargetContext()
  check("v3.6 (d) PLAYER_TARGET_CHANGED clears the cache", Calls == 2)

  -- (d2) a client that does not expose InCombatLockdown is NOT refused by the
  -- missing API (intentional weaken, plan 2026-10-03): out of combat with the
  -- other gates open it still probes once. InCombatEv / UnitAffectingCombat
  -- remain the lockdown signals, and the BLOCKED breaker is the backstop.
  InCombatLockdown = nil
  UnitAffectingCombat = function() return false end
  CheckInteractDistance = function() Calls = Calls + 1; return true end
  GetTime = function() return 2007.0 end
  ResetProbe()
  Calls = 0
  local mD2 = select(1, MDB.GetTargetContext())
  check("v3.6 (d2) missing InCombatLockdown -> probe proceeds (intentional)",
    mD2 == 1 and Calls == 1)
  InCombatLockdown = function() return false end

  -- (d3) a secret-string GUID (type()=="string" but secret) is scrubbed to nil
  -- BEFORE the cache compare, so `MeleeCache.guid == Guid` never runs on it: a
  -- fresh probe is taken instead of a throw/cached read. Scrub is stubbed to
  -- treat one sentinel string as secret (Lua strings cannot carry a metatable).
  local RealScrub = scrubsecretvalues
  UnitGUID = function() return "guid-1" end
  CheckInteractDistance = function() Calls = Calls + 1; return true end
  GetTime = function() return 2008.0 end
  ResetProbe()
  MDB.GetTargetContext()            -- prime the cache with a plain GUID
  scrubsecretvalues = function(v)
    if v == "guid-secret" then return nil end
    return v
  end
  UnitGUID = function() return "guid-secret" end
  Calls = 0
  local okG, mG = pcall(MDB.GetTargetContext)
  check("v3.6 (d3) secret-string GUID: no throw, fresh probe not cached read",
    okG and select(1, mG) == 1 and Calls == 1)
  scrubsecretvalues = RealScrub
  UnitGUID = function() return "guid-1" end

  -- (e) the ADDON_ACTION_BLOCKED breaker: the FIRST hit is a finite 5 s
  -- backoff (the probe resumes after it), the SECOND disables the session.
  CheckInteractDistance = function() Calls = Calls + 1; return true end
  GetTime = function() return 2000.0 end
  FirePlainEvent("ADDON_ACTION_BLOCKED", "MaxDpsBridge", "blocked CheckInteractDistance()")
  ResetProbe()
  Calls = 0
  local mE1 = select(1, MDB.GetTargetContext())
  check("v3.6 (e1) 1st BLOCKED -> 5 s backoff, 0 calls", mE1 == 2 and Calls == 0)
  GetTime = function() return 2006.0 end   -- past the 5 s first-hit backoff
  ResetProbe()
  Calls = 0
  local mE2 = select(1, MDB.GetTargetContext())
  check("v3.6 (e2) probe resumes after first-hit backoff", mE2 == 1 and Calls == 1)
  FirePlainEvent("ADDON_ACTION_BLOCKED", "MaxDpsBridge", "blocked CheckInteractDistance()")
  GetTime = function() return 9999.0 end   -- far future: session-disabled
  ResetProbe()
  Calls = 0
  local mE3 = select(1, MDB.GetTargetContext())
  check("v3.6 (e3) 2nd BLOCKED disables probe for the session", mE3 == 2 and Calls == 0)

  -- (f) source-grep: the addon touches CheckInteractDistance from exactly ONE
  -- non-comment site across addon/*.lua. Comments and quoted strings are
  -- stripped first, so the ADDON_ACTION_BLOCKED name-match (a string literal)
  -- counts for nothing, and the type() existence guard is not a touch.
  local Files = {
    "addon/legacy/MaxDpsBridge/Bars.lua", "addon/legacy/MaxDpsBridge/Bridge.lua",
    "addon/legacy/MaxDpsBridge/Catalog.lua", "addon/legacy/MaxDpsBridge/Keymap.lua",
    "addon/legacy/MaxDpsBridge/Options.lua", "addon/legacy/MaxDpsBridge/Panel.lua",
    "addon/legacy/MaxDpsBridge/Reader.lua", "addon/legacy/MaxDpsBridge/Toggles.lua",
  }
  local Sites, Invokes = 0, 0
  for _, Path in ipairs(Files) do
    local Fh = io.open(Path, "r")
    assert(Fh, "source-grep cannot open " .. Path)
    local Text = Fh:read("a"); Fh:close()
    for L in Text:gmatch("[^\n]*") do
      local Code = L:gsub("%-%-.*$", "")
      Code = Code:gsub('"[^"]*"', '""'):gsub("'[^']*'", "''")
      if Code:find("CheckInteractDistance", 1, true) then
        if Code:find("pcall%s*%(%s*CheckInteractDistance") then
          Sites = Sites + 1; Invokes = Invokes + 1
        elseif not Code:find("type%s*%(%s*CheckInteractDistance") then
          Sites = Sites + 1   -- any other reference is an unguarded touch
        end
      end
    end
  end
  check("v3.6 (f) exactly one addon CheckInteractDistance touch site", Sites == 1)
  check("v3.6 (f2) that one site is the guarded pcall", Invokes == 1)

  GetTime = SavedTime
  CheckInteractDistance = SavedCID
  InCombatLockdown = SavedICL
  UnitAffectingCombat = SavedUAC
end

-- v2.1: the deprecated GetSpecialization chain is bypassed when the modern
-- C_SpecializationInfo namespace exists; a client with neither degrades to
-- class/spec 0 instead of throwing.
local realGetSpec = GetSpecialization
local realGetSpecInfo = GetSpecializationInfo
C_SpecializationInfo = {
  GetSpecialization = function() return 1 end,
  GetSpecializationInfo = function() return 72 end,   -- Fury
}
MDB.BeginTick()
local mcid, msid = MDB.GetClassSpec()
check("class/spec via C_SpecializationInfo (Fury=2)", mcid == 13 and msid == 2)
C_SpecializationInfo = nil
GetSpecialization = nil
GetSpecializationInfo = nil
MDB.BeginTick()
mcid, msid = MDB.GetClassSpec()
check("class/spec without spec APIs degrades to 0", mcid == 0 and msid == 0)
GetSpecialization = realGetSpec
GetSpecializationInfo = realGetSpecInfo

-- disabled path must still produce a decodable Paused frame with both checksums
MaxDpsBridgeDB.Enabled = false
pcall(Update, Strip, 0.06)
check("v5 disabled -> Paused state", (Nib(9)) == 2)
coreSum = 0
for i = 1, 9 do local r, g, b = Nib(i); coreSum = coreSum + r + g + b end
local _, disabledCs = Nib(10)
check("v5 disabled core checksum", (coreSum % 16) == disabledCs)
extSum = 0
for i = 11, 33 do local r, g, b = Nib(i); extSum = extSum + r + g + b end
local _, disabledExt = Nib(34)
check("v5 disabled ext checksum", (extSum % 16) == disabledExt)
MaxDpsBridgeDB.Enabled = true

-- ================= 10. defensive urgency (protocol v6) =================
-- HP-curve stages mirror vendor GlowDefensiveHPMidnight's control points:
-- <=0.3 Red(4), <0.5 Orange(3), <1.0 Yellow(2), >=1.0 White(1). Every probe
-- is pcall-contained and scrubbed, so a secret input is UNKNOWN(0), no throw.
local function SetHp (Hp, Max)
  UnitHealth = function() return Hp end
  UnitHealthMax = function() return Max or 100 end
end

SetHp(100)
check("v6 defensive urgency 100% HP is White", MDB.GetDefensiveUrgency(871) == 1)
SetHp(80)
check("v6 defensive urgency 80% HP is Yellow", MDB.GetDefensiveUrgency(871) == 2)
SetHp(45)
check("v6 defensive urgency 45% HP is Orange", MDB.GetDefensiveUrgency(871) == 3)
SetHp(20)
check("v6 defensive urgency 20% HP is Red", MDB.GetDefensiveUrgency(871) == 4)

UnitHealth = function() return S(50) end
local okSecretHp, secretHpUrg = pcall(MDB.GetDefensiveUrgency, 871)
check("v6 defensive urgency secret HP is Unknown (no throw)",
  okSecretHp and secretHpUrg == 0)

-- ================= 11. stagger urgency (protocol v6) =================
-- Reversed curve (the Purifying Brew special case): >=1.0 Red(4),
-- >=0.5 Orange(3), >=0.3 Yellow(2), else White(1). Secret -> UNKNOWN.
local function SetStagger (Stagger, Max)
  UnitStagger = function() return Stagger end
  UnitHealthMax = function() return Max or 100 end
end

SetStagger(120)
check("v6 stagger urgency 120/100 is Red", MDB.GetStaggerUrgency() == 4)
SetStagger(60)
check("v6 stagger urgency 60/100 is Orange", MDB.GetStaggerUrgency() == 3)
SetStagger(40)
check("v6 stagger urgency 40/100 is Yellow", MDB.GetStaggerUrgency() == 2)
SetStagger(10)
check("v6 stagger urgency 10/100 is White", MDB.GetStaggerUrgency() == 1)

UnitStagger = function() return S(120) end
local okSecretStagger, secretStaggerUrg = pcall(MDB.GetStaggerUrgency)
check("v6 stagger urgency secret is Unknown (no throw)",
  okSecretStagger and secretStaggerUrg == 0)

-- Purifying Brew (119582) reads the stagger curve; any other spell uses HP.
-- HP 100 (White) + readable stagger 90/100 (Orange): brew -> Orange, else White.
UnitStagger = function() return 90 end
UnitHealthMax = function() return 100 end
SetHp(100)
check("v6 Purifying Brew uses stagger curve (other spells use HP)",
  MDB.GetDefensiveUrgency(119582) == 3 and MDB.GetDefensiveUrgency(871) == 1)

-- When brew's stagger is unreadable the vendor falls back to the HP curve.
UnitStagger = function() return S(90) end
SetHp(100)
local okBrewFallback, brewFallback = pcall(MDB.GetDefensiveUrgency, 119582)
check("v6 Purifying Brew falls back to HP when stagger is secret (no throw)",
  okBrewFallback and brewFallback == 1)

-- ================= 12. defensive candidate / gap-fill =================
-- Category truth comes from upstream's static class tables; the catalog
-- gap-fill only fires below Red and only inside enableDefensives.
MaxDps.classCooldowns = {
  WARRIOR = { Arms = { defensive = { 871, 97462, 118038, 23920 }, offensive = {} } },
}
MaxDps.classInterrupts = { WARRIOR = { Arms = { 6552 } } }
MaxDps.Spells[871] = { { HotKey = { GetText = function() return "3" end } } }
MaxDps.db.global.enableDefensives = true
MDB._BindCache = {}

-- 1) MaxDps's own flagged + ready + bound defensive wins, source=false.
MaxDps.Flags = { [871] = true }
SetHp(20)
MDB.BeginTick()
local cand, isCatalog = MDB.GetDefensiveCandidate()
check("v6 candidate flagged+ready+bound returns id, source false",
  cand == 871 and isCatalog == false)

-- 2) No flagged defensive + Red HP + a ready+bound catalog entry => gap-fill.
MaxDps.Flags = {}
SetHp(20)
MDB.BeginTick()
cand, isCatalog = MDB.GetDefensiveCandidate()
check("v6 candidate no-flag + Red + catalog returns catalog id, source true",
  cand == 871 and isCatalog == true)

-- 3) Below Red the companion never substitutes its own defensive
-- (group frames: Solo ladder bands NOT armed).
MDB.SoloLadderBands = nil
SetHp(80)   -- Yellow
MDB.BeginTick()
cand, isCatalog = MDB.GetDefensiveCandidate()
check("v6 candidate no-flag + Yellow is nil, false",
  cand == nil and isCatalog == false)

-- 4) enableDefensives=false mutes the gap-fill even at Red.
MaxDps.db.global.enableDefensives = false
SetHp(20)
MDB.BeginTick()
cand, isCatalog = MDB.GetDefensiveCandidate()
check("v6 candidate enableDefensives=false is nil, false even at Red",
  cand == nil and isCatalog == false)
MaxDps.db.global.enableDefensives = true

-- 5) v3.3.0 Solo ladder: bands armed -> HP-banded offers below Red.
-- Above the minor band the ladder stays silent; arming/disarming never throws.
MDB.SoloLadderBands = { minor = 75, major = 50, immunity = 30 }
MDB.BeginTick()
local ladderOk = pcall(function()
  MaxDps.Flags = {}
  SetHp(80)   -- Yellow, above the 75 minor band
  MDB.BeginTick()
  local above = MDB.GetDefensiveCandidate()
  assert(above == nil, "solo ladder above minor band must stay silent")
end)
check("v3.3.0 solo ladder bands arm/disarm without throw", ladderOk)
MDB.SoloLadderBands = nil
MaxDps.Flags = {}

-- ================= 13. extras diagnostics =================
-- Warrior/Arms catalog (v3 racial fix regenerated): mobility {100,6544} = 2,
-- selfHeal {202168,34428} + 10 scope=Racial self-heals (Gift variants,
-- Regeneratin', H.O.L.O.) = 12,
-- defensive {871,97462,118038,23920,34428,202168} = 6.
MDB.BeginTick()
local extrasDiag = MDB.GetExtrasDiag()
check("v6 extras diag prints def count (Arms def=6)", extrasDiag:match("def=6") ~= nil)
check("v6 extras diag prints cls/spec and mob/heal counts",
  extrasDiag:match("cls=13 spec=1") ~= nil
  and extrasDiag:match("mob=2") ~= nil
  and extrasDiag:match("heal=12") ~= nil)

-- ================= 14. protocol v6 frame encode =================
-- Drive a real Bridge.Update with a flagged+ready+bound defensive and stub
-- UnitHealth (80% -> Yellow) + UnitStagger (60/100 -> Orange), then verify
-- the captured cell bytes: cell 31 G = HP urgency, cell 31 B bit0 = source,
-- cell 32 B = stagger urgency, and the extension checksum commits the new
-- nibbles (G31 + B31 + Bb are all non-zero in this frame).
UnitStagger = function() return 60 end
SetHp(80)
MaxDps.Flags = { [185358] = true, [871] = true }
MaxDps.Spell = 185358
MDB._BindCache = {}
C_Spell.GetSpellCooldown = function()
  return { startTime = 0, duration = 0, isEnabled = true, isActive = false, isOnGCD = false }
end
C_Spell.GetSpellCharges = function() return nil end

local function ExtSumThrough33 ()
  local Sum = 0
  for i = 11, 33 do local r, g, b = Nib(i); Sum = Sum + r + g + b end
  return Sum
end

MDB.ResetRotation()
local okV6, errV6 = pcall(Update, Strip, 0.06)
check("v6 encode Update: no throw", okV6)
if not okV6 then print("  v6 update error: " .. tostring(errV6)) end

check("v6 encode flagged defensive id round-trips", IdAt(15, 16) == 871)

local _, g31enc, b31enc = Nib(31)
check("v6 encode cell31 G carries HP urgency Yellow(2), B source bit clear",
  g31enc == 2 and bit.band(b31enc, 1) == 0)

local _, _, b32enc = Nib(32)
check("v6 encode cell32 B carries stagger urgency Orange(3)", b32enc == 3)

-- The new nibbles are committed: with cell31 G (HP urgency) and cell32 B
-- (stagger urgency) non-zero, recompute the extension sum over cells 11-33
-- and demand it equals the painted checksum. An ExtSum that dropped either
-- nibble would mismatch here.
local _, v6ExtCs = Nib(34)
check("v6 encode extension checksum commits HP + stagger urgency",
  g31enc ~= 0 and b32enc ~= 0 and (ExtSumThrough33() % 16) == v6ExtCs)

-- Catalog gap-fill in a real frame: no flagged defensive, Red HP, ready+bound
-- catalog entry => slot 3 encodes the catalog id, cell31 B bit0 is set and
-- cell31 G is Red; now ALL THREE v6 nibbles (G31, B31, Bb) are non-zero, so
-- the recomputed extension checksum proves B31 is committed too.
MaxDps.Flags = { [185358] = true }
SetHp(20)
MDB._BindCache = {}
local okV6Gap = pcall(Update, Strip, 0.06)
check("v6 encode catalog gap-fill Update: no throw", okV6Gap)
local _, g31gap, b31gap = Nib(31)
local _, _, b32gap = Nib(32)
local _, v6ExtGapCs = Nib(34)
check("v6 encode catalog gap-fill source bit + Red + checksum commits all 3",
  bit.band(b31gap, 1) == 1 and g31gap == 4 and b32gap ~= 0
  and b31gap ~= 0 and (ExtSumThrough33() % 16) == v6ExtGapCs)

-- =====================================================================
-- Workstream B / Ext2 (bridge 3.0.0): spell variants, variant binds,
-- extras selection, 40-cell encode, commands, EnsureEngine throttle.
-- Scoped in a function so its locals do not push the main chunk over
-- Lua's 200-local limit.
-- =====================================================================
local function RunExt2Tests ()

-- ================= 12b. offensive gap-fill + defensive Orange (r1) =====
-- MaxDps names no bound offensive -> the curated per-spec catalog list
-- supplies the first ready+bound entry; no wire source bit, so the bridge's
-- isGapFill return is diagnostic and the companion derives it by membership.
-- The whole path stays inside MaxDps's own enableCooldowns switch.
MaxDps.classCooldowns.WARRIOR.Arms.offensive = { 262161 }
MaxDps.Spells[107574] = { { HotKey = { GetText = function() return "V" end } } }
MaxDps.Spells[262161] = { { HotKey = { GetText = function() return "B" end } } }
MaxDps.db.global.enableCooldowns = true
MaxDps.Flags = {}
MDB._BindCache = {}
MDB.BeginTick()
local offCand, offGap = MDB.GetOffensiveCandidate()
check("r1 offensive candidate no-flag + catalog list -> id, source true",
  offCand == 107574 and offGap == true)

-- MaxDps's own flagged offensive wins, even though the id is also listed.
MaxDps.Flags = { [262161] = true }
MDB.BeginTick()
local offFlagged, offFlaggedGap = MDB.GetOffensiveCandidate()
check("r1 offensive flagged MaxDps candidate wins, source false",
  offFlagged == 262161 and offFlaggedGap == false)

-- enableCooldowns off mutes the whole offensive path.
MaxDps.Flags = {}
MaxDps.db.global.enableCooldowns = false
MDB.BeginTick()
local offMuted, offMutedGap = MDB.GetOffensiveCandidate()
check("r1 offensive gap-fill muted when enableCooldowns off",
  offMuted == nil and offMutedGap == false)
MaxDps.db.global.enableCooldowns = true

-- 2026-10-04 arms-fury-exec-fix: Ravager 228920 is absent from MaxDps's
-- static classCooldowns.offensive, so CategoryOf only classifies it through
-- MDB.FlagOffensiveExtra (MajorCooldowns.lua). A flagged Ravager is now the
-- Offensive candidate (a MaxDps-wire pick, not a gap-fill); as of 3.7.8 it is
-- also a valid MAIN pick (the denylist is gone).
MaxDps.classCooldowns.WARRIOR.Arms.offensive = {}
MaxDps.Spells[228920] = { { HotKey = { GetText = function() return "F" end } } }
MDCB._BindCache = {}
MaxDps.Flags = { [228920] = true }
MDCB.BeginTick()
local ravCand, ravGap = MDCB.GetOffensiveCandidate()
check("rv Ravager flagged -> offensive candidate 228920",
  ravCand == 228920 and ravGap == false)
check("rv Exp has no MajorCDDeny table", MDCB.MajorCDDeny == nil)
-- 3.7.8: a Ravager glow is no longer held out of Main — with Arms active and
-- the glow usable, GetMainSpellID encodes 228920 directly.
do
  local savedGlow = MaxDps.SpellsGlowing
  local savedSpell = MaxDps.Spell
  MaxDps.SpellsGlowing = { [228920] = 1 }
  MaxDps.Spell = nil
  MDCB.BeginTick()
  local okMain, rvMain = pcall(MDCB.GetMainSpellID)
  check("rv Ravager glow -> MAIN 228920 (no deny)", okMain and rvMain == 228920)
  MaxDps.SpellsGlowing = savedGlow
  MaxDps.Spell = savedSpell
end
MaxDps.Flags = {}

-- 2026-10-06 divine-toll: Divine Toll 375576 is likewise absent from MaxDps's
-- static Ret classCooldowns.offensive, so CategoryOf routes it through the same
-- MDB.FlagOffensiveExtra tail. A flagged Divine Toll on a Paladin Retribution
-- player is the Offensive candidate (MaxDps-wire, not gap-fill).
do
  local savedUnitClass = UnitClass
  local savedGetSpec = GetSpecialization
  local savedGetSpecInfo = GetSpecializationInfo
  MaxDps.idtospec[70] = "Retribution"
  MaxDps.classCooldowns.PALADIN = { Retribution = { defensive = {}, offensive = {} } }
  UnitClass = function() return "Paladin", "PALADIN", 2 end
  GetSpecialization = function() return 1 end
  GetSpecializationInfo = function() return 70 end
  MaxDps.Spells[375576] = { { HotKey = { GetText = function() return "G" end } } }
  MDCB._BindCache = {}
  MaxDps.Flags = { [375576] = true }
  MDCB.BeginTick()
  local dtCand, dtGap = MDCB.GetOffensiveCandidate()
  check("dt Divine Toll 375576 flagged -> offensive candidate",
    dtCand == 375576 and dtGap == false)
  check("dt FlagOffensiveExtra[375576] is offensive",
    MDCB.FlagOffensiveExtra ~= nil and MDCB.FlagOffensiveExtra[375576] == true)
  UnitClass = savedUnitClass
  GetSpecialization = savedGetSpec
  GetSpecializationInfo = savedGetSpecInfo
  MaxDps.idtospec[70] = nil
  MaxDps.Flags = {}
end

-- Defensive Orange: the short-CD list (defensiveMinor) fires at Orange while
-- the major list is only consulted at Red (majors still need Red in policy).
MaxDps.Spells[23920] = { { HotKey = { GetText = function() return "X" end } } }
MaxDps.Flags = {}
MDB._BindCache = {}
SetHp(45)   -- Orange
MDB.BeginTick()
local defOrange, defOrangeGap = MDB.GetDefensiveCandidate()
check("r1 defensive Orange short-CD gap-fill -> id, source true",
  defOrange == 23920 and defOrangeGap == true)
SetHp(80)   -- Yellow: never a gap-fill (only Red/Orange offer one)
MDB.BeginTick()
local defYellow = MDB.GetDefensiveCandidate()
check("r1 defensive Yellow still no gap-fill", defYellow == nil)

-- ================= 15. spell variants (B1) =================
FindBaseSpellByID = function(id) if id == 34428 then return 202168 end return id end
FindSpellOverrideByID = function(id) if id == 202168 then return 34428 end return nil end
GetOverrideSpell = function() return nil end

local function HasId (Tbl, Id)
  for _, v in ipairs(Tbl) do if v == Id then return true end end
  return false
end

local vAlias = MDB.SpellVariants(202168)
check("B1 SpellVariants input first", vAlias[1] == 202168)
check("B1 SpellVariants alias-table expansion", HasId(vAlias, 34428))
local vBase = MDB.SpellVariants(34428)
check("B1 SpellVariants base knows its override id", vBase[1] == 34428 and HasId(vBase, 202168))
local vDedupe = MDB.SpellVariants(202168)
local seen, dup = {}, false
for _, v in ipairs(vDedupe) do if seen[v] then dup = true end; seen[v] = true end
check("B1 SpellVariants de-duplicated", not dup)
local vPlain = MDB.SpellVariants(65000)
check("B1 SpellVariants base-only is a single id", #vPlain == 1 and vPlain[1] == 65000)
check("B1 SpellVariants secret arg: no throw", pcall(MDB.SpellVariants, S(202168)))
check("B1 SpellVariants non-positive -> empty", #MDB.SpellVariants(0) == 0)

C_SpellBook = { IsSpellKnown = function(id) return id == 34428 end }
check("B1 ActiveVariant picks the known variant", MDB.ActiveVariant(202168) == 34428)
C_SpellBook = { IsSpellKnown = function(id) return id == 202168 end }
check("B1 ActiveVariant keeps the known base", MDB.ActiveVariant(202168) == 202168)
C_SpellBook = nil
IsPlayerSpell = function() return true end
check("B1 ActiveVariant IsPlayerSpell fallback", MDB.ActiveVariant(65000) == 65000)
IsPlayerSpell = nil
C_SpellBook = { IsSpellKnown = function() error("secret compare") end }
local okAV, av = pcall(MDB.ActiveVariant, 202168)
check("B1 ActiveVariant secret probe: no throw, fail open", okAV and av == 202168)
C_SpellBook = nil

-- ================= 16. binds by any variant (B2) =================
MaxDps.Spells = {}
MaxDps.Flags = {}
MDB._BindCache = {}
GetActionInfo = function() return nil end
GetBindingKey = function() return nil end
GetMacroSpell = function() return nil end
C_Spell.GetSpellName = nil
GetSpellTexture = function() return nil end

-- (a) overlay HotKey carried under the variant id.
MaxDps.Spells[34428] = { { HotKey = { GetText = function() return "V" end } } }
MDB._BindCache = {}
local bVK = MDB.ResolveBinding(202168)
check("B2 overlay HotKey matches a variant id", bVK == 0x56)

-- (b) action-bar spell slot holds 34428, list asks 202168.
MaxDps.Spells = {}
MaxDps.Spells[34428] = {}
MDB._BindCache = {}
GetActionInfo = function(slot) if slot == 1 then return "spell", 34428 end return nil end
GetBindingKey = function(cmd) if cmd == "ACTIONBUTTON1" then return "1" end return nil end
local barVK = MDB.ResolveBinding(202168)
check("B2 action-bar scan matches a variant id", barVK == 0x31)

-- (c) a /cast macro resolving to a variant counts as bound.
GetActionInfo = function(slot) if slot == 2 then return "macro", 99 end return nil end
GetMacroSpell = function(id) if id == 99 then return "Victory Rush", nil, 34428 end return nil end
GetBindingKey = function(cmd) if cmd == "ACTIONBUTTON2" then return "2" end return nil end
MDB._BindCache = {}
local macroVK = MDB.ResolveBinding(202168)
check("B2 macro casting a variant is bound", macroVK == 0x32)

-- (d) texture fallback matches a variant's texture.
GetActionInfo = function() return nil end
MDB.BindingForTexture = function(tex) if tex == "tex34428" then return "3" end return nil end
GetSpellTexture = function(id) if id == 34428 then return "tex34428" end return nil end
MDB._BindCache = {}
local texVK = MDB.ResolveBinding(202168)
check("B2 texture fallback matches a variant texture", texVK == 0x33)
GetSpellTexture = function() return nil end
MDB.BindingForTexture = function() return nil end

-- ================= 17. extras selection (B3) =================
local realExtras = MDB.Extras["WARRIOR"]["Arms"]
realExtras.selfHeal = { 202168, 34428, 184364 }
MaxDps.Spells = {
  [34428] = { { HotKey = { GetText = function() return "H" end } } },
  [184364] = { { HotKey = { GetText = function() return "J" end } } },
}
C_SpellBook = { IsSpellKnown = function(id) return id == 34428 or id == 184364 end }
C_Spell.GetSpellCooldown = function()
  return { startTime = 0, duration = 0, isEnabled = true, isActive = false, isOnGCD = false }
end
C_Spell.GetSpellCharges = function() return nil end
MDB._BindCache = {}
MDB.BeginTick()
local cands = MDB.ExtraCandidates("selfHeal", 2)
check("B3 first two distinct ready+bound entries",
  #cands == 2 and cands[1] == 34428 and cands[2] == 184364)
check("B3 encodes the known variant, never the unknown list id", not HasId(cands, 202168))

C_SpellBook = { IsSpellKnown = function() return false end }
MDB._BindCache = {}
MDB.BeginTick()
local candsUnknown = MDB.ExtraCandidates("selfHeal", 2)
check("B3 no known variant falls back to the listed id", candsUnknown[1] == 202168)
C_SpellBook = { IsSpellKnown = function(id) return id == 34428 or id == 184364 end }

-- ================= 17b. R2 cooldown freshness across ticks =================
-- The bridge must re-read readiness EVERY tick: a cooldown that becomes ready
-- (including via a reset the bridge cannot see directly) is picked up on the
-- very next ExtraCandidates call, and a ready heal is dropped on the very next
-- tick after it goes on cooldown. No cross-tick caching of not-ready.
do
  local savedSpells = MaxDps.Spells
  local savedKnown = C_SpellBook
  local savedHeal = realExtras.selfHeal
  realExtras.selfHeal = { 202168 }
  MaxDps.Spells = { [34428] = { { HotKey = { GetText = function() return "H" end } } } }
  C_SpellBook = { IsSpellKnown = function(id) return id == 34428 end }

  local function HealTick (Remaining)
    if Remaining == nil then
      C_Spell.GetSpellCooldown = function()
        return { startTime = 0, duration = 0, isEnabled = true, isActive = false, isOnGCD = false }
      end
      RemainingBySpell[34428] = 0
    else
      C_Spell.GetSpellCooldown = function()
        return { isEnabled = true, isActive = true, isOnGCD = false }
      end
      RemainingBySpell[34428] = Remaining
    end
    MDB.BeginTick()
    local Candidates = MDB.ExtraCandidates("selfHeal", 1)
    return Candidates[1]
  end

  MDB._BindCache = {}
  check("R2 tick1 ready: heal encoded", HealTick(0) == 34428)
  check("R2 tick2 on-CD: heal absent within 1 tick", HealTick(30) == nil)
  check("R2 tick3 ready again: heal re-encoded within 1 tick", HealTick(0) == 34428)
  -- A second on-CD -> ready transition must behave identically (no sticky CD).
  check("R2 tick4 on-CD again: heal absent", HealTick(12) == nil)
  check("R2 tick5 reset: heal re-encoded", HealTick(0) == 34428)

  MaxDps.Spells = savedSpells
  C_SpellBook = savedKnown
  realExtras.selfHeal = savedHeal
end

-- ================= 18. Ext2 encode (B4/B5) =================
MaxDps.Flags = { [34428] = true }
MaxDps.Spell = 34428
MaxDps.SpellsGlowing = { [34428] = 1 }
MaxDps.IsSpellInRange = function() return 1 end
MaxDpsBridgeDB.HpCurve = true
MDB._BindCache = {}
UnitHealthPercent = function() return { GetRGBA = function() return 0.4, 0.6, 0, 1 end } end

MDB.ResetRotation()
local okEnc = pcall(Update, Strip, 0.06)
check("B4 Ext2 encode Update: no throw", okEnc)
check("B4 strip is 43 cells wide", type(Strip._size) == "table"
  and Strip._size[1] == 8 * 43 and Strip._size[2] == 8)

local _, _, kfEnc = Nib(33)
check("B4 cell33 B bit2 EXT2 present", bit.band(kfEnc, 4) == 4)
check("B4 cell33 B bit3 HP-curve active", bit.band(kfEnc, 8) == 8)
check("B4 cell35 vertex colour painted", Vertex[36] ~= nil and Vertex[36] == 0.4)

-- SelfHeal2 = second distinct candidate (184364, VK 0x4A).
local s2h, s2l, s2f = Nib(36)
check("B4 SelfHeal2 slot vk+flags (VK 0x4A)",
  bit.band(s2f, 8) == 8 and (s2h * 16 + s2l) == 0x4A)
check("B4 SelfHeal2 spell id round-trips", IdAt(37, 38) == 184364)
local _, _, cfEnc = Nib(28)
check("B4 cell28 B carries SelfHeal2 range tri-state", bit.band(cfEnc, 3) == 1)

local sum36 = 0
for i = 36, 38 do local r, g, b = Nib(i); sum36 = sum36 + r + g + b end
local e2r, e2g, e2b = Nib(39)
local _, hbEnc = Nib(9)
check("B4 cell39 Ext2 checksum over cells 36-38",
  e2r == 0 and (sum36 % 16) == e2g and e2b == hbEnc)

local coreSum, extSum = 0, 0
for i = 1, 9 do local r, g, b = Nib(i); coreSum = coreSum + r + g + b end
for i = 11, 33 do local r, g, b = Nib(i); extSum = extSum + r + g + b end
local _, coreCs = Nib(10)
local _, extCs = Nib(34)
check("B4 core checksum still covers cells 1-9", (coreSum % 16) == coreCs)
check("B4 ext checksum still covers cells 11-33", (extSum % 16) == extCs)

-- Secret HP sentinel: GetRGBA -> SetVertexColor, untouched, no compare.
local SecretColor = S(1.0)
UnitHealthPercent = function() return { GetRGBA = function() return SecretColor end } end
MDB._BindCache = {}
local okSecret, errSecret = pcall(Update, Strip, 0.06)
check("B5 secret HP curve: no throw", okSecret)
if not okSecret then print("  secret curve error: " .. tostring(errSecret)) end
check("B5 secret sentinel reached SetVertexColor untouched", rawequal(Vertex[36], SecretColor))
local _, _, kfSecret = Nib(33)
check("B5 secret HP curve still reports active", bit.band(kfSecret, 8) == 8)

-- bit2/bit3 truth table.
UnitHealthPercent = function() return { GetRGBA = function() return 0.4, 0.6, 0, 1 end } end
MaxDpsBridgeDB.HpCurve = false
pcall(Update, Strip, 0.06)
local _, _, kfOff = Nib(33)
local offTex = Textures[36]
check("B5 hpcurve off: bit3 clear", bit.band(kfOff, 8) == 0)
check("B5 hpcurve off: cell35 black", offTex and offTex[1] == 0 and offTex[2] == 0 and offTex[3] == 0)
check("B5 hpcurve off: bit2 still present", bit.band(kfOff, 4) == 4)
MaxDpsBridgeDB.HpCurve = true

UnitHealthPercent = nil
pcall(Update, Strip, 0.06)
local _, _, kfNone = Nib(33)
check("B5 missing colour API: bit3 clear", bit.band(kfNone, 8) == 0)

UnitHealthPercent = function() error("secret") end
local okThrow = pcall(Update, Strip, 0.06)
check("B5 throwing colour source: contained", okThrow)
local _, _, kfThrow = Nib(33)
check("B5 throwing colour source: bit3 clear", bit.band(kfThrow, 8) == 0)

UnitHealthPercent = function() return { GetRGBA = function() return 0.4, 0.6, 0, 1 end } end
pcall(Update, Strip, 0.06)
local _, _, kfOn = Nib(33)
check("B5 valid colour source: bit3 set", bit.band(kfOn, 8) == 8)

-- ================= 19. Ext2 commands (B6) =================
local Cmd = SlashCmdList["MAXDPSBRIDGE"]
MaxDpsBridgeDB.HpCurve = true
Cmd("hpcurve off")
check("B6 /mdb hpcurve off persists", MaxDpsBridgeDB.HpCurve == false)
Cmd("hpcurve on")
check("B6 /mdb hpcurve on persists", MaxDpsBridgeDB.HpCurve == true)

ClearChat()
Cmd("status")
local StatusLine = ChatLog[#ChatLog] or ""
check("B6 /mdb status shows ext2=1", StatusLine:find("ext2=1", 1, true) ~= nil)
check("B6 /mdb status shows hpcurve=on", StatusLine:find("hpcurve=on", 1, true) ~= nil)
check("B6 /mdb status shows sh2=", StatusLine:find("sh2=", 1, true) ~= nil)

ClearChat()
Cmd("heal")
local HealLine = ChatLog[#ChatLog] or ""
check("B6 /mdb heal prints variant/ready/key",
  HealLine:find("known=", 1, true) ~= nil
  and HealLine:find("ready=", 1, true) ~= nil
  and HealLine:find("key=", 1, true) ~= nil)

-- ================= 20. EnsureEngine throttle (B7) =================
local realEnsure = MDB.EnsureEngine
local ensureCalls = 0
MDB.EnsureEngine = function() ensureCalls = ensureCalls + 1 end
local realSpells = MaxDps.Spells
local realSpell = MaxDps.Spell
MaxDps.Spells = {}
for _ = 1, 20 do pcall(Update, Strip, 0.06) end
check("B7 EnsureEngine fires while MaxDps.Spells is empty", ensureCalls >= 1)
check("B7 EnsureEngine is throttled (not every tick)", ensureCalls <= 2)
local callsAfter = ensureCalls
MaxDps.Spells = realSpells
MaxDps.Spell = realSpell
pcall(Update, Strip, 0.06)
check("B7 EnsureEngine skipped once Spells is populated", ensureCalls == callsAfter)
MDB.EnsureEngine = realEnsure

-- restore the generated selfHeal list so later workstreams see the catalog.
realExtras.selfHeal = { 202168 }

end
RunExt2Tests()

-- =====================================================================
-- Workstream: in-game 13-toggle gates (bridge 3.3.0, Toggles.lua).
-- Scoped in a function so its locals do not push the main chunk over
-- Lua's 200-local limit. Addon-only restriction: OFF always wins.
-- =====================================================================
local function RunToggleTests ()
  local TG = MDB.Toggles
  local DB = MaxDpsBridgeDB
  local ORDER = {
    "Main", "Offensive", "Defensive", "Consumable", "Trinket", "Interrupt",
    "Mobility", "SelfHeal", "Solo", "OOC", "AutoTarget", "AutoInteract", "TTK",
  }
  local SLOT_KEYS = {
    "Main", "Offensive", "Defensive", "Consumable",
    "Trinket", "Interrupt", "Mobility", "SelfHeal",
  }
  local Ctx = { InCombat = true, HpPct = 80, Grouped = true }

  local function SetAll (On)
    for i = 1, #ORDER do DB.Toggles[ORDER[i]] = On end
  end

  -- --- API surface + defaults all ON (missing key = ON) ---
  local Keys = TG.Keys()
  check("T21 Keys() returns 13 canonical keys", #Keys == 13 and Keys[13] == "TTK")
  -- OVERLAY-WINS default: every key reads ON except OOC, which is fail-closed
  -- OFF while no companion is live and the player has not set it (2026-09-30).
  local Snap = TG.Snapshot()
  local SnapOn = true
  for i = 1, #ORDER do
    if ORDER[i] == "OOC" then
      if Snap.OOC ~= false then SnapOn = false end
    elseif Snap[ORDER[i]] ~= true then
      SnapOn = false
    end
  end
  check("T21 Snapshot() 12 ON + OOC fail-closed OFF by default", SnapOn)
  check("T21 OOC getter is fail-closed when unset", TG.IsOOC() == false)

  SetAll(true)
  local AllAllow = true
  for Slot = 1, 8 do
    MDB._LastBlank[Slot] = nil
    if TG.SlotAllowed(Slot, Ctx) ~= true or MDB._LastBlank[Slot] ~= nil then
      AllAllow = false
    end
  end
  check("T21 defaults ON: all 8 slots allowed, no blank reason", AllAllow)

  -- --- all 13 keys OFF: every slot denies and records its reason ---
  SetAll(false)
  local OffRead = true
  for i = 1, #ORDER do if TG.Get(ORDER[i]) ~= false then OffRead = false end end
  check("T21 all 13 keys OFF read back false", OffRead)
  local AllDeny = true
  for Slot = 1, 8 do
    MDB._LastBlank[Slot] = nil
    local Allowed = TG.SlotAllowed(Slot, Ctx)
    if Allowed ~= false or MDB._LastBlank[Slot] ~= (SLOT_KEYS[Slot] .. " off") then
      AllDeny = false
    end
  end
  check("T21 all 13 OFF: slots 1-8 denied with _LastBlank reason", AllDeny)
  check("T21 policy getters reflect OFF",
    TG.IsAutoTarget() == false and TG.IsAutoInteract() == false
    and TG.IsTTK() == false and TG.IsOOC() == false)

  -- --- each slot key OFF denies only its own slot ---
  local PerSlot = true
  for Slot = 1, 8 do
    SetAll(true)
    DB.Toggles[SLOT_KEYS[Slot]] = false
    if TG.SlotAllowed(Slot, Ctx) ~= false
      or MDB._LastBlank[Slot] ~= (SLOT_KEYS[Slot] .. " off") then
      PerSlot = false
    end
    for Other = 1, 8 do
      if Other ~= Slot and TG.SlotAllowed(Other, Ctx) ~= true then PerSlot = false end
    end
  end
  check("T21 each slot key OFF denies that slot only", PerSlot)

  -- --- missing DB / missing Toggles / missing key = allow ---
  local SavedDB = _G.MaxDpsBridgeDB
  _G.MaxDpsBridgeDB = nil
  check("T21 missing MaxDpsBridgeDB: Get ON, slot allowed",
    TG.Get("Main") == true and TG.SlotAllowed(1, nil) == true)
  _G.MaxDpsBridgeDB = {}
  check("T21 DB without Toggles table: all allowed",
    TG.Get("Solo") == true and TG.SlotAllowed(3, Ctx) == true)
  _G.MaxDpsBridgeDB = { Toggles = {} }
  check("T21 Toggles table without keys: missing = ON",
    TG.Get("Defensive") == true and TG.SlotAllowed(8, Ctx) == true)
  _G.MaxDpsBridgeDB = SavedDB

  -- --- nil / malformed ctx = allow ---
  SetAll(true)
  check("T21 nil ctx allows", TG.SlotAllowed(4, nil) == true)
  check("T21 empty ctx allows", TG.SlotAllowed(4, {}) == true)
  check("T21 non-table ctx allows", TG.SlotAllowed(4, "nope") == true)
  check("T21 unknown/nil slot allows",
    TG.SlotAllowed(99, Ctx) == true and TG.SlotAllowed(nil, Ctx) == true)

  -- --- secret HpPct / secret ctx fields: no throw + fail open ---
  DB.Toggles.Solo = false
  local SecretHp = S(20)
  local okSec, allowSec = pcall(TG.SlotAllowed, 3,
    { InCombat = true, HpPct = SecretHp, Grouped = false })
  check("T21 secret HpPct: no throw + allow (fail open)", okSec and allowSec == true)
  local okSecCtx, allowSecCtx = pcall(TG.SlotAllowed, 3,
    { InCombat = S(false), HpPct = 20, Grouped = false })
  check("T21 secret ctx fields: no throw + allow", okSecCtx and allowSecCtx == true)
  DB.Toggles.Solo = true

  -- --- OOC OFF: strict InCombat==false blanks 1-8; nil/true allow ---
  DB.Toggles.OOC = false
  local OocDeny = true
  for Slot = 1, 8 do
    if TG.SlotAllowed(Slot, { InCombat = false, HpPct = 80, Grouped = true }) ~= false then
      OocDeny = false
    end
  end
  check("T21 OOC OFF + InCombat=false blanks slots 1-8", OocDeny)
  local OocNil = true
  for Slot = 1, 8 do
    if TG.SlotAllowed(Slot, { InCombat = nil, HpPct = 80, Grouped = true }) ~= true then
      OocNil = false
    end
  end
  check("T21 OOC OFF + unknown InCombat allows 1-8", OocNil)
  local OocTrue = true
  for Slot = 1, 8 do
    if TG.SlotAllowed(Slot, { InCombat = true, HpPct = 80, Grouped = true }) ~= true then
      OocTrue = false
    end
  end
  check("T21 OOC OFF + InCombat=true allows 1-8", OocTrue)
  DB.Toggles.OOC = true

  -- --- Solo OFF + ungrouped blanks 3/8; emergency/unknown HP allows ---
  DB.Toggles.Solo = false
  local function SoloCtx (Hp) return { InCombat = true, HpPct = Hp, Grouped = false } end
  MDB._LastBlank[3], MDB._LastBlank[8] = nil, nil
  local d3 = TG.SlotAllowed(3, SoloCtx(80))
  check("T21 Solo OFF ungrouped: slot3 blank + reason",
    d3 == false and MDB._LastBlank[3] == "solo not grouped")
  local d8 = TG.SlotAllowed(8, SoloCtx(80))
  check("T21 Solo OFF ungrouped: slot8 blank + reason",
    d8 == false and MDB._LastBlank[8] == "solo not grouped")
  check("T21 Solo OFF: emergency HpPct 35 allows 3/8",
    TG.SlotAllowed(3, SoloCtx(35)) == true and TG.SlotAllowed(8, SoloCtx(35)) == true)
  check("T21 Solo OFF: HpPct 36 blanks 3/8",
    TG.SlotAllowed(3, SoloCtx(36)) == false and TG.SlotAllowed(8, SoloCtx(36)) == false)
  check("T21 Solo OFF: unknown HP allows 3/8",
    TG.SlotAllowed(3, SoloCtx(nil)) == true and TG.SlotAllowed(8, SoloCtx(nil)) == true)
  check("T21 Solo OFF: non-survival slot 1 allowed", TG.SlotAllowed(1, SoloCtx(80)) == true)
  check("T21 Solo OFF: grouped allows 3/8",
    TG.SlotAllowed(3, Ctx) == true and TG.SlotAllowed(8, Ctx) == true)
  check("T21 Solo OFF: unknown Grouped allows 3/8",
    TG.SlotAllowed(3, { InCombat = true, HpPct = 80, Grouped = nil }) == true
    and TG.SlotAllowed(8, { InCombat = true, HpPct = 80, Grouped = nil }) == true)
  DB.Toggles.Solo = true

  -- --- Set / Flip / Label persistence ---
  local SavedTgl = DB.Toggles
  DB.Toggles = {}
  check("T21 Label canonical + fallback",
    TG.Label("selfheal") == "Self-heal" and TG.Label("Bogus") == "Bogus")
  TG.Set("selfheal", false)
  check("T21 Set stores canonical key false",
    DB.Toggles.SelfHeal == false and TG.Get("SelfHeal") == false)
  check("T21 Flip toggles back ON", TG.Flip("SelfHeal") == true and TG.Get("SelfHeal") == true)
  DB.Toggles = SavedTgl
  SetAll(true)

  -- --- TTK gate in a real frame (WriteTarget): ON keeps the computed band,
  -- OFF forces UNKNOWN (15) while MeleeFlag/CastFlags survive. ---
  MaxDps.Spells = { [185358] = { { HotKey = { GetText = function() return "1" end } } } }
  MaxDps.Flags = { [185358] = true }
  MaxDps.Spell = 185358
  MDB._BindCache = {}
  C_Spell.GetSpellCooldown = function()
    return { startTime = 0, duration = 0, isEnabled = true, isActive = false, isOnGCD = false }
  end
  C_Spell.GetSpellCharges = function() return nil end
  UnitHealth = function() return 80 end
  UnitHealthMax = function() return 100 end
  pcall(Update, Strip, 0.06)
  local _, bandOn = Nib(29)
  check("T21 TTK ON keeps target hp band (80% -> 12)", bandOn == 12)
  DB.Toggles.TTK = false
  pcall(Update, Strip, 0.06)
  local _, bandOff = Nib(29)
  check("T21 TTK OFF forces target hp band UNKNOWN (15)", bandOff == 15)
  DB.Toggles.TTK = true
end
RunToggleTests()

-- =====================================================================
-- Workstream: v3.5 Ext3 app mask + multi-candidate slot rotation.
-- =====================================================================
local function RunExt3Tests ()
  local TG = MDB.Toggles
  local DB = MaxDpsBridgeDB

  local function MaskAt ()
    local r0, g0, b0 = Nib(40)
    local r1 = Nib(41)
    return (bit.band(r1, 3) * 4096) + (b0 * 256) + (g0 * 16) + r0
  end

  -- Clean Warrior/Arms frame: Main + Charge + Heroic Leap bound, no CDs.
  MaxDps.Spells = {
    [185358] = { { HotKey = { GetText = function() return "1" end } } },
    [100]    = { { HotKey = { GetText = function() return "2" end } } },
    [6544]   = { { HotKey = { GetText = function() return "T" end } } },
  }
  MaxDps.Flags = { [185358] = true }
  MaxDps.Spell = 185358
  MaxDps.SpellsGlowing = { [185358] = 1 }
  MaxDps.NextSpell = nil
  MaxDps.classCooldowns = { WARRIOR = { Arms = { defensive = {}, offensive = {} } } }
  MaxDps.classInterrupts = { WARRIOR = { Arms = {} } }
  MaxDps.db.global.enableCooldowns = true
  MaxDps.db.global.enableDefensives = true
  C_Spell.GetSpellCooldown = function()
    return { startTime = 0, duration = 0, isEnabled = true, isActive = false, isOnGCD = false }
  end
  C_Spell.GetSpellCharges = function() return nil end
  UnitHealth = function() return 80 end
  UnitHealthMax = function() return 100 end
  UnitAffectingCombat = function() return true end
  MDB._BindCache = {}
  DB.Toggles = {}
  DB.AppMask, DB.AppEpoch, DB.AppBlocked = nil, nil, nil
  MaxDpsBridgeDB.RotationDwell = 3

  -- --- Ext3 presence + local effective mask (epoch 0) ---
  MDB.ResetRotation()
  local okEx3 = pcall(Update, Strip, 0.06)
  check("T35 Ext3 encode Update: no throw", okEx3)
  local _, _, c28b = Nib(28)
  check("T35 cell28 B bit2 Ext3 present", bit.band(c28b, 4) == 4)
  -- OVERLAY-WINS: with no explicit in-game value, the Ext3 mirror is all ON
  -- except OOC, which is fail-closed OFF (hold) while no companion default
  -- exists. 0x3FFF minus bit9.
  check("T35 epoch 0 publishes the fail-closed effective mask (0x3DFF)",
    MaskAt() == 0x3DFF)
  -- An explicit in-game OOC ON flips the mirror bit back on immediately.
  DB.Toggles.OOC = true
  pcall(Update, Strip, 0.06)
  check("T35 explicit in-game OOC ON republishes 0x3FFF", MaskAt() == 0x3FFF)
  DB.Toggles.OOC = nil
  pcall(Update, Strip, 0.06)
  local m40r, m40g, m40b = Nib(40)
  local m41r, m41g, m41b = Nib(41)
  local _, e3cs, e3commit = Nib(42)
  local _, hb3 = Nib(9)
  check("T35 Ext3 checksum over cells 40-41",
    ((m40r + m40g + m40b + m41r + m41g + m41b) % 16) == e3cs)
  check("T35 Ext3 commit == heartbeat", e3commit == hb3)
  check("T35 epoch 0 encodes epoch nibble 0", m41g == 0)

  -- --- overlay-wins while the app epoch is live ---
  TG.SetAppMask(0x0001, 5, 0)
  check("T35 AppControlled at epoch 5",
    TG.AppControlled() == true and TG.AppEpoch() == 5)
  check("T35 app bit ON for Main / unset Mobility falls back to the app OFF",
    TG.Get("Main") == true and TG.Get("Mobility") == false)
  MDB._LastBlank[7] = nil
  check("T35 unset key takes the app default (slot 7 denied, reason local)",
    TG.SlotAllowed(7, { InCombat = true, HpPct = 80, Grouped = true }) == false
    and MDB._LastBlank[7] == "Mobility off")
  DB.Toggles.Mobility = true   -- explicit in-game ON is authoritative
  check("T35 explicit in-game ON beats the app OFF (overlay-wins)",
    TG.SlotAllowed(7, { InCombat = true, HpPct = 80, Grouped = true }) == true)
  check("T35 EffectiveMask reflects the in-game overlay (app Main + local Mobility)",
    TG.EffectiveMask() == 0x0041)
  check("T35 Conflict detects the explicit local vs app disagreement",
    TG.Conflict("Mobility") == true)
  -- An explicit in-game OFF must beat an app ON (the old masked veto is gone).
  DB.Toggles.Main = false
  check("T35 explicit in-game OFF beats the app ON",
    TG.Get("Main") == false
    and TG.SlotAllowed(1, { InCombat = true, HpPct = 80, Grouped = true }) == false)
  DB.Toggles.Main = true

  -- Wire while the app epoch is live: the mirror is the EFFECTIVE mask, and
  -- the explicit in-game Mobility ON keeps the mobility slot alive.
  MDB.ResetRotation()
  pcall(Update, Strip, 0.06)
  check("T35 wire publishes the effective mask 0x0041", MaskAt() == 0x0041)
  local f41r, f41g, f41b = Nib(41)
  check("T35 wire publishes epoch 5 + blocked 0",
    f41g == 5 and f41b == 0 and bit.band(f41r, 3) == 0)
  local _, _, slot7flags = Nib(7)
  check("T35 explicit in-game ON keeps the mobility slot alive",
    bit.band(slot7flags, 8) == 8)
  local _, _, slot1flags = Nib(1)
  check("T35 app default ON keeps the main slot alive", bit.band(slot1flags, 8) == 8)

  -- --- /mdb mask refusals raise blocked bit1, valid push clears it ---
  local Cmd = SlashCmdList["MAXDPSBRIDGE"]
  ClearChat()
  Cmd("mask 0002 6")   -- in combat -> refused
  check("T35 /mdb mask refused in combat, blocks bit1",
    TG.AppEpoch() == 5 and TG.AppMask() == 0x0001
    and bit.band(TG.AppBlocked(), 2) == 2)
  UnitAffectingCombat = function() return false end
  Cmd("mask 0002 6")
  check("T35 /mdb mask applies out of combat",
    TG.AppMask() == 0x0002 and TG.AppEpoch() == 6 and TG.AppBlocked() == 0)
  Cmd("mask ffff 8")   -- mask > 0x3FFF -> bad checksum
  check("T35 /mdb mask bad checksum refused, blocks bit1",
    TG.AppEpoch() == 6 and bit.band(TG.AppBlocked(), 2) == 2)
  Cmd("mask 0002 99")  -- epoch out of range
  check("T35 /mdb mask bad epoch refused",
    TG.AppEpoch() == 6 and bit.band(TG.AppBlocked(), 2) == 2)
  Cmd("mask 0002 0")
  check("T35 /mdb mask valid clears the blocked bit",
    TG.AppEpoch() == 0 and TG.AppMask() == 0x0002 and TG.AppBlocked() == 0)
  DB.AppEpoch = 3
  DB.Toggles.Mobility = true
  ClearChat()
  Cmd("mobility off")
  check("T35 /mdb key writes the in-game value even while app epoch live",
    DB.Toggles.Mobility == false
    and (ChatLog[#ChatLog] or ""):find("off", 1, true) ~= nil)
  Cmd("mobility on")
  check("T35 /mdb key flips the in-game value back on",
    DB.Toggles.Mobility == true
    and (ChatLog[#ChatLog] or ""):find("on", 1, true) ~= nil)
  DB.AppEpoch = 0
  DB.AppMask = nil
  -- The rotation tests below run out of combat (UnitAffectingCombat=false), so
  -- the fail-closed OOC gate needs an explicit in-game ON to allow slots.
  DB.Toggles.OOC = true

  -- --- rotation: cap 4 + never-automatic filter ---
  local SavedMobility = MDB.Extras.WARRIOR.Arms.mobility
  local SavedSpells = MaxDps.Spells
  MDB.Extras.WARRIOR.Arms.mobility = { 100, 6544, 2983, 102401, 33786 }
  MaxDps.Spells = {
    [100]    = { { HotKey = { GetText = function() return "2" end } } },
    [6544]   = { { HotKey = { GetText = function() return "T" end } } },
    [2983]   = { { HotKey = { GetText = function() return "S" end } } },
    [102401] = { { HotKey = { GetText = function() return "W" end } } },
    [33786]  = { { HotKey = { GetText = function() return "C" end } } },
  }
  MDB._BindCache = {}
  MDB.BeginTick()
  local Pool = MDB.RotationCandidates(7, 10)
  check("T35 rotation pool is capped at 4", #Pool == 4)
  local HasNever = false
  for i = 1, #Pool do if Pool[i] == 33786 then HasNever = true end end
  check("T35 rotation pool excludes never-automatic ids", HasNever == false)
  check("T35 IsNeverAutomatic knows Cyclone", MDB.IsNeverAutomatic(33786) == true)
  check("T35 IsNeverAutomatic fails open on a plain id",
    MDB.IsNeverAutomatic(6544) == false)

  -- --- rotation: independent per-slot clocks + weighted advance ---
  MDB.Extras.WARRIOR.Arms.mobility = SavedMobility
  MaxDps.Spells = SavedSpells
  MDB._BindCache = {}
  MaxDpsBridgeDB.RotationDwell = 1
  MDB.ResetRotation()
  local Seen = {}
  for _ = 1, 4 do
    pcall(Update, Strip, 0.06)
    local Id = IdAt(23, 24)
    Seen[Id] = (Seen[Id] or 0) + 1
  end
  check("T35 weighted rotation offers both mobility candidates",
    (Seen[100] or 0) >= 1 and (Seen[6544] or 0) >= 1)
  check("T35 rotation advances every tick at dwell 1",
    ((Seen[100] or 0) + (Seen[6544] or 0)) == 4)
  check("T35 per-slot clocks are independent (4 rotating slots tracked)",
    type(MDB._RotCounts) == "table" and (MDB._RotCounts[3] or -1) >= 0
    and (MDB._RotCounts[6] or -1) >= 0 and (MDB._RotCounts[7] or -1) >= 0
    and (MDB._RotCounts[8] or -1) >= 0)
  ClearChat()
  Cmd("status")
  local Line = ChatLog[#ChatLog] or ""
  check("T35 /mdb status reports ext3=1", Line:find("ext3=1", 1, true) ~= nil)
  check("T35 /mdb status reports per-slot candidate counts",
    Line:find("rot=3:", 1, true) ~= nil and Line:find("dwell=1", 1, true) ~= nil)

  -- --- dwell command ---
  Cmd("dwell 5")
  check("T35 /mdb dwell persists", MaxDpsBridgeDB.RotationDwell == 5)
  Cmd("dwell 0")
  check("T35 /mdb dwell clamps to 1", MaxDpsBridgeDB.RotationDwell == 1)
  MaxDpsBridgeDB.RotationDwell = 3
  MDB.ResetRotation()

  -- --- user pause + fail-closed OOC hold (2026-09-30) ---
  -- `/mdb off` is sticky: even a re-enabled DB holds with state 2 (Paused).
  Cmd("off")
  check("T35 /mdb off sets the sticky user pause",
    MDB.IsUserPaused() == true and MaxDpsBridgeDB.UserPaused == true)
  MaxDpsBridgeDB.Enabled = true   -- simulate another writer forcing it on
  pcall(Update, Strip, 0.06)
  check("T35 user pause outranks Enabled (Paused frame)", (Nib(9)) == 2)
  local PauseSlots = true
  for i = 1, 8 do
    local _, _, F = Nib(i)
    if bit.band(F, 8) == 8 then PauseSlots = false end
  end
  check("T35 user pause leaves every slot empty", PauseSlots)
  Cmd("on")
  check("T35 /mdb on clears the sticky user pause",
    MDB.IsUserPaused() == false and MaxDpsBridgeDB.UserPaused == false)

  -- An explicit in-game OOC OFF while out of combat holds: every slot blank
  -- and the Ext3 OOC mirror bit (bit 9) clear, so the companion also holds.
  DB.Toggles.OOC = false
  pcall(Update, Strip, 0.06)
  local OocHold = true
  for i = 1, 8 do
    local _, _, F = Nib(i)
    if bit.band(F, 8) == 8 then OocHold = false end
  end
  check("T35 in-game OOC OFF blanks every slot out of combat", OocHold)
  check("T35 OOC OFF clears the Ext3 OOC mirror bit 9",
    bit.band(MaskAt(), 512) == 0)
  DB.Toggles.OOC = true

  UnitAffectingCombat = nil
end
RunExt3Tests()

-- =====================================================================
-- Workstream: v3.5 CC fix (Storm Bolt 107570 / Shockwave 46968).
-- =====================================================================
local function RunCcFixTests ()
  local TG = MDB.Toggles
  local DB = MaxDpsBridgeDB

  -- ---- IsCC delegates Get("CC") (local + app mask + fail-open) ----
  local SavedToggles = DB.Toggles
  local SavedMask, SavedEpoch = DB.AppMask, DB.AppEpoch
  local SavedCc = SavedToggles and SavedToggles.CC
  DB.Toggles = {}
  DB.AppMask, DB.AppEpoch = nil, nil
  check("CCF IsCC missing CC = ON (fail open)", TG.IsCC() == true)
  DB.Toggles.CC = false
  check("CCF IsCC local CC off = false", TG.IsCC() == false)
  check("CCF Get('CC') resolves the local key", TG.Get("CC") == false)
  DB.Toggles.CC = true
  check("CCF local EffectiveMask sets CC bit 13",
    bit.band(TG.EffectiveMask(), 8192) == 8192)
  DB.Toggles.CC = false
  check("CCF local EffectiveMask clears CC bit 13 when CC off",
    bit.band(TG.EffectiveMask(), 8192) == 0)

  -- OVERLAY-WINS: an explicit in-game CC OFF beats an app bit13 ON...
  TG.SetAppMask(8192, 5, 0)
  check("CCF explicit in-game CC off beats the app ON (overlay-wins)",
    DB.Toggles.CC == false and TG.IsCC() == false)
  -- ...and an unset CC key falls back to the live app bit (defaults).
  DB.Toggles.CC = nil
  check("CCF unset CC falls back to the app bit13 ON", TG.IsCC() == true)
  TG.SetAppMask(0, 5, 0)
  check("CCF unset CC falls back to the app bit13 OFF", TG.IsCC() == false)
  DB.AppMask, DB.AppEpoch = SavedMask, SavedEpoch

  -- ---- IsBossTarget fail-open + classification ----
  local SavedCI, SavedUL = UnitClassification, UnitLevel
  UnitClassification = function() return "worldboss" end
  check("CCF IsBossTarget worldboss -> true", MDB.IsBossTarget() == true)
  UnitClassification = function() error("secret") end
  UnitLevel = function() error("secret") end
  check("CCF IsBossTarget pcall failure -> fail open (false)",
    MDB.IsBossTarget() == false)
  UnitClassification = function() return "normal" end
  UnitLevel = function() return 72 end
  check("CCF IsBossTarget normal/72 -> false", MDB.IsBossTarget() == false)
  UnitClassification, UnitLevel = SavedCI, SavedUL

  -- ---- slot-6 CC pool skips a boss, includes otherwise ----
  MaxDps.Spells = {
    [5246]   = { { HotKey = { GetText = function() return "5" end } } },
    [107570] = { { HotKey = { GetText = function() return "6" end } } },
    [46968]  = { { HotKey = { GetText = function() return "7" end } } },
  }
  MaxDps.classInterrupts = { WARRIOR = { Arms = {} } }
  MDB._BindCache = {}
  DB.Toggles = DB.Toggles or {}
  DB.Toggles.CC = true

  local Pool = MDB.RotationCandidates(6, 4)
  local HasCc = false
  for i = 1, #Pool do
    if Pool[i] == 5246 or Pool[i] == 107570 or Pool[i] == 46968 then HasCc = true end
  end
  check("CCF slot-6 pool includes curated CC when not a boss", HasCc)

  UnitClassification = function() return "worldboss" end
  MDB._BindCache = {}
  local BossPool = MDB.RotationCandidates(6, 4)
  check("CCF slot-6 pool empty for a worldboss target",
    type(BossPool) == "table" and #BossPool == 0)
  UnitClassification, UnitLevel = SavedCI, SavedUL

  DB.Toggles = SavedToggles
  if SavedToggles then SavedToggles.CC = SavedCc end
  MaxDps.classInterrupts = nil
end
RunCcFixTests()

-- =====================================================================
-- Q1 no-downtime Main (plan 2026-10-03-no-downtime-main): sorted glowing
-- scan gated by MainUsable (only plain usable==false AND noPower==true
-- vetoes), then the per-spec MainFallback filler, then nil. Scoped so its
-- locals do not push the main chunk over Lua's 200-local limit.
-- =====================================================================
local function RunMainFallbackTests ()
  -- CURRENT client (Exp) owns the per-spec fillers pinned here (Arms/Ret were
  -- added 2026-10-04/06); stable is frozen with Fury only.
  local MDB = MDCB
  local savedGlow = MaxDps.SpellsGlowing
  local savedSpell = MaxDps.Spell
  local savedUsable = C_Spell.IsSpellUsable
  local savedGetSpecInfo = GetSpecializationInfo
  local savedCooldown = C_Spell.GetSpellCooldown
  local savedCharges = C_Spell.GetSpellCharges

  -- Cooldowns stay out of the way: MainUsable is power-only, so a plain
  -- no-cooldown shape keeps every id "castable" except power-starved ones.
  C_Spell.GetSpellCooldown = function()
    return { startTime = 0, duration = 0, isEnabled = true, isActive = false, isOnGCD = false }
  end
  C_Spell.GetSpellCharges = function() return nil end
  GetSpecializationInfo = function() return 72 end   -- Fury (specID 72)

  local function Usability (NoPower)
    C_Spell.IsSpellUsable = function(id)
      if NoPower[id] then return false, true end
      return true, false
    end
  end

  -- (1) Rampage glow no-power + Bloodthirst glow usable -> Bloodthirst.
  Usability({ [184367] = true })
  MaxDps.SpellsGlowing = { [184367] = 1, [23881] = 1 }
  MaxDps.Spell = nil
  MDB.BeginTick()
  local ok1, id1 = pcall(MDB.GetMainSpellID)
  check("MF rampage no-power + bloodthirst glow -> Bloodthirst", ok1 and id1 == 23881)

  -- (2) A power-starved glow with a LOWER id is skipped, not returned:
  --     synthetic 100 starved + 185358 usable -> 185358 (old lowest-id code
  --     would have returned 100 and stranded the slot).
  Usability({ [100] = true })
  MaxDps.SpellsGlowing = { [100] = 1, [185358] = 1 }
  MaxDps.Spell = nil
  MDB.BeginTick()
  local ok2, id2 = pcall(MDB.GetMainSpellID)
  check("MF lower-id power-starved glow skipped, next usable wins", ok2 and id2 == 185358)

  -- (3) Only a power-starved glow -> Fury fallback filler (Bloodthirst).
  Usability({ [184367] = true })
  MaxDps.SpellsGlowing = { [184367] = 1 }
  MaxDps.Spell = nil
  MDB.BeginTick()
  local ok3, id3 = pcall(MDB.GetMainSpellID)
  check("MF all glows power-starved -> Fury fallback filler", ok3 and id3 == 23881)

  -- (4) Fallback filler itself power-starved -> nil (never press blind).
  Usability({ [184367] = true, [23881] = true })
  MaxDps.SpellsGlowing = { [184367] = 1 }
  MaxDps.Spell = nil
  MDB.BeginTick()
  local ok4, id4 = pcall(MDB.GetMainSpellID)
  check("MF fallback filler power-starved -> nil", ok4 and id4 == nil)

  -- (5) Unverified spec (Protection 73) has no filler -> nil. (Arms now has
  --     a filler as of 2026-10-04; see (9) below.)
  Usability({ [184367] = true })
  GetSpecializationInfo = function() return 73 end   -- Protection
  MaxDps.SpellsGlowing = { [184367] = 1 }
  MaxDps.Spell = nil
  MDB.BeginTick()
  local ok5, id5 = pcall(MDB.GetMainSpellID)
  check("MF unverified spec (Protection) -> nil", ok5 and id5 == nil)
  GetSpecializationInfo = function() return 72 end

  -- (6) secret / nil / throwing usability probes -> fail OPEN (glow kept),
  --     never a stripped slot on an unobservable verdict.
  MaxDps.SpellsGlowing = { [185358] = 1 }
  MaxDps.Spell = nil
  C_Spell.IsSpellUsable = function() return S(false), S(true) end
  MDB.BeginTick()
  local ok6, id6 = pcall(MDB.GetMainSpellID)
  check("MF secret usability -> fail open, glow kept", ok6 and id6 == 185358)
  C_Spell.IsSpellUsable = function() return nil end
  MDB.BeginTick()
  local ok7, id7 = pcall(MDB.GetMainSpellID)
  check("MF nil usability -> fail open, glow kept", ok7 and id7 == 185358)
  C_Spell.IsSpellUsable = function() error("secret probe throw") end
  MDB.BeginTick()
  local ok8, id8 = pcall(MDB.GetMainSpellID)
  check("MF throwing usability -> fail open, glow kept", ok8 and id8 == 185358)

  -- (7) 3.7.8: former-deny ids are ordinary Main candidates now. With both
  --     190319 and 185358 glowing and usable, the ascending sort returns the
  --     lower usable id 185358; a lone 190319 glow returns 190319.
  Usability({})
  MaxDps.SpellsGlowing = { [190319] = 1, [185358] = 1 }
  MaxDps.Spell = nil
  MDB.BeginTick()
  local ok9, id9 = pcall(MDB.GetMainSpellID)
  check("MF lower usable glow wins (no deny)", ok9 and id9 == 185358)
  MaxDps.SpellsGlowing = { [190319] = 1 }                 -- former deny id only
  MDB.BeginTick()
  local ok10, id10 = pcall(MDB.GetMainSpellID)
  check("MF former-deny-only glow -> encoded as MAIN", ok10 and id10 == 190319)

  -- (8) a former-deny MaxDps.Spell (no glow at all) is encoded as MAIN too:
  --     3.7.8 trusts the engine's named pick when it is usable.
  MaxDps.SpellsGlowing = nil
  MaxDps.Spell = 190319                                   -- former deny id only
  MDB.BeginTick()
  local ok11, id11 = pcall(MDB.GetMainSpellID)
  check("MF former-deny Spell -> encoded as MAIN", ok11 and id11 == 190319)

  -- (9) 2026-10-04 arms-fury-exec-fix: Arms has a filler and Colossus Smash
  --     167105 is a normal Main candidate (former deny id).
  GetSpecializationInfo = function() return 71 end   -- Arms
  Usability({})
  -- (a) a plain Colossus Smash glow is encoded as Main (deny removed).
  MaxDps.SpellsGlowing = { [167105] = 1 }
  MaxDps.Spell = nil
  MDB.BeginTick()
  local ok12, id12 = pcall(MDB.GetMainSpellID)
  check("MF Arms glow 167105 (Colossus Smash) no longer denied", ok12 and id12 == 167105)

  -- (b) a former-deny glow is encoded as MAIN, not routed to the Arms filler.
  MaxDps.SpellsGlowing = { [190319] = 1 }   -- former Mage major deny id
  MDB.BeginTick()
  local ok13, id13 = pcall(MDB.GetMainSpellID)
  check("MF Arms former-deny glow -> MAIN 190319", ok13 and id13 == 190319)

  -- (c) Ravager 228920 is a former deny id; a usable glow now encodes as MAIN.
  MaxDps.SpellsGlowing = { [228920] = 1 }
  MDB.BeginTick()
  local ok14, id14 = pcall(MDB.GetMainSpellID)
  check("MF Arms Ravager-only glow -> MAIN 228920", ok14 and id14 == 228920)
  GetSpecializationInfo = function() return 72 end

  MaxDps.SpellsGlowing = savedGlow
  MaxDps.Spell = savedSpell
  C_Spell.IsSpellUsable = savedUsable
  GetSpecializationInfo = savedGetSpecInfo
  C_Spell.GetSpellCooldown = savedCooldown
  C_Spell.GetSpellCharges = savedCharges
  MDB.BeginTick()
end
RunMainFallbackTests()

-- =====================================================================
-- Ext4 (v4.0.x) hotbar press map: HotbarWatch + Bridge cells 43-50.
-- Loads the Exp Bars/Toggles/HotbarWatch/Bridge modules into MDCB and boots
-- the Exp strip so the encode is pinned end-to-end (harness style). Scoped in
-- a function so its locals do not push the main chunk over Lua's 200-local
-- limit.
-- =====================================================================
local function RunExt4Tests ()
  -- ---- load + boot the Exp encoder ----
  local NFramesBefore = #AllFrames
  assert(loadfile("addon/MaxDpsCompanionBridge/Bars.lua"))("MaxDpsCompanionBridge", MDCB)
  assert(loadfile("addon/MaxDpsCompanionBridge/Toggles.lua"))("MaxDpsCompanionBridge", MDCB)
  assert(loadfile("addon/MaxDpsCompanionBridge/HotbarWatch.lua"))("MaxDpsCompanionBridge", MDCB)
  assert(loadfile("addon/MaxDpsCompanionBridge/Bridge.lua"))("MaxDpsCompanionBridge", MDCB)
  assert(type(MDCB.HotbarWatch) == "table", "HotbarWatch module missing")

  local LoaderExp
  for i = NFramesBefore + 1, #AllFrames do
    local f = AllFrames[i]
    if f._events and f._events["ADDON_LOADED"] and f._scripts and f._scripts.OnEvent then
      LoaderExp = f
    end
  end
  assert(LoaderExp, "Exp loader frame missing")
  local TexBase = TextureCount        -- Exp cells get ids TexBase+1 .. TexBase+51
  LoaderExp._scripts.OnEvent(LoaderExp, "ADDON_LOADED", "MaxDpsCompanionBridge")
  local StripExp = _G.MaxDpsCompanionBridge_Block
  assert(StripExp, "Exp strip frame missing")
  local UpdateExp = StripExp._scripts.OnUpdate
  assert(type(UpdateExp) == "function", "Exp OnUpdate not wired")
  check("E4 Exp strip is 51 cells wide", type(StripExp._size) == "table"
    and StripExp._size[1] == 8 * 51 and StripExp._size[2] == 8)
  check("E4 Exp strip created 51 textures", TextureCount == TexBase + 51)

  local function XNib (i)
    local c = Textures[TexBase + 1 + i]
    if not c then return 0, 0, 0 end
    return math.floor(c[1] * 15 + 0.5), math.floor(c[2] * 15 + 0.5), math.floor(c[3] * 15 + 0.5)
  end

  -- ---- stub the action-bar APIs the walk reads ----
  local Actions, Binds = {}, {}
  GetActionInfo = function (slot)
    local a = Actions[slot]
    if not a then return nil end
    return a[1], a[2]
  end
  GetBindingKey = function (cmd) return Binds[cmd] end
  GetMacroSpell = function () return nil end
  GetMacroItem = function () return nil end
  IsUsableItem = nil
  GetItemCooldown = nil
  ChatEdit_GetActiveWindow = nil
  GetCurrentKeyBoardFocus = nil

  local HW = MDCB.HotbarWatch

  -- ---- (1) EMPTY map -> one EMPTY frame + presence bit ----
  Actions, Binds = {}, {}
  HW.Rebuild()
  local G1 = HW.Gen()
  local OkE = pcall(UpdateExp, StripExp, 0.06)
  check("E4 EMPTY update: no throw", OkE)
  local h43r, h43g, h43b = XNib(43)
  check("E4 EMPTY header gen / seq 0 / LAST+EMPTY",
    h43r == G1 and h43g == 0 and h43b == 3)
  local e44r, e44g, e44b = XNib(44)
  local e49r = XNib(49)
  check("E4 EMPTY entries are pad zeros",
    e44r == 0 and e44g == 0 and e44b == 0 and e49r == 0)
  local c28r, c28g, c28b = XNib(28)
  check("E4 cell 28 B bit3 Ext4 present", bit.band(c28b, 8) == 8)
  check("E4 cell 28 B bit2 Ext3 still present", bit.band(c28b, 4) == 4)
  local hb = select(2, XNib(9))
  local cs50r, cs50g, cs50b = XNib(50)
  check("E4 EMPTY commit = sum(cells 43-49) / heartbeat",
    cs50r == 0 and cs50g == ((G1 + 0 + 3) % 16) and cs50b == hb)

  -- ---- (2) one bound spell: entry packing + ready bit ----
  Actions = { [1] = { "spell", 185358 } }
  Binds = { ["ACTIONBUTTON1"] = "1" }
  HW.Rebuild()
  local G2 = HW.Gen()
  pcall(UpdateExp, StripExp, 0.06)
  local a1r, a1g, a1b = XNib(44)
  check("E4 entry A stroke (vk 0x31, valid flag)", a1r == 3 and a1g == 1 and a1b == 8)
  local a2r, a2g, a2b = XNib(45)
  check("E4 entry A id hi nibbles + ready bit", a2r == 4 and a2g == 2 and a2b == 13)
  local a3r, a3g, a3b = XNib(46)
  check("E4 entry A id lo nibbles", a3r == 4 and a3g == 0 and a3b == 14)
  local h2r, h2g, h2b = XNib(43)
  check("E4 one-entry header gen / seq 0 / LAST", h2r == G2 and h2g == 0 and h2b == 1)
  check("E4 entry B pad with one entry", XNib(47) == 0)
  local _, cs2g = XNib(50)
  check("E4 one-entry commit sum",
    cs2g == ((G2 + 0 + 1 + (3 + 1 + 8) + (4 + 2 + 13) + (4 + 0 + 14)) % 16))

  -- ---- (2b) modifiers pack into the stroke flags ----
  Binds = { ["ACTIONBUTTON1"] = "SHIFT-1" }
  HW.Rebuild()
  pcall(UpdateExp, StripExp, 0.06)
  local _, _, mf = XNib(44)
  check("E4 modifier packs into stroke flags (shift 1 | valid 8)", mf == 9)

  -- ---- (3) movement skip / macro->spell / item ----
  Actions = {
    [1] = { "spell", 185358 },
    [2] = { "spell", 100 },        -- bound to W (movement) -> skipped
    [3] = { "macro", 55 },
    [4] = { "item", 5512 },
  }
  Binds = {
    ["ACTIONBUTTON1"] = "1", ["ACTIONBUTTON2"] = "W",
    ["ACTIONBUTTON3"] = "3", ["ACTIONBUTTON4"] = "4",
  }
  GetMacroSpell = function (id) if id == 55 then return "x", nil, 871 end return nil end
  IsUsableItem = function () return true end
  GetItemCooldown = function () return 0, 0, true end
  HW.Rebuild()
  local E3 = HW.Entries()
  check("E4 movement-bound slot skipped (3 entries)", HW.Count() == 3)
  check("E4 macro resolves to its spell id", E3[2] and E3[2].Id == 871 and E3[2].IsItem == false)
  check("E4 item entry is flagged IsItem", E3[3] and E3[3].Id == 5512 and E3[3].IsItem == true)
  check("E4 movement slot is not watched", Binds["ACTIONBUTTON2"] == "W" and E3[2].Slot == 3)

  pcall(UpdateExp, StripExp, 0.06)       -- seq 0: e1 = 185358, e2 = 871
  local b1r, b1g, b1b = XNib(47)
  local b2r, b2g, b2b = XNib(48)
  local b3r, b3g, b3b = XNib(49)
  check("E4 entry B stroke (vk 0x33 / valid)", b1r == 3 and b1g == 3 and b1b == 8)
  check("E4 entry B id nibbles (871) + ready",
    b2r == 4 and b2g == 0 and b2b == 0 and b3r == 3 and b3g == 6 and b3b == 7)

  pcall(UpdateExp, StripExp, 0.06)       -- held tick 2 (still seq 0)
  pcall(UpdateExp, StripExp, 0.06)       -- advance to seq 1
  local _, h3g, h3b = XNib(43)
  check("E4 seq advances after 2 held paint ticks", h3g == 1 and h3b == 1)
  local i1r, i1g, i1b = XNib(44)
  local i2r, i2g, i2b = XNib(45)
  local i3r, i3g, i3b = XNib(46)
  check("E4 item entry stroke (vk 0x34 / valid)", i1r == 3 and i1g == 4 and i1b == 8)
  check("E4 item IsItem + ready bits", i2r == 12 and i2g == 0 and i2b == 1)
  check("E4 item id nibbles (5512)", i3r == 5 and i3g == 8 and i3b == 8)

  -- ---- (4) id >= 2^22 skipped + logged once ----
  Actions = { [1] = { "spell", 2 ^ 22 } }
  Binds = { ["ACTIONBUTTON1"] = "1" }
  ClearChat()
  HW.Rebuild()
  check("E4 id >= 2^22 skipped", HW.Count() == 0)
  local CapLine = ChatLog[#ChatLog] or ""
  check("E4 id cap logs once", CapLine:find("2^22", 1, true) ~= nil)
  ClearChat()
  HW.Rebuild()
  check("E4 id cap never logs twice", #ChatLog == 0)

  -- ---- (5) 32-entry cap + overflow logged once ----
  Actions = {}
  for slot = 1, 40 do Actions[slot] = { "spell", 100000 + slot } end
  GetBindingKey = function () return "F1" end   -- every command bound -> watchable
  ClearChat()
  HW.Rebuild()
  check("E4 map capped at 32 entries (bar order wins)", HW.Count() == 32)
  local OvrLine = ChatLog[#ChatLog] or ""
  check("E4 overflow logged once", OvrLine:find("overflow", 1, true) ~= nil)

  -- ---- (6) typing veto bit (cell 41 R bit2) ----
  Actions = { [1] = { "spell", 185358 } }
  GetBindingKey = function (cmd) if cmd == "ACTIONBUTTON1" then return "1" end return nil end
  HW.Rebuild()
  ChatEdit_GetActiveWindow = function () return { IsShown = function () return true end } end
  pcall(UpdateExp, StripExp, 0.06)
  local t41r = XNib(41)
  check("E4 typing bit set while chat focused", bit.band(t41r, 4) == 4)
  local m40r, m40g, m40b = XNib(40)
  local m41r, m41g, m41b = XNib(41)
  local _, e3g = XNib(42)
  check("E4 Ext3 checksum commits the typing bit",
    ((m40r + m40g + m40b + m41r + m41g + m41b) % 16) == e3g)
  ChatEdit_GetActiveWindow = nil
  GetCurrentKeyBoardFocus = function () return { IsShown = function () return true end } end
  pcall(UpdateExp, StripExp, 0.06)
  check("E4 typing bit via GetCurrentKeyBoardFocus", bit.band(XNib(41), 4) == 4)
  GetCurrentKeyBoardFocus = nil
  pcall(UpdateExp, StripExp, 0.06)
  check("E4 typing bit clear with no focus", bit.band(XNib(41), 4) == 0)

  -- ---- (7) /mcb hotbar diagnostic ----
  ClearChat()
  MDCB.HandleCommand("hotbar")
  local HotLine = ChatLog[#ChatLog] or ""
  check("E4 /mcb hotbar prints header + entry",
    HotLine:find("gen=", 1, true) ~= nil
    and HotLine:find("slot=1", 1, true) ~= nil
    and HotLine:find("binding=1", 1, true) ~= nil
    and HotLine:find("ready=", 1, true) ~= nil)

  -- ---- (8) 2026-10-11 hotfix: a DENIED slot repaints EMPTY ----
  -- Regression: ReadSlot returned zeros for a denied slot WITHOUT repainting,
  -- leaving the previous candidate's pixels while the core checksum used the
  -- zeros -> every frame failed the companion's core checksum ("No pixel
  -- block" while the strip visibly rendered; live report 2026-10-11).
  Actions = { [1] = { "spell", 185358 } }
  GetActionInfo = function (slot) local a = Actions[slot]; if not a then return nil end; return a[1], a[2] end
  GetBindingKey = function (cmd) if cmd == "ACTIONBUTTON1" then return "1" end return nil end
  MaxDps.SpellsGlowing = { [185358] = 1 }
  MaxDps.Spell = nil
  MDCB.Toggles.Set("OOC", true)   -- neutralise the OOC master gate for this pin
  MDCB.Toggles.Set("Main", true)
  HW.Rebuild()
  -- Earlier sections cached a binding MISS while the bars were stubbed empty;
  -- wipe it like a bar-change event would, then re-resolve against the stubs.
  if MDCB.InvalidateBindings then MDCB.InvalidateBindings() end
  local OkA = pcall(UpdateExp, StripExp, 0.06)
  local a1r, a1g, a1b = XNib(1)
  check("E4x allowed Main paints a valid cell 1", OkA and bit.band(a1b, 8) == 8)

  MDCB.Toggles.Set("Offensive", true)
  MDCB.Toggles.Set("Defensive", true)
  MDCB.Toggles.Set("SelfHeal", true)
  MDCB.Toggles.Set("Main", false)
  -- v4.0.4: Main is the MASTER switch. All 8 slots are denied by the master
  -- gate (not by their own pill), and the other stored toggles are untouched.
  local MasterAll, MasterReason = true, 0
  for Slot = 1, 8 do
    if MDCB.Toggles.SlotAllowed(Slot, { InCombat = true, HpPct = 80, Grouped = true }) ~= false then
      MasterAll = false
    end
    if MDCB._LastBlank[Slot] == "main off (master switch)" then MasterReason = MasterReason + 1 end
  end
  local MasterSnap = MDCB.Toggles.Snapshot()
  check("E4x Main OFF denies all 8 slots with the master reason", MasterAll and MasterReason == 8)
  check("E4x Main OFF leaves every other toggle physically ON",
    MasterSnap.Main == false and MasterSnap.Offensive == true
    and MasterSnap.Defensive == true and MasterSnap.SelfHeal == true
    and MasterSnap.Trinket == true)
  local OkD = pcall(UpdateExp, StripExp, 0.06)
  local d1r, d1g, d1b = XNib(1)
  check("E4x denied Main repaints EMPTY (0,0,0)", OkD and d1r == 0 and d1g == 0 and d1b == 0)

  local PaintedSum = 0
  for i = 1, 9 do
    local r, g, b = XNib(i)
    PaintedSum = PaintedSum + r + g + b
  end
  local _, CoreChecksum = XNib(10)
  check("E4x core checksum matches the PAINTED cells after a deny", CoreChecksum == (PaintedSum % 16))
  MDCB.Toggles.Set("Main", true)
  check("E4x Main ON restores slot 1 without rewriting other toggles",
    MDCB.Toggles.SlotAllowed(1, { InCombat = true, HpPct = 80, Grouped = true }) == true
    and MDCB.Toggles.Get("Offensive") == true and MDCB.Toggles.Get("Defensive") == true)

  -- restore the action-bar stubs
  GetActionInfo = function () return nil end
  GetBindingKey = function () return nil end
  GetMacroSpell = function () return nil end
  IsUsableItem = nil
  GetItemCooldown = nil
end
RunExt4Tests()


-- =====================================================================
-- Wave A / A1 "Paragon" (plan 2026-10-17): Exp addon hot-path remediation.
-- Every pin below fails against the pre-A1 code (proved by swapping the
-- HEAD file back in for a red run; see the wave report).
-- Scoped in a function so its locals do not push the main chunk over
-- Lua's 200-local limit.
-- =====================================================================
local function RunParagonTests ()
  local TG = MDCB.Toggles
  local DB = MaxDpsCompanionBridgeDB
  local HW = MDCB.HotbarWatch
  local StripExp = _G.MaxDpsCompanionBridge_Block
  local UpdateExp = StripExp and StripExp._scripts and StripExp._scripts.OnUpdate
  assert(type(UpdateExp) == "function", "Exp OnUpdate missing for A1 pins")

  local SavedClock = GetTime
  local Now = 5000.0
  GetTime = function () return Now end

  -- Drain anything queued before this section (the boot EnsureHooks retry).
  runDeferred()

  -- Shared action-bar state for the A1.7 / A1.8 / A1.11 pins.
  local Actions, Binds = {}, {}
  GetActionInfo = function (slot)
    local A = Actions[slot]
    if not A then return nil end
    return A[1], A[2]
  end
  GetBindingKey = function (cmd) return Binds[cmd] end
  GetMacroSpell = function () return nil end
  GetMacroItem = function () return nil end
  IsUsableItem = nil
  GetItemCooldown = nil

  -- ---- A1.1: SPELL_UPDATE_COOLDOWN must not touch the bind caches -----
  do
    MDCB._BindCache = { [185358] = { Miss = true } }
    local Cache = MDCB._BindCache
    local Rev = MDCB._BindRevision or 0
    local Wipes = 0
    local SavedInvalidate = MDCB.InvalidateBindings
    MDCB.InvalidateBindings = function () Wipes = Wipes + 1; SavedInvalidate() end
    local OkMode, Mode = pcall(MDCB.BarsEvent, "SPELL_UPDATE_COOLDOWN")
    check("A1.1 cooldown event is revision-only", OkMode and Mode == "revision")
    check("A1.1 cooldown event bumps _BindRevision", (MDCB._BindRevision or 0) == Rev + 1)
    check("A1.1 cooldown event keeps the cached Miss (same table)",
      MDCB._BindCache == Cache and Cache[185358] ~= nil)
    check("A1.1 cooldown event runs no full invalidation", Wipes == 0)
    local OkFlush, Pend = pcall(MDCB.BarsFlush)
    check("A1.1 cooldown event leaves nothing deferred", OkFlush and Pend == false)
    MDCB.InvalidateBindings = SavedInvalidate
  end
  do
    -- The live path: the Listener -> C_Timer -> BarsFlush chain.
    MDCB._BindCache = { [185358] = { Miss = true } }
    local Cache = MDCB._BindCache
    FirePlainEvent("SPELL_UPDATE_COOLDOWN")
    runDeferred()
    check("A1.1 cooldown EVENT keeps the cached Miss",
      MDCB._BindCache == Cache and Cache[185358] ~= nil)
  end

  -- ---- A1.1: full path stays deferred and coalesces to 1 / 0.25 s -----
  do
    MDCB._BindCache = { [185358] = { Miss = true } }
    local Rev = MDCB._BindRevision or 0
    FirePlainEvent("UPDATE_BINDINGS")
    check("A1.1 bar event is DEFERRED (bars repaint late)", MDCB._BindCache[185358] ~= nil)
    runDeferred()
    check("A1.1 deferred bar event wipes the VK cache", MDCB._BindCache[185358] == nil)
    check("A1.1 deferred bar event bumps _BindRevision", (MDCB._BindRevision or 0) == Rev + 1)
    local Rev2 = MDCB._BindRevision or 0
    MDCB._BindCache = { [185358] = { Miss = true } }
    FirePlainEvent("UPDATE_BINDINGS")
    FirePlainEvent("UPDATE_MACROS")
    FirePlainEvent("ACTIONBAR_SLOT_CHANGED")
    runDeferred()
    check("A1.1 an event burst coalesces to ONE wipe", (MDCB._BindRevision or 0) == Rev2 + 1)
    check("A1.1 the coalesced wipe still happened", MDCB._BindCache[185358] == nil)
    Now = Now + 1.0
    FirePlainEvent("UPDATE_MACROS")
    runDeferred()
    check("A1.1 next event after the window wipes again", (MDCB._BindRevision or 0) == Rev2 + 2)
  end

  -- ---- A1.4: a parse miss no longer writes a global -------------------
  do
    MDCB.LastMissBinding = nil
    local VK = MDCB.ParseBinding("BUTTON9")
    check("A1.4 unknown token is still a nil VK", VK == nil)
    check("A1.4 parse miss no longer writes MDCB.LastMissBinding", MDCB.LastMissBinding == nil)
  end

  -- ---- A1.2: FrameKey uses the DECISION EDGES, not the raw HP ---------
  local SavedHealth, SavedHealthMax = UnitHealth, UnitHealthMax
  local SavedStagger, SavedInGroup, SavedUACombat = UnitStagger, IsInGroup, UnitAffectingCombat
  local SavedSpell = MaxDps.Spell
  IsInGroup = function () return true end
  UnitAffectingCombat = function () return true end
  UnitStagger = nil
  local function HWIsDirty ()
    local Ok, V = pcall(HW.IsDirty)
    return Ok and V == true
  end
  local function Fk (Hp, Combat, Grouped)
    if Hp ~= nil then
      UnitHealth = function () return Hp end
      UnitHealthMax = function () return 100 end
    end
    local Ok, K = pcall(MDCB.FrameKey, { InCombat = Combat, Grouped = Grouped })
    if not Ok or type(K) ~= "number" then return -1 end
    return K
  end
  do
    local K = Fk(60, true, true)
    check("A1.2 FrameKey is a numeric hash", K > 1000)
    check("A1.2 FrameKey is stable for HP inside a band",
      Fk(60, true, true) == Fk(61, true, true)
      and Fk(80, true, true) == Fk(99, true, true)
      and Fk(20, true, true) == Fk(29, true, true)
      and Fk(36, true, true) == Fk(49, true, true))
    check("A1.2 30/31 (Red -> Orange) crosses a band", Fk(30, true, true) ~= Fk(31, true, true))
    check("A1.2 35/36 (Solo emergency) crosses a band", Fk(35, true, true) ~= Fk(36, true, true))
    check("A1.2 49/50 (Orange -> Yellow + solo major) crosses a band",
      Fk(49, true, true) ~= Fk(50, true, true))
    check("A1.2 75/76 (solo minor) crosses a band", Fk(75, true, true) ~= Fk(76, true, true))
    check("A1.2 99/100 (Yellow -> White) crosses a band", Fk(99, true, true) ~= Fk(100, true, true))
    check("A1.2 Ctx.InCombat is an input", Fk(60, true, true) ~= Fk(60, false, true))
    check("A1.2 Ctx.Grouped is an input", Fk(60, true, true) ~= Fk(60, true, false))
    check("A1.2 unknown Ctx fields differ from known ones", Fk(60, true, true) ~= Fk(60, nil, true))
    MDCB.SoloLadderBands = { minor = 75, major = 50, immunity = 30 }
    local Armed = Fk(60, true, true)
    MDCB.SoloLadderBands = nil
    check("A1.2 SoloLadderBands arming is an input", Armed ~= Fk(60, true, true))
    UnitStagger = function () return 10 end
    local White = Fk(60, true, true)
    UnitStagger = function () return 60 end
    check("A1.2 stagger stage is an input", White ~= Fk(60, true, true))
    UnitStagger = nil
    MaxDps.Spell = S(1)
    local S1 = Fk(60, true, true)
    MaxDps.Spell = S(2)
    check("A1.2 a secret value hashes to a FIXED key (no throw)",
      S1 > 1000 and S1 == Fk(60, true, true))
    MaxDps.Spell = SavedSpell
  end
  do
    -- Live path: HP drift inside a band keeps the cached scan; crossing 50 %
    -- still repins (DefPin -> RotationTick -> SlotCache invalidated).
    UnitAffectingCombat = function () return false end
    TG.Set("OOC", true)
    DB.Enabled = true
    MDCB.SetUserPaused(false)
    UnitHealthMax = function () return 100 end
    UnitHealth = function () return 60 end
    pcall(UpdateExp, StripExp, 0.06)          -- primes the cache
    local N0 = MDCB._SlotScans or 0
    UnitHealth = function () return 61 end
    pcall(UpdateExp, StripExp, 0.06)
    local N1 = MDCB._SlotScans or 0
    check("A1.2 HP drift inside a band reuses the cached scan", N1 == N0)
    UnitHealth = function () return 49 end
    pcall(UpdateExp, StripExp, 0.06)
    check("A1.2 crossing 50 % repins the scan", (MDCB._SlotScans or 0) == N1 + 1)
  end

  -- ---- A1.3: per-tick memo -------------------------------------------
  do
    local SavedBase, SavedOver, SavedGetOv = FindBaseSpellByID, FindSpellOverrideByID, GetOverrideSpell
    local Probes = 0
    FindBaseSpellByID = function (id) Probes = Probes + 1; return id end
    FindSpellOverrideByID = function (id) Probes = Probes + 1; return nil end
    GetOverrideSpell = function (id) Probes = Probes + 1; return nil end
    pcall(MDCB.BeginTick)
    local First = MDCB.SpellVariants(185358)
    local P1 = Probes
    local Second = MDCB.SpellVariants(185358)
    check("A1.3 SpellVariants evaluates once per tick", P1 > 0 and Probes == P1)
    check("A1.3 SpellVariants memo returns the same array", First == Second)
    pcall(MDCB.EndTick)
    MDCB.SpellVariants(185358)
    check("A1.3 SpellVariants outside a tick reads live again", Probes > P1)
    FindBaseSpellByID, FindSpellOverrideByID, GetOverrideSpell = SavedBase, SavedOver, SavedGetOv
  end
  do
    local SavedCD = C_Spell.GetSpellCooldown
    local Reads = 0
    C_Spell.GetSpellCooldown = function ()
      Reads = Reads + 1
      return { startTime = 0, duration = 0, isEnabled = true, isActive = false, isOnGCD = false }
    end
    pcall(MDCB.BeginTick)
    MDCB.IsSpellReady(185358); MDCB.IsSpellReady(185358); MDCB.IsSpellReady(185358)
    check("A1.3 IsSpellReady probes once per tick", Reads == 1)
    pcall(MDCB.EndTick)
    MDCB.IsSpellReady(185358)
    check("A1.3 IsSpellReady outside a tick probes again", Reads == 2)
    C_Spell.GetSpellCooldown = SavedCD
  end
  do
    local SavedNever = MDCB.IsNeverAutomatic
    local SavedHeal = MDCB.Extras.WARRIOR.Arms.selfHeal
    local Walks = 0
    MDCB.Extras.WARRIOR.Arms.selfHeal = { 202168 }
    MDCB.IsNeverAutomatic = function (id) Walks = Walks + 1; return SavedNever(id) end
    pcall(MDCB.BeginTick)
    local P1 = MDCB.RotationCandidates(8, 4)
    local W1 = Walks
    local P2 = MDCB.RotationCandidates(8, 4)
    check("A1.3 RotationCandidates reuses the per-tick pool",
      P1 == P2 and W1 >= 1 and Walks == W1)
    pcall(MDCB.EndTick)
    MDCB.RotationCandidates(8, 4)
    check("A1.3 RotationCandidates outside a tick re-walks", Walks > W1)
    MDCB.Extras.WARRIOR.Arms.selfHeal = SavedHeal
    MDCB.IsNeverAutomatic = SavedNever
  end

  -- ---- A1.5: the HP-curve cell paints black once, not per tick --------
  do
    -- The last 51 textures created are the Exp strip's cells 0-50, so cell 35
    -- is TextureCount - 51 + 1 + 35. (Vertex cannot identify it: the stable
    -- addon's own HP-curve cell also calls SetVertexColor.)
    local HpId = TextureCount - 15
    check("A1.5 Exp HP-curve texture identified", Textures[HpId] ~= nil)
    local SavedHpCurve = DB.HpCurve
    DB.HpCurve = true
    pcall(UpdateExp, StripExp, 0.06)
    local VOn = VertexWrites[HpId] or 0
    pcall(UpdateExp, StripExp, 0.06)
    check("A1.5 HP curve on paints the colour cell", (VertexWrites[HpId] or 0) > VOn)
    DB.HpCurve = false
    pcall(UpdateExp, StripExp, 0.06)              -- leaves the cell black
    local Before = ColorWrites[HpId] or 0
    pcall(UpdateExp, StripExp, 0.06)
    pcall(UpdateExp, StripExp, 0.06)
    check("A1.5 HP curve off does not repaint black every tick",
      (ColorWrites[HpId] or 0) == Before)
    DB.HpCurve = SavedHpCurve
    pcall(UpdateExp, StripExp, 0.06)
  end

  -- ---- A1.6: cached SavedVariables table + precomputed keys -----------
  do
    local SavedDBGlobal = _G.MaxDpsCompanionBridgeDB
    TG.Set("Main", false)
    check("A1.6 canonical and lowercase spellings agree",
      TG.Get("Main") == false and TG.Get("main") == false and TG.Get("MAIN") == false)
    _G.MaxDpsCompanionBridgeDB = { Toggles = { Main = true } }
    check("A1.6 Toggles reads the cached DB (not the swapped global)", TG.Get("Main") == false)
    pcall(TG.RefreshDatabase)
    check("A1.6 RefreshDatabase re-points the cache", TG.Get("Main") == true)
    _G.MaxDpsCompanionBridgeDB = SavedDBGlobal
    pcall(TG.RefreshDatabase)
    TG.Set("Main", true)
    check("A1.6 restored DB drives the gate again", TG.Get("Main") == true)
  end

  -- ---- A1.7: HotbarWatch debounce + signature-gated gen ---------------
  do
    Actions = { [1] = { "spell", 185358 } }
    Binds = { ["ACTIONBUTTON1"] = "1" }
    pcall(HW.MarkDirty); pcall(HW.Flush)
    local GenA = HW.Gen()
    check("A1.7 first rebuild streams one entry", HW.Count() == 1)
    pcall(HW.MarkDirty); pcall(HW.Flush)
    check("A1.7 an unchanged map keeps gen", HW.Gen() == GenA)
    Binds = { ["ACTIONBUTTON1"] = "SHIFT-1" }
    pcall(HW.MarkDirty); pcall(HW.Flush)
    check("A1.7 a changed map bumps gen", HW.Gen() == (GenA + 1) % 16)
    Actions = { [1] = { "spell", 185358 }, [2] = { "spell", 100 } }
    Binds = { ["ACTIONBUTTON1"] = "1", ["ACTIONBUTTON2"] = "2" }
    FirePlainEvent("UPDATE_BINDINGS")
    local CountBefore = HW.Count()
    pcall(HW.Frame)                              -- the paint tick
    check("A1.7 Frame never rebuilds inside the paint tick",
      HW.Count() == CountBefore and HWIsDirty())
    pcall(HW.Flush)
    check("A1.7 the coalesced flush applies the new map", HW.Count() == 2)
    local SweepAt = Now
    pcall(HW.Frame)                              -- arms LastSweep
    Now = Now + 3.0                              -- > SWEEP_SECONDS
    Actions[3] = { "spell", 6544 }
    Binds["ACTIONBUTTON3"] = "3"
    pcall(HW.Frame)
    check("A1.7 the 2 s sweep schedules instead of rebuilding",
      HW.Count() == 2 and HWIsDirty())
    pcall(HW.Flush)
    check("A1.7 the sweep flush picks the change up", HW.Count() == 3)
    Now = SweepAt
  end

  -- ---- A1.8: vehicle / override / bonus bar events --------------------
  do
    runDeferred()   -- drain anything queued before this pin
    MDCB._BindCache = { [185358] = { Miss = true } }
    local Rev = MDCB._BindRevision or 0
    Actions = { [1] = { "spell", 100 } }
    Binds = { ["ACTIONBUTTON1"] = "1" }
    FirePlainEvent("UPDATE_BONUS_ACTIONBAR")
    check("A1.8 bonus-bar event marks HotbarWatch dirty", HWIsDirty())
    pcall(HW.Flush)
    runDeferred()
    check("A1.8 bonus-bar event invalidates the bindings",
      (MDCB._BindRevision or 0) > Rev and MDCB._BindCache[185358] == nil)
    local Entries = HW.Entries()
    check("A1.8 bonus-bar event rebuilds the pressed map",
      HW.Count() == 1 and Entries[1] and Entries[1].Id == 100)
  end

  -- ---- A1.9: SavedVariables schema migration --------------------------
  do
    local Schema = MDCB.SCHEMA_VERSION
    check("A1.9 boot migrated the DB to schema 1",
      Schema == 1 and DB.SchemaVersion == 1)
    local Legacy = { Enabled = false }
    local OkMig = pcall(MDCB.Migrate, Legacy)
    check("A1.9 v0 Enabled=false seeds the sticky pause",
      OkMig and Legacy.UserPaused == true and Legacy.SchemaVersion == 1)
    local Cleared = { SchemaVersion = 1, Enabled = false, UserPaused = false }
    local OkMig2 = pcall(MDCB.Migrate, Cleared)
    check("A1.9 v1 never resurrects a cleared pause", OkMig2 and Cleared.UserPaused == false)
    local Fresh = {}
    local OkMig3 = pcall(MDCB.Migrate, Fresh)
    check("A1.9 a fresh DB gets schema 1 and no pause",
      OkMig3 and Fresh.SchemaVersion == 1 and Fresh.UserPaused == nil)
  end

  -- ---- A1.10: layout clamps to the screen -----------------------------
  do
    local SavedX, SavedY, SavedCell = DB.OffsetX, DB.OffsetY, DB.CellSize
    UIParent._w, UIParent._h = 100, 40
    DB.OffsetX, DB.OffsetY, DB.CellSize = 5000, 5000, 999
    MDCB.Layout()
    check("A1.10 cell size clamped to 64", DB.CellSize == 64)
    check("A1.10 offset clamped onto the screen", DB.OffsetX == 0 and DB.OffsetY == 0)
    UIParent._w, UIParent._h = 4000, 2000
    DB.OffsetX, DB.OffsetY = 5000, 5000
    MDCB.Layout()
    check("A1.10 oversized offset clamped to the screen edge",
      DB.OffsetX == 4000 - DB.CellSize * 51 and DB.OffsetY == 2000 - DB.CellSize)
    UIParent._w, UIParent._h = nil, nil
    DB.OffsetX, DB.OffsetY, DB.CellSize = SavedX, SavedY, SavedCell
    MDCB.Layout()
  end

  -- ---- A1.11: one shared, documented slot -> command table ------------
  do
    local SC = MDCB.SlotCommand
    check("A1.11 main bar 1-12 -> ACTIONBUTTON",
      SC and SC(1) == "ACTIONBUTTON1" and SC(12) == "ACTIONBUTTON12")
    check("A1.11 13-24 are Action Bar 1 page 2 (ACTIONBUTTON)",
      SC and SC(13) == "ACTIONBUTTON1" and SC(24) == "ACTIONBUTTON12")
    check("A1.11 61-72 -> MULTIACTIONBAR1 (bottom-left)",
      SC and SC(61) == "MULTIACTIONBAR1BUTTON1" and SC(72) == "MULTIACTIONBAR1BUTTON12")
    check("A1.11 documented rows 25-60 unchanged",
      SC and SC(25) == "MULTIACTIONBAR3BUTTON1" and SC(37) == "MULTIACTIONBAR4BUTTON1"
      and SC(49) == "MULTIACTIONBAR2BUTTON1" and SC(60) == "MULTIACTIONBAR2BUTTON12")
    check("A1.11 bars 6-8 -> MULTIACTIONBAR5/6/7",
      SC and SC(145) == "MULTIACTIONBAR5BUTTON1" and SC(156) == "MULTIACTIONBAR5BUTTON12"
      and SC(157) == "MULTIACTIONBAR6BUTTON1" and SC(169) == "MULTIACTIONBAR7BUTTON1"
      and SC(180) == "MULTIACTIONBAR7BUTTON12")
    check("A1.11 bonus/vehicle slots have no command",
      SC and SC(73) == nil and SC(144) == nil and SC(181) == nil and SC(0) == nil)
    Actions = { [13] = { "spell", 185358 } }
    Binds = { ["ACTIONBUTTON1"] = "1" }
    pcall(HW.MarkDirty); pcall(HW.Flush)
    local E = HW.Entries()
    check("A1.11 HotbarWatch binds slot 13 through ACTIONBUTTON",
      HW.Count() == 1 and E[1] and E[1].Slot == 13 and E[1].VK == 0x31)
    local SavedSpells = MaxDps.Spells
    MaxDps.Spells = {}
    MDCB._BindCache = {}
    local VK = MDCB.ResolveBinding(185358)
    check("A1.11 Reader binds slot 13 through ACTIONBUTTON", VK == 0x31)
    MaxDps.Spells = SavedSpells
  end

  -- ---- A1.12: stale diagnostics comments ------------------------------
  do
    ClearChat()
    if MDCB.Diag then pcall(MDCB.Diag) end
    local Line = table.concat(ChatLog, "\n")
    check("A1.12 /mdb diag reports proto=5", Line:find("proto=5", 1, true) ~= nil)
  end

  -- ---- A1.13: readiness is NOT map identity (gen/seq survive a flip) ---
  -- Reviewer P1: the signature used to fold each entry's Ready flag, so every
  -- cooldown flip bumped gen and restarted seq at 0. The companion's Ext4
  -- assembler only swaps the map after a COMPLETE seq cycle inside one gen, so
  -- a cooldown-heavy fight (readiness flips every GCD) starved it. Ready still
  -- rides the wire (entry cell 2 idTop bit2) and is now refreshed in place.
  do
    local SavedReadyFn = MDCB.IsSpellReady
    local ReadyNow = true
    -- Cell i of the Exp strip: the last 51 textures created (as in A1.5).
    local CellsBase = TextureCount - 50;
    local function XNib (i)
      local c = Textures[CellsBase + i]
      if not c then return 0 end
      return math.floor(c[1] * 15 + 0.5)
    end
    MDCB.IsSpellReady = function () return ReadyNow end
    runDeferred()
    Actions, Binds = {}, {}
    for i = 1, 4 do
      Actions[i] = { "spell", 100000 + i }
      Binds["ACTIONBUTTON" .. i] = tostring(i)
    end
    pcall(HW.MarkDirty); pcall(HW.Flush)
    check("A1.13 four-entry map streamed", HW.Count() == 4)
    local GenReady = HW.Gen()
    for _ = 1, 3 do pcall(HW.Frame) end         -- Hold cycle: 3 ticks -> seq 1
    local SeqReady = HW.Seq()
    check("A1.13 seq cycle advanced before the flip", SeqReady == 1)

    -- (a) Ready-only flip through a rebuild: gen and seq must not move
    ReadyNow = false
    pcall(HW.MarkDirty); pcall(HW.Flush)
    check("A1.13 a Ready-only rebuild took the new readiness",
      HW.Entries()[3] and HW.Entries()[3].Ready == false)
    check("A1.13 a Ready flip alone does not bump gen", HW.Gen() == GenReady)
    check("A1.13 a Ready flip alone does not reset seq", HW.Seq() == SeqReady)

    -- in-place refresh at frame build: no rebuild is scheduled at all
    ReadyNow = true
    check("A1.13 the map is not dirty before the frame build", HW.IsDirty() == false)
    pcall(HW.Frame)                             -- the paint tick refreshes in place
    local Fresh = HW.Entries()
    check("A1.13 frame build refreshes Ready in place (no rebuild)",
      Fresh[3] and Fresh[3].Ready == true and Fresh[4] and Fresh[4].Ready == true)
    check("A1.13 the in-place refresh keeps gen and seq",
      HW.Gen() == GenReady and HW.Seq() == SeqReady)

    -- (c) the ready bit on the WIRE follows the flip at paint time
    pcall(UpdateExp, StripExp, 0.06)
    local WireR = XNib(45)
    check("A1.13 wire ready bit set while ready", bit.band(WireR, 4) == 4)
    ReadyNow = false
    pcall(UpdateExp, StripExp, 0.06)
    WireR = XNib(45)
    check("A1.13 wire ready bit clears after the flip", bit.band(WireR, 4) == 0)

    -- (b) an IDENTITY change still bumps gen
    local GenNew = HW.Gen()
    Actions[5] = { "spell", 100005 }
    Binds["ACTIONBUTTON5"] = "5"
    pcall(HW.MarkDirty); pcall(HW.Flush)
    check("A1.13 a new entry bumps gen", HW.Gen() == (GenNew + 1) % 16)
    local GenKey = HW.Gen()
    Binds["ACTIONBUTTON1"] = "SHIFT-1"
    pcall(HW.MarkDirty); pcall(HW.Flush)
    check("A1.13 a changed key bumps gen", HW.Gen() == (GenKey + 1) % 16)
    MDCB.IsSpellReady = SavedReadyFn
  end

  -- ---- A1.14: the remaining Tick memos are gated on Tick.Active ---------
  -- Reviewer P2: ScrubbedFlags / ClassSpec / ItemSpellIDs served their last
  -- tick's memo to a direct call between ticks (e.g. a /mdb diagnostic).
  do
    local SavedSpells, SavedFlags = MaxDps.Spells, MaxDps.Flags
    local SavedItems, SavedCons = MaxDps.ItemSpells, MaxDps.Consumables
    local SavedInterrupts, SavedIdToSpec = MaxDps.classInterrupts, MaxDps.idtospec
    local SavedSpecInfo = GetSpecializationInfo
    local SavedBindCache = MDCB._BindCache
    runDeferred()

    -- (1) ScrubbedFlags + ItemSpellIDs: swapped upstream state shows at once
    MaxDps.Flags = { [500001] = true }
    MaxDps.Spells = { [500001] = {} }
    MaxDps.ItemSpells = { [5002] = 500001 }   -- ItemID -> ItemSpellID
    MaxDps.Consumables = { [5002] = true }
    pcall(MDCB.BeginTick)
    local C1 = MDCB.GetConsumableSpellID()
    pcall(MDCB.EndTick)
    check("A1.14 consumable memo warm inside the tick", C1 == 500001)
    MaxDps.Flags = { [500003] = true }
    MaxDps.Spells = { [500003] = {} }
    MaxDps.ItemSpells = { [5004] = 500003 }
    MaxDps.Consumables = { [5004] = true }
    check("A1.14 ScrubbedFlags/ItemSpellIDs are tick-gated (outside a tick)",
      MDCB.GetConsumableSpellID() == 500003)

    -- (2) ClassSpec: a spec change between ticks shows at once
    MaxDps.classInterrupts = { WARRIOR = { Arms = { 6552 }, Fury = { 6553 } } }
    MaxDps.idtospec = { [71] = "Arms", [72] = "Fury" }
    MaxDps.Flags = { [6553] = true }
    MaxDps.Spells = { [6553] = {} }
    Actions = { [1] = { "spell", 6553 } }
    Binds = { ["ACTIONBUTTON1"] = "1" }
    MDCB._BindCache = {}
    GetSpecializationInfo = function () return 71 end
    pcall(MDCB.BeginTick)
    local W1 = MDCB.GetInterruptSpellID()
    pcall(MDCB.EndTick)
    check("A1.14 interrupt memo warm: Arms does not own Fury's kick", W1 == nil)
    GetSpecializationInfo = function () return 72 end
    check("A1.14 ClassSpec is tick-gated (outside a tick)", MDCB.GetInterruptSpellID() == 6553)

    MaxDps.Spells, MaxDps.Flags = SavedSpells, SavedFlags
    MaxDps.ItemSpells, MaxDps.Consumables = SavedItems, SavedCons
    MaxDps.classInterrupts, MaxDps.idtospec = SavedInterrupts, SavedIdToSpec
    GetSpecializationInfo = SavedSpecInfo
    MDCB._BindCache = SavedBindCache
  end

  -- restore the action-bar stubs and the clock
  GetActionInfo = function () return nil end
  GetBindingKey = function () return nil end
  GetMacroSpell = function () return nil end
  GetMacroItem = function () return nil end
  GetTime = SavedClock
  UnitHealth, UnitHealthMax = SavedHealth, SavedHealthMax
  UnitStagger, IsInGroup, UnitAffectingCombat = SavedStagger, SavedInGroup, SavedUACombat
  MaxDps.Spell = SavedSpell
end
RunParagonTests()

-- =====================================================================
-- Ext5 (4.6.0 "Cadence"): target rank / cast age+kind / GCD landing / CC
-- insurance, cells 51-57 behind the addon DB key `Ext5` (default OFF).
-- With the key off the strip must stay the 51-cell 4.5.8 frame (no cell 51
-- texture, cell 50 R bit0 clear); with it on the strip is 58 cells and the
-- block is checksummed by cell 57.
-- =====================================================================
local function RunExt5Tests ()
  local DB = MaxDpsCompanionBridgeDB
  local SavedClock = GetTime
  local Now = 5000.0
  GetTime = function () return Now end
  runDeferred()

  -- Reload the encoder so the load-time CELL_COUNT picks up DB.Ext5, then boot
  -- it like the client would. Returns the strip, its OnUpdate and the texture
  -- id base (cell i = TexBase + 1 + i).
  local function BootStrip ()
    local Before, TexBase = #AllFrames, TextureCount
    assert(loadfile("addon/MaxDpsCompanionBridge/Bridge.lua"))("MaxDpsCompanionBridge", MDCB)
    local Loader
    for i = Before + 1, #AllFrames do
      local f = AllFrames[i]
      if f._events and f._events["ADDON_LOADED"] and f._scripts and f._scripts.OnEvent then
        Loader = f
      end
    end
    assert(Loader, "Ext5 reloaded loader frame missing")
    Loader._scripts.OnEvent(Loader, "ADDON_LOADED", "MaxDpsCompanionBridge")
    local Strip = _G.MaxDpsCompanionBridge_Block
    assert(Strip and Strip._scripts and Strip._scripts.OnUpdate, "Ext5 strip missing")
    return Strip, Strip._scripts.OnUpdate, TexBase
  end

  -- ---- (1) Ext5 OFF: the 51-cell compatibility frame ----
  DB.Ext5 = false
  local StripOff, TickOff, TexOff = BootStrip()
  local function NibOff (i)
    local c = Textures[TexOff + 1 + i]
    if not c then return 0, 0, 0 end
    return math.floor(c[1] * 15 + 0.5), math.floor(c[2] * 15 + 0.5), math.floor(c[3] * 15 + 0.5)
  end
  check("E5 Ext5=false strip stays 51 cells wide", type(StripOff._size) == "table"
    and StripOff._size[1] == 8 * 51 and StripOff._size[2] == 8)
  check("E5 Ext5=false creates exactly 51 textures", TextureCount == TexOff + 51)
  check("E5 Ext5=false has no cell 51 texture", Textures[TexOff + 52] == nil)
  local OkOff = pcall(TickOff, StripOff, 0.06)
  check("E5 Ext5=false cell 50 R stays 0 (no presence bit)", OkOff and NibOff(50) == 0)

  -- ---- (2) Ext5 ON: 58 cells, presence bit, Ext4 commit untouched ----
  DB.Ext5 = true
  local Strip, Tick, TexBase = BootStrip()
  local function XNib (i)
    local c = Textures[TexBase + 1 + i]
    if not c then return 0, 0, 0 end
    return math.floor(c[1] * 15 + 0.5), math.floor(c[2] * 15 + 0.5), math.floor(c[3] * 15 + 0.5)
  end
  check("E5 Ext5=true strip is 58 cells wide", type(Strip._size) == "table"
    and Strip._size[1] == 8 * 58 and Strip._size[2] == 8)
  check("E5 Ext5=true creates exactly 58 textures", TextureCount == TexBase + 58)

  UnitClassification, UnitLevel = nil, nil
  local OkOn = pcall(Tick, Strip, 0.06)
  local hb = select(2, XNib(9))
  local c50r, c50g, c50b = XNib(50)
  local Ext4Sum = 0
  for i = 43, 49 do
    local r, g, b = XNib(i)
    Ext4Sum = Ext4Sum + r + g + b
  end
  check("E5 cell 50 R bit0 = Ext5 presence", OkOn and c50r == 1)
  check("E5 cell 50 G is still the Ext4 checksum", c50g == (Ext4Sum % 16))
  check("E5 cell 50 B is still the heartbeat", c50b == hb)

  -- ---- (3) target rank (cell 51 R), incl. elite-skull ----
  local SavedClassFn, SavedLevelFn = UnitClassification, UnitLevel
  local function RankOf (Class, Level)
    UnitClassification = function () return Class end
    UnitLevel = function () return Level end
    pcall(Tick, Strip, 0.06)
    return (XNib(51))
  end
  check("E5 rank trivial = 1", RankOf("trivial", 1) == 1)
  check("E5 rank normal = 2", RankOf("normal", 80) == 2)
  check("E5 rank elite = 3", RankOf("elite", 82) == 3)
  check("E5 rank rare = 4", RankOf("rare", 80) == 4)
  check("E5 rank rareelite = 5", RankOf("rareelite", 80) == 5)
  check("E5 rank worldboss = 6", RankOf("worldboss", -1) == 6)
  check("E5 rank elite-skull = 7 (elite + UnitLevel -1)", RankOf("elite", -1) == 7)
  check("E5 rank undocumented classification = 0", RankOf("minus", 80) == 0)
  UnitClassification, UnitLevel = nil, nil
  pcall(Tick, Strip, 0.06)
  check("E5 rank without the classification API = 0", XNib(51) == 0)

  -- ---- (4) in-delve (cell 51 G): 0 unknown / 1 no / 2 yes ----
  local SavedInstFn, SavedScenario = GetInstanceInfo, _G.C_Scenario
  local function DelveOf (InstFn, Scenario)
    GetInstanceInfo = InstFn
    _G.C_Scenario = Scenario
    pcall(Tick, Strip, 0.06)
    return (select(2, XNib(51)))
  end
  check("E5 in-delve yes = 2 (scenario instance + C_Scenario.GetInfo)", DelveOf(
    function () return "Delve", "scenario" end,
    { GetInfo = function () return { name = "Delve" } end }) == 2)
  check("E5 in-delve no = 1 (overworld)", DelveOf(
    function () return nil, "none" end, nil) == 1)
  check("E5 in-delve unknown = 0 (scenario instance, no scenario API)", DelveOf(
    function () return "X", "scenario" end, nil) == 0)
  check("E5 in-delve unknown = 0 (instance API throws)", DelveOf(
    function () error("blocked") end, { GetInfo = function () return {} end }) == 0)
  GetInstanceInfo, _G.C_Scenario = SavedInstFn, SavedScenario

  -- ---- (5) cast kind / age buckets / interruptibility (cell 52) ----
  FireUnitEvent("target", "UNIT_SPELLCAST_STOP")   -- normalise the sensor latch
  Now = 6000.0
  FireUnitEvent("target", "UNIT_SPELLCAST_START")
  local function CastNow (AgeMs)
    Now = 6000.0 + (AgeMs or 0) / 1000
    pcall(Tick, Strip, 0.06)
    local r, g, b = XNib(52)
    return r, g, b
  end
  local cr, cg, cb = CastNow(0)
  check("E5 cell 52 B cast kind = 2 (cast)", cb == 2)
  check("E5 cell 52 G age 0 ms = bucket 1", cg == 1)
  check("E5 cell 52 R interruptible = 0 while the verdict is unknown", cr == 0)
  check("E5 age 249 ms = bucket 1", select(2, CastNow(249)) == 1)
  check("E5 age 250 ms = bucket 2", select(2, CastNow(250)) == 2)
  check("E5 age 251 ms = bucket 2", select(2, CastNow(251)) == 2)
  check("E5 age 1000 ms = bucket 5", select(2, CastNow(1000)) == 5)
  check("E5 age 9000 ms saturates at bucket 14", select(2, CastNow(9000)) == 14)
  FireUnitEvent("target", "UNIT_SPELLCAST_INTERRUPTIBLE")
  check("E5 cell 52 R interruptible = 2 (yes)", (CastNow(500)) == 2)
  FireUnitEvent("target", "UNIT_SPELLCAST_NOT_INTERRUPTIBLE")
  check("E5 cell 52 R interruptible = 1 (no)", (CastNow(500)) == 1)
  Now = 6000.75
  FireUnitEvent("target", "UNIT_SPELLCAST_CHANNEL_START")
  check("E5 cell 52 B cast kind = 3 (channel)", select(3, CastNow(0)) == 3)
  FireUnitEvent("target", "UNIT_SPELLCAST_START")
  FirePlainEvent("PLAYER_TARGET_CHANGED")
  local tr, tg, tb = CastNow(0)
  check("E5 target change clears cast kind/age/interruptible",
    tr == 0 and tg == 0 and tb == 1)
  FireUnitEvent("target", "UNIT_SPELLCAST_CHANNEL_STOP")
  local nr, ng, nb = CastNow(0)
  check("E5 no cast = kind 1 (none), age 0, interruptible 0",
    nr == 0 and ng == 0 and nb == 1)

  -- ---- (6) GCD landing bucket (cell 53) ----
  local SavedCooldown = C_Spell.GetSpellCooldown
  local GcdCalls = 0
  local function GcdStub (Ms, OnGcd)
    if Ms == nil then
      C_Spell.GetSpellCooldown = function (id)
        if id ~= 61304 then return SavedCooldown(id) end
        GcdCalls = GcdCalls + 1
        return { startTime = 0, duration = 0, isEnabled = true, isActive = false,
          isOnGCD = (OnGcd ~= false) }
      end
      return
    end
    C_Spell.GetSpellCooldown = function (id)
      if id ~= 61304 then return SavedCooldown(id) end
      GcdCalls = GcdCalls + 1
      return { startTime = Now, duration = Ms / 1000, isEnabled = true,
        isActive = true, isOnGCD = true }
    end
  end
  local function GcdBucketOf (Ms, OnGcd)
    GcdStub(Ms, OnGcd)
    pcall(Tick, Strip, 0.06)
    return (XNib(53))
  end
  check("E5 GCD 99 ms = bucket 2", GcdBucketOf(99) == 2)
  check("E5 GCD 100 ms = bucket 3", GcdBucketOf(100) == 3)
  check("E5 GCD 1299 ms = bucket 14", GcdBucketOf(1299) == 14)
  check("E5 GCD 1300 ms = bucket 15", GcdBucketOf(1300) == 15)
  check("E5 GCD 0 ms = bucket 2", GcdBucketOf(0) == 2)
  check("E5 confirmed not-on-GCD = bucket 1", GcdBucketOf(nil, false) == 1)

  -- ---- (7) secret mode: predicate read BEFORE any arithmetic ----
  local SavedSecrets = C_Secrets
  C_Secrets = { ShouldCooldownsBeSecret = function () return true end }
  -- The stub returns SECRET start/duration: any subtraction would throw (and
  -- the pcall would hide it), so the call COUNT is the assertion.
  C_Spell.GetSpellCooldown = function (id)
    if id ~= 61304 then return SavedCooldown(id) end
    GcdCalls = GcdCalls + 1
    return { startTime = S(Now), duration = S(1.2), isEnabled = true,
      isActive = true, isOnGCD = true }
  end
  local CallsBefore = GcdCalls
  local SecProbe = MDCB.GetExt5()
  check("E5 secret mode: gcdRemainingMs stays nil (bucket 0)",
    SecProbe.gcdRemainingMs == nil)
  check("E5 secret mode: the cooldown is never even read", GcdCalls == CallsBefore)
  pcall(Tick, Strip, 0.06)
  check("E5 secret mode: cell 53 R = 0 (UNKNOWN) on the wire", XNib(53) == 0)
  C_Secrets = SavedSecrets
  C_Spell.GetSpellCooldown = SavedCooldown

  -- ---- (8) CC banking entry: cells 54-56 key + spell id round-trip ----
  local SavedActionInfo, SavedBindingKey = GetActionInfo, GetBindingKey
  local SavedSpec, SavedSpecInfo = GetSpecialization, GetSpecializationInfo
  local SavedMacroSpell, SavedMacroItem = GetMacroSpell, GetMacroItem
  -- An earlier section left overlay HotKey buttons in MaxDps.Spells (path #1 of
  -- ResolveBinding). Clear them so the CC key comes from the action bar, which
  -- is what this pin is about.
  local SavedSpells = MaxDps.Spells
  MaxDps.Spells = {}
  GetSpecialization = function () return 1 end
  GetSpecializationInfo = function () return 71 end   -- Warrior / Arms
  GetMacroSpell = function () return nil end
  GetMacroItem = function () return nil end
  GetActionInfo = function (slot)
    if slot == 1 then return "spell", 107570 end   -- Storm Bolt (Arms cc)
    return nil
  end
  GetBindingKey = function (cmd)
    if cmd == "ACTIONBUTTON1" then return "1" end
    return nil
  end
  if MDCB.InvalidateBindings then pcall(MDCB.InvalidateBindings) end
  MDCB._BindCache = {}
  local CcVK, CcMods = MDCB.ResolveBinding(107570)
  check("E5 CC stub resolves the curated CC binding",
    CcVK == 0x31 and (CcMods or 0) == 0)
  local CcProbe = MDCB.GetExt5()
  check("E5 CC entry picks the ready+bound curated CC", CcProbe.ccSpellId == 107570)
  check("E5 CC entry carries the resolved key", CcProbe.ccKey == CcVK)
  pcall(Tick, Strip, 0.06)
  local k54r, k54g, k54b = XNib(54)
  local i55r, i55g, i55b = XNib(55)
  local i56r, i56g, i56b = XNib(56)
  local DecodedVK = k54r * 16 + k54g
  local DecodedId = i55r * 2 ^ 20 + i55g * 2 ^ 16 + i55b * 2 ^ 12
    + i56r * 2 ^ 8 + i56g * 2 ^ 4 + i56b
  check("E5 cell 54 key packing round-trips (vk hi/lo)", DecodedVK == CcVK)
  check("E5 cell 54 flags = mods | valid(8)",
    k54b == bit.bor(bit.band(CcMods or 0, 7), 8))
  check("E5 cell 55 R carries only idTop & 3", i55r == 0)
  check("E5 cells 55-56 spell id round-trips", DecodedId == 107570)

  GetBindingKey = function (cmd)
    if cmd == "ACTIONBUTTON1" then return "SHIFT-1" end
    return nil
  end
  MDCB._BindCache = {}
  pcall(Tick, Strip, 0.06)
  check("E5 cell 54 packs SHIFT into the key flags (9)", select(3, XNib(54)) == 9)

  MDCB.Toggles.SetCC(false)
  pcall(Tick, Strip, 0.06)
  local z54, z55, z56 = XNib(54), XNib(55), XNib(56)
  check("E5 CC toggle off = clean zero key/id block", z54 == 0 and z55 == 0 and z56 == 0)
  MDCB.Toggles.SetCC(true)

  -- ---- (9) commit cell 57 ----
  pcall(Tick, Strip, 0.06)
  local Sum = 0
  for i = 51, 56 do
    local r, g, b = XNib(i)
    Sum = Sum + r + g + b
  end
  local m57r, m57g, m57b = XNib(57)
  local hb2 = select(2, XNib(9))
  check("E5 cell 57 R = 0", m57r == 0)
  check("E5 cell 57 G = sum(cells 51-56) mod 16", m57g == (Sum % 16))
  check("E5 cell 57 B = heartbeat", m57b == hb2)

  -- ---- (10) no protected/secret cast API is ever called ----
  local SavedUIC, SavedChannel = UnitCastingInfo, UnitChannelInfo
  local UiCalls, ChannelCalls = 0, 0
  UnitCastingInfo = function () UiCalls = UiCalls + 1; return nil end
  UnitChannelInfo = function () ChannelCalls = ChannelCalls + 1; return nil end
  Now = Now + 0.4
  FireUnitEvent("target", "UNIT_SPELLCAST_START")
  pcall(Tick, Strip, 0.06)
  pcall(MDCB.GetExt5)
  check("E5 UnitCastingInfo / UnitChannelInfo are never called",
    UiCalls == 0 and ChannelCalls == 0)
  FireUnitEvent("target", "UNIT_SPELLCAST_STOP")
  UnitCastingInfo, UnitChannelInfo = SavedUIC, SavedChannel

  -- ---- (11) /mcb ext5 + /mcb status ----
  ClearChat()
  MDCB.HandleCommand("ext5 off")
  local OffLine = ChatLog[#ChatLog] or ""
  check("E5 /mcb ext5 off persists the key", DB.Ext5 == false)
  check("E5 /mcb ext5 off explains the /reload requirement",
    OffLine:find("ext5", 1, true) ~= nil and OffLine:find("/reload", 1, true) ~= nil)
  ClearChat()
  MDCB.HandleCommand("ext5 on")
  local OnLine = ChatLog[#ChatLog] or ""
  check("E5 /mcb ext5 on persists the key and reports 58 cells",
    DB.Ext5 == true and OnLine:find("58", 1, true) ~= nil)
  ClearChat()
  MDCB.HandleCommand("status")
  local StatusLine = ChatLog[#ChatLog] or ""
  check("E5 /mcb status prints ext5=1", StatusLine:find("ext5=1", 1, true) ~= nil)

  -- ---- (12) B1: flipping the key ON inside a 51-cell session ----
  -- The strip width (and every cell texture) is created once at ADDON_LOADED,
  -- so `/mcb ext5 on` only persists the key. The very next Update used to paint
  -- cells 51-57 that do not exist: SetNibbles indexed nil, Update threw before
  -- WriteStatus/WriteVersion and the companion saw a torn frame until /reload.
  -- The write sites are width-gated now (Ext5Active) and SetNibbles itself
  -- fails closed; this pins BOTH: a full Update writes a valid 51-cell frame,
  -- with no error and no paint above cell 50.
  DB.Ext5 = false
  local Strip51, Tick51, Tex51 = BootStrip()
  local function Nib51 (i)
    local c = Textures[Tex51 + 1 + i]
    if not c then return 0, 0, 0 end
    return math.floor(c[1] * 15 + 0.5), math.floor(c[2] * 15 + 0.5), math.floor(c[3] * 15 + 0.5)
  end
  check("E5 B1 boots a 51-cell strip", TextureCount == Tex51 + 51)

  ClearChat()
  MDCB.HandleCommand("ext5 on")
  local FlipLine = ChatLog[#ChatLog] or ""
  check("E5 B1 runtime flip persists the key", DB.Ext5 == true)
  check("E5 B1 runtime flip still asks for /reload", FlipLine:find("/reload", 1, true) ~= nil)

  local OkFlip = pcall(Tick51, Strip51, 0.06)
  check("E5 B1 runtime flip: Update completes with no error", OkFlip)
  check("E5 B1 runtime flip: no new texture was created", TextureCount == Tex51 + 51)
  check("E5 B1 runtime flip: there is still no cell 51 texture", Textures[Tex51 + 52] == nil)
  local f50r, f50g, f50b = Nib51(50)
  check("E5 B1 runtime flip: cell 50 R stays 0 (no presence bit)", f50r == 0)
  local FlipSum = 0
  for i = 43, 49 do
    local r, g, b = Nib51(i)
    FlipSum = FlipSum + r + g + b
  end
  local FlipHb = select(2, Nib51(9))
  check("E5 B1 runtime flip: Ext4 checksum still covers cells 43-49",
    f50g == (FlipSum % 16))
  check("E5 B1 runtime flip: cell 50 B is still the heartbeat", f50b == FlipHb)
  -- No paint landed above cell 50: every texture this strip owns is cell 0-50.
  local MaxWritten = 0
  for id in pairs(ColorWrites) do if id > MaxWritten then MaxWritten = id end end
  check("E5 B1 runtime flip: no paint above cell 50", MaxWritten <= Tex51 + 51)

  -- A second tick (the key is still on) must stay just as clean: the bug did
  -- not depend on the first frame after the flip.
  check("E5 B1 runtime flip: a second Update stays clean", pcall(Tick51, Strip51, 0.06))

  -- ---- restore stubs ----
  DB.Ext5 = false
  MaxDps.Spells = SavedSpells
  GetActionInfo, GetBindingKey = SavedActionInfo, SavedBindingKey
  GetSpecialization, GetSpecializationInfo = SavedSpec, SavedSpecInfo
  GetMacroSpell, GetMacroItem = SavedMacroSpell, SavedMacroItem
  GetTime = SavedClock
  UnitClassification, UnitLevel = SavedClassFn, SavedLevelFn
end
RunExt5Tests()

print(string.format("RESULT: %d passed, %d failed", PASS, FAIL))
if FAIL > 0 then os.exit(1) end
