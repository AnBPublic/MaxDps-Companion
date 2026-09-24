--- ============================ HEADER ============================
-- Renders MaxDps's current suggestions as a row of flat-coloured squares in
-- a screen corner. MaxDpsCompanion.exe samples those pixels and replays the
-- encoded keystrokes.
--
-- Protocol v2 -- 9 cells, left to right, each CellSize physical pixels square.
-- Every channel carries one nibble, encoded as nibble * 17 (0, 17, ... 255),
-- which leaves enough headroom to survive gamma and scaling.
--
-- Slot order = the user's 5-icon model: 1 main rotation (THE core
-- functionality — MaxDps.Spell, always encoded when suggested), 2 offensive
-- (classCooldowns offensive via GlowCooldownMidnight, often off-GCD), 3
-- defensive (via GlowDefensiveHPMidnight), 4 consumable (potions in
-- MaxDps.Consumables), 5 trinket (other ItemSpells, on-use trinkets),
-- 6 interrupt (via GlowInteruptMidnight — situational, companion priority
-- re-sorts at send time so it still preempts when live).
--
--   cell 0  magic       R=15 G=0 B=15  (magenta; presence + alignment check)
--   cell 1  Main        R=vk hi  G=vk lo  B=flags  (MaxDps.Spell)
--   cell 2  Offensive   R=vk hi  G=vk lo  B=flags  (Flags, not int/def/item)
--   cell 3  Defensive   R=vk hi  G=vk lo  B=flags  (via GlowDefensiveHPMidnight)
--   cell 4  Consumable  R=vk hi  G=vk lo  B=flags  (ItemSpells in Consumables)
--   cell 5  Trinket     R=vk hi  G=vk lo  B=flags  (ItemSpells NOT in Consumables)
--   cell 6  Interrupt   R=vk hi  G=vk lo  B=flags  (via GlowInteruptMidnight)
--   cell 7  status      R=state  G=heartbeat  B=flags (v4: bit0 combat, bit1 GCD)
--   cell 8  version     R=4  G=checksum  B=commit (=heartbeat, tear detect)
--
-- checksum = sum of R+G+B nibbles of cells 1-7, mod 16. commit must equal
-- heartbeat or the frame is torn and the companion drops it.
--
-- flags: bit0 Shift, bit1 Ctrl, bit2 Alt, bit3 slot valid
-- state: 0 idle, 1 active, 2 paused (bridge off / calibrate),
--   3 need-target (no/dead/unattackable target), 4 need-interact (melee range)
--
-- Calibrate mode (`/mdb calibrate on`): renders a deterministic pattern the
-- desktop app samples to learn the display chain (RenoDX / RTX HDR / ICC /
-- ReShade). cell 0 = magic reference, cells 1-6 = flat level for the current
-- step, cell 7 = Paused so the companion holds instead of sending keys.
-- Steps advance every ~8 ticks (~400 ms dwell at 50 ms/update) so the
-- sampler locks through post-processing: black, white, red ramp, green ramp,
-- blue ramp, then repeat with re-anchors. `/mdb calibrate off` (or `/mdb off`)
-- returns to normal rendering.
--
-- READ-ONLY: no protected calls, no CastSpell/TargetUnit/InteractUnit/UseAction.

local addonName, MDB = ...;

MDB.VERSION = "1.3.6";

-- Chat print, defined FIRST: AutoCalibrate (below) and the Update watchdog
-- both call it, and Lua resolves locals lexically — a later `local
-- function Print` would be invisible here and call a nil global instead
-- (that was the 711x "attempt to call a nil value" spam: the error also
-- skipped the LastContact reset, so the watchdog refired every 50ms).
local function Print (Message)
  DEFAULT_CHAT_FRAME:AddMessage("|cFF00D8FFMDB|r: " .. Message);
end

-- Protocol v4 (Sep-2026): same 9 cells. The STATUS cell B nibble (was
-- reserved 0) now carries COMbat/GCD flags:
--   bit0 IN_COMBAT (UnitAffectingCombat, NeverSecret)
--   bit1 ON_GCD    (C_Spell.GetSpellCooldown(61304).isOnGCD, NeverSecret)
-- so the companion can (a) pause outside combat and (b) wait for the GCD
-- instead of machine-gunning a suggestion that WoW will ignore. R bump
-- 3→4 forces stale-decode rejection both ends (same-length misread lesson).
local CELL_COUNT = 9;
local PROTOCOL_VERSION = 4;
local STATUS_FLAG_IN_COMBAT = 1;
local STATUS_FLAG_ON_GCD = 2;
-- v1.3.6: bit2 = a valid attackable target exists. The companion waits
-- for a target instead of spamming suggestions into empty space (user:
-- "attack out of combat is ok, it just shouldn't spam into empty space —
-- wait until I target something").
local STATUS_FLAG_HAS_TARGET = 4;
local UPDATE_INTERVAL = 0.05;

-- NOTE: a 25 s auto-cal watchdog lived here (reset strip to 0,0 when the
-- companion went quiet). REMOVED: it fired mid-session while the engine
-- was attached (alive ping raced it), moved the strip out from under a
-- learned profile, and produced "no pixel block" on a visible strip.
-- Aethys has no such timer and never needs it: the engine re-sweeps on
-- miss (Relocate) and explicit /mdb autocal covers the manual case.

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

-- Manual auto-calibration: forget position/size, back to the default
-- corner. Explicit /mdb autocal only — no timer (see note above).
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

local function WriteSlot (CellIndex, SpellID, IsInterrupt, SkipGate)
  -- Belt-and-braces: the getters already gate on readiness, but a stale
  -- value between their check and this encode must never go on the wire.
  -- Unready spells encode as EMPTY (FLAG_VALID clear) so the companion
  -- skips the slot and fires the next ready one instead of hammering an
  -- unavailable key.
  --
  -- v1.3.0 zero-taint: SpellIDs arrive from scrubbed scans (proven plain
  -- numbers); the readiness re-check below runs inside pcall so ANY taint
  -- throw (secret verdict, secret cooldown field) degrades to EMPTY for
  -- THIS slot only — never aborts the frame. No IsSecret call (deleted;
  -- verdict-compare regress, Sep-2026).
  --
  -- v1.3.2: SkipGate=true for the MAIN slot (cell 1) — upstream's glow IS
  -- the castability verdict (see GetMainSpellID); re-gating it here with
  -- tainted reads vetoed correct mains (Sep-2026 stuck-rotation: E fired,
  -- 1/2/R never did). Situational slots keep the gate.
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

local function WriteStatus (State, Flags)
  Flags = Flags or 0;
  Paint(7, State, Heartbeat, Flags);
  return State, Heartbeat, Flags;
end

-- v1.3.5/1.3.6: status flags from NeverSecret / plain-unit APIs only (no
-- secrets touched — safe to compare even under taint):
--   combat: UnitAffectingCombat("player") — plain boolean.
--   gcd:    C_Spell.GetSpellCooldown(61304).isOnGCD — NeverSecret.
--   target: UnitExists/UnitIsDead/UnitCanAttack on "target" — plain
--           unit booleans (same calls TargetState already relies on).
-- pcall-wrapped; any failure leaves the flag clear (companion fails open).
local function StatusFlags ()
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
  -- Valid attackable target: exists, alive, attackable. Reads only unit
  -- booleans (never secret).
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
  return Flags;
end

local function WriteVersion (Checksum)
  Paint(8, PROTOCOL_VERSION, Checksum, Heartbeat);
end

local function Update (self, Delta)
  Elapsed = Elapsed + Delta;
  if Elapsed < UPDATE_INTERVAL then return; end
  Elapsed = 0;

  Heartbeat = (Heartbeat + 1) % 16;

  -- (Watchdog removed — see note at top. Manual /mdb autocal only.)

  -- Calibrate pattern: hold the companion (state = Paused) while the
  -- desktop app learns the display chain. Steps advance every ~8 ticks
  -- (~400 ms dwell at 50 ms/update) so the sampler locks through
  -- post-processing; steps 0/52 and 1/53 are black/white anchors repeated
  -- late in the cycle so black/white can be re-found.
  -- Only cells 1-6 carry the flat level: the learner averages exactly
  -- cells 1-6 and requires cell 7 to read Paused.
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
    for i = 1, 6 do
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
      SlotSum = 6 * (Level * 3);
    else
      SlotSum = 6 * Level;
    end
    WriteVersion((SlotSum + SR + SG + SB) % 16);
    return;
  end

  if not MaxDpsBridgeDB.Enabled then
    for i = 1, 6 do Paint(i, 0, 0, 0); end
    local SR, SG, SB = WriteStatus(STATE_PAUSED);
    WriteVersion((SR + SG + SB) % 16);
    return;
  end

  MDB.EnsureHooks();

  -- Slot order = the user's 5-icon model: 1 main rotation (THE core
  -- functionality — always encoded when MaxDps suggests anything),
  -- 2 offensive, 3 defensive, 4 consumable, 5 trinket, 6 interrupt
  -- (interrupt rides last: situational, companion priority re-sorts at
  -- send time so it still preempts when live).
  local R1, G1, B1 = WriteSlot(1, MDB.GetMainSpellID(), false, true);
  local R2, G2, B2 = WriteSlot(2, MDB.GetOffensiveSpellID());
  local R3, G3, B3 = WriteSlot(3, MDB.GetDefensiveSpellID());
  local R4, G4, B4 = WriteSlot(4, MDB.GetConsumableSpellID());
  local R5, G5, B5 = WriteSlot(5, MDB.GetTrinketSpellID());
  local R6, G6, B6 = WriteSlot(6, MDB.GetInterruptSpellID(), true);

  local AnySlot = bit.band(B1, FLAG_VALID) == FLAG_VALID
    or bit.band(B2, FLAG_VALID) == FLAG_VALID
    or bit.band(B3, FLAG_VALID) == FLAG_VALID
    or bit.band(B4, FLAG_VALID) == FLAG_VALID
    or bit.band(B5, FLAG_VALID) == FLAG_VALID
    or bit.band(B6, FLAG_VALID) == FLAG_VALID;

  -- v1.3.4 MELEE-STATE FIX (Sep-2026 live test: R recommended, companion
  -- spammed InteractKey and never sent R): TargetState() returns
  -- NeedInteract (4) whenever CheckInteractDistance(target, 3) is true —
  -- which is TRUE FOR EVERY MELEE TARGET, i.e. the entire rotation for a
  -- melee class. The old code let that state OVERRIDE Active, so in melee
  -- the companion saw state 4, held the rotation, and fired the
  -- auto-interact key every frame. THE RULE: a live suggestion ALWAYS
  -- wins the state (Active). Target/interact states exist ONLY so the
  -- companion can ask for a target / interact when MaxDps has NOTHING to
  -- cast (AllSlotsEmpty) — never to suppress a pending rotation.
  local State;
  if AnySlot then
    State = STATE_ACTIVE;
  else
    State = TargetState() or STATE_IDLE;
  end

  local SR, SG, SB = WriteStatus(State, StatusFlags());
  WriteVersion((R1 + G1 + B1 + R2 + G2 + B2 + R3 + G3 + B3
    + R4 + G4 + B4 + R5 + G5 + B5 + R6 + G6 + B6 + SR + SG + SB) % 16);
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
    -- Ready flags (v1.3.1): one letter per slot, uppercase = ready and
    -- encoded, lowercase = suggested but on cooldown/unusable (skipped),
    -- '-' = no suggestion. Lets you see at a glance WHY the companion
    -- fires slot 2 while slot 1 shows a spell in MaxDps.
    -- Letters follow the 5-icon model: M=Main, O=Offensive, D=Defensive,
    -- N=coNsumable, T=Trinket, I=Interrupt (last — situational).
    -- v1.3.0 zero-taint status: the whole block runs inside ONE pcall
    -- with dropsecretaccess() containment (a diagnostic must NEVER throw).
    -- pcall multi-return is captured into a TABLE (locals, never secrets —
    -- the containment strips secret access from this closure first).
    local Ready, MainText = "??????", "-";
    local OkStatus, Captured = pcall(function ()
      if type(dropsecretaccess) == "function" then dropsecretaccess(); end
      local R = "";
      local M = MDB.GetMainSpellID();
      if M then R = R .. "M";
      else
        local ES = _G.MaxDps and _G.MaxDps.Spell;
        if type(ES) == "number" and ES ~= 0 then R = R .. "m";
        else R = R .. "-"; end
      end
      if MDB.GetOffensiveSpellID() then R = R .. "O"; else R = R .. "-"; end
      if MDB.GetDefensiveSpellID() then R = R .. "D"; else R = R .. "-"; end
      if MDB.GetConsumableSpellID() then R = R .. "N"; else R = R .. "-"; end
      if MDB.GetTrinketSpellID() then R = R .. "T"; else R = R .. "-"; end
      if MDB.GetInterruptSpellID() then R = R .. "I"; else R = R .. "-"; end
      local MT = "-";
      if type(Main) == "number" then MT = tostring(Main); end
      return { Ready = R, MainText = MT };
    end);
    if OkStatus and type(Captured) == "table" then
      if type(Captured.Ready) == "string" then Ready = Captured.Ready; end
      if type(Captured.MainText) == "string" then MainText = Captured.MainText; end
    end
    Print(("v%s enabled=%s calibrate=%s offset=%d,%d cell=%dpx bound=%d spell=%s next=%s ready=%s")
      :format(MDB.VERSION, tostring(DB.Enabled), tostring(DB.Calibrate),
        DB.OffsetX, DB.OffsetY, DB.CellSize, MDB.BindingCount(), MainText, NextFn, Ready));
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
