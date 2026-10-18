--- ============================ HEADER ============================
-- Maps WoW binding strings (e.g. "SHIFT-3", "NUMPAD7", "BUTTON4") to Windows
-- virtual-key codes plus a modifier bitmask, so the desktop helper can replay
-- the exact input without knowing anything about WoW's binding names.

local addonName, MDBX = ...;

MDBX.MOD_SHIFT = 1;
MDBX.MOD_CTRL  = 2;
MDBX.MOD_ALT   = 4;

-- WoW key name -> Windows virtual-key code.
-- v1.3.3 full-coverage audit (Sep-2026): EVERY bindable WoW token must map
-- here, or the slot silently encodes EMPTY (WriteSlot: no VK → Paint 0,0,0)
-- and the companion "gets stuck" on that spell with zero diagnostics. The
-- previously-missing rows (lock keys, OEM aliases, mouse aliases) are
-- marked NEW — any future WoW binding token belongs in this table first.
local VK = {
  -- Editing / whitespace
  ["BACKSPACE"] = 0x08, ["TAB"] = 0x09, ["ENTER"] = 0x0D, ["ESCAPE"] = 0x1B,
  ["SPACE"] = 0x20, ["CAPSLOCK"] = 0x14,
  -- Navigation
  ["PAGEUP"] = 0x21, ["PAGEDOWN"] = 0x22, ["END"] = 0x23, ["HOME"] = 0x24,
  ["LEFT"] = 0x25, ["UP"] = 0x26, ["RIGHT"] = 0x27, ["DOWN"] = 0x28,
  ["INSERT"] = 0x2D, ["DELETE"] = 0x2E,
  -- NEW: lock / system keys (all bindable in WoW's keybind UI)
  ["NUMLOCK"] = 0x90, ["SCROLLLOCK"] = 0x91, ["PRINTSCREEN"] = 0x2C,
  ["PAUSE"] = 0x13, ["BREAK"] = 0x13, ["CLEAR"] = 0x0C,
  -- NEW: common aliases WoW/the overlay emit
  ["ESC"] = 0x1B, ["RETURN"] = 0x0D, ["SPACEBAR"] = 0x20, ["CAPS"] = 0x14,
  ["PGUP"] = 0x21, ["PGDN"] = 0x22, ["PAGEDN"] = 0x22,
  ["INS"] = 0x2D, ["DEL"] = 0x2E, ["PRTSC"] = 0x2C, ["PRTSCR"] = 0x2C,
  ["SCROLL"] = 0x91,
  -- OEM punctuation (US layout)
  ["-"] = 0xBD, ["="] = 0xBB, ["["] = 0xDB, ["]"] = 0xDD, ["\\"] = 0xDC,
  [";"] = 0xBA, ["'"] = 0xDE, [","] = 0xBC, ["."] = 0xBE, ["/"] = 0xBF, ["`"] = 0xC0,
  -- NEW: OEM aliases (WoW emits these spellings on some locales/layouts)
  ["MINUS"] = 0xBD, ["EQUALS"] = 0xBB, ["LBRACKET"] = 0xDB, ["RBRACKET"] = 0xDD,
  ["BACKSLASH"] = 0xDC, ["SEMICOLON"] = 0xBA, ["QUOTE"] = 0xDE, ["APOSTROPHE"] = 0xDE,
  ["COMMA"] = 0xBC, ["PERIOD"] = 0xBE, ["SLASH"] = 0xBF, ["TILDE"] = 0xC0,
  ["GRAVE"] = 0xC0,
  -- Numpad operators. WoW spells these out; it also renders them shortened
  -- ("N1") in HotKey text, which is why the Reader expands HotKey text back
  -- to the raw binding string before calling ParseBinding.
  ["NUMPADDIVIDE"] = 0x6F, ["NUMPADMULTIPLY"] = 0x6A, ["NUMPADMINUS"] = 0x6D,
  ["NUMPADPLUS"] = 0x6B, ["NUMPADDECIMAL"] = 0x6E,
  -- NEW: numpad aliases (NumLock-dependent spellings WoW can emit)
  ["NUMLOCKDIVIDE"] = 0x6F, ["NUMLOCKMULTIPLY"] = 0x6A, ["NUMLOCKMINUS"] = 0x6D,
  ["NUMLOCKPLUS"] = 0x6B, ["NUMLOCKDECIMAL"] = 0x6E, ["NUMLOCK0"] = 0x60,
  ["NUMLOCK1"] = 0x61, ["NUMLOCK2"] = 0x62, ["NUMLOCK3"] = 0x63,
  ["NUMLOCK4"] = 0x64, ["NUMLOCK5"] = 0x65, ["NUMLOCK6"] = 0x66,
  ["NUMLOCK7"] = 0x67, ["NUMLOCK8"] = 0x68, ["NUMLOCK9"] = 0x69,

  -- Mouse. Buttons reuse their real Windows virtual-key codes, which the
  -- keyboard can never produce, so the helper can tell the two apart from the
  -- code alone. The wheel has no VK code of its own, so it borrows two values
  -- Windows leaves undefined (0x07 and 0x0B).
  ["BUTTON1"] = 0x01,  -- left
  ["BUTTON2"] = 0x02,  -- right
  ["BUTTON3"] = 0x04,  -- middle
  ["BUTTON4"] = 0x05,  -- X1
  ["BUTTON5"] = 0x06,  -- X2
  ["MOUSEWHEELUP"] = 0x07,
  ["MOUSEWHEELDOWN"] = 0x0B,
  -- NEW: mouse aliases (buttons 6+ have no Windows VK: ParseBinding
  -- returns nil for them BY DESIGN — see below — but every alias for
  -- buttons 1-5 + wheel must resolve so user binds never go EMPTY).
  ["LEFTBUTTON"] = 0x01, ["RIGHTBUTTON"] = 0x02, ["MIDDLEBUTTON"] = 0x04,
  ["XBUTTON1"] = 0x05, ["XBUTTON2"] = 0x06,
  ["MB1"] = 0x01, ["MB2"] = 0x02, ["MB4"] = 0x05, ["MB5"] = 0x06,
  ["MOUSE1"] = 0x01, ["MOUSE2"] = 0x02, ["MOUSE3"] = 0x04,
  ["MOUSE4"] = 0x05, ["MOUSE5"] = 0x06,
  ["WHEELUP"] = 0x07, ["WHEELDOWN"] = 0x0B,
  ["MWHEELUP"] = 0x07, ["MWHEELDOWN"] = 0x0B,
  ["MWU"] = 0x07, ["MWD"] = 0x0B,
};

-- 0-9, A-Z
for i = 0, 9 do VK[tostring(i)] = 0x30 + i; end
for i = 0, 25 do VK[string.char(65 + i)] = 0x41 + i; end
-- Numpad digits
for i = 0, 9 do VK["NUMPAD" .. i] = 0x60 + i; end
-- F1..F24
for i = 1, 24 do VK["F" .. i] = 0x70 + (i - 1); end

--[[*
  * @function MDBX.SlotCommand
  * @desc Action slot (1-180) -> Blizzard binding command, or nil.
  * @param Slot number
  * @return string|nil
  *]]
-- SHARED SLOT -> COMMAND TABLE (A1.11). Reader.lua (GetKeybindForSpell) and
-- HotbarWatch.lua (BuildSlotBinding) both call this, so the two hot paths can
-- never drift apart again.
--
-- Cited layout: warcraft.wiki.gg/wiki/Action_slot, table "Action bar name |
-- Action button names | slots" (checked 2026-10-17 against 12.1.x, and
-- corroborated by warcraft.wiki.gg/wiki/Action_Bar):
--   Action Bar 1 (page 1)        ActionButton[1-12]          001-012
--   Action Bar 1 (page 2)        ActionButton[1-12]          013-024
--   Action Bar 4                 MultiBarRightButton[1-12]   025-036  MAB3
--   Action Bar 5                 MultiBarLeftButton[1-12]    037-048  MAB4
--   Action Bar 3                 MultiBarBottomRightButton   049-060  MAB2
--   Action Bar 2                 MultiBarBottomLeftButton    061-072  MAB1
--   bonus / stance / vehicle slots (paged onto the main bar buttons)
--                                                          073-144  (none)
--   Action Bar 6                 MultiBar5Button[1-12]       145-156  MAB5
--   Action Bar 7                 MultiBar6Button[1-12]       157-168  MAB6
--   Action Bar 8                 MultiBar7Button[1-12]       169-180  MAB7
--
-- 4.1.0 fixes versus the old inline tables: slots 13-24 are Action Bar 1 PAGE
-- TWO (the same physical ACTIONBUTTON keys), not MULTIACTIONBAR1; 073-144 have
-- no ACTIONBUTTON-family command at all (mapping them to MULTIACTIONBAR5 made
-- the bridge resolve a key that presses a DIFFERENT slot); 145-156 are the real
-- MultiBar5 block. The documented-correct rows 25-36/37-48/49-60/61-72 and
-- 157-168/169-180 are unchanged. Bonus/stance slots fall through to the Bars
-- texture map (identity) and the ACTIONBUTTON entry (stroke) instead.
local SlotCommands = {
  { To = 24,  Command = "ACTIONBUTTON" },          -- bars 1-2 share buttons
  { To = 36,  Command = "MULTIACTIONBAR3BUTTON" },
  { To = 48,  Command = "MULTIACTIONBAR4BUTTON" },
  { To = 60,  Command = "MULTIACTIONBAR2BUTTON" },
  { To = 72,  Command = "MULTIACTIONBAR1BUTTON" },
  { To = 144, Command = false },                   -- bonus/stance/vehicle
  { To = 156, Command = "MULTIACTIONBAR5BUTTON" },
  { To = 168, Command = "MULTIACTIONBAR6BUTTON" },
  { To = 180, Command = "MULTIACTIONBAR7BUTTON" },
};

function MDBX.SlotCommand (Slot)
  if type(Slot) ~= "number" then return nil; end
  Slot = math.floor(Slot);
  if Slot < 1 or Slot > 180 then return nil; end
  local Button = ((Slot - 1) % 12) + 1;
  for i = 1, #SlotCommands do
    local Row = SlotCommands[i];
    if Slot <= Row.To then
      if not Row.Command then return nil; end
      return Row.Command .. Button;
    end
  end
  return nil;
end

--[[*
  * @function MDBX.ParseBinding
  * @desc Translate a WoW binding string into a virtual-key code + modifier mask.
  * @param Binding string|nil e.g. "ALT-CTRL-SHIFT-F5", "SHIFT-BUTTON4"
  * @return number|nil VirtualKey, number Modifiers
  *]]
function MDBX.ParseBinding (Binding)
  if type(Binding) ~= "string" or Binding == "" then return nil, 0; end

  local Key = strupper(Binding);
  local Modifiers = 0;

  -- WoW emits modifiers in ALT-CTRL-SHIFT order, but strip them in a loop so
  -- that any order (and repeated prefixes) still parses.
  local Stripped = true;
  while Stripped do
    Stripped = false;
    if strsub(Key, 1, 4) == "ALT-" then
      Modifiers = bit.bor(Modifiers, MDBX.MOD_ALT); Key = strsub(Key, 5); Stripped = true;
    elseif strsub(Key, 1, 5) == "CTRL-" then
      Modifiers = bit.bor(Modifiers, MDBX.MOD_CTRL); Key = strsub(Key, 6); Stripped = true;
    elseif strsub(Key, 1, 6) == "SHIFT-" then
      Modifiers = bit.bor(Modifiers, MDBX.MOD_SHIFT); Key = strsub(Key, 7); Stripped = true;
    end
  end

  -- BUTTON6 and up have no SendInput equivalent; Windows only synthesises
  -- left/right/middle/X1/X2. Those need remapping in the mouse driver instead.
  -- v1.3.3 added MISS-TRANSPARENCY; 4.1.0 (A1.4) removed the per-MISS global
  -- write: this runs for every unmapped token on the hot path (a bound mouse
  -- button 6+ is a permanent miss) and each write allocated/touched a global
  -- table field the bridge never reads. Callers already see the nil VK and
  -- encode EMPTY; /mdb status prints MDBX.LastMissBinding for any writer that
  -- still sets it.
  local Found = VK[Key];
  return Found, Modifiers;
end
