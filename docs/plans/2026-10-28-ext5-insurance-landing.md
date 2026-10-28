# 4.6.0 "Cadence" - Ext5 wire, interrupt insurance, GCD landing

Date: 2026-10-28
Base: `master` a72d702 (4.5.8 "Voidlight", bridge 4.5.4)
Wire: v5 core 35 + Ext2 40 + Ext3 43 + Ext4 51 stay **byte-identical**.
New additive Ext5 block = cells 51-57; strip grows to **58 cells**; version
nibble stays `5`. Every feature ships **default OFF** and is enabled by the user
in `dist\settings.ini` (or `/mcb ext5 on` for the addon).

## 0. Deviations from the approved research plan (all evidence-backed)

| # | Approved plan | Shipped | Why |
|---|---|---|---|
| D1 | Ext5 presence = cell 28 B bit4 | presence = **cell 50 R bit0** | Wire channels are 4-bit nibbles (`PixelProtocol.cs:547`); cell 28 B has no bit4. Cell 50 R is documented reserved-0 and is outside every checksum (`PROTOCOL.md:535`, `Bridge.lua:901`). |
| D2 | cast **remaining** bucket | cast **age** bucket | `UnitCastingInfo` is never called: all returns are secret (`Reader.lua:115-125`). Only the event cast-start stamp exists (`TargetCastSince`). A stun cancels a cast at any point, so age is sufficient for insurance. |
| D3 | opener gated on elite + max-HP | gated on **rank 6 (worldboss) or 7 (elite+`UnitLevel == -1`)** | Max-HP is not on the wire today. `elite` is the common delve classification; firing Recklessness/Avatar on trash is the cooldown waste the owner rejected for improvement 3. |
| D4 | Ext5 carries a CC index only | Ext5 carries the **full CC entry (key + spell id)** | A synthesized CC needs a keystroke. The bridge writes the slot-6 key for whichever spell it puts in slot 6; when the kick occupies it the CC key is absent. Index-only would make insurance unpressable. Reuses Ext4's proven key/id packing. |
| D5 | landing fires at the cell-53 **midpoint** | landing fires at the bucket's **upper edge** (`now + GcdRemainMinMs + 100`) | The bucket is a 100 ms LOWER bound, so the midpoint is not a safe press: the true remaining time can be up to 100 ms longer than the bucket floor. The upper edge converts that into a 0-100 ms **late** bias, which the client's ~400 ms spell queue absorbs, while an early press can be dropped. Recorded again in `docs/PROTOCOL.md` §Ext5, and the 10-150 ms learned lead trims transport latency on top. |

Not in scope: enemy count, revives, defensive-policy change, cast-remaining
time, max-HP on the wire, third-party addon patching, Delve preset.

## 1. Ext5 layout (58 cells)

Presence: `cell 50 R bit0 == 1` **and** Ext4 present **and** capture length
>= 58. A pre-Ext5 addon leaves it 0 and never grows past 51 cells; a pre-Ext5
companion cannot decode >51 cells, so the addon-side `Ext5` DB key (default
**false**) is the compatibility switch: with it off the bridge still emits 51
cells and every existing companion keeps working.

| Cell | Nibbles | Meaning |
|---|---|---|
| 51 | R | bits0-2 target rank: 0 unknown, 1 trivial, 2 normal, 3 elite, 4 rare, 5 rareelite, 6 worldboss, 7 elite-skull (`UnitClassification=="elite"` and `UnitLevel("-1")`) |
| 51 | G | bit0 in-delve (0 unknown, 1 no... use 0 unknown / 1 no / 2 yes in bits0-1), bits2-3 reserved |
| 51 | B | reserved 0 |
| 52 | R | bits0-1 interruptible (0 unknown, 1 no, 2 yes), bits2-3 reserved |
| 52 | G | cast age bucket 0-15: 0 unknown/none, 1..14 = `[(b-1)*250, b*250)` ms, 14 saturates; only valid when cast kind is cast/channel |
| 52 | B | bits0-1 cast kind (0 unknown, 1 none, 2 cast, 3 channel), bits2-3 reserved |
| 53 | R | GCD remaining bucket: 0 unknown, 1 not on GCD, 2..15 = `[(b-2)*100, (b-1)*100)` ms, 15 saturates >=1300 ms |
| 53 | G/B | reserved 0 |
| 54 | R/G/B | CC entry key: `vkHi`, `vkLo`, keyFlags (bit0 Shift, bit1 Ctrl, bit2 Alt, bit3 valid) - identical packing to Ext4 (`PixelProtocol.cs:776-781`) |
| 55 | R/G/B | CC spell id top: `(idTop & 3)`, `id19_16`, `id15_12` |
| 56 | R/G/B | CC spell id low: `id11_8`, `id7_4`, `id3_0` |
| 57 | R/G/B | commit: `R = 0`, `G = sum(R,G,B nibbles of cells 51-56) mod 16`, `B = heartbeat` |

All-zero = UNKNOWN. Secret or unreadable probe => 0; never attempt arithmetic
on a value before `C_Secrets.ShouldCooldownsBeSecret()` has been checked.

Degrade matrix:

| Case | Result |
|---|---|
| Ext5 addon key off (51 cells) | today's frame, byte-identical |
| Ext5 on, pre-Ext5 companion | **not rejected**: that companion samples its own 51-cell width and decodes the 51-cell prefix (cells 0-50 keep every checksum; cell 50 R bit0 is outside all of them), i.e. a normal Ext4 frame with Ext5 ignored. Nothing breaks; the new inputs are simply unknown to it. |
| Presence clear / len < 58 | `Ext5 == null`; insurance, opener, cell-53 landing inert; estimator landing still works |
| Cell-57 checksum or heartbeat failure | drops **only** Ext5; core/Ext2/Ext3/Ext4 unaffected |

## 2. Packages and ownership (no two writers in one file)

**A - bridge (Lua only).** `addon/MaxDpsCompanionBridge/Bridge.lua`,
`Reader.lua`, `tests/secret_harness.lua`. Adds `DB.Ext5` (default false),
`CELL_COUNT = 51 or 58`, `WriteExt5()` after `WriteExt4()`, `/mcb ext5 on|off`,
`/mcb status` prints `ext5=<0|1>`, and a `Reader.GetExt5()` probe returning
`{rank, delve, interruptible, castKind, castAgeMs, ccKey, ccSpellId, gcdRemainingMs}`.
Reuses the existing `CcExtras`/`ExtraCandidates` machinery to pick the first
ready+bound AutoEligible CC and its binding. Age from `TargetCastSince`
(`Reader.lua:1425-1430,1557-1573`). GCD from `C_Spell.GetSpellCooldown(61304)`
start/duration only when the secret predicate is false (`Bridge.lua:596-601`).
`VERSION.txt`/TOC are bumped in the release commit, not here.

**B - Core decode/context/width (first).** `Core/PixelProtocol.cs`
(`CellCountExt5=58`, `Ext5PresentCell=50`, decode + `Diagnose`),
`Core/Intelligence/CombatContext.cs` (+ `Ext5` field, `FromFrame`, `WithTtk`,
`WithSlotRange`), `app/MaxDpsCompanion/ScreenSampler.cs`,
`MaxDpsCompanion.Mac/MacScreenSampler.cs`, `MacBlockScanner.cs`,
`Core/AppSettings.cs`, `Core/Telemetry/TelemetryEvent.cs` + `ReplayRunner.cs`,
`Ui/Shell/AdvancedDiagnosticsView.cs`, `tests/TestV5Frames.cs`, protocol tests,
`docs/PROTOCOL.md`.

**Contract B publishes (C and D code against this):**

```csharp
public sealed record Ext5Block(
    int Rank, int Delve, int Interruptible, int CastKind,
    int CastAgeBucket, int CcIndexUnused, KeyStroke CcKey, int CcSpellId, int GcdBucket)
{
    public static readonly Ext5Block Unknown;      // every field UNKNOWN
    public bool BossGrade => Rank is 6 or 7;
    public int CastAgeMinMs { get; }               // -1 when UNKNOWN/none
    public int GcdRemainMinMs { get; }             // -1 when UNKNOWN
}
BridgeFrame.Ext5 : Ext5Block?
CombatContext.Ext5 : Ext5Block?
```

**C - policy insurance.** New `Core/Knowledge/InterruptInsurance.cs`;
`PolicyEvaluator.cs`, `CrowdControlVetoes.cs`, `AbilityCatalog.cs`,
`tests/.../InterruptInsuranceTests.cs` plus the policy/CC suites.
API: `bool InterruptInsurance.TryBuild(PolicyInput, Ext5Block?, KickState,
long nowMs, out PolicyDecision decision, out KeyStroke stroke, out int spellId)`.
Reuses `InterruptFallbackVeto` (casting-only, DR, immunity, `StunIneffective`)
and the central `NoteUsed` record. Never bypasses a veto.

**D - scheduler and engines.** `Core/Scheduler/ActionScheduler.cs` (landing
planner, `KickState` plumbing), new `Core/Scheduler/KickMemory.cs`, new
`Core/Scheduler/LandingPlanner.cs`, `Core/RotationEngine.cs`,
`app/MaxDpsCompanion/RotationEngine.cs`, `tests/.../LandingPlannerTests.cs`,
`ActionSchedulerTests`, `ActionSchedulerPolicyTests`.

D owns the injection point: when `InsuranceEnabled` and C returns a build, D
appends it to the slot-6 candidate list ahead of Main and sends its own stroke.

## 3. Algorithms

**Insurance.** Fire iff: toggle on; **companion CC opt-in on** (`CrowdControlGate`,
the same gate the delegated CC provider requires); in combat; `TargetCasting ==
Yes`; Ext5 `cast kind != 1` (none) and `interruptible != 1` (no) - the Ext5
duplicate is cross-checked against the core triple; `Interruptible != No`; CC
entry valid (`key.Valid`, spell id > 0); and `(no interrupt candidate) OR
KickMemory.Failed(now) OR castAge >= InsuranceMinCastAgeMs`. Cast age bucket 0
=> age term false (fail closed on the age path only). The stroke uses the
existing Interrupt-slot send gates; the GCD exemption is **per spell** (only a
catalogued `GcdKind.OffGcd` ability bypasses the GCD, so a GCD-riding CC waits
for the GCD like any other GCD stroke). Latch `(castSince, spellId)` until the
cast ends or 600 ms after send; the latched candidate is not stale-demoted.
`InsuranceEnabled=0` means `TryBuild` is never called and output is
byte-identical.

**KickMemory.** Per-scheduler instance (never a static). On `NoteFailure` for a
slot-6 stroke whose spell id is **not** a curated crowd-control row (i.e. the
wire kick, never a synthesized insurance press or a delegated CC):
`FailedUntilMs = now + 1500` (the existing first rung). Cleared on target
change, out of combat, or a confirmed GCD. Recorded in telemetry on the tick
that consumed it.

**Landing (Landing=Auto).** States Idle -> Armed -> Fired/Window.
`gcdEnd` = cell 53 **upper edge** (`now + remainMin + 100`; deviation **D5** -
a 0-100 ms late bias, never early) when live, else `lastOnGcdStart + estimatedGcdMs`
(EWMA over observed on-GCD edge gaps, seed 1500 ms, clamp 750-2000 ms).
Fire when `now >= gcdEnd - leadMs`, exactly once per GCD. Abort if the head
candidate identity or verdict changes between Arm and Fire. A pre-fire that
starts no GCD enters the existing `RejectDetectMs=600` window. `MinKeyIntervalMs`
keeps gating every unscheduled stroke. Latency EWMA: alpha 0.2, seed 40 ms,
clamp 10-150 ms, sample = observed on-GCD edge - send time, from `NowMs` only.
The Fired latch and the latency sample are committed only when the stroke was
actually emitted (no latch, no sample, no telemetry for a held pre-fire).

**Poll cadence (AdaptivePolling=1).** 16 ms while `predEnd - now` is within
[-50, +300] ms; configured `PollIntervalMs` otherwise. Inserted before
`DutyCycleWait` so the duty guard still caps total work. macOS shares Core.

**Opener (OpenerRelease=1).** New rising-edge detector on `InCombat`. When the
edge coincides with `Ext5.BossGrade` (rank 6 or 7), open a 2500 ms window: drop
the OOC gap-fill hold immediately and pre-rank one ready Offensive major ahead
of the first Main. Rank UNKNOWN, elite, rare, normal => today's behaviour.

## 4. Settings (all default OFF)

| Key | Range | Default |
|---|---|---|
| `[Interrupt] InsuranceEnabled` | 0/1 | 0 |
| `[Interrupt] InsuranceMinCastAgeMs` | 250-3000 | 1000 |
| `[Scheduler] Landing` | Off/Auto | Off |
| `[Scheduler] AdaptivePolling` | 0/1 | 0 |
| `[Scheduler] OpenerRelease` | 0/1 | 0 |

One Advanced toggle row per key; ranges clamp on load and on save.
`InsuranceEnabled` additionally requires the companion CC opt-in
(`CrowdControlEnabled`): with CC off the predicate denies with
`insurance: companion CC is off`, exactly like the delegated CC provider.

## 5. Tests

- B: width gate accepts 35/40/43/51/58 and rejects 54/56 (`PixelProtocolExt3Tests:80-88`);
  Ext5 golden packing, checksum fail, torn heartbeat, 51-cell frame => `Ext5 == null`,
  pre-Ext5 companion contract; `ScreenSampler`/Mac scanner width ladder; telemetry round-trip.
- C: 2^6 predicate matrix, DR ladder, `StunIneffective`, immune target, no CC entry,
  key-invalid => no fire, `InsuranceEnabled=0` byte-identical, latch behaviour.
- D: one pre-fire per GCD, abort on identity change, estimator convergence,
  EWMA clamps, failed pre-fire enters 600 ms, unscheduled stroke still min-interval,
  opener positive (worldboss/elite-skull) and negative (elite/rare/unknown),
  adaptive wait selection.
- A: bucket edges, secret => 0, `ShouldCooldownsBeSecret` true skips arithmetic,
  `Ext5=false` keeps cell 50 R 0 and 51 cells, `/mcb ext5` round-trip.
- Release: `ReleaseIdentityTests`, `ReleaseNotesTests`, fixture replay with timing fields.

Commands: `dotnet build app/MaxDpsCompanion -c Release` (0/0),
`dotnet test -c Release`, `lua tests/secret_harness.lua`, `luac -p`,
`pwsh tools/ability_audit.ps1`, `--ui-smoke-test`, `--ui-smoke-test --exp`,
`--bench-scheduler`, `--replay` fixtures. Live retail stays **OWED**.

## 6. Release hygiene

One commit: `docs/CHANGELOG.md` section `## 4.6.0 Cadence - 2026-10-28`,
`ReleaseNotes.cs` `All[0]`, `VERSION.txt`, `addon/.../VERSION.txt`, TOC, csproj
`<Version>4.6.0</Version>` / `<Codename>Cadence</Codename>`. Also
`HANDOVER.md`, `ARCHITECTURE.md`, `docs/TESTING.md` (live-OWED checklist),
`README.md` settings pointer.

## 7. Risks

| Risk | Mitigation |
|---|---|
| 58-cell anchoring/locator on live screen | Ext5 addon key default off; 51-cell path untouched; live test OWED |
| Secret-mode hides GCD/cast | every field UNKNOWN => today's behaviour; never arithmetic first |
| CC key packing drift | reuse Ext4's exact packing + golden tests |
| Pre-fire wastes a press | abort-on-change, one per GCD, failed pre-fire enters the existing window |
| Opener wastes a major on trash | rank 6/7 only; elite/unknown keep today's behaviour |
