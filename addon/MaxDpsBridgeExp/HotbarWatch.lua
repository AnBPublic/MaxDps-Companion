--- ============================ HEADER ============================
-- HotbarWatch (protocol Ext4, bridge 4.0.x) - streams the player's KEYBOUND
-- action-bar map (spells / items / macros) so MaxDpsCompanion can recognise a
-- physical keypress on a hotbar slot and queue that stroke into the rotation
-- window (see docs/plans/2026-10-11-hotbar-press-queue.md).
--
-- READ-ONLY by construction: GetActionInfo / GetBindingKey / GetBindingKey
-- command mapping / GetMacroSpell / GetMacroItem / readiness probes only. No
-- protected Lua, no UseAction, no input hooks, no memory reads. Every read is
-- pcall-contained and every id passes a plain `type() == "number"` scrub;
-- a secret / failed / nil return simply drops that entry.
--
-- Wire (see docs/PROTOCOL.md Ext4): one frame = header (gen/seq/flags) + up
-- to 2 entries; each entry is 3 cells:
--     cell 1 = (vkHi, vkLo, flags)  Shift=1 Ctrl=2 Alt=4 Valid=8
--     cell 2 = (idTop, id19-16, id15-12)  idTop bit3=isItem bit2=ready
--                                          bits1-0 = id bits 21-20
--     cell 3 = (id11-8, id7-4, id3-0)
-- Id is 22 bits; an id >= 2^22 is skipped with one log. The map is capped at
-- 32 entries (bar order wins) with one overflow log. The bridge holds each
-- frame for >= 2 paint ticks so the ~33 ms desktop sampler always sees it.
--
-- Rebuild triggers: the Bars.lua event family (bars / bindings / spec /
-- talents / macros / spells / cooldown resets) + PLAYER_ENTERING_WORLD + a
-- 2 s sweep. A rebuild bumps gen = (gen + 1) & 0xF and restarts the seq cycle.

local addonName, MDBX = ...;

local type, pcall, tostring = type, pcall, tostring;
local math_floor = math.floor;

MDBX.HotbarWatch = MDBX.HotbarWatch or {};

local HotbarWatch = MDBX.HotbarWatch;

local MAX_ENTRIES   = 32;
local MAX_ID        = 2 ^ 22;        -- 4,194,304 exclusive (22-bit id)
local HOLD_TICKS    = 2;             -- hold each frame >= 2 bridge paint ticks
local SWEEP_SECONDS = 2;             -- periodic resweep
local SLOT_COUNT    = 180;           -- main bar + 6 extra bars + 2 bonus bars

local function Print (Message)
  if type(DEFAULT_CHAT_FRAME) == "table" and DEFAULT_CHAT_FRAME.AddMessage then
    DEFAULT_CHAT_FRAME:AddMessage("|cFF00D8FFMDBX|r hotbar: " .. tostring(Message));
  end
end

--- ======= FILTERS =======
-- Movement keys are NEVER watched or sent (mirrors MovementGuard.MovementKeys;
-- Q/E deliberately excluded - they are common spell binds).
local MovementVKs = {
  [0x57] = true, [0x41] = true, [0x53] = true, [0x44] = true,   -- W A S D
  [0x20] = true,                                                -- Space (jump)
  [0x25] = true, [0x26] = true, [0x27] = true, [0x28] = true,   -- Left Up Right Down
};

-- Bare modifier keys: no action ever fires from them; exclude defensively.
local ModifierVKs = {
  [0x10] = true, [0x11] = true, [0x12] = true,                  -- SHIFT CONTROL MENU
  [0xA0] = true, [0xA1] = true, [0xA2] = true,                  -- LSHIFT RSHIFT LCONTROL
  [0xA3] = true, [0xA4] = true, [0xA5] = true,                  -- RCONTROL LMENU RMENU
};

-- VK 0x00-0x0B = VK 0, mouse VKs 1-6 and the two wheel sentinels (0x07/0x0B).
-- Wheel has no async key state and mouse buttons are excluded in v1.
local function VKWatchable (VK)
  if type(VK) ~= "number" or VK < 0x0C then return false; end
  if MovementVKs[VK] then return false; end
  if ModifierVKs[VK] then return false; end
  return true;
end

--- ======= SLOT -> BINDING COMMAND (A1.11: SHARED with Reader) =======
-- 4.1.0: one table, defined in Keymap.lua (loaded before this file) and
-- cited against the documented Blizzard slot layout there. The old inline
-- copy mapped 13-24 AND 61-72 to MULTIACTIONBAR1 and 73-156 to
-- MULTIACTIONBAR5; 61-72 was the only confirmed row.
local SlotCommand = MDBX.SlotCommand;

--- ======= BAR-ADDON BUTTONS =======
-- Mirrors the pattern names Bars.lua probes. The binding itself is read
-- through Bars' exposed MDBX.BindingForTexture, so the addon's own texture ->
-- binding map stays single-sourced (we never re-implement ButtonBinding).
-- Step = buttons-per-bar stride for index-based slot fallback (Bartender's
-- BT4Button1-120 map 1:1 to action slots).
local AddonBarSets = {
  { Bars = { {"BT4Button", 120} }, Step = 0 },
  { Bars = { {"ElvUI_Bar1Button", 12}, {"ElvUI_Bar2Button", 12},
             {"ElvUI_Bar3Button", 12}, {"ElvUI_Bar4Button", 12},
             {"ElvUI_Bar5Button", 12}, {"ElvUI_Bar6Button", 12} }, Step = 12 },
  { Bars = { {"EngineBar1Button", 12}, {"EngineBar2Button", 12},
             {"EngineBar3Button", 12}, {"EngineBar4Button", 12},
             {"EngineBar5Button", 12} }, Step = 12 },
};

local function ActiveAddonBars ()
  -- Probe real frames (the same rule Bars.ActiveBarSet uses).
  if _G.BT4Button1 then return AddonBarSets[1]; end
  if _G.ElvUI_Bar1Button1 then return AddonBarSets[2]; end
  if _G.EngineBar1Button1 then return AddonBarSets[3]; end
  return nil;
end

local function SlotForButton (Button, Fallback)
  if type(Button.action) == "number" then return Button.action; end
  if Button.GetPagedID then
    local Ok, Paged = pcall(Button.GetPagedID, Button);
    if Ok and type(Paged) == "number" then return Paged; end
  end
  if _G.ActionButton_GetPagedID then
    local Ok, Paged = pcall(_G.ActionButton_GetPagedID, Button);
    if Ok and type(Paged) == "number" then return Paged; end
  end
  return Fallback;
end

--[[*
  * @function BuildSlotBinding
  * @desc slot 1-180 -> raw binding string. Command mapping first (default
  *       bars and any bar addon that keeps the ACTIONBUTTON binds), then the
  *       Bars texture path for addon-owned buttons whose slot is unbound by
  *       the command route.
  *]]
local function BuildSlotBinding ()
  local Map = {};
  if type(GetBindingKey) == "function" then
    for Slot = 1, SLOT_COUNT do
      local Command = SlotCommand and SlotCommand(Slot);
      if Command then
        local Ok, Binding = pcall(GetBindingKey, Command);
        if Ok and type(Binding) == "string" and Binding ~= "" then Map[Slot] = Binding; end
      end
    end
  end
  local Set = ActiveAddonBars();
  if Set and MDBX.BindingForTexture then
    pcall(function ()
      for b = 1, #Set.Bars do
        local Prefix, Count = Set.Bars[b][1], Set.Bars[b][2];
        local Base = (Set.Step == 0) and 0 or (b - 1) * Set.Step;
        for j = 1, Count do
          local Button = _G[Prefix .. j];
          if Button then
            local Slot = SlotForButton(Button, Base + j);
            if type(Slot) == "number" and Slot >= 1 and Slot <= SLOT_COUNT and not Map[Slot] then
              local Texture = nil;
              if Button.icon and Button.icon.GetTexture then
                local OkT, Tex = pcall(Button.icon.GetTexture, Button.icon);
                if OkT then Texture = Tex; end
              end
              if Texture then
                local OkB, Binding = pcall(MDBX.BindingForTexture, Texture);
                if OkB and type(Binding) == "string" and Binding ~= "" then Map[Slot] = Binding; end
              end
            end
          end
        end
      end
    end);
  end
  return Map;
end

--- ======= ACTION READ (pcall + dropsecretaccess containment) =======
local function ActionInfo (Slot)
  if type(GetActionInfo) ~= "function" then return nil, nil; end
  local Ok, Kind, Id = pcall(function ()
    if type(dropsecretaccess) == "function" then dropsecretaccess(); end
    return GetActionInfo(Slot);
  end);
  if not Ok then return nil, nil; end
  return Kind, Id;
end

local function MacroSpellId (MacroId)
  if type(GetMacroSpell) ~= "function" then return nil; end
  local Ok, _, _, Spell = pcall(GetMacroSpell, MacroId);
  if Ok and type(Spell) == "number" and Spell > 0 then return Spell; end
  return nil;
end

local function MacroItemId (MacroId)
  if type(GetMacroItem) ~= "function" then return nil; end
  local Ok, _, _, Item = pcall(GetMacroItem, MacroId);
  if Ok and type(Item) == "number" and Item > 0 then return Item; end
  return nil;
end

--- ======= READINESS (fail-closed) =======
local function SpellReady (SpellId)
  if type(MDBX.IsSpellReady) ~= "function" then return false; end
  local Ok, Ready = pcall(MDBX.IsSpellReady, SpellId);
  if Ok and Ready == true then return true; end
  return false;
end

-- Items: usable + off cooldown. Any throw / secret / unexpected shape => not
-- ready (fail closed: a non-ready press is a harmless no-op and never defers
-- the rotation, but we must never claim readiness we cannot prove).
local function ItemReady (ItemId)
  local Ok, Ready = pcall(function ()
    if type(IsUsableItem) == "function" then
      local Usable = IsUsableItem(ItemId);
      if Usable == false then return false; end
      if Usable ~= true then return false; end   -- nil / secret => fail closed
    end
    if type(GetItemCooldown) == "function" then
      local Start, Duration, Enabled = GetItemCooldown(ItemId);
      if Enabled == false then return false; end
      if type(Start) == "number" and type(Duration) == "number" and Duration > 0 then
        local Now = nil;
        if type(GetTime) == "function" then
          local OkT, T = pcall(GetTime);
          if OkT and type(T) == "number" then Now = T; end
        end
        if type(Now) == "number" and Now < Start + Duration then return false; end
      end
    end
    return true;
  end);
  if Ok and Ready == true then return true; end
  return false;
end

--- ======= NAMES (best effort, diagnostics only) =======
local function EntryName (Entry)
  if Entry.IsItem then
    if type(GetItemInfo) == "function" then
      local Ok, Name = pcall(GetItemInfo, Entry.Id);
      if Ok and type(Name) == "string" then return Name; end
    end
    return "item";
  end
  local Name = nil;
  if _G.C_Spell and type(_G.C_Spell.GetSpellName) == "function" then
    local Ok, N = pcall(_G.C_Spell.GetSpellName, Entry.Id);
    if Ok and type(N) == "string" then Name = N; end
  end
  if not Name and type(GetSpellInfo) == "function" then
    local Ok, N = pcall(GetSpellInfo, Entry.Id);
    if Ok and type(N) == "string" then Name = N; end
  end
  return Name or "spell";
end

--- ======= REBUILD =======
local Entries = {};          -- array of { Slot, Kind, Id, IsItem, Ready, VK, Mods, Binding, Name }
local EntryCount = 0;
local Gen = 0;
local Seq = 0;
local Last = 0;
local Hold = 0;
local Dirty = true;
local Pending = false;       -- a coalesced rebuild is scheduled
local LastSweep = nil;
local OverflowLogged = false;
local IdCapLogged = false;
local LastSignature = nil;   -- streamed content of the last rebuild

-- A1.7: coalesced rebuild delay. A burst of bar events (or a sweep that finds
-- nothing new) must cost at most one rebuild per window, and the rebuild must
-- run OUTSIDE the bridge's paint tick.
local REBUILD_DELAY = 0.25;

-- Numeric fold of everything that goes on the wire for one entry. Products
-- stay exact in a double (Acc < 2^32, *4099 < 2^43).
local function Mix (Acc, Value)
  if type(Value) ~= "number" then Value = 0; end
  if Value ~= Value or Value < 0 then Value = 0; end
  return (Acc * 4099 + math_floor(Value)) % 4294967291;
end

local function Signature (List, Count)
  local H = 0;
  for i = 1, Count do
    local E = List[i];
    H = Mix(H, E.Slot or 0);
    H = Mix(H, E.VK or 0);
    H = Mix(H, E.Mods or 0);
    H = Mix(H, E.Id or 0);
    H = Mix(H, E.IsItem and 1 or 0);
    H = Mix(H, E.Ready and 1 or 0);
  end
  return H * 33 + Count;
end

function HotbarWatch.Rebuild ()
  local New = {};
  local Binding = BuildSlotBinding();
  local Overflow = false;
  -- Per-rebuild readiness memo: one BeginTick/EndTick bracket so the whole
  -- walk shares one evaluation per spell (A1.3) and never leaks a memo into
  -- the next bridge tick.
  if MDBX.BeginTick then MDBX.BeginTick(); end
  for Slot = 1, SLOT_COUNT do
    local Kind, Id = ActionInfo(Slot);
    local Entry = nil;
    if Kind == "spell" then
      if type(Id) == "number" and Id > 0 then
        Entry = { Kind = "spell", Id = Id, IsItem = false };
      end
    elseif Kind == "item" then
      if type(Id) == "number" and Id > 0 then
        Entry = { Kind = "item", Id = Id, IsItem = true };
      end
    elseif Kind == "macro" then
      if type(Id) == "number" and Id > 0 then
        local SpellId = MacroSpellId(Id);
        if SpellId then
          Entry = { Kind = "macro", Id = SpellId, IsItem = false };
        else
          local ItemId = MacroItemId(Id);
          if ItemId then Entry = { Kind = "macro", Id = ItemId, IsItem = true }; end
        end
      end
    end
    if Entry then
      if Entry.Id >= MAX_ID then
        if not IdCapLogged then
          IdCapLogged = true;
          Print(("skipping id >= 2^22 (%d); entry dropped"):format(Entry.Id));
        end
        Entry = nil;
      end
    end
    if Entry then
      -- Resolve the keybinding for this slot.
      local BindingStr = Binding[Slot];
      local VK, Mods = nil, 0;
      if type(BindingStr) == "string" and type(MDBX.ParseBinding) == "function" then
        local OkP, Pv, Pm = pcall(MDBX.ParseBinding, BindingStr);
        if OkP and type(Pv) == "number" then
          VK = Pv;
          if type(Pm) == "number" then Mods = Pm; end
        end
      end
      if VKWatchable(VK) then
        Entry.Slot = Slot;
        Entry.VK = VK;
        Entry.Mods = Mods;
        Entry.Binding = BindingStr;
        Entry.Ready = Entry.IsItem and ItemReady(Entry.Id) or SpellReady(Entry.Id);
        Entry.Name = EntryName(Entry);
        if #New < MAX_ENTRIES then
          New[#New + 1] = Entry;
        else
          Overflow = true;   -- bar order wins; keep the first 32
        end
      end
    end
  end

  Entries = New;
  EntryCount = #New;
  if MDBX.EndTick then MDBX.EndTick(); end
  -- A1.7: gen is the companion's "new map, restart the seq cycle" signal.
  -- A sweep or readiness refresh that produced the same streamed content must
  -- not bump it (the old code bumped gen on every rebuild, so the 2 s sweep
  -- threw away a cycle the companion had already assembled). The signature
  -- covers exactly what the wire carries.
  local Sig = Signature(New, EntryCount);
  if Sig ~= LastSignature then
    LastSignature = Sig;
    Gen = (Gen + 1) % 16;
    Seq = 0;
    Last = EntryCount > 0 and math_floor((EntryCount - 1) / 2) or 0;
    Hold = 0;
  end
  Dirty = false;
  Pending = false;
  if Overflow and not OverflowLogged then
    OverflowLogged = true;
    Print(("hotbar map overflow: capped at %d entries"):format(MAX_ENTRIES));
  end
  return EntryCount;
end

-- Apply a scheduled rebuild now (the C_Timer backstop, the 2 s sweep target
-- and the deterministic hook the offline harness drives).
function HotbarWatch.Flush ()
  Pending = false;
  if not Dirty then return EntryCount; end
  return HotbarWatch.Rebuild();
end

function HotbarWatch.MarkDirty ()
  Dirty = true;
  if Pending then return; end
  Pending = true;
  if type(C_Timer) == "table" and type(C_Timer.After) == "function" then
    C_Timer.After(REBUILD_DELAY, HotbarWatch.Flush);
  else
    HotbarWatch.Flush();   -- no scheduler: rebuild now (ancient client)
  end
end

function HotbarWatch.IsDirty () return Dirty == true; end

--- ======= STREAMING FRAME PROVIDER =======
-- Returns the frame to paint this tick and advances seq at most once every
-- HOLD_TICKS paint ticks. EMPTY map => a single EMPTY frame (seq 0 + LAST).
local Current = { gen = 0, seq = 0, last = true, empty = true, e1 = nil, e2 = nil };

function HotbarWatch.Frame ()
  -- A1.7: NEVER rebuild inside the bridge's paint tick. Events (MarkDirty)
  -- and the sweep below only SCHEDULE the coalesced rebuild; the C_Timer
  -- callback runs it outside Update, so a bar burst can never cost a rebuild
  -- mid-frame.
  local Now = nil;
  if type(GetTime) == "function" then
    local Ok, T = pcall(GetTime);
    if Ok and type(T) == "number" then Now = T; end
  end
  if Now ~= nil then
    if LastSweep == nil then
      LastSweep = Now;
    elseif (Now - LastSweep) >= SWEEP_SECONDS then
      LastSweep = Now;
      -- Bars can change without an event we registered. Rebuild compares the
      -- map signature, so an unchanged map keeps its gen/seq cycle.
      HotbarWatch.MarkDirty();
    end
  end
  if Dirty then HotbarWatch.MarkDirty(); end   -- no-op when already scheduled

  if EntryCount > 0 then
    if Hold < HOLD_TICKS then
      Hold = Hold + 1;
    else
      Hold = 1;
      if Seq >= Last then Seq = 0 else Seq = Seq + 1; end
    end
  end

  Current.gen = Gen;
  Current.seq = EntryCount > 0 and Seq or 0;
  Current.last = (EntryCount == 0) or (Seq >= Last);
  Current.empty = (EntryCount == 0);
  Current.e1 = Entries[Current.seq * 2 + 1];
  Current.e2 = Entries[Current.seq * 2 + 2];
  return Current;
end

--- ======= TYPING VETO =======
-- Chat edit box focused => the companion must drop detections. Fail-closed:
-- any throw / unreadable state reports "not typing" (false), so the veto can
-- never wedge the feature on a broken API.
function HotbarWatch.TypingActive ()
  local Ok, Active = pcall(function ()
    if type(ChatEdit_GetActiveWindow) == "function" then
      local Window = ChatEdit_GetActiveWindow();
      if Window ~= nil then
        if type(Window.IsShown) == "function" then
          local OkS, Shown = pcall(Window.IsShown, Window);
          if OkS and Shown == false then return false; end
        end
        return true;
      end
    end
    if type(GetCurrentKeyBoardFocus) == "function" then
      local Focus = GetCurrentKeyBoardFocus();
      if Focus ~= nil then
        if type(Focus.IsShown) == "function" then
          local OkS, Shown = pcall(Focus.IsShown, Focus);
          if OkS and Shown == false then return false; end
        end
        return true;
      end
    end
    return false;
  end);
  if Ok and Active == true then return true; end
  return false;
end

--- ======= DIAGNOSTICS (/mdbx hotbar) =======
function HotbarWatch.Count () return EntryCount; end
function HotbarWatch.Gen () return Gen; end
function HotbarWatch.Seq () return EntryCount > 0 and Seq or 0; end
function HotbarWatch.Entries () return Entries; end

function HotbarWatch.Diag ()
  if Dirty then HotbarWatch.Rebuild(); end   -- a diagnostic never churns gen
  local Lines = {};
  Lines[#Lines + 1] = ("gen=%d seq=%d entries=%d cap=%d typing=%s")
    :format(Gen, HotbarWatch.Seq(), EntryCount, MAX_ENTRIES,
      HotbarWatch.TypingActive() and "yes" or "no");
  for i = 1, EntryCount do
    local Entry = Entries[i];
    Lines[#Lines + 1] = ("  slot=%d %s id=%d name=%s binding=%s ready=%s")
      :format(Entry.Slot or 0, Entry.Kind or "?", Entry.Id or 0,
        tostring(Entry.Name or "?"), tostring(Entry.Binding or "-"),
        Entry.Ready and "yes" or "no");
  end
  if EntryCount == 0 then Lines[#Lines + 1] = "  (no keybound entries)"; end
  return table.concat(Lines, "\n");
end

--- ======= EVENT LISTENER =======
do
  local Listener = CreateFrame("Frame");
  Listener:RegisterEvent("PLAYER_ENTERING_WORLD");
  Listener:RegisterEvent("ACTIONBAR_SLOT_CHANGED");
  Listener:RegisterEvent("ACTIONBAR_PAGE_CHANGED");
  Listener:RegisterEvent("UPDATE_BINDINGS");
  Listener:RegisterEvent("UPDATE_SHAPESHIFT_FORM");
  Listener:RegisterEvent("PLAYER_SPECIALIZATION_CHANGED");
  Listener:RegisterEvent("PLAYER_TALENT_UPDATE");
  Listener:RegisterEvent("SPELLS_CHANGED");
  Listener:RegisterEvent("UPDATE_MACROS");
  Listener:RegisterEvent("SPELL_UPDATE_COOLDOWN");
  -- A1.8 bar-family events: vehicle / override / bonus bar swaps change the
  -- visible slot map without ACTIONBAR_SLOT_CHANGED on every client.
  Listener:RegisterEvent("UNIT_ENTERED_VEHICLE");
  Listener:RegisterEvent("UNIT_EXITED_VEHICLE");
  Listener:RegisterEvent("UPDATE_VEHICLE_ACTIONBAR");
  Listener:RegisterEvent("UPDATE_OVERRIDE_ACTIONBAR");
  Listener:RegisterEvent("UPDATE_BONUS_ACTIONBAR");
  -- Just mark dirty; the coalescer schedules one rebuild outside the paint
  -- tick, so a burst of events costs a single rebuild.
  Listener:SetScript("OnEvent", function () HotbarWatch.MarkDirty(); end);
end
