# No-downtime MAIN (never strand the rotation on an empty Main slot)

Date: 2026-10-03 · Route: L (protected `addon/*.lua`, `Scheduler/**`) · Wire: no change

## Problem

When every glowing spell is a denied major CD or is power/cooldown-gated,
`GetMainSpellID` returns nil, the Main slot is absent, and the scheduler can
reach `Hold` even though the spec has a perfectly usable baseline filler. The
rotation must keep a legal key every tick (`MainReprobeMs`), never a dead tick.

## Layers

- **A — Reader `MainUsable` filter, `Reader.lua:302-321` (helper) +
  `Reader.lua:377-399` (scan).** Inside the `SpellsGlowing` scan, collect every
  number id with `On == 1` that is not `MajorCDDeny`-denied, **sort ascending
  and skip power-starved picks** (`IsSpellUsable(ID)` false AND `noPower` true;
  Q3 = YES), then take the first still usable id. `IsSpellUsable` is a
  read-only API call, pcall-wrapped; never a protected call.
- **B — Scheduler re-probe, `Scheduler/**`.** While the encoder reports an
  empty Main, re-probe the bridge every `MainReprobeMs = 400`. If the same
  spell id yields no state change (`MainSameSpellNoOpCap = 3`) stop re-pinging
  and surface `NoCandidate` for the Main slot only; other slots keep
  evaluating. Holds stay non-latching — every tick re-evaluates.
- **C — Tracker TTL refresh (Q2 = YES).** `CandidateTracker` entries carry a
  TTL; while the frame heartbeat stays fresh the **sole Main identity's** TTL is
  refreshed rather than dropped, so a briefly unseen-but-usable spell can
  re-enter without a full stall. A second Main identity or a frozen heartbeat
  refreshes nothing and expires exactly as before.

## User decisions

- **Q1 = NO.** An empty Main is **not** acceptable. This overrides the
  architect recommendation (which left empty Main as a legal hold state).
- **Q2 = YES.** Do the T3 Tracker TTL refresh.
- **Q3 = YES.** `IsSpellUsable` is allowed in the Reader filter.

## Fallback registry (user-approved hard-rule exception)

Per Q1, when no usable glowing spell survives, the bridge must still encode a
safe rotational filler. New `addon/MaxDpsBridge/MainFallback.lua` (or a table
in `MajorCooldowns.lua`) maps spec → ordered filler spellIDs: non-cooldown,
non-power-gated baseline abilities only. Example: Fury → Bloodthirst when all
glows are power-starved. Selection order: **usable glowing first, then fallback
filler, then nil.** IDs must be verified by spell NAME against `vendor/`
`MaxDps/SpellData.lua` — never invented; unmatched specs stay nil and are
listed as OWED in `HANDOVER.md`. This documents a deliberate, user-approved
exception to the "bridge only encodes what MaxDps suggests" hard rule; it is
a custom behaviour, not upstream parity.

## Tests

- `tests/secret_harness.lua`: glowing all-denied ⇒ Main falls through to the
  fallback id, never nil for a registered spec.
- Reader unit: `IsSpellUsable=false` id is skipped, next id wins.
- Scheduler: repeated identical empty-Main probes stop at `MainSameSpellNoOpCap`.
- Build `dotnet build -c Release` (0/0); `luac -p addon/MaxDpsBridge/*.lua`.

## Risks

- Wrong/renamed fallback ids in 12.1 → guarded by name verification; live
  retail mapping stays OWED (static ≠ live).
- `IsSpellUsable` on a denied/nil id — pcall + nil-check both.
- Fallback spam on power-starved specs — only fires when Main would be empty,
  and `MainSameSpellNoOpCap` bounds repeats.
- Hard-rule exception must be called out in `HANDOVER.md` + `ARCHITECTURE.md`.
