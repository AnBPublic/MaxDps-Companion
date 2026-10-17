# 3.7.9 "Unyielding" — MaxDps recommendation authority (offensive TTK conservation removed)

## Goal / directive

**Remove all hard blockers: if MaxDps recommends an ability as part of the core
rotation, it should be used.** A Main/Offensive ability the bridge offers is
used unless a genuine game-truth or structural gate applies — not because the
companion second-guesses the rotation with a time-to-kill guess.

## Root cause

The offensive `OffensiveCandidateProvider` layered a TTK **conservation**
cluster on top of MaxDps's own recommendation. On targets/pulls that never
produced a *valid* estimate, majors over 60 s — Avatar 107574, Ravager 228920
and peers (e.g. Recklessness) — were held despite the vendor flagging them via
`GlowCooldown`. The result: the companion suppressed exactly the cooldowns
MaxDps wanted used.

## What was removed (Exp companion, no wire change)

Removed from `OffensiveCandidateProvider`:

- **T1 waste guard** — the valid/provisional `minTtkSec` hold plus the v3.7
  `ttk-hist` adaptive branch (the `"target ~Xs to die; saving <name>"` Hold).
- **Warmup hold** — `"warming up TTK"` (`[TimeToKill] WarmupSec` /
  `WarmupHoldHolds`).
- **Grace hold** — `"fast pack, waiting for TTK"` (fast-pack latch + age <4 s).
- **Enemy-count `Uncertain` hold.**
- **Unknown-range `Uncertain` hold.**

`minTtkSec` / `holdForBurst` are now **informational metadata** for offensives.
The TTK estimator itself is **retained** — see below.

## What stays gated

- Power starvation (`MainUsable`).
- Out-of-range (`Range == No`) and melee requirement.
- Player cast/channel (execution safety) and GCD/scheduler timing.
- User `Never`/`Manual` and the addon's own toggles (addon-wins; can only
  restrict).
- The user-selected **Burst/AoE presets** (an explicit user choice, not a
  companion guess).
- Offensive structural gates: own-buff skip, the pair window (retained but
  **inert** — there are no offensive groups), the gap-fill out-of-combat hold,
  and the melee/range gates.

The TTK estimator is retained for the defensive T4 `DyingTargetHolds` and for
telemetry.

## Audit result (offline, evidence)

- 161 vendor-offensive ids across all **13 classes**, extracted from
  `vendor/MaxDps_*/Specialization/TWW/*.lua` + `vendor/MaxDps/Cooldowns.lua`.
- **0** carry `neverAutomatic` / `ManualByDesign` / `UnsafeToAutomate`.
- After the gate removal **no vendor-recommended core-rotation id remains
  hard-blocked**.
- Only `212084` Fel Devastation and `98008` Spirit Link Totem route to the
  Defensive slot by `classOf` precedence (documented deviation).
- **49/161** vendor-only rows resolve to `MaxDpsBacked` vendor catalog rows
  (not blocked).

## Test / fixture updates

- `Status`/provider policy tests updated for the removed offensive holds
  (T1/ttk-hist, warmup, grace, enemy-count/unknown-range `Uncertain`).
- Offensive `Uncertain`/hold reason pins flipped to Use where a
  game-truth/structural gate does not apply.
- TTK fixtures/replays regenerated so offensives fire on a MaxDps offer while
  the estimator/T4 feeds stay pinned.

## OWED (live retail 12.1)

1. Avatar 107574 / Ravager 228920 / Recklessness / Bladestorm (and peers) press
   on a MaxDps recommendation against a ≤15 s-TTK target **and in trash**.
2. The Burst preset still holds majors until a boss TTK is measurable.
3. Gap-fill majors still fire only when the bridge offers them (MaxDps silent).
4. Record + export + replay: 0 mismatches.
5. Static (parses/builds) ≠ automated test ≠ live in-game.

## Publish steps

1. Land the provider/policy change + flipped test/fixture pins.
2. Bump identity to 3.7.9 / Unyielding (companion + Exp bridge); stable
   `addon/MaxDpsBridge/` + `dist\` stay frozen.
3. Docs: `README.md`, `ARCHITECTURE.md`, `docs/UI.md`, `docs/KNOWLEDGE.md`,
   `docs/TESTING.md` §3k, `HANDOVER.md`, `settings.ini` comments, this spec.
4. Run the offline bar; keep the live checks OWED.
5. Deploy with `tools/install-addon-exp.ps1` + `dist-exp` publish; verify Exp
   hashes. `install-addon.ps1` / `dist\` are legacy-only.
