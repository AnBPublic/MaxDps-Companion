# Slice 4 readability pass — result (2026-10-04)

Display only. No wire/protocol change. Files: `addon/MaxDpsBridgeExp/Exp/Config.lua`, `Exp/Overlay.lua`.

- Fonts/colors: headers gold `#FFD15C` (`GOLD`), body near-white `TEXT`, muted `GameFontDisableSmall` `MUTED`; pill ON `ACCENT` `#FFC747` + dark `ON_TEXT`, OFF dim + brighter `OFF_TEXT = { 0.74, 0.74, 0.77 }` (Config.lua:45-47).
- `Style()` now forces OUTLINE + `1,-1` shadow on every custom string (Config.lua:110); Overlay fallback adds the same (Overlay.lua:206).
- Row hover brightness shift: `SetBackdropColor(HOVER_BG...)` on Enter, `ROW_BG` on Leave (Config.lua:321).
- Tooltips: short + full + effect + `Bind: <key>`/`Bind: unbound` + `Click to toggle` (Config.lua:330; Overlay.lua:427).
- Empty states: `"(no profiles available)"` (Config.lua:510), `"(no bindings registered)"` (Config.lua:561), `"profile: (none)"` (Overlay.lua:611).
- Search: `"Search toggles..."` placeholder + clear `X` button (Config.lua:656).

VERIFY: `luac -p` 14/14 clean; `lua tests/secret_harness.lua` → `RESULT: 275 passed, 0 failed`; mirrored live via `tools/install-addon-exp.ps1` (stable bridge untouched). LIVE screenshot OWED.
