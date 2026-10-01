namespace MaxDpsCompanion;

/// <summary>Three-valued observation. UNKNOWN is a first-class result (Midnight secret values, missing data).</summary>
internal enum TriState
{
    Unknown = 0,
    Yes = 1,
    No = 2,
}

/// <summary>
/// Where the observed player HP came from (v3.0.0). Plain cell 27 wins; when it
/// is secret/unknown a valid Ext2 HP curve supplies a band-derived reading.
/// </summary>
internal enum HpSource
{
    /// <summary>No usable reading (plain unknown and no valid curve).</summary>
    Unknown = 0,
    /// <summary>The bridge's plain HP percent (cell 27).</summary>
    Plain,
    /// <summary>The Ext2 HP curve (cell 35), band-derived and quantised.</summary>
    Curve,
}

/// <summary>What the player is doing right now (protocol v5 cast sensor).</summary>
internal enum PlayerCastState
{
    Unknown = 0,
    None = 1,
    Casting = 2,
    Channeling = 3,
}

/// <summary>
/// Everything the policy layer may observe for one tick. Built from the decoded
/// protocol v5 frame only — no invented game state. On a v4/v1 frame every
/// field is UNKNOWN and the policy fails open (see <see cref="Unknown"/>),
/// which keeps behaviour identical for a stale in-game addon.
///
/// Midnight secret rules: the bridge already degrades each sensor to UNKNOWN
/// before it reaches the wire, so nothing in this class can hold a secret
/// value. The companion never calls a game API.
/// </summary>
internal sealed class CombatContext
{
    /// <summary>Whether a usable player HP reading exists (plain or curve).</summary>
    public bool HpValid { get; init; }

    /// <summary>Where <see cref="HpPct"/> came from (plain cell 27 &gt; curve &gt; unknown).</summary>
    public HpSource HpSource { get; init; } = HpSource.Unknown;

    /// <summary>Player health percent (only meaningful when <see cref="HpValid"/>).</summary>
    public int HpPct { get; init; }

    /// <summary>
    /// Upper bound of the player health percent. Exact for a plain reading; for
    /// a curve reading it is the top of the band (the overheal guard must not
    /// assume the low end of the quantisation).
    /// </summary>
    public int HpPctUpper { get; init; }

    /// <summary>Player cast/channel state from the bridge's arg-blind unit events.</summary>
    public PlayerCastState Cast { get; init; } = PlayerCastState.Unknown;

    /// <summary>Is the current enemy target casting (arg-blind UNIT_SPELLCAST events on the target).</summary>
    public TriState TargetCasting { get; init; } = TriState.Unknown;

    /// <summary>Is the target's live cast interruptible (may stay UNKNOWN when Blizzard hides it).</summary>
    public TriState TargetCastInterruptible { get; init; } = TriState.Unknown;

    /// <summary>Is the current target inside melee reach (CheckInteractDistance follow index).</summary>
    public TriState TargetInMelee { get; init; } = TriState.Unknown;

    public bool TargetHpValid { get; init; }
    public int TargetHpPct { get; init; }

    /// <summary>
    /// v3.2.0 TTK passthrough: true when the engine's <see cref="TtkEstimator"/>
    /// produced a trustworthy per-target time-to-kill this tick. False on every
    /// legacy/replay frame and when [TimeToKill] Enabled=0 — all TTK gates are
    /// then skipped (documented fail-open).
    /// </summary>
    public bool TtkValid { get; init; }

    /// <summary>Estimated seconds remaining on the current target (clamped 0..300).</summary>
    public double TtkSec { get; init; } = TtkEstimator.MaxTtkSec;

    /// <summary>Last observed target-HP fraction (band midpoint); engine-owned, read-only here.</summary>
    public double TargetHpFrac { get; init; }

    /// <summary>
    /// v3.6.0 dying-trash guard: true when the estimator has only an early
    /// (provisional) rate for the current target while <see cref="TtkValid"/>
    /// is still false. Provisional never holds minors — the policy gates it
    /// like a valid estimate only for the majors named in the spec.
    /// </summary>
    public bool TtkProvisional { get; init; }

    /// <summary>Seconds since the first observation of the current target (0 with no target).</summary>
    public double TargetAgeSec { get; init; }

    /// <summary>
    /// True while the fast-pack latch is set (two fast kills inside the latch
    /// window). Survives target swaps; the policy's documented fail-open
    /// exception reads it for the age&lt;4s grace hold.
    /// </summary>
    public bool FastPackLatch { get; init; }

    /// <summary>True when a valid OR provisional estimate exists — the gate the waste guard reads.</summary>
    public bool EffectiveTtkKnown => TtkValid || TtkProvisional;

    /// <summary>
    /// v3.7 adaptive TTK: true when the rolling kill window holds at least
    /// <c>[TimeToKill] HistoryMinKills</c> kills, so the learned burn rate may
    /// gate a cooldown. False on every legacy/replay frame and with History=0.
    /// </summary>
    public bool TtkHistBinding { get; init; }

    /// <summary>v3.7: the window's pessimistic p-quantile burn-rate (frac/s); 0 when not binding.</summary>
    public double TtkHistRate { get; init; }

    /// <summary>
    /// v3.7: the history-blended estimate in seconds (clamp 0..300); 0 when no
    /// usable history value was produced this tick. The adaptive-need hold reads
    /// this instead of <see cref="TtkSec"/> while the window is binding.
    /// </summary>
    public double TtkHistSec { get; init; }

    /// <summary>v3.7: the history value is history-only (no trusted live rate yet).</summary>
    public bool TtkHistProvisional { get; init; }

    /// <summary>v3.7: number of kills currently in the rolling window (0 when not binding).</summary>
    public int TtkHistKills { get; init; }

    /// <summary>
    /// v3.7: the configured adaptive-need duration factor
    /// (<c>[TimeToKill] HistoryDurFactor</c>, clamp 0..1). The engine threads the
    /// setting in; a manually-built context falls back to the estimate's own
    /// value so tests/replays stay deterministic.
    /// </summary>
    public double TtkHistDurFactor { get; init; }

    /// <summary>Per-slot in-range tri-state from the bridge's IsSpellInRange probe.</summary>
    public TriState[] SlotRange { get; init; } = new TriState[PixelProtocol.SlotCount];

    /// <summary>
    /// Per-slot "the suggested spell's own helpful aura is on the player" —
    /// tri-state. The bridge sets <see cref="BuffProbeValid"/> only when every
    /// probe ran without a failure/secret degradation; on legacy frames the
    /// whole block is UNKNOWN. The buff gate treats Unknown as "not active"
    /// (documented fail-open, v2.7 §20): the trigger to consider the ability is
    /// MaxDps's own recommendation, and this gate is only waste prevention.
    /// </summary>
    public TriState[] SlotBuffActive { get; init; } = new TriState[PixelProtocol.SlotCount];

    /// <summary>False when the bridge could not compute the self-buff block (failed/secret probe, or legacy encoder).</summary>
    public bool BuffProbeValid { get; init; }

    /// <summary>
    /// Additive (v2.3, wire version stays 5): MaxDps defensive urgency (the vendor GlowDcurve evaluated
    /// at the player HP fraction, staged at its own control points). Unknown
    /// on v5/v4/v1 frames and when the HP probe degraded.
    /// </summary>
    public DefensiveUrgency DefensiveUrgency { get; init; } = DefensiveUrgency.Unknown;

    /// <summary>
    /// Additive (v2.3): stagger-curve urgency for stagger-based abilities
    /// (Purifying Brew). Unknown when the stagger probe failed; the policy
    /// then mirrors the vendor's own fallback to the HP curve.
    /// </summary>
    public DefensiveUrgency StaggerUrgency { get; init; } = DefensiveUrgency.Unknown;

    /// <summary>
    /// Additive (v2.3): the Defensive slot carries a catalog gap-fill candidate
    /// (MaxDps named no bound defensive) rather than a MaxDps suggestion.
    /// </summary>
    public bool DefensiveCatalogSource { get; init; }

    /// <summary>True when the Defensive slot's ability was recommended by MaxDps itself.</summary>
    public bool MaxDpsDefensiveRecommendation => !DefensiveCatalogSource;

    /// <summary>
    /// Additive (Stream 3, v3.3.0): true when the Offensive slot's candidate is
    /// a companion-derived gap-fill (id membership in the per-spec generated
    /// offensive list) rather than a MaxDps wire suggestion. The wire carries no
    /// offensive source bit, so this is derived at projection time from the
    /// decoded class/spec + the Offensive slot's spell id. Diagnostic/parity
    /// only — the policy verdict is unchanged, and the Defensive path keeps its
    /// explicit wire bit (<see cref="DefensiveCatalogSource"/>) byte-identical.
    /// </summary>
    public bool OffensiveDerivedGapFill { get; init; }

    /// <summary>
    /// Resolves the Offensive source explicitly: the projected derived bit OR
    /// the id-membership fallback for manually-built contexts (tests/replay).
    /// Diagnostic/parity only; it never changes a verdict by itself.
    /// </summary>
    public bool IsOffensiveGapFill(int spellId, AbilityCatalog catalog) =>
        OffensiveDerivedGapFill || catalog.IsOffensiveGapFill(Class, Spec, spellId);

    public string? Class { get; init; }
    public string? Spec { get; init; }

    /// <summary>False when the bridge could not compute the v5 sensor block (or frame is v4/v1).</summary>
    public bool ContextValid { get; init; }

    /// <summary>The all-unknown context used for v4/v1 frames and stale sessions.</summary>
    public static CombatContext Unknown() => new()
    {
        HpValid = false,
        Cast = PlayerCastState.Unknown,
        TargetCasting = TriState.Unknown,
        TargetCastInterruptible = TriState.Unknown,
        TargetInMelee = TriState.Unknown,
        SlotRange = new TriState[PixelProtocol.SlotCount],
        SlotBuffActive = new TriState[PixelProtocol.SlotCount],
        ContextValid = false,
    };

    /// <summary>
    /// Projects a decoded frame onto the policy context. A frame without the
    /// v5 sensor block (v4/v1, or a failed sensor tick) yields
    /// <see cref="Unknown"/> so every rule degrades to its safe fallback.
    /// </summary>
    public static CombatContext FromFrame(BridgeFrame frame) => FromFrame(frame, hpCurve: true);

    /// <summary>
    /// Projects a decoded frame onto the policy context.
    /// <paramref name="hpCurve"/> mirrors <c>[Intelligence] HpCurve</c>: when
    /// false the Ext2 curve fallback is ignored entirely (plain HP only).
    /// </summary>
    public static CombatContext FromFrame(BridgeFrame frame, bool hpCurve)
    {
        if (!frame.ContextValid) return Unknown();

        var hpSource = HpSource.Unknown;
        var hpPct = 0;
        var hpPctUpper = 0;
        if (frame.HpPct >= 0)
        {
            // Plain cell 27 always wins (precedence: plain > curve > unknown).
            hpSource = HpSource.Plain;
            hpPct = frame.HpPct;
            hpPctUpper = frame.HpPct;
        }
        else if (hpCurve && frame.HpCurveValid && frame.HpCurveBand >= 0)
        {
            var band = frame.HpCurveBand;
            hpSource = HpSource.Curve;
            hpPct = (int)Math.Round(band * 100.0 / 15.0);
            hpPctUpper = Math.Min(100, (int)Math.Round((band + 0.5) * 100.0 / 15.0));
        }

        // Vendor curve staging: only when MaxDps's own nibble is unknown AND
        // the curve supplied the HP — a plain HP read never invents urgency
        // the addon did not send.
        var urgency = frame.DefensiveUrgency;
        if (urgency == DefensiveUrgency.Unknown && hpSource == HpSource.Curve)
            urgency = UrgencyFromHp(hpPct);

        return new CombatContext
        {
            HpValid = hpSource != HpSource.Unknown,
            HpSource = hpSource,
            HpPct = hpPct,
            HpPctUpper = hpPctUpper,
            Cast = frame.Cast,
            TargetCasting = frame.TargetCasting,
            TargetCastInterruptible = frame.TargetCastInterruptible,
            TargetInMelee = frame.TargetInMelee,
            TargetHpValid = frame.TargetHpPct >= 0,
            TargetHpPct = Math.Max(0, frame.TargetHpPct),
            SlotRange = frame.SlotRange,
            SlotBuffActive = frame.SlotBuffActive,
            BuffProbeValid = frame.BuffProbeValid,
            Class = frame.ClassName,
            Spec = frame.SpecName,
            DefensiveUrgency = urgency,
            StaggerUrgency = frame.StaggerUrgency,
            DefensiveCatalogSource = frame.DefensiveCatalogSource,
            OffensiveDerivedGapFill = AbilityCatalog.Default.IsOffensiveGapFill(
                frame.ClassName, frame.SpecName, frame.SpellId(Slot.Offensive)),
            ContextValid = true,
        };
    }

    /// <summary>Vendor curve control points: HP &lt;=30 Red, &lt;50 Orange, &lt;100 Yellow, 100 White.</summary>
    private static DefensiveUrgency UrgencyFromHp(int hpPct) =>
        hpPct <= 30 ? DefensiveUrgency.Red
        : hpPct < 50 ? DefensiveUrgency.Orange
        : hpPct < 100 ? DefensiveUrgency.Yellow
        : DefensiveUrgency.White;

    /// <summary>
    /// A copy with the engine's per-target TTK estimate attached (v3.2.0). The
    /// estimator lives in <c>RotationEngine</c>; this is a pure passthrough so
    /// the policy/providers can read it without a scheduler signature change.
    /// </summary>
    /// <param name="historyDurFactor">
    /// Optional v3.7 override for the adaptive-need duration factor
    /// (<c>[TimeToKill] HistoryDurFactor</c>). When null the estimator's own
    /// <see cref="TtkEstimate.NeedDurFactor"/> is used, so the old one-argument
    /// call keeps working unchanged (legacy/replay paths).
    /// </param>
    public CombatContext WithTtk(TtkEstimate estimate, double? historyDurFactor = null)
    {
        var durFactor = historyDurFactor ?? estimate.NeedDurFactor;
        if (TtkValid == estimate.Valid && TtkSec.Equals(estimate.TtkSec)
            && TtkProvisional == estimate.Provisional && TargetAgeSec.Equals(estimate.AgeSec)
            && FastPackLatch == estimate.FastPackLatch
            && TtkHistBinding == estimate.HistBinding
            && TtkHistRate.Equals(estimate.HistRate)
            && TtkHistSec.Equals(estimate.HistTtkSec)
            && TtkHistProvisional == estimate.HistProvisional
            && TtkHistKills == estimate.HistKills
            && TtkHistDurFactor.Equals(durFactor)) return this;
        return new CombatContext
        {
            HpValid = HpValid,
            HpSource = HpSource,
            HpPct = HpPct,
            HpPctUpper = HpPctUpper,
            Cast = Cast,
            TargetCasting = TargetCasting,
            TargetCastInterruptible = TargetCastInterruptible,
            TargetInMelee = TargetInMelee,
            TargetHpValid = TargetHpValid,
            TargetHpPct = TargetHpPct,
            SlotRange = SlotRange,
            SlotBuffActive = SlotBuffActive,
            BuffProbeValid = BuffProbeValid,
            DefensiveUrgency = DefensiveUrgency,
            StaggerUrgency = StaggerUrgency,
            DefensiveCatalogSource = DefensiveCatalogSource,
            OffensiveDerivedGapFill = OffensiveDerivedGapFill,
            Class = Class,
            Spec = Spec,
            ContextValid = ContextValid,
            TtkValid = estimate.Valid,
            TtkSec = estimate.TtkSec,
            TargetHpFrac = estimate.TargetHpFrac,
            TtkProvisional = estimate.Provisional,
            TargetAgeSec = estimate.AgeSec,
            FastPackLatch = estimate.FastPackLatch,
            TtkHistBinding = estimate.HistBinding,
            TtkHistRate = estimate.HistRate,
            TtkHistSec = estimate.HistTtkSec,
            TtkHistProvisional = estimate.HistProvisional,
            TtkHistKills = estimate.HistKills,
            TtkHistDurFactor = durFactor,
        };
    }

    /// <summary>
    /// A copy with one slot's range replaced. Used by the scheduler to evaluate
    /// the Ext2 SelfHeal2 alternate with its OWN range probe (cell 28 B) instead
    /// of the primary SelfHeal slot's (cell 31 R).
    /// </summary>
    public CombatContext WithSlotRange(int slot, TriState value)
    {
        if (slot < 0 || slot >= SlotRange.Length || SlotRange[slot] == value) return this;
        var range = (TriState[])SlotRange.Clone();
        range[slot] = value;
        return new CombatContext
        {
            HpValid = HpValid,
            HpSource = HpSource,
            HpPct = HpPct,
            HpPctUpper = HpPctUpper,
            Cast = Cast,
            TargetCasting = TargetCasting,
            TargetCastInterruptible = TargetCastInterruptible,
            TargetInMelee = TargetInMelee,
            TargetHpValid = TargetHpValid,
            TargetHpPct = TargetHpPct,
            SlotRange = range,
            SlotBuffActive = SlotBuffActive,
            BuffProbeValid = BuffProbeValid,
            DefensiveUrgency = DefensiveUrgency,
            StaggerUrgency = StaggerUrgency,
            DefensiveCatalogSource = DefensiveCatalogSource,
            OffensiveDerivedGapFill = OffensiveDerivedGapFill,
            Class = Class,
            Spec = Spec,
            ContextValid = ContextValid,
            TtkValid = TtkValid,
            TtkSec = TtkSec,
            TargetHpFrac = TargetHpFrac,
            TtkProvisional = TtkProvisional,
            TargetAgeSec = TargetAgeSec,
            FastPackLatch = FastPackLatch,
            TtkHistBinding = TtkHistBinding,
            TtkHistRate = TtkHistRate,
            TtkHistSec = TtkHistSec,
            TtkHistProvisional = TtkHistProvisional,
            TtkHistKills = TtkHistKills,
            TtkHistDurFactor = TtkHistDurFactor,
        };
    }
}
