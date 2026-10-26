> [!IMPORTANT]
> **Current client: 4.5.5 "Voidlight"** - the WinUI 3 Windows client
> (`app/MaxDpsCompanion/`), the shared brain (`app/MaxDpsCompanion.Core/`) and the
> macOS client (`app/MaxDpsCompanion.Mac/`, still `4.4.0 "Wayfinder"`), targeting
> `MaxDpsCompanionBridge` + `dist`. **C# is the long-term base.** Frozen: the
> 3.7.9 WinForms distribution (`dist-legacy/`, `build.ps1`), the legacy bridge
> (`addon/legacy/MaxDpsBridge/`) and the Rust port (`companion-rs/`, reference
> only, no Ext4). See
> [`LEGACY.md`](LEGACY.md); changelog: [`docs/CHANGELOG.md`](docs/CHANGELOG.md);
> release plans: [4.5.0 Voidlight](docs/plans/2026-10-25-midnight-ui-4.5.0.md),
> [4.4.0 Wayfinder](docs/plans/2026-10-25-wayfinder-transport-foreground.md),
> [4.3.0 Namesake](docs/plans/2026-10-18-companion-bridge-rename.md),
> [4.2.0 Nightblade](docs/plans/2026-10-18-rogue-cc-intelligence.md),
> [4.1.0 Paragon](docs/plans/2026-10-17-paragon-audit-remediation.md).

> [!TIP]
> **macOS Companion - `4.4.0 "Wayfinder"` (Apple Silicon, latest Mac build):** the same release,
> version and decision brain as the Windows 4.4.0 client. Download
> `MaxDpsCompanion-4.4.0-macos-arm64.zip` and
> `MaxDpsCompanionBridge-4.4.0-addon.zip` from the
> [v4.4.0 release](https://github.com/AnBPublic/MaxDps-Companion/releases/tag/v4.4.0),
> drag `MaxDPS Companion.app` to `/Applications`, run `Install Addon.command`
> from the addon package, grant **Screen Recording** + **Accessibility**, then
> Start (or run `--probe` for a read-only report).
>
> Both 4.4.0 clients are built from tag **`v4.4.0`** (commit `7b8bbfa`) and share
> [`app/MaxDpsCompanion.Core/`](app/MaxDpsCompanion.Core/), the 51-cell
> v5+Ext2/3/4 wire and the same `MaxDpsCompanionBridge` addon. Only the OS host
> differs: WinUI 3 + `PostMessage` on Windows, Avalonia + ScreenCaptureKit +
> `CGEventPostToPid` on macOS. The macOS binary is therefore **not
> byte-identical** to the Windows one, but it is the same product build
> (version, codename, source tag, brain, wire and addon).
>
> **The macOS client has not been ported to the 4.5.0 Midnight theme yet**;
> 4.5.0 is a Windows-only UI release. A macOS 4.5.0 asset is OWED.
>
> The release app is **ad-hoc signed, not notarized**: on first launch use
> **System Settings -> Privacy & Security -> Open Anyway** (or
> `xattr -dr com.apple.quarantine "/Applications/MaxDPS Companion.app"`).
> If Gatekeeper blocks the addon's `Install Addon.command`, run the same
> `xattr` on the unzipped addon folder.
> Build from source with `./build-mac.sh`. Capture is a **window-filtered
> ScreenCaptureKit stream bounded to the strip rect** (no screen recording, no
> per-tick capture call); every coordinate is window-relative. Numbers:
> `--bench-capture`. Full client doc:
> [`app/MaxDpsCompanion.Mac/README.md`](app/MaxDpsCompanion.Mac/README.md);
> live retail checks are OWED (`docs/TESTING.md` §4).

# MaxDPS Companion (Retail Midnight 12.1.5, v4.5.5 "Voidlight")

Pixel bridge driver for [kaminaris MaxDps](https://www.curseforge.com/wow/addons/maxdps)
(vendor pin: MaxDps v11.3.49). No memory read, no injection, no OCR, no LLM.
It reads what MaxDps suggests through a pixel strip and presses the player's
own bound keys via `PostMessage` into the game window only.

Two pieces:

 | Piece | What it does |
 | :--- | :--- |
 | `MaxDpsCompanionBridge` (addon, `addon/MaxDpsCompanionBridge/`) | Queries the MaxDps rotation engine each frame and encodes suggestions, ability ids, combat context and the keybound hotbar map into a 51-cell strip of flat-coloured pixels (35-cell v5 core + additive Ext2/Ext3/Ext4 blocks). |
 | `MaxDpsCompanion.exe` (desktop app, `app/MaxDpsCompanion/`) | WinUI 3 shell that samples those pixels, decodes the frame, evaluates every situational suggestion (USE / HOLD / SKIP / UNAVAILABLE / UNKNOWN) against an embedded ability intelligence registry + explicit candidate providers, queues recognised hotbar presses, schedules one action at a time, and replays the player's own keybinds into the attached game window. |

The legacy `MaxDpsBridge` addon (`addon/legacy/MaxDpsBridge/`, 40-cell strip) and the
old WinForms client (`dist-legacy/`) are **legacy/frozen**; all current work targets
`MaxDpsCompanionBridge` + `dist`.

## v4.5.5 "Voidlight" - GW2_UI taint root hotfix, 4.5.4 diagnosis corrected

4.5.4's bridge guard stops the assisted-combat frame's own tick/cascade, but the
**3,619** `SetCooldown` errors came from Blizzard's shared
`ActionBarButtonEventsFrame`: once GW2_UI's
`OverrideActionBar:HookScript("OnShow"/"OnHide", ...)` called `Hide()` on the
protected override bar in combat, the button stayed tainted and every
`ACTIONBAR_UPDATE_COOLDOWN` threw until reload.

- **Root fix:** `tools/patch-gw2ui-taint.ps1` (attached to the release)
  comments out that hook and GW2_UI's extra-action
  `ActionButton_UpdateCooldown` call, keeps `*.maxdps-backup-4.5.4` backups,
  verifies `luac`, is idempotent and supports `-Revert`. Run it after a
  GW2_UI update, then `/reload`. The local install is already patched.
- **Bridge 4.5.1 is unchanged:** it still hides the assisted-combat frame while
  cooldowns are secret, which stops the `SetAttribute` path; it cannot
  untaint a button, so the GW2_UI hotfix is the root fix.
- No wire, rotation or addon change in this release.

## v4.5.4 "Voidlight" - secret-cooldown error flood stopped (bridge 4.5.1, no wire change)

The game was logging thousands of `ActionButton.lua:847 SetCooldown ... Secret
values are only allowed during untainted execution` errors while the companion
pressed in combat. The stack is Blizzard's assisted-combat frame: it calls
`ActionButton_UpdateCooldown` / `UpdatePressAndHoldAction` on its parent
button every 0.15 s, and a UI addon had tainted that override button. The live
session logged **3,619** copies on `OverrideActionBarButton2` plus the matching
`SetAttribute` block.

- **Bridge 4.5.1** adds `SecretGuard.lua`. While cooldowns are secret it hides
  the assisted-combat frame on override/vehicle/extra buttons, and on any button
  where `issecurevariable` names a taint source. WoW does not tick hidden
  frames, so the error loop stops. Clean main bars are untouched,
  `C_AssistedCombat` still returns the next spell, and the companion still
  presses.
- **In game:** `/mcb guard` names the assisted frame, its button and the addon
  the client blames; `/mcb guard off` restores the stock Single-Button
  Assistant refresh.
- The underlying taint belongs to the bar addon (the session named **GW2_UI**);
  the guard stops the flood, but update or disable that addon if override-bar
  buttons still misbehave.

## v4.5.3 "Voidlight" - toggles in the log, holds named (no wire change)

Two visibility fixes after a Main off report that looked like a broken app:

- **Toggles are communicated.** The companion logs the addon's 14-bit toggle
  mirror the first time it sees it, on every change and when it stops:
  `[toggles] Main ON · all others ON`,
  `[toggles] Main OFF - rotation suspended (turn it back on: /mcb t Main on)`,
  `[toggles] Trinket OFF, Defensive ON`. The Exp **Details** disclosure shows
  the same summary as a live line.
- **Holds are named.** The status card reads `holding - <reason>` (Main off in
  game, typing in chat, out of combat, link lost) instead of the green
  "waiting for suggestion"; the classic shell reads `Holding - <reason>`.
- The old hint pointed at `/mcb main on`, which is not a command; the real one
  is `/mcb t Main on` (or click the in-game Main pill).

## v4.5.2 "Voidlight" - edge and corner resizing restored (hotfix, no wire change)

The 4.5.0 chrome fix removed the system non-client frame, and WinUI hosts the
XAML content in a child HWND that answers `WM_NCHITTEST` with `HTCLIENT` for
the whole client area - so the window had no resize cursors and could not be
dragged at the edges. The shell now declares the four `NonClientRegionKind`
border regions (8 DIP) and refreshes them on every size change, which hands the
frame back to the system: arrows, edge/corner drag and the 520x560 DIP minimum
all work again. The caption starts below the top grip (drag-to-move still
works) and the caption buttons' passthrough starts below it, so the top corners
resize too. No wire or addon change.

## v4.5.1 "Voidlight" - live resize, wrapped log lines, readable changelog (no wire change)

UI-only follow-up to 4.5.0. The addon stays 4.5.0 and the wire is untouched.

- **Every segment follows the window.** The Exp card re-clamps its Details
  disclosure live while you resize, the transport pill height and type follow the
  width tier (42 -> 52 DIP), and the classic shell stacks its two-up toggle rows
  (below 500 DIP) and console cards (below 560 DIP) instead of squeezing them.
- **Log text breaks.** Long log lines now wrap (fixed `HH:mm:ss` column, wrapped
  body, continuation lines aligned under the message) instead of ending in
  `…`; the empty state wraps too.
- **Changelog text breaks.** The Details body is the single scroller for the
  in-app changelog, its bullets and `Earlier:` line wrap, and its scrollbar is
  shown exactly while the content cannot fit — so the notes are never cut.
- Console status/history logs are height-capped (220/180 DIP) and the stacked
  console body scrolls, so no card is clipped by the window edge.

## v4.5.0 "Voidlight" - Midnight palette, gradient surfaces, no title-bar band (no wire change)

A UI-only release: the Windows client is repainted in the World of Warcraft:
Midnight palette and every large surface became a gradient. No wire, addon
logic, engine or settings change; the addon bump is identity-only.

- **New palette** - void-violet ground (`#0A0714`), amethyst accent
  (`#A78BFA`), Sunwell-gold warning/relaunch accent (`#F0C05A`), moonlit-silver
  text (`#F3EEFB`). The classic console and the in-game-config shell share one
  palette again.
- **Gradients, not flat fills** - window ground (violet-to-void diagonal with an
  aurora wash), title bar, transport buttons (role-coloured vertical ramps),
  ghost buttons, status pill and card cores.
- **The white band above the title bar is gone.** It was the system's light
  resize frame: `SetBorderAndTitleBar(false, false)` + `IsResizable = true`
  keeps `WS_THICKFRAME`, and the default `WM_NCCALCSIZE` inset the client area by
  it. The shell now claims a full-window client area, keeps DWM in dark mode and
  paints the frame line in the Midnight keyline. Edge resizing is unchanged.
- **Changelog in Details** - the Details disclosure renders the current
  release's highlights plus the earlier-version line. `docs/CHANGELOG.md` is the
  long form and `ReleaseNotesTests` enforces that both are updated with every
  version bump (`AGENTS.md`, "Release notes").
- **Restart Game** keeps a Sunwell-gold hairline (the Light action) while the app
  accent stays amethyst; **Start/Stop** use deeper fill bases so light text keeps
  contrast across the whole gradient.

## v4.4.0 "Wayfinder" - transport rework, foreground-gated detection, quiet bridge log (no wire change)

The in-game-config **Exp** transport card is reorganized around what you
actually press, and detection now respects the foreground:

- **Row 1** stays `Start | Pause | Stop`. **Row 2** is now
  `Open Game | Restart Game` (two buttons).
- **Open Game** is disabled and reads **`Game Running`** while the configured
  WoW process exists, and **`Game Starting…`** during the ~20 s launch gap after
  a launch/restart, so a second click can never spawn a second client.
- **Restart Game** asks for confirmation, kills the configured process (and its
  process tree) and relaunches it through the configured Battle.net launcher.
- **Calibrate** and the `Now: <action> — <why>` line moved into the collapsed
  **Details** disclosure; the card shows state + log, nothing else.
- **Detection runs only while the game window is foreground.** Alt-tabbing
  pauses screen sampling/decoding (hard gate, independent of
  `AllowBackgroundKeys`), the state reads `game in background` in a neutral
  tone, and detection resumes silently on return.
- **Quiet bridge log:** once the bridge has been seen, transient capture gaps
  are no longer printed. An unestablished bridge (or a 5 s+ outage) warns
  **one warning per outage (after ~5 s)** instead of spamming `no pixel block` /
  occluded / minimized lines on every alt-tab.
- **Addon is identity-only** (`4.4.0` / `Wayfinder` in the TOC, `VERSION.txt`,
  `MDCB.VERSION` and `MDCB.CODENAME`): no Lua logic, Catalog or cell change.
  The wire stays v5 + Ext2/Ext3/Ext4, 51 cells, every cell byte-identical.

## v4.0.4 "Aegis" - Main is the master switch; compact overlay (no wire change)

The in-game **Main** pill is now the master switch for the whole rotation. While
it is OFF, the addon blanks all eight slots (`main off (master switch)`) and the
companion suspends every send path - core rotation, offensive/defensive/utility
slots, auto-target, auto-interact and queued hotbar presses. The other pills are
**not rewritten**: their stored state stays exactly as it was, and turning Main
back ON restores the previous per-slot policy. A missing/stale Ext3 mirror fails
open for old addons. The toggle overlay also loses its `profile:` footer
(profiles remain in the `/mdbx` Config window) and shrinks from 300x210 to
300x165. No cell, checksum or layout change on the wire; the strip stays 51
cells. Stable `MaxDpsBridge` + `dist\` stay frozen.

## v4.0.3 "Bulwark" - hotbar press queue (additive Ext4 wire)

A companion + addon feature with an **additive** protocol extension: the strip
grows to **51 cells** (v5 core + Ext2 + Ext3 stay byte-identical; new Ext4
block, cells 43-50, presence = cell 28 B bit3; the version nibble stays 5, so
pre-Ext4 builds keep working). New `HotbarWatch.lua` streams the player's
**keybound action-bar map** (spells / items / macros, bar order, ready flag,
32-entry cap, gen/seq frame cycle, plus a chat-focus typing veto), and the
companion watches exactly those physical strokes with `GetAsyncKeyState` - no
hooks, no injection, no memory reads. Pressing one of your bar keys mid-GCD now
**queues the stroke into the current GCD window**: a GCD-riding ability is
delivered the moment the GCD falls (today an early press is silently dropped by
the client's ~400 ms queue), an off-GCD ability is delivered immediately, and a
press the focused game already received is never re-sent (no double
trinket/charge use). UI binds, movement keys and unbound keys are structurally
excluded; a cooling-down key never stalls the rotation. `/mdbx hotbar` prints
the live map; `[UserQueue] Enabled=1` (default) and the Advanced Configuration
"Follow my hotbar keypresses" toggle; `Background=0` keeps delivery to the
focused game. Stable `MaxDpsBridge` + `dist\` stay frozen at 3.7.7.

## v3.7.9 "Unyielding" - MaxDps recommendations are used; offensive TTK conservation removed (no wire change)

A behaviour change in the **companion** with **no protocol change**: `PROTOCOL`
stays at 5 and every cell is byte-identical. The hard TTK conservation gates on
offensive candidates are removed, so a Main/Offensive ability the bridge offers
is **used unless a game-truth/structural gate applies**. Gate removal: the T1
waste guard (valid/provisional + `ttk-hist`), the warmup hold
(`"warming up TTK"`), the grace hold (`"fast pack, waiting for TTK"`), the
enemy-count `Uncertain` hold, and the unknown-range `Uncertain` hold. Retained
gates: power starvation (`MainUsable`), out-of-range (`Range == No`), melee
requirement, player cast/channel, GCD/scheduler timing, user Never/Manual,
addon toggles, and the user-selected Burst/AoE presets. Also kept: own-buff
skip, the pair window (retained but inert — no offensive groups), the gap-fill
out-of-combat hold, melee/range gates. The TTK **estimator itself stays** for
the defensive T4 `DyingTargetHolds` and telemetry; `minTtkSec`/`holdForBurst`
become informational metadata for offensives. Offline audit: 161
vendor-offensive ids across all 13 classes carry no `neverAutomatic`/
`ManualByDesign`/`UnsafeToAutomate`, and after the removal no
vendor-recommended core-rotation id remains hard-blocked (only 212084 Fel
Devastation and 98008 Spirit Link Totem route to the Defensive slot by
`classOf` precedence — a documented deviation). The bridge is untouched; the
stable bridge (`addon/MaxDpsBridge/`) and `dist\` remain **frozen**. Live
retail validation is **OWED**: Avatar 107574 / Ravager 228920 / Recklessness /
Bladestorm must press on a MaxDps recommendation against a ≤15 s-TTK target and
in trash. Static ≠ automated test ≠ live in-game.

## v3.7.8 "Steadfast" — MAIN trusts the MaxDps core rotation (deny list removed, no wire change)

A behaviour change in the **Exp bridge** (`MaxDpsBridgeExp`) with **no protocol
change**: `PROTOCOL` stays at 5 and every cell is byte-identical. The MAIN slot
now presses **every ability the official MaxDps addon recommends as its core
rotation** — core MaxDps plus every `MaxDps_<Class>` module, all classes — with
no exception. The former `MDBX.MajorCDDeny` major-cooldown denylist
(`addon/MaxDpsBridgeExp/MajorCooldowns.lua`) is removed, so a recommendation the
addon glows as its core rotation (e.g. Combustion 190319, Avatar 107574,
Ravager 228920) is encoded as MAIN. The only remaining veto is game truth:
`usable == false AND noPower == true` (secret/nil/throw fail open), plus the
per-spec `MainFallback` filler so MAIN is never dead. Offensive / Defensive /
Interrupt routing (`MDBX.FlagOffensiveExtra`) is unchanged. The stable bridge
(`addon/MaxDpsBridge/`) and `dist\` stay **frozen** at 3.7.7; only
`MaxDpsBridgeExp` + `dist-exp` move to 3.7.8. New dedicated codename
**Steadfast** (Vigil/Warden/Fullcover/Holdfast/Onslaught/Reaver/Vindicator/
Templar/Gallant are never reused). Live retail validation is **OWED**: formerly
denied core-rotation ids must be observed pressed as MAIN, with no stall.
Static ≠ automated test ≠ live in-game.

## v3.7.7 "Gallant" — version-only identity bump (no behaviour or wire change)

Companion **and** bridge version bump with **no protocol change**: `PROTOCOL`
stays at 5 and every cell is byte-identical. New dedicated codename **Gallant**
(Vigil/Warden/Fullcover/Holdfast/Onslaught/Reaver/Vindicator/Templar are never
reused). csproj `<Version>`/`<Codename>`, the `Native.cs` fallback, `MDB.VERSION`,
`MDBX.VERSION`, both TOCs, both addon `VERSION.txt` files, the repo `VERSION.txt`,
this README, `ARCHITECTURE.md`, `docs/UI.md`, `docs/TESTING.md` and
`ReleaseIdentityTests` all read 3.7.7 / Gallant. The title bar reads
`MaxDPS Companion v3.7.7 Gallant`.

## v3.7.6 "Templar" — identity bump + offensive gap-fill verification fix

Identity bump to the new dedicated codename **Templar**, plus a data-only
offensive gap-fill fix: `abilities.json` 382245 renamed `Cold Blood` ->
`Cold Blooded Killer` (live SpellName), and live-unverified ids 370452 (Evoker
Devastation) / 392060 (Hunter Marksmanship) removed from the companion
gap-fill lists (rows kept; the MaxDps fork un-comment still routes them on the
wire). `PROTOCOL` stays at 5 and every cell is byte-identical. csproj
`<Version>`/`<Codename>`, the `Native.cs` fallback, `MDB.VERSION`, both TOCs,
both addon `VERSION.txt` files, the repo `VERSION.txt`, this README,
`ARCHITECTURE.md`, `docs/UI.md`, `docs/TESTING.md` and `ReleaseIdentityTests`
all read 3.7.6 / Templar. The title bar reads `MaxDPS Companion v3.7.6 Templar`.

## v3.7.5 "Vindicator" — version-only identity bump (no behaviour or wire change)

A companion **and** bridge version bump with **no protocol change**: `PROTOCOL`
stays at 5 and every cell is byte-identical. The codename moves to the new
dedicated **Vindicator** (Vigil/Warden/Fullcover/Holdfast/Onslaught/Reaver are
never reused). csproj `<Version>`/`<Codename>`, the `Native.cs` fallback,
`MDB.VERSION`, both TOCs, both addon `VERSION.txt` files, the repo `VERSION.txt`,
this README, `ARCHITECTURE.md`, `docs/UI.md`, `docs/TESTING.md` and
`ReleaseIdentityTests` all read 3.7.5 / Vindicator. The title bar reads
`MaxDPS Companion v3.7.5 Vindicator`.

## v3.7.4 "Reaver" — version-only identity bump (no behaviour or wire change)

A companion **and** bridge version bump with **no protocol change**: `PROTOCOL`
stays at 5 and every cell is byte-identical. The codename stays **Reaver**.
csproj `<Version>`, `MDB.VERSION`, both TOCs, both addon `VERSION.txt` files, the
repo `VERSION.txt`, this README, `ARCHITECTURE.md`, `docs/UI.md`,
`docs/TESTING.md` and `ReleaseIdentityTests` all read 3.7.4 / Reaver. The title
bar reads `MaxDPS Companion v3.7.4 Reaver`.

## v3.7.3 "Reaver" — version-only identity bump (no behaviour or wire change)

A companion **and** bridge version bump with **no protocol change**: `PROTOCOL`
stays at 5 and every cell is byte-identical. The codename moves to the new
dedicated **Reaver** (Vigil/Onslaught are never reused). csproj
`<Version>`/`<Codename>`, the `Native.cs` fallback, `MDB.VERSION`, both TOCs,
both addon `VERSION.txt` files, the repo `VERSION.txt`, this README,
`ARCHITECTURE.md`, `docs/UI.md`, `docs/TESTING.md` and `ReleaseIdentityTests`
all read 3.7.3 / Reaver. The title bar reads `MaxDPS Companion v3.7.3 Reaver`.

## v3.7.2 "Onslaught" — Arms + Fury execution fix (un-deny + offense routing + history release, no wire change)

A companion **and** bridge version bump with **no protocol change**: `PROTOCOL`
stays at 5 and every cell is byte-identical. Colossus Smash 167105 is no longer
denied (the 12.1 stale-id guess was wrong — it is a ~45 s Arms rotation button),
so Arms MAIN can encode it again, with the new `MainFallback.lua` Arms filler
(Mortal Strike 12294 then Overpower 7384). Ravager 228920 is added to
`MDB.MajorCDDeny` (never MAIN) and to `MDB.FlagOffensiveExtra`, which
`Reader.CategoryOf` maps to `"offensive"` so `FirstFlagged("offensive")` routes
it into the Offensive slot. On the companion, `TtkPolicy.LiveReleasesHistory`
releases `HistoryWasteGuardHolds` when a valid live TTK >= the need, or (no
valid live TTK) the target is >= 8 s old with HP known and >= 85%, so Fury
Recklessness / Avatar / Ancestral Call fire on long-lived rares while
trash-learned majors stay conserved. The title bar reads
`MaxDPS Companion v3.7.2 Onslaught`.

## v3.7.1 "Vigil" — no-downtime MAIN (usable-glow scan + Fury filler, no wire change)

A companion **and** bridge version bump with **no protocol change**: `PROTOCOL`
stays at 5 and every cell is byte-identical. The MAIN slot can no longer be
stranded: `Reader.GetMainSpellID` collects every non-denied glowing id, sorts
ascending, skips power-starved picks (`C_Spell.IsSpellUsable`), and falls
through to the new `MainFallback.lua` per-spec filler (Fury → Bloodthirst
23881) instead of a dead Main slot. The scheduler re-probes a failed Main at
`MainReprobeMs = 400` (`MainSameSpellNoOpCap = 3`) and `CandidateTracker`
refreshes the sole Main TTL while the frame heartbeat stays fresh. The fallback
is a deliberate, user-approved exception to "the bridge only encodes what
MaxDps suggests"; unlisted specs stay empty. The title bar reads
`MaxDPS Companion v3.7.1 Vigil`.

## v3.7.0 "Vigil" — custom MaxDps 12.1 fork (bridge denylist + reversible sync, no wire change)

A companion **and** bridge version bump with **no protocol change**: `PROTOCOL`
stays at 5 and every cell is byte-identical. A stale major cooldown can no
longer be encoded as the MAIN rotation pick: the new `MajorCooldowns.lua`
denylist makes `GetMainSpellID` skip stale/current major-CD ids, and the
Offensive slot stays an independent candidate so an empty Main cannot stall the
rotation. `custom/` vendors the untouched MaxDps v11.3.49 snapshot, a 75-entry
declarative patch manifest and `tools/Sync-CustomMaxDps.ps1`, which applies
patches into a generated, reversible `custom/out/` and publishes to `AddOns`
only on a clean run (with a `_backup/` rollback). `vendor/` is never edited.
The title bar reads `MaxDPS Companion v3.7.0 Vigil`.

## v3.6.0 "Warden" — taint-safe probe, TTK buff gating, unified masks (no wire change)

A companion **and** bridge version bump with **no protocol change**: `PROTOCOL`
stays at 5 and the Ext3 layout stays byte-identical. Three changes ship
together. (1) **Taint-safe melee probe:** `CheckInteractDistance` is
`#nocombat`-restricted, so it now has exactly one call site
(`Reader.MDB.ProbeTargetMelee`), event-gated and per-target cached; the Bridge
consumes that result and never calls it itself, and an `ADDON_ACTION_BLOCKED`
on the call backs off then disables the probe for the session. (2) **TTK-aware
buff gating:** a major offensive cooldown is held `"warming up TTK"` while the
target is younger than `[TimeToKill] WarmupSec` (default 3 s; 0 = legacy) and
TTK is unknown, and the adaptive-need input is the ability's own buff duration.
(3) **Unified masks:** one `BuildUnifiedPopup` shell builds both the Advanced
and Class-browser masks. The title bar reads `MaxDPS Companion v3.6.0 Warden`;
the build hash/time remains on the Advanced Diagnostics install doctor card
only. Both app and addon move together because `InstallDoctor` warns on a
version mismatch.

## v3.5.2 "Fullcover" — single full-window mask (UI only, no wire change)

A UI-only release with **no protocol change**: `PROTOCOL` stays at 5 and the
Ext3 layout stays byte-identical. The Advanced and Class-browser masks are now
parented to the top-level window and track its client rectangle, and the old
two-pixel form padding is gone, so **one opaque layer covers the entire
window** — no background ring or menu peeks at any edge. The 24/10/24/12 inset
moved from the gradient canvas onto the classic body scroll, so the gradient
ring surrounds the toggle card rather than the window. The title bar reads
`MaxDPS Companion v3.5.2 Fullcover`; the build hash/time remains on the
Advanced Diagnostics install doctor card only. Both app and addon move together
because `InstallDoctor` warns on a version mismatch.

## v3.5.1 "Holdfast" — semantics-only (no wire change)

A companion **and** bridge version bump with **no protocol change**: `PROTOCOL`
stays at 5 and the Ext3 layout stays byte-identical. It ships the semantic
flips: the companion now holds **fail-closed out of combat** unless the bridge
echoes the Ext3 OOC mirror bit, and toggle authority is **addon-wins** (the
in-game overlay is authoritative; the app never auto-pushes a mask). The title
bar reads `MaxDPS Companion v3.5.1 Holdfast`; the build hash/time moved from the
title bar into the Advanced Diagnostics install doctor card. Both app and addon
move together because `InstallDoctor` warns on a version mismatch.

## v3.5.0 — reliable coverage, Class Browser, Console home

The v3.5 release closes the "toggled skills never fire" defects (RC1–RC7) and
rebuilds the UI around them. The bridge now rotates **every** ready+bound+
policy-eligible candidate per slot instead of only the first, so a held gap
closer no longer starves an escape; Escape/Movement route to the Mobility
provider; and the scheduler can preempt on a brief GCD-off tick. Toggles are
**app-wins**: the companion pushes its 14-bit mask to the addon and the bridge
echoes the effective mask in the new additive **Ext3** block (43-cell strip,
cells 40–42; nibble stays 5, a pre-3.5 companion ignores it). The Class Browser
replaces the separate Class skills/Explorer tabs (Class|Spec|Mode, knobs,
virtualized owner-drawn rows, live why-held), and the default view is a
read-only Console home with rolling log and five named presets. Per-class
verified overlays (`Knowledge/classes/<CLASS>.json`, 13 classes) plus an
override store give every spec a companion-owned path. Perf/diagnostics add
off-thread browser precompute, cached scaled icons, a why-not-firing panel and
an install doctor. Protocol: v5 core + Ext2 + additive Ext3 (no v5 change).
Offline-proven; live retail checklist stays OWED (`docs/TESTING.md` §3).

## Intelligence coverage (v2.7.0, unchanged in 2.8)

Every registry entry now answers two independent questions: **who owns the
when-to-use decision** (Companion / MaxDps / Shared / Manual / Unavailable)
and **how complete that knowledge is** (Complete / Partial / Delegated /
ManualByDesign / Unobservable / ResearchPending / LiveUnverified). Every
MaxDps-owned entry carries an explicit delegation reason, every manual entry a
manual reason, and every entry carries patch metadata (introduced / source /
last-validated / source type / confidence / live-verified). A generated
coverage manifest reports DISCOVERED / REGISTERED / AUTOMATABLE /
COMPANION-GENERATED / MAXDPS-DELEGATED / MANUAL / RESEARCH-PENDING / STALE /
MISSING - so "3,279 entries" can never be mistaken for "3,279 intelligent
abilities".

```
MaxDpsCompanion.exe --ability-audit=<path>          # machine-checkable report; the audit tool enforces Violations 0 / Warnings 0 / Missing 0 / Stale 0
MaxDpsCompanion.exe --ability-coverage=<path>       # coverage manifest (JSON, stable classes)
MaxDpsCompanion.exe --ability-info=<spellId>        # inspect one ability (same canonical model the UI shows)
MaxDpsCompanion.exe --ability-search=<text> [--ability-class=CLASS --ability-spec=SPEC]
```

Explicit candidate providers (MaxDpsRotation / Offensive / Defensive /
Interrupt / Mobility / SelfSustain / Utility) own the per-category decision
logic, record where a candidate came from (MaxDps wire / bridge extra /
companion gap-fill / none) and attach structured "why" evidence. The Utility
provider is structurally incapable of firing. Verdicts and reason strings are
unchanged, and the scheduler hash is pinned (`b71a999d5e46570e`).

## Classic UI (v3.0.0)

A fast fixed 660-wide classic shell replaces the v2.8.1 rail/pages layout: one
non-scrolling main window whose hero card shows status, the live action with a
human-readable why, the sampled strip, and two-toggle rows for the Spells and
Modes. `Start` / `Stop` / `Launch Game` sit under it, with `Recalibrate`,
`Abilities…` and `Advanced…`. `Advanced…` opens a tabbed popup (Configuration |
Diagnostics | Intelligence) and `Abilities…` hosts Class skills | Explorer;
`Esc` closes the topmost. The shell never owns engine state. `--bench-ui`
measures 2000 refreshes (mean/p95 µs + startup ms); 12 snapshots (6 pages ×
{660×920, 520×560}) live in `dist/ui-snapshots`. Architecture: `docs/UI.md`.

```
MaxDpsCompanion.exe --ui-smoke-test
MaxDpsCompanion.exe --ui-snapshot-page=main --ui-snapshot=main.png --ui-snapshot-width=660 --ui-snapshot-height=920
```

## Gap-fill + reset-aware sustain (v3.1.0)

v3.1.0 closes two gaps. The Offensive slot gains a curated per-spec gap-fill
list (1–4 true burst cooldowns, shared burst first) so a cooldown the user
toggled ON fires even when MaxDps never surfaces it, and the defensive
gap-fill opens a short-cooldown (Orange) tier while majors still wait for Red.
Self-sustain is now reset-aware: self-heal readiness is re-read every tick, a
reset proc re-offers the same spell, a transient failed press is capped at
1.5 s, and a ready heal is never demoted behind the rotation (a cooldown wait
is reported as `SelfHealCoolingDown`). The classic shell also scales by width
tier (Compact/Classic/Roomy/Wide) and measured content height. Protocol is
unchanged (v5 core + additive Ext2); all of it is offline-proven — the live
retail checklist stays OWED.

## TTK intelligence (v3.2.0)

A pure, fake-clock estimator derives a per-target time-to-kill from the target
HP band already on the wire (no wire change, no Lua change) and uses it to stop
wasting cooldowns: **T1** holds a major offensive while the target dies before
the cooldown pays off, **T2** fires early (bypassing the pairing hold) when the
fight is long enough for two full uses, **T3** fires a favored execute cooldown
below its HP threshold, and **T4** saves a non-emergency Solo defensive when the
target dies imminently. An unknown/invalid estimate fails open (every gate is
skipped), and `[TimeToKill] Enabled=0` disables the whole feature. Telemetry
records `ttk`/`thp`/`ttkMs` so replay rebuilds the estimator deterministically.

## Ability intelligence (v2.6.0)

Every ability in the registry carries an explicit intelligence level -
Verified / Research-backed / MaxDps-backed / Companion rule / Manual by
design / Incomplete / Unknown - plus what MaxDps thinks of it and when the
companion may act on it. Anything without real intelligence says so and can
never be invented by the companion: the 2971-entry modeled tail only ever
fires when MaxDps itself suggests it. Manual utilities (CC, purges, dispels)
never fire automatically. An emergency self-heal (at or below 35% HP) now
outranks the rotation even outside Solo mode; the wider self-sustain layer
stays Solo-only.

The Class skills screen shows each row's status, tier and cooldown with a
why-tooltip; full reference: `docs/research/ABILITY_REGISTRY.md`.

MaxDps remains **the only rotation source**. The companion is the execution
and context layer: the main rotation is authoritative and always stays
responsive; situational abilities (offensive, defensive, interrupt,
consumable, trinket, mobility, self-heal) are only pressed when the policy
says now is the right time.

## Protocol v5 35-cell core + Ext2 40-cell block

Each cell is `CellSize` physical pixels square; every channel carries one
nibble (`nibble * 17`). Slot names mirror the in-game Spell Frame categories.
The wire version nibble **stays 5**: both the defensive-urgency nibbles
(bridge 2.3) and the Ext2 block (bridge 3.0.0) are **additive** on reserved
bits, so an updated addon still decodes with an older companion exe and a
stale 35-cell addon still drives the current exe. Full spec:
`docs/PROTOCOL.md`.

| Cells | Contents |
| :--- | :--- |
| 0 | magic (presence / alignment / calibration reference) |
| 1-8 | slots: Main, Offensive, Defensive, Consumable, Trinket, Interrupt, Mobility, SelfHeal (key hi/lo + modifier flags) |
| 9 | state + heartbeat + status flags (in-combat, on-GCD, has-target, context-valid) |
| 10 | protocol version + core checksum + commit |
| 11-26 | per-slot 24-bit spell id (0 = unknown identity) |
| 27 | player HP% (or unknown) |
| 28 | player cast/channel state + Ext2 SelfHeal2 range (bits0-1) |
| 29 | target: melee flag, HP band, cast/interruptible flags |
| 30-31 | per-slot range tri-state (unknown / in / out) + additive HP-curve defensive urgency + gap-fill source bit |
| 32 | per-slot self-buff-active bits + additive stagger-curve defensive urgency |
| 33 | class + spec wire ids (bit0 valid, bit1 buff block valid, bit2 Ext2 present, bit3 HP curve active) |
| 34 | extension checksum + commit |
| 35 | Ext2 HP curve (a live reading when bit3 is set, otherwise black; never checksummed) |
| 36-38 | Ext2 SelfHeal2 key + 24-bit spell id (the next distinct ready+bound self-heal) |
| 39 | Ext2 checksum over cells 36-38 (a failure drops only SelfHeal2) |

The companion decodes the 40-cell Ext2 frame, v5/v6 (35 cells), v4 (9 cells)
and v1 (8 cells): a stale in-game addon degrades gracefully (urgency and
context UNKNOWN → fail-open/conservative) and the app says so instead of going
blind. The Ext2 **HP curve** gives Solo a coarse HP reading when the game
hides cell 27 (priority: plain cell 27 > curve > unknown); the **SelfHeal2**
slot is a second, independently ranged self-sustain candidate. The self-buff
block is tri-state since bridge 2.7. Solo mode itself is unchanged from v2.8:
`[Solo] Enabled=1` runs the survival layer (sustain at ≤65%, emergency at
≤35%, self-heals as an independent candidate source) — see the Solo section
below. The addon is installed by `tools/install-addon.ps1`; after updating
always `/reload` (or `/mcb status` to confirm `protocol=5 ext2=1`).

## In-game commands (`/mcb`)

Legacy aliases `/mdb` and `/mdbx` work everywhere below (and `/mcb toggles`,
`/mcb settings`, `/mcb hotbar` are additional in-game-config commands).

| Command | Effect |
| :--- | :--- |
| `/mcb status` | Offset, cell size, state, bound slots, class/spec + extras; `ext2=1 hpcurve=<on\|off> sh2=<id\|->`. |
| `/mcb on` / `/mcb off` / `/mcb toggle` | Enable / pause the bridge. |
| `/mcb offset <x> <y>` | Move the strip in physical pixels from top-left. |
| `/mcb cellsize <px>` | Resize cells (1-64; 8 default/recommended). |
| `/mcb calibrate on\|off` | Show/hide the 54-step calibration pattern. |
| `/mcb heal` | Per curated self-heal entry: known / ready / usable / key / why (slot-8 + SelfHeal2 selection). |
| `/mcb hpcurve on\|off` | Ext2 HP-curve cell on/off (SavedVariables, default on). |
| `/mcb diag` | Scrubbed MaxDps internals snapshot. |
| `/mcb reset` | Back to defaults (clears calibrate mode). |

## Combat intelligence (v2.0/v2.1, `[Intelligence] Enabled=1`)

The core of the product. A deterministic, non-LLM policy layer:

```
MaxDps candidate → context evaluation → ability knowledge → policy
  → USE / HOLD / SKIP → scheduler → executor
```

- **Execution safety first (v2.1).** While the player is casting OR
  channeling, every GCD-riding ability is held — including the main
  rotation, because a channel is not ours to clip. Only interrupts, item
  activations and curated non-movement off-GCD abilities may fire during a
  cast/channel. This gate runs in every mode (policy on/off, scheduler
  on/off) on protocol v5/v6 frames; it is the only thing besides target range
  allowed to hold the main rotation.
- **Main rotation** stays authoritative. Only two gates exist: a clearly
  out-of-range target (skip) and an active cast/channel (hold; the next
  suggestion fires the moment it ends).
- **Defensives (v2.3)** use MaxDps's own flagged/usable recommendation as the
  trigger and the **rendered glow urgency** as the gate: at full health
  (White) no defensive fires and the rotation continues; a short-CD defensive
  fires from Yellow; a major needs Red unless it is the exact ability MaxDps
  is currently recommending (one-stage discount, never below Yellow);
  emergency HP overrides. The policy still prevents waste: a defensive
  already running is skipped, overlapping mitigation is sequenced (smallest
  sufficient first, escalate when HP keeps dropping), and reflect-type
  abilities (Spell Reflection, Diffuse Magic) need an observed incoming
  cast. Urgency is the vendor's own colour curve, not invented HP bands; the
  bridge stages it at the curve's anchors (docs/PROTOCOL.md, §6 of the
  research doc).
- **Offensives** never fire during a cast/channel and never into a curated
  pairing window or their own active buff. (Offensive cooldown scheduling
  itself stays with MaxDps's own engine — stacking is intentional.)
- **Interrupts** rank first, bypass the GCD and the key interval when the
  suggestion changes, and are range-gated. The bridge vetoes a kick when the
  target's cast was observed as NOT interruptible, and the policy skips a
  stale suggestion (no live cast). (Blizzard exposes no cast-school or cast
  identity for enemy units, so "important vs trivial cast" cannot be
  distinguished — documented limit.)
- **Mobility** (curated gap closers: Charge, Shadowstep, Harpoon, Death
  Grip, …) only fires when the target is confirmed outside melee, the
  ability is confirmed in range, and combat is active. Escape / movement
  utility (Blink, Vanish, Disengage, Sprint) is never automatic.
- **Self-heals** exist only in Solo mode (below); a running immunity and an
  overheal guard both conserve the heal. The ability's own preconditions also
  gate it: a melee self-heal with a confirmed out-of-range target is skipped,
  a target-requiring heal holds without one, and an ability curated as
  never-automatic stays manual.
- **Unknown means unknown.** Midnight secret values degrade each sensor to
  UNKNOWN, and every rule has a documented fallback (fail-open to MaxDps's
  own gate for MaxDps-gated slots, hold for companion-only slots). A missed
  cast-stop event is watch-dogged to UNKNOWN after 15 s instead of holding
  the rotation forever.

## Defensive automation (v2.3, `[Abilities]`)

MaxDps renders each defensive through a 3-point colour curve
(Red ≤30% HP / Orange 30-50% / Yellow 50-100% / no glow at 100%); Purifying
Brew uses the reversed stagger curve instead. The bridge stages that rendered
colour into the protocol, and the companion maps it to a per-ability gate:

| Urgency | Behaviour |
| :--- | :--- |
| White (full HP) | every automatic defensive holds; the rotation continues |
| Yellow | a short-CD defensive may fire; a major holds |
| Orange | a major holds unless it is the exact ability MaxDps is recommending (one-stage discount) |
| Red (≤30% HP) | a ready major fires; if MaxDps names no bound defensive, the catalog's derived gap-fill list may supply one (Major first, immunities excluded) |
| Unknown | majors hold; minors keep the legacy gates |

The **Class skills** screen (below) shows a generated class → spec → ability
list with a toggle per ability; the old inline Advanced list moved there.
**OFF never fires** — urgency, a MaxDps
recommendation, Solo mode and emergency HP cannot override it. **ON is only
eligibility** (White still holds, ON is never spam). Defaults are curated:
manual-by-design abilities (externals, escapes, dispels) start unchecked, and
a user can explicitly enable one (e.g. Vanish), which then stays
emergency-only. This whole path lives inside MaxDps's own `enableDefensives`
switch, so muting defensives upstream mutes the gap-fill too. See
`docs/KNOWLEDGE.md` and `docs/PROTOCOL.md`.

## Class skills (per-ability toggles)

A full-size screen opened from the main menu ("Class skills…", next to
"Advanced…") and from the Advanced page. Pick a class and spec; abilities are
grouped into a "Shared — all <class> specs" section, then per-spec Main
rotation / Offensive / Defensive / Movement.

- The data is the same knowledge base: curated `abilities.json` > vendor
  `Cooldowns.lua` > the generated class-spells book (`class-spells.json`, the
  retail `ns.classSpellData` block). Each row shows the 44×44 game icon, the
  ability name and a "Recommendation: On/Off" subtitle; spell id, source and
  default live only in the hover tooltip.
- The book is cross-checked against the live client (`spell-verification.json`,
  built by `tools/Verify-ClassSpells.ps1`): names and icons are the client's
  own official values, and abilities the client no longer has (removed in 12.1)
  are omitted from the list rather than offered as dead toggles.
- Icons are downloaded once from the official WoW CDN — preferring the slug
  from that same verification — and cached next to the exe
  (`assets/icons/{spellId}.jpg`, preserved across publishes); offline a drawn
  placeholder is shown and the screen still works. This is the app's only
  network use, opt-in by opening the screen.
- Toggling writes the existing `[Abilities] On=`/`Off=` storage. **OFF** is
  the absolute automatic-use prohibition (for a Main-slot spell the suggestion
  is skipped and the scheduler falls through to the next suggested stroke,
  never a rotation rewrite); **ON** is eligibility only. Movement abilities
  stay manual/emergency-only.

Classification and name decoding are modeled best-effort: the vendor data has
no active/passive flag, so a passive talent that evades the filter can appear
(its toggle is inert because it never arrives as a suggestion). See
`docs/KNOWLEDGE.md`.

## Solo / self-sustain mode (`[Solo] Enabled=0`, requires intelligence)

Survival-first when enabled:

| HP | Behaviour |
| :--- | :--- |
| above 65% | defensives stay MaxDps-triggered; self-heals conserve |
| below 65% | efficient self-heals eligible (overheal guard: a heal that is mostly wasted is held) |
| below 60% | a major defensive waits while a minor mitigation is running |
| below 35% | every available survival action outranks the rotation (emergency) |

**Where the self-heal candidate comes from (this is the part MaxDps cannot
provide).** MaxDps never suggests Impending Victory, Exhilaration, Crimson
Vial and friends — they are not in any damage rotation. The bridge therefore
resolves a per-spec *curated self-heal list* (generated `Catalog.lua`, the
same knowledge base the companion embeds) and writes the first ability that is
**ready and has a resolvable keybind** into the companion-only SelfHeal slot.
That slot is an independent candidate source: it does not wait for MaxDps to
suggest anything, and the Solo policy decides USE/HOLD/SKIP for it exactly
like any other candidate. The normal MaxDps rotation is never transformed —
the heal is an extra candidate that can preempt damage only while the HP
policy says it should.

Ability-specific policy (not one hardcoded percentage): the curated
`useBelowHpPct` ceiling (e.g. Lay on Hands below 40%) and `healPct` overheal
estimate are per-ability; melee heals are range-gated on the slot-8 probe;
target-requiring heals hold without a target; never-automatic heals (raid
cooldowns, channels that would be clipped) stay manual even in Solo mode; and
`EmergencyHpPct` shortens every rule for a survival action that can execute.

The same layer runs on the legacy path when the scheduler is disabled
(`[Scheduler] Enabled=0`): the companion slots are inserted into the
intelligence order (SelfHeal before Main, Mobility after Defensive) so Solo
mode does not silently disappear with the scheduler. With intelligence OFF
the companion-only slots are never considered — standard behaviour.

## Action scheduler (default on, `[Scheduler] Enabled`)

Deterministic state machine between the policy and the send path. Pure: a
function of the frame, the knowledge base, the policy memory and settings —
no clock reads, no randomness, no new game state.

| Rule | Behaviour |
| :--- | :--- |
| Link gate | A heartbeat frozen past `HeartbeatTimeoutMs` is link loss: nothing fires. |
| Protocol gate | A frame version that is neither supported nor a known older width holds. |
| Rank | Interrupt → emergency survival → defensive → solo self-sustain → Main → Mobility → Offensive → Consumable → Trinket; duplicate strokes collapse to the highest rank. |
| Stale demotion | A pressed-and-unchanged candidate moves behind fresh ones — never removed; a stale defensive cannot block a fresh main. |
| GCD + interval | Nothing that rides the GCD fires (interrupts bypass); one key press per `MinKeyIntervalMs`; a **changed** interrupt stroke bypasses the interval. |
| Cast/channel gate | The hard execution-safety hold (above) also runs here when intelligence is off, with `CastHold`/`ChannelHold` plan reasons. |
| Failure recovery | A press that never starts a GCD inside ~600 ms is treated as failed; the stroke is suppressed with an escalating window (1.5 s → 3 s → 6 s → 10 s, reset by a success) instead of hammered. The same stroke sent ≥5 times in 1.5 s backs off the same way. Failure suppression survives state transitions, so a dead action cannot be re-armed by every GCD pulse; a pending non-main press is demoted behind fresh actions while its confirmation is pending. |
| Unavailable suppression | A candidate rejected by a local gate (movement bind, held key, focus, lost window) is skipped briefly so lower ranks get a turn. |

Every existing per-slot OS gate stays authoritative. Advanced → Diagnostics
shows the plan head, reason, confidence, policy verdict/skip counts and the
hold reason; `--bench-scheduler` measures it on a scripted fake clock.

## Settings (`settings.ini`)

| Section | Key | Meaning |
| :--- | :--- | :--- |
| `Bridge` | `CellSize`, `OffsetX`, `OffsetY` | Strip geometry; match `/mdb status`. |
| `Window` | `ProcessName` | Game process without `.exe` (`Wow`). |
| `Window` | `AllowBackgroundKeys` | `1` = spell keys send while in background (mouse/interact always need focus). Since 4.4.0 this only describes **key delivery**: screen detection is foreground-only, so a backgrounded game is never detected regardless of this key. |
| `Window` | `ModifierInput` | 4.1.0. How a Ctrl/Alt/Shift chord is delivered: `Post` (default; PostMessage to the game window, works in the background, unchanged behaviour), `Auto` (SendInput while the game is focused, Post fallback with a status note in the background), `SendInput` (focused only; deferred in the background). Never releases a modifier you hold; defers when you hold an extra modifier. Plain keys are always posted. |
| `Window` | `Width` / `Height` | Remembered client size (`0` = default); saved on resize-end and close, restored clamped to the working area on start. |
| `Pause` | `Button` | Global pause hotkey (`Pause`, `F9`, …). |
| `CrowdControl` | `Enabled` | `1` = enable the interrupt-fallback CC pool (single-target stun/silence only). Blind/Gouge/Sap/Polymorph/Fear stay manual. |
| `CrowdControl` | `BreakGuard` | `1` = after a manual break-on-damage CC, hold Main/Offensive/Interrupt for its expected duration (cap 10 s). |
| `CrowdControl` | `ManualHoldMs` | Manual party-CC hold duration (3000-30000, default 8000). |
| `CrowdControl` | `HoldHotkey` | Global hotkey for the manual party-CC hold (default `Ctrl+Shift+C`). |
| `Spells` | `spell1`…`spell8` | Which slots may fire: Main, Offensive, Defensive, Consumable, Trinket, Interrupt, Mobility, SelfHeal. Item slots default off; Mobility/SelfHeal on. |
| `Timing` | `PollIntervalMs` / `MinKeyIntervalMs` / `KeyPressMs` | Sample rate / gap between inputs / hold length. |
| `Targeting` | `AutoTargetEnabled`, `CombatOnly`, `TargetKey` | Press `TargetKey` when bridge reports no-target. Never a movement key. |
| `Interact` | `InteractEnabled`, `InteractKey` | Press `InteractKey` when bridge reports need-interact. |
| `Scheduler` | `Enabled` | `1` (default) = deterministic scheduler. `0` = legacy loop (policy still applies when intelligence is on). |
| `Scheduler` | `HeartbeatTimeoutMs` / `RepeatSuppressMs` | Link-loss window / v1-frame repeat suppression. |
| `Intelligence` | `Enabled` | `1` (default) = situational policy (USE/HOLD/SKIP). `0` = every MaxDps suggestion fires (companion-only slots off). |
| `Intelligence` | `StaleAfterMs` | Pressed-and-unchanged demotion window. |
| `Solo` | `Enabled` | `0` (default) = standard. `1` = survival-first layer (requires intelligence). |
| `Solo` | `EmergencyHpPct` / `SelfSustainHpPct` / `DefensiveEscalateHpPct` | 35 / 65 / 60 — see the table above. The emergency and escalation thresholds also tune normal-mode defensive behaviour (emergency override / save-the-major rule); `Enabled` gates the proactive self-heal layer. |
 | `Abilities` | `On` / `Off` | Comma lists of spell ids explicitly enabled/disabled over the curated default (v2.3). `Off` never fires automatically (absolute); `On` is eligibility, not spam. Written only as a diff from the catalog default. |
 | `Abilities` | `Modes` | Per-ability modes `id:Mode` (v2.6.0): `SoloOnly` / `NormalOnly` / `Manual` / `Never` / `Always` / `Automatic` (`Always` currently evaluates as `Automatic` - reserved). |
| `Telemetry` | `Enabled` / `Capacity` | Opt-in local JSONL ring (no network, no Blizzard values). |
| `Color` | (empty = unlearned) | Learned display-chain profile from `/mdb calibrate`. |
| `Launch` | `BNetPath` | Empty = auto-detect Battle.net. **Launch Game** uses the launcher's remembered account — no credentials stored anywhere. |

The borderless window's 2px frame ring is red while stopped and green while
running, so the state is readable at the window edge without looking at the
status line; the resize grip is unaffected. The client size is remembered (see
`Width`/`Height` above).

## Ability knowledge base

`docs/KNOWLEDGE.md` documents the two-layer catalog: the vendor-extracted
base (`vendor-abilities.json`, regenerated by
`tools/Extract-VendorAbilities.ps1`) and the curated policy layer
(`abilities.json`) with per-ability purpose/tier/conditions plus per-spec
Mobility/SelfHeal lists and a derived defensive gap-fill list. The bridge
mirrors the class/spec ids and extras via the generated
`addon/MaxDpsCompanionBridge/Catalog.lua`
(`MaxDpsCompanion.exe --gen-catalog=<path>`); a test fails if it drifts. The
user per-ability ON/OFF layer is `Knowledge/AbilityPolicy.cs` (`[Abilities]`).
Research artifact and sources: `docs/research/ABILITY_RESEARCH.md`.

## Rotation telemetry + replay (opt-in, `[Telemetry] Enabled`)

Local-only record of each decoded tick — suggestions, policy verdicts with
reasons, decision, keys sent, timing and decode faults. Bounded in-memory
ring, nothing on disk until you export, no network, no Blizzard values.

1. Advanced → **Telemetry** → *Record rotation telemetry* → **Start**.
2. Fight, then Advanced → **Export…** → `telemetry-<time>.jsonl`.
3. `MaxDpsCompanion.exe --replay="<file>.jsonl"` writes a diagnostic report:
   per-tick candidates, selected action + reason, policy verdicts, rejected
   candidates, GCD/stale holds. Recorded *intelligence* decisions recompute
   with **0 mismatches**. Format spec: `docs/TELEMETRY.md`.

## Build / install

```powershell
.\tools\install-addon.ps1      # CURRENT: copy MaxDpsCompanionBridge into Interface\AddOns (never touches legacy addons)
.\tools\publish.ps1            # CURRENT: WinUI 3 4.4.0 "Wayfinder" -> dist\MaxDpsCompanion.exe
                                #   (unpackaged, framework-dependent; keeps settings.ini + the Rust
                                #    mdc-*.exe untouched; asserts MaxDpsCompanion.exe +
                                #    MaxDpsCompanion.pri; writes VERSION.txt = 4.4.0 Wayfinder)
.\build.ps1                     # LEGACY: publishes the frozen 3.7.9 WinForms client to dist-legacy\
```

Prerequisite for the dist client: the **Windows App SDK 1.8 runtime**
(framework-dependent, same model as the .NET 8 Desktop Runtime). A clean machine
with only that runtime is still an OWED live check. `tools/publish.ps1` must
keep `EnableMsixTooling=true` + `WindowsPackageType=None` paired, or the
published output lacks the XAML resource index (`MaxDpsCompanion.pri`) and the
app fails `0xC000027B`.

Then in game: `/reload`, `/mcb status`, Start in the app. Client must be
windowed/borderless and visible. Tests: `docs/TESTING.md`.

`MaxDpsBridge` + `dist-legacy\` + `build.ps1` are **LEGACY/deprecated** (the
frozen 3.7.9 WinForms client); all releases, fixes and deploys target the
companion client unless legacy is explicitly requested.

Version locations: addon `MaxDpsCompanionBridge.toc` + `addon/MaxDpsCompanionBridge/VERSION.txt`,
app title bar (`ThisAssembly.Gen.cs` stamp), repo `VERSION.txt`.

## Safety limits

- Windowed or borderless windowed; visible (not minimized/covered).
- **Detection is foreground-only (4.4.0):** sampling/decoding runs only while
  the game window is the foreground window; alt-tabbing pauses detection and
  the state reads `game in background`. `AllowBackgroundKeys` does not change
  this.
- Keys go to the game window handle only; mouse-bound slots need focus.
- **Restart Game terminates the configured process** (`Kill(entireProcessTree)`):
  the client is killed, not logged out, so this session's SavedVariables can be
  lost. The confirmation dialog states it; use it only when you mean to relaunch.
- MaxDps is a single-spell engine: the main slot is the rotation; everything
  else is situational and policy-gated.
- **Immune targets** cannot be detected (no immune flag in any safe API): a
  press that the game rejects is bounded by the failure recovery (escalating
  suppression), never by knowledge.
- **UNKNOWN range fails open** for MaxDps-gated slots (MaxDps's own readiness
  gate owns range); a known out-of-range target is always skipped.
- **Enemy cast identity / importance** is not observable in Midnight 12.x
  (UnitCastingInfo is secret for non-player units; combat-log events are not
  available to addons) — reflect filtering uses "a cast is live", never
  "which spell".
- ~50–120 ms pixel-to-key latency; a suggestion follower, not a bot.
