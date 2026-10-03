--- ============================ HEADER ============================
-- READ-ONLY readout of the live MaxDps engine. Never calls protected Lua,
-- never drives gameplay; it only reports what MaxDps already suggests.
--
-- Slot naming mirrors the in-game Spell Frame categories (vendor
-- Options.lua / SpellFrame.lua):
--
--   Main       = MaxDps.Spell (number spellID, set by Core:InvokeNextSpell)
--   Offensive  = first MaxDps.Flags[spellID]==true that is not interrupt /
--                defensive / consumable / trinket, while enableCooldowns
--                is on (classCooldowns offensive, via GlowCooldownMidnight)
--   Interrupt  = Flags entry set via MaxDps:GlowInteruptMidnight
--   Defensive  = Flags entry set via MaxDps:GlowDefensiveHPMidnight
--   Consumable = flagged spellID whose itemID is in MaxDps.Consumables
--                (potions, via GlowConsumables -> GlowCooldown)
--   Trinket    = flagged spellID in MaxDps.ItemSpells whose itemID is NOT
--                in MaxDps.Consumables (equipped on-use trinkets)
--
-- READINESS (v1.1.0, slots v1.2.0): every slot passes through MDB.IsSpellReady before it
-- is encoded. A spell on cooldown or currently unusable (out of range,
-- no resources, shapeshifted, ...) encodes as EMPTY (FLAG_VALID clear) so
-- the companion skips it and fires the next ready slot instead of
-- hammering an unavailable key. Read-only queries only: C_Spell cooldown
-- + MaxDps:CooldownConsolidated (GCD-aware) + C_Spell.IsSpellUsable.
-- MaxDps:CheckSpellUsable is deliberately NOT used here: it prints chat
-- errors on failure and has side effects beyond a boolean.
--
-- Category tracking hooks the Midnight glow entry points because every path
-- funnels through GlowIndependent(spellId, spellId, ...) with identical ids,
-- so Flags alone cannot tell cooldown apart from interrupt/defensive.
--
-- ResolveBinding(spellID) priority:
--   1. MaxDps.Spells[spellID][i].HotKey:GetText() (skip byte-226 range text),
--      expanded back to a raw binding string
--   2. action slot scan 1-180 + ACTIONBUTTON/MULTIACTIONBAR mapping
--      (mirrors SpellFrame.lua GetKeybindForSpell)
--   3. MaxDpsSpellFrame.bindText, when visible (main spell only)
--   4. spell texture -> Bars.lua texture map fallback

local addonName, MDB = ...;

local InterruptSet = {};  -- spellID -> true, via GlowInteruptMidnight
local DefensiveSet = {};  -- spellID -> true, via GlowDefensiveHPMidnight
local CooldownSet = {};   -- spellID -> true, via GlowCooldownMidnight/GlowCooldown

--- ======= v2.0/v2.1 SENSOR STATE (declared above every user) =======
-- Lua lexical rule (pinned by the 204x/287x outages): a `local` is visible
-- only BELOW its declaration. IsInterruptReady (the readiness gate) reads
-- TargetCastInterruptible for the interruptibility veto, so the sensor state
-- MUST live above it; MDB.InitSensors (far below) registers the event frames
-- that flip these plain booleans.
local SensorEvents = false;
local PlayerCasting, PlayerChanneling = false, false;
local TargetCasting, TargetCastInterruptible = false, nil;
-- Watchdog timestamps (plain GetTime numbers, never secret): a missed STOP
-- event must not latch "casting" forever and hold the whole rotation.
local PlayerCastSince, TargetCastSince = nil, nil;

-- ======= v3.6 TAINT-SAFE MELEE PROBE STATE =======
-- Declared above every user for the same lexical reason as the cast sensors:
-- EnsureProbeEvents (below) registers the event frame that flips these plain
-- booleans, and MDB.ProbeTargetMelee (far below) reads them. CheckInteract-
-- Distance is #nocombat-restricted in 12.x, so it is never called while any
-- of these gates is closed; ADDON_ACTION_BLOCKED is the breaker of last resort.
local MeleeCache = { val = nil, guid = nil, at = 0 };  -- per-target result
local SafeAfter = 0;        -- no probe before this GetTime stamp
local InCombatEv = false;   -- event-driven combat flag (regen/encounter)
local BlockHits = 0;        -- ADDON_ACTION_BLOCKED count; 2 = disabled

-- Plain GetTime stamp (pcall-contained; nil when missing). Lives here so both
-- the event frame and the probe can share it without a lexical gap.
local function SafeNow ()
  if type(GetTime) ~= "function" then return nil; end
  local Ok, Now = pcall(GetTime);
  if Ok and type(Now) == "number" then return Now; end
  return nil;
end

local function MaxDpsEngine ()
  local Direct = _G.MaxDps;
  if Direct and (Direct.Spells or Direct.Flags or Direct.GlowIndependent) then
    return Direct;
  end
  -- MaxDps publishes itself as _G["MaxDps"] from Core.lua; fall back to the
  -- AceAddon registry when load order leaves the global unset.
  if type(LibStub) == "table" and LibStub.GetAddon then
    local Ok, Engine = pcall(LibStub.GetAddon, LibStub, "MaxDps", true);
    if Ok then return Engine; end
  end
  return Direct;
end

--- ======= ZERO-TAINT DESIGN (v1.3.0) =======
-- Deep-research outcome (Sep-2026, after the 99x/384x secret-compare,
-- 204x/97x/287x/260x guard-nil-call, and 14x/52x secret-boolean waves):
--
-- The guard strategy is ABANDONED. issecretvalue() verdicts arrive tainted,
-- so branching on the verdict needs a guard; guarding the guard needs
-- another guard — infinite regress, and every layer added a new load-order
-- nil-call. Midnight's OWN documented model says the same thing: untainted
-- code may use secrets freely, tainted code must NEVER branch on them —
-- instead pass secrets INTO engine APIs (StatusBar, ColorCurve, Duration)
-- or avoid reading them at all.
--
-- So the bridge no longer reads secrets, period:
--   1. Hook args are NEVER inspected (SyncSet ignores its spellID arg —
--      it may be a secret; even type() is legal but pointless). Each hook
--      only records THAT its category fired (plain-boolean dirty flag).
--      The encode path re-derives WHICH spell from MaxDps.Flags scanned
--      with scrubsecretvalues-cleaned keys.
--   2. Readiness uses only NeverSecret fields (C_Spell.GetSpellCooldown's
--      isActive/isEnabled/isOnGCD) + C_Spell.GetSpellCooldownDuration
--      Duration objects (EvaluateRemainingDuration on a ColorCurve —
--      engine-side, secret-blind) + C_Spell.IsSpellUsable's plain boolean
--      via dropsecretaccess() containment. NO raw startTime/duration/
--      charge arithmetic anywhere. issecretvalue is NEVER called — no
--      guards, no regress, no load-order surface. A leftover IsSecret
--      reference fails LOUDLY (nil call), caught by luac/grep.
--   3. Interrupt cast state: UnitCastingInfo NOT called (all returns
--      tainted in combat). Instead: target-exists/unit-can-attack checks
--      (unit tokens are strings, never secret) + upstream's own overlay
--      alpha dimming is OBSERVED, not read — MaxDps dims the interrupt
--      overlay to alpha 0 for non-interruptible casts, and our Interrupt
--      slot encodes whenever the category is dirty; the companion's
--      existing priority (Interrupt first) is unchanged.
--
-- Lua lexical rule (pinned — caused 204x/97x/287x): `local function` is
-- visible ONLY below its line. Define-then-register, top-down, always.
--- ======= CATEGORY HOOKS (arg-blind) =======

local function SyncSet (Set, _SpellID)
  -- Arg-blind: mark the category dirty; the Update tick resolves the
  -- spell from scrubbed Flags (see SnapshotFlags). The raw arg is never
  -- read, compared, cached, or printed — it may be a secret.
  Set.__dirty = true;
end

local function WipeCache ()
  if MDB._BindCache then wipe(MDB._BindCache); end
end

function MDB.EnsureHooks ()
  if MDB._ReaderHooked then return; end
  local MaxDps = MaxDpsEngine();
  if not MaxDps then return; end
  if type(hooksecurefunc) ~= "function" then MDB._ReaderHooked = true; return; end

    local function TryHook (Method, Set)
    if type(MaxDps[Method]) == "function" then
      pcall(hooksecurefunc, MaxDps, Method, function (_, SpellID)
        if Set then
          -- Secret-safe: SyncSet filters secret args (UNKNOWN → drop).
          if type(SpellID) == "number" then SyncSet(Set, SpellID); end
        else
          -- MaxDps:Fetch rebuilds Spells/Flags/ItemSpells wholesale.
          wipe(InterruptSet);
          wipe(DefensiveSet);
          wipe(CooldownSet);
        end
        WipeCache();
      end);
    end
  end

  TryHook("GlowInteruptMidnight", InterruptSet);
  TryHook("GlowDefensiveHPMidnight", DefensiveSet);
  TryHook("GlowCooldownMidnight", CooldownSet);
  TryHook("GlowCooldown", CooldownSet);
  TryHook("Fetch", nil);
  MDB._ReaderHooked = true;
end

--- ======= DIAGNOSTICS =======

local function DiagPrint (Message)
  DEFAULT_CHAT_FRAME:AddMessage("|cFF00D8FFMDB|r: " .. Message);
end

function MDB.Diag ()
  -- v1.3.0 zero-taint diag: scrubbed snapshots only (never raw tables),
  -- dropsecretaccess() containment so NOTHING here can throw under taint.
  -- A diagnostic must never be the error source.
  local OkDiag, Msg = pcall(function ()
    if type(dropsecretaccess) == "function" then dropsecretaccess(); end
    local MDPS = MaxDpsEngine();
    if not MDPS then return "no MaxDps engine"; end
    local Count = 0;
    local WithHotKey, HotKeyText = 0, "-";
    local Spells = MDPS.Spells;
    if type(Spells) == "table" then
      local Clean = Spells;
      if type(scrubsecretvalues) == "function" then
        local OkS, C = pcall(scrubsecretvalues, Spells);
        if OkS and type(C) == "table" then Clean = C; end
      end
      for SpellID, Buttons in pairs(Clean) do
        if type(Buttons) == "table" then
          Count = Count + 1;
          for i = 1, #Buttons do
            local Button = Buttons[i];
            local HotKey = Button and Button.HotKey;
            if not HotKey and Button and Button.GetName then
              local Name = Button:GetName();
              if Name then HotKey = _G[Name .. "HotKey"]; end
            end
            if HotKey and HotKey.GetText then
              local Ok, Text = pcall(HotKey.GetText, HotKey);
              if Ok and type(Text) == "string" and Text ~= "" and string.byte(Text) ~= 226 then
                WithHotKey = WithHotKey + 1;
                if HotKeyText == "-" then HotKeyText = tostring(SpellID) .. "=" .. Text; end
              end
            end
          end
        end
      end
    end
    local BarHit = {};
    for _, Prefix in ipairs({ "ActionButton", "MultiBarBottomLeftButton", "MultiBarBottomRightButton",
      "MultiBarRightButton", "MultiBarLeftButton", "ElvUI_Bar1Button", "BT4Button" }) do
      if _G[Prefix .. "1"] then BarHit[#BarHit + 1] = Prefix .. "1"; end
    end
    local SlotHit = 0;
    for Slot = 1, 180 do
      local OkInfo, ActionType = pcall(GetActionInfo, Slot);
      if OkInfo and ActionType == "spell" then SlotHit = SlotHit + 1; end
    end
    return ("diag spells=%d withHotKey=%d e.g.%s bars={%s} spellSlots=%d/180 proto=%d")
      :format(Count, WithHotKey, HotKeyText, table.concat(BarHit, ","), SlotHit, 2);
  end);
  if OkDiag and type(Msg) == "string" then DiagPrint(Msg);
  else DiagPrint("diag failed (taint-contained, no state touched)"); end
end

--- ======= ENGINE ENSURE =======

-- MaxDps class modules are LoadOnDemand and the rotation only starts on
-- game events (login, combat, target change). On a fresh idle login
-- NextSpell can stay nil with nothing scheduled, so there is no suggestion
-- to encode. EnsureEngine performs exactly the init the login event path
-- performs (LOADING_SCREEN_DISABLED -> UpdateSpellsAndTalents +
-- InitRotations + EnableRotation), minus starting timers: LoadAddOn the
-- class module, InitRotations to pick the spec function, Fetch to scan the
-- bars. Everything is pcall-guarded and read-only w.r.t. gameplay: no
-- casts, no targeting, no protected calls. Without this the bridge (and
-- the companion) would sit idle until first combat with no way to
-- calibrate or verify the link in town.
function MDB.EnsureEngine ()
  local MaxDps = MaxDpsEngine();
  if not MaxDps then return; end

  if type(MaxDps.NextSpell) ~= "function" then
    -- 1. Demand-load the class module, exactly like LoadModule does.
    local ClassFile = select(2, UnitClass("player"));
    local ClassName = nil;
    if MaxDps.ClassId and MaxDps.Classes then
      local _, _, ClassId = UnitClass("player");
      if ClassId and MaxDps.Classes[ClassId] then ClassName = MaxDps.Classes[ClassId]; end
    end
    if not ClassName and ClassFile then
      ClassName = strupper(strsub(ClassFile, 1, 1)) .. strlower(strsub(ClassFile, 2));
    end
    if ClassName and type(LoadAddOn) == "function" then
      pcall(LoadAddOn, "MaxDps_" .. ClassName);
    end
    -- 2. Pick the spec rotation function (sets NextSpell, no timers).
    if type(MaxDps.InitRotations) == "function" then
      pcall(MaxDps.InitRotations, MaxDps, true);
    end
  end

  -- 3. Scan the bars so spellID -> HotKey resolution works even before
  -- MaxDps's own Fetch ran (it only runs while rotationEnabled).
  if (not MaxDps.Spells or not next(MaxDps.Spells))
    and type(MaxDps.Fetch) == "function" then
    pcall(MaxDps.Fetch, MaxDps, "MaxDpsBridge");
  end

  -- 4. Build FrameData so the retail rotation function can run read-only.
  -- Hunter:BeastMastery indexes MaxDps.FrameData.ACSpells on entry; without
  -- PrepareFrameData (normally run inside InvokeNextSpell) the pcall in
  -- GetMainSpellID would die on a nil index and look like "no suggestion".
  if (not MaxDps.FrameData or not MaxDps.FrameData.ACSpells) then
    if type(MaxDps.PrepareFrameData) == "function" then
      pcall(MaxDps.PrepareFrameData, MaxDps);
    end
    if type(MaxDps.UpdateAuraData) == "function" then
      pcall(MaxDps.UpdateAuraData, MaxDps);
    end
  end
end

--- ======= SLOT READOUT (guards live above CATEGORY HOOKS; duplicate deleted v1.2.3) =======

function MDB.GetMainSpellID ()
  -- v1.3.2 MAIN-SLOT FIX (Sep-2026: main pressed 1-2 keys then stuck —
  -- E fired, 1/2/R and Shift+E never did):
  --
  -- (a) SOURCE: MaxDps.Spell is STALE between engine ticks and usually
  --     secret/tainted in combat (caught by pcall → nil → EMPTY). The
  --     RELIABLE live pick is MaxDps.SpellsGlowing: InvokeNextSpell →
  --     GlowNextSpell → GlowSpell sets SpellsGlowing[spellID] = 1 for the
  --     CURRENT main pick (Core.lua:828-829, Buttons.lua:1216) and
  --     GlowClear zeroes it on change — upstream maintains it on its own
  --     trusted path every rotation tick. Scan it scrubbed (never raw).
  -- (b) GATE: the v1.1.0 readiness gate (CooldownConsolidated + Duration
  --     + IsSpellUsable) was built for COOLDOWN triage — skip the unready
  --     while others fire. Applied to MAIN it inverts: the main pick is
  --     USUALLY "unready" (just fired → on GCD; pooling → no resources
  --     yet), so the gate held EVERY main suggestion EMPTY and the engine
  --     sat on "holding" while MaxDps glowed plainly. The v1.3.1
  --     "return-it-anyway" fallback papered over Blow #1 but STILL ran
  --     the tainted gate first (wasted tick + pcall-contained throw that
  --     poisoned _BindCache misses for the same spell).
  -- NEW RULE: the MAIN slot trusts upstream unconditionally — if MaxDps
  -- glows it (SpellsGlowing) or picks it (Spell), it encodes. Upstream
  -- ALREADY decided castability on its trusted path (CheckSpellUsable +
  -- CooldownConsolidated inside InvokeNextSpell, Core.lua:791-797); our
  -- second-guessing with tainted reads can only veto correct answers.
  -- The readiness gate KEEPS guarding the five SITUATIONAL slots
  -- (off/def/cons/trin/int), where "skip the unready while others fire"
  -- is the right semantic. Wrong-main costs one GCD; no-main costs the
  -- whole rotation. Idle-by-design (no glow, no pick) still → nil →
  -- EMPTY + Idle downstream — correct, not a failure.
  local MaxDps = MaxDpsEngine();
  if not MaxDps then return nil; end
  local Glowing = MaxDps.SpellsGlowing;
  if type(Glowing) == "table" then
    local Clean = Glowing;
    if type(scrubsecretvalues) == "function" then
      local OkS, C = pcall(scrubsecretvalues, Glowing);
      if OkS and type(C) == "table" then Clean = C; end
    end
    local OkScan, Found = pcall(function ()
      if type(dropsecretaccess) == "function" then dropsecretaccess(); end
      local Best = nil;
      for ID, On in pairs(Clean) do
        if type(ID) == "number" and ID ~= 0 and On == 1 then
          if not Best or ID < Best then Best = ID; end
        end
      end
      return Best;
    end);
    if OkScan and type(Found) == "number" and Found ~= 0 then return Found; end
  end
  local Spell = MaxDps.Spell;
  if type(Spell) == "number" and Spell ~= 0 then return Spell; end
  return nil;
end

-- v1.3.0: category sets carry ONLY dirty flags now (SyncSet is arg-blind).
-- WHICH spell is flagged is re-derived here from MaxDps.Flags by scanning
-- scrubbed keys: scrubsecretvalues(MaxDps.Flags) returns a second table in
-- which every secret key/value is replaced with nil — iterating THAT table
-- can never observe a secret, so no guard, no compare-guard, no taint.
-- Membership test: dirty category ∧ flagged-true ∧ on-bars (MaxDps.Spells).
-- Stale entries (Flags cleared by DestroyAllOverlays/Fetch) and unready
-- spells (cooldown / unusable) are pruned from the dirty set so the
-- companion never hammers an unavailable key while other slots have live
-- suggestions. Pruning only touches OUR OWN sets (plain data), never
-- upstream tables.
-- PERF (v1.3.9): per-tick memo. The six slot getters each used to scrub
-- MaxDps.Flags / ItemSpells and re-resolve class+spec on EVERY call — up
-- to ~10 scrubsecretvalues table copies and ~6 UnitClass/GetSpecialization
-- lookups per 50 ms tick, all allocating Lua garbage inside the game's
-- frame budget (GC pressure = WoW frametime spikes). Bridge.Update now
-- calls MDB.BeginTick() once; the first getter computes, the rest reuse.
local Tick = { Gen = 0, Flags = nil, FlagsOwner = nil, Items = nil, Class = nil, ClassFile = nil, Spec = nil };

function MDB.BeginTick ()
  Tick.Gen = Tick.Gen + 1;
  Tick.Flags, Tick.FlagsOwner, Tick.Items = nil, nil, nil;
  Tick.Class, Tick.ClassFile, Tick.Spec = nil, nil, nil;
end

local function ScrubbedFlags ()
  local MaxDps = MaxDpsEngine();
  if not MaxDps then return nil, nil; end
  -- Per-tick memo (see Tick above).
  if Tick.FlagsOwner == MaxDps and Tick.Flags ~= nil then
    return Tick.Flags, MaxDps;
  end
  local Flags = MaxDps.Flags;
  if type(Flags) ~= "table" then return nil, MaxDps; end
  local Clean = Flags;
  if type(scrubsecretvalues) == "function" then
    local Ok, C = pcall(scrubsecretvalues, Flags);
    if Ok and type(C) == "table" then Clean = C; end
  end
  -- No scrub API (pre-Midnight client): table holds no secrets by
  -- construction, iterate directly.
  Tick.Flags, Tick.FlagsOwner = Clean, MaxDps;
  return Clean, MaxDps;
end

-- v1.3.5 CATEGORY TRUTH (fixes #3 interrupts not executing): arg-blind
-- hooks no longer record WHICH spells belong to a category, so routing by
-- dirty-set membership was broken — the Interrupt scan could claim ANY
-- flagged spell (a smaller-id defensive won), and the Offensive scan
-- excluded nothing (InterruptSet/DefensiveSet hold no keys). The real
-- kick never encoded. Category is now read from upstream's STATIC class
-- tables (plain data, never secret, exact):
--   interrupt = MaxDps.classInterrupts[class][spec] values
--   defensive = MaxDps.classCooldowns[class][spec].defensive values
--   offensive = MaxDps.classCooldowns[class][spec].offensive values
local function ClassSpec ()
  local MaxDps = MaxDpsEngine();
  if not MaxDps then return nil; end
  -- Per-tick memo (see Tick above): resolved once per update, not once per
  -- candidate spell (CategoryOf calls this for every flagged spell).
  if Tick.Class == MaxDps then return MaxDps, Tick.ClassFile, Tick.Spec; end
  if type(UnitClass) ~= "function" then return nil; end
  local OkC, _, classFile = pcall(UnitClass, "player");
  if not OkC or not classFile then return nil; end
  local specName = nil;
  -- v2.1 API HARDENING: GetSpecialization/GetSpecializationInfo are
  -- deprecated (11.2+); prefer C_SpecializationInfo when present and fall
  -- back to the legacy globals. Both chains are pcall-contained and the
  -- idtospec INDEXING is contained too (no unguarded secret indexing).
  local specID = nil;
  if _G.C_SpecializationInfo
    and type(_G.C_SpecializationInfo.GetSpecialization) == "function"
    and type(_G.C_SpecializationInfo.GetSpecializationInfo) == "function" then
    local OkS, Index = pcall(_G.C_SpecializationInfo.GetSpecialization);
    if OkS and Index then
      local OkI, ID = pcall(_G.C_SpecializationInfo.GetSpecializationInfo, Index);
      if OkI and ID then specID = ID; end
    end
  end
  if not specID and type(GetSpecialization) == "function" and type(GetSpecializationInfo) == "function" then
    -- GetSpecialization() is contained on its own line: nesting a call in
    -- pcall's argument list evaluates it OUTSIDE the pcall (a secret/throw
    -- there would abort the whole frame).
    local OkI, Index = pcall(GetSpecialization);
    if OkI and Index then
      local OkS, ID = pcall(GetSpecializationInfo, Index);
      if OkS and ID then specID = ID; end
    end
  end
  if specID and MaxDps.idtospec then
    local OkName, Name = pcall(function () return MaxDps.idtospec[specID] end);
    if OkName and type(Name) == "string" then specName = Name; end
  end
  if not specName then return nil; end
  Tick.Class, Tick.ClassFile, Tick.Spec = MaxDps, classFile, specName;
  return MaxDps, classFile, specName;
end

local function SetHas (Tbl, Id)
  if type(Tbl) ~= "table" then return false; end
  for _, v in pairs(Tbl) do
    if v == Id then return true; end
  end
  return false;
end

-- Returns "interrupt" | "defensive" | "offensive" | nil (item spells and
-- unknown spells fall to nil — the item buckets have their own getters).
local function CategoryOf (Id)
  local MaxDps, classFile, specName = ClassSpec();
  if not MaxDps or not classFile or not specName then return nil; end
  local Interrupts = MaxDps.classInterrupts and MaxDps.classInterrupts[classFile]
    and MaxDps.classInterrupts[classFile][specName];
  if SetHas(Interrupts, Id) then return "interrupt"; end
  local CDs = MaxDps.classCooldowns and MaxDps.classCooldowns[classFile]
    and MaxDps.classCooldowns[classFile][specName];
  if type(CDs) == "table" then
    if SetHas(CDs.defensive, Id) then return "defensive"; end
    if SetHas(CDs.offensive, Id) then return "offensive"; end
  end
  return nil;
end

local function FirstFlagged (WantCategory, IsInterrupt, RequireBinding)
  local Clean, MaxDps = ScrubbedFlags();
  if not Clean then return nil; end
  local Spells = MaxDps and MaxDps.Spells;
  if not Spells then return nil; end
  local Best = nil;
  for SpellID, On in pairs(Clean) do
    -- Clean keys/values are proven non-secret by scrub (or by client age).
    -- Plain Lua compares from here down — NO secret guards needed.
    if type(SpellID) == "number" and SpellID ~= 0 and On == true and Spells[SpellID]
      and CategoryOf(SpellID) == WantCategory then
      local Ready;
      if IsInterrupt then Ready = MDB.IsInterruptReady(SpellID);
      else Ready = MDB.IsSpellReady(SpellID); end
      if Ready and (not RequireBinding or MDB.ResolveBinding(SpellID)) then
        if not Best or SpellID < Best then Best = SpellID; end
      end
    end
  end
  return Best;
end

function MDB.GetInterruptSpellID ()
  return FirstFlagged("interrupt", true);
end

function MDB.GetDefensiveSpellID ()
  local Id = MDB.GetDefensiveCandidate and MDB.GetDefensiveCandidate();
  return Id;
end

-- spellID -> itemID for flagged item spells, plus which itemIDs are
-- potions/consumables (MaxDps.Consumables). Two disjoint Spell Frame
-- buckets come out of this: consumable = potion items, trinket = the rest
-- (equipped on-use trinkets). v1.3.0: iterated over ScrubbedFlags (never
-- raw MaxDps tables), so keys are proven non-secret — plain compares.
local function ItemSpellIDs ()
  local MaxDps = MaxDpsEngine();
  if Tick.Items ~= nil then return Tick.Items; end
  local Out = {};
  if MaxDps and MaxDps.ItemSpells then
    local CleanItems;
    if type(scrubsecretvalues) == "function" then
      local Ok, Clean = pcall(scrubsecretvalues, MaxDps.ItemSpells);
      if Ok and type(Clean) == "table" then CleanItems = Clean; end
    end
    for ItemID, ItemSpellID in pairs(CleanItems or MaxDps.ItemSpells) do
      if type(ItemSpellID) == "number" and type(ItemID) == "number" then
        Out[ItemSpellID] = ItemID;
      end
    end
  end
  Tick.Items = Out;   -- memo even when empty: one build per tick
  return Out;
end

local function IsConsumableItem (ItemID)
  local MaxDps = MaxDpsEngine();
  -- Consumables is a static data table (never secret), but gate anyway:
  -- a secret ItemID must never index-compare. type() never throws.
  if type(ItemID) ~= "number" then return false; end
  return MaxDps and MaxDps.Consumables and MaxDps.Consumables[ItemID] == true;
end

local function BestFlaggedItem (WantConsumable)
  local Clean, MaxDps = ScrubbedFlags();
  if not Clean then return nil; end
  local Spells = MaxDps and MaxDps.Spells;
  if not Spells then return nil; end
  local Items = ItemSpellIDs();
  local Best = nil;
  for SpellID, On in pairs(Clean) do
    -- Clean = proven non-secret. Plain compares from here down.
    if type(SpellID) == "number" and SpellID ~= 0 and On == true then
      local ItemID = Items[SpellID];
      if ItemID and Spells[SpellID] then
        local IsConsumable = IsConsumableItem(ItemID);
        if (WantConsumable and IsConsumable) or (not WantConsumable and not IsConsumable) then
          if MDB.IsSpellReady(SpellID) then
            if not Best or SpellID < Best then Best = SpellID; end
          end
        end
      end
    end
  end
  return Best;
end

function MDB.GetConsumableSpellID ()
  return BestFlaggedItem(true);
end

function MDB.GetTrinketSpellID ()
  return BestFlaggedItem(false);
end

-- Offensive = classCooldowns offensive bucket (Spell Frame "Show offensive
-- spells"): flagged, on the bars, and classified "offensive" by the class
-- tables (v1.3.5 — exact category, no item/exclusion guesswork).
-- v3.0.0: when MaxDps names no bound offensive, the curated per-spec
-- `offensive` gap-fill list supplies the first ready+bound entry (see
-- MDB.GetOffensiveCandidate, defined next to the defensive candidate below
-- because it reuses the scoped ExtraSpellID helper). The whole path stays
-- inside MaxDps's own `enableCooldowns` switch.
function MDB.GetOffensiveSpellID ()
  return MDB.GetOffensiveCandidate();
end

-- Back-compat alias: C# mirrors and old chat macros may still call the old
-- cooldown name. Same bucket as offensive.
function MDB.GetCooldownSpellID ()
  return MDB.GetOffensiveSpellID();
end

--- ======= READINESS GATE (v1.1.0 slots; v1.3.0 zero-taint rewrite) =======
-- v1.3.0: the v1.1.0–v1.2.x gate compared RAW C_Spell numbers
-- (startTime/duration/charges) under taint → 99x/384x secret-compare
-- outage. The v1.1.0 behaviour is PRESERVED (same skip-vs-fire decisions)
-- but re-implemented with ZERO raw-secret reads:
--
--   1. Upstream verdict FIRST: MaxDps:CooldownConsolidated(spell) computes
--      on MaxDps's own trusted path and returns a table whose `.ready`
--      field is unwrapped via scrubsecretvalues (secret → nil → fall
--      through). A plain true/false decides immediately. This is the
--      SAME helper, SAME GCD/tail forgiveness as v1.1.0 — just consumed
--      through the scrub boundary instead of direct field compares.
--   2. NeverSecret fallback: C_Spell.GetSpellCooldown returns isActive /
--      isEnabled / isOnGCD as NeverSecret booleans (wiki-documented,
--      safe to branch on even in combat). Inactive/disabled/GCD-only ⇒
--      ready. Anything else ⇒ defer to Duration objects.
--   3. Duration-object fallback: C_Spell.GetSpellCooldownDuration(spell)
--      returns a Duration object; :EvaluateRemainingDuration(ColorCurve)
--      runs ENGINE-SIDE (secret-blind, the documented Midnight pattern —
--      upstream Buttons.lua:1094-1096 already does exactly this for glow
--      alpha). Remaining ≤ 0.5 s tail ⇒ ready. No Lua-side number ever
--      materialises, so nothing can throw.
--   4. Usable: C_Spell.IsSpellUsable via dropsecretaccess() containment —
--      dropsecretaccess() strips secret access from OUR function, so the
--      boolean it returns is provably plain (documented Midnight API).
--      Plain false ⇒ unusable; anything else ⇒ usable.
--   5. Interrupt cast state: target presence via UnitExists (unit TOKENS
--      are strings, never secret) + upstream overlay observation — MaxDps
--      dims the interrupt overlay to alpha 0 for non-interruptible casts
--      (Buttons.lua:1136-1143); when the Interrupt category is dirty AND
--      upstream flagged it, encode it. No UnitCastingInfo call at all
--      (every return tainted in combat).
--
-- Fail-open throughout: unknown ⇒ ready/encode. A missed cooldown skip
-- costs one keypress; a thrown tick costs the WHOLE frame (all 6 slots).
-- Rule for future edits: NO C_Spell/C_Item field arithmetic in this file
-- — verdicts via CooldownConsolidated, NeverSecret flags, Duration
-- objects, or dropsecretaccess() only.
local function Scrubbed (Value)
  -- One scrub boundary for single values: secret → nil (UNKNOWN),
  -- plain → itself. type() never throws, so probe it first for speed;
  -- the scrub call itself is pcall-wrapped (API may not exist pre-12.0).
  if Value == nil then return nil; end
  if type(scrubsecretvalues) ~= "function" then return Value; end
  local Ok, Clean = pcall(scrubsecretvalues, Value);
  if not Ok then return nil; end
  return Clean;  -- secret arrived as nil; plain arrives intact
end

local function HasCharges (SpellID)
  -- Charges via upstream verdict ONLY (no C_Spell.GetSpellCharges read —
  -- currentCharges is secret in combat). CooldownConsolidated already
  -- folds charges into .ready (Helper.lua:1972-1986: charges>=max ⇒
  -- remains 0; charges>=1 ⇒ remains 0). So: ready ⇒ charged-or-n/a.
  -- A 0-charge spell reports .ready=false ⇒ CooldownReady false ⇒ skip.
  -- Return values mirror the old contract: false = conclusively empty,
  -- true = conclusively charged, nil = unknown (cooldown path decides).
  -- Without a raw charge read, "conclusively charged" is unobservable —
  -- return nil always and let CooldownReady decide via .ready.
  return nil;
end

-- PERF (v1.3.9): the evaluation curve is built ONCE and reused. The old
-- code called CreateColorCurve + SetType + 2x AddPoint on EVERY slot,
-- every tick — ~6 curve objects + ~24 engine calls per 50 ms, pure garbage
-- for the game's collector. The curve is a constant (red→green ramp), so
-- lazily creating it once removes all of that.
local ReadyCurve = nil;
local ReadyCurveTried = false;
local function GetReadyCurve ()
  if ReadyCurve or ReadyCurveTried then return ReadyCurve; end
  ReadyCurveTried = true;
  if not (_G.C_CurveUtil and type(_G.C_CurveUtil.CreateColorCurve) == "function") then return nil; end
  local Ok, Curve = pcall(_G.C_CurveUtil.CreateColorCurve);
  if not Ok or Curve == nil then return nil; end
  -- One containment for the whole setup: the Enum/CreateColor lookups must
  -- be INSIDE the pcall or a client without them throws on argument
  -- evaluation (harness-caught; a readout must never throw).
  local OkSetup = pcall(function ()
    Curve:SetType(Enum.LuaCurveType.Linear);
    Curve:AddPoint(0.0, CreateColor(1, 0, 0, 1));
    Curve:AddPoint(1.0, CreateColor(0, 1, 0, 1));
  end);
  if not OkSetup then return nil; end
  ReadyCurve = Curve;
  return ReadyCurve;
end

local function DurationRemainingOk (SpellID)
  -- Duration-object fallback: engine-side remaining-time evaluation.
  -- Returns true = ready (remaining ≤ 0.5 s tail or inactive), false =
  -- on real cooldown, nil = API unavailable/failed (caller fails open).
  if not (_G.C_Spell and type(_G.C_Spell.GetSpellCooldownDuration) == "function") then
    return nil;
  end
  local Curve = GetReadyCurve();
  if Curve == nil then return nil; end;
  local OkDur, Duration = pcall(_G.C_Spell.GetSpellCooldownDuration, SpellID);
  if not OkDur or Duration == nil then return nil; end;
  -- Duration objects are engine userdata; method calls on them run
  -- engine-side and accept secret internals (documented pattern).
  local OkEval, Remaining = pcall(Duration.EvaluateRemainingDuration, Duration, Curve);
  if not OkEval then return nil; end;
  local Clean = Scrubbed(Remaining);
  if type(Clean) ~= "number" then return nil; end;
  return Clean <= 0.5;
end

local function CooldownReady (SpellID)
  local MaxDps = MaxDpsEngine();
  if MaxDps and type(MaxDps.CooldownConsolidated) == "function" then
    -- Upstream verdict through the scrub boundary: .ready secret/nil ⇒
    -- fall through (never `not Info.ready` — a secret would invert).
    local Ok, Info = pcall(MaxDps.CooldownConsolidated, MaxDps, SpellID);
    if Ok and type(Info) == "table" then
      local Ready = Scrubbed(Info.ready);
      if Ready == true then return true; end
      if Ready == false then return false; end
    end
    -- pcall failed or .ready scrubbed to nil: fall through below.
  end
  -- NeverSecret fallback: isActive/isEnabled/isOnGCD are documented
  -- NeverSecret (safe to branch even in combat). Inactive/disabled ⇒
  -- ready (nothing ticking). GCD-only ⇒ ready (press-time forgiveness,
  -- same rule the v1.1.0 gate applied via GCDduration compare).
  if _G.C_Spell and type(_G.C_Spell.GetSpellCooldown) == "function" then
    local Ok, Info = pcall(_G.C_Spell.GetSpellCooldown, SpellID);
    if Ok and type(Info) == "table" then
      local Active = Scrubbed(Info.isActive);
      local Enabled = Scrubbed(Info.isEnabled);
      local OnGCD = Scrubbed(Info.isOnGCD);
      if Active == false then return true; end
      if Enabled == false then return true; end
      if OnGCD == true then return true; end
      -- Active (or unknown-active) with no timing info yet: ask the
      -- Duration object. Unknown-active + no Duration ⇒ fail OPEN.
      local DurOk = DurationRemainingOk(SpellID);
      if DurOk ~= nil then return DurOk; end
      return true;
    end
  end
  return true;  -- fail-open: unknown ⇒ ready
end

-- PERF (v1.3.9): the containment call used to be an anonymous closure
-- created PER SLOT PER TICK (6 closures/50 ms = 120/s of pure Lua garbage).
-- A named local function costs nothing to reuse.
local function UsableContained (SpellID)
  if type(dropsecretaccess) == "function" then dropsecretaccess(); end
  return _G.C_Spell.IsSpellUsable(SpellID);
end

local function UsableNow (SpellID)
  if not (_G.C_Spell and type(_G.C_Spell.IsSpellUsable) == "function") then
    return true;  -- fail-open
  end
  -- dropsecretaccess() containment: strips secret access from the probe,
  -- so the boolean it returns is provably plain (documented Midnight API).
  -- Plain false ⇒ unusable; anything else ⇒ usable. pcall-wrapped so a
  -- pre-12.0 client or a dead API degrades to fail-open.
  local Ok, Usable = pcall(UsableContained, SpellID);
  if not Ok or Usable == nil then return true; end
  if Usable == false then return false; end
  return true;
end

--- Returns true when the spell may be encoded into a slot.
--- v1.3.0: SpellID arrives from scrubbed scans (proven plain numbers) —
--- no entry guard needed; type check is pure Lua on our own data.
function MDB.IsSpellReady (SpellID)
  if type(SpellID) ~= "number" or SpellID == 0 then return false; end
  if not CooldownReady(SpellID) then return false; end
  return UsableNow(SpellID);
end

--- Interrupt slots need a live, interruptible cast on the target — the
--- upstream flag alone is not enough (Devotion-special: vendor
--- Buttons.lua:1136-1143 sets Flags + dims overlay alpha to 0 instead of
--- clearing the flag for non-interruptible casts).
--- v1.3.0 zero-taint rewrite: NO UnitCastingInfo/UnitChannelInfo call AT
--- ALL (every return is tainted in combat — 14x/52x Reader.lua:659; even
--- the `== nil` presence check and the pcall verdict-compare detonate).
-- Instead the gate mirrors what UPSTREAM's own trusted path already
-- decided: GlowInteruptMidnight (Buttons.lua:1113-1157) sets Flags[spell]
-- = true only when its engine-side Duration objects report a live cast
-- (UnitCastingDuration/UnitChannelDuration are Duration objects — the
-- documented secret-blind pattern; `if color then` is engine-internal).
-- Our hook observed the category fire (dirty flag) and the scrubbed Flags
-- scan confirmed membership — that IS upstream's live-cast verdict,
-- reached without us touching a single secret. Cooldown/usable gates
-- above already passed, so: encode it. No second cast check exists that
-- wouldn't reintroduce tainted reads.
--
-- v2.1 INTERRUPTIBILITY VETO: upstream's Flags does NOT encode
-- interruptibility - GlowInteruptMidnight sets Flags=true for every live
-- cast and only dims the overlay alpha to 0 for a NOT_INTERRUPTIBLE one.
-- The v5 target sensor flips a PLAIN boolean on UNIT_SPELLCAST_INTERRUPTIBLE
-- / UNIT_SPELLCAST_NOT_INTERRUPTIBLE (the state is in the event NAME; no
-- payload is read). Explicit false => empty slot; nil => fail open.
-- UnitCastingInfo's notInterruptible is secret for non-player units, so this
-- event pair is the only safe interruptibility signal under Midnight.
function MDB.IsInterruptReady (SpellID)
  if type(SpellID) ~= "number" or SpellID == 0 then return false; end
  if not MDB.IsSpellReady(SpellID) then return false; end
  -- Target presence uses UNIT TOKENS (strings, never secret): no target
  -- ⇒ no interrupt (same TargetState logic the bridge already applies;
  -- duplicated here so the slot encodes EMPTY instead of a kick into
  -- nothing). pcall-wrapped: UnitExists itself is safe, but under taint
  -- even safe calls ride our (tainted) execution — pcall contains it.
  if type(UnitExists) ~= "function" then return true; end
  local OkT, HasTarget = pcall(UnitExists, "target");
  if not OkT or not HasTarget then return false; end
  -- v2.1 SENSOR VETO: an explicit NOT_INTERRUPTIBLE observation (plain
  -- boolean from the event NAME; both interruptibility events fire at cast
  -- start) must not encode. nil = unknown = fail open. Upstream's Flags
  -- alone cannot distinguish this (it only dims the overlay alpha).
  if SensorEvents and TargetCastInterruptible == false then return false; end
  return true;
end

--- v3.5 (CC fix): the interrupt slot is pinned on MaxDps's own interrupt ONLY
--- while the target sensor confirms a live cast; otherwise the slot rotates
--- the curated CC pool. This is the bridge-side counterpart of the companion's
--- casting-only CC gate (Q1). Unknown interruptibility (the UNIT_SPELLCAST_
--- INTERRUPTIBLE event has not arrived) still fails OPEN and pins — only a
--- definite "not casting" passes the slot to the CC pool; a definite
--- "not interruptible" was already vetoed by IsInterruptReady. A client with
--- no sensor events at all fails OPEN to the ordinary verdict, so this can
--- only ever DEMOTE when the sensor gives a definite state.
function MDB.IsInterruptPinReady (SpellID)
  if not MDB.IsInterruptReady(SpellID) then return false; end
  if not SensorEvents then return true; end
  return TargetCasting and TargetCastInterruptible ~= false;
end

--- ======= SPELL VARIANTS (protocol Ext2 / v3.0.0) =======
-- One curated ability can be known to the client under several ids: the
-- base spell (FindBaseSpellByID), the talent override (FindSpellOverrideByID
-- / GetOverrideSpell) and the curated alias map (MDB.SpellAliases, emitted
-- by the companion's generator). Readiness and binding must therefore match
-- ANY of them; the slot encodes whichever id the player actually knows.
--
-- Every API call is pcall-contained and every return scrubbed: a secret or
-- failed probe simply contributes nothing (never a compare, never a throw).
local function KnownSpell (SpellID)
  -- true / false / nil (UNKNOWN when neither API answers). Callers fail open
  -- on nil: an unobservable id is treated as eligible, never as absent.
  if type(SpellID) ~= "number" or SpellID <= 0 then return false; end
  if _G.C_SpellBook and type(_G.C_SpellBook.IsSpellKnown) == "function" then
    local Ok, Known = pcall(_G.C_SpellBook.IsSpellKnown, SpellID);
    if Ok and Known ~= nil then
      local Clean = Scrubbed(Known);
      if Clean == true then return true; end
      if Clean == false then return false; end
    end
  end
  if type(IsPlayerSpell) == "function" then
    local Ok, Known = pcall(IsPlayerSpell, SpellID);
    if Ok and Known ~= nil then
      local Clean = Scrubbed(Known);
      if Clean == true then return true; end
      if Clean == false then return false; end
    end
  end
  return nil;
end

--[[*
  * @function MDB.SpellVariants
  * @desc Every plain-number id this ability may be known under, de-duplicated
  *       (the input first, then base / override / alias expansions).
  * @param SpellID number
  * @return table array of ids (empty for a non-positive input)
  *]]
function MDB.SpellVariants (SpellID)
  if type(SpellID) ~= "number" or SpellID <= 0 then return {}; end
  local Aliases = MDB.SpellAliases;
  local Out, Seen, Queue = {}, {}, { SpellID };
  local Head = 1;
  local function Add (Id)
    if type(Id) ~= "number" or Id <= 0 or Seen[Id] then return false; end
    Seen[Id] = true;
    Out[#Out + 1] = Id;
    Queue[#Queue + 1] = Id;
    return true;
  end
  local function Probe (Fn, Id)
    if type(Fn) ~= "function" then return nil; end
    local Ok, Value = pcall(Fn, Id);
    if not Ok then return nil; end
    local Clean = Scrubbed(Value);
    if type(Clean) == "number" and Clean > 0 then return Clean; end
    return nil;
  end
  Add(SpellID);
  local BaseFn = _G.FindBaseSpellByID;
  local OverFn = _G.FindSpellOverrideByID;
  local GetOvFn = _G.GetOverrideSpell;
  while Head <= #Queue do
    local Id = Queue[Head];
    Head = Head + 1;
    if type(Aliases) == "table" and type(Aliases[Id]) == "table" then
      local List = Aliases[Id];
      for i = 1, #List do Add(List[i]); end
    end
    Add(Probe(BaseFn, Id));
    Add(Probe(OverFn, Id));
    Add(Probe(GetOvFn, Id));
  end
  return Out;
end

-- The variant the player actually knows (first known in SpellVariants order).
-- No known variant / unknown API -> the listed id (fail open; the caller
-- still gates on readiness + binding).
function MDB.ActiveVariant (SpellID)
  local Variants = MDB.SpellVariants(SpellID);
  for i = 1, #Variants do
    if KnownSpell(Variants[i]) == true then return Variants[i]; end
  end
  return SpellID;
end

--[[*
  * @function MDB.IsSpellKnownVariant
  * @desc true/false/nil for diagnostics (nil = the client API is absent).
  *]]
function MDB.IsSpellKnownVariant (SpellID)
  return KnownSpell(SpellID);
end

--- ======= BINDING RESOLUTION =======

-- SpellFrame.lua shortens raw bindings for display (SHIFT- -> S-,
-- NUMPAD -> N, BUTTON4 -> MB4, ...). HotKey/bindText reads come back
-- shortened, so expand back to the raw form ParseBinding expects.
function MDB.ExpandHotKey (Text)
  if type(Text) ~= "string" or Text == "" then return nil; end
  if string.byte(Text) == 226 then return nil; end  -- range glyph etc.
  local Key = strupper(strtrim(Text));

  -- Split trailing modifiers so short key names can be expanded behind them
  -- ("S-N1" -> SHIFT- + NUMPAD1). Done with plain strsub in a loop:
  -- Lua 5.1 patterns have no (?:...) non-capturing groups.
  local Prefix = "";
  local Stripped = true;
  while Stripped do
    Stripped = false;
    if strsub(Key, 1, 7) == "SHIFT-" then
      Prefix = Prefix .. "SHIFT-"; Key = strsub(Key, 8); Stripped = true;
    elseif strsub(Key, 1, 5) == "CTRL-" then
      Prefix = Prefix .. "CTRL-"; Key = strsub(Key, 6); Stripped = true;
    elseif strsub(Key, 1, 4) == "ALT-" then
      Prefix = Prefix .. "ALT-"; Key = strsub(Key, 5); Stripped = true;
    elseif strsub(Key, 1, 2) == "S-" then
      Prefix = Prefix .. "SHIFT-"; Key = strsub(Key, 3); Stripped = true;
    elseif strsub(Key, 1, 2) == "C-" then
      Prefix = Prefix .. "CTRL-"; Key = strsub(Key, 3); Stripped = true;
    elseif strsub(Key, 1, 2) == "A-" then
      Prefix = Prefix .. "ALT-"; Key = strsub(Key, 3); Stripped = true;
    end
  end
  local Base = Key;
  -- ElvUI renders modifiers dash-less ("CQ" = CTRL-Q, "S3" = SHIFT-3,
  -- "CSF" = CTRL-SHIFT-F). Without a dash the loop above strips nothing,
  -- so expand a leading S/C/A run when the tail is a valid key. The
  -- trial-parse guard means plain keys ("C", "A", "F"...) and unknown
  -- tails never misparse: if the expansion is not a real binding the
  -- text falls through to the normal path untouched.
  if Prefix == "" and not strfind(Key, "-") and strlen(Key) >= 2 then
    local Cut = 0;
    while Cut < strlen(Key) do
      local Ch = strsub(Key, Cut + 1, Cut + 1);
      if Ch ~= "S" and Ch ~= "C" and Ch ~= "A" then break; end
      Cut = Cut + 1;
    end
    if Cut >= 1 and Cut < strlen(Key) then
      local Expanded = "";
      for i = 1, Cut do
        local Ch = strsub(Key, i, i);
        if Ch == "S" then Expanded = Expanded .. "SHIFT-";
        elseif Ch == "C" then Expanded = Expanded .. "CTRL-";
        else Expanded = Expanded .. "ALT-"; end
      end
      local Trial = Expanded .. strsub(Key, Cut + 1);
      if MDB.ParseBinding(Trial) then return Trial; end
    end
  end
  -- Expand ShortenKeybind tokens back to raw. Raw full names (BUTTON4,
  -- MOUSEWHEELUP, NUMPADPLUS, MIDDLE MOUSE, ...) pass through untouched.
  -- Only exact short tokens are rewritten, so substrings inside longer
  -- names can never collide.
  Base = Base:gsub("MIDDLE MOUSE", "BUTTON3");
  Base = Base:gsub("LMB", "BUTTON1");
  Base = Base:gsub("RMB", "BUTTON2");
  Base = Base:gsub("MB3", "BUTTON3");
  Base = Base:gsub("MB4", "BUTTON4");
  Base = Base:gsub("MB5", "BUTTON5");
  Base = Base:gsub("MWU", "MOUSEWHEELUP");
  Base = Base:gsub("MWD", "MOUSEWHEELDOWN");

  if Base == "M3" then
    Base = "BUTTON3";
  elseif Base == "N+" then
    Base = "NUMPADPLUS";
  elseif Base == "N-" then
    Base = "NUMPADMINUS";
  elseif Base == "N*" then
    Base = "NUMPADMULTIPLY";
  elseif Base == "N/" then
    Base = "NUMPADDIVIDE";
  elseif Base == "NDECIMAL" or Base == "N." then
    Base = "NUMPADDECIMAL";
  elseif strmatch(Base, "^N[0-9]$") then
    Base = "NUMPAD" .. strsub(Base, 2, 2);
  elseif Base == "+" then
    -- SpellFrame collapses NUMPADPLUS to "+" and bare PLUS to "+";
    -- NUMPADPLUS is the common case (Keymap has no bare-PLUS entry),
    -- so map "+" there.
    Base = "NUMPADPLUS";
  elseif Base == "-" then
    Base = "NUMPADMINUS";
  end

  return Prefix .. Base;
end

-- v1.3.2 BINDING FIX (Sep-2026: Shift+E never fired, 1/2/R stuck):
-- MaxDps's overlay HotKey text is the AUTHORITATIVE binding — it is what
-- the user sees light up on the bars (the user's own words). It was
-- ALREADY path #1, but three defects demoted it in practice:
-- (a) HotKey texts arrive tainted (FontString under taint): string ops on
--     them are legal (strings never throw on compare — only secrets do),
--     but GetText under taint can return secret-wrapped strings, so the
--     whole read runs in dropsecretaccess() containment: provably plain.
-- (b) SHIFT-/CTRL-/ALT- PREFIXES WERE DROPPED: the old code called
--     ExpandHotKey(Text) → "SHIFT-E", then ParseBinding — correct — BUT
--     when HotKey text was missing/empty it fell to FindSpellOnActionBar
--     → GetBindingKey, which returns only the FIRST binding and SILENTLY
--     DROPS modifiers on some bar addons. Worse: ElvUI dash-less "SE"
--     (Shift+E) hit the dash-less expander, which requires a trial-parse
--     hit — and Keymap.ParseBinding("SHIFT-E") was fine, so "SE" became
--     "SHIFT-E" ONLY if the trial passed; on failure it fell through as
--     literal "SE" → ParseBinding("SE") → VK nil → NO BINDING → EMPTY.
--     Fix: try the dash-less expansion FIRST when the text has no dash,
--     and accept the trial ONLY on parse hit (unchanged), but LOG the
--     miss by falling through to path #2 instead of returning nil.
-- (c) The overlay bindText (MaxDpsSpellFrame.bindText, main spell only)
--     was path #3 — BEHIND the flaky action-bar scan. It is the SAME
--     overlay the user watches, so for the MAIN slot it now runs SECOND,
--     before the scan: overlay HotKey → overlay bindText → bar scan →
--     texture map. No visual reading involved — all four are MaxDps's own
--     transported keybind data, exactly as the user says.
local function HotKeyBinding (SpellID)
  local MaxDps = MaxDpsEngine();
  if not MaxDps or not MaxDps.Spells then return nil; end
  -- v3: the MaxDps overlay may carry the ability under any variant id (a
  -- base spell / talent override / curated alias), so scan every variant.
  local Variants = MDB.SpellVariants(SpellID);
  local OkRead, Result = pcall(function ()
    if type(dropsecretaccess) == "function" then dropsecretaccess(); end
    for v = 1, #Variants do
      local Buttons = MaxDps.Spells[Variants[v]];
      if Buttons then
        for i = 1, #Buttons do
          local Button = Buttons[i];
          local HotKey = Button and Button.HotKey;
          if not HotKey and Button and Button.GetName then
            local Name = Button:GetName();
            if Name then HotKey = _G[Name .. "HotKey"]; end
          end
          if HotKey and HotKey.GetText then
            local Text = HotKey:GetText();
            if type(Text) == "string" and Text ~= "" and string.byte(Text) ~= 226 then
              local VirtualKey, Modifiers = MDB.ParseBinding(MDB.ExpandHotKey(Text));
              if VirtualKey then return { VK = VirtualKey, Mods = Modifiers }; end
              -- Parse miss (e.g. exotic ElvUI token): keep scanning buttons
              -- instead of aborting — a later button may carry plain text.
            end
          end
        end
      end
    end
    return nil;
  end);
  if OkRead and Result then return Result.VK, Result.Mods; end
  return nil;
end

-- Mirrors SpellFrame.lua FindSpellOnActionBar (slots 1-180, id or name match).
-- v1.3.0 zero-taint: dropsecretaccess() containment for the whole scan —
-- every GetActionInfo ID / GetSpellName result inside is provably plain,
-- so plain `==` below cannot throw. Without containment, secret slot IDs
-- detonate on compare (Sep-2026 taint outage). SpellID arrives from
-- scrubbed scans (already plain); re-check cheaply (pure Lua, our data).
local function FindSpellOnActionBar (SpellID)
  if type(SpellID) ~= "number" or SpellID == 0 then return nil; end
  local Variants = MDB.SpellVariants(SpellID);
  local VariantSet = {};
  for i = 1, #Variants do VariantSet[Variants[i]] = true; end
  local OkScan, Found = pcall(function ()
    if type(dropsecretaccess) == "function" then dropsecretaccess(); end
    -- Name set is built from the variants so a renamed/base ability still
    -- matches its live-client name. All inside dropsecretaccess containment.
    local NameSet = nil;
    if C_Spell and C_Spell.GetSpellName then
      NameSet = {};
      for i = 1, #Variants do
        local Name = C_Spell.GetSpellName(Variants[i]);
        if type(Name) == "string" then NameSet[Name] = true; end
      end
    end
    local function Matches (ID)
      if type(ID) ~= "number" then return false; end
      if VariantSet[ID] then return true; end;
      if NameSet and C_Spell and C_Spell.GetSpellName then
        local Name = C_Spell.GetSpellName(ID);
        if type(Name) == "string" and NameSet[Name] then return true; end
      end
      return false;
    end
    for Slot = 1, 180 do
      local ActionType, ID = GetActionInfo(Slot);
      if ActionType == "spell" then
        if Matches(ID) then return Slot; end
      elseif ActionType == "macro" and type(ID) == "number" and type(GetMacroSpell) == "function" then
        -- A /cast macro still counts: GetMacroSpell resolves the macro to the
        -- spell it casts (any variant), so a macro'd ability is bound.
        local _, _, MacroSpell = GetMacroSpell(ID);
        if Matches(MacroSpell) then return Slot; end
      end
    end
    return nil;
  end);
  if OkScan then return Found; end
  return nil;
end

-- Mirrors SpellFrame.lua GetKeybindForSpell: slot -> binding command.
local function GetKeybindForSpell (SpellID)
  local Slot = FindSpellOnActionBar(SpellID);
  if not Slot then return nil; end
  local Button = ((Slot - 1) % 12) + 1;
  local Command = nil;
  if Slot <= 12 then
    Command = "ACTIONBUTTON" .. Button;
  elseif Slot <= 24 then
    Command = "MULTIACTIONBAR1BUTTON" .. Button;
  elseif Slot <= 36 then
    Command = "MULTIACTIONBAR3BUTTON" .. Button;
  elseif Slot <= 48 then
    Command = "MULTIACTIONBAR4BUTTON" .. Button;
  elseif Slot <= 60 then
    Command = "MULTIACTIONBAR2BUTTON" .. Button;
  elseif Slot <= 72 then
    Command = "MULTIACTIONBAR1BUTTON" .. Button;
  elseif Slot <= 156 then
    Command = "MULTIACTIONBAR5BUTTON" .. Button;
  elseif Slot <= 168 then
    Command = "MULTIACTIONBAR6BUTTON" .. Button;
  elseif Slot <= 180 then
    Command = "MULTIACTIONBAR7BUTTON" .. Button;
  end
  if not Command then return nil; end
  return GetBindingKey(Command);
end

local function SpellFrameBinding (SpellID)
  local Frame = _G.MaxDpsSpellFrame;
  if not Frame or not Frame.IsVisible or not Frame:IsVisible() then return nil; end
  -- Both sides arrive from scrubbed scans (proven plain); pure-Lua compare.
  if type(SpellID) ~= "number" then return nil; end
  local Main = MDB.GetMainSpellID();
  local Variants = MDB.SpellVariants(SpellID);
  local IsMain = false;
  for i = 1, #Variants do
    if Variants[i] == Main then IsMain = true; break; end
  end
  if not IsMain then return nil; end
  if not Frame.bindText or not Frame.bindText.GetText then return nil; end
  local Ok, Text = pcall(Frame.bindText.GetText, Frame.bindText);
  if not Ok then return nil; end
  return MDB.ParseBinding(MDB.ExpandHotKey(Text));
end

local function TextureBinding (SpellID)
  -- SpellID arrives from scrubbed scans (proven plain, pure-Lua check).
  if type(SpellID) ~= "number" or SpellID == 0 then return nil; end
  -- v3: the mapped texture may belong to any variant of the ability.
  local Variants = MDB.SpellVariants(SpellID);
  for i = 1, #Variants do
    local Id = Variants[i];
    local Texture = nil;
    if C_Spell and C_Spell.GetSpellTexture then
      local Ok, Tex = pcall(C_Spell.GetSpellTexture, Id);
      if Ok then Texture = Tex; end
    end
    if not Texture and type(GetSpellTexture) == "function" then
      local Ok, Tex = pcall(GetSpellTexture, Id);
      if Ok then Texture = Tex; end
    end
    if Texture then
      local VirtualKey, Modifiers = MDB.ParseBinding(MDB.BindingForTexture(Texture));
      if VirtualKey then return VirtualKey, Modifiers; end
    end
  end
  return nil;
end

-- v1.3.2 order (user's words: the overlay IS the binding — no visual
-- reading needed, MaxDps transports the keybinds itself): overlay HotKey
-- (what lights up on the bars) → overlay bindText (main-slot SpellFrame
-- text, same overlay) → action-bar scan → texture map. The scan moved
-- AFTER the overlay paths because GetBindingKey drops modifiers on some
-- bar addons (the Shift+E → E-class failure mode).
local function ResolveBindingUncached (SpellID)
  local VirtualKey, Modifiers = HotKeyBinding(SpellID);
  if VirtualKey then return VirtualKey, Modifiers; end

  VirtualKey, Modifiers = SpellFrameBinding(SpellID);
  if VirtualKey then return VirtualKey, Modifiers; end

  VirtualKey, Modifiers = MDB.ParseBinding(GetKeybindForSpell(SpellID));
  if VirtualKey then return VirtualKey, Modifiers; end

  return TextureBinding(SpellID);
end

-- Results are cached per spellID; the cache is wiped by bar updates
-- (via MDB.InvalidateBindings) and by MaxDps:Fetch/category hooks.
-- SpellIDs arrive from scrubbed scans (proven plain); keys are our own
-- plain data. Pure-Lua type check only.
function MDB.ResolveBinding (SpellID)
  if type(SpellID) ~= "number" or SpellID == 0 then return nil, 0; end
  MDB._BindCache = MDB._BindCache or {};
  local Cached = MDB._BindCache[SpellID];
  if Cached then
    if Cached.Miss then return nil, 0; end
    return Cached.VK, Cached.Mods;
  end
  local VirtualKey, Modifiers = ResolveBindingUncached(SpellID);
  if VirtualKey then
    MDB._BindCache[SpellID] = { VK = VirtualKey, Mods = Modifiers or 0 };
  else
    MDB._BindCache[SpellID] = { Miss = true };
  end
  return VirtualKey, Modifiers or 0;
end

--- ======= v2.0 SENSORS (protocol v5) =======
-- Everything below is SECRET-SAFE by construction. The rules (see the header
-- of this file and HANDOVER):
--   * cast state comes from RegisterUnitEvent events on dedicated frames:
--     the unit filter is applied by the game, no event argument is ever read,
--     and the handlers only flip plain booleans. This is the only way to
--     observe a cast without touching tainted UnitCastingInfo returns.
--   * HP / range / buff probes wrap every Blizzard call in pcall and run
--     every returned value through Scrubbed() (secret -> nil = UNKNOWN).
--     Arithmetic happens only on values that survived the scrub.
--   * nothing here is allowed to throw: a failed probe degrades to UNKNOWN
--     and the companion's policy treats UNKNOWN with its documented fallback.

-- (Sensor state is declared at the top of the file: IsInterruptReady reads
-- TargetCastInterruptible and must see the same local — see the comment there.)

-- Resets the tracked cast state (PLAYER_ENTERING_WORLD / reload). Events can
-- be missed across a loading screen, and a cast cannot span one.
function MDB.ResetSensors ()
  PlayerCasting, PlayerChanneling = false, false;
  TargetCasting, TargetCastInterruptible = false, nil;
  PlayerCastSince, TargetCastSince = nil, nil;
end

-- Plain GetTime stamp for the watchdog (pcall-contained; nil when missing).
local function CastTimer ()
  if type(GetTime) ~= "function" then return nil; end
  local Ok, Now = pcall(GetTime);
  if Ok and type(Now) == "number" then return Now; end
  return nil;
end

-- True when a latch has been held longer than any legitimate cast/channel:
-- the STOP event was missed, so the state is UNKNOWN, not "still casting".
local CAST_WATCHDOG_SECONDS = 15;
local function CastStale (Since)
  if type(Since) ~= "number" or type(GetTime) ~= "function" then return false; end
  local Ok, Now = pcall(GetTime);
  if not Ok or type(Now) ~= "number" then return false; end
  return Now - Since > CAST_WATCHDOG_SECONDS;
end

-- Registers arg-blind unit events for one unit. Two separate frames (player,
-- target) so the handler never has to read the unit token argument.
local function CreateSensor (Unit, Handler)
  if type(CreateFrame) ~= "function" then return nil; end
  local Frame = CreateFrame("Frame");
  if type(Frame) ~= "table" or type(Frame.RegisterUnitEvent) ~= "function" then
    return nil;
  end
  local Ok = pcall(function ()
    Frame:RegisterUnitEvent("UNIT_SPELLCAST_START", Unit);
    Frame:RegisterUnitEvent("UNIT_SPELLCAST_STOP", Unit);
    Frame:RegisterUnitEvent("UNIT_SPELLCAST_FAILED", Unit);
    Frame:RegisterUnitEvent("UNIT_SPELLCAST_INTERRUPTED", Unit);
    Frame:RegisterUnitEvent("UNIT_SPELLCAST_CHANNEL_START", Unit);
    Frame:RegisterUnitEvent("UNIT_SPELLCAST_CHANNEL_STOP", Unit);
  end);
  if not Ok then return nil; end
  Frame:SetScript("OnEvent", function (_, Event)
    -- Event names are plain strings; no event payload is ever inspected.
    Handler(Event);
  end);
  return Frame;
end

-- Event frame that gates the melee probe (v3.6). #nocombat APIs are blocked
-- from the moment the client enters combat lockdown until 0.5 s after it
-- leaves, and a hidden breaker (ADDON_ACTION_BLOCKED) can fire even outside
-- the lockdown window. This frame tracks all three signals on plain booleans
-- and disables the probe for the session after two blocked calls. Idempotent:
-- reuses the frame if one already exists (MDB._ProbeEvents).
local function ClearMeleeCache ()
  MeleeCache.val, MeleeCache.guid, MeleeCache.at = nil, nil, 0;
end

local function EnsureProbeEvents ()
  if MDB._ProbeEvents then return MDB._ProbeEvents; end
  if type(CreateFrame) ~= "function" then return nil; end
  local Frame = CreateFrame("Frame");
  if type(Frame) ~= "table" or type(Frame.RegisterEvent) ~= "function" then
    return nil;
  end
  local Ok = pcall(function ()
    Frame:RegisterEvent("PLAYER_REGEN_DISABLED");
    Frame:RegisterEvent("PLAYER_REGEN_ENABLED");
    Frame:RegisterEvent("ENCOUNTER_START");
    Frame:RegisterEvent("ENCOUNTER_END");
    Frame:RegisterEvent("PLAYER_TARGET_CHANGED");
    Frame:RegisterEvent("PLAYER_ENTERING_WORLD");
    Frame:RegisterEvent("ADDON_ACTION_BLOCKED");
  end);
  if not Ok then return nil; end
  Frame:SetScript("OnEvent", function (_, Event, Arg1, Arg2)
    if Event == "PLAYER_REGEN_DISABLED" or Event == "ENCOUNTER_START" then
      InCombatEv = true;
      ClearMeleeCache();
    elseif Event == "PLAYER_REGEN_ENABLED" or Event == "ENCOUNTER_END" then
      InCombatEv = false;
      local T = SafeNow();
      if BlockHits < 2 and T then SafeAfter = T + 0.5; end
    elseif Event == "PLAYER_TARGET_CHANGED" then
      ClearMeleeCache();
    elseif Event == "PLAYER_ENTERING_WORLD" then
      local Lock = false;
      if type(InCombatLockdown) == "function" then
        local OkL, L = pcall(InCombatLockdown);
        Lock = (OkL and L == true);
      end
      InCombatEv = Lock;
      local T = SafeNow();
      if T then SafeAfter = T + 0.5; end
    elseif Event == "ADDON_ACTION_BLOCKED" then
      if Arg1 == addonName and type(Arg2) == "string"
         and string.find(Arg2, "CheckInteractDistance", 1, true) then
        BlockHits = BlockHits + 1;
        ClearMeleeCache();
        if BlockHits >= 2 then
          SafeAfter = math.huge;   -- 2nd hit: never probe again this session
        else
          local T = SafeNow();
          if T then SafeAfter = T + 5; end;   -- 1st hit: finite 5 s backoff
        end;
        if BlockHits == 1 then
          pcall(DiagPrint,
            "CheckInteractDistance blocked by the client; melee range is now UNKNOWN.");
        end
      end
    end
  end);
  MDB._ProbeEvents = Frame;
  return Frame;
end

function MDB.InitSensors ()
  if SensorEvents then return; end
  SensorEvents = true;

  local PlayerFrame = CreateSensor("player", function (Event)
    if Event == "UNIT_SPELLCAST_START" then
      PlayerCasting = true; PlayerChanneling = false; PlayerCastSince = CastTimer();
    elseif Event == "UNIT_SPELLCAST_STOP"
      or Event == "UNIT_SPELLCAST_FAILED"
      or Event == "UNIT_SPELLCAST_INTERRUPTED" then
      PlayerCasting = false; PlayerCastSince = nil;
    elseif Event == "UNIT_SPELLCAST_CHANNEL_START" then
      PlayerChanneling = true; PlayerCasting = false; PlayerCastSince = CastTimer();
    elseif Event == "UNIT_SPELLCAST_CHANNEL_STOP" then
      PlayerChanneling = false; PlayerCastSince = nil;
    end
  end);
  if PlayerFrame then
    -- The frame stays alive as long as the closure is referenced; the
    -- PLAYER_ENTERING_WORLD reset is driven from Bridge.lua's loader.
  end

  local TargetFrame = CreateSensor("target", function (Event)
    if Event == "UNIT_SPELLCAST_START" or Event == "UNIT_SPELLCAST_CHANNEL_START" then
      TargetCasting = true; TargetCastInterruptible = nil; TargetCastSince = CastTimer();
    elseif Event == "UNIT_SPELLCAST_INTERRUPTIBLE" then
      TargetCastInterruptible = true;
    elseif Event == "UNIT_SPELLCAST_NOT_INTERRUPTIBLE" then
      TargetCastInterruptible = false;
    elseif Event == "UNIT_SPELLCAST_STOP"
      or Event == "UNIT_SPELLCAST_FAILED"
      or Event == "UNIT_SPELLCAST_INTERRUPTED"
      or Event == "UNIT_SPELLCAST_CHANNEL_STOP"    -- v2.1: a target channel
      then                                          -- ending must clear too
      TargetCasting = false; TargetCastInterruptible = nil; TargetCastSince = nil;
    elseif Event == "PLAYER_TARGET_CHANGED" then
      -- v2.1: the tracked cast belonged to the PREVIOUS target; without this
      -- a target switch mid-cast leaves a stale "casting" bit that would gate
      -- Spell Reflection onto a target that is not casting anything.
      TargetCasting = false; TargetCastInterruptible = nil; TargetCastSince = nil;
    end
  end);
  if TargetFrame then
    pcall(function ()
      TargetFrame:RegisterUnitEvent("UNIT_SPELLCAST_INTERRUPTIBLE", "target");
      TargetFrame:RegisterUnitEvent("UNIT_SPELLCAST_NOT_INTERRUPTIBLE", "target");
      TargetFrame:RegisterEvent("PLAYER_TARGET_CHANGED");
    end);
  end

  -- v3.6: the melee-probe event frame (regen/encounter/target/world/BLOCKED).
  EnsureProbeEvents();

  if not PlayerFrame and not TargetFrame then
    SensorEvents = false;   -- pre-Midnight client: stay UNKNOWN forever
  end
end

--- Player health percent, or nil = UNKNOWN.
local function HealthPct (Unit)
  if type(UnitHealth) ~= "function" or type(UnitHealthMax) ~= "function" then
    return nil;
  end
  local OkHp, Hp = pcall(UnitHealth, Unit);
  if not OkHp then return nil; end
  local OkMax, Max = pcall(UnitHealthMax, Unit);
  if not OkMax then return nil; end
  local PlainHp = nil;
  local CleanHp = Scrubbed(Hp);
  if type(CleanHp) == "number" then PlainHp = CleanHp; end
  local PlainMax = nil;
  local CleanMax = Scrubbed(Max);
  if type(CleanMax) == "number" then PlainMax = CleanMax; end
  if not PlainHp or not PlainMax or PlainMax <= 0 then return nil; end
  local Pct = math.floor(PlainHp * 100 / PlainMax + 0.5);
  if Pct < 0 then Pct = 0 elseif Pct > 100 then Pct = 100; end
  return Pct;
end

function MDB.GetPlayerHpPct ()
  return HealthPct("player");
end

function MDB.GetTargetHpPct ()
  return HealthPct("target");
end

function MDB.GetCastState ()
  -- 0 none, 1 casting, 2 channeling, 15 unknown
  if not SensorEvents then return 15; end
  if (PlayerCasting or PlayerChanneling) and CastStale(PlayerCastSince) then
    -- A missed STOP must not latch "casting" forever (the companion would
    -- hold the whole rotation behind a dead bit). Degrade to UNKNOWN: the
    -- policy fails open and the main rotation resumes.
    PlayerCasting, PlayerChanneling, PlayerCastSince = false, false, nil;
    return 15;
  end
  if PlayerCasting then return 1; end
  if PlayerChanneling then return 2; end
  return 0;
end

-- v3.6: the ONLY CheckInteractDistance call site in the addon. The API is
-- #nocombat-restricted in 12.x and can raise ADDON_ACTION_BLOCKED, so it is
-- gated twice: ProbeSafe rejects event/lockdown/UnitAffectingCombat blocks and
-- the 0.5 s post-combat cool-off, and it is disabled for the session after two
-- ADDON_ACTION_BLOCKED hits. One probe is shared by GetTargetContext and
-- Bridge.TargetState so the call rate does not multiply. Results are cached
-- per target GUID for 0.25 s. pcall stays belt-and-braces: a throwing/secret
-- API fails to nil => UNKNOWN.
-- Returns 1 in melee, 0 confirmed out of melee, nil unknown/not probed.
local function ProbeSafe ()
  if InCombatEv then return false; end
  if BlockHits >= 2 then return false; end
  -- Intentional: a client that does not expose InCombatLockdown is NOT
  -- refused outright (plan 2026-10-03). InCombatEv (regen/encounter) and
  -- UnitAffectingCombat still gate it, and ADDON_ACTION_BLOCKED is the
  -- breaker, so an absent API can never un-gate an ongoing combat state.
  if type(InCombatLockdown) == "function" then
    local OkL, Locked = pcall(InCombatLockdown);
    if not OkL or Locked == true then return false; end
  end
  if type(UnitAffectingCombat) == "function" then
    local OkC, InC = pcall(UnitAffectingCombat, "player");
    if OkC and Scrubbed(InC) == true then return false; end
  end
  local Now = SafeNow();
  if Now and Now < SafeAfter then return false; end
  return true;
end

local function TargetGuid ()
  if type(UnitGUID) ~= "function" then return nil; end
  local Ok, Guid = pcall(UnitGUID, "target");
  if not Ok then return nil; end
  -- Secret GUIDs still report type()=="string"; scrubbing turns them into
  -- nil so the cache compare (`MeleeCache.guid == Guid`) can never throw.
  Guid = Scrubbed(Guid);
  if type(Guid) == "string" then return Guid; end
  return nil;
end

function MDB.ProbeTargetMelee ()
  if not ProbeSafe() then return nil; end;   -- no API touch, no cache read
  local Now = SafeNow();
  local Guid = TargetGuid();
  if Guid and MeleeCache.guid == Guid and Now and (Now - MeleeCache.at) < 0.25 then
    return MeleeCache.val;   -- same target, still fresh: one call per 0.25 s
  end
  if type(CheckInteractDistance) ~= "function" then return nil; end
  local Val = nil;
  local Ok, Near = pcall(CheckInteractDistance, "target", 3);
  if Ok then
    -- Scrub first, then map ONLY the explicit booleans: a secret/failed probe
    -- must stay UNKNOWN (2), never become a confirmed out-of-melee (0) that
    -- would let a gap closer fire blind.
    Near = Scrubbed(Near);
    if Near == true then Val = 1
    elseif Near == false then Val = 0 end;
  end
  MeleeCache.val, MeleeCache.guid, MeleeCache.at = Val, Guid, Now or 0;
  return Val;
end

-- Returns meleeFlag (0 = confirmed out of melee, 1 = in melee,
-- 2 = unknown), target hp band (0..14 = 0..~100% in steps, 15 unknown) and
-- target cast flags:
--   bit0 casting, bit1 cast state unknown, bit2 interruptible,
--   bit3 not interruptible.
function MDB.GetTargetContext ()
  -- Target presence first: without a target the cast latch is meaningless,
  -- and a stale "casting" bit from the previous target must not gate reflect
  -- or kick decisions onto nothing.
  local HasTarget = true;
  if type(UnitExists) == "function" then
    local OkE, Exists = pcall(UnitExists, "target");
    if OkE and Exists ~= true then HasTarget = false; end
  end
  if not HasTarget then
    TargetCasting, TargetCastInterruptible, TargetCastSince = false, nil, nil;
  end

  local Melee = 2;   -- unknown unless the shared event-gated probe answers
  if HasTarget then
    local Probe = MDB.ProbeTargetMelee();
    if Probe ~= nil then Melee = Probe; end
  end

  local HpBand = 15;
  local Pct = HealthPct("target");
  if Pct then
    HpBand = math.floor(Pct * 15 / 100);
    if HpBand > 14 then HpBand = 14; end
  end

  local CastFlags = 0;
  if SensorEvents then
    if TargetCasting and CastStale(TargetCastSince) then
      -- Watchdog: the STOP was missed; report UNKNOWN, not "casting".
      TargetCasting, TargetCastInterruptible, TargetCastSince = false, nil, nil;
      CastFlags = bit.bor(CastFlags, 2);
    elseif TargetCasting then
      CastFlags = bit.bor(CastFlags, 1);
      if TargetCastInterruptible == true then
        CastFlags = bit.bor(CastFlags, 4);
      elseif TargetCastInterruptible == false then
        CastFlags = bit.bor(CastFlags, 8);
      else
        CastFlags = bit.bor(CastFlags, 2);
      end
    else
      CastFlags = bit.bor(CastFlags, 2);   -- not casting: state UNKNOWN
    end
  else
    CastFlags = bit.bor(CastFlags, 2);
  end
  return Melee, HpBand, CastFlags;
end

-- Per-slot range probe: 0 unknown, 1 in range, 2 out of range.
-- Uses MaxDps's own helper (the same call upstream uses) through a pcall;
-- a secret return scrubs to nil and stays UNKNOWN.
function MDB.GetSlotRange (SpellID)
  if type(SpellID) ~= "number" or SpellID == 0 then return 0; end
  local MaxDps = MaxDpsEngine();
  if not MaxDps or type(MaxDps.IsSpellInRange) ~= "function" then return 0; end
  if type(UnitExists) ~= "function" then return 0; end
  local OkExists, HasTarget = pcall(function ()
    return UnitExists("target") == true;
  end);
  if not OkExists or not HasTarget then return 0; end
  local Ok, InRange = pcall(MaxDps.IsSpellInRange, MaxDps, SpellID, "target");
  if not Ok then return 0; end
  local Clean = Scrubbed(InRange);
  if Clean == 1 or Clean == true then return 1; end
  if Clean == 0 or Clean == false then return 2; end
  return 0;
end

-- Per-slot self-buff probe: 1 when the suggested spell's own helpful aura is
-- on the player, 0 when the probe ran and found none, nil when the probe
-- failed / degraded (no AuraUtil, thrown call, secret value). The bridge marks
-- the whole block invalid when any probe returns nil (v2.7 cell 33 B bit1), so
-- the companion sees UNKNOWN instead of a silent "not active"; its policy then
-- keeps the documented fail-open for Unknown (legacy encoders leave the bit 0
-- and behave exactly as before).
function MDB.GetSlotBuff (SpellID)
  if type(SpellID) ~= "number" or SpellID == 0 then return nil; end
  if not (_G.AuraUtil and type(_G.AuraUtil.FindAuraBySpellID) == "function") then
    return nil;
  end
  local Ok, Found = pcall(function ()
    local Aura = _G.AuraUtil.FindAuraBySpellID(SpellID, "player", "HELPFUL");
    return Aura ~= nil;
  end);
  if not Ok then return nil; end
  if Found == true then return 1; end
  if Found == false then return 0; end
  return nil;
end

-- Class id + spec ordinal for protocol v5 cell 33 (see Catalog.lua).
function MDB.GetClassSpec ()
  local _, classFile, specName = ClassSpec();
  if not classFile or not specName then return 0, 0; end
  local ClassId = 0;
  if type(MDB.ClassIds) == "table" and type(MDB.ClassIds[classFile]) == "number" then
    ClassId = MDB.ClassIds[classFile];
  end
  local SpecId = 0;
  if ClassId > 0
    and type(MDB.SpecIds) == "table"
    and type(MDB.SpecIds[classFile]) == "table"
    and type(MDB.SpecIds[classFile][specName]) == "number" then
    SpecId = MDB.SpecIds[classFile][specName];
  end
  return ClassId, SpecId;
end

-- The curated companion-only slot candidates (Catalog.lua). First entry whose
-- spell is currently ready AND has a resolvable keybind wins; the policy then
-- decides USE/HOLD.
--
-- v2.2: the binding check matters when a list holds more than one curated
-- ability (e.g. Warrior self-heal IV -> Enraged Regeneration): a ready but
-- UNBOUND talent (not on any bar, no MaxDps overlay button) must not shadow a
-- bound alternative. ResolveBinding is cached (misses included) and
-- invalidated on bar updates, so the extra lookups are a table hit.
--[[*
  * @function MDB.ExtraCandidates
  * @desc First Count DISTINCT entries of a curated extras list that are
  *       ready AND bound, each encoded as the variant the player actually
  *       knows (base / override / alias; see MDB.ActiveVariant).
  * @param Kind string "mobility" | "selfHeal" | "defensive" | "cc"
  * @param Count number
  * @return table array of variant ids (may be shorter than Count)
  *]]
function MDB.ExtraCandidates (Kind, Count)
  Count = Count or 1;
  local _, classFile, specName = ClassSpec();
  if not classFile or not specName then return {}; end
  local Table = MDB.Extras and MDB.Extras[classFile] and MDB.Extras[classFile][specName];
  if type(Table) ~= "table" then return {}; end
  local List = Table[Kind];
  if type(List) ~= "table" then return {}; end
  local Out, Seen = {}, {};
  for i = 1, #List do
    local Entry = List[i];
    if type(Entry) == "number" and Entry > 0 then
      local Active = MDB.ActiveVariant(Entry);
      if type(Active) == "number" and Active > 0 and not Seen[Active] then
        -- Readiness/binding are tested on the variant that will be encoded.
        if MDB.IsSpellReady(Active) and MDB.ResolveBinding and MDB.ResolveBinding(Active) then
          Seen[Active] = true;
          Out[#Out + 1] = Active;
          if #Out >= Count then return Out; end
        end
      end
    end
  end
  return Out;
end

local function ExtraSpellID (Kind)
  local Candidates = MDB.ExtraCandidates(Kind, 1);
  return Candidates[1];
end

function MDB.GetMobilitySpellID ()
  return ExtraSpellID("mobility");
end

function MDB.GetSelfHealSpellID ()
  return ExtraSpellID("selfHeal");
end

-- The second distinct SelfHeal candidate (protocol Ext2 cells 36-38).
function MDB.GetSelfHeal2SpellID ()
  local Candidates = MDB.ExtraCandidates("selfHeal", 2);
  return Candidates[2];
end

-- ======= CROWD-CONTROL SLOT-6 REUSE (v3.4.0 Option A) =======
-- The Interrupt slot (wire 6) is reused as the CC candidate source: the bridge
-- prefers MaxDps's own flagged+live-cast interrupt (GetInterruptSpellID +
-- IsInterruptReady) and only falls through to this when MaxDps names no usable
-- interrupt. A CC row needs no live target cast, so it is offered through the
-- SAME ready+bound+ActiveVariant walk as the other curated extras (no
-- IsInterruptReady gate). The addon's own CC toggle (IsCC, restrict-only,
-- missing = ON) is consulted here; the companion's CrowdControlGate remains the
-- final authority on whether the candidate may actually fire. No wire change:
-- the CC id simply rides the existing slot-6 id cells, and the companion
-- recognises it by curated CC membership.
function MDB.GetCrowdControlCandidate ()
  if MDB.Toggles and MDB.Toggles.IsCC and not MDB.Toggles.IsCC() then return nil; end
  return ExtraSpellID("cc");
end

--- v3.5 CC fix: bosses are immune to the curated CC (stun/fear/root/…), so the
--- slot-6 CC pool is not offered against a worldboss / boss-level target.
--- UnitClassification returns a plain string and UnitLevel a plain number for
--- the target; both are pcall-contained and ANY failure/unknown fails OPEN
--- (not a boss → include the candidate).
function MDB.IsBossTarget ()
  if type(UnitClassification) == "function" then
    local OkC, Class = pcall(UnitClassification, "target");
    if OkC and Class == "worldboss" then return true; end
  end
  if type(UnitLevel) == "function" then
    local OkL, Level = pcall(UnitLevel, "target");
    if OkL and Level == -1 then return true; end
  end
  return false;
end

--- ======= MULTI-CANDIDATE ROTATION (v3.5, bridge 3.5.0) =======
-- The single "first ready+bound entry wins" selection let one held candidate
-- shadow every alternative (RC4: Charge > Heroic Leap). These helpers return
-- an ORDERED, de-duplicated pool (cap 4) for the four rotating slots so
-- Bridge.lua can cycle through it. A candidate must be ready, bound, known as
-- a variant, and NOT never-automatic; readiness is re-read every tick.
--
-- neverAutomatic is a curated deny-list of manual-only buttons the plan
-- forbids from ever firing automatically. It is matched against the spell and
-- every known variant of it, and fails open (unknown id = allowed).
local NEVER_AUTOMATIC = {
  [33786] = true,   -- Cyclone
  [118]   = true,   -- Polymorph
  [6770]  = true,   -- Sap
  [710]   = true,   -- Banish
  [217832] = true,  -- Imprison
  [73325] = true,   -- Leap of Faith
  [20484] = true,   -- Rebirth
};

function MDB.IsNeverAutomatic (SpellID)
  if type(SpellID) ~= "number" or SpellID <= 0 then return false; end
  if NEVER_AUTOMATIC[SpellID] then return true; end
  if MDB.SpellVariants then
    local Ok, Variants = pcall(MDB.SpellVariants, SpellID);
    if Ok and type(Variants) == "table" then
      for i = 1, #Variants do
        if NEVER_AUTOMATIC[Variants[i]] then return true; end
      end
    end
  end
  return false;
end

-- Walk one curated list into the pool: ready + bound + known variant +
-- not never-automatic, dedup by the ACTIVE variant id. Returns an array.
-- SkipBoss (v3.5, slot 6): when true, the whole walk is skipped if the
-- current target is a boss (immune to the curated CC); unknown fails open.
local function WalkCurated (List, Count, Seen, SkipBoss)
  local Out = {};
  if type(List) ~= "table" then return Out; end
  if SkipBoss and MDB.IsBossTarget and MDB.IsBossTarget() then return Out; end
  for i = 1, #List do
    local Entry = List[i];
    if type(Entry) == "number" and Entry > 0 then
      local Active = MDB.ActiveVariant(Entry);
      if type(Active) == "number" and Active > 0 and not Seen[Active]
        and not MDB.IsNeverAutomatic(Active) then
        local Ready = false;
        pcall(function () Ready = MDB.IsSpellReady(Active) == true; end);
        local Bound = false;
        if MDB.ResolveBinding then
          pcall(function () Bound = MDB.ResolveBinding(Active) ~= nil; end);
        end
        if Ready and Bound then
          Seen[Active] = true;
          Out[#Out + 1] = Active;
          if #Out >= Count then return Out; end
        end
      end
    end
  end
  return Out;
end

--- Ordered rotation pool for a rotating slot (3 defensive, 6 interrupt/CC,
-- 7 mobility, 8 self-heal). Cap is hard-limited to 4 so the wire and the
-- `/mdb status` count stay bounded. Catalog priority for the defensive slot
-- is major > minor > utility, with MaxDps's own flagged candidate on top.
function MDB.RotationCandidates (Slot, Count)
  Count = tonumber(Count) or 4;
  if Count > 4 then Count = 4 end;
  if Count < 1 then return {}; end
  local Out, Seen = {}, {};
  local function Append (List, SkipBoss)
    if #Out >= Count then return; end
    local Found = WalkCurated(List, Count, Seen, SkipBoss);
    for i = 1, #Found do
      Out[#Out + 1] = Found[i];
      if #Out >= Count then return; end
    end
  end

  if Slot == 3 then
    local Flagged = FirstFlagged("defensive", false, true);
    if Flagged and not MDB.IsNeverAutomatic(Flagged) then
      Seen[Flagged] = true;
      Out[1] = Flagged;
    end
    local _, classFile, specName = ClassSpec();
    local Table = classFile and specName and MDB.Extras
      and MDB.Extras[classFile] and MDB.Extras[classFile][specName];
    if type(Table) == "table" then
      Append(Table.defensiveMajor);
      Append(Table.defensiveMinor);
      Append(Table.defensive);
    end
  elseif Slot == 6 then
    -- The interrupt candidate stays first-class in Bridge (a live cast is
    -- unconditional); the pool here is the CC fall-through list. The addon CC
    -- toggle gates it (restrict-only, missing = ON) and boss targets are
    -- skipped (immune to the curated CC; unknown fails open/include).
    if not (MDB.Toggles and MDB.Toggles.IsCC and not MDB.Toggles.IsCC()) then
      local _, classFile, specName = ClassSpec();
      local Table = classFile and specName and MDB.Extras
        and MDB.Extras[classFile] and MDB.Extras[classFile][specName];
      if type(Table) == "table" then Append(Table.cc, true); end
    end
  elseif Slot == 7 or Slot == 8 then
    local _, classFile, specName = ClassSpec();
    local Table = classFile and specName and MDB.Extras
      and MDB.Extras[classFile] and MDB.Extras[classFile][specName];
    if type(Table) == "table" then
      Append(Slot == 7 and Table.mobility or Table.selfHeal);
    end
  end
  return Out;
end

--- Defensive urgency with the v3.5 HP-curve fallback. When the plain HP read
-- is secret/unreadable (Midnight combat) but the Ext2 HP curve is live, the
-- slot still offers a conservative Orange so the catalog minor gap-fill is in
-- the pool; otherwise an unreadable HP stays UNKNOWN. MDB.GetDefensiveUrgency
-- itself is untouched so its documented secret-HP = UNKNOWN contract holds.
function MDB.GetDefensiveUrgencyFallback (SpellID)
  local U = MDB.GetDefensiveUrgency(SpellID);
  if U ~= URGENCY_UNKNOWN then return U; end
  if MDB.HpCurve then return URGENCY_ORANGE; end
  return URGENCY_UNKNOWN;
end

--- ======= DEFENSIVE URGENCY + GAP-FILL (protocol v6) =======
-- Mirrors the vendor colour curves in MaxDps:GlowDefensiveHPMidnight
-- (vendor/MaxDps/Buttons.lua:1056-1110) at the curves' own control points.
-- MaxDps has no discrete colour enum: it evaluates UnitHealthPercent through
-- GlowDcurve (0.3 red a=1, 0.5 yellow a=.5, 1.0 green a=0) and stages the
-- RESULT as the glow colour. The stages below are exactly those anchors:
--   HP:      <=30% Red (red anchor), <50% Orange (red->yellow blend),
--            <100% Yellow (yellow->clear fade), 100% White (no glow).
--   Stagger: >=100% Red, >=50% Orange, >=30% Yellow, <30% White (reversed
--            curve, the one per-spell special case: Purifying Brew 119582).
-- Inputs are the ALREADY-SCRUBBED HP / stagger values the vitals cell uses:
-- a secret or failed probe is UNKNOWN, never compared. No arithmetic ever
-- runs on a value that did not survive the scrub (the 99x/384x lesson).
local URGENCY_UNKNOWN, URGENCY_WHITE, URGENCY_YELLOW, URGENCY_ORANGE, URGENCY_RED = 0, 1, 2, 3, 4;

local function UrgencyFromFraction (Fraction)
  if type(Fraction) ~= "number" then return URGENCY_UNKNOWN; end
  if Fraction <= 0.3 then return URGENCY_RED; end
  if Fraction < 0.5 then return URGENCY_ORANGE; end
  if Fraction < 1.0 then return URGENCY_YELLOW; end
  return URGENCY_WHITE;
end

local function UrgencyFromStagger (Fraction)
  if type(Fraction) ~= "number" then return URGENCY_UNKNOWN; end
  if Fraction >= 1.0 then return URGENCY_RED; end
  if Fraction >= 0.5 then return URGENCY_ORANGE; end
  if Fraction >= 0.3 then return URGENCY_YELLOW; end
  return URGENCY_WHITE;
end

-- Stagger fraction (UnitStagger / UnitHealthMax) through the reversed curve.
-- Both reads are pcall-contained and scrubbed; anything not a plain number
-- degrades to UNKNOWN (0).
function MDB.GetStaggerUrgency ()
  if type(UnitStagger) ~= "function" or type(UnitHealthMax) ~= "function" then
    return URGENCY_UNKNOWN;
  end
  local OkS, Stagger = pcall(UnitStagger, "player");
  if not OkS then return URGENCY_UNKNOWN; end
  local OkM, Max = pcall(UnitHealthMax, "player");
  if not OkM then return URGENCY_UNKNOWN; end
  local PlainStagger = Scrubbed(Stagger);
  local PlainMax = Scrubbed(Max);
  if type(PlainStagger) ~= "number" or type(PlainMax) ~= "number" or PlainMax <= 0 then
    return URGENCY_UNKNOWN;
  end
  return UrgencyFromStagger(PlainStagger / PlainMax);
end

-- The urgency MaxDps would render for this spell. Purifying Brew (119582) is
-- the vendor's only per-spell special case; when its stagger is unreadable
-- the vendor itself falls back to the HP curve (`if not color then color =
-- UnitHealthPercent(...)`), so the bridge mirrors that fallback exactly.
function MDB.GetDefensiveUrgency (SpellID)
  if SpellID == 119582 then
    local Stagger = MDB.GetStaggerUrgency();
    if Stagger and Stagger ~= URGENCY_UNKNOWN then return Stagger; end
  end
  local Pct = MDB.GetPlayerHpPct();
  if type(Pct) ~= "number" then return URGENCY_UNKNOWN; end
  return UrgencyFromFraction(Pct / 100);
end

-- The Defensive slot candidate, plus whether it is a catalog gap-fill.
-- 1. MaxDps's own flagged + ready + BOUND defensive wins (the pre-2.3
--    contract; the binding requirement is what the slot encoder applied
--    anyway, but now failing it falls through instead of blanking the slot).
-- 2. When MaxDps names nothing AND the observed HP urgency is Red, the
--    catalog's derived gap-fill list (Catalog.lua, Major first, immunities
--    excluded) supplies the first ready+bound defensive. Below Red the
--    companion never substitutes its own defensive for MaxDps's silence.
-- 3. v3.3.0 Solo ladder (ADDITIVE, group behaviour unchanged): Bridge.lua
--    arms MDB.SoloLadderBands = { minor, major, immunity } (HP pct thresholds;
--    nil/0 band = disabled) from the in-game Solo toggle + group state, and
--    the companion app gates the verdict by the same bands. When Solo bands
--    are armed, a MaxDps-silent slot ALSO offers: defensiveMinor at/below the
--    minor band, defensiveMajor at/below the major band, and immunity
--    at/below the immunity band. Group frames (no bands armed) keep the exact
--    pre-3.3.0 Red/Orange behaviour below. Unchosen talents are naturally
--    ignored: ExtraSpellID only offers a ready+bound spell the player knows.
-- The whole path (including the gap-fill) stays inside MaxDps's own
-- `enableDefensives` switch: muting defensive intelligence in MaxDps mutes
-- the companion's defensive automation too. The companion's own per-ability
-- ON/OFF and the Defensive slot toggle remain the automation controls.
function MDB.GetDefensiveCandidate ()
  local MaxDps = MaxDpsEngine();
  local Enabled = MaxDps and MaxDps.db and MaxDps.db.global and MaxDps.db.global.enableDefensives;
  if not Enabled then return nil, false; end
  local Flagged = FirstFlagged("defensive", false, true);
  if Flagged then return Flagged, false; end
  -- v3.3.0 Solo ladder: HP-banded offers below the classic tiers.
  local Bands = MDB.SoloLadderBands;
  if type(Bands) == "table" then
    local Hp = MDB.GetPlayerHpPct();
    if type(Hp) == "number" then
      local MinorBand = tonumber(Bands.minor) or 0;
      local MajorBand = tonumber(Bands.major) or 0;
      local ImmBand = tonumber(Bands.immunity) or 0;
      if ImmBand > 0 and Hp <= ImmBand then
        local Imm = ExtraSpellID("immunity");
        if Imm then return Imm, true; end
      end
      if MajorBand > 0 and Hp <= MajorBand then
        local Maj = ExtraSpellID("defensiveMajor");
        if Maj then return Maj, true; end
      end
      if MinorBand > 0 and Hp <= MinorBand then
        local Min = ExtraSpellID("defensiveMinor");
        if Min then return Min, true; end
      end
    end
  end
  local Urgency = MDB.GetDefensiveUrgency(nil);
  if Urgency == URGENCY_RED then
    local Gap = ExtraSpellID("defensive");
    if Gap then return Gap, true; end
  elseif Urgency == URGENCY_ORANGE then
    -- v3.0.0 Orange tier: short-cooldown (Minor/None) gap-fill. The user's
    -- complaint was that toggled-on short CDs never fire because the old
    -- gap-fill only offered a candidate at Red. Majors are excluded from the
    -- defensiveMinor list, so a held major can never shadow a ready short CD,
    -- and MaxDps-silent is already required (no flagged candidate above).
    local Gap = ExtraSpellID("defensiveMinor");
    if Gap then return Gap, true; end
  end
  return nil, false;
end

-- The Offensive slot candidate, plus whether it is a companion gap-fill.
-- 1. MaxDps's own flagged + ready offensive wins (unchanged).
-- 2. When MaxDps names none, the curated per-spec `offensive` list (shared
--    burst first, spec-specific second) supplies the first ready+bound entry.
-- The whole path stays inside MaxDps's own `enableCooldowns` switch.
-- There is NO wire source bit for this (PixelProtocol decode is frozen and no
-- slot-flag bit is free), so the companion derives the source by id
-- membership in the same generated Catalog.lua list (see
-- AbilityCatalog.IsOffensiveGapFill). Documented in docs/PROTOCOL.md.
function MDB.GetOffensiveCandidate ()
  local MaxDps = MaxDpsEngine();
  if not (MaxDps and MaxDps.db and MaxDps.db.global and MaxDps.db.global.enableCooldowns) then
    return nil, false;
  end
  local Flagged = FirstFlagged("offensive");
  if Flagged then return Flagged, false; end
  local Gap = ExtraSpellID("offensive");
  if Gap then return Gap, true; end
  return nil, false;
end

function MDB.GetDefensiveUrgencyNibble (SpellID)
  local Nibble = MDB.GetDefensiveUrgency(SpellID);
  if type(Nibble) ~= "number" or Nibble < 0 or Nibble > 4 then return 0; end
  return Nibble;
end

--- ======= EXT2 HP CURVE (protocol Ext2 / v3.0.0) =======
-- The companion reads the player HP off the strip when the game hides the
-- vitals cell (plain cell 27 secret). The bridge never reads the value: it
-- passes a ColorCurve into UnitHealthPercent and forwards the returned
-- colour straight into Texture:SetVertexColor, engine-side. The curve is
-- linear, 0.0 HP -> green (0,1,0,1), 1.0 HP -> red (1,0,0,1), so the
-- decoder's R = hp fraction and G = 1-hp arithmetic holds. Built once,
-- lazily; a client without C_CurveUtil simply reports the curve inactive.
function MDB.EnsureHpCurve ()
  if MDB.HpCurve then return MDB.HpCurve; end
  if MDB._HpCurveTried then return nil; end
  MDB._HpCurveTried = true;
  if not (_G.C_CurveUtil and type(_G.C_CurveUtil.CreateColorCurve) == "function") then
    return nil;
  end
  local Ok, Curve = pcall(_G.C_CurveUtil.CreateColorCurve);
  if not Ok or Curve == nil then return nil; end
  -- Enum / CreateColor live INSIDE the pcall: a client without them must
  -- degrade to inactive, never throw on argument evaluation.
  local OkSetup = pcall(function ()
    Curve:SetType(Enum.LuaCurveType.Linear);
    Curve:AddPoint(0.0, CreateColor(0, 1, 0, 1));
    Curve:AddPoint(1.0, CreateColor(1, 0, 0, 1));
  end);
  if not OkSetup then return nil; end
  MDB.HpCurve = Curve;
  return MDB.HpCurve;
end

--- ======= /mdb heal DIAGNOSTICS =======
-- Per selfHeal list entry: the variant that would be encoded, whether it is
-- known / ready / usable and the resolved key. Every probe is pcall-safe and
-- reports "?" where the client cannot answer. Plain numbers/strings only.
function MDB.SelfHealDiag ()
  local _, classFile, specName = ClassSpec();
  if not classFile or not specName then return "no class/spec"; end
  local Table = MDB.Extras and MDB.Extras[classFile] and MDB.Extras[classFile][specName];
  local List = Table and Table.selfHeal;
  if type(List) ~= "table" then
    return ("%s/%s: no selfHeal list"):format(classFile, specName);
  end
  local Lines = {};
  for i = 1, #List do
    local Entry = List[i];
    local Active = MDB.ActiveVariant(Entry);
    local Known = "?";
    local KnownState = MDB.IsSpellKnownVariant(Active);
    if KnownState == true then Known = "y" elseif KnownState == false then Known = "n"; end
    local Ready = "?";
    local OkReady, IsReady = pcall(MDB.IsSpellReady, Active);
    if OkReady then Ready = IsReady and "y" or "n"; end
    local Usable = "?";
    local OkUsable, IsUsable = pcall(UsableNow, Active);
    if OkUsable then Usable = IsUsable and "y" or "n"; end
    local Key = "-";
    local OkBind, VK = pcall(MDB.ResolveBinding, Active);
    if OkBind and type(VK) == "number" and VK > 0 then
      Key = ("0x%02X"):format(VK);
    end
    local Why = "listed";
    if Active ~= Entry then Why = ("variant of %d"):format(Entry); end
    if KnownState == false then Why = Why .. ", not known"; end
    Lines[#Lines + 1] = ("%d -> %d known=%s ready=%s usable=%s key=%s why=%s")
      :format(Entry, Active, Known, Ready, Usable, Key, Why);
  end
  return table.concat(Lines, "\n");
end

-- /mdb status diagnostics for the extras lists.
function MDB.GetExtrasDiag ()
  local ClassId, SpecId = MDB.GetClassSpec();
  local Parts = {};
  Parts[#Parts + 1] = ("cls=%d spec=%d"):format(ClassId, SpecId);
  local _, classFile, specName = ClassSpec();
  local Table = classFile and specName and MDB.Extras
    and MDB.Extras[classFile] and MDB.Extras[classFile][specName];
  if type(Table) == "table" then
    Parts[#Parts + 1] = ("mob=%d heal=%d def=%d"):format(
      #(Table.mobility or {}), #(Table.selfHeal or {}), #(Table.defensive or {}));
  else
    Parts[#Parts + 1] = "no-extras";
  end
  return table.concat(Parts, " ");
end

--- ======= PERFORMANCE: SLOT-INTENT CHANGE KEY (Stream 2, bridge 3.4.0) =======
-- Bridge.Update caches the encoded slot candidates and recomputes that scan
-- only when this key changes (see Bridge.lua PERF block). The key is built
-- from cheap, pcall-contained reads ONLY:
--   * scalar suggestion values (MaxDps.Spell),
--   * order-independent numeric hashes / counts of the flagged sets,
--   * table + function identities and list shapes (binding / readiness /
--     variant resolvers, per-spec curated lists),
--   * the binding revision and the toggle table.
-- It NEVER copies or scrubs an upstream table, never runs a secret compare
-- outside pcall, and never allocates more than the returned string. Sensor
-- cells (vitals, cast, target, range, aura, class/spec, urgency, HP curve)
-- are recomputed every tick regardless, so only the candidate scan is skipped.
-- Returns nil when the key cannot be computed safely; Bridge then falls back
-- to a full recompute (fail-open, identical behaviour).
function MDB.FrameKey ()
  local function KeyIdent (V)
    if type(issecretvalue) == "function" then
      local OkSecret, Secret = pcall(issecretvalue, V);
      if OkSecret and Secret then return "secret"; end
    end
    local Ok, S = pcall(tostring, V);
    if Ok then return S; end
    return type(V);
  end
  local function KeyCount (T)
    if type(T) ~= "table" then return -1; end
    local N = 0;
    local Ok = pcall(function () for _ in pairs(T) do N = N + 1; end end);
    if not Ok then return -1; end
    return N;
  end
  -- Sum of numeric keys whose value is boolean true. A secret value is not a
  -- boolean and the compare is `==` against a boolean, which never invokes a
  -- number/table metamethod; the whole loop is pcall-contained anyway.
  local function KeyHash (T)
    if type(T) ~= "table" then return -1; end
    local Sum = 0;
    local Ok = pcall(function ()
      for K, V in pairs(T) do
        if type(K) == "number" and type(V) == "boolean" and V == true then Sum = Sum + K; end
      end
    end);
    if not Ok then return -1; end
    return Sum;
  end
  local function KeyList (List)
    if type(List) ~= "table" then return "nil"; end
    local Ok, N = pcall(function () return #List; end);
    return KeyIdent(List) .. "#" .. (Ok and N or -1);
  end

  local MaxDps = MaxDpsEngine();
  if not MaxDps then return nil; end;
  local _, classFile, specName = ClassSpec();
  local Parts = {};
  local function P (Label, Value) Parts[#Parts + 1] = Label .. "=" .. Value; end

  P("spell", KeyIdent(MaxDps.Spell));
  P("glow", KeyIdent(MaxDps.SpellsGlowing) .. "#" .. KeyCount(MaxDps.SpellsGlowing));
  P("spells", KeyIdent(MaxDps.Spells) .. "#" .. KeyCount(MaxDps.Spells));
  P("flags", KeyCount(MaxDps.Flags) .. "@" .. KeyHash(MaxDps.Flags));
  P("items", KeyIdent(MaxDps.ItemSpells) .. "#" .. KeyCount(MaxDps.ItemSpells));
  P("cls", KeyIdent(classFile));
  P("spec", KeyIdent(specName));

  local CDs = MaxDps.classCooldowns and classFile and MaxDps.classCooldowns[classFile]
    and MaxDps.classCooldowns[classFile][specName];
  P("def", KeyList(CDs and CDs.defensive));
  P("off", KeyList(CDs and CDs.offensive));
  local Ints = MaxDps.classInterrupts and classFile and MaxDps.classInterrupts[classFile]
    and MaxDps.classInterrupts[classFile][specName];
  P("int", KeyList(Ints));
  local Global = MaxDps.db and MaxDps.db.global;
  P("cdOn", KeyIdent(Global and Global.enableCooldowns));
  P("defOn", KeyIdent(Global and Global.enableDefensives));

  local Extra = MDB.Extras and classFile and MDB.Extras[classFile]
    and MDB.Extras[classFile][specName];
  P("x", Extra and (KeyList(Extra.mobility) .. KeyList(Extra.selfHeal)
    .. KeyList(Extra.offensive) .. KeyList(Extra.defensive)
    .. KeyList(Extra.defensiveMajor) .. KeyList(Extra.defensiveMinor)
    .. KeyList(Extra.immunity) .. KeyList(Extra.cc)) or "nil");

  P("bind", KeyIdent(MDB._BindCache) .. "#" .. KeyCount(MDB._BindCache)
    .. "@" .. KeyIdent(MDB._BindRevision));
  do
    local OkHp, Hp = pcall(MDB.GetPlayerHpPct);
    P("hp", OkHp and KeyIdent(Hp) or "?");
  end
  local Bands = MDB.SoloLadderBands;
  if type(Bands) == "table" then
    P("solo", KeyIdent(Bands.minor) .. "," .. KeyIdent(Bands.major)
      .. "," .. KeyIdent(Bands.immunity));
  else
    P("solo", "nil");
  end
  local TG = MaxDpsBridgeDB and MaxDpsBridgeDB.Toggles;
  if type(TG) == "table" then
    P("tg", KeyIdent(TG.Main) .. KeyIdent(TG.Offensive) .. KeyIdent(TG.Defensive)
      .. KeyIdent(TG.Consumable) .. KeyIdent(TG.Trinket) .. KeyIdent(TG.Interrupt)
      .. KeyIdent(TG.Mobility) .. KeyIdent(TG.SelfHeal) .. KeyIdent(TG.Solo)
      .. KeyIdent(TG.OOC) .. KeyIdent(TG.AutoTarget) .. KeyIdent(TG.AutoInteract)
      .. KeyIdent(TG.TTK));
  else
    P("tg", "nil");
  end
  P("f", table.concat({
    KeyIdent(_G.C_Spell and _G.C_Spell.GetSpellCooldown),
    KeyIdent(_G.C_Spell and _G.C_Spell.GetSpellCooldownDuration),
    KeyIdent(_G.C_Spell and _G.C_Spell.GetSpellCharges),
    KeyIdent(_G.C_Spell and _G.C_Spell.IsSpellUsable),
    KeyIdent(_G.C_SpellBook),
    KeyIdent(_G.IsPlayerSpell),
    KeyIdent(_G.FindBaseSpellByID),
    KeyIdent(_G.FindSpellOverrideByID),
    KeyIdent(_G.GetOverrideSpell),
    KeyIdent(_G.GetMacroSpell),
    KeyIdent(_G.GetActionInfo),
    KeyIdent(_G.GetBindingKey),
    KeyIdent(_G.GetSpellTexture),
    KeyIdent(MDB.BindingForTexture),
    KeyIdent(MaxDps.IsSpellInRange),
    KeyIdent(MDB.GetMainSpellID),
    KeyIdent(MDB.GetOffensiveCandidate),
    KeyIdent(MDB.GetDefensiveCandidate),
    KeyIdent(MDB.ExtraCandidates),
    KeyIdent(MDB.ResolveBinding),
    KeyIdent(MDB.IsSpellReady),
    KeyIdent(MDB.IsInterruptReady),
  }, ","));
  return table.concat(Parts, "|");
end
