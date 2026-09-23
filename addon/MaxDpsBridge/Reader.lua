--- ============================ HEADER ============================
-- READ-ONLY readout of the live MaxDps engine. Never calls protected Lua,
-- never drives gameplay; it only reports what MaxDps already suggests.
--
--   Main       = MaxDps.Spell (number spellID, set by Core:InvokeNextSpell)
--   Cooldown   = first MaxDps.Flags[spellID]==true that is not interrupt /
--                defensive / consumable, while enableCooldowns is on
--   Interrupt  = Flags entry set via MaxDps:GlowInteruptMidnight
--   Defensive  = Flags entry set via MaxDps:GlowDefensiveHPMidnight
--   Consumable = flagged spellID that appears in MaxDps.ItemSpells values
--
-- READINESS (v1.2.0): every slot passes through MDB.IsSpellReady before it
-- is encoded. A spell on cooldown or currently unusable (out of range,
-- no resources, shapeshifted, ...) encodes as EMPTY (FLAG_VALID clear) so
-- the companion skips it and fires the next ready slot instead of
-- hammering an unavailable key.
--
-- SECRET-SAFETY (v1.2.0, Midnight 12.x): in restricted contexts (combat /
-- encounter / M+ / PvP) C_Spell.GetSpellCooldown / GetSpellCharges hand
-- back SECRET numbers and booleans. Tainted addon code may store/pass
-- secrets but must never compare, arithmetic, boolean-test, or key them:
-- doing so throws immediately ("attempt to compare local 'Start' (a
-- secret number value, while execution tainted by 'MaxDpsBridge')" — the
-- 860x spam from the v1.1.0 gate at Reader.lua:417 via IsSpellReady <-
-- GetMainSpellID <- Bridge Update). The gate is now secret-safe by
-- construction, using ONLY the fields Blizzard marks NeverSecret:
--
--   * Cooldown: SpellCooldownInfo.isActive / .isOnGCD (NeverSecret bools,
--     the plain `true` seen in the error dump). isActive==false => no
--     active cooldown. isActive==true + isOnGCD==true => the wait is the
--     GCD, which v1.1.0 deliberately forgives. isActive==true +
--     isOnGCD==false => real cooldown => not ready. Only when isActive is
--     missing does a pcall-guarded numeric fallback run; the secret throw
--     inside it is caught and degrades to fail-open.
--   * Charges: SpellChargeInfo.isActive (NeverSecret) plus a guarded
--     currentCharges compare. currentCharges is secret-restricted too, so
--     it is probed with issecretvalue (pcall-guarded, failure = unsafe):
--     secret => unknown => fail-open; readable => old v1.1.0 semantics.
--     cooldownStartTime/cooldownDuration are never compared directly.
--   * Usable: C_Spell.IsSpellUsable (no SecretWhen predicate) => plain
--     booleans, safe to branch on.
--   * MaxDps:CooldownConsolidated is never called: its remains math runs
--     inside tainted execution and throws on the same secrets (upstream
--     Helper.lua:1892 does GetTime() arithmetic on secret start/duration).
--   * Secret spell IDs are unbranchable and unencodable (the pixel strip
--     needs nibble math), so they degrade to "no suggestion" instead of
--     erroring.
-- Fail-open everywhere: unknown/nil/pcall-failure degrades to "ready" so
-- an API hiccup can never silence the rotation; a secret spell ID
-- degrades to "no suggestion" for the same reason.
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

--- ======= SECRET-SAFETY HELPERS =======

-- Midnight 12.x returns SECRET values from combat-restricted APIs. Tainted
-- addon code may store and pass them, but comparing, doing arithmetic on,
-- boolean-testing, or using one as a table key throws immediately. These
-- helpers are the ONLY place the bridge probes a possibly-secret value, so
-- no raw cooldown/charge field is ever compared directly.
--
-- issecretvalue is the sanctioned probe but is itself annotated
-- SecretArguments=AllowedWhenUntainted, so the probe is pcall-guarded and
-- FAILURE IS TREATED AS UNSAFE: we only ever do numeric work on a value
-- that proved non-secret. Callers treat "unsafe" as UNKNOWN (nil) and
-- fail OPEN — never as an error, never as a false "not ready".
function MDB.IsValueSafe (Value)
  if type(issecretvalue) == "function" then
    local Ok, Secret = pcall(issecretvalue, Value);
    if not Ok or Secret == true then return false; end
  end
  return true;
end

-- A spell ID usable for compare / table key / nibble math: a real number,
-- provably non-secret, nonzero. Secret or malformed IDs cannot be
-- branched on or encoded, so callers treat false as "no suggestion".
local function IsSpellIDValue (Value)
  if type(Value) ~= "number" then return false; end
  if not MDB.IsValueSafe(Value) then return false; end
  return Value ~= 0;
end

-- A NeverSecret boolean from an API table: plain when it is a real
-- boolean, nil when missing or (defensively) secret. Docs bless these
-- fields as NeverSecret, so no pcall dance is needed beyond the type
-- check; a secret here would still be caught by IsValueSafe.
local function SafeBool (Value)
  if type(Value) ~= "boolean" then return nil; end
  if not MDB.IsValueSafe(Value) then return nil; end
  return Value;
end

--- ======= CATEGORY HOOKS =======

local function SyncSet (Set, SpellID)
  -- Secret IDs cannot be table keys or compares: drop them outright.
  if not IsSpellIDValue(SpellID) then return; end
  local MaxDps = MaxDpsEngine();
  if MaxDps and MaxDps.Flags and MaxDps.Flags[SpellID] == true then
    Set[SpellID] = true;
  else
    Set[SpellID] = nil;
  end
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
  local MDPS = MaxDpsEngine();
  if not MDPS then DiagPrint("no MaxDps engine"); return; end
    local Count, Shown = 0, 0;
    local WithHotKey, HotKeyText = 0, "-";
    if MDPS.Spells then
      for SpellID, Buttons in pairs(MDPS.Spells) do
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
      local ActionType = GetActionInfo(Slot);
      if ActionType == "spell" then SlotHit = SlotHit + 1; end
    end
    DiagPrint(("diag spells=%d withHotKey=%d e.g.%s bars={%s} spellSlots=%d/180")
      :format(Count, WithHotKey, HotKeyText, table.concat(BarHit, ","), SlotHit));
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

--- ======= SLOT READOUT =======

function MDB.GetMainSpellID ()
  local MaxDps = MaxDpsEngine();
  if not MaxDps then return nil; end
  MDB.EnsureEngine();
  local SpellID = MaxDps.Spell;
  if IsSpellIDValue(SpellID) then
    -- Even the engine's current pick can go stale between its tick and
    -- ours (it fired, now on cooldown). Gate it like every other slot.
    if MDB.IsSpellReady(SpellID) then return SpellID; end
    -- Stale pick: fall through to the live re-query below instead of
    -- encoding an unavailable spell.
  end
  -- Classic path: the class function returns the spellID directly.
  -- Guarded by the FrameData check above: Hunter:BeastMastery indexes
  -- FrameData.ACSpells on entry and dies without the EnsureEngine prep.
  if type(MaxDps.NextSpell) == "function"
    and MaxDps.FrameData and MaxDps.FrameData.ACSpells then
    local Ok, Res = pcall(MaxDps.NextSpell, MaxDps);
    if Ok and IsSpellIDValue(Res) then return Res; end
  end
  -- Calibrate-pattern readout also needs the assisted-combat answer, so
  -- run the class glow pass first (fills Flags/InterruptSet/DefensiveSet
  -- even while idle), exactly like InvokeNextSpell does minus the glow
  -- of the main spell onto the bars. Pure queries + glow overlays only.
  -- Hunter:BeastMastery hits MaxDps:GlowCooldownMidnight for trinkets and
  -- IsAddOnLoaded hits, so guard hard: any error here must not propagate.
  if type(MaxDps.NextSpell) == "function" and MaxDps.FrameData and MaxDps.FrameData.ACSpells then
    pcall(MaxDps.NextSpell, MaxDps);
  end
  -- Retail Midnight path (Core.lua:791-797): the class function only glows
  -- cooldowns/interrupts and returns nothing; the main spell comes from the
  -- assisted-combat API gated by CheckSpellUsable. Query-only, no gameplay.
  -- Plus our own readiness gate: the AC pick can be a frame stale too.
  if _G.C_AssistedCombat and type(_G.C_AssistedCombat.GetNextCastSpell) == "function" then
    local Ok, Next = pcall(_G.C_AssistedCombat.GetNextCastSpell, false);
    if Ok and IsSpellIDValue(Next) then
      if type(MaxDps.CheckSpellUsable) == "function" then
        local SpellName = nil;
        if _G.C_Spell and type(_G.C_Spell.GetSpellName) == "function" then
          local OkName, Name = pcall(_G.C_Spell.GetSpellName, Next);
          if OkName then SpellName = Name; end
        end
        local OkUse, Usable = pcall(MaxDps.CheckSpellUsable, MaxDps, Next, SpellName);
        if OkUse and Usable and MDB.IsSpellReady(Next) then return Next; end
      elseif MDB.IsSpellReady(Next) then
        return Next;
      end
    end
  end
  return nil;
end

-- Smallest flagged spellID in Set that is still flagged, on the bars,
-- AND ready to cast right now (v1.2.0 secret-safe readiness gate). Stale
-- entries (Flags cleared by DestroyAllOverlays/Fetch) and unready spells
-- (cooldown / unusable) are pruned here so the companion never hammers
-- an unavailable key while other slots have live suggestions.
-- InterruptSet members additionally require a live interruptible cast.
-- Keys are safe by construction (SyncSet drops secret IDs), the guard is
-- belt-and-braces so a compare can never see a secret.
local function FirstFlagged (Set, IsInterrupt)
  local MaxDps = MaxDpsEngine();
  local Flags = MaxDps and MaxDps.Flags;
  local Spells = MaxDps and MaxDps.Spells;
  if not Flags or not Spells then return nil; end
  local Best = nil;
  for SpellID in pairs(Set) do
    if IsSpellIDValue(SpellID) and Flags[SpellID] == true and Spells[SpellID] then
      local Ready;
      if IsInterrupt then Ready = MDB.IsInterruptReady(SpellID);
      else Ready = MDB.IsSpellReady(SpellID); end
      if Ready then
        if not Best or SpellID < Best then Best = SpellID; end
      else
        -- Unready right now (cooldown ticking, no cast to kick): drop from
        -- the set so the NEXT slot wins this frame. The glow hook re-adds
        -- it when MaxDps re-suggests it, so nothing is lost permanently.
        Set[SpellID] = nil;
      end
    else
      Set[SpellID] = nil;
    end
  end
  return Best;
end

function MDB.GetInterruptSpellID ()
  return FirstFlagged(InterruptSet, true);
end

function MDB.GetDefensiveSpellID ()
  local MaxDps = MaxDpsEngine();
  if not (MaxDps and MaxDps.db and MaxDps.db.global and MaxDps.db.global.enableDefensives) then
    return nil;
  end
  return FirstFlagged(DefensiveSet);
end

local function ItemSpellIDs ()
  local MaxDps = MaxDpsEngine();
  local Out = {};
  if MaxDps and MaxDps.ItemSpells then
    for _, ItemSpellID in pairs(MaxDps.ItemSpells) do
      if IsSpellIDValue(ItemSpellID) then Out[ItemSpellID] = true; end
    end
  end
  return Out;
end

function MDB.GetConsumableSpellID ()
  local MaxDps = MaxDpsEngine();
  local Flags = MaxDps and MaxDps.Flags;
  local Spells = MaxDps and MaxDps.Spells;
  if not Flags or not Spells then return nil; end
  local Items = ItemSpellIDs();
  local Best = nil;
  for SpellID, On in pairs(Flags) do
    if On == true and IsSpellIDValue(SpellID) and Items[SpellID] and Spells[SpellID]
      and MDB.IsSpellReady(SpellID) then
      if not Best or SpellID < Best then Best = SpellID; end
    end
  end
  return Best;
end

function MDB.GetCooldownSpellID ()
  local MaxDps = MaxDpsEngine();
  local Flags = MaxDps and MaxDps.Flags;
  local Spells = MaxDps and MaxDps.Spells;
  if not Flags or not Spells then return nil; end
  if not (MaxDps.db and MaxDps.db.global and MaxDps.db.global.enableCooldowns) then
    return nil;
  end
  local Items = ItemSpellIDs();
  local Best = nil;
  for SpellID, On in pairs(Flags) do
    if On == true and IsSpellIDValue(SpellID) and Spells[SpellID]
      and not InterruptSet[SpellID] and not DefensiveSet[SpellID]
      and not Items[SpellID] and MDB.IsSpellReady(SpellID) then
      if not Best or SpellID < Best then Best = SpellID; end
    end
  end
  return Best;
end

--- ======= READINESS GATE (v1.2.0, secret-safe) =======

-- True when the spell can actually be cast RIGHT NOW. Read-only queries
-- only, all pcall-guarded (a dead API on one client must degrade to
-- "ready" rather than blanking the whole rotation).
--
-- SECRET-SAFETY: under Midnight restriction predicates (combat /
-- encounter / M+ / PvP) C_Spell.GetSpellCooldown and GetSpellCharges
-- return SECRET numbers and booleans. Tainted code that compares
-- (<, <=, >, >=, ==), does arithmetic (+, -), boolean-tests, or keys on
-- those values throws IMMEDIATELY ("attempt to compare local 'Start' (a
-- secret number value, while execution tainted by 'MaxDpsBridge')") —
-- the 860x spam from the v1.1.0 gate at Reader.lua:417.
--
-- This gate never touches a raw field. It uses only NeverSecret data:
--
--   1. Cooldown: SpellCooldownInfo.isActive / .isOnGCD — documented
--      NeverSecret booleans (the plain `true` visible in the error dump).
--      isActive==false => no active cooldown. isActive==true and
--      isOnGCD==true => the wait is the GCD, which v1.1.0 deliberately
--      forgave. isActive==true and isOnGCD==false => real cooldown => not
--      ready. A pcall-guarded numeric fallback runs only when isActive is
--      absent; a secret throw inside it is caught and fails open.
--   2. Charges: SpellChargeInfo.isActive (NeverSecret) plus a
--      currentCharges compare guarded by MDB.IsValueSafe. Secret/unknown
--      currentCharges fails open; readable values keep v1.1.0 semantics.
--      cooldownStartTime/cooldownDuration are only read when the charge
--      count itself proved non-secret, so they are readable too.
--   3. Usable: C_Spell.IsSpellUsable — no SecretWhen predicate => plain
--      booleans, safe to branch on.
--   4. MaxDps:CooldownConsolidated is deliberately NOT called: its
--      remains math runs inside tainted execution and throws on the same
--      secrets (upstream Helper.lua:1892).
--   5. Overlay glow (Devotion-special): MaxDps:GlowInteruptMidnight sets
--      Flags[spell] even when the target is NOT interruptible and only
--      dims the overlay alpha to 0 (vendor Buttons.lua:1136-1143 — the flag
--      is never cleared for a non-interruptible cast). The interrupt
--      check below re-validates the target cast instead of trusting it.
--
-- Anything unknown (nil APIs, pcall failure, no cooldown info, secret
-- values) returns TRUE: fail-open, so an API hiccup can never silence the
-- rotation.

-- Legacy numeric cooldown check, used only when the NeverSecret isActive
-- flag is absent. Runs inside pcall at the call site: under restrictions
-- startTime/duration are secret, the compare throws, and the throw is the
-- signal to fail open. Keeps the v1.1.0 GCD + 0.5 s tail forgiveness.
local function NumericCooldownReady (Info)
  local Start = Info.startTime or 0;
  local Duration = Info.duration or 0;
  if type(Start) ~= "number" or type(Duration) ~= "number" then return true; end
  if Start <= 0 or Duration <= 0 then return true; end
  local Remains = Duration - (GetTime() - Start);
  local OkGcd, GcdInfo = pcall(_G.C_Spell.GetSpellCooldown, 61304);
  if OkGcd and type(GcdInfo) == "table" then
    local GcdDuration = GcdInfo.duration;
    if type(GcdDuration) == "number" and GcdDuration > 0 and Remains <= GcdDuration then
      return true;
    end
  end
  return Remains <= 0.5;
end

-- 0 banked charges: ready only when the recharge tail fits the same 0.5 s
-- forgiveness window v1.1.0 used. Reached only when currentCharges proved
-- non-secret, so start/duration are readable too; still probed + pcall'd.
local function ChargeTailReady (Info)
  local Start = Info.cooldownStartTime;
  local Duration = Info.cooldownDuration;
  if not MDB.IsValueSafe(Start) or not MDB.IsValueSafe(Duration) then return nil; end
  if type(Start) ~= "number" or type(Duration) ~= "number" then return nil; end
  if Start <= 0 or Duration <= 0 then return false; end
  local Remains = Duration - (GetTime() - Start);
  return Remains <= 0.5;
end

local function HasCharges (SpellID)
  if not (_G.C_Spell and type(_G.C_Spell.GetSpellCharges) == "function") then
    return nil;  -- unknown: let the cooldown path decide
  end
  local Ok, Info = pcall(_G.C_Spell.GetSpellCharges, SpellID);
  if not Ok or type(Info) ~= "table" then return nil; end
  -- NeverSecret (12.0.1+): false => not recharging => at max charges =>
  -- a charge is banked and the spell is ready.
  local Charging = SafeBool(Info.isActive);
  if Charging == false then return true; end
  local Charges = Info.currentCharges;
  if not MDB.IsValueSafe(Charges) then
    -- Secret count (restricted content): cannot count without touching a
    -- secret. Recharging => fail open (a 0-charge press is ignored by the
    -- game; failing closed would drop the spell for the whole fight).
    -- Otherwise unknown => let the cooldown path decide.
    if Charging == true then return true; end
    return nil;
  end
  if type(Charges) ~= "number" then return nil; end
  if Charges >= 1 then return true; end
  local OkTail, Ready = pcall(ChargeTailReady, Info);
  if not OkTail or Ready == nil then return false; end
  return Ready == true;
end

local function CooldownReady (SpellID)
  if not (_G.C_Spell and type(_G.C_Spell.GetSpellCooldown) == "function") then
    return true;  -- fail-open
  end
  local Ok, Info = pcall(_G.C_Spell.GetSpellCooldown, SpellID);
  if not Ok or type(Info) ~= "table" then return true; end
  -- NeverSecret booleans (12.0.1+): plain true/false even while
  -- startTime/duration are secret (seen in the live error dump).
  local IsActive = SafeBool(Info.isActive);
  if IsActive == false then return true; end   -- no active cooldown
  if IsActive == true then
    local OnGCD = SafeBool(Info.isOnGCD);
    if OnGCD == true then return true; end     -- GCD wait: forgiven
    if OnGCD == false then return false; end   -- real cooldown active
    -- isOnGCD unreadable: fall through to the guarded numeric check.
  end
  local OkNum, Ready = pcall(NumericCooldownReady, Info);
  if not OkNum or Ready == nil then return true; end  -- secret throw: fail-open
  return Ready == true;
end

local function UsableNow (SpellID)
  if not (_G.C_Spell and type(_G.C_Spell.IsSpellUsable) == "function") then
    return true;  -- fail-open
  end
  local Ok, Usable = pcall(_G.C_Spell.IsSpellUsable, SpellID);
  -- pcall failure or nil verdict: fail-open. Explicit false = unusable.
  if not Ok or Usable == nil then return true; end
  return Usable == true;
end

--- Returns true when the spell may be encoded into a slot.
function MDB.IsSpellReady (SpellID)
  -- Secret IDs cannot be compared or encoded (the strip needs nibble
  -- math): treat as "no suggestion" instead of throwing.
  if not IsSpellIDValue(SpellID) then return false; end
  -- Charges first: a 0-charge spell is dead even if the cooldown API
  -- reports something odd.
  local Charged = HasCharges(SpellID);
  if Charged == false then return false; end
  if Charged == true then
    -- Charges available: skip the cooldown check (recharge timer runs
    -- while charges remain) but still require usable.
    return UsableNow(SpellID);
  end
  if not CooldownReady(SpellID) then return false; end
  return UsableNow(SpellID);
end

--- Interrupt slots need a live, interruptible cast on the target — the
--- upstream flag alone is not enough (see header note 5). Read-only:
--- UnitCastingInfo/UnitChannelInfo + the notInterruptible boolean, the
--- same fields the overlay-alpha code reads (vendor Buttons.lua:1125-1127).
---
--- SECRET-SAFETY: under SecretWhenUnitSpellCastRestricted, cast info for
--- non-player units (i.e. every enemy) comes back SECRET, including
--- notInterruptible. A secret boolean cannot be branched on, so it is
--- probed and skipped; the verdict then degrades to "unknown" and the
--- caller fails OPEN (trusts MaxDps's own flag), preserving interrupts in
--- restricted content instead of dropping the slot for the whole fight.

-- Reads the live target cast state. Returns true = interruptible,
-- false = explicitly non-interruptible, nil = unknown (no cast / secret /
-- API failure). notInterruptible sits at return #8 of UnitCastingInfo and
-- #7 of UnitChannelInfo on retail 12.x (castingSpellID pushed the casting
-- index from 7 to 8 in 7.2.5); both plausible slots are checked but only
-- a readable boolean is ever accepted, so a number/string neighbour can
-- never be mistaken for the flag.
local function ReadInterruptible ()
  local Target = "target";
  if type(UnitCastingInfo) == "function" then
    local Ok, R1, R2, R3, R4, R5, R6, R7, R8 = pcall(UnitCastingInfo, Target);
    if Ok then
      local NotInt = nil;
      if type(R8) == "boolean" then NotInt = R8;
      elseif type(R7) == "boolean" then NotInt = R7; end
      if NotInt ~= nil and MDB.IsValueSafe(NotInt) then
        return NotInt == false;
      end
    end
  end
  if type(UnitChannelInfo) == "function" then
    local Ok, R1, R2, R3, R4, R5, R6, R7 = pcall(UnitChannelInfo, Target);
    if Ok and type(R7) == "boolean" and MDB.IsValueSafe(R7) then
      return R7 == false;
    end
  end
  return nil;
end

function MDB.IsInterruptReady (SpellID)
  if not MDB.IsSpellReady(SpellID) then return false; end
  local Target = "target";
  if type(UnitExists) == "function" then
    local Ok, Exists = pcall(UnitExists, Target);
    -- Only an explicitly readable "no target" denies: secret/unreadable
    -- returns fall through to fail-open rather than blanking the slot.
    if Ok and MDB.IsValueSafe(Exists) and Exists == false then
      return false;
    end
  end
  local OkRead, Interruptible = pcall(ReadInterruptible);
  if not OkRead then return true; end
  -- nil (no cast running or secret cast info): fail-OPEN for the cast
  -- check — MaxDps may pre-suggest the interrupt for the cast that is
  -- about to start, and the cooldown/usable gates above already passed.
  -- Only an explicitly readable non-interruptible cast is a hard no:
  -- encoding it would spam kick into an immune cast.
  if Interruptible == false then return false; end
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

local function HotKeyBinding (SpellID)
  local MaxDps = MaxDpsEngine();
  local Buttons = MaxDps and MaxDps.Spells and MaxDps.Spells[SpellID];
  if not Buttons then return nil; end
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
        local VirtualKey, Modifiers = MDB.ParseBinding(MDB.ExpandHotKey(Text));
        if VirtualKey then return VirtualKey, Modifiers; end
      end
    end
  end
  return nil;
end

-- Mirrors SpellFrame.lua FindSpellOnActionBar (slots 1-180, id or name match).
local function FindSpellOnActionBar (SpellID)
  if not SpellID then return nil; end
  local SearchName = nil;
  if C_Spell and C_Spell.GetSpellName then
    local Ok, Name = pcall(C_Spell.GetSpellName, SpellID);
    if Ok then SearchName = Name; end
  end
  for Slot = 1, 180 do
    local ActionType, ID = GetActionInfo(Slot);
    if ActionType == "spell" and ID then
      if ID == SpellID then return Slot; end
      if SearchName and C_Spell and C_Spell.GetSpellName then
        local Ok, SlotName = pcall(C_Spell.GetSpellName, ID);
        if Ok and SlotName and SlotName == SearchName then return Slot; end
      end
    end
  end
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
  if SpellID ~= MDB.GetMainSpellID() then return nil; end
  if not Frame.bindText or not Frame.bindText.GetText then return nil; end
  local Ok, Text = pcall(Frame.bindText.GetText, Frame.bindText);
  if not Ok then return nil; end
  return MDB.ParseBinding(MDB.ExpandHotKey(Text));
end

local function TextureBinding (SpellID)
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

local function ResolveBindingUncached (SpellID)
  local VirtualKey, Modifiers = HotKeyBinding(SpellID);
  if VirtualKey then return VirtualKey, Modifiers; end

  VirtualKey, Modifiers = MDB.ParseBinding(GetKeybindForSpell(SpellID));
  if VirtualKey then return VirtualKey, Modifiers; end

  VirtualKey, Modifiers = SpellFrameBinding(SpellID);
  if VirtualKey then return VirtualKey, Modifiers; end

  return TextureBinding(SpellID);
end

-- Results are cached per spellID; the cache is wiped by bar updates
-- (via MDB.InvalidateBindings) and by MaxDps:Fetch/category hooks.
function MDB.ResolveBinding (SpellID)
  if not IsSpellIDValue(SpellID) then return nil, 0; end
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
