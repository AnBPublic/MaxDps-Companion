# Third-party asset licenses (MaxDpsBridgeExp)

Per-asset provenance for the experimental addon's TGA icon set and the
companion-only fonts. Regenerated/checked by `tools/fetch_assets.ps1`
(dev-time only; the build and the installed addon never download anything).

Every upstream file is pinned to a tag/commit and its exact bytes are
enforced by a SHA256 check inside `tools/fetch_assets.ps1`. If a hash does
not match the pin the fetch is rejected and nothing is written.

## Icon SVG sources (rasterized to 64/128 PNG then TGA)

| Asset set | Source URL (pinned) | License | Pin (tag / commit) |
|---|---|---|---|
| Lucide icon set | https://github.com/lucide-icons/lucide/tree/v0.469.0/icons | ISC | tag `v0.469.0`; SHA256 pinned in script |
| Phosphor icon set | https://github.com/phosphor-icons/core/tree/v2.1.1/assets/regular | MIT | tag `v2.1.1`; SHA256 pinned in script |

Per-image mapping (Lucide unless noted; one file per toggle key + chrome):

- Keys: `Main`, `Offensive`, `Defensive`, `Consumable`, `Trinket`,
  `Interrupt`, `Mobility`, `SelfHeal`, `Solo`, `OOC`, `AutoTarget`,
  `AutoInteract`, `TTK`, `CC`.
- Chrome: `lock`, `unlock`, `gear`, `pause`, `play`, `stop`, `crosshair`
  (Phosphor `regular` is the documented fallback for any Lucide name that
  does not exist at the pin).

## Fonts (companion only)

| Asset | Source URL (pinned) | License | Pin (tag / commit) |
|---|---|---|---|
| Inter (variable/static TTF) | https://github.com/rsms/inter/tree/v4.1 | SIL OFL 1.1 | tag `v4.1`; SHA256 pinned in script |
| JetBrains Mono (TTF) | https://github.com/JetBrains/JetBrainsMono/tree/v2.304 | SIL OFL 1.1 | tag `v2.304`; SHA256 pinned in script |

Fonts are embedded by the companion (WinForms) only. They are **not** shipped
into the addon and are not downloaded at runtime. The addon uses WoW's own
`GameFont*` / `NumberFont*` faces.

## Spell icons

Spell icons are **never bundled**. They are read at runtime from the game
client via the public API (`C_Spell.GetSpellTexture`, group/unit APIs) and
rendered from WoW's own texture paths. No Blizzard art is committed here.

## TGA constraints for `Exp/Assets/*.tga`

- Image type 2, **uncompressed** true-color.
- **32-bit** BGR(A), 8 attribute (alpha) bits per pixel.
- Dimensions power-of-two: 64, 128, 256; never larger than 256.
- **Top-left origin** (TGA image descriptor bit 0x20 set).
- `SetTexture` paths drop the extension, e.g.
  `Interface\AddOns\MaxDpsBridgeExp\Exp\Assets\main-64` (the runtime resolver
  requests the `-64` variant of `<keylower>`; `-128` is optional hi-dpi art).

If an expected TGA is missing at runtime the addon falls back to a stock
Blizzard texture (see `Exp/Assets/README.md`); it must never error because an
art file is absent.
