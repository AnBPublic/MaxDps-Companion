--- ============================ HEADER ============================
-- In-game 13-toggle policy (bridge 3.3.0), OVERLAY-WINS (2026-09-30, bridge
-- 3.5.0; semantics-only change - the Ext3 wire layout is byte-identical, so
-- neither PROTOCOL_VERSION nor the bridge release version is bumped).
-- Pure logic, no frames: authoritative in-game toggles restrict what the
-- companion may do.
--
--   epoch == 0: effective = the local boolean (missing key behaviour below).
--   epoch ~= 0: effective = the local boolean when the player has set one;
--               otherwise the app's pushed 14-bit mask bit is the default.
--               The app mask NEVER vetoes an explicit in-game value.
--
-- OVERLAY-WINS (2026-09-30 flip): the companion pushes a permissive all-ON
-- mask and the in-game overlay/panel is authoritative. A local toggle flip
-- takes effect on the very next frame even while the app epoch is live, and
-- the Ext3 mirror (cells 40-42) republishes the EFFECTIVE state so the
-- companion can read back the overlay (notably the OOC bit for its
-- out-of-combat hold). The old "app wins" masked veto is removed.
--
-- Storage: MaxDpsBridgeExpDB.Toggles[<Key>] booleans. An explicit local value
-- wins. A missing key reads as ON (fail open) for every key EXCEPT OOC:
-- out-of-combat automation is fail-CLOSED, so a missing OOC key reads OFF
-- (hold) unless the companion is live and pushed an OOC bit ON (its
-- permissive all-ON default). This file never seeds defaults.
--
-- Keys, in order (slots 1-8 first, then the four policy toggles + TTK):
--   Main Offensive Defensive Consumable Trinket Interrupt Mobility SelfHeal
--   Solo OOC AutoTarget AutoInteract TTK
--
-- SlotAllowed(slot, ctx) is the single gate Bridge.Update consults before it
-- writes a slot. It is deliberately context-driven: the only inputs are the
-- slot number and ctx{ InCombat, HpPct, Grouped } supplied by Bridge.lua,
-- which owns the game APIs (IsInGroup / UnitAffectingCombat) and the
-- sanitized MDBX.GetPlayerHpPct(). No game API is called here, no value is
-- compared unless type() says it is a plain number/boolean, and the whole
-- body is pcall-contained and fails OPEN (allow). Secret values therefore
-- degrade to "allow", never to a blank slot or a throw.
--
-- EMERGENCY_HP mirrors the companion's emergency threshold
-- (AppSettings.SoloEmergencyHpPct / PolicyEvaluator.EmergencyHpPct = 35):
-- a player at-or-below 35% is an emergency and the Solo gate must not blank
-- Defensive(3)/SelfHeal(8) even while ungrouped.
--
-- Reason strings: every denial records MDBX._LastBlank[slot] so `/mdb why`
-- and the status line can explain the blank ("main off", "solo not grouped").

local addonName, MDBX = ...;

MDBX._LastBlank = MDBX._LastBlank or {};
MDBX._LastAllow = MDBX._LastAllow or {};

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
-- The CC appendix key (Ext3 bit 13) is resolvable by the same Canon path as
-- the 13 canonical keys, but is NOT added to KEYS: Keys() stays 13 (frozen
-- toggle-count semantics / harness). It still needs LocalGet/Get/Set/Flip and
-- the EffectiveMask walk to see it, so register the lowercase spelling only.
KEY_BY_LOWER["cc"] = "CC";

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
  TTK = "TTK guard",
};

-- Short display labels (slice 1). DISPLAY ONLY: the wire keys and the full
-- LABELS above are unchanged. Every short label is <= 8 chars so a pill can
-- render without truncating at 4 columns / 300 px. Tooltips and command
-- output keep the full LABELS strings.
local SHORT_LABELS = {
  Main = "Main",
  Offensive = "Offense",
  Defensive = "Defense",
  Consumable = "Potion",
  Trinket = "Trinket",
  Interrupt = "Kick",
  Mobility = "Move",
  SelfHeal = "Heal",
  Solo = "Solo",
  OOC = "OOC",
  AutoTarget = "AutoTgt",
  AutoInteract = "Interact",
  TTK = "TTK",
  CC = "CC",
};

local function Canon (Key)
  if type(Key) ~= "string" then return nil; end
  return KEY_BY_LOWER[Key:lower()];
end

-- ---- App-pushed mask defaults (v3.5 / Ext3) -----------------------------
-- The desktop app pushes a 14-bit toggle mask over `/mdb mask <hhhh> <e>`
-- (only out of combat). The bridge stores it as the companion's DEFAULT:
-- while the epoch is non-zero the app bit fills in for any key the player
-- has not explicitly set. The in-game overlay is authoritative (overlay-wins)
-- and the app mask never vetoes an explicit local value.
--
-- The 14 bits map to the 13 canonical keys (slot order first) plus the CC
-- appendix key: bit0 Main ... bit7 SelfHeal, bit8 Solo, bit9 OOC,
-- bit10 AutoTarget, bit11 AutoInteract, bit12 TTK, bit13 CC. All 14 ON =
-- 0x3FFF. Fail open: a missing/malformed mask reads all-ON and a nil epoch
-- reads 0 (no companion defaults, local + OOC fail-closed).
local BIT_BY_KEY = {
  Main = 0, Offensive = 1, Defensive = 2, Consumable = 3,
  Trinket = 4, Interrupt = 5, Mobility = 6, SelfHeal = 7,
  Solo = 8, OOC = 9, AutoTarget = 10, AutoInteract = 11, TTK = 12, CC = 13,
};
local ALL_MASK = 0x3FFF;

local function GetAppEpoch ()
  local DB = _G.MaxDpsBridgeExpDB;
  if type(DB) ~= "table" then return 0; end
  local E = DB.AppEpoch;
  if type(E) ~= "number" or E < 0 or E > 15 then return 0; end
  return math.floor(E);
end

local function GetAppMask ()
  local DB = _G.MaxDpsBridgeExpDB;
  if type(DB) ~= "table" then return ALL_MASK; end
  local M = DB.AppMask;
  if type(M) ~= "number" or M < 0 or M > ALL_MASK then return ALL_MASK; end
  return math.floor(M);
end

local function GetAppBlocked ()
  local DB = _G.MaxDpsBridgeExpDB;
  if type(DB) ~= "table" then return 0; end
  local B = DB.AppBlocked;
  if type(B) ~= "number" or B < 0 or B > 15 then return 0; end
  return math.floor(B);
end

local function LocalGet (Key)
  local C = Canon(Key);
  if not C then return true; end
  local DB = _G.MaxDpsBridgeExpDB;
  if type(DB) ~= "table" then return true; end
  local Toggles = DB.Toggles;
  if type(Toggles) ~= "table" then return true; end
  local Value = Toggles[C];
  if Value == nil then return true; end
  return Value ~= false;
end

local function AppControlled () return GetAppEpoch() ~= 0; end

--- True when the player has explicitly stored this key (a non-nil DB value).
-- A missing key is what triggers the app-default / fail-closed fallbacks.
local function LocalSet (Key)
  local C = Canon(Key);
  if not C then return false; end
  local DB = _G.MaxDpsBridgeExpDB;
  if type(DB) ~= "table" then return false; end
  local Toggles = DB.Toggles;
  if type(Toggles) ~= "table" then return false; end
  return Toggles[C] ~= nil;
end

--- The resolved value of one toggle, OVERLAY-WINS.
-- An explicit in-game boolean is authoritative with or without a live app
-- epoch (the app mask never vetoes it). With no local value, the live app
-- mask supplies the default; without a companion, OOC is fail-closed OFF
-- (hold) and every other key keeps the historical fail-open (missing = ON).
local function Get (Key)
  local C = Canon(Key);
  if not C then return true; end
  if LocalSet(C) then return LocalGet(C); end
  if AppControlled() then
    local Bit = BIT_BY_KEY[C];
    if Bit then return bit.band(GetAppMask(), bit.lshift(1, Bit)) ~= 0; end;
    return true;
  end
  if C == "OOC" then return false; end
  return true;
end

--- Persist one toggle. Returns the stored boolean. nil/true store ON; only an
-- explicit false turns it off, matching "missing = ON".
local function Set (Key, On)
  local C = Canon(Key);
  if not C then return Get(Key); end
  local DB = _G.MaxDpsBridgeExpDB;
  if type(DB) ~= "table" then DB = {}; _G.MaxDpsBridgeExpDB = DB; end
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
  local DB = _G.MaxDpsBridgeExpDB;
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
  local DB = _G.MaxDpsBridgeExpDB;
  if type(DB) ~= "table" then DB = {}; _G.MaxDpsBridgeExpDB = DB; end
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

--- Compact display label (slice 1), <= 8 chars. Falls back to Label() for an
-- unmapped key so callers always get something printable. Display only.
local function ShortLabel (Key)
  local C = Canon(Key);
  if C and SHORT_LABELS[C] then return SHORT_LABELS[C]; end;
  if type(Key) == "string" and Key ~= "" and #Key <= 8 then return Key; end;
  return Label(Key);
end

--- Flat copy of all 13 resolved booleans, keyed by canonical name.
local function Snapshot ()
  local Out = {};
  for i = 1, #KEYS do Out[KEYS[i]] = Get(KEYS[i]); end
  return Out;
end

-- Reason texts are short and stable so `/mdb why` stays greppable.
local function Deny (Slot, Reason)
  MDBX._LastBlank[Slot] = Reason;
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
    if MDBX._LastAllow then MDBX._LastAllow[Slot] = nil; end

    -- 1) The slot's own EFFECTIVE toggle. Overlay-wins: an explicit in-game
    --    OFF denies here (and is no longer bypassed while the app epoch is
    --    live); with no local value the app mask bit supplies the default.
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
        if Unknown then MDBX._LastAllow[Slot] = "unknown HP allowed"; end
      end
    end

    MDBX._LastBlank[Slot] = nil;
    return true;
  end);
  if Ok then return Allowed; end
  MDBX._LastBlank = MDBX._LastBlank or {};
  return true;   -- fail open on any gate error
end

-- Blocked nibble bit1 (value 2): the app mask command was refused (in
-- combat, or a malformed/absent mask/epoch). Published in Ext3 cell 41 B.
local BLOCKED_APP = 2;

local function ClampN (V, Lo, Hi, Def)
  if type(V) ~= "number" or V ~= V then return Def; end
  V = math.floor(V);
  if V < Lo then return Lo; end
  if V > Hi then return Hi; end
  return V;
end

--- Store the app mask/epoch pushed by the companion. Everything is clamped
-- to its wire range; the reserved bit stays 0. Returns the stored triple.
local function SetAppMask (Mask, Epoch, Blocked)
  local DB = _G.MaxDpsBridgeExpDB;
  if type(DB) ~= "table" then DB = {}; _G.MaxDpsBridgeExpDB = DB; end
  DB.AppMask = ClampN(Mask, 0, ALL_MASK, ALL_MASK);
  DB.AppEpoch = ClampN(Epoch, 0, 15, 0);
  DB.AppBlocked = ClampN(Blocked, 0, 15, 0);
  BumpVersion();
  return DB.AppMask, DB.AppEpoch, DB.AppBlocked;
end

local function SetAppBlocked (Nibble)
  local DB = _G.MaxDpsBridgeExpDB;
  if type(DB) ~= "table" then DB = {}; _G.MaxDpsBridgeExpDB = DB; end
  DB.AppBlocked = ClampN(Nibble, 0, 15, 0);
  BumpVersion();
  return DB.AppBlocked;
end

--- The 14-bit EFFECTIVE mask the bridge publishes on the wire (Ext3 mirror).
-- Overlay-wins: rebuilt from Get() for all 14 bits (13 canonical + CC), so
-- the companion/panel sees the in-game truth, not the app's pushed mask. The
-- companion reads bit 9 (OOC) for its out-of-combat hold, so an in-game OOC
-- OFF must clear this bit immediately even while the app epoch is live.
local function EffectiveMask ()
  local M = 0;
  for Key, Bit in pairs(BIT_BY_KEY) do
    if Get(Key) then M = bit.bor(M, bit.lshift(1, Bit)); end
  end
  return M;
end

MDBX.Toggles = {
  Get = Get,
  Set = Set,
  Flip = Flip,
  Keys = Keys,
  Label = Label,
  ShortLabel = ShortLabel,
  Snapshot = Snapshot,
  SlotAllowed = SlotAllowed,
  -- App mask / epoch (v3.5 Ext3): the companion's pushed DEFAULT while live.
  -- The in-game overlay is authoritative (overlay-wins); these accessors stay
  -- for the panel/status mirror and the mask command.
  AppMask = GetAppMask,
  AppEpoch = GetAppEpoch,
  AppBlocked = GetAppBlocked,
  SetAppMask = SetAppMask,
  SetAppBlocked = SetAppBlocked,
  AppControlled = AppControlled,
  EffectiveMask = EffectiveMask,
  LocalGet = LocalGet,
  LocalSet = LocalSet,
  BLOCKED_APP = BLOCKED_APP,
  -- True when the app is live AND the player has explicitly set this key to
  -- the opposite of the pushed app bit (a diagnostic badge only; the local
  -- value always wins in Get, so this never changes the effective value).
  Conflict = function (Key)
    if not AppControlled() then return false; end
    local C = Canon(Key);
    if not C then return false; end
    local Bit = BIT_BY_KEY[C];
    if not Bit then return false; end
    if not LocalSet(C) then return false; end
    local AppOn = bit.band(GetAppMask(), bit.lshift(1, Bit)) ~= 0;
    return LocalGet(C) ~= AppOn;
  end,
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
function MDBX.Toggles.IsTTK () return Get("TTK"); end
function MDBX.Toggles.IsOOC () return Get("OOC"); end
function MDBX.Toggles.IsAutoTarget () return Get("AutoTarget"); end
function MDBX.Toggles.IsAutoInteract () return Get("AutoInteract"); end

-- ---- Crowd control (v3.4.0 CC appendix) -------------------------------
-- A 14th toggle that is deliberately NOT part of the canonical 13 (Keys()
-- stays 13 so the frozen toggle-count semantics and the harness are
-- unchanged). It lives in BIT_BY_KEY (Ext3 bit 13) and resolves through the
-- ordinary Canon/Get overlay-wins path: an explicit in-game CC value wins
-- even while the app epoch is live; a missing key falls back to the live app
-- mask bit, then to ON. It is restrict-only and missing = ON, exactly like
-- the others: an absent DB / absent Toggles table / absent key reads as ON,
-- and only an explicit false turns CC off. Every accessor fails open — IsCC
-- delegates to Get("CC") inside a pcall and returns true on any throw.
--
-- The app mask no longer vetoes an in-game ON. The companion's own opt-in
-- remains AppSettings.CrowdControlEnabled.
function MDBX.Toggles.IsCC ()
  local Ok, Allowed = pcall(Get, "CC");
  if Ok then return Allowed ~= false; end
  return true;
end

function MDBX.Toggles.SetCC (On)
  local DB = _G.MaxDpsBridgeExpDB;
  if type(DB) ~= "table" then DB = {}; _G.MaxDpsBridgeExpDB = DB; end
  local Toggles = DB.Toggles;
  if type(Toggles) ~= "table" then Toggles = {}; DB.Toggles = Toggles; end
  local Value = (On ~= false);
  Toggles.CC = Value;
  BumpVersion();
  return Value;
end
