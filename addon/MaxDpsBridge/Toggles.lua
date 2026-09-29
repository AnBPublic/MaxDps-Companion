--- ============================ HEADER ============================
-- In-game 13-toggle policy (bridge 3.3.0). Pure logic, no frames: the addon
-- only ever RESTRICTS what the companion already decided.
--
--   effective = companion AND addon; an addon OFF always wins.
--
-- Storage: MaxDpsBridgeDB.Toggles[<Key>] booleans. A missing key (fresh
-- install, downgrade, crafted SavedVariables) reads as ON, so the bridge
-- never blanks itself because a DB field is absent. This file therefore
-- never seeds defaults and every getter fails open.
--
-- Keys, in order (slots 1-8 first, then the four policy toggles + TTK):
--   Main Offensive Defensive Consumable Trinket Interrupt Mobility SelfHeal
--   Solo OOC AutoTarget AutoInteract TTK
--
-- SlotAllowed(slot, ctx) is the single gate Bridge.Update consults before it
-- writes a slot. It is deliberately context-driven: the only inputs are the
-- slot number and ctx{ InCombat, HpPct, Grouped } supplied by Bridge.lua,
-- which owns the game APIs (IsInGroup / UnitAffectingCombat) and the
-- sanitized MDB.GetPlayerHpPct(). No game API is called here, no value is
-- compared unless type() says it is a plain number/boolean, and the whole
-- body is pcall-contained and fails OPEN (allow). Secret values therefore
-- degrade to "allow", never to a blank slot or a throw.
--
-- EMERGENCY_HP mirrors the companion's emergency threshold
-- (AppSettings.SoloEmergencyHpPct / PolicyEvaluator.EmergencyHpPct = 35):
-- a player at-or-below 35% is an emergency and the Solo gate must not blank
-- Defensive(3)/SelfHeal(8) even while ungrouped.
--
-- Reason strings: every denial records MDB._LastBlank[slot] so `/mdb why`
-- and the status line can explain the blank ("main off", "solo not grouped").

local addonName, MDB = ...;

MDB._LastBlank = MDB._LastBlank or {};
MDB._LastAllow = MDB._LastAllow or {};

-- Monotonic dirty counter (Stream 1 §1.3): bumped by every Set/Flip/SetVeto so
-- a consumer (Bridge.OnUpdate dirty-flag, Stream 2) can cheaply tell whether
-- the toggle state changed since it last rendered. Starts at 0; never reset.
local VersionCounter = 0;
local function BumpVersion () VersionCounter = VersionCounter + 1; end

-- Match the companion's emergency HP (AppSettings.SoloEmergencyHpPct = 35).
local EMERGENCY_HP = 35;

-- Canonical key order. Keys() hands out a copy of this list.
local KEYS = {
  "Main", "Offensive", "Defensive", "Consumable", "Trinket", "Interrupt",
  "Mobility", "SelfHeal", "Solo", "OOC", "AutoTarget", "AutoInteract", "TTK",
};

-- Slot number -> per-slot toggle key (slots 1-8, in wire order).
local SLOT_KEY = {
  "Main", "Offensive", "Defensive", "Consumable",
  "Trinket", "Interrupt", "Mobility", "SelfHeal",
};

-- Case-insensitive lookup so "/mdb selfheal off" and Panel callbacks can use
-- the slash spellings; the canonical spelling is always what is stored.
local KEY_BY_LOWER = {};
for i = 1, #KEYS do KEY_BY_LOWER[KEYS[i]:lower()] = KEYS[i]; end

local LABELS = {
  Main = "Main",
  Offensive = "Offensive",
  Defensive = "Defensive",
  Consumable = "Consumable",
  Trinket = "Trinket",
  Interrupt = "Interrupt",
  Mobility = "Mobility",
  SelfHeal = "Self-heal",
  Solo = "Solo",
  OOC = "Out of combat",
  AutoTarget = "Auto-target",
  AutoInteract = "Auto-interact",
  TTK = "Time to kill",
};

local function Canon (Key)
  if type(Key) ~= "string" then return nil; end
  return KEY_BY_LOWER[Key:lower()];
end

--- Resolved state of one toggle: ON unless explicitly stored false. Unknown
-- keys and a missing/unreadable DB fail open (ON).
local function Get (Key)
  local C = Canon(Key);
  if not C then return true; end
  local DB = _G.MaxDpsBridgeDB;
  if type(DB) ~= "table" then return true; end
  local Toggles = DB.Toggles;
  if type(Toggles) ~= "table" then return true; end
  local Value = Toggles[C];
  if Value == nil then return true; end
  return Value ~= false;
end

--- Persist one toggle. Returns the stored boolean. nil/true store ON; only an
-- explicit false turns it off, matching "missing = ON".
local function Set (Key, On)
  local C = Canon(Key);
  if not C then return Get(Key); end
  local DB = _G.MaxDpsBridgeDB;
  if type(DB) ~= "table" then DB = {}; _G.MaxDpsBridgeDB = DB; end
  local Toggles = DB.Toggles;
  if type(Toggles) ~= "table" then Toggles = {}; DB.Toggles = Toggles; end
  local Value = (On ~= false);
  Toggles[C] = Value;
  BumpVersion();
  return Value;
end

local function Flip (Key)
  return Set(Key, not Get(Key));
end

-- ---- Group vetoes (Stream 1 §1.3) -------------------------------------
-- The per-ability ON/OFF veto lives companion-side; the addon can only
-- RESTRICT by whole category. These three keys let a player blank every
-- SelfHeal / Defensive / Immunity candidate in one click. Stored separately
-- from the 13 toggles (Keys() stays 13) and missing = ON, exactly like a
-- toggle: absent DB, absent Veto table or absent key reads as allow.
local VETO_GROUPS = { "SelfHeal", "Defensive", "Immunity" };

local function CanonVeto (Group)
  if type(Group) ~= "string" then return nil; end
  local Lower = Group:lower();
  for i = 1, #VETO_GROUPS do
    if VETO_GROUPS[i]:lower() == Lower then return VETO_GROUPS[i]; end
  end
  return nil;
end

local function GetVeto (Group)
  local C = CanonVeto(Group);
  if not C then return true; end
  local DB = _G.MaxDpsBridgeDB;
  if type(DB) ~= "table" then return true; end
  local Veto = DB.Veto;
  if type(Veto) ~= "table" then return true; end
  local Value = Veto[C];
  if Value == nil then return true; end
  return Value ~= false;
end

local function SetVeto (Group, On)
  local C = CanonVeto(Group);
  if not C then return GetVeto(Group); end
  local DB = _G.MaxDpsBridgeDB;
  if type(DB) ~= "table" then DB = {}; _G.MaxDpsBridgeDB = DB; end
  local Veto = DB.Veto;
  if type(Veto) ~= "table" then Veto = {}; DB.Veto = Veto; end
  local Value = (On ~= false);
  Veto[C] = Value;
  BumpVersion();
  return Value;
end

local function VetoKeys ()
  local Out = {};
  for i = 1, #VETO_GROUPS do Out[i] = VETO_GROUPS[i]; end
  return Out;
end

local function Keys ()
  local Out = {};
  for i = 1, #KEYS do Out[i] = KEYS[i]; end
  return Out;
end

local function Label (Key)
  local C = Canon(Key);
  if C and LABELS[C] then return LABELS[C]; end
  if type(Key) == "string" and Key ~= "" then return Key; end
  return "?";
end

--- Flat copy of all 13 resolved booleans, keyed by canonical name.
local function Snapshot ()
  local Out = {};
  for i = 1, #KEYS do Out[KEYS[i]] = Get(KEYS[i]); end
  return Out;
end

-- Reason texts are short and stable so `/mdb why` stays greppable.
local function Deny (Slot, Reason)
  MDB._LastBlank[Slot] = Reason;
  return false;
end

--- slot 1-8 gate. Returns true (allowed) or false (blank).
-- ctx = { InCombat = boolean|nil, HpPct = number|nil, Grouped = boolean|nil }.
-- Unknown ctx fields fail open (allow). Never throws; an internal error is
-- treated as "allow" so a gate bug can never silently mute the rotation.
local function SlotAllowed (Slot, Ctx)
  local Ok, Allowed = pcall(function ()
    if type(Slot) ~= "number" then return true; end
    Slot = math.floor(Slot);
    local Key = SLOT_KEY[Slot];
    if not Key then return true; end

    -- Fresh tick: clear this slot's allow diagnostic so a stale note from a
    -- previous tick never survives a later deny.
    if MDB._LastAllow then MDB._LastAllow[Slot] = nil; end

    -- 1) The slot's own toggle. ON = no restriction.
    if Get(Key) == false then
      return Deny(Slot, Key .. " off");
    end

    -- 1b) Group vetoes (restrict-only, missing = ON). Defensive/Immunity
    --     both blank the defensive slot 3; SelfHeal blanks self-heal slot 8.
    if GetVeto("Defensive") == false and Slot == 3 then
      return Deny(Slot, "defensive veto off");
    end
    if GetVeto("Immunity") == false and Slot == 3 then
      return Deny(Slot, "immunity veto off");
    end
    if GetVeto("SelfHeal") == false and Slot == 8 then
      return Deny(Slot, "self-heal veto off");
    end

    local Context = Ctx;
    if type(Context) ~= "table" then Context = nil; end

    -- 2) Out-of-combat master. Only a strict boolean false counts as
    --    "known out of combat"; nil/secret stays fail-open.
    if type(Context) == "table" then
      local InCombat = Context.InCombat;
      if Get("OOC") == false and type(InCombat) == "boolean" and InCombat == false then
        return Deny(Slot, "out of combat off");
      end

      -- 3) Solo only restrains the survival slots (Defensive 3, SelfHeal 8)
      --    while the player is known ungrouped. Emergency (<=35%) or unknown
      --    HP is always allowed, matching the companion's fail-open.
      local Grouped = Context.Grouped;
      if Get("Solo") == false and type(Grouped) == "boolean" and Grouped == false
        and (Slot == 3 or Slot == 8) then
        local Hp = Context.HpPct;
        local Emergency = true;   -- unknown HP = allow (fail open)
        local Unknown = true;
        if type(Hp) == "number" then
          Unknown = false;
          if Hp > EMERGENCY_HP then
            Emergency = false;    -- readable, above the emergency floor
          end
        end
        if not Emergency then
          return Deny(Slot, "solo not grouped");
        end
        -- Diagnostic only (/mdb why heal copy): the slot was allowed because
        -- the HP reading is unreadable. Never a blank reason.
        if Unknown then MDB._LastAllow[Slot] = "unknown HP allowed"; end
      end
    end

    MDB._LastBlank[Slot] = nil;
    return true;
  end);
  if Ok then return Allowed; end
  MDB._LastBlank = MDB._LastBlank or {};
  return true;   -- fail open on any gate error
end

MDB.Toggles = {
  Get = Get,
  Set = Set,
  Flip = Flip,
  Keys = Keys,
  Label = Label,
  Snapshot = Snapshot,
  SlotAllowed = SlotAllowed,
  -- Group vetoes (Stream 1 §1.3): restrict-only, missing = ON. Version is the
  -- monotonic dirty counter consumers poll instead of diffing the table.
  Veto = GetVeto,
  SetVeto = SetVeto,
  VetoKeys = VetoKeys,
  Version = function () return VersionCounter; end,
};

-- Convenience getters for Bridge.lua's non-slot gates (AutoTarget /
-- AutoInteract suppress the need-target / need-interact states, TTK forces
-- the target HP band to unknown).
function MDB.Toggles.IsTTK () return Get("TTK"); end
function MDB.Toggles.IsOOC () return Get("OOC"); end
function MDB.Toggles.IsAutoTarget () return Get("AutoTarget"); end
function MDB.Toggles.IsAutoInteract () return Get("AutoInteract"); end

-- ---- Crowd control (v3.4.0 CC appendix) -------------------------------
-- A 14th toggle that is deliberately NOT part of the canonical 13 (Keys()
-- stays 13 so the frozen toggle-count semantics and the 186-check harness are
-- unchanged). It is restrict-only and missing = ON, exactly like the others:
-- an absent DB / absent Toggles table / absent key reads as ON, and only an
-- explicit false turns CC off. Every accessor fails open.
--
-- effective = companion AND addon; an addon OFF wins. No wire field carries
-- this key, so the addon-side gate is published here for the bridge to consult
-- when it encodes CC candidates; that cross-surface wiring is OWED live. The
-- companion's own opt-in remains AppSettings.CrowdControlEnabled.
function MDB.Toggles.IsCC ()
  local Ok, Allowed = pcall(function ()
    local DB = _G.MaxDpsBridgeDB;
    if type(DB) ~= "table" then return true; end
    local Toggles = DB.Toggles;
    if type(Toggles) ~= "table" then return true; end
    local Value = Toggles.CC;
    if Value == nil then return true; end
    return Value ~= false;
  end);
  if Ok then return Allowed; end
  return true;
end

function MDB.Toggles.SetCC (On)
  local DB = _G.MaxDpsBridgeDB;
  if type(DB) ~= "table" then DB = {}; _G.MaxDpsBridgeDB = DB; end
  local Toggles = DB.Toggles;
  if type(Toggles) ~= "table" then Toggles = {}; DB.Toggles = Toggles; end
  local Value = (On ~= false);
  Toggles.CC = Value;
  BumpVersion();
  return Value;
end
