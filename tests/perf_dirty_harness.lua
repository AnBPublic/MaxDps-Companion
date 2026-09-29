-- Stream 2 (perf) addon check: proves the Bridge.lua slot-intent dirty flag
-- actually caches. It reuses the secret harness's full fake environment (run
-- it first, unchanged) and then drives the real OnUpdate with an unchanged
-- input (must NOT rescan) and with a changed suggestion (must rescan).
-- Run from the repo root:  lua tests/perf_dirty_harness.lua
--
-- This is a NEW file: tests/secret_harness.lua is a shared helper and is not
-- modified. The harness exits non-zero on its own failures, so a green run
-- here also means the 186 existing checks passed.

dofile("tests/secret_harness.lua")

local PASS, FAIL = 0, 0
local function check (Name, Cond)
  if Cond then PASS = PASS + 1; print("PASS: " .. Name)
  else FAIL = FAIL + 1; print("FAIL: " .. Name) end
end

local MDB = _G.MaxDpsBridge
local Strip = _G.MaxDpsBridge_Block
assert(MDB and Strip, "bridge did not boot")
local Update = Strip._scripts.OnUpdate
assert(type(Update) == "function", "OnUpdate not wired")
assert(type(MDB.FrameKey) == "function", "FrameKey missing")

local function Scans () return MDB._SlotScans or 0 end

-- First tick after the harness (state may still be dirty from its last change).
Update(Strip, 0.06)
local afterFirst = Scans()

-- No input changed: the second tick must reuse the cached slot scan.
Update(Strip, 0.06)
local afterSecond = Scans()
check("unchanged tick reuses the cached slot scan (no rescan)", afterSecond == afterFirst)
check("at least one full scan has run", afterFirst >= 1)

-- The key must still notice a suggestion change.
MaxDps.Spell = 99999
MDB._BindCache = {}
Update(Strip, 0.06)
local afterChange = Scans()
check("changed suggestion forces a rescan", afterChange > afterSecond)

-- A change can take one more tick to settle (resolving a new spell populates
-- the binding-miss cache, itself part of the key). Once settled, a further
-- unchanged tick must reuse the cache.
Update(Strip, 0.06)
Update(Strip, 0.06)
local settled = Scans()
Update(Strip, 0.06)
check("stable tick after a change is cached again", Scans() == settled)

-- A toggle flip must also invalidate the cache (Solo gate / TTK are in the key).
MaxDpsBridgeDB.Toggles.TTK = false
Update(Strip, 0.06)
check("toggle change forces a rescan", Scans() > settled)
MaxDpsBridgeDB.Toggles.TTK = true

print(string.format("DIRTY RESULT: %d passed, %d failed", PASS, FAIL))
if FAIL > 0 then os.exit(1) end
