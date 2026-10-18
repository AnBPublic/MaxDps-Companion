# 4.2.0 "Nightblade" - rogue CC intelligence (no wire change)

Date: 2026-10-18. Route: **L** (touches `Knowledge/**`, `docs/PROTOCOL.md` semantics,
`addon/MaxDpsBridgeExp/**`, the Windows engine). Research:
[`docs/research/CC_DR_12.1.5.md`](../research/CC_DR_12.1.5.md).
Release identity: 4.1.0 "Paragon" -> **4.2.0 "Nightblade"** (never reuse
Vigil/Warden/Fullcover/Holdfast/Onslaught/Reaver/Vindicator/Templar/Gallant/
Steadfast/Unyielding/Lumen/Bulwark/Aegis/Paragon). `PROTOCOL` stays v5 +
Ext2/Ext3/Ext4, 51 cells, every cell byte-identical. `addon/MaxDpsBridge`
(stable) + `dist\` + `vendor\` + `companion-rs\` are NOT touched.

## User request (verbatim intent)

- Kidney Shot / Blind / similar abilities intelligence.
- Never automatically attack enemies affected with Blind (sheep, Gouge, ...) or
  similar break-on-damage CC.
- Never use a stun together with an interrupt; use stun abilities as a fallback
  to "interrupt" an enemy when the normal interrupt is not possible for a cast.
- Never use a stun twice on an enemy that is immune to stun; take diminishing
  returns into account before re-casting Kidney Shot (12.1.5 research).

## Scope (approved)

1. **Role split.** `CrowdControlCatalog` gains `CcRole`
   (`InterruptFallback` / `Proactive` / `Manual`) and per-row metadata
   (`BaseDurationMs`, `BreaksOnDamage`, `RequiresStealth`, `RequiresOutOfCombat`,
   `Priority`). `AutoEligible` is derived from the role: auto CC is ONLY the
   single-target stun/silence interrupt fallback (Kidney Shot 408, Hammer of
   Justice 853, Storm Bolt 107570, Asphyxiate 221562, Mighty Bash 5211,
   Intimidation 19577, Silence 15487, Solar Beam 78675; Ring of Peace stays the
   AoE silence-zone exception). Blind/Gouge/Sap/Polymorph/Fear/Hex/Repentance
   and every other break-on-damage row are manual-only and can never be
   generated, even with the global CC toggle ON. Rogue generated `cc = { 408 }`;
   the old order `{ Gouge, Blind, Sap, Kidney }` is gone.
2. **12.1.5 DR ladder.** `CrowdControlDiminishing`: window = 20 s from the END
   of the last application (12.1.0 raised 16 s -> 20 s; 12.1.5 unchanged),
   multipliers 1 / 0.5 / 0.25 / 0.125 / 0.0625 (1/16 floor), PvP fully immune
   after 2 applications, PvE minimum useful stun 1.0 s. Kidney Shot uses the
   conservative 2 s base (live 2-6 s by combo points; combo points are not on
   the wire). Applied to the **delegated** Kidney Shot path too: the ability is
   registry `Incomplete` and routes to MaxDpsRotation, so
   `PolicyEvaluator.Known` calls `CrowdControlVetoes.InterruptFallbackVeto`
   before any provider and records the application centrally.
3. **Interrupt/stun discipline.** Casting-only for every auto CC row; explicit
   `TargetCastInterruptible == No` skips; a blind stun is never generated. A
   stun sent on a confirmed cast that leaves the target still casting ~800 ms
   later sets `PolicyMemory.StunIneffective` until target loss / out of combat.
4. **Break-on-damage guard.** `PolicyMemory` tracks `CcBreakGuardUntilMs` and a
   manual hold. A manual Blind/Gouge/Sap/Polymorph/Fear (recognised through the
   Ext4 hotbar press path, or a companion send) holds Main/Offensive/Interrupt
   for the row's expected duration, capped at 10 s. `[CrowdControl] BreakGuard`
   (default 1), `ManualHoldMs` (3000-30000, default 8000), `HoldHotkey`
   (default `Ctrl+Shift+C`). Console top-bar "CC Hold" item + global hotkey
   toggle the manual hold; status shows the countdown. Defensive/self-buff and
   item slots stay usable. The companion never presses a break-on-damage CC.

Out of scope: no new wire cell, no protocol bump, no stable-addon changes, no
addon-side DR state (unobservable), no automatic party-CC detection.

## Why "fail open" is not enough here, and why it is still safe

The target's live DR/immunity state is unobservable (secret auras, no combat
log). The companion therefore tracks **its own** applications, uses the
conservative PvP 2-application cap on every target, and fails open only when
data is unknown. Two live-play heuristics cover the rest: the auto CC pool
skips boss targets in the addon (`IsBossTarget`, fail-open) and the
stun-ineffective latch catches inherent immunity after ~800 ms.

## File map

| Area | Files |
| --- | --- |
| Registry/role | `app/MaxDpsCompanion.Core/Knowledge/CrowdControlCatalog.cs`, `Knowledge/abilities.json` (notes only) |
| Gate/DR/vetoes | `CrowdControlVetoes.cs`, `CandidateProviders.cs`, `PolicyEvaluator.cs`, `AbilityCatalog.cs` |
| Engine/scheduler | `Scheduler/ActionScheduler.cs` (Observe/NoteSent/Reset), `app/MaxDpsCompanion/RotationEngine.cs` (press hook, manual hold, properties) |
| Settings/UI | `AppSettings.cs`, `settings.ini`, `Ui/Shell/Win32Host.cs`, `ShellWindow.*.cs`, `ConsoleHomeView.cs` |
| Addon (generated/identity) | `addon/MaxDpsBridgeExp/Catalog.lua`, `Bridge.lua`, `Exp/Core.lua`, TOC, `VERSION.txt`, `tests/.../fixtures/Catalog.lua` |
| Docs | `docs/research/CC_DR_12.1.5.md` (new), `ARCHITECTURE.md`, `HANDOVER.md`, `docs/KNOWLEDGE.md`, `docs/TESTING.md`, `VERSION.txt`, `README.md` |

## Acceptance criteria

- Kick ready + target casting -> Kick only, never Kidney Shot in the same cast.
- Kick on cooldown + casting -> Kidney Shot (2nd application only at 50% and
  only if the expected stun is >= 1 s; 3rd held).
- PvP 3rd stun inside 20 s held (DR 2/2 immune).
- Blind/Gouge/Sap never auto-cast, regardless of `[CrowdControl] Enabled`.
- Own manual Blind/Gouge/Sap/Polymorph/Fear holds damage slots for the
  expected duration; a manual press still goes through.
- Party Polymorph covered by the manual hold hotkey/button.
- Explicit uninterruptible cast -> nothing. Boss/immune -> no auto stun, and
  the latch prevents a second auto stun until target loss.
- No CC active -> rotation output unchanged (1072 tests green).

## Verify bar (offline evidence, 2026-10-18)

- `dotnet build app/MaxDpsCompanion/MaxDpsCompanion.csproj -c Release`:
  0 warnings / 0 errors.
- `dotnet test -c Release` from `tests/MaxDpsCompanion.Tests`: 1072 passed /
  0 failed (includes 12 new guard/DR/manual-only tests).
- `lua tests/secret_harness.lua`: PASS set, no throws.
- `luac -p addon/MaxDpsBridgeExp/*.lua`: 9 files OK.
- `pwsh tools/ability_audit.ps1`: 0 violations / 0 warnings; exit 2 is the
  documented stable-Catalog drift (stable addon is frozen and still carries the
  old `cc` list).
- `MaxDpsCompanion.exe --ui-smoke-test --exp`: PASS, title
  "MaxDPS Companion v4.2.0 Nightblade".
- Exp `Catalog.lua` byte-matches the regenerated generator output; the test
  fixture is regenerated and pinned.

Live retail 12.1.5 checks (see `docs/TESTING.md` 3o) remain **OWED**:
static != automated test != live in-game E2E. A passing build never closes a
"needs retail run" item.
