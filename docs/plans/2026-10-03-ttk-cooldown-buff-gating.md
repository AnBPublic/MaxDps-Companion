# TTK-aware offensive cooldown gating (v3.8)

Date: 2026-10-03 · Route: L (Knowledge/** approved) · Status: implemented, offline-verified.

## Goal

Stop a major offensive cooldown being spent on a target whose time-to-kill is
still unknown and may collapse (fresh trash), and make the adaptive-need gate
read the ability's own **buff duration** instead of the T2 second-use window.

## Contract

| Item | Rule |
|---|---|
| `TtkPolicy.DefaultWarmupSec` | `3.0` (app default) |
| `TtkPolicy.WarmupHoldReason` | `"warming up TTK"` |
| `TtkPolicy.BuffNeed(a, durFactor)` | `NeedAdaptive(MinTtkSec(a), a.DurationMs/1000 (0 if <=0), durFactor)` |
| `TtkPolicy.WarmupHoldHolds(a, ctx, warmupSec, durFactor)` | true iff warmupSec>0 ∧ ProvisionalWasteEligible ∧ usage≠Execute ∧ !a.KillSecure ∧ MinTtkSec>0 ∧ !EffectiveTtkKnown ∧ TargetAgeSec<warmupSec ∧ !(TtkHistBinding ∧ TtkHistSec≥BuffNeed) ∧ !ExecuteWasteBypass |
| `[TimeToKill] WarmupSec` | AppSettings default 3.0, clamp 0..10; 0 = legacy fail-open |
| `PolicyOptions.TtkWarmupSec` | default 0 (legacy) so tests/older replays are byte-identical; `FromSettings` carries 3.0 |
| Provider order | WasteGuard → GraceHold → **Warmup** → gap-fill → T2 pairing |
| WasteGuard adaptive input | `buffDurSec = DurationMs/1000` (was `(2·Cd+Dur)/1000`) |
| Telemetry | `ttkw` omitted at 0; ReplayRunner rebuilds it |

## Files

- `Knowledge/TtkPolicy.cs` — consts + `BuffNeed` + `WarmupHoldHolds`; WasteGuard XML doc.
- `Knowledge/CandidateProviders.cs` — `buffDurSec`; warmup call after grace hold.
- `Knowledge/PolicyEvaluator.cs` — `PolicyOptions.TtkWarmupSec` + `FromSettings`.
- `AppSettings.cs` + `settings.ini` — `TimeToKillWarmupSec` parse/clamp/save.
- `Telemetry/TelemetryEvent.cs` + `Telemetry/ReplayRunner.cs` — `ttkw` record/rebuild.
- `Knowledge/abilities.json` — ~29 majors curated with `cdMs`/`durMs`/`minTtkSec`
  (+ `ttkSource`); notables: Avatar 107574 cd 90 s / dur 20 s / min 15,
  Combustion min 30, Arcane Surge min 15, Voidform min 20, Tyrant min 12,
  Metamorphosis min 15, Recklessness dur 12 s / min 10, Ravager min 15.
- Tests: `TtkPolicyTests` (5 new), `TtkPolicyHistoryTests` buff-dur update,
  `TtkCurationTests` Tyrant 12, `TtkReplayTests` recording + regenerated
  `fixtures/ttk-warrior-burst.jsonl`.

## Open questions (resolved from source)

- **Q1 field names:** `AbilityOverride.CdMs`/`DurMs`/`MinTtkSec`
  (`AbilityCatalog.cs:897-898`, `:1002`, `AbilityModel.cs:1456-1501`); camelCase
  `cdMs`/`durMs`/`minTtkSec` in JSON.
- **Q2 constructors:** `PolicyOptions.FromSettings` is the only production
  constructor; `new PolicyOptions{...}` in tests/replay gets the legacy 0 default.
  Replay is made exact by recording/rebuilding `ttkw` (the v3.7 pattern).
- **Q3 target age:** `TtkEstimator._firstObsMs` is reset by `ClearTargetState()`
  on a target swap AND on an upward HP jump (`TtkEstimator.cs:353,374-380,613`);
  `TargetAgeSec` is therefore per-target and warmup intentionally re-fires on a
  swap (conservative).

## Verification (this machine)

- `dotnet build -c Release` (app) **0 warnings / 0 errors**.
- `dotnet test -c Release` **938/938** (from `tests\MaxDpsCompanion.Tests`).
- `lua tests/secret_harness.lua` **256 passed / 0 failed** (unchanged pass).
- `luac -p addon/MaxDpsBridge/*.lua` clean.
- `pwsh tools/ability_audit.ps1` exit 0 — violations 0 / warnings 0, catalog matches.

## Owed live (retail 12.1)

Pull an unmeasured target: a major shows `"warming up TTK"` for ~3 s, then fires
on a long fight; `WarmupSec=0` is legacy. Static ≠ automated ≠ live.
