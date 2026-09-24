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
local function ScrubbedFlags ()
  local MaxDps = MaxDpsEngine();
  local Flags = MaxDps and MaxDps.Flags;
  if type(Flags) ~= "table" then return nil, nil; end
  if type(scrubsecretvalues) == "function" then
    local Ok, Clean = pcall(scrubsecretvalues, Flags);
    if Ok and type(Clean) == "table" then return Clean, MaxDps; end
  end
  -- No scrub API (pre-Midnight client): table holds no secrets by
  -- construction, iterate directly.
  return Flags, MaxDps;
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
  if type(UnitClass) ~= "function" then return nil; end
  local OkC, _, classFile = pcall(UnitClass, "player");
  if not OkC or not classFile then return nil; end
  local specName = nil;
  if type(GetSpecialization) == "function" and type(GetSpecializationInfo) == "function" then
    local OkS, specIndex = pcall(function ()
      return GetSpecializationInfo(GetSpecialization());
    end);
    if OkS and specIndex and MaxDps.idtospec then specName = MaxDps.idtospec[specIndex]; end
  end
  if not specName then return nil; end
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

local function FirstFlagged (WantCategory, IsInterrupt)
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
      if Ready then
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
  local MaxDps = MaxDpsEngine();
  if not (MaxDps and MaxDps.db and MaxDps.db.global and MaxDps.db.global.enableDefensives) then
    return nil;
  end
  return FirstFlagged("defensive");
end

-- spellID -> itemID for flagged item spells, plus which itemIDs are
-- potions/consumables (MaxDps.Consumables). Two disjoint Spell Frame
-- buckets come out of this: consumable = potion items, trinket = the rest
-- (equipped on-use trinkets). v1.3.0: iterated over ScrubbedFlags (never
-- raw MaxDps tables), so keys are proven non-secret — plain compares.
local function ItemSpellIDs ()
  local MaxDps = MaxDpsEngine();
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
function MDB.GetOffensiveSpellID ()
  local MaxDps = MaxDpsEngine();
  if not (MaxDps and MaxDps.db and MaxDps.db.global and MaxDps.db.global.enableCooldowns) then
    return nil;
  end
  return FirstFlagged("offensive");
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

local function DurationRemainingOk (SpellID)
  -- Duration-object fallback: engine-side remaining-time evaluation.
  -- Returns true = ready (remaining ≤ 0.5 s tail or inactive), false =
  -- on real cooldown, nil = API unavailable/failed (caller fails open).
  if not (_G.C_Spell and type(_G.C_Spell.GetSpellCooldownDuration) == "function") then
    return nil;
  end
  local OkDur, Duration = pcall(_G.C_Spell.GetSpellCooldownDuration, SpellID);
  if not OkDur or Duration == nil then return nil; end
  -- Duration objects are engine userdata; method calls on them run
  -- engine-side and accept secret internals (documented pattern).
  -- EvaluateRemainingDuration needs a ColorCurve; build one per call is
  -- cheap (no per-frame allocation pressure at 20 Hz for ≤6 slots — and
  -- pcall-wrapped so a missing API degrades to nil, never throws out).
  local OkCurve, Curve = pcall(C_CurveUtil.CreateColorCurve);
  if not OkCurve or Curve == nil then return nil; end
  local OkType = pcall(Curve.SetType, Curve, Enum.LuaCurveType.Linear);
  if not OkType then return nil; end
  pcall(Curve.AddPoint, Curve, 0.0, CreateColor(1, 0, 0, 1));
  pcall(Curve.AddPoint, Curve, 1.0, CreateColor(0, 1, 0, 1));
  local OkEval, Remaining = pcall(Duration.EvaluateRemainingDuration, Duration, Curve);
  if not OkEval then return nil; end
  local Clean = Scrubbed(Remaining);
  if type(Clean) ~= "number" then return nil; end
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

local function UsableNow (SpellID)
  if not (_G.C_Spell and type(_G.C_Spell.IsSpellUsable) == "function") then
    return true;  -- fail-open
  end
  -- dropsecretaccess() containment: strips secret access from THIS
  -- function, so the boolean it returns is provably plain (documented
  -- Midnight API). Plain false ⇒ unusable; anything else ⇒ usable.
  -- Wrapped in pcall (pre-12.0 clients lack the API → fail open).
  local Ok, Usable = pcall(function ()
    if type(dropsecretaccess) == "function" then dropsecretaccess(); end
    return _G.C_Spell.IsSpellUsable(SpellID);
  end);
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
  return true;
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
  local Buttons = MaxDps and MaxDps.Spells and MaxDps.Spells[SpellID];
  if not Buttons then return nil; end
  local OkRead, Result = pcall(function ()
    if type(dropsecretaccess) == "function" then dropsecretaccess(); end
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
  local OkScan, Found = pcall(function ()
    if type(dropsecretaccess) == "function" then dropsecretaccess(); end
    local SearchName = nil;
    if C_Spell and C_Spell.GetSpellName then
      local Name = C_Spell.GetSpellName(SpellID);
      if type(Name) == "string" then SearchName = Name; end
    end
    for Slot = 1, 180 do
      local ActionType, ID = GetActionInfo(Slot);
      if ActionType == "spell" and type(ID) == "number" then
        if ID == SpellID then return Slot; end
        if SearchName and C_Spell and C_Spell.GetSpellName then
          local SlotName = C_Spell.GetSpellName(ID);
          if type(SlotName) == "string" and SlotName == SearchName then return Slot; end
        end
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
  if SpellID ~= MDB.GetMainSpellID() then return nil; end
  if not Frame.bindText or not Frame.bindText.GetText then return nil; end
  local Ok, Text = pcall(Frame.bindText.GetText, Frame.bindText);
  if not Ok then return nil; end
  return MDB.ParseBinding(MDB.ExpandHotKey(Text));
end

local function TextureBinding (SpellID)
  -- SpellID arrives from scrubbed scans (proven plain, pure-Lua check).
  if type(SpellID) ~= "number" or SpellID == 0 then return nil; end
  local Texture = nil;
  if C_Spell and C_Spell.GetSpellTexture then
    local Ok, Tex = pcall(C_Spell.GetSpellTexture, SpellID);
    if Ok then Texture = Tex; end
  end
  if not Texture and type(GetSpellTexture) == "function" then
    local Ok, Tex = pcall(GetSpellTexture, SpellID);
    if Ok then Texture = Tex; end
  end
  if not Texture then return nil; end
  return MDB.ParseBinding(MDB.BindingForTexture(Texture));
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
