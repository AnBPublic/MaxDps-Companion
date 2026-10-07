--- ============================ HEADER ============================
-- MaxDpsBridgeExp - profile store (v1 schema).
--
-- Three named stores: Global, Spec and Talent. Each store captures the 14
-- in-game toggles plus the per-spec slider values. The active store is a
-- pointer only (DB.active.mode); nothing is auto-applied at login so a
-- profile switch can never silently overwrite a deliberate in-game toggle.
--
-- Export/import is a single versioned line:
--     MDBX1|Global|t:Main:1,t:OOC:0,s:MinTtkSec:0
-- An import is REJECTED when the version token does not match, so a future
-- schema can never be half-read by an older build.
--
-- No game API, no frames, no protected calls: pure table work over
-- MaxDpsBridgeExpDB.Toggles / DB.profiles / DB.sliders.

local addonName, MDBX = ...;

local P = {};
MDBX.Profiles = P;

P.SCHEMA = 1;
P.MODES = { "Global", "Spec", "Talent" };

-- Per-spec slider definitions. The bridge/companion consume the values as
-- advisory tuning; ranges are deliberately conservative and clamps are
-- applied on every write. Order is the render order in Settings > Rotation.
local SLIDER_DEFS = {
  { Key = "MinTtkSec",      Label = "Min TTK (s)",       Min = 0, Max = 30, Step = 1, Default = 0 },
  { Key = "DefensiveHp",    Label = "Defensive HP %",    Min = 0, Max = 100, Step = 5, Default = 0 },
  { Key = "SelfHealHp",     Label = "Self-heal HP %",    Min = 0, Max = 100, Step = 5, Default = 0 },
  { Key = "SoloMinorHp",    Label = "Solo minor HP %",   Min = 0, Max = 100, Step = 5, Default = 75 },
  { Key = "SoloMajorHp",    Label = "Solo major HP %",   Min = 0, Max = 100, Step = 5, Default = 50 },
  { Key = "SoloImmunityHp", Label = "Solo immunity HP %", Min = 0, Max = 100, Step = 5, Default = 30 },
};
P.SliderDefs = SLIDER_DEFS;

local function DB ()
  if type(_G.MaxDpsBridgeExpDB) ~= "table" then _G.MaxDpsBridgeExpDB = {}; end;
  return _G.MaxDpsBridgeExpDB;
end

--- Per-character override table (SavedVariablesPerCharacter). The override
-- wins over the account-wide DB.active.mode while set; an explicit profile
-- command clears it.
local function CharDB ()
  if type(_G.MaxDpsBridgeExpCharDB) ~= "table" then _G.MaxDpsBridgeExpCharDB = {}; end;
  return _G.MaxDpsBridgeExpCharDB;
end

local function ValidMode (Mode)
  if type(Mode) ~= "string" then return nil; end;
  local Lower = Mode:lower();
  for i = 1, #P.MODES do
    if P.MODES[i]:lower() == Lower then return P.MODES[i]; end;
  end;
  return nil;
end
P.ValidMode = ValidMode;

local function SliderDef (Key)
  if type(Key) ~= "string" then return nil; end;
  local Lower = Key:lower();
  for i = 1, #SLIDER_DEFS do
    if SLIDER_DEFS[i].Key:lower() == Lower then return SLIDER_DEFS[i]; end;
  end;
  return nil;
end

local function Clamp (Value, Lo, Hi)
  if type(Value) ~= "number" or Value ~= Value then return nil; end;
  if Value < Lo then return Lo; end;
  if Value > Hi then return Hi; end;
  return Value;
end

--- 14 toggle keys: the canonical 13 plus the CC appendix (Ext3 bit 13).
local function ToggleKeys ()
  local Keys = {};
  if MDBX.Toggles and MDBX.Toggles.Keys then
    local K = MDBX.Toggles.Keys();
    for i = 1, #K do Keys[#Keys + 1] = K[i]; end;
  end;
  Keys[#Keys + 1] = "CC";   -- Ext3 bit 13 appendix, always 14th
  return Keys;
end
P.ToggleKeys = ToggleKeys;

function P.Ensure ()
  local D = DB();
  D.v = P.SCHEMA;
  if type(D.profiles) ~= "table" then D.profiles = {}; end;
  if type(D.active) ~= "table" then D.active = { mode = "Global" }; end;
  if type(D.sliders) ~= "table" then D.sliders = {}; end;
  if ValidMode(D.active.mode) == nil then D.active.mode = "Global"; end;
  for i = 1, #P.MODES do
    local Name = P.MODES[i];
    if type(D.profiles[Name]) ~= "table" then D.profiles[Name] = {}; end;
    if type(D.profiles[Name].toggles) ~= "table" then D.profiles[Name].toggles = {}; end;
    if type(D.profiles[Name].sliders) ~= "table" then D.profiles[Name].sliders = {}; end;
  end;
  return D;
end

function P.ActiveMode ()
  local D = P.Ensure();
  local Override = ValidMode(CharDB().profileOverride);
  if Override then return Override; end;
  return ValidMode(D.active.mode) or "Global";
end

function P.GetProfileOverride ()
  return ValidMode(CharDB().profileOverride);
end

function P.SetProfileOverride (Mode)
  Mode = ValidMode(Mode);
  CharDB().profileOverride = Mode;
  return Mode;
end

function P.GetSlider (Key)
  local Def = SliderDef(Key);
  if not Def then return nil; end;
  local D = P.Ensure();
  local Value = Clamp(D.sliders[Def.Key], Def.Min, Def.Max);
  if Value == nil then return Def.Default; end;
  return Value;
end

function P.SetSlider (Key, Value)
  local Def = SliderDef(Key);
  if not Def then return nil; end;
  local D = P.Ensure();
  local V = Clamp(tonumber(Value), Def.Min, Def.Max);
  if V == nil then V = Def.Default; end;
  D.sliders[Def.Key] = V;
  return V;
end

--- Copy the CURRENT effective toggles + sliders into a named store.
function P.Capture (Mode)
  Mode = ValidMode(Mode) or P.ActiveMode();
  local D = P.Ensure();
  local Profile = D.profiles[Mode];
  local Keys = ToggleKeys();
  local Toggles = {};
  for i = 1, #Keys do
    Toggles[Keys[i]] = (MDBX.Toggles and MDBX.Toggles.Get and MDBX.Toggles.Get(Keys[i]) ~= false) and true or false;
  end;
  Profile.toggles = Toggles;
  local Sliders = {};
  for i = 1, #SLIDER_DEFS do
    Sliders[SLIDER_DEFS[i].Key] = P.GetSlider(SLIDER_DEFS[i].Key);
  end;
  Profile.sliders = Sliders;
  return Profile;
end

--- Push a named store back onto the live in-game state (toggles + sliders).
-- An absent toggle key in the store is left untouched, so a store written by
-- an older build cannot blank a key this build added.
function P.Apply (Mode)
  Mode = ValidMode(Mode) or P.ActiveMode();
  local D = P.Ensure();
  local Profile = D.profiles[Mode];
  local Keys = ToggleKeys();
  for i = 1, #Keys do
    local Value = Profile.toggles[Keys[i]];
    if Value ~= nil then
      if MDBX.Toggles and MDBX.Toggles.Set then MDBX.Toggles.Set(Keys[i], Value); end;
    end;
  end;
  for i = 1, #SLIDER_DEFS do
    local Key = SLIDER_DEFS[i].Key;
    local Value = Profile.sliders[Key];
    if type(Value) == "number" then D.sliders[Key] = Clamp(Value, SLIDER_DEFS[i].Min, SLIDER_DEFS[i].Max); end;
  end;
  D.active.mode = Mode;
  CharDB().profileOverride = nil;
  if MDBX.Overlay and MDBX.Overlay.SetDirty then MDBX.Overlay.SetDirty(); end;
  return Profile;
end

--- Switch the active pointer without touching live state (Settings dropdown).
function P.SetActiveMode (Mode)
  Mode = ValidMode(Mode);
  if not Mode then return nil; end;
  DB().active.mode = Mode;
  return Mode;
end

--- Versioned single-line export of one store (default: the active store).
function P.Export (Mode)
  Mode = ValidMode(Mode) or P.ActiveMode();
  local Profile = P.Capture(Mode);
  local Parts = {};
  local Keys = ToggleKeys();
  for i = 1, #Keys do
    Parts[#Parts + 1] = "t:" .. Keys[i] .. ":" .. (Profile.toggles[Keys[i]] and "1" or "0");
  end;
  for i = 1, #SLIDER_DEFS do
    local Key = SLIDER_DEFS[i].Key;
    Parts[#Parts + 1] = "s:" .. Key .. ":" .. tostring(Profile.sliders[Key] or SLIDER_DEFS[i].Default);
  end;
  return ("MDBX%d|%s|%s"):format(P.SCHEMA, Mode, table.concat(Parts, ","));
end

--- Parse a versioned export line into a store. Returns ok, message.
-- A version mismatch is REJECTED (never partially applied). Anything the
-- parser does not understand is skipped rather than aborting the import.
function P.Import (Text, Mode)
  if type(Text) ~= "string" or Text == "" then return false, "empty import"; end;
  local Version, ImportMode, Body = Text:match("^MDBX(%d+)|([%a]+)|(.*)$");
  if Version == nil then return false, "not an MDBX export string"; end;
  Version = tonumber(Version);
  if Version ~= P.SCHEMA then
    return false, ("schema mismatch: got v%s, expected v%d"):format(tostring(Version), P.SCHEMA);
  end;
  Mode = ValidMode(Mode) or ValidMode(ImportMode) or P.ActiveMode();
  local D = P.Ensure();
  local Profile = D.profiles[Mode];
  Profile.toggles = Profile.toggles or {};
  Profile.sliders = Profile.sliders or {};
  for Entry in Body:gmatch("[^,]+") do
    local Kind, Key, Value = Entry:match("^([ts]):([^:]+):(.+)$");
    if Kind == "t" then
      if MDBX.Toggles and MDBX.Toggles.Get(Key) ~= nil then
        Profile.toggles[Key] = (Value == "1");
      end;
    elseif Kind == "s" then
      local Def = SliderDef(Key);
      if Def then
        local N = tonumber(Value);
        if N ~= nil then Profile.sliders[Def.Key] = Clamp(N, Def.Min, Def.Max); end;
      end;
    end;
  end;
  D.active.mode = Mode;
  return true, ("imported into %s"):format(Mode);
end
