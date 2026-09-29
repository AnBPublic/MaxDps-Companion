--- ============================ HEADER ============================
-- Renders MaxDps's current suggestions as a row of flat-coloured squares in
-- a screen corner. MaxDpsCompanion.exe samples those pixels and replays the
-- encoded keystrokes.
--
-- Protocol v5 -- 35 cells, left to right, each CellSize physical pixels square.
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
--   cell 28       cast state   R=0 none/1 cast/2 channel/15 unknown G=band B=flags
--   cell 29       target       R=bit0 melee bit1 melee-unknown
--                              G=target hp band 0-14 (15 unknown)
--                              B=bit0 casting bit1 unknown bit2 interruptible
--                                bit3 not-interruptible
--   cells 30-31   range        2 bits per slot: 0 unknown / 1 in / 2 out
--   cell 31 G     defensive urgency  0 unknown/1 white/2 yellow/3 orange/4 red
--   cell 31 B     bit0 = defensive slot is a catalog gap-fill candidate
--   cell 32       buff active  R=bits0-3 slots 1-4 G=bits0-3 slots 5-8
--                 B = stagger-curve defensive urgency (Purifying Brew)
--   cell 33       class/spec   R=class id G=spec id B=bit0 valid
--   cell 34       ext checksum R=0 G=checksum(cells 11-33) B=commit
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

MDB.VERSION = "2.7.0";

-- Chat print, defined FIRST: AutoCalibrate (below) and the Update watchdog
-- both call it, and Lua resolves locals lexically -- a later `local
-- function Print` would be invisible here and call a nil global instead
-- (that was the 711x "attempt to call a nil value" spam: the error also
-- skipped the LastContact reset, so the watchdog refired every 50ms).
local function Print (Message)
  DEFAULT_CHAT_FRAME:AddMessage("|cFF00D8FFMDB|r: " .. Message);
end

-- Protocol v5 (bridge 2.7.0): 35 cells. Additive blocks on reserved nibbles:
-- defensive urgency (cell 31 G/B, cell 32 B) from bridge 2.3 and the self-buff
-- probe-block validity bit (cell 33 B bit1) from bridge 2.7. The version nibble
-- STAYS 5, so an older companion exe still decodes this strip (it ignores
-- reserved bits; the extension checksum already covered them). Zero means
-- UNKNOWN and the companion keeps its documented fail-open.
-- The status cell B keeps the v4 combat/GCD/target flags and v5's bit3 =
-- context-valid. Cells 1-8 keep the v4 slot semantics so the companion's
-- legacy fallback decode stays valid.
local CELL_COUNT = 35;
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
  OffsetX = 0,
  OffsetY = 0,
  -- Aethys-proven 8px cells: ±1px misalignment tolerance by construction.
  -- 1px cells have zero tolerance (sub-pixel offset, UI-scale rounding or
  -- HDR dither corrupts the single read point) and failed live.
  CellSize = 8,
  Calibrate = false,
};

local Root, Cells;
local Heartbeat = 0;
local Elapsed = 0;
local CalStep = 0;
local CalTick = 0;

--- ======= PIXEL PLUMBING =======

local function SetNibbles (Index, R, G, B)
  Cells[Index]:SetColorTexture(R / 15, G / 15, B / 15, 1);
end

-- Cached paint: slot/status cells skip SetColorTexture when nibbles are
-- unchanged. Heartbeat ticks every update so the status/checksum cells almost
-- always repaint; slot cells repaint only on change.
local LastPaint = {};
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
local function WriteSpellId (SlotIndex, SpellID)
  local Base = SPELLID_CELL + (SlotIndex - 1) * 2;
  if type(SpellID) ~= "number" or SpellID < 0 or SpellID > 0xFFFFFF then SpellID = 0; end
  local Hi12 = bit.band(bit.rshift(SpellID, 12), 0x0FFF);
  local Lo12 = bit.band(SpellID, 0x0FFF);
  Paint(Base, bit.band(bit.rshift(Hi12, 8), 0x0F), bit.band(bit.rshift(Hi12, 4), 0x0F), bit.band(Hi12, 0x0F));
  Paint(Base + 1, bit.band(bit.rshift(Lo12, 8), 0x0F), bit.band(bit.rshift(Lo12, 4), 0x0F), bit.band(Lo12, 0x0F));
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
  if type(CheckInteractDistance) == "function" then
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

local function WriteCast ()
  local State = CAST_STATE_UNKNOWN;
  if MDB.GetCastState then State = MDB.GetCastState(); end
  local Band = 15;   -- remaining-time band is not safely observable; reserved
  Paint(CAST_CELL, State, Band, 0);
  return State, Band, 0;
end

local function WriteTarget ()
  local MeleeFlag, HpBand, CastFlags = 0, 15, 0;
  if MDB.GetTargetContext then
    MeleeFlag, HpBand, CastFlags = MDB.GetTargetContext();
  end
  if type(CastFlags) ~= "number" then CastFlags = 0; end
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

local function WriteClassSpec (BuffValid)
  local ClassId, SpecId = 0, 0;
  if MDB.GetClassSpec then ClassId, SpecId = MDB.GetClassSpec(); end
  local Valid = 0;
  if type(ClassId) == "number" and ClassId > 0 then Valid = 1; else ClassId = 0; end
  if type(SpecId) ~= "number" then SpecId = 0; end
  -- v2.7 additive: bit1 of the B nibble = the self-buff probe block is
  -- trustworthy. Pre-2.7 encoders leave it 0; old companions read only bit0.
  local Flags = Valid;
  if BuffValid == 1 then Flags = bit.bor(Flags, 2); end
  Paint(CLASSSPEC_CELL, ClassId, SpecId, Flags);
  return ClassId, SpecId, Flags;
end

-- A complete, valid v5 frame with no suggestions and no context. Used while
-- the bridge is disabled so the companion decodes "Paused" instead of
-- treating the stale cells as a torn frame.
local function WriteEmptyFrame (State)
  for i = 1, SLOT_COUNT do
    Paint(i, 0, 0, 0);
    WriteSpellId(i, 0);
  end
  local Vh, Vl, Vf = 0, 0, 0;
  Paint(VITALS_CELL, Vh, Vl, Vf);
  local Ch, Cg, Cf = CAST_STATE_UNKNOWN, 15, 0;
  Paint(CAST_CELL, Ch, Cg, Cf);
  local Th, Tg, Tf = 0, 15, 2;   -- cast state unknown
  Paint(TARGET_CELL, Th, Tg, Tf);
  Paint(RANGE_CELL, 0, 0, 0);
  Paint(RANGE_CELL2, 0, 0, 0);
  Paint(BUFF_CELL, 0, 0, 0);
  Paint(CLASSSPEC_CELL, 0, 0, 0);
  local Sr, Sg, Sb = WriteStatus(State, 0);
  WriteVersion((Sr + Sg + Sb) % 16);
  local ExtSum = Ch + Cg + Cf + Th + Tg + Tf;
  Paint(EXT_CELL, 0, ExtSum % 16, Heartbeat);
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

  if not MaxDpsBridgeDB.Enabled then
    WriteEmptyFrame(STATE_PAUSED);
    return;
  end

  MDB.EnsureHooks();

  -- PERF: reset the per-tick memo ONCE per update (the slot getters share
  -- one Flags scrub, one item map and one class/spec resolve per tick).
  if MDB.BeginTick then MDB.BeginTick(); end

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

  local R, G, B, Id = {}, {}, {}, {};
  R[1], G[1], B[1], Id[1] = WriteSlot(1, SafeRead(MDB.GetMainSpellID), false, true);
  R[2], G[2], B[2], Id[2] = WriteSlot(2, SafeRead(MDB.GetOffensiveSpellID));
  -- Defensive: MaxDps's flagged+bound candidate, or the catalog gap-fill at
  -- Red urgency. Both returns captured (the source bit goes to cell 31 B).
  local DefId, DefCatalog = nil, false;
  if MDB.GetDefensiveCandidate then
    local OkDef, Candidate, IsCatalog = pcall(MDB.GetDefensiveCandidate);
    if OkDef and type(Candidate) == "number" and Candidate ~= 0 then
      DefId = Candidate;
      DefCatalog = IsCatalog == true;
    end
  end
  R[3], G[3], B[3], Id[3] = WriteSlot(3, DefId);
  R[4], G[4], B[4], Id[4] = WriteSlot(4, SafeRead(MDB.GetConsumableSpellID));
  R[5], G[5], B[5], Id[5] = WriteSlot(5, SafeRead(MDB.GetTrinketSpellID));
  R[6], G[6], B[6], Id[6] = WriteSlot(6, SafeRead(MDB.GetInterruptSpellID), true);
  R[7], G[7], B[7], Id[7] = WriteSlot(7, SafeRead(MDB.GetMobilitySpellID));
  R[8], G[8], B[8], Id[8] = WriteSlot(8, SafeRead(MDB.GetSelfHealSpellID));

  for i = 1, SLOT_COUNT do
    WriteSpellId(i, Id[i] or 0);
  end

  -- v6 defensive urgency: the HP-curve stage for the defensive slot's spell
  -- (stagger curve for Purifying Brew, with the vendor's HP fallback), and
  -- the stagger stage on its own for the policy's per-ability source choice.
  -- Every getter is pcall-contained; anything unexpected stays 0 = UNKNOWN.
  local Urgency, StaggerUrgency = 0, 0;
  if MDB.GetDefensiveUrgencyNibble then
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
  local Ch, Cg, Cf = WriteCast();
  local Th, Tg, Tf = WriteTarget();
  local R30, G30, B30, R31, G31, B31 = WriteRanges(Id, Urgency, DefCatalog);
  local Bh, Bg, Bb, BuffValid = WriteBuffs(Id, StaggerUrgency);
  local Kh, Kg, Kf = WriteClassSpec(BuffValid);

  -- v1.3.4 MELEE-STATE FIX: a live suggestion ALWAYS wins the state (Active).
  -- Target/interact states exist ONLY so the companion can ask for a target
  -- / interact when MaxDps has NOTHING to cast -- never to suppress a
  -- pending rotation.
  local State;
  if AnySlot then
    State = STATE_ACTIVE;
  else
    State = TargetState() or STATE_IDLE;
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

local function HandleCommand (Input)
  local Command, Arg1, Arg2 = strsplit(" ", strlower(strtrim(Input or "")));
  local DB = MaxDpsBridgeDB;

  -- Diagnostics live in Reader.lua (needs MaxDps internals there).
  if Command == "diag" and MDB.Diag then
    MDB.Diag();
    return;
  end

  if Command == "on" or Command == "off" or Command == "toggle" then
    if Command == "toggle" then
      DB.Enabled = not DB.Enabled;
    else
      DB.Enabled = (Command == "on");
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
      -- Entering calibrate implies the bridge should run: a previous
      -- cleanup that left Enabled=false (stale paused strip) would
      -- otherwise render nothing and the learner sweeps a dead corner.
      if not DB.Enabled then
        DB.Enabled = true;
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
    for Key, Value in pairs(Defaults) do DB[Key] = Value; end
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
    Print(("v%s protocol=%d enabled=%s calibrate=%s offset=%d,%d cell=%dpx bound=%d spell=%s next=%s ready=%s urg=%s extras=%s")
      :format(MDB.VERSION, PROTOCOL_VERSION, tostring(DB.Enabled), tostring(DB.Calibrate),
        DB.OffsetX, DB.OffsetY, DB.CellSize, MDB.BindingCount(), MainText, NextFn, Ready, UrgencyText, ExtrasText));
  elseif Command == "version" then
    Print("MaxDpsBridge v" .. MDB.VERSION .. " (protocol v" .. PROTOCOL_VERSION .. ")");
  elseif Command == "autocal" then
    AutoCalibrate("manual /mdb autocal");
  else
    Print("commands: |cFFFFFF00on|r / |cFFFFFF00off|r / |cFFFFFF00toggle|r / "
      .. "|cFFFFFF00offset <x> <y>|r / |cFFFFFF00cellsize <px>|r / |cFFFFFF00calibrate on|off|r / "
      .. "|cFFFFFF00status|r / |cFFFFFF00diag|r / |cFFFFFF00autocal|r / |cFFFFFF00reset|r / |cFFFFFF00version|r");
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
      for Key, Value in pairs(Defaults) do
        if MaxDpsBridgeDB[Key] == nil then MaxDpsBridgeDB[Key] = Value; end
      end

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
