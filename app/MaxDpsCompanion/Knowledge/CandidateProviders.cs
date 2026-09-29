namespace MaxDpsCompanion;

/// <summary>
/// Where a candidate came from (v2.7 §11): the MaxDps wire itself, a companion
/// bridge extra, or a companion gap-fill the knowledge base generated. NONE is
/// reserved for a verdict that carries no candidate source (legacy / generic).
/// Diagnostic only — it never changes a verdict or an order.
/// </summary>
internal enum CandidateSourceKind
{
    /// <summary>The candidate is the MaxDps wire suggestion itself.</summary>
    MaxDpsWire = 0,
    /// <summary>The candidate is a companion bridge extra (companion-curated slot content).</summary>
    BridgeExtra,
    /// <summary>The candidate is a companion gap-fill produced from the knowledge base.</summary>
    CompanionGapFill,
    /// <summary>No candidate source (legacy / generic fallback).</summary>
    None,
}

/// <summary>One named evidence fact rendered for a UI "why" line. Plain decoded fields only.</summary>
internal sealed record ProviderEvidence(string Key, string Value);

/// <summary>
/// Everything a provider may read for one candidate. It wraps the existing
/// <see cref="PolicyInput"/> plus the resolved <see cref="AbilityDefinition"/>
/// and the slot range the policy already decoded.
/// </summary>
internal sealed class ProviderInput
{
    public required PolicyInput Input { get; init; }
    public required AbilityDefinition Ability { get; init; }
    public required AbilityCatalog Catalog { get; init; }
    public required TriState Range { get; init; }

    public CombatContext Context => Input.Context;
    public PolicyOptions Options => Input.Options;
    public PolicyMemory Memory => Input.Memory;
    public long NowMs => Input.NowMs;
    public Slot Slot => Input.Slot;
    public int SpellId => Input.SpellId;
    public bool HasTarget => Input.HasTarget;
}

/// <summary>
/// One explicit candidate provider: the named, traceable owner of the
/// per-category situational logic that used to live inline in
/// <see cref="PolicyEvaluator"/>. Renaming a provider must not change a single
/// verdict or reason — the provider only adds identity + evidence.
/// </summary>
internal interface ICandidateProvider
{
    /// <summary>The primary ability category this provider serves (MaxDpsRotation serves several).</summary>
    AbilityCategory Category { get; }

    /// <summary>The human-readable provider name recorded on every decision.</summary>
    string Name { get; }

    /// <summary>The default source kind for this provider's decisions.</summary>
    CandidateSourceKind Source { get; }

    /// <summary>Evaluate the category branch for the candidate. Reasons are byte-identical to the inline policy.</summary>
    PolicyDecision Evaluate(ProviderInput input);
}

/// <summary>Stamps provider identity + evidence onto a decision (shared by the evaluator and providers).</summary>
internal static class ProviderStamp
{
    public static PolicyDecision Stamp(ICandidateProvider provider, PolicyDecision decision, params string[] evidence) =>
        decision with { Provider = provider.Name, Source = provider.Source, Evidence = evidence };

    public static PolicyDecision Stamp(ICandidateProvider provider, PolicyDecision decision, CandidateSourceKind source, params string[] evidence) =>
        decision with { Provider = provider.Name, Source = source, Evidence = evidence };
}

/// <summary>
/// Resolves the provider that owns one catalogued candidate. The resolution
/// order is byte-identical to the inline dispatch it replaces; a mismatch here
/// would change which branch runs.
/// </summary>
internal static class CandidateProviders
{
    public static readonly ICandidateProvider SelfSustain = new SelfSustainCandidateProvider();
    public static readonly ICandidateProvider Defensive = new DefensiveCandidateProvider();
    public static readonly ICandidateProvider Utility = new UtilityCandidateProvider();
    public static readonly ICandidateProvider Interrupt = new InterruptCandidateProvider();
    public static readonly ICandidateProvider Offensive = new OffensiveCandidateProvider();
    public static readonly ICandidateProvider MaxDpsRotation = new MaxDpsRotationProvider();
    public static readonly ICandidateProvider Mobility = new MobilityCandidateProvider();

    /// <summary>The provider for an uncatalogued / generic candidate.</summary>
    public static readonly ICandidateProvider Generic = new GenericCandidateProvider();

    public static ICandidateProvider For(Slot slot, AbilityDefinition ability)
    {
        if (ability.Category == AbilityCategory.SelfHeal || slot == Slot.SelfHeal) return SelfSustain;
        if (ability.IsSurvival) return Defensive;
        if (ability.Purpose is AbilityPurpose.Escape or AbilityPurpose.Movement) return Defensive;
        if (ability.Purpose is AbilityPurpose.Dispel or AbilityPurpose.CrowdControl
            or AbilityPurpose.Purge or AbilityPurpose.Threat) return Utility;
        if (ability.Purpose == AbilityPurpose.Interrupt) return Interrupt;
        if (ability.Purpose is AbilityPurpose.MajorOffensive or AbilityPurpose.MinorOffensive) return Offensive;
        if (ability.Purpose is AbilityPurpose.Consumable or AbilityPurpose.Trinket) return MaxDpsRotation;
        if (ability.Purpose == AbilityPurpose.GapCloser) return Mobility;
        return MaxDpsRotation;
    }
}

/// <summary>Fallback for abilities the catalog does not know. Never selected by <see cref="CandidateProviders.For"/>.</summary>
internal sealed class GenericCandidateProvider : ICandidateProvider
{
    public AbilityCategory Category => AbilityCategory.Main;
    public string Name => "Generic";
    public CandidateSourceKind Source => CandidateSourceKind.MaxDpsWire;
    public PolicyDecision Evaluate(ProviderInput input) =>
        PolicyDecision.Hold("generic candidate; no provider rule");
}

/// <summary>
/// Main-rotation identity (MaxDps authoritative), consumables and trinkets, and
/// the fail-open tail for a catalogued ability that matches no specific rule.
/// </summary>
internal sealed class MaxDpsRotationProvider : ICandidateProvider
{
    public AbilityCategory Category => AbilityCategory.Main;
    public string Name => "MaxDpsRotation";
    public CandidateSourceKind Source => CandidateSourceKind.MaxDpsWire;

    public PolicyDecision Evaluate(ProviderInput p)
    {
        var input = p.Input;
        var ability = p.Ability;
        var ctx = p.Context;
        var slot = (int)p.Slot;

        // Main-rotation identity (rare: catalogued rotational spell).
        if (ability.Category == AbilityCategory.Main || ability.Purpose == AbilityPurpose.Rotational)
        {
            if (p.Range == TriState.No) return D(PolicyDecision.Unavailable("target out of range"), "target out of range");
            return D(PolicyDecision.Use("MaxDps main candidate; actionable"), "MaxDps main rotation");
        }

        if (ability.Purpose is AbilityPurpose.Consumable or AbilityPurpose.Trinket)
        {
            if (ability.HoldWhenBuffActive && ctx.SlotBuffActive[slot] == TriState.Yes)
                return D(PolicyDecision.Skip("ability's own buff already active"), "own buff already active");
            if (ability.Purpose == AbilityPurpose.Trinket
                && input.Memory.OffensiveActive("Trinket.OnUse", input.NowMs))
                return D(PolicyDecision.Skip("another on-use trinket was used inside the shared lockout"), "shared trinket lockout");
            return D(PolicyDecision.Use($"{Describe(ability.Purpose)} candidate; MaxDps gate passed"), "MaxDps gate passed");
        }

        return D(PolicyDecision.Use("no specific rule; fail-open"), "fail-open (no specific rule)");
    }

    private PolicyDecision D(PolicyDecision d, params string[] ev) => ProviderStamp.Stamp(this, d, Source, ev);

    private static string Describe(AbilityPurpose purpose) => purpose switch
    {
        AbilityPurpose.External => "external defensive",
        AbilityPurpose.Escape => "escape utility",
        AbilityPurpose.Movement => "movement utility",
        AbilityPurpose.Dispel => "dispel",
        _ => purpose.ToString(),
    };
}

/// <summary>Offensive cooldowns (major/minor burst windows).</summary>
internal sealed class OffensiveCandidateProvider : ICandidateProvider
{
    public AbilityCategory Category => AbilityCategory.Offensive;
    public string Name => "Offensive";
    public CandidateSourceKind Source => CandidateSourceKind.MaxDpsWire;

    public PolicyDecision Evaluate(ProviderInput p)
    {
        var ability = p.Ability;
        var ctx = p.Context;
        var input = p.Input;
        var slot = (int)p.Slot;

        // v3.0.0: there is no offensive gap-fill source bit on the wire, so the
        // companion detects it by membership in the same generated per-spec
        // offensive list the bridge walked (AbilityCatalog.IsOffensiveGapFill).
        // A MaxDps-named candidate keeps MaxDpsWire; a curated list candidate
        // is CompanionGapFill. Diagnostic/evidence only — the gates below are
        // unchanged for MaxDps-sourced candidates.
        var gapFill = p.Slot == Slot.Offensive
            && p.Catalog.IsOffensiveGapFill(ctx.Class, ctx.Spec, p.SpellId);
        var src = gapFill ? CandidateSourceKind.CompanionGapFill : CandidateSourceKind.MaxDpsWire;

        if (ability.HoldWhenBuffActive && ctx.SlotBuffActive[slot] == TriState.Yes)
            return D(PolicyDecision.Skip("ability's own buff already active"), src, "own buff already active");
        // A companion offensive gap-fill never fires out of combat in Normal
        // mode — Solo mode is the only out-of-combat path (mirrors the
        // self-sustain philosophy). MaxDps-sourced candidates are unaffected.
        if (gapFill && !input.InCombat && !p.Options.SoloEnabled)
            return D(PolicyDecision.Hold("offensive gap-fill out of combat (solo off)"), src, "out of combat (offensive gap-fill)");
        if (input.Memory.OffensiveActive(ability.ConflictGroup, input.NowMs))
            return D(PolicyDecision.Hold($"paired cooldown window active ({ability.ConflictGroup})"), src, $"paired window active ({ability.ConflictGroup})");
        if (ability.EnemyCountMin is > 1 && ability.Status != IntelligenceStatus.MaxDpsBacked)
            return D(PolicyDecision.Uncertain(
                $"enemy count not observable (use condition needs {ability.EnemyCountMin})"),
                src, $"enemy count not observable (needs {ability.EnemyCountMin})");
        if (ability.TargetRange == RangeRequirement.InMelee && ctx.TargetInMelee == TriState.No)
            return D(PolicyDecision.Unavailable("target outside melee range"), src, "target outside melee range");
        if (p.Range == TriState.No) return D(PolicyDecision.Unavailable("target out of range"), src, "target out of range");
        if (p.Range == TriState.Unknown && ability.Unknown != UnknownPolicy.Use)
            return D(PolicyDecision.Uncertain("ability range unknown"), src, "ability range unknown");
        return D(PolicyDecision.Use("offensive candidate; no conflict observed"), src, "no conflict observed");
    }

    private PolicyDecision D(PolicyDecision d, params string[] ev) => ProviderStamp.Stamp(this, d, Source, ev);

    private PolicyDecision D(PolicyDecision d, CandidateSourceKind source, params string[] ev) =>
        ProviderStamp.Stamp(this, d, source, ev);
}

/// <summary>
/// Defensive survival abilities plus the emergency-only escape/movement utility
/// path (which has no validated automatic trigger of its own).
/// </summary>
internal sealed class DefensiveCandidateProvider : ICandidateProvider
{
    public AbilityCategory Category => AbilityCategory.Defensive;
    public string Name => "Defensive";
    public CandidateSourceKind Source => CandidateSourceKind.MaxDpsWire;

    public PolicyDecision Evaluate(ProviderInput p)
    {
        var input = p.Input;
        var ability = p.Ability;
        var ctx = p.Context;
        var opts = p.Options;
        var slot = (int)p.Slot;
        var src = input.Slot == Slot.Defensive && !ctx.DefensiveCatalogSource
            ? CandidateSourceKind.MaxDpsWire
            : CandidateSourceKind.CompanionGapFill;

        if (ability.HoldWhenBuffActive && ctx.SlotBuffActive[slot] == TriState.Yes)
            return D(PolicyDecision.Skip("ability's own buff already active"), src, "own buff already active");

        // Reflect-style abilities need live evidence of an incoming cast;
        // midnight exposes no cast-school data, so the observable is the
        // arg-blind target-cast event state. UNKNOWN degrades per policy.
        if (ability.RequiresEnemyCast)
        {
            var observed = ctx.TargetCasting;
            if (observed == TriState.No)
                return ability.Unknown == UnknownPolicy.Use
                    ? D(PolicyDecision.Use("no incoming cast observed; policy fails open"), src, "no incoming cast (fail-open)")
                    : D(PolicyDecision.Hold("no incoming cast detected"), src, "no incoming cast detected");
            if (observed == TriState.Unknown)
                return ability.Unknown == UnknownPolicy.Use
                    ? D(PolicyDecision.Use("incoming cast unknown; policy fails open"), src, "incoming cast unknown (fail-open)")
                    : D(PolicyDecision.Uncertain("incoming cast state unknown"), src, "incoming cast state unknown");
        }

        var hp = ctx.HpValid ? ctx.HpPct : (int?)null;
        if (hp is { } hpv)
        {
            if (ability.UseBelowHpPct is { } need && hpv > need)
                return D(PolicyDecision.Hold($"HP {hpv}% above {need}% usage threshold"), src, $"HP {hpv}% above {need}%");
            if (ability.HoldAboveHpPct is { } hold && hpv >= hold)
                return D(PolicyDecision.Hold($"HP {hpv}% at/above {hold}% hold threshold"), src, $"HP {hpv}% at/above {hold}%");
        }

        var emergency = hp is { } ehp && ehp <= opts.EmergencyHpPct;

        // ---- MaxDps defensive urgency (additive v2.3 block) ------------------
        var urgency = ability.UrgencySource == DefensiveUrgencySource.Stagger
            && ctx.StaggerUrgency != DefensiveUrgency.Unknown
            ? ctx.StaggerUrgency
            : ctx.DefensiveUrgency;
        var urgencyFact = ability.UrgencySource == DefensiveUrgencySource.Stagger && ctx.StaggerUrgency != DefensiveUrgency.Unknown
            ? $"urgency {urgency} (stagger curve)"
            : $"urgency {urgency} (MaxDps HP curve)";
        if (!emergency)
        {
            if (urgency == DefensiveUrgency.White)
                return D(PolicyDecision.Hold("white defensive urgency (MaxDps renders no glow); holding"), src, "urgency White (MaxDps renders no glow)");
            var required = ability.Purpose is AbilityPurpose.Escape or AbilityPurpose.Movement
                or AbilityPurpose.External
                ? DefensiveUrgency.Red
                : ability.MinimumUrgency;
            var discountable = required > DefensiveUrgency.Yellow
                && !ability.MinimumUrgencyCurated
                && ability.Purpose is not (AbilityPurpose.Escape or AbilityPurpose.Movement or AbilityPurpose.External);
            var discounted = input.Slot == Slot.Defensive && ctx.MaxDpsDefensiveRecommendation && discountable;
            if (discounted) required = (DefensiveUrgency)((int)required - 1);
            if (urgency == DefensiveUrgency.Unknown)
            {
                if (required > DefensiveUrgency.Yellow)
                    return D(PolicyDecision.Uncertain("defensive urgency unknown; no automatic major defensive"),
                        src, urgencyFact, "no automatic major defensive on unknown urgency");
            }
            else if (urgency < required)
            {
                return D(PolicyDecision.Hold($"{MitigationName(ability)} held at {urgency} urgency (needs {required})"),
                    src, urgencyFact, $"minimum urgency {required}{(ability.MinimumUrgencyCurated ? " (curated)" : " (tier default)")}");
            }
        }

        var activeTier = input.Memory.ActiveDefensiveTier(input.NowMs);
        if (!emergency && ability.Tier != DefensiveTier.None && activeTier >= ability.Tier)
            return D(PolicyDecision.Hold($"stronger/equal defensive already active ({activeTier})"), src, $"active defensive tier {activeTier}");
        if (!emergency && ability.Tier == DefensiveTier.Major && activeTier == DefensiveTier.Minor
            && hp is { } shp && shp > opts.DefensiveEscalateHpPct)
            return D(PolicyDecision.Hold($"minor mitigation running and HP {shp}% stable; saving major cooldown"),
                src, $"minor mitigation active, HP {shp}% stable");

        if (ability.RequiresTarget && !input.HasTarget)
            return D(PolicyDecision.Hold("no target"), src, "no target");
        if (p.Range == TriState.No && ability.TargetsEnemy)
            return D(PolicyDecision.Unavailable("target out of range"), src, "target out of range");

        return emergency
            ? D(PolicyDecision.Use($"HP {hp}% at/below emergency {opts.EmergencyHpPct}%; emergency mitigation", emergency: true),
                src, $"HP {hp}% at/below emergency {opts.EmergencyHpPct}%")
            : D(PolicyDecision.Use($"{DefensiveSource(input, ctx)} + {urgency} urgency; {MitigationName(ability)} eligible"),
                src, urgencyFact, $"MaxDps recommendation: {(ctx.MaxDpsDefensiveRecommendation ? "yes" : "no")}");
    }

    private PolicyDecision D(PolicyDecision d, CandidateSourceKind source, params string[] ev) =>
        ProviderStamp.Stamp(this, d, source, ev);

    /// <summary>Which source offered the defensive candidate (explainability).</summary>
    private static string DefensiveSource(PolicyInput input, CombatContext ctx) =>
        input.Slot == Slot.Defensive
            ? ctx.MaxDpsDefensiveRecommendation
                ? "MaxDps defensive recommendation"
                : "catalog defensive gap-fill"
            : "companion defensive";

    /// <summary>Short-CD vs major mitigation, for the verdict reason.</summary>
    private static string MitigationName(AbilityDefinition ability) =>
        ability.Purpose is AbilityPurpose.Escape or AbilityPurpose.Movement or AbilityPurpose.External
            ? "utility"
            : ability.Tier is DefensiveTier.Major or DefensiveTier.Immunity
                ? "major mitigation"
                : "short-CD mitigation";
}

/// <summary>Interrupts / kicks. Off the GCD and time-critical.</summary>
internal sealed class InterruptCandidateProvider : ICandidateProvider
{
    public AbilityCategory Category => AbilityCategory.Interrupt;
    public string Name => "Interrupt";
    public CandidateSourceKind Source => CandidateSourceKind.MaxDpsWire;

    public PolicyDecision Evaluate(ProviderInput p)
    {
        var input = p.Input;
        if (PolicyEvaluator.InterruptVetoes(input) is { } veto)
            return D(veto, VetoEvidence(p));
        if (p.Range == TriState.No) return D(PolicyDecision.Unavailable("target out of ability range"), "target out of ability range");
        if (p.Range == TriState.Unknown && p.Ability.Unknown != UnknownPolicy.Use)
            return D(PolicyDecision.Uncertain("ability range unknown"), "ability range unknown");
        return D(PolicyDecision.Use($"interrupt ({p.Ability.InterruptKind}): live interruptible cast, in range or range unknown"),
            "observed interruptible cast", $"kind {p.Ability.InterruptKind}");
    }

    private static string[] VetoEvidence(ProviderInput p) => p.Context.TargetCastInterruptible == TriState.No
        ? ["observed target cast is not interruptible"]
        : ["no live target cast observed (stale MaxDps flag)"];

    private PolicyDecision D(PolicyDecision d, params string[] ev) => ProviderStamp.Stamp(this, d, Source, ev);
}

/// <summary>
/// Mobility: only target-reaching gap closers are ever automatic. Escape /
/// repositioning utilities do not reach this provider — they route through the
/// emergency-only defensive path.
/// </summary>
internal sealed class MobilityCandidateProvider : ICandidateProvider
{
    public AbilityCategory Category => AbilityCategory.Mobility;
    public string Name => "Mobility";
    public CandidateSourceKind Source => CandidateSourceKind.BridgeExtra;

    public PolicyDecision Evaluate(ProviderInput p)
    {
        var input = p.Input;
        var ctx = p.Context;
        if (!input.InCombat) return D(PolicyDecision.Hold("not in combat"), "not in combat");
        if (!input.HasTarget) return D(PolicyDecision.Hold("no target"), "no target");
        if (ctx.TargetInMelee == TriState.Yes) return D(PolicyDecision.Hold("target already in melee range"), "target already in melee range");
        if (ctx.TargetInMelee == TriState.Unknown) return D(PolicyDecision.Uncertain("target melee state unknown"), "target melee state unknown");
        if (p.Range == TriState.No) return D(PolicyDecision.Unavailable("target out of ability range"), "target out of ability range");
        if (p.Range == TriState.Unknown) return D(PolicyDecision.Uncertain("ability range unknown"), "ability range unknown");
        return D(PolicyDecision.Use("target outside melee and ability in range; closing gap"), "target outside melee, ability in range");
    }

    private PolicyDecision D(PolicyDecision d, params string[] ev) => ProviderStamp.Stamp(this, d, Source, ev);
}

/// <summary>
/// Solo self-sustain (the companion-only self-heal slot): HP-gated with an
/// overheal guard and an active-immunity guard.
/// </summary>
internal sealed class SelfSustainCandidateProvider : ICandidateProvider
{
    public AbilityCategory Category => AbilityCategory.SelfHeal;
    public string Name => "SelfSustain";
    public CandidateSourceKind Source => CandidateSourceKind.BridgeExtra;

    /// <summary>
    /// R2 (sustain-cd): the distinct hold reason shown while the player is
    /// inside the sustain window but no self-heal candidate is ready (the
    /// bridge encodes a heal only when it is off cooldown, so an absent slot
    /// while the spec owns a curated self-heal means "on cooldown / not yet
    /// usable"). Owned by the provider so the wording lives with the rule.
    /// </summary>
    public const string CooldownWaitReason = "waiting for self-heal cooldown";

    /// <summary>
    /// R2: true when the tick should be explained as "waiting for self-heal
    /// cooldown": policy on, Solo on, a known HP reading inside the sustain
    /// window, the spec owns at least one curated self-heal, and the wire has
    /// not offered a ready self-heal this tick. Informational only — it never
    /// changes a verdict or an order, and it fires no send.
    /// </summary>
    public static bool CoolingDown(
        CombatContext context, PolicyOptions options, AbilityCatalog? catalog, bool hasReadyCandidate)
    {
        if (hasReadyCandidate) return false;
        if (!options.SoloEnabled) return false;
        if (catalog is null) return false;
        if (!context.HpValid) return false;
        if (context.HpPct > options.SelfSustainHpPct) return false;
        return catalog.Extras(context.Class, context.Spec, AbilityCategory.SelfHeal).Length > 0;
    }

    public PolicyDecision Evaluate(ProviderInput p)
    {
        var input = p.Input;
        var ability = p.Ability;
        var opts = p.Options;
        if (!p.Context.HpValid)
            return D(PolicyDecision.Hold("player HP unknown; conserving self-heal"), "player HP unknown");
        if (ability.RequiresTarget && !input.HasTarget)
            return D(PolicyDecision.Hold("self-heal requires a target"), "self-heal requires a target");
        if (ability.TargetsEnemy && p.Range == TriState.No)
            return D(PolicyDecision.Unavailable("target out of ability range"), "target out of ability range");

        var hp = p.Context.HpPct;
        // The reason carries where the HP came from: a curve reading is
        // approximate, a plain reading is exact. Existing plain reason strings
        // stay byte-identical ("HP 50%"); the curve reads "HP ~40% (curve)".
        var hpText = p.Context.HpSource == HpSource.Curve ? $"~{hp}% (curve)" : $"{hp}%";
        // The overheal guard must not assume the bottom of a curve band: use
        // the band's upper bound so a quantised reading cannot justify a heal
        // that would mostly overheal.
        var hpUpper = p.Context.HpSource == HpSource.Curve ? p.Context.HpPctUpper : hp;

        // A running immunity already prevents the damage; healing into it is
        // waste (the emergency branch below still overrides for a lethal-low
        // health pool that must be topped up before the immunity ends).
        if (hp > opts.EmergencyHpPct
            && input.Memory.ActiveDefensiveTier(input.NowMs) == DefensiveTier.Immunity)
            return D(PolicyDecision.Hold("immunity already active; self-heal conserved"), "immunity already active");

        if (hp <= opts.EmergencyHpPct)
            return D(PolicyDecision.Use(
                $"HP {hpText} at/below emergency {opts.EmergencyHpPct}%; emergency self-sustain",
                emergency: true),
                $"HP {hpText} at/below emergency {opts.EmergencyHpPct}%");

        if (!opts.SoloEnabled)
            return D(PolicyDecision.Hold("solo mode off; self-heal held above emergency HP"), "solo mode off");

        if (ability.UseBelowHpPct is { } need && hp > need)
            return D(PolicyDecision.Hold($"solo: HP {hpText} above ability threshold {need}%"), $"HP {hpText} above ability threshold {need}%");
        if (hp > opts.SelfSustainHpPct)
            return D(PolicyDecision.Hold($"solo: HP {hpText} above sustain threshold {opts.SelfSustainHpPct}%; conserving"),
                $"self-sustain window {opts.SelfSustainHpPct}%");
        if (ability.HealPctMaxHp > 0)
        {
            var missing = 100 - hpUpper;
            var materiallyUseful = (int)Math.Ceiling(ability.HealPctMaxHp * 0.6);
            if (missing < materiallyUseful)
                return D(PolicyDecision.Hold($"solo: HP {hpText}; heal {ability.HealPctMaxHp}% would mostly overheal"),
                    $"overheal guard: {missing}% missing vs {materiallyUseful}% needed");
        }
        return D(PolicyDecision.Use($"solo: HP {hpText} below sustain {opts.SelfSustainHpPct}%; self-sustain"),
            $"solo self-sustain window {opts.SelfSustainHpPct}%");
    }

    private PolicyDecision D(PolicyDecision d, params string[] ev) => ProviderStamp.Stamp(this, d, Source, ev);
}

/// <summary>
/// Manual utility (dispel / crowd control / purge / threat). Structurally
/// incapable of returning USE: it has no observable trigger in Midnight
/// (debuff and target state are secret or unavailable), so it always HOLDs,
/// even when explicitly enabled. Tests pin this: no code path here calls
/// <see cref="PolicyDecision.Use"/>.
/// </summary>
internal sealed class UtilityCandidateProvider : ICandidateProvider
{
    public AbilityCategory Category => AbilityCategory.Utility;
    public string Name => "Utility";
    public CandidateSourceKind Source => CandidateSourceKind.None;

    public PolicyDecision Evaluate(ProviderInput p) =>
        ProviderStamp.Stamp(this,
            PolicyDecision.Hold("manual utility has no observable trigger; never automatic"),
            Source,
            "manual utility: no observable trigger");
}
