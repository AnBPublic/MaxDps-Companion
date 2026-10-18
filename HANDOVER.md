# Handover - MaxDps-Companion

Read order: this file -> `ARCHITECTURE.md` -> `docs/PROTOCOL.md`. Full history
(every release back to 1.x, root-cause write-ups, per-release verify logs) is in
[`docs/archive/HANDOVER-history.md`](docs/archive/HANDOVER-history.md); search it
by release name or date before re-investigating an old problem.

## CLIENT STATUS (current)

- **Current:** `app/MaxDpsCompanion/` (WinUI 3 Windows client) + `app/MaxDpsCompanion.Core/`
  (shared brain, consumed by Windows since 4.1.0) + `app/MaxDpsCompanion.Mac/` +
  `addon/MaxDpsCompanionBridge/` + `dist/`. **C# is the long-term base** (decision 2026-10-17).
- **Frozen:** `addon/legacy/MaxDpsBridge/` (legacy stable bridge), `dist-legacy/` + `build.ps1`
  (3.7.9 WinForms), and `companion-rs/` (Rust port; reference only, no Ext4). See `LEGACY.md`.
- The legacy and companion bridge addons are mutually exclusive per character; use the
  companion bridge (`/mcb`; legacy aliases `/mdb`, `/mdbx`).
  Deploy: `pwsh tools/publish.ps1` + `pwsh tools/install-addon.ps1` (both only on the user's go-ahead).
- Wire: protocol v5 + Ext2/Ext3/Ext4 (51 cells), unchanged since 4.0.3. Any wire change is an
  architect-trigger task (`AGENTS.md`).

## 2026-10-18 RELEASE 4.3.0 "NAMESAKE" (Companion Bridge rename; NO wire change)

Spec/plan: `docs/plans/2026-10-18-companion-bridge-rename.md` (approved; tasks 1-8).
Branch `release/v4.3.0-namesake`. Identity: csproj, `app/Version.props`, `Native.cs`,
`ReleaseIdentityTests`, addon TOC/`VERSION.txt`/`MDCB.VERSION` 4.3.0, `MDCB.CODENAME` Namesake.

DONE (code side)
- Addon renamed `MaxDpsBridgeExp` -> `MaxDpsCompanionBridge` (TOC `MaxDpsCompanionBridge.toc`,
  Title "MaxDps Companion Bridge", `Exp/` -> `InGame/`); Lua namespace `MDBX` -> `MDCB`;
  saved variables `MaxDpsCompanionBridgeDB` / `MaxDpsCompanionBridgeCharDB`; keybinds
  `MDCB_TOGGLE_*`; slash `/mcb` with `/mdb` + `/mdbx` aliases; overlay/settings/chat branding updated.
- Legacy stable addon archived: `addon/MaxDpsBridge` -> `addon/legacy/MaxDpsBridge`
  (byte-identical rename, frozen); the harness legacy-compat section points at the archive path.
- Companion: the in-game-config shell is now the DEFAULT (`InGameConfigMode=1`; `=0` opts into
  the classic shell; `--exp` stays a compatibility alias); `tools/publish.ps1` +
  `tools/install-addon.ps1` target `dist\` + the new addon; the legacy build output convention
  moves to `dist-legacy\`; the catalog audit is strict again (exit 0; addon Catalog.lua
  regenerated to byte-match the generator).
- Tests: 1072 passed / 0 failed; Lua harness 409/409; `luac -p` clean on addon root + InGame;
  `ability_audit.ps1` exit 0; `--ui-smoke-test` title `MaxDPS Companion v4.3.0 Namesake`.

NEXT (post-merge rollout; primary checkout)
- `dist\` -> `dist-legacy\`; `dist-exp\` -> `dist\` (settings.ini travels); publish; install addon;
  migrate `WTF\...\SavedVariables\MaxDpsBridgeExp*.lua` to the new variable names and
  `bindings-cache.wtf` `MDBX_TOGGLE_*` -> `MDCB_TOGGLE_*` (both with `.bak`); retarget the
  Windows launch shortcuts to the new exe dir.

OWED (live retail) - addon loads as "MaxDps Companion Bridge", `/mcb` (+ aliases) responds,
toggle keybinds work, overlay icons + settings page intact; plus the 4.2.0 CC checks
(`docs/TESTING.md` §3o) still owed.
Static != automated test != live.

## 2026-10-18 RELEASE 4.2.0 "NIGHTBLADE" (CC intelligence; NO wire change)

Research: `docs/research/CC_DR_12.1.5.md`. Plan:
`docs/plans/2026-10-18-rogue-cc-intelligence.md`. Identity: csproj, `Native.cs`,
`ReleaseIdentityTests`, `app/Version.props`, Exp TOC/`VERSION.txt`/`MDBX.VERSION`
4.2.0-exp, `MDBX.CODENAME` Nightblade.

DONE
- **CC role split:** only single-target Stun/Silence interrupt-fallback rows are
  auto-eligible; Blind/Gouge/Sap/Polymorph/Fear/Hex/Repentance and every other
  break-on-damage row are manual-only. Rogue generated `cc = { 408 }`.
- **12.1.5 DR:** 20 s reset from the end of the last application, 1/0.5/0.25/
  0.125/0.0625, PvP immune after two, PvE 1.0 s usefulness floor. Applied to
  the delegated Kidney Shot path (registry Incomplete) as well as the curated
  CC provider. Kidney Shot uses the conservative 2 s base.
- **Interrupt/stun discipline:** casting-only, explicit not-interruptible skip,
  no blind stun, and a stun-ineffective latch (still casting ~800 ms after a
  stun on a confirmed cast) until target loss/OOC.
- **Break-on-damage guard:** `[CrowdControl] BreakGuard=1`; manual Blind/Gouge/
  Sap/Polymorph/Fear (Ext4 press) holds Main/Offensive/Interrupt for the
  expected duration (cap 10 s). Manual party-CC hold: `ManualHoldMs=8000`,
  `HoldHotkey=Ctrl+Shift+C`, Console "CC Hold" top-bar item.
- Limits documented: party CC is unobservable, active DoTs can still break a
  manual CC, DR is self-tracked, native DR tracker is not readable.
- Tests: 1071 passed / 0 failed; Lua harness 409/409; `luac -p` 9 Exp root files;
  catalog fixture + Exp Catalog regenerated.

OWED (live retail 12.1.5) - Kick vs Kidney Shot fallback, second/third stun DR,
Blind/Gouge attack guard, manual CC hold, boss/immune latch, status strings.
Static != automated test != live.

## 2026-10-18 RELEASE 4.1.0 "PARAGON" (audit remediation; NO wire change)

Spec/plan: `docs/plans/2026-10-17-paragon-audit-remediation.md` (architect-reviewed).
Branch `release/v4.1.0-paragon`. Identity: csproj, `Native.cs`, `ReleaseIdentityTests`,
`app/Version.props` (Core + Mac now share it), Exp TOC/`VERSION.txt`/`MDBX.VERSION` 4.1.0-exp.

DONE
- **Addon hot path** (`addon/MaxDpsBridgeExp/*`): `SPELL_UPDATE_COOLDOWN` no longer wipes the
  binding cache (revision bump only; full wipes coalesced); `FrameKey` keyed on the exact decision
  edges (HP 30/35/50/75, stagger stage) and now includes the previously missing `InCombat`,
  `Grouped`, stagger inputs; per-tick memo for `SpellVariants`/`KnownSpell`/`IsSpellReady`/
  `RotationCandidates`; per-tick closures/tables hoisted; HP-curve black paint cached; Toggles DB
  cached; HotbarWatch rebuild debounced, `gen` bumps only on content change; vehicle/override/bonus
  bar events; SavedVariables `SchemaVersion` 1 + `Migrate`; strip layout clamped to the screen;
  one shared slot->command table (slots 13-24 -> ACTIONBUTTON, 61-72 -> MULTIACTIONBAR1, 73-144 ->
  no key, texture fallback; source warcraft.wiki.gg/wiki/Action_slot). Harness 324 -> 393 pins.
  `issecretvalue` stays in `KeyIdent` (removal was conditional on proving `tostring(secret)` stable).
- **Windows engine:** one frame decode per tick; immutable toggle-mirror and colour-profile
  snapshots (no tearing); `KeyDelivery` (checked PostMessage/SendInput results, key-up always sent,
  lParam fix, UIPI detection); `NoteExternalSend` after a delivered queued user press; a
  focus-blocked keyboard slot no longer aborts lower ranks (`SlotGate`); `Process.GetProcesses`
  throttled to 1 s; duty-cycle stretch capped at one poll interval; `Stop()` checks the join and
  `PanicRelease`s injected modifiers; minimized / black-frame ("strip occluded") diagnostics.
- **`[Window] ModifierInput=Post|Auto|SendInput`** (default `Post` = byte-identical old behaviour).
  `Auto` uses SendInput only while the game is focused (re-checked right before sending), never
  releases a modifier the user holds, defers when an extra modifier is held, ignores its own injected
  keys in the physical-key scan (30 ms own-injection window), shows a status note for a background
  chord. Whether WoW honours posted modifiers is UNPROVEN - see TESTING 3n.
- **Shared brain:** Windows references `MaxDpsCompanion.Core` (32 byte-identical files + 8 merged
  supersets removed from the Windows project; Knowledge JSONs single-sourced in Core). Only
  `RotationEngine` still exists in both assemblies (allow-listed by `ShadowedTypeGateTests`); merging
  the two engines is Wave C, NOT done. Core's engine gained the Main master gate and the typing
  veto, so the Mac client honours them; it still has no user-press queue (Ext4 is decoded but unused).
- **Mac host:** failed permission check no longer starts the engine; keyboard is per-process only
  (no global-tap fallback); numpad / OEM / nav / F13-F20 keys mapped; region changes off the tick;
  Ext3 mirror in stream mode; settings persisted + pause/target/interact key boxes; Start/Stop off
  the UI thread; Swift buffer-lifetime and error-string races fixed (UNVERIFIED on a Mac);
  `build-mac.sh` no longer hardcodes a user path, reads `Version.props`, runs `selftest.swift`.
- **CI:** `.github/workflows/ci.yml` (windows: build/test/Lua harness/luac/ability audit; macos:
  Core+Mac build + `swiftc -parse`). `.gitattributes` keeps `*.sh` LF.
- Repo: history moved to `docs/archive/`; `LEGACY.md` rewritten (C# base, Rust frozen).

VERIFY (this machine, 2026-10-18): `dotnet build app/MaxDpsCompanion/MaxDpsCompanion.csproj -c Release`
0 warnings / 0 errors; Mac build 0 / 0; `dotnet test -c Release` 1054 passed / 0 failed (975 at 4.0.4);
`lua tests/secret_harness.lua` 393 passed / 0 failed; `luac -p` 15 Exp files clean; `pwsh
tools/ability_audit.ps1` Violations 0 / Warnings 0 / Missing 0 / Stale 0 (exit 2 = documented
stable-Catalog drift); a `dotnet publish` to a temp dir contains `MaxDpsCompanion.exe`, `.pri`,
`MaxDpsCompanion.Core.dll` and `--ui-smoke-test --exp` PASSES from it. Static != automated test != live.

OWED (live retail 12.1) - `docs/TESTING.md` 3n: ModifierInput chords (Post vs Auto/SendInput,
foreground + background); elevated-game delivery failure; stuck-modifier panic release; no double
press after a queued user press; focus-blocked slot lets an off-GCD interrupt through; strip-occluded
message; addon frametime before/after (CPU profiler); Ext4 map stays populated through cooldown bursts
(gen only bumps on content change); stance/vehicle bar binds (slots 73-144 now use the texture
fallback). Mac: permissions, key delivery to the game, key map on a real keyboard, Swift edits,
notarization. CI: first run on GitHub.

NEXT (not started): Wave C (one engine: characterisation tests -> port to Core -> delete the Windows
engine -> `SendGate`/status split); stages for 4.2 needing a wire change or a user decision: Assisted
Combat (`C_AssistedCombat`) source, on-screen suggestion icon, cooldown/aura/AoE/resource signals,
Windows Graphics Capture backend, dead profile sliders (`MinTtkSec`, Solo HP bands), melee probe in
combat via `IsSpellInRange`, stance/vehicle bar key mapping.

## Hard-won lessons (still true; details in the history file)

- A denied slot MUST repaint its cell empty (4.0.3 root cause #3), else the core checksum never matches.
- The addon `Main` pill is the master switch (4.0.4); a stale `Main=false` in SavedVariables looks like
  "only non-core skills fire" - check `/mcb main on` first.
- The user-press queue never suppresses the plan; it only wins the send-site race (4.0.3 hotfix).
- Keep `WindowsPackageType=None` + `EnableMsixTooling=true` paired or `MaxDpsCompanion.pri` is missing
  and the app dies `0xC000027B`.
- Never claim live validation without a retail run.
