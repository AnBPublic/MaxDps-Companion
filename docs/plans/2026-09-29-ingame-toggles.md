# In-game 13-toggle UI — spec (2026-09-29)

Reader-frozen: `PixelProtocol.cs` unchanged, no wire layout/version change, no companion C# edits.
Per-ability (~3000) toggles out of scope.

## Goal

`/mdb` overlay + `Esc > Options > MaxDps Bridge` panel with the same 13 toggles as the
companion hero (8 Spells + Solo / Out-of-combat / Auto-target / Auto-interact / TTK).
App baseline all-ON; addon only restricts: `effective = app AND addon`, addon OFF wins.

## Files

- New `addon/MaxDpsBridge/Toggles.lua` — pure logic, no frames (harness-loadable):
  `MDB.Toggles.Get/Set/Flip/Keys/Label/SlotAllowed/Snapshot`, DB `MaxDpsBridgeDB.Toggles`
  (13 booleans, default true) + `Ui{Overlay,Point,X,Y,Scale,Minimap}`. Missing key = ON.
- New `addon/MaxDpsBridge/Panel.lua` — settings panel (Settings.RegisterCanvasLayoutCategory,
   InterfaceOptions_AddCategory fallback) + draggable overlay (`/mdb overlay [on|off]`), plain frames only.
- `Bridge.lua` — gates in `Update`, slash commands, status text, defaults merge.
- `Options.lua` — thin entry (Enable + CellSize + "Toggles…" button).
- `MaxDpsBridge.toc` + `VERSION.txt` → 3.3.0. Order: Catalog, Keymap, Bars, Reader,
  Toggles, Bridge, Options, Panel.
- `tests/secret_harness.lua` — 13 OFF cases, nil ctx, secret HpPct.
- Docs: `docs/PROTOCOL.md` (semantics note, no layout change), `ARCHITECTURE.md`,
  `HANDOVER.md`.

## Gates (all in Update, never Reader logic)

- Slots 1-8: `X = SlotAllowed(n,ctx) and X or nil` before `WriteSlot`; slot 3 also clears
  `DefCatalog`; slot 8 nils Heal1+Heal2 (Ext2 blank, range 0). Skip Reader call when OFF.
- OOC: `ctx.InCombat == false` (strict, reuse StatusFlags bit) blanks slots 1-8.
- Solo OFF: blank Defensive(3)+SelfHeal(8) while not grouped (`IsInGroup` pcall);
  emergency exception: HpPct <= EMERGENCY_HP (match CandidateProviders:494) does NOT blank;
  unknown HP = allow. HpPct only from sanitized `MDB.GetPlayerHpPct()`.
- AutoTarget OFF suppresses STATE_NEED_TARGET → Idle; AutoInteract OFF suppresses
  STATE_NEED_INTERACT → Idle. Flags untouched.
- TTK OFF forces target HpBand = 15 (unknown) in WriteTarget; MeleeFlag/CastFlags kept.
  Collateral: execute/band consumers also go blind — documented.

## Slash / status

`/mdb toggles`, `/mdb overlay [on|off]`, `/mdb <key> [on|off]` (main/offensive/defensive/
consumable/trinket/interrupt/mobility/selfheal/solo/ooc/autotarget/autointeract/ttk),
 `/mdb all on|off`; `status` line gains `toggles=<line>` + `Toggles: <line>`; `/mdb why heal` explains
slot 8/3 blank reason via `MDB._LastBlank[slot]`.

## Secret-safety

Gates test booleans/type()/sanitized HpPct only; every game API pcall; nil/unknown = allow;
`SlotAllowed` pcall-wrapped fail-open; no protected calls, no TargetUnit/InteractUnit.

## Verify

`luac -p`, `lua tests/secret_harness.lua`, `dotnet build -c Release` 0/0 (no app changes),
`dotnet test -c Release`, `pwsh tools/ability_audit.ps1`, bench hash unchanged,
`git diff --stat` shows no app/vendor/settings.ini changes. Live retail OWED.
