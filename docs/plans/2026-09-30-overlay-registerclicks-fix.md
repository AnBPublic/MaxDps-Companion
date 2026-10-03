# Overlay RegisterForClicks fix 2026-09-30
- Culprit: addon/MaxDpsBridge/Panel.lua:545 O:RegisterForClicks on Frame (Button-only method) aborts BuildOverlay, Overlay stays nil, repeats on ADDON_LOADED/PLAYER_LOGIN.
- Fix: delete line 545; replace lines 558-560 OnClick with OnMouseUp right-click reset:
```lua
O:SetScript("OnMouseUp", function (_, Mouse)
  if Mouse == "RightButton" then ResetOverlayPosition(); end
end);
```
- Keep: Frame type, SetMovable, ClampedToScreen, RegisterForDrag LeftButton, OnDragStart/Stop position save, toggle Buttons (line 509) + minimap Button untouched, pixel bridge (Bridge/Bars/Reader/Toggles) untouched, no PROTOCOL change.
- Docs same pass: HANDOVER.md status + ARCHITECTURE.md Panel section note Frame+OnMouseUp.
- Verify: luac -p, secret_harness, ability_audit, dotnet build/test. Live retail OWED: /reload, /mdb overlay on no error, drag persists, right-click resets, toggles blank slots.
