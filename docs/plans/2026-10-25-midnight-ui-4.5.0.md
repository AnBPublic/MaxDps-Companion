# 4.5.0 "Voidlight" - Midnight palette, gradient surfaces, no title-bar band

Status: approved by the human request (2026-10-25). No wire, addon-logic or
engine change.

## Goal

1. Remove the white band above the title bar.
2. Repaint the Windows client in a World of Warcraft: Midnight palette
   (void violet, amethyst, Sunwell gold, moonlit silver) instead of the
   "Ethereal Glass x brass" greys/brass.
3. Use gradients (background, title bar, buttons, cards) instead of flat fills.
4. Put a changelog in the app's **Details** disclosure and make a changelog
   entry mandatory for every future release (enforced by a test + AGENTS.md).

## Root cause of the white band

`OverlappedPresenter.SetBorderAndTitleBar(false, false)` clears the caption,
then `presenter.IsResizable = true` re-applies `WS_THICKFRAME`. The window keeps
a ~8 DIP non-client resize frame, the default `WM_NCCALCSIZE` handler insets the
client area by it, and DWM paints the top edge of that frame in the **system**
(light) frame colour - `#F3F3F3` on the capture. Measured on the 4.4.0 build:
650x702 px window, 632x684 px XAML root, 9 px of frame on every side.

Fix: answer `WM_NCCALCSIZE` (wParam = TRUE) in the existing `Win32Host`
subclass without calling the default proc, so the client area is the whole
window. `WS_THICKFRAME` stays, so the existing `WM_NCHITTEST` edge grip keeps
resizing. Also switch DWM to immersive dark mode so a residual frame/border can
never render light again.

## Stock-chrome leak (found while repainting)

`UiClickableStyle` declared `TargetType="controls:UiClickable"`, but WinUI
resolves an **internal** subclass as its `Button` base and rejects the style
("cannot apply a Style with TargetType ... to an object of type Button"). The
throw was swallowed, so every owner-drawn control kept the stock grey Button
fill behind its content (visible as a box around `Details` and on the console
nav items). The style now targets `Button` and each concrete subclass applies it
from its own constructor.

## Palette (Midnight)

| Token | Hex | Role |
| :--- | :--- | :--- |
| `Background` | `#0A0714` | void-black ground (violet undertone) |
| `Surface` | `#141022` | cards |
| `SurfaceElevated` | `#1D1735` | raised rows, hover |
| `SurfaceShell` | `#110C1F` | title bar base |
| `Border` | `#2B2244` | quiet keyline |
| `TextPrimary` | `#F3EEFB` | moonlit silver |
| `TextSecondary` | `#B3A9CB` | |
| `TextMuted` | `#7C7294` | |
| `Accent` | `#A78BFA` | Midnight amethyst (text/ticks/focus) |
| `AccentStrong` | `#6D38E0` | gradient bottoms |
| `AccentSoft` | `#DDD0FF` | hover text |
| `Success` | `#4FD6A8` | Start fill base |
| `Warning` | `#F0C05A` | Sunwell gold |
| `Danger` | `#D9455E` | Stop fill base |
| `Info` | `#8CB8FF` | arcane blue |

Exp shell tokens move to the same family (void ground, amethyst accent) so the
in-game-config shell and the classic shell can never drift apart again.

## Gradients

- **Ground**: 155 deg `#171029 -> #0A0714` base with a 25 deg violet glow
  (`#3A1F73` at 20 % alpha, fading out) - both shells.
- **Title bar**: vertical `#1B1333 -> #110C1F`, 1 px amethyst hairline below.
- **Primary / Danger / accent buttons**: vertical ramp derived from the fill
  colour (`Lighten 10 %` at the top, `Darken 28 %` at the bottom); hover lifts
  the ramp 6 %, press darkens 10 %.
- **Ghost buttons**: vertical `#1E1736 -> #141022` (hover `#251D42 -> #1A1330`).
- **Status card**: vertical `#171126 -> #120D1F` core inside the existing
  white/4 bezel; top-edge highlight unchanged.
- **Status pill**: vertical `#1E1733 -> #151026`, amethyst 22 % ring.

No animation is added; the existing press/hover physics stay as they are.

## Changelog in Details

`Ui/Foundation/ReleaseNotes.cs` holds newest-first `Release` entries
(headline + highlights). The Exp Details disclosure renders the current
release's highlights plus an `Earlier:` line.

The disclosure opens at **244 DIP** and is clamped so the log always keeps a
**220 DIP** reserve; the disclosure body is the only scroller, so small windows
scroll the changelog instead of clipping it mid-line. The disclosure still has
a fixed (computed) height, so the 250 ms status pump can never reflow the card.

`docs/CHANGELOG.md` is the canonical long-form history. `ReleaseNotesTests`
fails when `Native.AppVersion` and `ReleaseNotes.Current.Version` disagree,
when the codename differs, or when `docs/CHANGELOG.md` has no heading for the
current version - so a release cannot be cut without notes. `AGENTS.md` records
the rule for future releases.

## Verification bar

`dotnet build -c Release` (0/0), `dotnet test -c Release`,
`lua tests/secret_harness.lua`, `luac -p` addon, `pwsh tools/ability_audit.ps1`,
`--ui-smoke-test` + `--ui-snapshot`, live window capture (PrintWindow) to prove
the band is gone and the gradients render. Live retail stays OWED.
