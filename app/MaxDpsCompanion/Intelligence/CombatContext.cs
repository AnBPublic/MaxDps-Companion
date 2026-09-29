namespace MaxDpsCompanion;

/// <summary>Three-valued observation. UNKNOWN is a first-class result (Midnight secret values, missing data).</summary>
internal enum TriState
{
    Unknown = 0,
    Yes = 1,
    No = 2,
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
    /// <summary>Whether the player HP sensor produced a plain value this tick.</summary>
    public bool HpValid { get; init; }

    /// <summary>Player health percent (only meaningful when <see cref="HpValid"/>).</summary>
    public int HpPct { get; init; }

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
    public static CombatContext FromFrame(BridgeFrame frame)
    {
        if (!frame.ContextValid) return Unknown();
        return new CombatContext
        {
            HpValid = frame.HpPct >= 0,
            HpPct = Math.Max(0, frame.HpPct),
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
            DefensiveUrgency = frame.DefensiveUrgency,
            StaggerUrgency = frame.StaggerUrgency,
            DefensiveCatalogSource = frame.DefensiveCatalogSource,
            ContextValid = true,
        };
    }
}
