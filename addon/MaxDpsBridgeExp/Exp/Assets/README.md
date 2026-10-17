# MaxDpsBridgeExp assets (placeholder)

This folder holds the committed `.tga` icons for the experimental addon,
plus this placeholder. **Do not hand-edit binaries here** — regenerate them
with `tools/fetch_assets.ps1` (dev-time only) and commit the results.

Expected names (64/128 variants): ``<name>-64.tga`` and ``<name>-128.tga``
for the toggle keys and chrome icons listed in `assets/LICENSES.md`.
`SetTexture` paths drop the extension, e.g.
`Interface\AddOns\MaxDpsBridgeExp\Exp\Assets\main-64`.

## Runtime resolver

`Exp/Overlay.lua` (`IconFor`) resolves each pill to
`Interface\AddOns\MaxDpsBridgeExp\Exp\Assets\<keylower>-64`, i.e. the 64 px
variant of the matching file written by `tools/fetch_assets.ps1`
(`Main` -> `main-64.tga`, `SelfHeal` -> `selfheal-64.tga`). The `-128`
variant is optional hi-dpi art; the resolver never requests it and a missing
`-64` is not an error (the fallback below covers it).

Contract for every file (validated by the fetch script; see
`assets/LICENSES.md`): image type 2 (uncompressed), 32-bit BGRA, square
power-of-two 64/128/256 (never larger than 256), top-left origin.

## Missing-texture fallback

If an expected TGA is absent (fresh checkout, failed fetch, or a build that
skipped generation) the addon must **never error**. Code resolves each icon
to the game texture first and falls back to a stock Blizzard texture when
the file is missing, e.g.:

- `Interface\Icons\INV_Misc_QuestionMark`
- `Interface\Buttons\UI-Panel-Button-Up`
- `Interface\Common\UI-Panel-BarberShop`

The fallback is intentionally ugly so a missing asset is visible in-game but
never blocks the overlay, Settings page, or slash commands.
