MaxDPS Macro Support v1.18.1
Author: Daxomault
License: MIT

Companion addon for MaxDPS Rotation Helper.

Caveat:
- Some code in this addon was written with assistance from ChatGPT.
- This addon does not include MaxDPS Rotation Helper or any MaxDPS rotation modules.
- This addon does not cast spells, press buttons, run macros, or automate gameplay. It only helps MaxDPS find and glow actionbar macro buttons.

What it does:
- Helps MaxDPS glow macro buttons when the recommended spell is inside /cast, /castsequence, /castrandom, /use, /userandom, etc.
- Scans only macros currently on hotbars/action bars.
- Supports saved macros and secure macrotext buttons exposed by some actionbar addons.
- Uses cached parsing and debounced hotbar rescans for performance.
- Supports manual spell-to-button bindings as a fallback.
- Clears stale glows when macro buttons are moved.

New in v1.18.1:
- Added dual Retail interface metadata for 12.0.7 and 12.1.0.
- Updated /mdms status compatibility text to recognize both 120007 and 120100 as verified.
- No macro scanning or glow behavior changes.

New in v1.17.0:
- Added an AddOns options panel.
- Improved /mdms status output with clearer state, warnings, timing, cache, mapping, and glow information.
- Added /mdms clearglows.
- Added compatibility warnings when MaxDPS or hooks are unavailable.
- Added /mdms debug on|off.
- Debug-heavy commands now require debug mode.
- Added CHANGELOG.txt and LICENSE.
- Changed Author metadata to Daxomault.

Primary commands:
/mdms help
/mdms options
/mdms status
/mdms scan
/mdms clearglows
/mdms bind <spellID>
/mdms bindslot <spellID> <action slot>
/mdms bindmacro <spellID> <macro name>
/mdms bindings
/mdms unbind <spellID>
/mdms unbind all
/mdms autoscan off
/mdms autoscan on
/mdms perf safe
/mdms perf normal
/mdms debug on
/mdms debug off

Debug commands:
/mdms debug on
/mdms macros
/mdms rawmacros
/mdms hover
/mdms dump

Recommended test after installing:
1. /reload
2. /mdms loaded
3. /mdms scan
4. /mdms status

If you feel lag while editing macros:
/mdms perf safe

Then manually refresh after changing hotbar macros:
/mdms scan


Retail Client Compatibility
- Verified package metadata for World of Warcraft Retail 12.0.7 and 12.1.0.
- TOC interfaces: 120007, 120100.
- This companion addon still requires MaxDPS Rotation Helper to be installed and enabled. If MaxDPS itself is marked out of date for your client, you may need to enable Load out of date AddOns for MaxDPS until it is updated.
