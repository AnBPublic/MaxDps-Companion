# 2026-10-11 — v4.0.3 "hotbar user-press priority queue" — implementation spec

> **2026-10-11 HOTFIX (live report):** the plan-suppression semantics described
> in §2 (`SuppressGcdRiding`) shipped in the first 4.0.3 build and starved the
> core rotation (Main) while a press was pending — only the BypassGcd set fired.
> Removed: **the queue never gates the plan**; it only wins the send-site race
> (pending-first ordering). `MaxPending` is **1** (newest press replaces). The
> scan edge is `down && !was` only (no shared GetAsyncKeyState low-bit latch).
> Everything else in this spec stands.

Status: approved design (user decisions 2026-10-11). Author: architect (Claude Sonnet 5.5).
Owners: code-worker A1 (C# protocol + render widths), A2 (addon Lua), B (companion watcher/queue/engine).
Route: L. Verify bar: see §5. Live checks: §6 + docs/TESTING.md §3l (OWED).

## 0) Goal (user-approved, authoritative)

Companion watches the user's physical keypresses for keys bound to hotbar action-bar slots (any bar) and delivers the stroke so it fits smoothly into the rotation window:

1. GCD-riding ability: delivered the moment the observed GCD ends, so it casts immediately in that window (today an early press is silently dropped by the client's ~400 ms spell queue — the bug being fixed).
2. Off-GCD ability (items always; spells by registry off-GCD classification): delivered immediately, even during a GCD.
3. Unknown classification → treat as GCD-riding (wait). Items → off-GCD.
4. Watched set = EVERY keybound action-bar entry (spells, items/trinkets, macros resolving to a spell). Keybind changes auto-picked up ≤ ~1 stream cycle. UI binds structurally excluded (only ACTIONBUTTON-family commands are read). WASD/Space/arrows never watched/sent (existing guard). Wheel excluded (no async key state); mouse buttons excluded v1 (SendInput echo + focus gate).
5. Ready flag per entry: non-ready presses are recognized/delivered (harmless no-op) but never defer the rotation.
6. Typing veto: addon reports chat/edit-box focus; companion drops detections while set.
7. Default ON: `[UserQueue] Enabled=1` (tracked settings.ini), toggle in Advanced Configuration.
8. New addon diagnostic `/mdbx hotbar` (slot → spell/item id → binding → ready).
9. Exp addon only (`addon/MaxDpsBridgeExp/`); stable bridge + `dist\` frozen. No memory reading. No input hooks (detection = GetAsyncKeyState polling).

## 1) Decision

Add an Ext4 additive block (51 cells) that streams a hotbar map of up to 32 entries. The addon sends it as a self-describing frame sequence, and the companion polls `GetAsyncKeyState` for the watched keys. This is the only design that meets "no memory reading, no hooks". The version nibble stays 5. No memory-reading or hook alternative fits the repo rules.

## 2) Contracts

### Ext4 wire (cell numbers are strip cells)

- Presence is cell 28 B bit3. Bit2 stays Ext3 and bits 0-1 stay SelfHeal2 range. The addon sets bit3 consistently inside the cell-34 checksum.
- Typing veto is cell 41 R bit2 (Ext3 flags cell, bits 0-1 carry mask bits 12-13, bit3 reserved). The Ext3 checksum sums the whole nibble; decoders keep masking the mask value with `&3`.
- Cell 43 is the header: R = gen (0-15), G = seq (0-15), B = flags (bit0 LAST, bit1 EMPTY, bits 2-3 = 0). EMPTY means seq 0 and LAST with no entries.
- Cells 44-46 are entry A and cells 47-49 are entry B. Each entry is 3 cells:
  - Cell 1 = `(vkHi, vkLo, flags)` using the existing keystroke layout: Shift=1, Ctrl=2, Alt=4, Valid=8. Valid=0 = pad entry.
  - Cell 2 = `(idTop, id19-16, id15-12)`. `idTop` bit3 = isItem, bit2 = ready, bits 1-0 = id bits 21-20.
  - Cell 3 = `(id11-8, id7-4, id3-0)`.
  - Id is 22 bits. Encoder skips and logs any id ≥ 2^22 (4,194,304).
- Cell 50 commit: R = 0 (reserved), G = nibble sum of cells 43-49 mod 16, B = heartbeat. Bad checksum or heartbeat mismatch drops only that frame.
- `CellCountExt4 = 51`. Strip width becomes 51 in `ScreenSampler`, `BlockLocator`, `ColorLearner`, `StripView`.
- A cycle is seq 0..N where N is the LAST frame; 32 entries = 16 frames. The addon holds each frame for at least 2 Paint ticks; repeated reads of the same gen+seq are idempotent.

### Companion decode

- `BridgeFrame` gains `bool TypingActive` and `Ext4Frame? Ext4`.
  - `TypingActive` is true only when the Ext3 block is valid and bit2 is set; Ext3 invalid → false (typing not detected).
  - `Ext4Frame` = (gen, seq, last, empty, Ext4Entry[2]).
  - `Ext4Entry` = (KeyStroke Stroke, int Id, bool IsItem, bool Ready).
- `Ext4Assembler.Feed(BridgeFrame, long nowMs)` lives in new `HotbarMapAssembler.cs`:
  - cycle starts only at seq 0; seqs contiguous within one gen; gap / gen change / torn frame discards the partial cycle;
  - LAST swaps the map atomically;
  - map dropped after 5 s with no completed cycle, or when Ext4Present false.
- `HotbarMap` has `Status` (Unavailable, OldAddon, Stale, Ok) and `IReadOnlyList<Ext4Entry> Lookup(KeyStroke)`. A stroke can map to several entries.

### Queue (pure state machine, no I/O): `UserPressQueue`

- `OnPress(KeyStroke, IReadOnlyList<Ext4Entry>, long nowMs, ctx)`
- `Poll(long nowMs, bool onGcd, bool keyDown)` returns `QueueAction` = `None`, `Deliver(stroke)` or `Drop(reason)`
- `bool SuppressGcdRiding`
- Constants: FIFO ≤2, coalesce 250 ms, confirm 600 ms, 1 retry, expiry 1.2 s.

### GCD class

A stroke is GCD-riding when any entry for it is a spell with `RidesGcd`, or has an unknown classification. Off-GCD only when every entry is an item or a known off-GCD spell. (Implementation note: confirm the existing GCD classification property name; cite `AbilityModel.cs:642` as the reference; preserve these semantics regardless of naming.)

### Settings

`[UserQueue] Enabled=1`, `Background=0`.

### Telemetry

New `kind:"user"` event with the same fields as a spell send, plus `note` = `queued` / `delivered` / `retry` / `dropped:<reason>` / `native`.

## 3) Tasks (disjoint files)

### Worker A1 — protocol, assembler, render widths (C#)

- `docs/PROTOCOL.md`: title, Ext4 section, compat matrix.
- `app/MaxDpsCompanion/PixelProtocol.cs`: constants + presence flag + typing bit + Ext4 decode.
- `app/MaxDpsCompanion/HotbarMapAssembler.cs` (new): `Ext4Frame`, `Ext4Entry`, `HotbarMap`, `Ext4Assembler`.
- `app/MaxDpsCompanion/ScreenSampler.cs`, `BlockLocator.cs`, `ColorLearner.cs`, `Ui/Controls/StripView.cs`: widths.
- New `tests/MaxDpsCompanion.Tests/PixelProtocolExt4Tests.cs` (golden frame, torn frame, gap, gen change, staleness, compat matrix, typing bit independent of Ext3 mask).

### Worker A2 — addon (Lua)

- New `addon/MaxDpsBridgeExp/HotbarWatch.lua`: walks the bars via `Bars.BarSets` / `ButtonBinding` incl. paged + bonus slots; resolves macros to spell or item; reuses `Reader.ResolveBinding` / `Keymap.ParseBinding`; ready flag via the ExtraCandidates readiness helper; rebuild on `Bars.lua` events (:167-189) + 2 s sweep; bumps gen on rebuild; streams the sequence (≥2 Paint ticks per frame); `/mdbx hotbar` (slot → id → binding → ready); ACTIONBUTTON-family commands only; sets typing bit from chat edit box + focused EditBox.
- `addon/MaxDpsBridgeExp/Exp.xml`: load order entry.
- `addon/MaxDpsBridgeExp/Bridge.lua`: create cells 43-50, encode Ext3 (cell 41 R bit2) + Ext4 nibbles, checksums.
- `tests/secret_harness.lua`: encode pins.

### Worker B — companion watcher, queue, engine, settings, telemetry, UI

- New `app/MaxDpsCompanion/KeyStateProbe.cs` (wraps `GetAsyncKeyState`, injectable for tests), new `app/MaxDpsCompanion/UserPressQueue.cs`.
- Edited: `RotationEngine.cs`, `AppSettings.cs`, `settings.ini`, `Ui/Shell/AdvancedConfigurationView.cs`, `Telemetry/TelemetryEvent.cs`, `Telemetry/ReplayRunner.cs`, `HANDOVER.md`, `ARCHITECTURE.md`, `docs/TESTING.md` §3l.
- Engine glue: scan only watched VKs at ≤10 ms steps between ticks (Loop wait slicing, `RotationEngine.cs:324-389`); edge = previous-down state OR low bit; capture modifiers at edge; exact stroke match.
- Pending-first: pending delivery goes before the Tick plan; `TrySendScheduled` / `TrySendLegacy` skip GCD-riding sends while pending; `BypassGcd` actions exempt.
- Gate order same as Tick: link, OOC, paused, window, typing bit.
- Focused: On-GCD + GCD-riding → queue, deliver on `!OnGcd` + key released; off-GCD → recognize only, never re-send (no double trinket/charge); not on-GCD → recognize only. Unfocused: deliver only if `Background=1`.
- Status strings: `UserQueue: off / unavailable(old addon) / stale / ok n keys / pending`.
- Tests: `UserPressQueueTests.cs`, engine-glue test, replay fixture (`kind:"user"` rebuilds the same policy memory as a `spell` send).

## 4) Filters (fail-closed)

| Input | Result |
|---|---|
| Not an ACTIONBUTTON-family command (UI binds) | Excluded by the addon |
| Movement keys (`MovementGuard.MovementKeys`), Space, arrows | Never watched or sent |
| VK 0, mouse VKs 1-6, wheel | Excluded |
| Shift/Ctrl/Alt VKs alone | Excluded |
| Unknown or zero id, id ≥ 2^22 | Entry skipped (encoder logs) |
| More than 32 entries | Bar order wins; addon logs overflow |
| Typing bit set | Drop the detection |
| Ext3 invalid | Typing treated as false; status says so |
| Ext4 absent or stale | Feature unavailable |

## 5) Acceptance tests (commands)

- `dotnet build app/MaxDpsCompanion/MaxDpsCompanion.csproj -c Release` → 0 warnings / 0 errors.
- `dotnet test -c Release` (from `tests\MaxDpsCompanion.Tests`) → all pass.
- `lua tests/secret_harness.lua` → all pass.
- `luac -p` on Exp (top level + `Exp/`) and stable → clean.
- `pwsh tools/ability_audit.ps1` → clean.

New coverage: Ext4 golden/torn/gap/gen/staleness; compat matrix (old addon bit3=0; 43-cell old companion ignoring new cells); typing bit independent of Ext3 mask; queue (coalesce, FIFO overflow, deliver on GCD end, 1 retry, expiry, off-GCD + unknown classification, suppression); replay fixture with `kind:"user"`.

## 6) Risks + OWED live tests

Risks: focused accepted-native press can still produce a duplicate attempt (accepted as harmless); a key bound to several entries replays all bindings; map ≤1 cycle (~0.3 s typical) stale after a bind change; 32-entry cap; bar-addon binding edge cases (ElvUI, Bartender); `GetAsyncKeyState` can miss a <10 ms press (mitigated by the low bit).

OWED for `docs/TESTING.md` §3l (none run): early press during a GCD casts at GCD end; trinket press is immediate; press while typing in chat is not sent; rebind picked up within ~1 cycle; `/mdbx hotbar` output correct; behaviour with an old addon; Exp bridge cell visibility at 51 cells; background press with `Background=1`.

## 7) Changes vs the original proposal (architect)

1. Header: R = gen, G = seq 0-15, B = flags (LAST/EMPTY). A 4-bit seq cannot carry 0-14 plus a LAST marker; 32 entries need 16 frames.
2. Entry: id is 22 bits so one nibble holds isItem + ready. 36 bits left no room; ready needs a home.
3. Typing is cell 41 R bit2 (Ext3 flags cell), not a separate field.
4. The assembler is a new file, owned by A1.
5. `TypingActive` is false when Ext3 is invalid.

## 8) Resolved confirmations (2026-10-11)

1. 22-bit id cap accepted. Encoder skips + logs id ≥ 2^22; live ids (spells < ~1.7M incl. 1237885; items ~300k) fit. No 4-cell variant.
2. Repaint cadence: bridge repaints ~30 Hz (33 ms); companion default `PollIntervalMs` 33 ms. Hold each Ext4 frame ≥2 paint ticks (≈66 ms); worst-case cycle 16 frames ≈ 1.1 s, typical ≤0.5 s.
3. 51-cell width accepted (≈408 px at cell 8). Add live check: if the strip clips a screen edge, re-anchor via `/mdbx offset`; old companion keeps decoding the 43-cell prefix.

## 9) Verified file entry points (facts pack)

- `docs/PROTOCOL.md`; `PixelProtocol.cs` CellCountExt3=43 (:197), CastFlagExt3Present=4 (:252), ClassFlagExt2Present=4 (:257), ext2/ext3 decode :594/:643; widths: `ScreenSampler.cs:138`, `BlockLocator.cs:172-174`, `ColorLearner.cs:30-32`, `Ui/Controls/StripView.cs:61`.
- `RotationEngine.cs`: Loop wait :324-389; Tick gates link :529, OOC :559, paused :575/:582, window :590, target :606, state :623, send :720; TrySendScheduled :871 (movement :918, physical-hold :927, focus :949, send :955); TrySendLegacy :987; TrySendTargetKey :1174; TrySendInteractKey :1223; telemetry appends :968/:1206/:1286.
- `MovementGuard.cs` MovementKeys :37-42, IsPhysicallyDown :269; `Native.cs` GetAsyncKeyState :204.
- `Scheduler/SchedulerModel.cs` ScheduledAction.BypassGcd :141, ScheduleReason append-only :39-44, AttemptOutcome :47-54, ScheduleInput :61.
- `AppSettings.cs` sections :64-136; `Ui/Shell/AdvancedConfigurationView.cs`; tracked `settings.ini`.
- `Telemetry/TelemetryEvent.cs`, `Telemetry/ReplayRunner.cs`.
- Addon: `addon/MaxDpsBridgeExp/Bridge.lua` (Paint/cells/Ext3 encode :649-697), `Exp.xml`, `Reader.lua` ResolveBinding :1324 / HotKeyBinding :1145 / FindSpellOnActionBar :1187 / GetKeybindForSpell :1231, `Bars.lua` ButtonBinding :53 / BarSets :29 / events :167-189, `Keymap.lua` ParseBinding; `tests/secret_harness.lua`.
