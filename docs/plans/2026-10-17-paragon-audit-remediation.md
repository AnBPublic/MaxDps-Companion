# 4.1.0 "Paragon" - audit remediation (no wire change)

Date: 2026-10-17. Route: **L** (touches KeySender, Scheduler-adjacent engine code, `addon/MaxDpsBridgeExp/*.lua`).
Decisions (user): **C# is the long-term base** (companion-rs frozen, not touched); user uses
Shift/Ctrl/Alt bindings, so modifier delivery is a first-class requirement.
Release identity: 4.0.4 "Aegis" -> **4.1.0 "Paragon"** (new dedicated codename; never reuse
Vigil/Warden/Fullcover/Holdfast/Onslaught/Reaver/Vindicator/Templar/Gallant/Steadfast/
Unyielding/Lumen/Bulwark/Aegis). `PROTOCOL` stays v5 + Ext2/Ext3/Ext4, 51 cells, every cell byte-identical.
`addon/MaxDpsBridge` (stable) + `dist\` + `vendor\` + `companion-rs\` are NOT touched.

## Execution model

Cheap workers (`code-worker`, DeepSeek V4.1 Flash high; `code-worker-max` for the engine
unification) implement; `smart` reviews every diff end to end and runs the full verify bar;
one `reviewer` (or `reason-worker-xhigh` if the reviewer harness fails) on the final L diff.
Work runs in **waves**. Within a wave, packages own DISJOINT files and run in parallel.
Later waves start only after the previous wave is merged and green, because they edit the
same files (`RotationEngine.cs`, csproj, Core).

Verify bar after every wave (quote output): `dotnet build app/MaxDpsCompanion/MaxDpsCompanion.csproj -c Release`
(0 warnings / 0 errors), `dotnet test -c Release` from `tests/MaxDpsCompanion.Tests`,
`lua tests/secret_harness.lua`, `luac -p addon/MaxDpsBridgeExp/**/*.lua`, `pwsh tools/ability_audit.ps1`
(exit 2 = documented stable-Catalog drift only), `dotnet build app/MaxDpsCompanion.Mac -c Release`.
Live retail items stay **OWED**; static != automated != live.

## Wave A (parallel, disjoint files)

### A1 - Exp addon hot path (owner: `addon/MaxDpsBridgeExp/**`, `tests/secret_harness.lua`)
1. `Bars.lua:184-188`: `SPELL_UPDATE_COOLDOWN` must NOT wipe `_BindCache` or run a 180-slot walk per
   event. Bump `_BindRevision` only; full wipe on ACTIONBAR/UPDATE_BINDINGS/UPDATE_MACROS/spec/talent
   events, coalesced to <= 1 per 0.25 s. If a cached *Miss* can depend on cooldown state, give Misses a
   1 s TTL instead (worker must read `Reader.lua` `FindSpellOnActionBar`/`ResolveBinding` and decide).
2. `Reader.lua` `FrameKey` (~2237-2354) - AMENDED: replace raw player HP with the **exact decision edges**:
   `UrgencyFromFraction` (HP<=30 Red, <50 Orange, `Reader.lua:1998-1999`, alt ladder `:2007-2008`), Solo bands 75/50/30 when
   `SoloLadderBands` is set (`Reader.lua:2080-2091`, `Bridge.lua:997`), emergency `EMERGENCY_HP=35` (test is `Hp > 35`,
   `Toggles.lua:65,381-391`). Pin tests at each edge +-1. ALSO add the inputs `FrameKey` is MISSING today (stale-cache bug):
   `Ctx.InCombat`, `Ctx.Grouped` (`Bridge.lua:958-976`, `Toggles.lua:371-379`) and the stagger stage (`Reader.lua:2037`), each
   quantised, each with a harness pin. Keep `DefPin`/`RotationTick(DefPin)` invalidation (`Bridge.lua:1027-1032`) unchanged and pin
   "crossing 50% repins". Replace per-call string building with numeric hashes. `issecretvalue` branch removal in `KeyIdent` is
   conditional on a harness pin that a secret value yields a fixed key and no error (`tostring` of a secret may be stable).
3. Per-tick memo (reuse the existing `Tick` table, `Reader.lua:~446`) for `SpellVariants`,
   `KnownSpell`, `IsSpellReady`, and the `RotationCandidates` result (4 calls/tick at `Bridge.lua:1103/1141/1153/1165`).
4. Hoist per-tick closures to file scope (`Bridge.lua:983/1037/1079/395/653`, `Toggles.lua:325`,
   `Reader.lua:1899/1902`); allocate `R,G,B,Id` only in the scan branch (`Bridge.lua:1063`); stop writing the
   global `MDBX.LastMissBinding` per miss (`Keymap.lua:118`).
5. HP-curve cell: track "currently black" and skip repeated `SetColorTexture` (`Bridge.lua:638-657`).
6. `Toggles.lua`: cache the DB table in a local refreshed on load; precompute lowercase keys (lines 151-223, 444-450).
7. `HotbarWatch.lua`: debounce rebuilds (0.25 s coalescer, never inside `Update`), bump `gen` only when the
   map signature changes, 2 s sweep compares signature before rebuilding.
8. Register bar-family events (UNIT_ENTERED_VEHICLE, UNIT_EXITED_VEHICLE, UPDATE_VEHICLE_ACTIONBAR,
   UPDATE_OVERRIDE_ACTIONBAR, UPDATE_BONUS_ACTIONBAR) -> invalidate bindings + HotbarWatch dirty.
9. SavedVariables `DB.SchemaVersion` + `Migrate(DB)` before `MergeDefaults` (`Bridge.lua:231-240,1638-1647`).
10. `Layout` clamps `OffsetX/OffsetY/CellSize` to the screen on `UI_SCALE_CHANGED`/`DISPLAY_SIZE_CHANGED`.
11. `Reader.lua:1238-1247` + `HotbarWatch.lua:77,81`: slots 13-24 and 61-72 both map to MULTIACTIONBAR1.
    Worker verifies against the documented Blizzard slot layout (cite the source); change only with
    evidence, otherwise add a comment and a harness pin. One shared slot->command table for both files.
12. Fix stale comments (`Bridge.lua:6` "43 cells", `Reader.lua:227-228` "proto=2").
Not in scope: melee-in-combat probe change, dead profile sliders (decision owed), Assisted Combat source.
Acceptance: harness pins that fail without the change: cooldown event does not wipe the bind cache;
FrameKey is stable for HP changes inside a band and changes when a band is crossed; memo returns one
`SpellVariants` evaluation per tick. `lua tests/secret_harness.lua` all pass, `luac -p` clean, `ability_audit` clean.

### A2 - Windows engine / IO (owner: `app/MaxDpsCompanion/**` EXCEPT `Scheduler/**`, `Decision/**`, `Knowledge/**`, `PixelProtocol.cs`; `tests/MaxDpsCompanion.Tests/**`)
1. `KeySender.SendToWindow` (`KeySender.cs:24-56`): `try/finally` so the up path always runs; check every
   `PostMessage`/`SendInput` result; return `false` (-> `AttemptOutcome.WindowLost`) if any down failed;
   fix `lParam` sign-extension (`unchecked((nint)(uint)v)`, :51); add extended-key flag for right Ctrl/Alt
   and numpad Enter where the VK allows; extract a pure `BuildLParam(vk, up)` and unit-test it.
2. **Modifier chords** (user uses them) - AMENDED by architect review (premise "WoW ignores posted modifiers" is
   UNPROVEN in this repo, so the proven path stays the default): new `[Window] ModifierInput=Post|Auto|SendInput`,
   **default `Post` (= today's behaviour, zero regression)**. `Auto`: a stroke with Ctrl/Alt/Shift uses `SendInput`
   when the game is foreground, else falls back to `Post` with a visible hold note. `SendInput` forces it.
   Hard requirements for the SendInput path (all unit-tested with an injectable poster/probe):
   a. never release a modifier the user physically holds: query `IsDown(0x10/0x11/0x12)` first, press/release ONLY
      the modifiers not already down;
   b. if the user physically holds a modifier the stroke does NOT contain (e.g. Shift held, stroke Ctrl+X) defer with a
      hold note (extend the hold-collision rule at `RotationEngine.cs:~1189/1374/1447` and `MovementGuard.cs:~269` to modifiers);
   c. re-check foreground immediately before `SendInput`; short hold; up events always in `finally`; check the
      `SendInput` return count (UIPI/elevated game returns 0) -> failure, release anything already pressed;
   d. own-injection guard: record per-VK "own injection" window (send start .. end + ~30 ms); `ScanUserKeys`
      (`RotationEngine.cs:985-1003`) updates its previous-down state but NEVER queues during it; test with a scripted probe;
   e. panic-release: on `Stop()`/exit/pause send key-up for Ctrl/Shift/Alt only if our own injection is outstanding;
   f. the key-sender seam signature (also in Core `IPlatform`) carries `ModifierInput mode, bool gameForeground`;
   g. status line shows the background-chord note; settings section `[Window]` documented in `settings.ini` with a comment
      (default stays `Post`; the tracked default file is only extended by the new commented key).
   Live test owed with a control: the same chord under `Post` and `Auto`/`SendInput`, foreground and background.
3. `ScreenSampler.cs:90-95,114-119`: delete `UpdateExt3Mirror` (nothing in the Windows app reads it) so a frame
   is decoded once. Delete dead `SampleV1/SampleRegion/SampleRegionV1` only if no caller (grep tests too).
4. `RotationEngine.cs:521-528`: publish the toggle mirror as ONE immutable reference (or write ints before the volatile flag).
5. `RotationEngine.cs:~1081` `TrySendUserPress`: call `_scheduler.NoteExternalSend(now)` like lines 1466/1546.
6. `RotationEngine.cs:~1211-1216` (and ~1395-1399): focus-blocked keyboard slot -> `NoteAttempt(FocusRequired)` + `continue`, like the mouse branch.
7. `WowWindow.cs:75-88`: throttle `Process.GetProcesses()` to 1 s when no handle.
8. Duty-cycle guard (`RotationEngine.cs:~385-389`): cap the stretch at `PollIntervalMs` instead of 10x tick cost; log the `Thread.Sleep` fallback once.
9. `Stop()`: check the join result; make `Relocate` observe `_running`; dispose the sampler only after confirmed exit.
10. `ColorProfile`: UI replaces the reference (new immutable instance) instead of mutating in place; engine reads it once per tick.
11. Build `CombatContext` once per tick and pass it down (UI publish, scheduler, telemetry); cache parsed Target/Interact strokes (`MovementGuard.cs:153-185`); remove dead `_lastSlotPress`.
12. Capture robustness: before BitBlt check `IsIconic`; add black-frame / "strip occluded" status text distinct from "no pixel block".
Acceptance: all existing tests pass; new tests for (1) lParam composition + failure path with an injectable poster, (2) modifier routing decision, (5), (6), (4).

### A3 - Mac host (owner: `app/MaxDpsCompanion.Mac/**`, `build-mac.sh`, `tools/mac-signing.sh`; NOT `MaxDpsCompanion.Core`)
1. `MacApp.cs:181-185`: failed permission check must not start the engine; UI shows the missing permission.
2. `MacKeySender.cs:103-108`: never fall back to the global HID tap for keyboard strokes; return `false`.
3. `MacVkMap.cs`: add numpad, OEM punctuation, Home/End/PgUp/PgDn/Ins/Del, F13-F24; log "unmapped VK n" once per VK.
4. Tick purity: `MacBlockScanner.cs:160-161` -> `RequestRegion` + supervisor, no blocking `SetRegion` on the tick;
   update the Ext3 mirror in stream mode (`MacScreenSampler.cs:~203`); build `Trace` only under diagnostics (`:181-185`).
5. `MacApp`: persist settings (`Save`) and add a minimal settings pane for pause/target/interact keys; `Start()` off the UI thread (`MacEngineHost.cs:98-109`).
6. Swift (`native/MdcCapture.swift`): lock `cacheError` (:743-748), free retired buffers after two generations (:88-90,124-128). Delete dead AppleScript dialog code (`Program.cs:100-118`).
7. `build-mac.sh`: `${DOTNET:-dotnet}` instead of the hardcoded user path; plist version from `app/Version.props`; run `native/selftest.swift`. Document signing/notarization steps (Developer ID + hardened runtime) in `app/MaxDpsCompanion.Mac/README.md`; notarization itself stays OWED.
Acceptance: `dotnet build app/MaxDpsCompanion.Mac -c Release` 0 warnings / 0 errors; Swift cannot be compiled on this Windows box -> OWED on a Mac.

### A4 - CI + repo hygiene (owner: `.github/**`, `.gitignore`)
GitHub Actions: windows-latest job (`dotnet build/test`, Lua 5.4 `luac -p` + harness, `ability_audit.ps1`);
macos-latest job (`dotnet build` Core + Mac, `swiftc` compile of `MdcCapture.swift`). No secrets. Cannot be
executed locally -> OWED until the first GitHub run. Do not touch `HANDOVER.md` here (Wave D owns it).

## Wave B - Core consolidation, stages 1-2 (owner: `app/MaxDpsCompanion.Core/**`, both csproj, tests csproj). Starts after Wave A is green. `code-worker-max`.
Safety gates (architect): both projects use `namespace MaxDpsCompanion`, so a leftover Windows copy shadows Core with only warning
CS0436 -> **delete the Windows duplicates and add the ProjectReference in ONE commit, `<WarningsAsErrors>CS0436`** for the wave.
Before B1 record baselines: `dotnet test` count (expected unchanged), replay filter 0 mismatches, sha256 list of every Core `.cs` vs
the Windows same-path file (commit the exact identical list; only identical files are deleted in B1; differing files go to B2 with an
explicit diff review). B2 is ONE superset file per commit with the full bar green, `PixelProtocol.cs` first (architect-trigger path;
Ext4/`TypingActive` decode must stay byte-identical on the wire). Before deleting `UpdateExt3Mirror`/`SampleV1` (A2.3) grep Core + Mac.
1. Windows csproj `ProjectReference` -> Core (`InternalsVisibleTo` for `MaxDpsCompanion` and `MaxDpsCompanion.Tests`);
   delete the 33 byte-identical copies from the Windows project. Zero behaviour change; full test bar must stay at the same count.
2. Make Core the **superset** of the additive shared types: Ext4 decode + `TypingActive` in `PixelProtocol`, `[UserQueue]` in
   `AppSettings`, `ToggleSync.MainAllowsExecution`, `ReplayRunner`/`TelemetryEvent` user-kind + `Note`, `ColorLearner` widths.
   Delete the Windows duplicates of those files. Drop the redundant `System.Drawing.Common` package from Core (`IPlatform.cs:11-15`).
Gate: replay filter 0 mismatches, `PixelProtocolExt4Tests`, `ToggleSyncTests` unchanged and green.

## Wave C - one engine - **NO-GO in this run** (architect). Decided after Wave B ships; the user may stop at B. If resumed it is split:
C1 characterisation tests against the CURRENT Windows engine (fake platform) first; C2 port to Core behind the adapters; C3 delete the
Windows `RotationEngine`; `SendGate` extraction is a separate later step, never combined with the port. A2 already adds the tests for
the individual fixes. Original scope kept below for reference. (owner: Core `RotationEngine` + Windows platform adapters + new tests). `code-worker-max`.
1. Core `RotationEngine` gains the user-press path (`UserPressQueue`, `KeyStateProbe`, `Ext4Assembler`, `HotbarMap`), the 4.0.4 Main master
   gate, the typing veto, and every A2 fix, all behind `IPlatform`/`IClock`/`IPhysicalKeys`.
2. Windows adds `Platform/Win*.cs` adapters (window, sampler, key sender, physical keys, clock); the Windows `RotationEngine.cs` is retired.
   Extract one `SendGate` (replaces the 4 copies at `RotationEngine.cs:1180-1223/1365-1405/1443-1463/1514-1543`) and split status formatting out.
3. New fake-platform tests for the send/capture seam (the I/O surface that is currently untested); a Mac/Core test project covering `MacVkMap`, `MacPixelRepair`, sampler grid math.
Risk: this is the only wave that can regress the Windows live path. Proceed only if Waves A+B are green; user may stop here.

## Wave D - identity, docs, publish (after C). `code-worker-high` + smart.
Bump to **4.1.0 "Paragon"**: `MaxDpsCompanion.csproj` Version/Codename, `Native.cs` fallback, `ReleaseIdentityTests`, `app/Version.props`
(Core + Mac become 4.1.0, no longer 3.7.9), `build-mac.sh`, Exp `MaxDpsBridgeExp.toc` + `VERSION.txt` + `MDBX.VERSION` (4.1.0-exp),
repo `VERSION.txt`, README, ARCHITECTURE, docs/UI.md, docs/TESTING.md (new section: modifier-chord live test; Paragon live checklist),
docs/PROTOCOL.md untouched. HANDOVER.md: move history to `docs/archive/HANDOVER-history.md`; keep a short current-state file.
`pwsh tools/publish-exp.ps1` and `tools/install-addon-exp.ps1` and git push/tag are run only after the user confirms.

## Deferred (needs a user decision / wire change -> 4.2)
Assisted Combat (`C_AssistedCombat`) as a bridge source (conflicts with AGENTS rule "bridge only encodes what MaxDps suggests"),
on-screen suggestion icon, cooldown/aura/AoE/resource signals on the wire, Windows Graphics Capture backend, dead profile
sliders (`MinTtkSec`, Solo HP bands: publish or delete), melee probe in combat via `IsSpellInRange`.

## OWED live (retail 12.1)
Modifier chords with `ModifierInput=Auto` foreground and background; `KeySender` failure path with an elevated game; focus-blocked slot no longer starves lower ranks; no double press after a queued user press; strip-occluded message; addon frametime (before/after `/run` profiler or CPU addon metrics); Mac items (permissions, key delivery, notarization).
