--- ============================ HEADER ============================
-- Maps WoW binding strings (e.g. "SHIFT-3", "NUMPAD7", "BUTTON4") to Windows
-- virtual-key codes plus a modifier bitmask, so the desktop helper can replay
-- the exact input without knowing anything about WoW's binding names.

local addonName, MDB = ...;

MDB.MOD_SHIFT = 1;
MDB.MOD_CTRL  = 2;
MDB.MOD_ALT   = 4;

-- WoW key name -> Windows virtual-key code.
local VK = {
  -- Editing / whitespace
  ["BACKSPACE"] = 0x08, ["TAB"] = 0x09, ["ENTER"] = 0x0D, ["ESCAPE"] = 0x1B,
  ["SPACE"] = 0x20, ["CAPSLOCK"] = 0x14,
  -- Navigation
  ["PAGEUP"] = 0x21, ["PAGEDOWN"] = 0x22, ["END"] = 0x23, ["HOME"] = 0x24,
  ["LEFT"] = 0x25, ["UP"] = 0x26, ["RIGHT"] = 0x27, ["DOWN"] = 0x28,
  ["INSERT"] = 0x2D, ["DELETE"] = 0x2E,
  -- OEM punctuation (US layout)
  ["-"] = 0xBD, ["="] = 0xBB, ["["] = 0xDB, ["]"] = 0xDD, ["\\"] = 0xDC,
  [";"] = 0xBA, ["'"] = 0xDE, [","] = 0xBC, ["."] = 0xBE, ["/"] = 0xBF, ["`"] = 0xC0,
  -- Numpad operators. WoW spells these out; it also renders them shortened
  -- ("N1") in HotKey text, which is why the Reader expands HotKey text back
  -- to the raw binding string before calling ParseBinding.
  ["NUMPADDIVIDE"] = 0x6F, ["NUMPADMULTIPLY"] = 0x6A, ["NUMPADMINUS"] = 0x6D,
  ["NUMPADPLUS"] = 0x6B, ["NUMPADDECIMAL"] = 0x6E,

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
};

-- 0-9, A-Z
for i = 0, 9 do VK[tostring(i)] = 0x30 + i; end
for i = 0, 25 do VK[string.char(65 + i)] = 0x41 + i; end
-- Numpad digits
for i = 0, 9 do VK["NUMPAD" .. i] = 0x60 + i; end
-- F1..F24
for i = 1, 24 do VK["F" .. i] = 0x70 + (i - 1); end

--[[*
  * @function MDB.ParseBinding
  * @desc Translate a WoW binding string into a virtual-key code + modifier mask.
  * @param Binding string|nil e.g. "ALT-CTRL-SHIFT-F5", "SHIFT-BUTTON4"
  * @return number|nil VirtualKey, number Modifiers
  *]]
function MDB.ParseBinding (Binding)
  if type(Binding) ~= "string" or Binding == "" then return nil, 0; end

  local Key = strupper(Binding);
  local Modifiers = 0;

  -- WoW emits modifiers in ALT-CTRL-SHIFT order, but strip them in a loop so
  -- that any order (and repeated prefixes) still parses.
  local Stripped = true;
  while Stripped do
    Stripped = false;
    if strsub(Key, 1, 4) == "ALT-" then
      Modifiers = bit.bor(Modifiers, MDB.MOD_ALT); Key = strsub(Key, 5); Stripped = true;
    elseif strsub(Key, 1, 5) == "CTRL-" then
      Modifiers = bit.bor(Modifiers, MDB.MOD_CTRL); Key = strsub(Key, 6); Stripped = true;
    elseif strsub(Key, 1, 6) == "SHIFT-" then
      Modifiers = bit.bor(Modifiers, MDB.MOD_SHIFT); Key = strsub(Key, 7); Stripped = true;
    end
  end

  -- BUTTON6 and up have no SendInput equivalent; Windows only synthesises
  -- left/right/middle/X1/X2. Those need remapping in the mouse driver instead.
  return VK[Key], Modifiers;
end
