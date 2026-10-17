# MaxDPS Companion v3.7.7 "Gallant" — Architect Spec

Date: 2026-10-10
Scope: UI parity + `mdc-runtime` engine wiring. Visual/behavioral parity with
the Gallant mock. No wire/protocol change to `MaxDpsBridgeExp`.

## 1. UI layout

- Title bar: `MaxDPS Companion v3.7.7 Gallant`.
- Status pill: `{●|○} {State}·{fps}` — filled dot when Running/Paused, hollow
  when Stopped; `fps` rendered as integer samples/sec.
- Transport grid, 2x3, buttons: `Start`, `Pause`, `Stop`, `Calibrate`,
  `OPEN GAME` (spans/emphasized), plus one enable/placeholder cell.
  - Enable rules: `Start` enabled only when Stopped; `Pause` toggles
    Running↔Paused; `Stop` enabled when Calibrating/Running/Paused;
    `Calibrate` enabled when Stopped/Paused; `OPEN GAME` always enabled.
- Status card: hero state line, `Details` (session/config summary), `Now`
  (current recommendation), `Log` lines stamped `HH:mm:ss`.

## 2. Theme tokens

| Token | Value |
|---|---|
| Bg | `#0B1110` |
| Card | `#131C1A` |
| Border | `#25312E` |
| Accent | `#E8C558` |
| Danger | `#E5604F` |
| Ok | `#4CC38A` |
| MonoStamp | `#5E6B67` |

Geometry: corner radius `12`, control height `42`.

## 3. State machine

States: `Stopped`, `Calibrating`, `Running`, `Paused`.

- `Stopped -> Calibrating` on Calibrate; `Stopped -> Running` on Start.
- `Running -> Paused` on Pause; `Paused -> Running` on Pause (toggle).
- `Running|Paused|Calibrating -> Stopped` on Stop.
- `fps = 1000 / poll_ms` (integer), recomputed on each poll cycle.
- Log is a `VecDeque` capacity `200`; push_back timestamped entry, pop_front
  when full.

## 4. Engine wiring (`mdc-runtime`)

- New crate `mdc-runtime`.
- Background thread owns the engine; UI communicates via `std::sync::mpsc`.
- Tick interval `33ms`; sampler applies a `5-tap median` filter.
- Decode adapter: protocol `v5`/`v4`/`v1` auto-detection.
- Calls: `RotationEngine` (start/stop/tick), `post_key` (with `hold_ms`),
  `note_sent`, `post_mouse`.
- If the game window is `NotForeground`, skip input and log an error path
  instead of injecting.
- All engine errors are logged through the UI log channel.

## 5. Settings mapping + OPEN GAME

- Map persisted settings to engine config on start/calibrate.
- `OPEN GAME` button executes a `Command` that launches/focuses the client
  (no key injection; window focus only).

## 6. File plan

- **A** `mdc-runtime`: engine thread, tick, sampler, decode adapter, wiring.
- **B** theme + widgets: `theme.rs` tokens, transport grid, status pill,
  status card, log view.
- **C** `main` / console plumbing: app entry, state machine, mpsc pump.
- **Docs**: update `HANDOVER.md` (status/next) and `ARCHITECTURE.md`
  (pipeline/file map) in the same pass.

## 7. Verification

- `cargo build`, `cargo test`, `cargo clippy` — all clean.
- Screenshot checklist: title, pill `{●|○}`, 2x3 grid + enable states, hero,
  Details/Now/Log with `HH:mm:ss` stamps, theme token match.
- Live behavior in a retail client: **OWED** until a real run is performed
  (static/build ≠ automated test ≠ live in-game E2E).

## Risks

- egui must never block the engine thread; keep painting off the mpsc drain.
- Tick jitter vs `33ms` target; sampler median must smooth frame spikes.
- DPI scaling may distort the `42` height / `12` radius at fractional scales.

## Open questions

- Does a sampler already exist, or is it net-new in `mdc-runtime`?
- Secondary hex values for hover/active states not given by the mock.
- Row 3, right cell: intentionally empty or reserved for a 6th control?
