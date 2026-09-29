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
function strsplit(sep, s) local a, b = s:match("([^" .. sep .. "]*)" .. sep .. "?(.*)"); return a, b end
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
  bor = function(a, b)
    local r, p = 0, 1
    while a > 0 or b > 0 do
      local abit, bbit = a % 2, b % 2
      if abit == 1 or bbit == 1 then r = r + p end
      a = math.floor(a / 2); b = math.floor(b / 2); p = p * 2
    end
    return r
  end,
}
SlashCmdList = {}
C_Timer = { After = function() end }

-- frames
local AnonCount = 0
local AllFrames = {}
local Textures = {}
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
    t.SetColorTexture = function(self2, r, g, b, a) Textures[self2._id] = { r, g, b } end
    t.SetVertexColor = function(self2, r, g, b, a) Vertex[self2._id] = r end
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

local function FirePlainEvent(event)
  for _, f in ipairs(AllFrames) do
    if f._events and f._events[event] and f._scripts.OnEvent then
      f._scripts.OnEvent(f, event)
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
function UnitCastingInfo() return nil end
function UnitChannelInfo() return nil end
function UnitClass() return "Warrior", "WARRIOR", 1 end
function GetActionInfo() return nil end
function GetBindingKey() return nil end
function GetSpellTexture() return nil end
function CheckInteractDistance() return false end
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
assert(loadfile("addon/MaxDpsBridge/Catalog.lua"))("MaxDpsBridge", MDB)
-- Workstream C emits the real MDB.SpellAliases in Catalog.lua; this worktree
-- stubs it so the variant path is exercised before C is merged.
MDB.SpellAliases = { [202168] = { 34428 } }
assert(loadfile("addon/MaxDpsBridge/Keymap.lua"))("MaxDpsBridge", MDB)
assert(loadfile("addon/MaxDpsBridge/Bars.lua"))("MaxDpsBridge", MDB)
assert(loadfile("addon/MaxDpsBridge/Reader.lua"))("MaxDpsBridge", MDB)
-- 3.3.0 toggle policy loads before Bridge (Bridge builds its slash key map at
-- load time from MDB.Toggles.Keys); pure logic, no frames.
assert(loadfile("addon/MaxDpsBridge/Toggles.lua"))("MaxDpsBridge", MDB)
assert(loadfile("addon/MaxDpsBridge/Bridge.lua"))("MaxDpsBridge", MDB)
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
CheckInteractDistance = function() return S(true) end
local secretMelee = select(1, MDB.GetTargetContext())
CheckInteractDistance = function() return false end
check("secret melee probe stays UNKNOWN", secretMelee == 2)

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
-- Warrior/Arms catalog (v3 regenerated): mobility {100,6544} = 2,
-- selfHeal {202168,34428} = 2,
-- defensive {871,97462,118038,23920,34428,202168} = 6.
MDB.BeginTick()
local extrasDiag = MDB.GetExtrasDiag()
check("v6 extras diag prints def count (Arms def=6)", extrasDiag:match("def=6") ~= nil)
check("v6 extras diag prints cls/spec and mob/heal counts",
  extrasDiag:match("cls=13 spec=1") ~= nil
  and extrasDiag:match("mob=2") ~= nil
  and extrasDiag:match("heal=2") ~= nil)

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

local okEnc = pcall(Update, Strip, 0.06)
check("B4 Ext2 encode Update: no throw", okEnc)
check("B4 strip is 40 cells wide", type(Strip._size) == "table"
  and Strip._size[1] == 8 * 40 and Strip._size[2] == 8)

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
  local Snap = TG.Snapshot()
  local SnapOn = true
  for i = 1, #ORDER do if Snap[ORDER[i]] ~= true then SnapOn = false end end
  check("T21 Snapshot() all 13 ON by default", SnapOn)

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

print(string.format("RESULT: %d passed, %d failed", PASS, FAIL))
if FAIL > 0 then os.exit(1) end
