--- ============================ HEADER ============================
-- Renders MaxDps's current suggestions as a row of flat-coloured squares in
-- a screen corner. MaxDpsCompanion.exe samples those pixels and replays the
-- encoded keystrokes.
--
-- Protocol v5 + Ext2/Ext3 -- 43 cells, left to right, each CellSize physical
-- pixels square (cells 0-34 the v5 core, 35-39 the Ext2 block, 40-42 the Ext3
-- app toggle mask block; the version nibble stays 5 throughout).
-- Every channel carries one nibble, encoded as nibble * 17 (0, 17, ... 255),
-- which leaves enough headroom to survive gamma and scaling.
--
-- ADDITIVE defensive urgency (v2.3): cell 31 G (HP curve), cell 31 B bit0
-- (catalog gap-fill source) and cell 32 B (stagger curve) were RESERVED
-- nibbles in v5 and are always 0 in pre-2.3 encoders. Writing them does not
-- change the wire version: old companions ignore reserved nibbles, their
-- extension checksum over cells 11-33 already covers them, and the version
-- nibble stays 5 — so an updated in-game addon keeps working with an older
-- companion exe (the reverse skew was rejected before). Zero still means
-- UNKNOWN, so old frames decode conservatively.
--
-- Slot order = the user's icon model plus two companion-only extras:
--   1 main rotation (THE core functionality -- MaxDps.Spell), 2 offensive,
--   3 defensive, 4 consumable, 5 trinket, 6 interrupt, 7 mobility,
--   8 self-heal. Slots 7-8 come from the generated Catalog.lua extras
--   (per class/spec curated lists); MaxDps never surfaces them itself.
--
--   cell 0        magic        R=15 G=0 B=15 (magenta; presence + alignment)
--   cells 1-8     slots        R=vk hi G=vk lo B=flags
--   cell 9        status       R=state G=heartbeat B=flags (combat/gcd/target/ctx)
--   cell 10       version      R=5 G=checksum(cells 1-9) B=commit (=heartbeat)
--   cells 11-26   spell ids    6 nibbles per slot (2 cells): MSB in cell A.R
--   cell 27       vitals       R=hp% hi G=hp% lo B=bit0 valid
--   cell 28       cast state   R=0 none/1 cast/2 channel/15 unknown G=band
--                              B=bits0-1 SelfHeal2 range tri-state (0/1/2)
--   cell 29       target       R=bit0 melee bit1 melee-unknown
--                              G=target hp band 0-14 (15 unknown)
--                              B=bit0 casting bit1 unknown bit2 interruptible
--                                bit3 not-interruptible
--   cells 30-31   range        2 bits per slot: 0 unknown / 1 in / 2 out
--   cell 31 G     defensive urgency  0 unknown/1 white/2 yellow/3 orange/4 red
--   cell 31 B     bit0 = defensive slot is a catalog gap-fill candidate
--   cell 32       buff active  R=bits0-3 slots 1-4 G=bits0-3 slots 5-8
--                 B = stagger-curve defensive urgency (Purifying Brew)
--   cell 33       class/spec   R=class id G=spec id
--                              B=bit0 valid bit1 buff-valid bit2 EXT2 bit3 hpcurve
--   cell 34       ext checksum R=0 G=checksum(cells 11-33) B=commit
--   cell 35       HP curve     SetVertexColor(UnitHealthPercent colour); black off
--   cell 36       SelfHeal2    R=vk hi G=vk lo B=flags
--   cells 37-38   SelfHeal2 id 6 nibbles
--   cell 39       Ext2 checksum R=0 G=checksum(cells 36-38) B=commit
--   cell 28 B bit2 Ext3 PRESENT (app mask block live)
--   cells 40-41   Ext3 app mask (14 bits) + epoch + blocked nibble
--   cell 42       Ext3 checksum R=0 G=checksum(cells 40-41) B=commit
--
-- The defensive urgency mirrors MaxDps's own glow curves at their control
-- points (vendor Buttons.lua:1056-1110); see Reader.lua.
--
-- checksum = sum of R+G+B nibbles, mod 16. commit must equal heartbeat or the
-- frame is torn and the companion drops it.
--
-- flags: bit0 Shift, bit1 Ctrl, bit2 Alt, bit3 slot valid
-- state: 0 idle, 1 active, 2 paused (bridge off / calibrate),
--   3 need-target (no/dead/unattackable target), 4 need-interact (melee range)
--
-- Calibrate mode (`/mdb calibrate on`): renders a deterministic pattern the
-- desktop app samples to learn the display chain (RenoDX / RTX HDR / ICC /
-- ReShade). cell 0 = magic reference, cells 1-8 = flat level for the current
-- step, cell 9 = Paused so the companion holds instead of sending keys.
-- Steps advance every ~8 ticks (~260 ms dwell at 33 ms/update).
-- `/mdb calibrate off` (or `/mdb off`) returns to normal rendering.
--
-- READ-ONLY: no protected calls, no CastSpell/TargetUnit/InteractUnit/UseAction.
-- Secret-safety: every sensor degrades to UNKNOWN (never throws) -- see the
-- header of Reader.lua for the Midnight rules.

local addonName, MDB = ...;

MDB.VERSION = "3.5.2";

-- Chat print, defined FIRST: AutoCalibrate (below) and the Update watchdog
-- both call it, and Lua resolves locals lexically -- a later `local
-- function Print` would be invisible here and call a nil global instead
-- (that was the 711x "attempt to call a nil value" spam: the error also
-- skipped the LastContact reset, so the watchdog refired every 50ms).
local function Print (Message)
  DEFAULT_CHAT_FRAME:AddMessage("|cFF00D8FFMDB|r: " .. Message);
end

-- Protocol v5 (bridge 3.0.0): 40 cells. Additive blocks on reserved nibbles:
-- defensive urgency (cell 31 G/B, cell 32 B) from bridge 2.3 and the self-buff
-- probe-block validity bit (cell 33 B bit1) from bridge 2.7. The version nibble
-- STAYS 5, so an older companion exe still decodes this strip (it ignores
-- reserved bits; the extension checksum already covered them). Zero means
-- UNKNOWN and the companion keeps its documented fail-open.
-- The status cell B keeps the v4 combat/GCD/target flags and v5's bit3 =
-- context-valid. Cells 1-8 keep the v4 slot semantics so the companion's
-- legacy fallback decode stays valid.
-- Ext2 (bridge 3.0.0, protocol v5 additive): the strip grows to 40 cells and
-- the trailing block is ignored by old companions (cell 33 B bit2 = EXT2
-- PRESENT). Cells 0-34 keep the exact v5 semantics; the version nibble STAYS
-- 5. Cell 35 is the HP-curve colour, cells 36-38 are the SelfHeal2 slot and
-- cell 39 is the Ext2 checksum. The v5 checksums (cells 10 and 34) keep
-- covering only their original ranges.
local CELL_COUNT = 43;
local PROTOCOL_VERSION = 5;

local STATUS_FLAG_IN_COMBAT = 1;
local STATUS_FLAG_ON_GCD = 2;
local STATUS_FLAG_HAS_TARGET = 4;
local STATUS_FLAG_CONTEXT_VALID = 8;

-- v5 cell map.
local STATUS_CELL = 9;
local VERSION_CELL = 10;
local SPELLID_CELL = 11;      -- 2 cells per slot
local VITALS_CELL = 27;
local CAST_CELL = 28;
local TARGET_CELL = 29;
local RANGE_CELL = 30;
local RANGE_CELL2 = 31;
local BUFF_CELL = 32;
local CLASSSPEC_CELL = 33;
local EXT_CELL = 34;
local SLOT_COUNT = 8;

-- Ext2 (v3.0.0) additive block.
local HP_CURVE_CELL = 35;
local SELFHEAL2_CELL = 36;
local SELFHEAL2_ID_CELL = 37;   -- cells 37-38
local EXT2_CELL = 39;

-- Ext3 (v3.5) additive block: 14-bit app toggle mask + epoch + blocked nibble
-- (cells 40-42), presence = cell 28 B bit2. The version nibble stays 5.
local EXT3_MASK_CELL = 40;
local EXT3_FLAGS_CELL = 41;
local EXT3_COMMIT_CELL = 42;
local CAST_EXT3_PRESENT = 4;    -- cell 28 B bit2
local EXT3_MASK_BITS = 0x3FFF;
local EXT3_BLOCKED_APP = 2;     -- blocked nibble bit1

local CLASS_SPEC_VALID = 1;
local BUFF_VALID = 2;
local EXT2_PRESENT = 4;
local HP_CURVE_ACTIVE = 8;

-- Slots whose range is probed per tick (target-facing only; the other slots
-- are self-targeted and the policy ignores their range). Slot 8 (SelfHeal) is
-- probed because some curated self-heals are melee attacks (Impending
-- Victory, Death Strike): a confirmed out-of-range reading must skip the
-- candidate instead of spending a failed press and a retry window on it.
local RANGE_SLOTS = { [1] = true, [2] = true, [3] = true, [6] = true, [7] = true, [8] = true };

local UPDATE_INTERVAL = 0.033;

local STATE_IDLE   = 0;
local STATE_ACTIVE = 1;
local STATE_PAUSED = 2;
local STATE_NEED_TARGET = 3;
local STATE_NEED_INTERACT = 4;

local FLAG_VALID = 8;

-- v5 cast-state nibbles (mirror PixelProtocol).
local CAST_STATE_NONE = 0;
local CAST_STATE_CASTING = 1;
local CAST_STATE_CHANNELING = 2;
local CAST_STATE_UNKNOWN = 15;

local Defaults = {
  Enabled = true,
  -- Fail-closed pause memory. Set with `/mdb off`, cleared only by `/mdb on`:
  -- while true the strip stays a Paused frame and the auto-revive nudge is
  -- suppressed, so nothing can silently resume the rotation after a pause.
  UserPaused = false,
  OffsetX = 0,
  OffsetY = 0,
  -- Aethys-proven 8px cells: ±1px misalignment tolerance by construction.
  -- 1px cells have zero tolerance (sub-pixel offset, UI-scale rounding or
  -- HDR dither corrupts the single read point) and failed live.
  CellSize = 8,
  Calibrate = false,
  -- Ext2 HP-curve cell 35: on by default. Off paints black and clears
  -- cell 33 B bit3, so the companion falls back to its plain vitals path.
  HpCurve = true,
  -- v3.5 rotating slots (3/6/7/8): each candidate is offered for this many
  -- rendered ticks (the frame heartbeat) before the weighted sequence
  -- [top, next, top, next2] advances to the following one.
  RotationDwell = 3,
  -- v3.5 app mask (Ext3). Epoch 0 = no companion defaults; a non-zero epoch
  -- means the desktop app's 14-bit mask fills in for unset in-game toggles.
  -- OVERLAY-WINS: an explicit in-game value always beats this mask.
  AppMask = 0x3FFF,
  AppEpoch = 0,
  AppBlocked = 0,
  -- 3.3.0 in-game 13-toggle policy (Toggles.lua). OVERLAY-WINS (2026-09-30):
  -- an explicit in-game value is authoritative; the live app mask only fills
  -- in for unset keys. OOC is deliberately NOT seeded: out-of-combat is
  -- fail-closed (missing OOC = hold) until the player sets it or the live
  -- companion pushes an OOC bit ON. Every other unset key reads ON.
  Toggles = {
    Main = true, Offensive = true, Defensive = true, Consumable = true,
    Trinket = true, Interrupt = true, Mobility = true, SelfHeal = true,
    Solo = true, AutoTarget = true, AutoInteract = true, TTK = true,
  },
  -- 3.3.0 overlay/panel geometry (Panel.lua consumes this). Missing keys read
  -- as the defaults; Bridge.lua persists the state, Panel.lua owns the frame.
  Ui = { Overlay = false, Point = "CENTER", X = 0, Y = 0, Scale = 1.0, Minimap = false },
};

-- Per-key merge: never replace a whole sub-table, so a DB persisted by an
-- older bridge keeps the toggles it already has and only gains the keys this
-- build knows about (that is what makes old SavedVariables forward-valid).
-- Tables in Defaults are copied recursively, never aliased: mutating
-- DB.Toggles must not mutate Defaults.Toggles.
local function MergeDefaults (DB, Def)
  for Key, Value in pairs(Def) do
    if type(Value) == "table" then
      if type(DB[Key]) ~= "table" then DB[Key] = {}; end
      MergeDefaults(DB[Key], Value);
    elseif DB[Key] == nil then
      DB[Key] = Value;
    end
  end
end

local Root, Cells;
local Heartbeat = 0;
local Elapsed = 0;
local CalStep = 0;
local CalTick = 0;
local HpCurveBase = false;     -- cell 35 base (white) painted for vertex colour
local EnsureEngineElapsed = 0; -- B7: 1/s EnsureEngine throttle for a cold login

-- Explicit user pause (`/mdb off`). Sticky until `/mdb on` (or a same-session
-- `Enabled=true` from another writer is overridden by fail-closed hold below).
-- Distinct from transient `Enabled` so an auto-revive can never resume play.
local UserPaused = false;

function MDB.IsUserPaused () return UserPaused == true; end

-- Single writer for the sticky flag: `/mdb on|off` and the Options checkbox
-- both route through here so no UI path can bypass the fail-closed hold.
function MDB.SetUserPaused (Flag)
  UserPaused = (Flag == true);
  if type(MaxDpsBridgeDB) == "table" then MaxDpsBridgeDB.UserPaused = UserPaused; end
  return UserPaused;
end

-- Single writer for the Enabled/UserPaused PAIR. `/mdb on|off|toggle` and the
-- Options checkbox route here so the two can never diverge: a bare
-- `DB.Enabled = false` would be re-seeded as a sticky pause on the next login,
-- and a bare `DB.Enabled = true` would not clear an existing pause. Keeping the
-- write here is what makes Options.lua + Panel.lua pause-safe (review finding 5).
function MDB.SetEnabled (Flag)
  local On = (Flag ~= false);
  if type(MaxDpsBridgeDB) == "table" then MaxDpsBridgeDB.Enabled = On; end
  MDB.SetUserPaused(not On);
  return On;
end

--- ======= PIXEL PLUMBING =======

local function SetNibbles (Index, R, G, B)
  Cells[Index]:SetColorTexture(R / 15, G / 15, B / 15, 1);
end

-- Cached paint: slot/status cells skip SetColorTexture when nibbles are
-- unchanged. Heartbeat ticks every update so the status/checksum cells almost
-- always repaint; slot cells repaint only on change.
local LastPaint = {};

-- PERF (Stream 2, 3.4.0): cached slot-intent scan. Update computes the
-- expensive candidate scan (scrubbed flag scans, per-candidate readiness and
-- keybind resolution) only when Reader.FrameKey changes; otherwise it reuses
-- these nibbles. Sensors (vitals/cast/target/range/aura/class-spec/urgency/
-- HP-curve) are still written every tick. Invalidated by Layout (LastPaint is
-- cleared there) and by the Paused/disabled frame so the strip always repaints.
local SlotCache = {
  Valid = false, Key = nil,
  R = {}, G = {}, B = {}, Id = {},
  DefCatalog = false, Heal2 = nil, SH2 = nil,
};
local function Paint (Index, R, G, B)
  local Key = R * 256 + G * 16 + B;
  if LastPaint[Index] == Key then return false; end
  LastPaint[Index] = Key;
  SetNibbles(Index, R, G, B);
  return true;
end

local function Layout ()
  local DB = MaxDpsBridgeDB;
  if not Root or not DB then return; end
  -- Cancel out the UI scale so that one frame unit is exactly one screen pixel.
  Root:SetScale(1 / UIParent:GetEffectiveScale());
  Root:ClearAllPoints();
  Root:SetPoint("TOPLEFT", UIParent, "TOPLEFT", DB.OffsetX, -DB.OffsetY);
  Root:SetSize(DB.CellSize * CELL_COUNT, DB.CellSize);
  for i = 0, CELL_COUNT - 1 do
    Cells[i]:ClearAllPoints();
    Cells[i]:SetPoint("TOPLEFT", Root, "TOPLEFT", DB.CellSize * i, 0);
    Cells[i]:SetSize(DB.CellSize, DB.CellSize);
  end
  local Stale = _G.MaxDpsBridge_Border;
  if Stale then Stale:Hide(); end
  LastPaint = {};
  SlotCache.Valid = false;   -- the repainted strip must be re-derived too
  HpCurveBase = false;
  SetNibbles(0, 15, 0, 15);
  LastPaint[0] = 15 * 256 + 0 * 16 + 15;
end

MDB.Layout = Layout;

-- Manual auto-calibration: forget position/size, back to the default
-- corner. Explicit /mdb autocal only -- no timer.
local function AutoCalibrate (Why)
  local DB = MaxDpsBridgeDB;
  if not DB then return; end
  DB.OffsetX = Defaults.OffsetX;
  DB.OffsetY = Defaults.OffsetY;
  DB.CellSize = Defaults.CellSize;
  DB.Calibrate = false;
  CalStep = 0;
  CalTick = 0;
  Layout();
  Print("auto-calibrated (" .. Why .. ") - strip is back at 0,0");
end

MDB.AutoCalibrate = AutoCalibrate;

local function CreateBlock ()
  Root = CreateFrame("Frame", "MaxDpsBridge_Block", UIParent);
  Root:SetFrameStrata("TOOLTIP");
  Root:SetFrameLevel(128);
  Root:EnableMouse(false);

  Cells = {};
  LastPaint = {};
  for i = 0, CELL_COUNT - 1 do
    Cells[i] = Root:CreateTexture(nil, "OVERLAY");
    Cells[i]:SetColorTexture(0, 0, 0, 1);
    LastPaint[i] = 0;
  end

  Layout();
  Root:Show();
end

--- ======= MAXDPS READOUT =======

-- Resolves readiness + binding for one suggestion and paints the slot cell.
-- Returns the painted nibbles plus the (post-gate) spell id so Update can
-- paint the id cells and the extension checksum.
--
-- v1.3.2: SkipGate=true for the MAIN slot (cell 1) -- upstream's glow IS the
-- castability verdict; re-gating it with tainted reads vetoed correct mains.
-- Situational slots keep the gate; unready spells encode as EMPTY (FLAG_VALID
-- clear) so the companion skips the slot and fires the next ready one.
local function WriteSlot (CellIndex, SpellID, IsInterrupt, SkipGate)
  if SpellID then
    if type(SpellID) ~= "number" or SpellID == 0 then
      SpellID = nil;
    elseif not SkipGate then
      local Ok, Ready = pcall(function ()
        if IsInterrupt then return MDB.IsInterruptReady(SpellID);
        else return MDB.IsSpellReady(SpellID); end
      end);
      if not Ok or not Ready then SpellID = nil; end
    end
  end
  local VirtualKey, Modifiers = MDB.ResolveBinding(SpellID);
  if not VirtualKey then
    Paint(CellIndex, 0, 0, 0);
    return 0, 0, 0, nil;
  end
  local Hi = bit.rshift(VirtualKey, 4);
  local Lo = bit.band(VirtualKey, 0x0F);
  local Flags = bit.bor(Modifiers or 0, FLAG_VALID);
  Paint(CellIndex, Hi, Lo, Flags);
  return Hi, Lo, Flags, SpellID;
end

-- Spell id cells: 24-bit id, 6 nibbles, two cells. 0 = no identity (the
-- companion treats the ability as uncatalogued and fails open).
local function WriteSpellIdAt (Base, SpellID)
  if type(SpellID) ~= "number" or SpellID < 0 or SpellID > 0xFFFFFF then SpellID = 0; end
  local Hi12 = bit.band(bit.rshift(SpellID, 12), 0x0FFF);
  local Lo12 = bit.band(SpellID, 0x0FFF);
  Paint(Base, bit.band(bit.rshift(Hi12, 8), 0x0F), bit.band(bit.rshift(Hi12, 4), 0x0F), bit.band(Hi12, 0x0F));
  Paint(Base + 1, bit.band(bit.rshift(Lo12, 8), 0x0F), bit.band(bit.rshift(Lo12, 4), 0x0F), bit.band(Lo12, 0x0F));
end

local function WriteSpellId (SlotIndex, SpellID)
  WriteSpellIdAt(SPELLID_CELL + (SlotIndex - 1) * 2, SpellID);
end

local function IdNibbleSum (SpellID)
  if type(SpellID) ~= "number" or SpellID < 0 or SpellID > 0xFFFFFF then return 0; end
  local Sum = 0;
  local Value = SpellID;
  for _ = 1, 6 do
    Sum = Sum + bit.band(Value, 0x0F);
    Value = bit.rshift(Value, 4);
  end
  return Sum;
end

-- Target state is read-only: UnitExists/UnitIsDead/UnitCanAttack and
-- CheckInteractDistance never taint and never act on the world.
local function TargetState ()
  if type(UnitExists) ~= "function" then return nil; end
  if not UnitExists("target") then return STATE_NEED_TARGET; end
  if type(UnitIsDead) == "function" and UnitIsDead("target") then
    return STATE_NEED_TARGET;
  end
  if type(UnitCanAttack) == "function" and not UnitCanAttack("player", "target") then
    return STATE_NEED_TARGET;
  end
  if type(InCombatLockdown) == "function" and not InCombatLockdown()
     and type(CheckInteractDistance) == "function" then
    local Ok, Near = pcall(CheckInteractDistance, "target", 3);
    if Ok and Near then return STATE_NEED_INTERACT; end
  end
  return nil;
end

local function WriteStatus (State, Flags)
  Flags = Flags or 0;
  Paint(STATUS_CELL, State, Heartbeat, Flags);
  return State, Heartbeat, Flags;
end

-- NeverSecret / plain-unit status flags only; any failure leaves the flag
-- clear (companion fails open). v5 adds bit3 = context valid: the sensor
-- block was computed (with UNKNOWN values where Blizzard hid a value, which
-- is not the same as "no sensors").
local function StatusFlags (ContextValid)
  local Flags = 0;
  if type(UnitAffectingCombat) == "function" then
    local Ok, InC = pcall(UnitAffectingCombat, "player");
    if Ok and InC == true then Flags = bit.bor(Flags, STATUS_FLAG_IN_COMBAT); end
  end
  if _G.C_Spell and type(_G.C_Spell.GetSpellCooldown) == "function" then
    local Ok, Info = pcall(_G.C_Spell.GetSpellCooldown, 61304);
    if Ok and type(Info) == "table" and Info.isOnGCD == true then
      Flags = bit.bor(Flags, STATUS_FLAG_ON_GCD);
    end
  end
  if type(UnitExists) == "function" then
    local OkE, Exists = pcall(UnitExists, "target");
    if OkE and Exists == true then
      local Dead = false;
      if type(UnitIsDead) == "function" then
        local OkD, D = pcall(UnitIsDead, "target");
        Dead = (OkD and D == true);
      end
      local Attackable = true;
      if type(UnitCanAttack) == "function" then
        local OkA, A = pcall(UnitCanAttack, "player", "target");
        Attackable = (OkA and A == true);
      end
      if not Dead and Attackable then
        Flags = bit.bor(Flags, STATUS_FLAG_HAS_TARGET);
      end
    end
  end
  if ContextValid then Flags = bit.bor(Flags, STATUS_FLAG_CONTEXT_VALID); end
  return Flags;
end

local function WriteVersion (Checksum)
  Paint(VERSION_CELL, PROTOCOL_VERSION, Checksum, Heartbeat);
end

--- ======= v5 SENSOR CELLS =======

local function WriteVitals ()
  local Hi, Lo, Flags = 0, 0, 0;
  if MDB.GetPlayerHpPct then
    local Pct = MDB.GetPlayerHpPct();
    if Pct then
      if Pct < 0 then Pct = 0 elseif Pct > 100 then Pct = 100 end
      Hi = math.floor(Pct / 16);
      Lo = Pct % 16;
      Flags = 1;
    end
  end
  Paint(VITALS_CELL, Hi, Lo, Flags);
  return Hi, Lo, Flags;
end

local function WriteCast (SelfHeal2Range)
  local State = CAST_STATE_UNKNOWN;
  if MDB.GetCastState then State = MDB.GetCastState(); end
  local Band = 15;   -- remaining-time band is not safely observable; reserved
  -- Ext2: cell 28 B bits0-1 carry the SelfHeal2 range tri-state (0 unknown /
  -- 1 in / 2 out). Ext3 (v3.5): bit2 = EXT3 PRESENT (the app mask block is
  -- meaningful, cells 40-42 are live). Bits3 reserved.
  local Range = bit.bor(bit.band(SelfHeal2Range or 0, 3), CAST_EXT3_PRESENT);
  Paint(CAST_CELL, State, Band, Range);
  return State, Band, Range;
end

-- ForceUnknownBand (3.3.0 TTK OFF): the wall-clock time-to-kill estimate is
-- untrusted, so the target HP band is forced to 15 = UNKNOWN. MeleeFlag and
-- CastFlags are kept untouched -- only the HP band goes blind (execute/band
-- consumers on the companion side also go blind; documented collateral).
local function WriteTarget (ForceUnknownBand)
  local MeleeFlag, HpBand, CastFlags = 0, 15, 0;
  if MDB.GetTargetContext then
    MeleeFlag, HpBand, CastFlags = MDB.GetTargetContext();
  end
  if type(CastFlags) ~= "number" then CastFlags = 0; end
  if ForceUnknownBand then HpBand = 15; end
  Paint(TARGET_CELL, MeleeFlag or 0, HpBand or 15, CastFlags);
  return MeleeFlag or 0, HpBand or 15, CastFlags;
end

local function WriteRanges (Ids, Urgency, DefSource)
  local Tri = {};
  for i = 1, SLOT_COUNT do
    local Value = 0;
    if RANGE_SLOTS[i] and Ids[i] and MDB.GetSlotRange then
      Value = MDB.GetSlotRange(Ids[i]);
    end
    Tri[i] = bit.band(Value or 0, 3);
  end
  local R30 = bit.bor(Tri[1], bit.lshift(Tri[2], 2));
  local G30 = bit.bor(Tri[3], bit.lshift(Tri[4], 2));
  local B30 = bit.bor(Tri[5], bit.lshift(Tri[6], 2));
  local R31 = bit.bor(Tri[7], bit.lshift(Tri[8], 2));
  local G31 = bit.band(Urgency or 0, 0x0F);   -- v6 defensive urgency
  local B31 = (DefSource and 1) or 0;         -- v6 gap-fill source bit
  Paint(RANGE_CELL, R30, G30, B30);
  Paint(RANGE_CELL2, R31, G31, B31);
  return R30, G30, B30, R31, G31, B31;
end

local function WriteBuffs (Ids, StaggerUrgency)
  local R, G = 0, 0;
  local Valid = 1;
  if MDB.GetSlotBuff then
    for i = 1, 8 do
      local Buff = 0;
      if Ids[i] and Ids[i] ~= 0 then
        local Probe = MDB.GetSlotBuff(Ids[i]);
        if type(Probe) == "number" then
          Buff = Probe;
        else
          -- v2.7: a failed/secret probe invalidates the whole block (cell 33
          -- B bit1 stays 0) so the companion sees UNKNOWN, never a silent
          -- "not active". Legacy encoders wrote 0 here and the companion's
          -- documented fail-open keeps their behaviour identical.
          Valid = 0;
        end
      end
      if Buff == 1 then
        if i <= 4 then
          R = bit.bor(R, bit.lshift(1, i - 1));
        else
          G = bit.bor(G, bit.lshift(1, i - 5));
        end
      end
    end
  else
    Valid = 0;
  end
  local B = bit.band(StaggerUrgency or 0, 0x0F);   -- v6 stagger urgency
  Paint(BUFF_CELL, R, G, B);
  return R, G, B, Valid;
end

local function WriteClassSpec (BuffValid, HpCurveOn)
  local ClassId, SpecId = 0, 0;
  if MDB.GetClassSpec then ClassId, SpecId = MDB.GetClassSpec(); end
  local Valid = 0;
  if type(ClassId) == "number" and ClassId > 0 then Valid = 1; else ClassId = 0; end
  if type(SpecId) ~= "number" then SpecId = 0; end
  -- bit0 class/spec valid, bit1 self-buff probe block valid (v2.7 additive),
  -- bit2 EXT2 PRESENT (v3.0.0), bit3 HP CURVE ACTIVE. Pre-2.7/3.0 encoders
  -- leave the upper bits 0; old companions read only the bits they know.
  local Flags = Valid;
  if BuffValid == 1 then Flags = bit.bor(Flags, BUFF_VALID); end
  Flags = bit.bor(Flags, EXT2_PRESENT);
  if HpCurveOn == 1 then Flags = bit.bor(Flags, HP_CURVE_ACTIVE); end
  Paint(CLASSSPEC_CELL, ClassId, SpecId, Flags);
  return ClassId, SpecId, Flags;
end

-- Ext2 cell 35: the HP-curve colour. The bridge never reads the player HP
-- value — it passes MDB.HpCurve (linear green->red) into UnitHealthPercent
-- and forwards the returned colour straight into SetVertexColor. Nothing is
-- compared, stringified or arithmetized. Returns 1 when the cell is live so
-- cell 33 B bit3 is set; 0 (black cell) otherwise.
local function WriteHpCurve ()
  local Tex = Cells[HP_CURVE_CELL];
  if not Tex then return 0; end
  local Curve = nil;
  if MaxDpsBridgeDB.HpCurve ~= false and MDB.EnsureHpCurve then
    local OkC, C = pcall(MDB.EnsureHpCurve);
    if OkC then Curve = C; end
  end
  if not Curve then
    Tex:SetColorTexture(0, 0, 0, 1);
    HpCurveBase = false;
    LastPaint[HP_CURVE_CELL] = nil;
    return 0;
  end
  if not HpCurveBase then
    -- White base once; SetVertexColor multiplies it, so the per-frame path
    -- is a single engine call.
    Tex:SetColorTexture(1, 1, 1, 1);
    HpCurveBase = true;
  end
  -- The returned colour object is secret-capable: it is only ever passed
  -- through GetRGBA -> SetVertexColor, all inside the pcall. A failure
  -- degrades to black and bit3=0.
  local Ok = pcall(function ()
    local Color = UnitHealthPercent("player", false, Curve);
    Tex:SetVertexColor(Color:GetRGBA());
  end);
  LastPaint[HP_CURVE_CELL] = nil;   -- never let Paint() clobber the colour
  if Ok then return 1; end
  Tex:SetColorTexture(0, 0, 0, 1);
  HpCurveBase = false;
  return 0;
end

-- Ext2 cells 36-39: the SelfHeal2 slot. Cell 36 = vk hi|lo|flags, cells 37-38
-- = the spell id, cell 39 = R0 / G = checksum(cells 36-38) mod 16 / B commit.
-- A missing/blank candidate still paints a valid zero block so the companion
-- can distinguish "no second self-heal" from "old addon". The nibbles come
-- from the cached scan on a clean tick, so this repaints without re-resolving
-- readiness/binding; cell 39 (heartbeat + checksum) is rewritten every tick.
local function WriteSelfHeal2Raw (Raw)
  local Hi, Lo, Flags, Id = Raw.Hi or 0, Raw.Lo or 0, Raw.Flags or 0, Raw.Id or 0;
  local Sum = Hi + Lo + Flags + IdNibbleSum(Id);
  WriteSpellIdAt(SELFHEAL2_ID_CELL, Id);
  Paint(EXT2_CELL, 0, Sum % 16, Heartbeat);
end

-- Ext3 cells 40-42 (v3.5): 14-bit app toggle mask + epoch + blocked nibble.
--   cell 40: R = mask bits 0-3, G = bits 4-7, B = bits 8-11
--   cell 41: R = mask bits 12-13, G = epoch (0-15), B = blocked nibble
--   cell 42: R = 0 (reserved), G = checksum(cells 40-41) mod 16, B = commit
-- The block is always painted (presence bit 2 of cell 28 B) so the panel and
-- the companion can read the addon's effective mask even when the app is not
-- controlling it (epoch 0). No core/Ext2 checksum covers cells 40-42.
local function WriteExt3 (Mask, Epoch, Blocked)
  if type(Mask) ~= "number" or Mask < 0 or Mask > EXT3_MASK_BITS then Mask = EXT3_MASK_BITS; end
  if type(Epoch) ~= "number" or Epoch < 0 or Epoch > 15 then Epoch = 0; end
  if type(Blocked) ~= "number" or Blocked < 0 or Blocked > 15 then Blocked = 0; end
  Mask, Epoch, Blocked = math.floor(Mask), math.floor(Epoch), math.floor(Blocked);
  local M0 = bit.band(Mask, 0xF);
  local M1 = bit.band(bit.rshift(Mask, 4), 0xF);
  local M2 = bit.band(bit.rshift(Mask, 8), 0xF);
  local MHi = bit.band(bit.rshift(Mask, 12), 0x3);
  Paint(EXT3_MASK_CELL, M0, M1, M2);
  Paint(EXT3_FLAGS_CELL, MHi, Epoch, Blocked);
  local Sum = M0 + M1 + M2 + MHi + Epoch + Blocked;
  Paint(EXT3_COMMIT_CELL, 0, Sum % 16, Heartbeat);
  return Sum % 16;
end

-- A complete, valid v5 frame with no suggestions and no context. Used while
-- the bridge is disabled so the companion decodes "Paused" instead of
-- treating the stale cells as a torn frame.
local function WriteEmptyFrame (State)
  -- A disabled/Paused frame is not the cached scan; force a full rescan when
  -- the bridge is re-enabled (the underlying suggestion may have moved on).
  SlotCache.Valid = false;
  for i = 1, SLOT_COUNT do
    Paint(i, 0, 0, 0);
    WriteSpellId(i, 0);
  end
  local Vh, Vl, Vf = 0, 0, 0;
  Paint(VITALS_CELL, Vh, Vl, Vf);
  local Ch, Cg, Cf = CAST_STATE_UNKNOWN, 15, CAST_EXT3_PRESENT;
  Paint(CAST_CELL, Ch, Cg, Cf);
  local Th, Tg, Tf = 0, 15, 2;   -- cast state unknown
  Paint(TARGET_CELL, Th, Tg, Tf);
  Paint(RANGE_CELL, 0, 0, 0);
  Paint(RANGE_CELL2, 0, 0, 0);
  Paint(BUFF_CELL, 0, 0, 0);
  -- Ext2 present (bit2) but curve inactive (bit3 clear): a Paused frame still
  -- advertises the block so the companion knows 35-39 are meaningful.
  local Kh, Kg, Kf = 0, 0, EXT2_PRESENT;
  Paint(CLASSSPEC_CELL, Kh, Kg, Kf);
  local Tex = Cells[HP_CURVE_CELL];
  if Tex then Tex:SetColorTexture(0, 0, 0, 1); end
  HpCurveBase = false;
  LastPaint[HP_CURVE_CELL] = nil;
  Paint(SELFHEAL2_CELL, 0, 0, 0);
  WriteSpellIdAt(SELFHEAL2_ID_CELL, 0);
  Paint(EXT2_CELL, 0, 0, Heartbeat);
  -- Ext3: publish the effective mask (app or local) even on a Paused frame.
  local EMask, EEpoch, EBlocked = EXT3_MASK_BITS, 0, 0;
  if MDB.Toggles then
    if MDB.Toggles.EffectiveMask then EMask = MDB.Toggles.EffectiveMask(); end
    if MDB.Toggles.AppEpoch then EEpoch = MDB.Toggles.AppEpoch(); end
    if MDB.Toggles.AppBlocked then EBlocked = MDB.Toggles.AppBlocked(); end
  end
  WriteExt3(EMask, EEpoch, EBlocked);
  local Sr, Sg, Sb = WriteStatus(State, 0);
  WriteVersion((Sr + Sg + Sb) % 16);
  -- v5 extension checksum covers cells 11-33 exactly (Ext2 cells excluded).
  local ExtSum = Ch + Cg + Cf + Th + Tg + Tf + Kh + Kg + Kf;
  Paint(EXT_CELL, 0, ExtSum % 16, Heartbeat);
end

--- ======= v3.5 ROTATION (slots 3/6/7/8) =======
-- One candidate per slot is not enough when the first ready+bound entry is
-- held by policy: it shadows every alternative (RC4). Each rotating slot owns
-- an independent clock; every `RotationDwell` rendered ticks the weighted
-- sequence [top, next, top, next2] advances (the top candidate is offered on
-- two of the four steps). A defensive urgency of Orange or Red pins slot 3 on
-- top for the whole dwell. The rotation only re-encodes when it advances
-- (SlotCache is dropped), so an unchanged scan stays cached.
local RotationState = {};
local ROT_SEQ = { 1, 2, 1, 3 };
local ROTATING = { [3] = true, [6] = true, [7] = true, [8] = true };
MDB._RotCounts = MDB._RotCounts or {};

local function RotationDwell ()
  local D = MaxDpsBridgeDB and tonumber(MaxDpsBridgeDB.RotationDwell) or 3;
  if D < 1 then D = 1 elseif D > 60 then D = 60; end
  return math.floor(D);
end

-- Advance the clocks once per rendered tick. Returns true when a slot
-- advanced, so the caller drops the cached slot scan. DefPin holds slot 3.
local function RotationTick (DefPin)
  local Advanced = false;
  local Dwell = RotationDwell();
  for Slot in pairs(ROTATING) do
    local St = RotationState[Slot];
    if not St then St = { Ticks = 0, Step = 1 }; RotationState[Slot] = St; end
    if DefPin and Slot == 3 then
      St.Ticks = 0;
    else
      St.Ticks = St.Ticks + 1;
      if St.Ticks >= Dwell then
        St.Ticks = 0;
        St.Step = (St.Step % #ROT_SEQ) + 1;
        Advanced = true;
      end
    end
  end
  return Advanced;
end

-- Weighted position into a pool of Count candidates, clamped so a short pool
-- still rotates (Count 1 always returns top).
local function RotationPos (Slot, Count, Pin)
  if type(Count) ~= "number" or Count < 1 then return nil; end
  if Pin then return 1; end
  local St = RotationState[Slot];
  if not St then St = { Ticks = 0, Step = 1 }; RotationState[Slot] = St; end
  local Pos = ROT_SEQ[St.Step] or 1;
  if Pos > Count then Pos = Count; end
  return Pos;
end

-- Test/diagnostic reset: forget every slot clock (next tick offers top).
function MDB.ResetRotation ()
  RotationState = {};
  MDB._RotCounts = {};
end

local function Update (self, Delta)
  Elapsed = Elapsed + Delta;
  if Elapsed < UPDATE_INTERVAL then return; end
  Elapsed = 0;

  Heartbeat = (Heartbeat + 1) % 16;

  -- Calibrate pattern: hold the companion (state = Paused) while the
  -- desktop app learns the display chain. Steps advance every ~8 ticks
  -- (~260 ms dwell at 33 ms/update) so the sampler locks through
  -- post-processing; steps 0/52 and 1/53 are black/white anchors repeated
  -- late in the cycle so black/white can be re-found.
  -- Only cells 1-8 carry the flat level: the learner averages exactly
  -- cells 1-8 and requires the status cell to read Paused.
  if MaxDpsBridgeDB.Calibrate then
    CalTick = CalTick + 1;
    if CalTick >= 8 then
      CalTick = 0;
      CalStep = (CalStep + 1) % 54;
    end
    Paint(0, 15, 0, 15);
    local Kind, Level = nil, 0;
    if CalStep == 0 or CalStep == 52 then
      Kind = "flat"; Level = 0;                                   -- black
    elseif CalStep == 1 or CalStep == 53 then
      Kind = "flat"; Level = 15;                                  -- white
    elseif CalStep < 18 then
      Kind = "r"; Level = CalStep - 2;                            -- red ramp 0..15
    elseif CalStep < 34 then
      Kind = "g"; Level = CalStep - 18;                           -- green ramp 0..15
    else
      Kind = "b"; Level = CalStep - 34;                           -- blue ramp 0..15
      if Level > 15 then Level = 15; end
    end
    for i = 1, SLOT_COUNT do
      if Kind == "flat" then
        Paint(i, Level, Level, Level);
      elseif Kind == "r" then
        Paint(i, Level, 0, 0);
      elseif Kind == "g" then
        Paint(i, 0, Level, 0);
      else
        Paint(i, 0, 0, Level);
      end
    end
    -- Valid core checksum + commit==heartbeat so the strip still decodes as
    -- a Paused frame (companion holds) instead of a torn frame.
    local SR, SG, SB = WriteStatus(STATE_PAUSED);
    local SlotSum;
    if Kind == "flat" then
      SlotSum = SLOT_COUNT * (Level * 3);
    else
      SlotSum = SLOT_COUNT * Level;
    end
    WriteVersion((SlotSum + SR + SG + SB) % 16);
    return;
  end

  -- A user pause outranks a transient `Enabled` write: keep emitting the
  -- Paused frame (state = 2) so the companion holds instead of firing.
  if not MaxDpsBridgeDB.Enabled or UserPaused then
    WriteEmptyFrame(STATE_PAUSED);
    return;
  end

  MDB.EnsureHooks();

  -- 3.3.0 in-game toggle context. Built ONCE per tick from plain, pcall-
  -- contained game APIs; every field is boolean/number-or-nil so Toggles.lua
  -- never sees a secret (nil/unknown makes its gates fail OPEN = allow).
  local Ctx;
  do
    local InCombat = nil;
    if type(UnitAffectingCombat) == "function" then
      local OkC, V = pcall(UnitAffectingCombat, "player");
      if OkC then InCombat = (V == true); end
    end
    local HpPct = nil;
    if MDB.GetPlayerHpPct then
      local OkH, V = pcall(MDB.GetPlayerHpPct);
      if OkH and type(V) == "number" then HpPct = V; end
    end
    local Grouped = nil;
    if type(IsInGroup) == "function" then
      local OkG, V = pcall(IsInGroup);
      if OkG then Grouped = (V == true); end
    end
    Ctx = { InCombat = InCombat, HpPct = HpPct, Grouped = Grouped };
  end

  local Toggles = MDB.Toggles;
  -- Single slot gate. One call per slot: the per-slot key (slots 1-8), the
  -- OOC master (InCombat == false blanks all), and Solo (blank defensive /
  -- self-heal while known ungrouped, emergency HP excepted) all live in
  -- Toggles.SlotAllowed. Denials are recorded in MDB._LastBlank.
  local function Allowed (Slot)
    if Toggles and Toggles.SlotAllowed then return Toggles.SlotAllowed(Slot, Ctx); end
    return true;
  end

  -- v3.3.0 Solo ladder bands (ADDITIVE, in-game truth): armed ONLY while the
  -- in-game Solo toggle is ON and the player is known ungrouped; Solo OFF or
  -- grouped (or unknown group state) disarms every band so group behaviour is
  -- byte-identical to pre-3.3.0. Bands mirror the companion defaults
  -- (minor 75 / major 50 / immunity 30) and degrade fail-open (nil = disarm).
  do
    local SoloOn = Toggles and Toggles.Get and Toggles.Get("Solo");
    local Ungrouped = type(Ctx.Grouped) == "boolean" and Ctx.Grouped == false;
    if SoloOn and Ungrouped then
      MDB.SoloLadderBands = { minor = 75, major = 50, immunity = 30 };
    else
      MDB.SoloLadderBands = nil;
    end
  end

  -- B7: a cold idle login can leave MaxDps.NextSpell nil with nothing
  -- scheduled, so the strip (and the companion) sits idle until first
  -- combat. Nudge the engine exactly like the login event path does, but
  -- only while no spell has been fetched and at most once a second.
  local MaxDpsGlobal = _G.MaxDps;
  if (not MaxDpsGlobal or not MaxDpsGlobal.Spells or not next(MaxDpsGlobal.Spells))
    and not UserPaused
    and MDB.EnsureEngine then
    EnsureEngineElapsed = EnsureEngineElapsed + Delta;
    if EnsureEngineElapsed >= 1 then
      EnsureEngineElapsed = 0;
      pcall(MDB.EnsureEngine);
    end
  else
    EnsureEngineElapsed = 0;
  end

  -- PERF: reset the per-tick memo ONCE per update (the slot getters share
  -- one Flags scrub, one item map and one class/spec resolve per tick).
  if MDB.BeginTick then MDB.BeginTick(); end

  -- v3.5 rotation clock + defensive pin. The pin reads the HP urgency before
  -- the scan (the wire urgency later is identity-aware); a secret HP stays
  -- UNKNOWN and never pins.
  local DefPin = false;
  if MDB.GetDefensiveUrgencyNibble then
    local OkPin, Pin = pcall(MDB.GetDefensiveUrgencyNibble, nil);
    DefPin = (OkPin and type(Pin) == "number" and Pin >= 3);
  end
  if RotationTick(DefPin) then SlotCache.Valid = false; end

  -- Containment: a getter that throws must never abort the frame (which
  -- would leave every slot stale) or spam the UI error handler. Degrade to
  -- nil and warn exactly once.
  local function SafeRead (Fn)
    if type(Fn) ~= "function" then return nil; end
    local Ok, Value = pcall(Fn);
    if Ok then return Value; end
    if not MDB._ReadoutWarned then
      MDB._ReadoutWarned = true;
      Print("a MaxDps read failed; the bridge keeps running (see /mdb diag)");
    end
    return nil;
  end

  -- PERF 3.4.0 DIRTY-FLAG. FrameKey captures every cheap input that can
  -- change a slot candidate (suggestion, HP band affecting the defensive
  -- gap-fill, combat/group-adjacent toggle state, readiness/binding/variant
  -- resolver identities, per-spec curated lists). When it is unchanged the
  -- scan below is skipped and the cached nibbles are reused; sensor cells are
  -- still recomputed further down and cells 9/10/34/39 (heartbeat + status +
  -- both checksums) are rewritten every tick. A nil key fails open to a full
  -- scan, so a client without the helper still behaves exactly as before.
  local Key = nil;
  do
    local OkK, K = pcall(MDB.FrameKey);
    if OkK and type(K) == "string" then Key = K; end
  end
  local Clean = Key ~= nil and SlotCache.Valid and SlotCache.Key == Key;

  local R, G, B, Id = {}, {}, {}, {};
  local DefCatalog = false;
  local Heal2 = nil;
  local SH2 = nil;   -- Ext2 cells 36-38 raw nibbles { Hi, Lo, Flags, Id }

  if Clean then
    for i = 1, SLOT_COUNT do
      R[i], G[i], B[i], Id[i] = SlotCache.R[i], SlotCache.G[i], SlotCache.B[i], SlotCache.Id[i];
    end
    DefCatalog = SlotCache.DefCatalog;
    Heal2 = SlotCache.Heal2;
    SH2 = SlotCache.SH2;
  else
    -- 3.3.0 gate: check the toggle BEFORE the reader call, so an OFF slot
    -- never even invokes MaxDps (no wasted work, no side effects). A denied
    -- slot paints an all-zero/FLAG_VALID-clear cell (= EMPTY).
    local function ReadSlot (Slot, Fn, IsInterrupt, SkipGate)
      if not Allowed(Slot) then return 0, 0, 0, nil; end
      return WriteSlot(Slot, SafeRead(Fn), IsInterrupt, SkipGate);
    end
    R[1], G[1], B[1], Id[1] = ReadSlot(1, MDB.GetMainSpellID, false, true);
    -- Offensive: MaxDps flagged+bound first; when MaxDps names none the
    -- curated gap-fill list supplies the first ready+bound entry (v3.0.0). No
    -- source bit is encoded: the companion detects a gap-fill by id membership
    -- in the shared generated offensive list (see docs/PROTOCOL.md).
    R[2], G[2], B[2], Id[2] = ReadSlot(2, MDB.GetOffensiveSpellID);
    -- Slots 3/6/7/8 rotate over their multi-candidate pool (v3.5). The pool
    -- is ready+bound+known, never-automatic filtered, cap 4; the weighted
    -- position comes from the per-slot clock advanced at the top of Update.
    local DefId = nil;
    if Allowed(3) then
      local Pool = (MDB.RotationCandidates and MDB.RotationCandidates(3, 4)) or {};
      MDB._RotCounts[3] = #Pool;
      local Pos = RotationPos(3, #Pool, DefPin);
      if Pos then DefId = Pool[Pos]; end
      if DefId then
        -- Source bit: clear only when the offered id is MaxDps's own flagged
        -- candidate; any catalog entry is a gap-fill (cell 31 B bit0).
        local FlaggedId = nil;
        if MDB.GetDefensiveCandidate then
          local OkFlag, Flagged, FlaggedCatalog = pcall(MDB.GetDefensiveCandidate);
          if OkFlag and type(Flagged) == "number" and Flagged ~= 0 and FlaggedCatalog == false then
            FlaggedId = Flagged;
          end
        end
        DefCatalog = (FlaggedId == nil) or (DefId ~= FlaggedId);
      end
    end
    R[3], G[3], B[3], Id[3] = WriteSlot(3, DefId);
    R[4], G[4], B[4], Id[4] = ReadSlot(4, MDB.GetConsumableSpellID);
    R[5], G[5], B[5], Id[5] = ReadSlot(5, MDB.GetTrinketSpellID);
    -- Interrupt (slot 6): a live, ready MaxDps interrupt that the target
    -- sensor confirms is a casting, interruptible cast pins the slot;
    -- otherwise the slot rotates the curated CC pool. v3.5: the pin uses the
    -- casting-only helper so a blind (non-casting) interrupt never pins, and
    -- the CC pool is offered instead.
    do
      local IntId = nil;
      if Allowed(6) then
        IntId = SafeRead(MDB.GetInterruptSpellID);
        local PinReady = false;
        if IntId then
          local PinFn = MDB.IsInterruptPinReady or MDB.IsInterruptReady;
          local OkInt, Ready = pcall(PinFn, IntId);
          PinReady = OkInt and Ready == true;
        end
        if PinReady then
          MDB._RotCounts[6] = 1;
        else
          local Pool = (MDB.RotationCandidates and MDB.RotationCandidates(6, 4)) or {};
          MDB._RotCounts[6] = #Pool;
          local Pos = RotationPos(6, #Pool, false);
          IntId = Pos and Pool[Pos] or nil;
        end
      end
      R[6], G[6], B[6], Id[6] = WriteSlot(6, IntId);
    end
    -- Mobility rotation.
    do
      local MobId = nil;
      if Allowed(7) then
        local Pool = (MDB.RotationCandidates and MDB.RotationCandidates(7, 4)) or {};
        MDB._RotCounts[7] = #Pool;
        local Pos = RotationPos(7, #Pool, false);
        MobId = Pos and Pool[Pos] or nil;
      end
      R[7], G[7], B[7], Id[7] = WriteSlot(7, MobId);
    end
    -- Ext2: slot 8 offers the rotated self-heal; the Ext2 block keeps the
    -- NEXT DISTINCT candidate so the companion always sees two options. A
    -- denied SelfHeal slot nils BOTH, so the block blanks too.
    local Heal1 = nil;
    if Allowed(8) then
      local Pool = (MDB.RotationCandidates and MDB.RotationCandidates(8, 4)) or {};
      MDB._RotCounts[8] = #Pool;
      local Pos = RotationPos(8, #Pool, false);
      if Pos then
        Heal1 = Pool[Pos];
        for Off = 1, #Pool do
          local Next = Pool[((Pos - 1 + Off) % #Pool) + 1];
          if Next ~= Heal1 then Heal2 = Next; break; end
        end
      end
    end
    R[8], G[8], B[8], Id[8] = WriteSlot(8, Heal1);
    -- Resolve the second self-heal's stroke/identity once here; the clean
    -- path reuses it and only repaints cells 36-39.
    local S2Hi, S2Lo, S2Flags, S2Id = WriteSlot(SELFHEAL2_CELL, Heal2);
    SH2 = { Hi = S2Hi or 0, Lo = S2Lo or 0, Flags = S2Flags or 0, Id = S2Id or 0 };

    MDB._SlotScans = (MDB._SlotScans or 0) + 1;   -- diagnostics/bench only
    if Key ~= nil then
      SlotCache.Valid = true;
      SlotCache.Key = Key;
      for i = 1, SLOT_COUNT do
        SlotCache.R[i], SlotCache.G[i], SlotCache.B[i], SlotCache.Id[i] = R[i], G[i], B[i], Id[i];
      end
      SlotCache.DefCatalog = DefCatalog;
      SlotCache.Heal2 = Heal2;
      SlotCache.SH2 = SH2;
    else
      -- Without a key a clean tick can never be proven; stay in full-scan mode.
      SlotCache.Valid = false;
    end
  end

  for i = 1, SLOT_COUNT do
    WriteSpellId(i, Id[i] or 0);
  end

  -- v6 defensive urgency: the HP-curve stage for the defensive slot's spell
  -- (stagger curve for Purifying Brew, with the vendor's HP fallback), and
  -- the stagger stage on its own for the policy's per-ability source choice.
  -- Every getter is pcall-contained; anything unexpected stays 0 = UNKNOWN.
  local Urgency, StaggerUrgency = 0, 0;
  if MDB.GetDefensiveUrgencyFallback then
    local OkU, U = pcall(MDB.GetDefensiveUrgencyFallback, Id[3]);
    if OkU and type(U) == "number" and U >= 0 and U <= 4 then Urgency = U; end
  elseif MDB.GetDefensiveUrgencyNibble then
    local OkU, U = pcall(MDB.GetDefensiveUrgencyNibble, Id[3]);
    if OkU and type(U) == "number" and U >= 0 and U <= 4 then Urgency = U; end
  end
  if MDB.GetStaggerUrgency then
    local OkS, S = pcall(MDB.GetStaggerUrgency);
    if OkS and type(S) == "number" and S >= 0 and S <= 4 then StaggerUrgency = S; end
  end

  local AnySlot = false;
  local SlotNibbleSum = 0;
  for i = 1, SLOT_COUNT do
    -- Core checksum covers cells 1..9 ONLY (slot cells + status). The spell
    -- id cells are covered by the extension checksum below.
    SlotNibbleSum = SlotNibbleSum + R[i] + G[i] + B[i];
    if bit.band(B[i], FLAG_VALID) == FLAG_VALID then AnySlot = true; end
  end

  local Vh, Vl, Vf = WriteVitals();
  local SelfHeal2Range = 0;
  if Heal2 and MDB.GetSlotRange then
    local OkR2, R2 = pcall(MDB.GetSlotRange, Heal2);
    if OkR2 and type(R2) == "number" then SelfHeal2Range = bit.band(R2, 3); end
  end
  local Ch, Cg, Cf = WriteCast(SelfHeal2Range);
  -- TTK OFF forces the target HP band to UNKNOWN (MeleeFlag/CastFlags kept).
  local Th, Tg, Tf = WriteTarget(Toggles and Toggles.IsTTK and not Toggles.IsTTK());
  local R30, G30, B30, R31, G31, B31 = WriteRanges(Id, Urgency, DefCatalog);
  local Bh, Bg, Bb, BuffValid = WriteBuffs(Id, StaggerUrgency);
  -- Ext2 cell 35 + bits: paint the curve first so its activity bit is known.
  local HpCurveOn = WriteHpCurve();
  local Kh, Kg, Kf = WriteClassSpec(BuffValid, HpCurveOn);
  -- Ext2 cells 36-39: the second distinct SelfHeal candidate (cached scan on
  -- a clean tick; cell 39's heartbeat+checksum is rewritten every tick).
  WriteSelfHeal2Raw(SH2);

  -- v3.5 Ext3: publish the EFFECTIVE 14-bit toggle mask + app epoch + blocked
  -- nibble. OVERLAY-WINS: EffectiveMask() rebuilds the mask from the resolved
  -- in-game state every frame, so an in-game flip (notably OOC off) reaches
  -- the companion even while the app epoch is live. Presence bit in cell 28 B.
  local PubMask, PubEpoch, PubBlocked = EXT3_MASK_BITS, 0, 0;
  if MDB.Toggles then
    if MDB.Toggles.EffectiveMask then PubMask = MDB.Toggles.EffectiveMask(); end
    if MDB.Toggles.AppEpoch then PubEpoch = MDB.Toggles.AppEpoch(); end
    if MDB.Toggles.AppBlocked then PubBlocked = MDB.Toggles.AppBlocked(); end
  end
  WriteExt3(PubMask, PubEpoch, PubBlocked);

  -- v1.3.4 MELEE-STATE FIX: a live suggestion ALWAYS wins the state (Active).
  -- Target/interact states exist ONLY so the companion can ask for a target
  -- / interact when MaxDps has NOTHING to cast -- never to suppress a
  -- pending rotation.
  local State;
  if AnySlot then
    State = STATE_ACTIVE;
  else
    State = TargetState() or STATE_IDLE;
    -- 3.3.0: AutoTarget / AutoInteract OFF suppress the corresponding ask
    -- state to Idle. Status flags are NOT touched (they still report the
    -- real target/combat facts); only the state nibble goes quiet.
    if State == STATE_NEED_TARGET and Toggles and Toggles.IsAutoTarget
      and not Toggles.IsAutoTarget() then
      State = STATE_IDLE;
    elseif State == STATE_NEED_INTERACT and Toggles and Toggles.IsAutoInteract
      and not Toggles.IsAutoInteract() then
      State = STATE_IDLE;
    end
  end

  local SR, SG, SB = WriteStatus(State, StatusFlags(true));
  WriteVersion((SlotNibbleSum + SR + SG + SB) % 16);

  -- Extension checksum over cells 11-33 (the spell ids, vitals, cast,
  -- target, range + defensive urgency, buffs + stagger urgency and
  -- class/spec cells).
  local ExtSum = 0;
  for i = 1, SLOT_COUNT do ExtSum = ExtSum + IdNibbleSum(Id[i]); end
  ExtSum = ExtSum + Vh + Vl + Vf + Ch + Cg + Cf + Th + Tg + Tf
    + R30 + G30 + B30 + R31 + G31 + B31 + Bh + Bg + Bb + Kh + Kg + Kf;
  Paint(EXT_CELL, 0, ExtSum % 16, Heartbeat);
end

--- ======= SLASH COMMANDS =======
-- (Print is defined at the top so AutoCalibrate can use it.)

-- Lower-case spelling -> canonical toggle key, built from Toggles.Keys() so
-- the slash surface stays in lockstep with the policy table (no second list).
local ToggleKeyLower = {};
if MDB.Toggles and MDB.Toggles.Keys then
  local SlashKeys = MDB.Toggles.Keys();
  for i = 1, #SlashKeys do ToggleKeyLower[SlashKeys[i]:lower()] = SlashKeys[i]; end
end

local function TogglesLine ()
  if not (MDB.Toggles and MDB.Toggles.Keys and MDB.Toggles.Get) then return "unavailable"; end
  local Keys = MDB.Toggles.Keys();
  local On, Off = 0, {};
  for i = 1, #Keys do
    if MDB.Toggles.Get(Keys[i]) then On = On + 1; else Off[#Off + 1] = Keys[i]; end
  end
  local Text = ("%d/%d ON"):format(On, #Keys);
  if #Off > 0 then Text = Text .. "; OFF: " .. table.concat(Off, ", "); end
  return Text;
end

local function HandleCommand (Input)
  local Command, Arg1, Arg2 = strsplit(" ", strlower(strtrim(Input or "")));
  local DB = MaxDpsBridgeDB;

  -- Diagnostics live in Reader.lua (needs MaxDps internals there).
  if Command == "diag" and MDB.Diag then
    MDB.Diag();
    return;
  end

  if Command == "heal" then
    if MDB.SelfHealDiag then
      local OkHeal, Text = pcall(MDB.SelfHealDiag);
      Print(OkHeal and Text or "heal diag failed (contained)");
    else
      Print("heal diag unavailable");
    end
    return;
  end

  if Command == "hpcurve" then
    if DB.HpCurve == nil then DB.HpCurve = true; end
    if Arg1 == "on" then
      DB.HpCurve = true;
    elseif Arg1 == "off" then
      DB.HpCurve = false;
    else
      Print("hpcurve is " .. (DB.HpCurve and "ON" or "OFF") .. " - use '/mdb hpcurve on|off'");
      return;
    end
    HpCurveBase = false;
    Print("hpcurve " .. (DB.HpCurve and "|cFF00FF00on|r" or "|cFFFF0000off|r"));
    return;
  end

  if Command == "mask" then
    -- v3.5 mask push: `/mdb mask <hhhh> <epoch>`. The desktop app is the only
    -- writer; it pushes its permissive DEFAULTS (overlay-wins — the in-game
    -- toggles override). The bridge refuses while in combat (never rewrite the
    -- defaults mid-fight) or when the arguments fail the wire range check
    -- ("bad checksum"). A refusal raises blocked bit1 in cell 41 B and leaves
    -- the stored mask untouched, so the panel can show why.
    local OkRef = true;
    local Refuse = nil;
    local InCombat = false;
    if type(UnitAffectingCombat) == "function" then
      local OkC, V = pcall(UnitAffectingCombat, "player");
      OkRef = OkC;
      if OkC then InCombat = (V == true); end
    end
    local MaskArg = tonumber(tostring(Arg1 or ""), 16);
    local EpochArg = tonumber(Arg2);
    if not OkRef then
      Refuse = "combat state unreadable";
    elseif InCombat then
      Refuse = "in combat";
    elseif type(MaskArg) ~= "number" or MaskArg < 0 or MaskArg > EXT3_MASK_BITS then
      Refuse = "bad checksum (mask)";
    elseif type(EpochArg) ~= "number" or EpochArg ~= math.floor(EpochArg)
      or EpochArg < 0 or EpochArg > 15 then
      Refuse = "bad checksum (epoch)";
    end
    if Refuse then
      if MDB.Toggles and MDB.Toggles.SetAppBlocked then
        MDB.Toggles.SetAppBlocked((MDB.Toggles.BLOCKED_APP or EXT3_BLOCKED_APP));
      end
      Print("mask refused (" .. Refuse .. "); stored mask unchanged");
      return;
    end
    if MDB.Toggles and MDB.Toggles.SetAppMask then
      MDB.Toggles.SetAppMask(MaskArg, EpochArg, 0);
    end
    Print(("app mask 0x%04X epoch %d applied (default; overlay-wins)")
      :format(MaskArg, EpochArg));
    return;
  end

  if Command == "dwell" then
    if Arg1 == nil then
      Print(("rotation dwell is %d ticks - use '/mdb dwell <1-60>'"):format(RotationDwell()));
      return;
    end
    local N = tonumber(Arg1);
    if type(N) ~= "number" then
      Print("usage: /mdb dwell <1-60>");
      return;
    end
    N = math.floor(N);
    if N < 1 then N = 1 elseif N > 60 then N = 60; end
    DB.RotationDwell = N;
    Print(("rotation dwell set to %d ticks"):format(N));
    return;
  end

  if Command == "on" or Command == "off" or Command == "toggle" then
    -- Single writer for the Enabled/UserPaused pair: `/mdb off` records an
    -- explicit user pause (sticky, fail-closed) and `/mdb on` (or toggling
    -- back on) is the only thing that clears it. The Options checkbox routes
    -- through the same setter, so no UI path can diverge the two flags.
    if Command == "toggle" then
      MDB.SetEnabled(not DB.Enabled);
    else
      MDB.SetEnabled(Command == "on");
    end
    -- on/off doubles as the panic switch for the calibrate pattern.
    if DB.Calibrate and Command ~= "toggle" then
      DB.Calibrate = false;
      CalStep = 0;
      CalTick = 0;
      Layout();
    end
    Print("bridge " .. (DB.Enabled and "|cFF00FF00enabled|r" or "|cFFFF0000paused|r"));
  elseif Command == "offset" then
    DB.OffsetX = tonumber(Arg1) or DB.OffsetX;
    DB.OffsetY = tonumber(Arg2) or DB.OffsetY;
    Layout();
    Print(("block offset set to %d, %d"):format(DB.OffsetX, DB.OffsetY));
  elseif Command == "cellsize" then
    DB.CellSize = math.max(1, math.min(64, tonumber(Arg1) or DB.CellSize));
    Layout();
    Print(("cell size set to %d px"):format(DB.CellSize));
  elseif Command == "calibrate" then
    if Arg1 == "on" then
      DB.Calibrate = true;
      -- Entering calibrate is an explicit user action that MUST render the
      -- sweep: a stale Enabled=false strip or a previous `/mdb off` pause
      -- would otherwise leave the learner sweeping a dead corner. Route
      -- through the single writer so Enabled and the sticky pause clear in
      -- lockstep (never a bare `DB.Enabled = true`).
      if not DB.Enabled or MDB.IsUserPaused() then
        MDB.SetEnabled(true);
        Print("bridge enabled");
      end
      CalStep = 0;
      CalTick = 0;
      Print("calibrate pattern ON - run Learn colors in the app, then '/mdb calibrate off'");
    elseif Arg1 == "off" then
      DB.Calibrate = false;
      Layout();
      Print("calibrate pattern OFF - normal rendering resumed");
    else
      Print("calibrate is " .. (DB.Calibrate and "ON" or "OFF") .. " - use '/mdb calibrate on|off'");
    end
  elseif Command == "reset" then
    -- Deep restore, not `DB[Key] = Defaults[Key]`: that would alias the
    -- nested Toggles/Ui tables and let later edits mutate Defaults.
    for Key in pairs(DB) do DB[Key] = nil; end
    MergeDefaults(DB, Defaults);
    -- Keep the in-memory sticky flag in lockstep with the restored defaults
    -- (Defaults.UserPaused = false), or a clock-change reset would stay paused.
    UserPaused = false;
    CalStep = 0;
    CalTick = 0;
    Layout();
    Print("settings reset to defaults");
  elseif Command == "status" then
    local Main = MDB.GetMainSpellID();
    local NextFn = "nil";
    local MDPS = _G.MaxDps;
    if MDPS then
      NextFn = type(MDPS.NextSpell) == "function" and "fn"
        or (MDPS.NextSpell == nil and "nil-ensure-ran"
          or type(MDPS.NextSpell));
      if MDPS.rotationEnabled ~= nil then
        NextFn = NextFn .. (MDPS.rotationEnabled and "+rot" or "-idle");
      end
    else
      NextFn = "no-engine";
    end
    local Ready, MainText, ExtrasText, UrgencyText = "????????", "-", "?", "-";
    local OkStatus, Captured = pcall(function ()
      if type(dropsecretaccess) == "function" then dropsecretaccess(); end
      local R = "";
      local M = MDB.GetMainSpellID();
      if M then R = R .. "M"; else
        local ES = _G.MaxDps and _G.MaxDps.Spell;
        if type(ES) == "number" and ES ~= 0 then R = R .. "m"; else R = R .. "-"; end
      end
      if MDB.GetOffensiveSpellID() then R = R .. "O"; else R = R .. "-"; end
      if MDB.GetDefensiveSpellID() then R = R .. "D"; else R = R .. "-"; end
      if MDB.GetConsumableSpellID() then R = R .. "N"; else R = R .. "-"; end
      if MDB.GetTrinketSpellID() then R = R .. "T"; else R = R .. "-"; end
      if MDB.GetInterruptSpellID() then R = R .. "I"; else R = R .. "-"; end
      if MDB.GetMobilitySpellID and MDB.GetMobilitySpellID() then R = R .. "Mob"; else R = R .. "-"; end
      if MDB.GetSelfHealSpellID and MDB.GetSelfHealSpellID() then R = R .. "H"; else R = R .. "-"; end
      local MT = "-";
      if type(Main) == "number" then MT = tostring(Main); end
      local ET = "-";
      if MDB.GetExtrasDiag then ET = MDB.GetExtrasDiag(); end
      local UT = "-";
      if MDB.GetDefensiveUrgencyNibble then
        UT = tostring(MDB.GetDefensiveUrgencyNibble(nil));
      end
      return { Ready = R, MainText = MT, Extras = ET, Urgency = UT };
    end);
    if OkStatus and type(Captured) == "table" then
      if type(Captured.Ready) == "string" then Ready = Captured.Ready; end
      if type(Captured.MainText) == "string" then MainText = Captured.MainText; end
      if type(Captured.Extras) == "string" then ExtrasText = Captured.Extras; end
      if type(Captured.Urgency) == "string" then UrgencyText = Captured.Urgency; end
    end
    local Sh2Text = "-";
    if MDB.GetSelfHeal2SpellID then
      local OkSh2, Sh2 = pcall(MDB.GetSelfHeal2SpellID);
      if OkSh2 and type(Sh2) == "number" then Sh2Text = tostring(Sh2); end
    end
    if DB.HpCurve == nil then DB.HpCurve = true; end
    local AppEpochText, AppMaskText, AppBlockedText = 0, EXT3_MASK_BITS, 0;
    if MDB.Toggles then
      if MDB.Toggles.AppEpoch then AppEpochText = MDB.Toggles.AppEpoch(); end
      if MDB.Toggles.EffectiveMask then AppMaskText = MDB.Toggles.EffectiveMask(); end
      if MDB.Toggles.AppBlocked then AppBlockedText = MDB.Toggles.AppBlocked(); end
    end
    local RC = MDB._RotCounts or {};
    local RotText = ("3:%d,6:%d,7:%d,8:%d"):format(RC[3] or 0, RC[6] or 0, RC[7] or 0, RC[8] or 0);
    Print(("v%s protocol=%d ext2=1 ext3=1 hpcurve=%s sh2=%s enabled=%s calibrate=%s offset=%d,%d cell=%dpx bound=%d spell=%s next=%s ready=%s urg=%s extras=%s toggles=%s app=%s/%s/%d dwell=%d rot=%s")
      :format(MDB.VERSION, PROTOCOL_VERSION, DB.HpCurve and "on" or "off", Sh2Text,
        tostring(DB.Enabled), tostring(DB.Calibrate),
        DB.OffsetX, DB.OffsetY, DB.CellSize, MDB.BindingCount(), MainText, NextFn, Ready, UrgencyText, ExtrasText,
        TogglesLine(),
        tostring(AppEpochText), ("0x%04X"):format(AppMaskText), AppBlockedText,
        RotationDwell(), RotText));
  elseif Command == "version" then
    Print("MaxDpsBridge v" .. MDB.VERSION .. " (protocol v" .. PROTOCOL_VERSION .. ")");
  elseif Command == "autocal" then
    AutoCalibrate("manual /mdb autocal");
  elseif Command == "toggles" then
    Print("Toggles: " .. TogglesLine());
    Print("use '/mdb <key> on|off' (main offensive defensive consumable trinket "
      .. "interrupt mobility selfheal solo ooc autotarget autointeract ttk) or '/mdb all on|off'");
  elseif Command == "overlay" then
    DB.Ui = DB.Ui or {};
    if Arg1 == "on" then
      DB.Ui.Overlay = true;
    elseif Arg1 == "off" then
      DB.Ui.Overlay = false;
    elseif Arg1 ~= nil then
      Print("usage: /mdb overlay [on|off]");
      return;
    end
    -- State is persisted here; Panel.lua's slash wrapper re-syncs the frame
    -- immediately after this handler returns.
    Print("overlay " .. (DB.Ui.Overlay and "|cFF00FF00on|r" or "|cFFFF0000off|r"));
  elseif Command == "all" then
    if not (MDB.Toggles and MDB.Toggles.Keys and MDB.Toggles.Set) then
      Print("toggles unavailable");
      return;
    end
    if Arg1 ~= "on" and Arg1 ~= "off" then
      Print("usage: /mdb all on|off");
      return;
    end
    local Keys = MDB.Toggles.Keys();
    for i = 1, #Keys do MDB.Toggles.Set(Keys[i], Arg1 == "on"); end
    Print("all toggles " .. (Arg1 == "on" and "|cFF00FF00ON|r" or "|cFFFF0000OFF|r"));
  elseif Command == "why" then
    if Arg1 == "heal" then
      local Last = MDB._LastBlank or {};
      Print(("why heal: slot8=%s, slot3=%s (nil = allowed; set in Toggles.SlotAllowed)")
        :format(tostring(Last[8] or "allowed"), tostring(Last[3] or "allowed")));
    else
      Print("usage: /mdb why heal");
    end
  elseif ToggleKeyLower[Command] and MDB.Toggles and MDB.Toggles.Set then
    local Canon = ToggleKeyLower[Command];
    -- OVERLAY-WINS (2026-09-30): the in-game value is always writable, even
    -- while the app epoch is live. Set() persists it; the effective mask and
    -- the Ext3 mirror republish from Get() on the next frame, and FrameKey
    -- includes the stored toggles so the slot cells repaint immediately.
    if Arg1 == "on" then
      MDB.Toggles.Set(Canon, true);
    elseif Arg1 == "off" then
      MDB.Toggles.Set(Canon, false);
    elseif Arg1 ~= nil then
      Print("usage: /mdb " .. Command .. " [on|off]");
      return;
    end
    Print(("%s is %s"):format(MDB.Toggles.Label(Canon),
      MDB.Toggles.Get(Canon) and "|cFF00FF00on|r" or "|cFFFF0000off|r"));
  else
    Print("commands: |cFFFFFF00on|r / |cFFFFFF00off|r / |cFFFFFF00toggle|r / "
      .. "|cFFFFFF00offset <x> <y>|r / |cFFFFFF00cellsize <px>|r / |cFFFFFF00calibrate on|off|r / "
      .. "|cFFFFFF00hpcurve on|off|r / |cFFFFFF00heal|r / "
      .. "|cFFFFFF00status|r / |cFFFFFF00diag|r / |cFFFFFF00autocal|r / |cFFFFFF00reset|r / |cFFFFFF00version|r / "
      .. "|cFFFFFF00toggles|r / |cFFFFFF00overlay on|off|r / |cFFFFFF00<key> on|off|r / "
      .. "|cFFFFFF00all on|off|r / |cFFFFFF00why heal|r / "
      .. "|cFFFFFF00mask <hhhh> <epoch>|r / |cFFFFFF00dwell <1-60>|r");
  end
end

--- ======= BOOTSTRAP =======

do
  local Loader = CreateFrame("Frame");
  Loader:RegisterEvent("ADDON_LOADED");
  Loader:RegisterEvent("PLAYER_ENTERING_WORLD");
  Loader:RegisterEvent("UI_SCALE_CHANGED");
  Loader:RegisterEvent("DISPLAY_SIZE_CHANGED");
  Loader:SetScript("OnEvent", function (self, Event, Arg1)
    if Event == "ADDON_LOADED" then
      if Arg1 ~= addonName then return; end

      MaxDpsBridgeDB = MaxDpsBridgeDB or {};
      -- Per-key merge so an old SavedVariables file stays valid: existing
      -- top-level values and existing toggles are preserved; only missing
      -- keys are seeded from Defaults (nested tables are copied, not aliased).
      MergeDefaults(MaxDpsBridgeDB, Defaults);

      -- Legacy pause memory: a DB saved with Enabled=false (pre-UserPaused)
      -- means the user paused, so seed the sticky flag from it.
      UserPaused = (MaxDpsBridgeDB.UserPaused == true) or (MaxDpsBridgeDB.Enabled == false);
      MaxDpsBridgeDB.UserPaused = UserPaused;

      CreateBlock();
      Root:SetScript("OnUpdate", Update);

      SLASH_MAXDPSBRIDGE1 = "/mdb";
      SlashCmdList["MAXDPSBRIDGE"] = HandleCommand;

      MDB.EnsureHooks();
      -- v2.0: arg-blind cast sensors (player + target unit events).
      if MDB.InitSensors then pcall(MDB.InitSensors); end
      if C_Timer and C_Timer.After then
        -- MaxDps may load after us despite the dependency; retry hooks once.
        C_Timer.After(5, MDB.EnsureHooks);
      end
    elseif Event == "PLAYER_ENTERING_WORLD" then
      MDB.EnsureHooks();
      if MDB.ResetSensors then pcall(MDB.ResetSensors); end
    elseif Root then
      Layout();
    end
  end);
end
