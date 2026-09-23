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
function strsplit(sep, s) local a, b = s:match("([^" .. sep .. "]*)" .. sep .. "?(.*)"); return a, b end
function format(f, ...) return string.format(f, ...) end
LibStub = nil
DEFAULT_CHAT_FRAME = { AddMessage = function() end }
bit = {
  rshift = function(a, b) return math.floor(a / 2 ^ b) end,
  band = function(a, b) return a % (b * 2) - (a % b) end,
  bor = function(a, b) return a + b end,
}
SlashCmdList = {}
C_Timer = nil

-- frames
local AnonCount = 0
local function FakeFrame(name)
  local f = { _scripts = {} }
  f.RegisterEvent = function() end
  f.RegisterAllEvents = function() end
  f.UnregisterAllEvents = function() end
  f.SetScript = function(self, s, fn) self._scripts[s] = fn end
  f.SetFrameStrata = function() end
  f.SetFrameLevel = function() end
  f.EnableMouse = function() end
  f.CreateTexture = function() return FakeFrame() end
  f.SetScale = function() end
  f.GetEffectiveScale = function() return 1 end
  f.ClearAllPoints = function() end
  f.SetPoint = function() end
  f.SetSize = function() end
  f.SetWidth = function() end
  f.SetHeight = function() end
  f.SetColorTexture = function() end
  f.SetAlpha = function() end
  f.Show = function() end
  f.Hide = function() end
  if name then _G[name] = f end
  return f
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
function UnitCastingInfo() return nil end
function UnitChannelInfo() return nil end
function UnitClass() return "Hunter", "HUNTER", 3 end
function GetActionInfo() return nil end
function GetBindingKey() return nil end
function GetSpellTexture() return nil end
function CheckInteractDistance() return false end

-- ================= MaxDps engine stub =================
C_Spell = {}
-- default: non-charge spell, plain no-cooldown table
C_Spell.GetSpellCharges = function() return nil end
C_Spell.GetSpellCooldown = function()
  return { startTime = 0, duration = 0, isEnabled = true, isActive = false, isOnGCD = false }
end
C_Spell.IsSpellUsable = function() return true, false end

MaxDps = {
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
assert(loadfile("addon/MaxDpsBridge/Reader.lua"))("MaxDpsBridge", MDB)
assert(loadfile("addon/MaxDpsBridge/Bridge.lua"))("MaxDpsBridge", MDB)
assert(type(MDB.IsSpellReady) == "function")
assert(type(MDB.IsInterruptReady) == "function")
assert(type(MDB.IsValueSafe) == "function")

-- boot the bridge: fire ADDON_LOADED like the client would, then take the
-- OnUpdate handler the bootstrap installed on the strip frame.
local Loader = _G.AnonFrame1
assert(Loader and Loader._scripts.OnEvent, "loader frame missing")
Loader._scripts.OnEvent(Loader, "ADDON_LOADED", "MaxDpsBridge")
local Strip = _G.MaxDpsBridge_Block
assert(Strip, "strip frame missing")
local Update = Strip._scripts.OnUpdate
assert(type(Update) == "function", "OnUpdate not wired")

-- ================= 1. restricted cooldown shapes =================
-- active real cooldown: isActive=true, isOnGCD=false (NeverSecret bools)
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

-- ================= 3. charges =================
-- secret 0 charges, recharging => fail open (documented)
C_Spell.GetSpellCharges = function()
  return { currentCharges = S(0), maxCharges = 2,
           cooldownStartTime = S(999), cooldownDuration = S(30), isActive = true }
end
ok, ready = pcall(MDB.IsSpellReady, 185358)
check("restricted secret 0 charges: no throw", ok)
check("restricted secret 0 charges: fail-open ready", ok and ready == true)

-- secret charges but NOT recharging (isActive=false) => ready, count never read
C_Spell.GetSpellCharges = function()
  return { currentCharges = S(2), maxCharges = 2,
           cooldownStartTime = S(0), cooldownDuration = S(0), isActive = false }
end
ok, ready = pcall(MDB.IsSpellReady, 185358)
check("restricted max charges (isActive=false): ready", ok and ready == true)

-- unrestricted numeric charges keep v1.1.0 semantics
C_Spell.GetSpellCharges = function()
  return { currentCharges = 1, maxCharges = 2, cooldownStartTime = 975, cooldownDuration = 30, isActive = true }
end
ok, ready = pcall(MDB.IsSpellReady, 185358)
check("unrestricted 1 charge: ready", ok and ready == true)

C_Spell.GetSpellCharges = function()
  return { currentCharges = 0, maxCharges = 2, cooldownStartTime = 970.2, cooldownDuration = 30, isActive = true }
end
ok, ready = pcall(MDB.IsSpellReady, 185358)
check("unrestricted 0 charges, 0.2s tail: ready", ok and ready == true)

C_Spell.GetSpellCharges = function()
  return { currentCharges = 0, maxCharges = 2, cooldownStartTime = 975, cooldownDuration = 30, isActive = true }
end
ok, ready = pcall(MDB.IsSpellReady, 185358)
check("unrestricted 0 charges, 5s left: not ready", ok and ready == false)

C_Spell.GetSpellCharges = function() return nil end

-- ================= 4. numeric fallback is pcall-contained =================
-- legacy table shape (no isActive) with plain numbers and a clock that
-- throws: exercises the guarded numeric path end to end.
C_Spell.GetSpellCooldown = function()
  return { startTime = 990, duration = 20, isEnabled = true }
end
GetTime = function() error("simulated secret arithmetic throw") end
ok, ready = pcall(MDB.IsSpellReady, 185358)
GetTime = RealGetTime
check("numeric path throw contained: no throw", ok)
check("numeric path throw: fail-open ready", ok and ready == true)

-- legacy shape, plain numbers, 10s left => not ready (v1.1.0 semantics);
-- the GCD dummy spell (61304) reports a real 1.5s GCD so forgiveness does
-- not swallow a genuine 10s cooldown.
C_Spell.GetSpellCooldown = function(id)
  if id == 61304 then return { startTime = 990, duration = 1.5, isEnabled = true } end
  return { startTime = 990, duration = 20, isEnabled = true }
end
ok, ready = pcall(MDB.IsSpellReady, 185358)
check("legacy numeric 10s left: not ready", ok and ready == false)

-- ================= 5. interrupts =================
C_Spell.GetSpellCooldown = function()
  return { startTime = 0, duration = 0, isEnabled = true, isActive = false, isOnGCD = false }
end
-- secret flag => unknown => fail open
UnitCastingInfo = function() return "Fireball", nil, 1, 0, 5000, false, "Cast-1", S(true), 133 end
ok, ready = pcall(MDB.IsInterruptReady, 185358)
check("secret notInterruptible: no throw", ok)
check("secret notInterruptible: fail-open", ok and ready == true)
-- plain flag true => deny
UnitCastingInfo = function() return "Fireball", nil, 1, 0, 5000, false, "Cast-1", true, 133 end
ok, ready = pcall(MDB.IsInterruptReady, 185358)
check("plain notInterruptible=true: deny", ok and ready == false)
-- plain flag false => allow
UnitCastingInfo = function() return "Fireball", nil, 1, 0, 5000, false, "Cast-1", false, 133 end
ok, ready = pcall(MDB.IsInterruptReady, 185358)
check("plain notInterruptible=false: allow", ok and ready == true)
UnitCastingInfo = function() return nil end
-- channel flag at index 7
UnitChannelInfo = function() return "Mind Flay", nil, 1, 0, 5000, false, false end
ok, ready = pcall(MDB.IsInterruptReady, 185358)
check("plain channel interruptible: allow", ok and ready == true)
UnitChannelInfo = function() return "Mind Flay", nil, 1, 0, 5000, false, S(false) end
ok, ready = pcall(MDB.IsInterruptReady, 185358)
check("secret channel flag: fail-open", ok and ready == true)
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

print(string.format("RESULT: %d passed, %d failed", PASS, FAIL))
if FAIL > 0 then os.exit(1) end
