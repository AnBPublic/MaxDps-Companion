--- ============================ HEADER ============================
-- Maps WoW binding strings (e.g. "SHIFT-3", "NUMPAD7", "BUTTON4") to Windows
-- virtual-key codes plus a modifier bitmask, so the desktop helper can replay
-- the exact input without knowing anything about WoW's binding names.

local addonName, MDB = ...;

MDB.MOD_SHIFT = 1;
MDB.MOD_CTRL  = 2;
MDB.MOD_ALT   = 4;

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
  -- v1.3.3: MISS-TRANSPARENCY — a nil VK used to encode EMPTY with no trace
  -- (the "gets stuck" symptom). MDB.LastMissBinding records the token so
  -- /mdb status can surface it; writers must never swallow the miss.
  local Found = VK[Key];
  if Found == nil then MDB.LastMissBinding = Binding; end
  return Found, Modifiers;
end
