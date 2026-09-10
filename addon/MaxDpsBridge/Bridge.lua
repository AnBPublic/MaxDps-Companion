--- ============================ HEADER ============================
-- Renders MaxDps's current suggestions as a row of flat-coloured squares in
-- a screen corner. MaxDpsCompanion.exe samples those pixels and replays the
-- encoded keystrokes.
--
-- Protocol v1 -- 8 cells, left to right, each CellSize physical pixels square.
-- Every channel carries one nibble, encoded as nibble * 17 (0, 17, ... 255),
-- which leaves enough headroom to survive gamma and scaling.
--
--   cell 0  magic       R=15 G=0 B=15  (magenta; presence + alignment check)
--   cell 1  Main        R=vk hi  G=vk lo  B=flags  (MaxDps.Spell)
--   cell 2  Cooldown    R=vk hi  G=vk lo  B=flags  (Flags, not int/def/item)
--   cell 3  Interrupt   R=vk hi  G=vk lo  B=flags  (via GlowInteruptMidnight)
--   cell 4  Defensive   R=vk hi  G=vk lo  B=flags  (via GlowDefensiveHPMidnight)
--   cell 5  Consumable  R=vk hi  G=vk lo  B=flags  (ItemSpells glow)
--   cell 6  status      R=state  G=heartbeat  B=0 (reserved)
--   cell 7  version     R=1  G=checksum  B=commit (=heartbeat, tear detect)
--
-- checksum = sum of R+G+B nibbles of cells 1-6, mod 16. commit must equal
-- heartbeat or the frame is torn and the companion drops it.
--
-- flags: bit0 Shift, bit1 Ctrl, bit2 Alt, bit3 slot valid
-- state: 0 idle, 1 active, 2 paused (bridge off / calibrate),
--   3 need-target (no/dead/unattackable target), 4 need-interact (melee range)
--
-- Calibrate mode (`/mdb calibrate on`): renders a deterministic pattern the
-- desktop app samples to learn the display chain (RenoDX / RTX HDR / ICC /
-- ReShade). cell 0 = magic reference, cells 1-5 = flat level for the current
-- step, cell 6 = Paused so the companion holds instead of sending keys.
-- Steps advance every ~8 ticks (~400 ms dwell at 50 ms/update) so the
-- sampler locks through post-processing: black, white, red ramp, green ramp,
-- blue ramp, then repeat with re-anchors. `/mdb calibrate off` (or `/mdb off`)
-- returns to normal rendering.
--
-- READ-ONLY: no protected calls, no CastSpell/TargetUnit/InteractUnit/UseAction.

local addonName, MDB = ...;

MDB.VERSION = "1.0.0";

-- Chat print, defined FIRST: AutoCalibrate (below) and the Update watchdog
-- both call it, and Lua resolves locals lexically — a later `local
-- function Print` would be invisible here and call a nil global instead
-- (that was the 711x "attempt to call a nil value" spam: the error also
-- skipped the LastContact reset, so the watchdog refired every 50ms).
local function Print (Message)
  DEFAULT_CHAT_FRAME:AddMessage("|cFF00D8FFMDB|r: " .. Message);
end

local CELL_COUNT = 8;
local PROTOCOL_VERSION = 1;
local UPDATE_INTERVAL = 0.05;

-- Auto-calibration watchdog: after 25 s without a word from the companion
-- (no /mdb command, no UpdateTimer drive), pop the strip back to the
-- default corner. Covers: crashed companion mid-session, user quitting
-- without /reload, pixel size forgotten after graphics changes. The
-- companion suppresses the timer with /mdb alive while it runs.
local AUTOCAL_TIMEOUT = 25;
local LastContact = 0;

local STATE_IDLE   = 0;
local STATE_ACTIVE = 1;
local STATE_PAUSED = 2;
local STATE_NEED_TARGET = 3;
local STATE_NEED_INTERACT = 4;

local FLAG_VALID = 8;

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
-- unchanged. Heartbeat ticks every update so cells 6-7 almost always
-- repaint; cells 0-5 repaint only on change.
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
  -- Border frame removed: at 1px cells a 1px border would double the
  -- strip's footprint. Contrast comes from nibble*17 + checksum.
  local Stale = _G.MaxDpsBridge_Border;
  if Stale then Stale:Hide(); end
  LastPaint = {};
  SetNibbles(0, 15, 0, 15);
  LastPaint[0] = 15 * 256 + 0 * 16 + 15;
end

MDB.Layout = Layout;

-- Auto-calibration: forget position/size, back to the default corner.
-- The companion then re-finds the strip with one sweep, no typing needed.
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
  -- No border frame: at 1px cells a 1px border would double the strip's
  -- footprint. Contrast comes from the nibble*17 levels + checksum, not
  -- from an outline.
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

local function WriteSlot (CellIndex, SpellID)
  local VirtualKey, Modifiers = MDB.ResolveBinding(SpellID);
  if not VirtualKey then
    Paint(CellIndex, 0, 0, 0);
    return 0, 0, 0;
  end
  local Hi = bit.rshift(VirtualKey, 4);
  local Lo = bit.band(VirtualKey, 0x0F);
  local Flags = bit.bor(Modifiers or 0, FLAG_VALID);
  Paint(CellIndex, Hi, Lo, Flags);
  return Hi, Lo, Flags;
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

local function WriteStatus (State)
  Paint(6, State, Heartbeat, 0);
  return State, Heartbeat, 0;
end

local function WriteVersion (Checksum)
  Paint(7, PROTOCOL_VERSION, Checksum, Heartbeat);
end

local function Update (self, Delta)
  Elapsed = Elapsed + Delta;
  if Elapsed < UPDATE_INTERVAL then return; end
  Elapsed = 0;

  Heartbeat = (Heartbeat + 1) % 16;

  -- Auto-calibration watchdog: companion gone quiet (crash, quit, resize
  -- without a word) - drop the strip back to the default corner instead
  -- of sitting invisible at a stale offset forever. LastContact is
  -- refreshed by every /mdb command and by the alive ping below.
  -- pcall-guarded: AutoCalibrate must never throw out of Update — a throw
  -- skips the LastContact reset below and the watchdog refires every tick
  -- (that was the 711x error-spam loop).
  if MaxDpsBridgeDB and not MaxDpsBridgeDB.Calibrate then
    if LastContact > 0 and (GetTime() - LastContact) > AUTOCAL_TIMEOUT then
      pcall(AutoCalibrate, "no companion contact for " .. AUTOCAL_TIMEOUT .. "s");
      LastContact = GetTime();
    end
  end

  -- Calibrate pattern: hold the companion (state = Paused) while the
  -- desktop app learns the display chain. Steps advance every ~8 ticks
  -- (~400 ms dwell at 50 ms/update) so the sampler locks through
  -- post-processing; steps 0/52 and 1/53 are black/white anchors repeated
  -- late in the cycle so black/white can be re-found.
  -- Only cells 1-5 carry the flat level: the learner averages exactly
  -- cells 1-5 and requires cell 6 to read Paused.
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
    for i = 1, 5 do
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
    -- Valid checksum + commit==heartbeat so the strip still decodes as a
    -- Paused frame (companion holds) instead of a torn frame.
    local SR, SG, SB = WriteStatus(STATE_PAUSED);
    local SlotSum;
    if Kind == "flat" then
      SlotSum = 5 * (Level * 3);
    else
      SlotSum = 5 * Level;
    end
    WriteVersion((SlotSum + SR + SG + SB) % 16);
    return;
  end

  if not MaxDpsBridgeDB.Enabled then
    for i = 1, 5 do Paint(i, 0, 0, 0); end
    local SR, SG, SB = WriteStatus(STATE_PAUSED);
    WriteVersion((SR + SG + SB) % 16);
    return;
  end

  MDB.EnsureHooks();

  local R1, G1, B1 = WriteSlot(1, MDB.GetMainSpellID());
  local R2, G2, B2 = WriteSlot(2, MDB.GetCooldownSpellID());
  local R3, G3, B3 = WriteSlot(3, MDB.GetInterruptSpellID());
  local R4, G4, B4 = WriteSlot(4, MDB.GetDefensiveSpellID());
  local R5, G5, B5 = WriteSlot(5, MDB.GetConsumableSpellID());

  local AnySlot = bit.band(B1, FLAG_VALID) == FLAG_VALID
    or bit.band(B2, FLAG_VALID) == FLAG_VALID
    or bit.band(B3, FLAG_VALID) == FLAG_VALID
    or bit.band(B4, FLAG_VALID) == FLAG_VALID
    or bit.band(B5, FLAG_VALID) == FLAG_VALID;

  local State = TargetState();
  if State == nil then
    State = AnySlot and STATE_ACTIVE or STATE_IDLE;
  end

  local SR, SG, SB = WriteStatus(State);
  WriteVersion((R1 + G1 + B1 + R2 + G2 + B2 + R3 + G3 + B3
    + R4 + G4 + B4 + R5 + G5 + B5 + SR + SG + SB) % 16);
end

--- ======= SLASH COMMANDS =======
-- (Print is defined at the top so AutoCalibrate can use it.)

local function HandleCommand (Input)
  local Command, Arg1, Arg2 = strsplit(" ", strlower(strtrim(Input or "")));
  local DB = MaxDpsBridgeDB;
  -- Every command is proof of life for the auto-cal watchdog.
  if type(GetTime) == "function" then LastContact = GetTime(); end

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
    -- on/off doubles as the panic switch for the calibrate pattern: a stuck
    -- pattern then never needs its own command to clear.
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
      Print("calibrate pattern ON — run Learn colors in the app, then '/mdb calibrate off'");
    elseif Arg1 == "off" then
      DB.Calibrate = false;
      Layout();
      Print("calibrate pattern OFF — normal rendering resumed");
    else
      Print("calibrate is " .. (DB.Calibrate and "ON" or "OFF") .. " — use '/mdb calibrate on|off'");
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
    Print(("v%s enabled=%s calibrate=%s offset=%d,%d cell=%dpx bound=%d spell=%s next=%s")
      :format(MDB.VERSION, tostring(DB.Enabled), tostring(DB.Calibrate),
        DB.OffsetX, DB.OffsetY, DB.CellSize, MDB.BindingCount(), tostring(Main), NextFn));
  elseif Command == "version" then
    Print("MaxDpsBridge v" .. MDB.VERSION .. " (protocol v" .. PROTOCOL_VERSION .. ")");
  elseif Command == "alive" then
    -- Companion proof of life: silent, no output. Stamps LastContact via
    -- the HandleCommand header above; this branch just stays quiet.
    return;
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

      if type(GetTime) == "function" then LastContact = GetTime(); end
      MDB.EnsureHooks();
      if C_Timer and C_Timer.After then
        -- MaxDps may load after us despite the dependency; retry hooks once.
        C_Timer.After(5, MDB.EnsureHooks);
      end
    elseif Event == "PLAYER_ENTERING_WORLD" then
      MDB.EnsureHooks();
    elseif Root then
      Layout();
    end
  end);
end
