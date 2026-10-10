# Spec: MaxDpsBridgeExp, in-game-config fork (experimental) — 2026-10-03
Status: approved. Route L (architect spec → implement → reviewer). Source: facts pack 2026-10-03 + architect pass.

## 1) Decision
- Fork, don't refactor. Copy bridge into `addon/MaxDpsBridgeExp/`. Stable addon, protocol v5, KeySender, vendor untouched.
- Mutually exclusive: Exp checks `C_AddOns.IsAddOnLoaded("MaxDpsBridge")` at ADDON_LOADED; if stable loaded, Exp prints one warning and stays inert (no pixel frame, no slash).
- Addon owns toggles (ADDON-WINS, SlotAllowed single gate). Companion Exp mode: no toggles, mirrors Ext3 mask cells 40-42 fail-closed.
- Companion stays one exe: runtime flag `InGameConfigMode` (settings.ini + --exp), WinForms adaptation of RICE layout; no Avalonia rewrite this pass.
- RICE replica UX: top pills APP/SOURCE/GAME PICTURE/FRAMES/ROTATION, tabs Pause/Rotation/Binds/Settings/Console/Debug, per-spec sliders, console ring buffer 200 lines.

## 2) File layout + load order
- `addon/MaxDpsBridgeExp/MaxDpsBridgeExp.toc` (## Interface: 120100, ## Dependencies: MaxDps, ## SavedVariables: MaxDpsBridgeExpDB, ## SavedVariablesPerCharacter: MaxDpsBridgeExpCharDB)
- Files: Toggles.lua + Bridge.lua (copied, globals renamed MaxDpsBridgeExp*), Exp/Core.lua, Exp/Profiles.lua, Exp/Overlay.lua, Exp/Settings.lua, Exp/Slash.lua, Bindings.xml, Exp/Assets/*.tga, Exp.xml load order: Toggles, Bridge, Core, Profiles, Overlay, Settings, Slash.
- Companion: AppSettings.InGameConfigMode bool (default false, ini key InGameConfigMode=0, CLI --exp), MainForm BuildExpLayout() hiding 14 switches, mirror pill rendering ToggleSync.EffectiveMask read-only, Inter + JetBrains Mono embedded (OFL in assets/LICENSES.md).
- Tools: tools/install-addon-exp.ps1 (copies only Exp dir), tools/fetch_assets.ps1 (dev-time regen), assets/LICENSES.md per-asset source+license+commit.
- dist-exp/ output (gitignored like dist/), own settings.ini with InGameConfigMode=1.

## 3) SV schema
MaxDpsBridgeExpDB = { v=1, overlay={shown,locked,scale 0.5-2,alpha 0.2-1,point,x,y,pillCols}, ui={lastTab,consoleMax=200}, profiles={Global={toggles{13+cc},sliders{}}}, active={mode}, sliders={} }; CharDB {profileOverride}.

## 4) Slash /mdbx
show|hide|toggle, lock|unlock, scale <n>, alpha <n>, reset, t <key> [on|off], profile <Global|Spec|Talent>, export, import, console, debug [on|off], cfg, help. Register /mdbx only, never /mdb.

## 5) Overlay
MDBXOverlay plain Frame BackdropTemplate, no secure attrs. Header (drag handle, lock, title) → PillGrid (14 pills icon+label+tint, 4 cols, pillCols configurable) → Footer (profile, status dot). Drag only unlocked, SetClampedToScreen, GetPoint save. Event dirty flag (toggle/spec/talent/profile/PLAYER_REGEN_*); 0.2s C_Timer.NewTicker only while OnShow, cancelled OnHide, redraw if dirty. Combat: insecure Show/Hide/Move/Scale allowed; Settings/profile rebuild deferred via InCombatLockdown()+PLAYER_REGEN_ENABLED queue; toggle flips plain Lua allowed.

## 6) Settings page
Settings.RegisterCanvasLayoutCategory(frame,"MaxDps Bridge Exp"), fallback InterfaceOptions_AddCategory. Tabs: Pause (auto-pause, pause key), Rotation (13+CC + per-spec sliders from Profiles.lua static table), Binds (read-only + open Keybindings btn), Settings (overlay geom + profile mode + export/import editbox), Console (ring 200), Debug (mask hex, counters, companion-seen). Top pills read-only local state.

## 7) Bindings.xml
Header MDBX. Actions MDBX_TOGGLE_OVERLAY + MDBX_TOGGLE_<KEY> x14 via global MDBXBinding(key) in Slash.lua. BINDING_HEADER_MDBX + BINDING_NAME_*.

## 8) Assets
Lucide/Phosphor MIT/ISC SVG one per key + lock/unlock/gear/pause/play/stop/crosshair → 64/128 PNG → TGA. TGA: uncompressed 32-bit type2 alpha, PoT 64/128/256 max256, top-left origin, path Interface\\AddOns\\MaxDpsBridgeExp\\Exp\\Assets\\<name> no ext in SetTexture. Committed. No runtime download. assets/LICENSES.md records URL+license+commit. Spell icons never bundled (game API). Fonts Inter+JetBrainsMono OFL companion-only. WoW uses GameFont*/NumberFont*.

## 9) Do NOT change
docs/PROTOCOL.md v5, PixelProtocol.cs, KeySender.cs, Scheduler/**, Decision/**, Knowledge/**, vendor/**, stable addon/MaxDpsBridge/*, ToggleSync push path (receive-only), ADDON-WINS gate, one-exe rule.

## 10) Rollout
1) tools/install-addon-exp.ps1 copies only Exp. Never deletes stable. 2) Disable stable per-character in AddOns list (not deleted), enable Exp. 3) Build to dist-exp/ with InGameConfigMode=1; dist/ untouched. 4) User pins taskbar manually; we supply path only. 5) Rollback: re-enable stable, disable Exp, run dist\\. 6) Update HANDOVER.md + ARCHITECTURE.md same pass.

## 11) Risks
Double-load guard both orders; Settings drift fallback + stub test; global rename + secret_harness check no MaxDpsBridge non-Exp global; TGA validation fallback Blizzard textures; versioned export/import reject v mismatch; no secure templates; mirror grey no-mask after N missed frames.

## Acceptance (offline)
dotnet build -c Release 0w/0e; dotnet test -c Release (from tests\\MaxDpsCompanion.Tests); lua tests/secret_harness.lua; luac -p addon/MaxDpsBridgeExp/**/*.lua + addon/MaxDpsBridge/*.lua; pwsh tools/ability_audit.ps1.

## Owed live (retail)
Exp loads alone; strip decodes; overlay drag/lock/scale/combat; Settings opens; bindings work; mirror matches mask.

## Open questions
TOC name MaxDpsBridgeExp ok? Separate Interface version? Per-spec slider names/ranges source?

## OPEN GAME (approved addition) — 2026-10-04
- Exp transport row now has **five** buttons, left→right: START, PAUSE, STOP, CALIBRATE, OPEN GAME. OPEN GAME is last (separated from the transport grouping), `MainForm.cs` `BuildExpButtonRow`.
- OPEN GAME calls the existing `LaunchGame()` (`MainForm.cs:2497`), which reuses `BattleNetLauncher.TryLaunch` (`BattleNetLauncher.cs:60`) + the `[Launch] BNetPath` setting. **No new launch logic; no direct Wow.exe launch; no protocol/KeySender change.**
- No new settings UI in Exp. BNetPath stays configured via stable mode or `settings.ini` `[Launch] BNetPath`. If BNetPath is absent, `OpenGameFromExp` appends one hint line to the Exp rolling log ("BNetPath not set - auto-detecting..."); the launcher still auto-detects Battle.net.
- Test: `ExpModeTests.Exp_Button_Row_Has_Wired_OpenGame_Last` asserts the button is mounted with text `OPEN GAME` and that raising its Click reaches the `Launcher` seam (no real Battle.net).

