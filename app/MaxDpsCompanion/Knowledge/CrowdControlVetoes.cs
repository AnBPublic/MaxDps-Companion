namespace MaxDpsCompanion;

/// <summary>
/// Companion-side CC opt-in gate (v3.4.0 CC appendix). Default OFF — CC is an
/// explicit opt-in (Solo-like safety), so a fresh install or a manually-built
/// <see cref="PolicyOptions"/> never auto-fires crowd control.
///
/// Effective authority is intended to be <c>companion AND addon</c> with an
/// addon OFF winning. The addon-side key exists (<c>MaxDpsBridgeDB.Toggles.CC</c>,
/// missing = ON, restrict-only) but no wire field carries it; until the bridge
/// gates its CC encoding on it, the companion uses this companion-side gate.
/// That cross-surface wiring is OWED live (see HANDOVER.md), not claimed here.
/// </summary>
internal static class CrowdControlGate
{
    /// <summary>Companion-side CC auto-use switch. Default false (opt-in).</summary>
    public static bool Enabled { get; set; }

    public static void Configure(bool enabled) => Enabled = enabled;

    /// <summary>Test hygiene: return to the opt-in default.</summary>
    public static void Reset() => Enabled = false;
}

/// <summary>
/// The companion's OWN recent-CC memory (never a read of the target's live DR
/// state, which Midnight does not expose). It records when the companion itself
/// applied a CC of a given DR category and blocks a second auto-application of
/// the same category inside a conservative window. A null memory, an unknown
/// category, or no recorded use fails OPEN (allow) — an unobservable state must
/// never become an invented lockout.
/// </summary>
internal sealed class CrowdControlDiminishing
{
    /// <summary>
    /// Conservative DR re-application window. Live full DR reset is ~20 s
    /// without application; using the longest common value biases toward
    /// "never chain", never toward over-firing.
    /// </summary>
    public const long DefaultWindowMs = 20_000;

    private readonly Dictionary<CcDrCategory, long> _until = new();

    public long WindowMs { get; init; } = DefaultWindowMs;

    /// <summary>True when the same category was applied by the companion inside its window.</summary>
    public bool IsDiminished(CcDrCategory category, long nowMs) =>
        category != CcDrCategory.Unknown
        && _until.TryGetValue(category, out var until)
        && nowMs < until;

    /// <summary>Records a companion CC application for the DR category.</summary>
    public void NoteUse(CcDrCategory category, long nowMs)
    {
        if (category == CcDrCategory.Unknown) return;
        var until = nowMs + WindowMs;
        if (!_until.TryGetValue(category, out var current) || until > current)
            _until[category] = until;
    }

    public void Reset() => _until.Clear();
}

/// <summary>
/// The single CC entry point consulted by <see cref="PolicyEvaluator"/> (one
/// call-site). It returns a fully stamped decision only when the CC appendix
/// applies — companion CC gate ON, a curated auto-eligible row for the current
/// class/spec, and a meaningful registry status. In every other case it returns
/// null, so the pre-existing path (manual-by-design / MaxDps authority) runs
/// byte-identically.
///
/// This is deliberately NOT a general ability veto: it never touches
/// interrupts, defensives, offensives or the main rotation.
/// </summary>
internal static class CrowdControlVetoes
{
    /// <summary>
    /// The companion's own DR memory. Non-null by default (safe: empty ⇒ fail
    /// open). Replaceable/clearable for tests. Engine-side recording is
    /// inherently local to the provider (it records on its own Use decision),
    /// so no scheduler wiring is required.
    /// </summary>
    public static CrowdControlDiminishing? Memory { get; set; } = new();

    /// <summary>Returns a stamped CC decision, or null when the normal path must run.</summary>
    public static PolicyDecision? Evaluate(PolicyInput input, AbilityDefinition ability, AbilityCatalog catalog)
    {
        if (!CrowdControlGate.Enabled) return null;
        if (ability.Status is IntelligenceStatus.Incomplete or IntelligenceStatus.Unknown
            or IntelligenceStatus.UnsafeToAutomate) return null;

        // An explicit user OFF (or Manual/Never mode) stays absolute: the CC
        // gate can only ever *add* eligibility to a default-off row, never
        // override a user's decision. Returning null lets the normal path Skip.
        if (input.Options.Abilities is { } policy)
        {
            var mode = policy.ModeOf(ability.SpellId);
            if (mode is UserAbilityMode.Never or UserAbilityMode.Manual) return null;
            if (policy.OverrideOf(ability.SpellId) == false) return null;
        }

        var entry = catalog.CrowdControlFor(input.Context.Class, input.Context.Spec, ability.SpellId);
        if (entry is null || !entry.AutoEligible) return null;

        var pinput = new ProviderInput
        {
            Input = input,
            Ability = ability,
            Catalog = catalog,
            Range = input.Context.SlotRange[(int)input.Slot],
            Fallback = input.Options.TimeToKillFallback,
        };
        return CandidateProviders.CrowdControl.Evaluate(pinput);
    }

    internal static void NoteUsed(CcDrCategory category, long nowMs) =>
        Memory?.NoteUse(category, nowMs);
}
