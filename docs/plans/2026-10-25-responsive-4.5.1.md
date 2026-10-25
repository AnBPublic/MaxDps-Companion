# 4.5.1 "Voidlight" - live resize, wrapped log lines, readable changelog

Follow-up to `2026-10-25-midnight-ui-4.5.0.md`. UI-only, no wire change, no
addon change (the bridge stays 4.5.0).

## Problem

1. The Exp Details disclosure computed its height once, at expansion time. A
   window resize afterwards left the card overflowing or clipping its own
   content, and the changelog text was cut.
2. `RollingLogView` rendered every entry with `TextWrapping=NoWrap` +
   `TextTrimming=CharacterEllipsis`, so long status/spec/bridge lines ended in
   an ellipsis and the rest was unreachable.
3. The classic shell kept two-up toggle rows and two console cards at any width,
   squeezing both segments below ~560 DIP.
4. Buttons only followed the width tier in type, not in height.

## Change

- **`ExpConsoleView`**: `SizeChanged` re-clamps the open disclosure
  (`ExpandedHeight()` counts the card's inner height, reserves ~164 DIP for the
  chrome above plus the log), and the Details body is the single scroller. A
  slim amethyst thumb (`_detailsThumb`) tracks extent/viewport because WinUI's
  own scrollbar is hover-only; it hides when the content fits.
- **`RollingLogView`**: a stamped entry is a two-column grid - fixed `HH:mm:ss`
  column plus a wrapping body - so continuation lines align under the message.
  The empty state wraps too. Logs stay height-capped in the classic console
  (status 220 / history 180 DIP).
- **`ResponsiveColumns`** (new panel in `Ui/Foundation/Layout.cs`): equal
  columns while wide, a single stacked column below `StackWidth`. Used by
  `ToggleRowPanel` (< 500 DIP) and the console body, which switches its
  two-column grid to two equal star rows below 560 DIP and scrolls when the
  stacked cards need more height.
- **`ChamferButton.ApplyScale`**: follows the tier height (42 -> 52 DIP) and
  padding as well as the type step (compact buttons such as Exp Calibrate are
  untouched because they are not tiered).
- **`ReleaseNotes.EarlierSummary`** caps the history tail at four releases plus
  an ellipsis so the line fits on one row at the default width.

## Verification

Build 0/0, `dotnet test` (1096), Lua harness 409, `luac -p` 15/0, ability audit
clean, `--ui-smoke-test` PASS on both shells, snapshots at 520/660/1100 DIP plus
a live resize capture (650x700 -> 825x1150 with Details open) proving the
re-clamp, the thumb, the wrapped log and the uncut changelog. Live retail stays
OWED (`docs/TESTING.md` §3r).
