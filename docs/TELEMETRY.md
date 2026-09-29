# Local rotation telemetry + replay (format v1)

Opt-in, local-only record of what the Companion saw and did each tick, plus a
deterministic replay that re-runs every recorded decision context through
`DecisionEngine`. Purpose: diagnose why the Companion selected or failed to
select an action during a real session without reproducing the situation.

> **Privacy contract.** Every recorded value originates in this process or in
> the decoded pixel protocol: player-bound key Strokes, engine state flags,
> our own status messages and monotonic timestamps. No Blizzard API return
> values, no secret/protected data, no character names, no chat, no memory
> reads, no OCR, and no network. Raw cell colours are deliberately not
> recorded — a failed decode is classified as a `fault` instead. Telemetry is
> **off by default** (`[Telemetry] Enabled=0`).

## Recording

Nothing is written to disk while recording. Events are serialized once into a
**bounded in-memory ring** (default 10 000 events ≈ 8.5 MB ≈ 5-6 minutes at
30 Hz); when full, the oldest event is evicted and counted as `dropped`. The
buffer never grows forever. Export is an explicit user action.

- Enable: Advanced → **Telemetry** checkbox, or `settings.ini`:
  ```ini
  [Telemetry]
  Enabled=1
  Capacity=10000
  ```
- Press **Start** (recording happens on the engine thread).
- Advanced → **Export…** writes `telemetry-<timestamp>.jsonl` where you choose.
  Export does not stop recording and does not clear the buffer.
- Detach/re-attach with the checkbox keeps the buffered events.

## JSONL event format (v1)

One JSON object per line, UTF-8, `\n`-terminated; null sections are omitted.
`fmt` is the format version this document specifies.

```json
{"fmt":1,"kind":"session","seq":1,"tMs":0,"utc":"2026-09-27T18:00:00.0000000Z","proto":5,"link":true,"note":"start","app":"2.3.0","events":10000}
{"fmt":1,"kind":"send","seq":2,"tMs":10000,"utc":"2026-09-27T18:00:10.0000000Z","send":{"what":"spell","slot":"Main","key":{"vk":69,"sh":false,"ct":false,"alt":false},"intervalMs":121,"sp":185358}}
{"fmt":1,"kind":"tick","seq":3,"tMs":10000,"utc":"2026-09-27T18:00:10.0000000Z","proto":5,"state":"Active","hb":3,"inCombat":true,"onGcd":false,"hasTarget":true,"link":true,"note":"sending","slots":[{"vk":69},{"vk":50},null,null,null,null,null,null],"cands":[{"slot":"Main","key":{"vk":69,"sh":false,"ct":false,"alt":false},"en":true,"act":true,"seen":8000,"chg":8000,"pressedMs":0,"pressed":false}],"dec":{"src":"intelligence","sel":"Main","reason":"MainRotation","conf":70,"stale":false,"order":["Main","Offensive"]},"pol":{"fresh":true,"sel":"Main","reason":"MainRotation","conf":70,"suppressed":0,"held":1,"skipped":0,"detail":"no incoming cast detected","hp":73,"hpKnown":true,"cast":"None","melee":"No","tcast":"No","tint":"No","ctx":true,"rang":"IOUUUUUU","buff":"00000000","du":"Yellow","dsu":"Unknown","dsrc":true,"opts":{"solo":false,"em":35,"sus":65,"esc":60,"on":"1856"},"verdicts":[{"slot":"Defensive","spell":23920,"v":"Hold","r":"no incoming cast detected"},{"slot":"Main","spell":185358,"v":"Use","r":"MaxDps main candidate; actionable"}]},"staleAfterMs":1500}
{"fmt":1,"kind":"link","seq":5,"tMs":60000,"utc":"2026-09-27T18:01:00.0000000Z","link":false,"note":"no pixel block - client must be windowed or borderless","fault":"Checksum"}
```

| Field | Kind | Meaning |
| :--- | :--- | :--- |
| `fmt` | all | Format version, always `1`. |
| `kind` | all | `session` \| `tick` \| `send` \| `link`. |
| `seq` | all | Monotonic event sequence within the recording. |
| `tMs` | all | Monotonic engine-clock milliseconds (session-relative). |
| `utc` | all | Wall-clock ISO-8601 for human correlation only. |
| `proto` | tick, session | Decoded protocol version (5 current; 6 accepted forward-compatible; 4/1 stale addon). |
| `state` | tick | `Idle` \| `Active` \| `Paused` \| `NeedTarget` \| `NeedInteract`. |
| `hb` | tick | Bridge heartbeat nibble (frozen ⇒ addon stopped rendering). |
| `inCombat` / `onGcd` / `hasTarget` | tick | Status-cell flags (NeverSecret booleans from the protocol). |
| `link` | tick, link, session | Whether a frame decoded / recording attached. |
| `note` | tick, link, session | The plain status line the UI showed that tick. |
| `err` | tick, link | Engine-side exception message (never a decode failure). |
| `fault` | tick, link | `Length` \| `Magic` \| `Contrast` \| `Version` \| `State` \| `Commit` \| `Checksum` — why a sample did not decode. |
| `slots` | tick | 8 entries (Main/Off/Def/Cons/Trin/Int/Mobility/SelfHeal): decoded keybind or null. |
| `cands` | tick | Candidate snapshot: `slot`, `key`, `en` (slot toggle), `act` (not movement-bound), `seen`/`chg` (observation window), `pressedMs`/`pressed` (press history). |
| `dec` | tick | `src` (`intelligence` \| `legacy`), `sel`, `reason`, `conf`, `stale`, `order`. |
| `pol` | tick | **Policy record (v2.0; extended v2.1, v2.3, v2.6):** `fresh` (the scheduler ran this tick), `sel`/`reason`/`conf` (plan head, e.g. `CastHold`/`ChannelHold`/`PolicyHold`), `suppressed`/`held`/`skipped` counts, `detail` (first hold/skip reason), `hp`/`hpKnown`, `cast`, `melee`, `tcast` (target cast), `tint` (target interruptibility), `ctx` (sensor block valid), `rang` (8-char per-slot range: `U`/`I`/`O`), `buff` (8-char self-buff bits), `du` (v2.3 HP-curve defensive urgency string), `dsu` (v2.3 stagger-curve urgency string), `dsrc` (v2.3 catalog gap-fill source bool; omitted when false), `opts` (the exact policy options the policy ran with), `verdicts` = per-candidate `{slot, spell, v (Use/Hold/Skip/Unavailable/Unknown - v2.6 five-state), r (reason)}`. This is the explainability record: why every situational ability was or was not pressed. Present only when the scheduler actually ran (`fresh:true`); legacy-path ticks carry no policy block (a stale plan is never recorded as this tick's reasoning). Hold + Unknown recompute as held, Skip + Unavailable as skipped. |
| `pol.du` / `pol.dsu` | tick | v2.3 MaxDps defensive urgency: `Unknown`/`White`/`Yellow`/`Orange`/`Red` (`du` from the HP curve, `dsu` from the stagger curve). **A policy record without `du` is a legacy pre-v2.3 record** — see Replay below. |
| `pol.dsrc` | tick | `true` when the Defensive slot was supplied by the catalog gap-fill rather than MaxDps itself; omitted when false. |
| `pol.opts` | tick | The exact policy options the record ran with: `solo`, `em`/`sus`/`esc` (the `[Solo]` thresholds) and v2.3 `on`/`off` (the `[Abilities]` explicit override lists, sorted comma lists, omitted when empty). Replay rebuilds `PolicyOptions` from this exactly. |
| `send` | send | `what` (`spell` \| `target` \| `interact`), `slot`, `key`, `intervalMs` (gap since the previous send), `sp` (sent ability spell id; omitted when unknown). |
| `staleAfterMs` | tick | The staleness window in force when the decision was made. |
| `app` / `events` | session | App version / ring capacity. |

Since protocol v5 the frame carries a 24-bit spell id per slot; it is recorded
in `pol.verdicts[].spell` (plain MaxDps table values only — never a secret).
The keybind remains the physical identity used for sends and duplicate
collapse.

## Replay

```
MaxDpsCompanion.exe --replay="<file>.jsonl"
```

writes `<file>.jsonl.replay.txt` next to the input (the app is a WinExe with
no console). The Advanced **Replay…** button does the same via a file picker.
The reader skips corrupt lines and reports them as `bad`.

For every `tick` event the runner rebuilds the exact `DecisionContext`
(candidates with their timestamps, in-combat/on-GCD/has-target, `NowMs`,
`StaleAfterMs`) and calls the unmodified `DecisionEngine.Evaluate`. Then:

- `intelligence` recordings: the recomputed result is compared with the
  recorded one field-for-field. **`mismatches: 0` is the determinism proof** —
  any other value means build/version skew or a non-deterministic change.
- `legacy` recordings: the report shows the evaluator's **`wouldsel`** — what
  the deterministic layer would have chosen on that session — without
  flagging a defect.

**Policy verdict replay (v2.1).** Every recorded `pol.verdicts` entry is also
re-derived through the unmodified `PolicyEvaluator.Evaluate`, with the exact
recorded context (`pol` fields) and options (`pol.opts`), and with
`PolicyMemory` rebuilt from the recorded `send` events. **Event order is the
live order: the engine writes a send event BEFORE the tick event of the same
tick, so the replay evaluates the tick first and applies its send afterwards —
a tick never observes its own send.** `verdicts: N policy verdicts recomputed,
0 mismatch(es)` is the policy determinism proof; mismatches are printed with
both sides. Legacy recordings (no `pol.opts`) skip verdict replay.

**Defensive urgency replay (v2.3).** `du`/`dsu`/`dsrc` and the `opts.on`/
`opts.off` lists are rebuilt into `CombatContext`/`PolicyOptions` exactly as
recorded, so a defensive verdict that depended on urgency, the gap-fill
source or a user ON/OFF override recomputes deterministically. `dsrc` absent
means the slot was a MaxDps recommendation.

**Legacy pre-v2.3 records.** A `pol` record written before v2.3 carries no
`du` field: defensive urgency was not recorded, so its verdicts cannot be
re-derived. The runner does **not** recompute them, counts them separately,
and the report appends `(N legacy pre-v2.3 policy record(s) skipped:
defensive urgency was not recorded)`. This is a version-skew accommodation,
not a mismatch — `0 mismatch(es)` still holds for the recomputed verdicts.
Offline proof: `DefensiveReplayTests.Replay_Of_A_Legacy_Policy_Record_Skips_Verdicts`.

**Self-sustain proof.** The same mechanism is the offline acceptance for Solo
mode: a tick whose `pol` record shows `sel: "SelfHeal"`,
`reason: "SelfSustain"` (or `EmergencySurvival`) with a `verdicts` entry
`{slot: "SelfHeal", spell: 202168, v: "Use", r: "solo: HP 50% below sustain
65%; self-sustain"}` is re-derived from `hp`/`hpKnown`/`opts`/`rang` alone —
including the `Hold` for `solo mode off`, the `Skip` for a confirmed
out-of-range melee heal, and the `Hold` above the threshold. The canonical
fixture is `tests/MaxDpsCompanion.Tests/fixtures/solo-warrior-selfheal.jsonl`
(replay report: `9 policy verdicts recomputed, 0 mismatch(es)`). The send
event for the heal carries `slot: "SelfHeal"` and `sp: 202168`; the plan head
names the scheduler's selection, so telemetry can prove
`candidate → USE → selected → sent` without ever reading a secret value.
The checked-in fixture is regenerated with the v2.3 format and replays with
`9 policy verdicts recomputed, 0 mismatch(es)`.

**Defensive urgency proof.** The companion fixture
`tests/MaxDpsCompanion.Tests/fixtures/defensive-warrior-urgency.jsonl` is the
offline acceptance for the v2.3 defensive layer: it records the urgency
stage, the gap-fill source and the user-policy lists, and replays with
`16 policy verdicts recomputed, 0 mismatch(es)` — including a White hold, a
Yellow short-CD use, an Orange gap-fill hold, a Red major use and a
user-OFF skip.

**Offensive-interrupt proof (v2.6).** The companion fixture
`tests/MaxDpsCompanion.Tests/fixtures/offensive-interrupt-warrior.jsonl`
covers the five-state verdicts (Unavailable / Unknown included) and replays
with `7 policy verdicts recomputed, 0 mismatch(es)`.

The report lists per tick: candidates, selected action, reason, confidence,
rejected candidates (slot disabled / movement-bound / duplicate stroke / lower
rank / stale), GCD and stale holds, the last send and its interval, and a
summary (reasons, sends, decode faults, notes). Same input ⇒ byte-identical
report.

## Overhead

- **Disabled (default)**: a null check at the `Report`/`TrySendOne` seams and
  a few field resets per tick — nanoseconds, below measurement noise.
- **Enabled**, measured on this machine with
  `--bench-telemetry=50000` (writes `bench-telemetry.txt`):

  | Path | Cost |
  | :--- | :--- |
  | capture (event model + candidate map) | 1.8-2.2 µs/op |
  | serialize (source-gen) | 7.5-7.9 µs/op |
  | append (serialize + ring store, prebuilt event) | 7.0-9.1 µs/op |
  | **end-to-end (capture + append, what the engine pays)** | **10.4-11.2 µs/tick** |
  | implied CPU at 30 Hz | **0.031-0.034% of one core** |
  | tick event (3 candidates) | 890 bytes |
  | ring at default capacity 10 000 | 8.5 MB |
  | export 10 000 events | 0.4-0.5 s (≈9 MB) |

  Sampling and sending are untouched: recording never changes what is pressed.

## Files

| Path | Role |
| :--- | :--- |
| `app/MaxDpsCompanion/Telemetry/TelemetryEvent.cs` | Event model + format v1 factories. |
| `app/MaxDpsCompanion/Telemetry/TelemetryJson.cs` | Source-generated JSONL codec. |
| `app/MaxDpsCompanion/Telemetry/TelemetryRecorder.cs` | Bounded ring + export. |
| `app/MaxDpsCompanion/Telemetry/TelemetryReader.cs` | Tolerant JSONL reader. |
| `app/MaxDpsCompanion/Telemetry/ReplayRunner.cs` | Deterministic replay + report. |
| `tests/MaxDpsCompanion.Tests/Telemetry*Tests.cs` | Serialization/ring/replay contracts. |
| `tests/MaxDpsCompanion.Tests/fixtures/sample-session.jsonl` | CLI-verifiable sample session. |
| `tests/MaxDpsCompanion.Tests/fixtures/solo-warrior-selfheal.jsonl` | Canonical Solo self-sustain recording (regenerated with the v2.3 option shape; 9 verdicts, 0 mismatches). |
| `tests/MaxDpsCompanion.Tests/fixtures/defensive-warrior-urgency.jsonl` | Canonical v2.3 defensive-urgency recording (16 verdicts, 0 mismatches; White/Yellow/Orange/Red + user-OFF + gap-fill). |
| `tests/MaxDpsCompanion.Tests/fixtures/offensive-interrupt-warrior.jsonl` | Canonical v2.6 offensive-interrupt recording (7 verdicts, 0 mismatches; five-state incl. Unavailable/Unknown). |
