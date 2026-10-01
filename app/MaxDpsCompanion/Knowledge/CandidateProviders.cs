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

    /// <summary>
    /// [TimeToKill] Fallback threaded from settings (spec §4). Defaults to the
    /// documented fail-open so manually-built inputs (tests / legacy replays)
    /// keep their verdicts; callers thread the real setting once
    /// <see cref="PolicyOptions"/> exposes it.
    /// </summary>
    public TtkFallback Fallback { get; init; } = TtkPolicy.DefaultFallback;
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

    /// <summary>
    /// v3.4.0 CC appendix. Not returned by <see cref="For"/> (purpose routing is
    /// unchanged): the evaluator reaches it only through the one-line
    /// <see cref="CrowdControlVetoes.Evaluate"/> call-site, and only for curated
    /// auto-eligible CC rows while the companion CC toggle is ON.
    /// </summary>
    public static readonly ICandidateProvider CrowdControl = new CrowdControlCandidateProvider();

    /// <summary>The provider for an uncatalogued / generic candidate.</summary>
    public static readonly ICandidateProvider Generic = new GenericCandidateProvider();

    public static ICandidateProvider For(Slot slot, AbilityDefinition ability)
    {
        // Racial-scope rows (AbilityDefinition.Scope == "Racial") need NO
        // special routing: they carry a normal Purpose, so they resolve through
        // the same providers as class abilities (Offensive -> Offensive,
        // DefensiveMinor/Absorb -> Defensive, SelfHeal -> SelfSustain,
        // CrowdControl -> Utility). The bridge's known-spell filter means a
        // non-matching race never offers the candidate in the first place.
        if (ability.Category == AbilityCategory.SelfHeal || slot == Slot.SelfHeal) return SelfSustain;
        if (ability.IsSurvival) return Defensive;

        // v3.5 movement routing (RC5). Every movement tool belongs to the
        // Mobility provider, which only ever fires a target-reaching tool when
        // the target is confirmed outside melee and the ability is in range.
        // A plain Escape therefore routes to Mobility and holds (it does not
        // close a gap). An Escape carrying the taxonomy overlay's
        // emergencyEscape flag is an emergency survival button, so it routes to
        // Defensive, whose escape gate is Red-only (emergency HP or Red
        // urgency — never White/Yellow).
        if (ability.Purpose == AbilityPurpose.GapCloser) return Mobility;
        if (ability.Purpose == AbilityPurpose.Movement) return Mobility;
        if (ability.Purpose == AbilityPurpose.Escape)
        {
            // A flagged emergencyEscape, or a curated manual-by-design escape
            // (a true panic button such as Vanish — never a target-reaching
            // tool), is an emergency survival button owned by Defensive's
            // Red-only escape gate. Any other escape is a movement tool.
            return ability.EmergencyEscape || ability.NeverAutomatic ? Defensive : Mobility;
        }

        // v3.5 CC slot reuse (RC2). The bridge reuses the interrupt slot (wire
        // 6) as the crowd-control source when MaxDps names no usable
        // interrupt, so a curated CC row offered there resolves to the CC
        // provider. The provider re-checks the opt-in gate and every safety
        // rule, so an ungated call can never fire; non-slot-6 CC rows keep the
        // existing Vetoes call-site and the Utility fallback.
        if (slot == Slot.Interrupt && ability.Purpose == AbilityPurpose.CrowdControl) return CrowdControl;

        if (ability.Purpose is AbilityPurpose.Dispel or AbilityPurpose.CrowdControl
            or AbilityPurpose.Purge or AbilityPurpose.Threat) return Utility;
        if (ability.Purpose == AbilityPurpose.Interrupt) return Interrupt;
        if (ability.Purpose is AbilityPurpose.MajorOffensive or AbilityPurpose.MinorOffensive) return Offensive;
        if (ability.Purpose is AbilityPurpose.Consumable or AbilityPurpose.Trinket) return MaxDpsRotation;
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
            // Stream 3 preset (restrict-only, default Full = no-op): Burst holds
            // consumables and on-use trinkets until the target's TTK is
            // measurable, so a burst window is not spent on dying trash.
            // v3.6.0: the provisional rate and the fast-pack grace hold are NOT
            // consumed here. v3.7: when a binding history window predicts a
            // fight longer than 5 s, the burst is released (the consumable is
            // worth spending) even though the live estimate is still invalid; a
            // history-only short target and "no binding window" keep the hold.
            // A binding window with no usable value this tick (HistSec==0) must
            // NOT claim "ttk-hist ~0.0s": it falls back to the legacy
            // burst-preset reason, exactly like the offensive guard's HistSec>0
            // gate, so the log never states a false target duration.
            if (p.Options.Preset == RotationPreset.Burst && !ctx.TtkValid
                && (!ctx.TtkHistBinding || ctx.TtkHistSec < 5.0))
            {
                return ctx.TtkHistBinding && ctx.TtkHistSec > 0
                    ? D(PolicyDecision.Hold(
                            $"ttk-hist: target ~{ctx.TtkHistSec:0.#}s to die; holding {ability.Name}"),
                        "ttk-hist (history target < 5s)")
                    : D(PolicyDecision.Hold($"burst preset: holding {ability.Name} until a boss TTK is measurable"),
                        "burst preset (no valid boss TTK)");
            }
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
        // Stream 3 makes the derived source explicit: the engine projects the
        // bit into CombatContext (OffensiveDerivedGapFill) at frame decode, and
        // telemetry records it; the id-membership fallback keeps manually-built
        // contexts (tests / older replays) identical.
        var gapFill = p.Slot == Slot.Offensive && ctx.IsOffensiveGapFill(p.SpellId, p.Catalog);
        var src = gapFill ? CandidateSourceKind.CompanionGapFill : CandidateSourceKind.MaxDpsWire;

        if (ability.HoldWhenBuffActive && ctx.SlotBuffActive[slot] == TriState.Yes)
            return D(PolicyDecision.Skip("ability's own buff already active"), src, "own buff already active");

        // Stream 3 preset (restrict-only, default Full = no-op): Burst conserves
        // major offensive cooldowns until the target's TTK is measurable. The
        // preset only ever HOLDs; an invalid TTK (including TimeToKill off) is
        // exactly the "not a boss" case it protects against. v3.6.0 wiring note:
        // this preset hold stays UNCHANGED — it still requires a *Valid*
        // estimate only and does not read the provisional rate or the fast-pack
        // grace hold (those belong to the T1 waste guard below).
        if (p.Options.Preset == RotationPreset.Burst
            && ability.Purpose == AbilityPurpose.MajorOffensive
            && !ctx.TtkValid)
            return D(PolicyDecision.Hold($"burst preset: holding {ability.Name} until a boss TTK is measurable"),
                src, "burst preset (no valid boss TTK)");

        // Stream 3 preset (restrict-only, default SingleTarget = no-op): AoE
        // conserves catalogued single-target-only cooldowns so the curated
        // AoE-ranked candidates in the list get priority.
        if (p.Options.TargetPreset == TargetPreset.Aoe
            && ability.OffensiveUsage == OffensiveUsage.SingleTargetOnly)
            return D(PolicyDecision.Hold($"AoE preset: conserving single-target {ability.Name}"),
                src, "AoE preset (single-target conserved)");

        // T1 (v3.6.0) waste guard: a Valid OR provisional TTK shorter than the
        // ability's minimum means the cooldown cannot pay for itself on this
        // target. <see cref="TtkPolicy.WasteGuardHolds"/> applies the
        // Valid||Provisional gate (provisional only for MajorBurst /
        // Transformation / Summon / WindowDriven — never a minor) and the
        // execute carve-out (ExecuteRange bypasses with TtkSec>=3 or unknown)
        // plus the kill-secure bypass, both failing the rule open. Holds BOTH
        // MaxDps-sourced and companion gap-fill offensives; MaxDps simply
        // re-suggests next tick, so there is no lockout. An unknown TTK fails
        // open (never holds).
        // activeDurSec is the provider's T2 value: the full second-use window
        // (2·cooldown + duration). It feeds the v3.7 adaptive need so a long
        // cooldown is held until the fight is expected to cover its own
        // duration. Computed here because the ability carries both components.
        var activeDurSec = (TtkPolicy.TwoUsesFactor * (double)ability.CooldownMs + ability.DurationMs) / 1000.0;
        if (TtkPolicy.WasteGuardHolds(ability, ctx, activeDurSec, ctx.TtkHistDurFactor))
        {
            var need = TtkPolicy.MinTtkSec(ability);
            if (TtkPolicy.HistoryWasteGuardHolds(ability, ctx, activeDurSec, ctx.TtkHistDurFactor))
            {
                var needAdapt = TtkPolicy.NeedAdaptive(need, activeDurSec, ctx.TtkHistDurFactor);
                return D(PolicyDecision.Hold(
                        $"ttk-hist: target ~{ctx.TtkHistSec:0.#}s to die; saving {ability.Name} (needs {needAdapt:0.#}s)"),
                    src, $"ttk-hist {ctx.TtkHistSec:0.#}s below adaptive {needAdapt:0.#}s");
            }
            return D(PolicyDecision.Hold(
                    $"target ~{ctx.TtkSec:0.#}s to die; saving {ability.Name} (needs {need:0.#}s)"),
                src, $"TTK {ctx.TtkSec:0.#}s below minimum {need:0.#}s");
        }

        // Grace hold (v3.6.0, spec §2): the only fail-open exception to the
        // unknown-TTK rule. No estimate exists (valid or provisional), but the
        // fast-pack latch says the last two targets died inside 20 s and this
        // one is younger than 4 s — hold a major rather than spend it on what is
        // probably the next trash mob. Never fires without the latch under the
        // default FailOpen fallback; [TimeToKill] Fallback=ConserveMajors
        // applies it to any unknown-TTK major with no latch needed. Minors are
        // never held here (GraceHoldHolds restricts the usages itself).
        if (TtkPolicy.GraceHoldHolds(ability, ctx, p.Fallback))
            return D(PolicyDecision.Hold(TtkPolicy.GraceHoldReason),
                src, "fast pack (unknown TTK, latch set, age < 4s)");

        // A companion offensive gap-fill never fires out of combat in Normal
        // mode — Solo mode is the only out-of-combat path (mirrors the
        // self-sustain philosophy). MaxDps-sourced candidates are unaffected.
        if (gapFill && !input.InCombat && !p.Options.SoloEnabled)
            return D(PolicyDecision.Hold("offensive gap-fill out of combat (solo off)"), src, "out of combat (offensive gap-fill)");

        // T2/T3 (v3.2.0): a paired-window hold is bypassed when the fight is
        // long enough for a second full use, or the target is in execute range
        // with a confirmed execute-favored burst. Both then fall through to the
        // normal gates and may fire.
        var pairActive = input.Memory.OffensiveActive(ability.ConflictGroup, input.NowMs);
        if (pairActive
            && !TtkPolicy.TwoUsesAvailable(ability, ctx)
            && !TtkPolicy.ExecuteRange(ability, ctx))
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

        // T4 (v3.6.0) dying-target hold, now a per-tier / scope lookup. Solo:
        // Minor 6 s, Major 10 s, Immunity 15 s. Group: only Minor is gated, and
        // only on a Valid rate below 4 s with the fast-pack latch and urgency
        // below Orange — enemy count is unobservable so Major/Immunity are never
        // conserved. Emergency HP always overrides (checked above/after and
        // inside the helper). Ladder-band carve-out: inside the Solo Major /
        // Immunity escalation band the defensive is needed now and is not held.
        if (ability.IsDefensive && TtkPolicy.DyingTargetHolds(
                ability, ctx, opts.SoloEnabled, emergency,
                opts.SoloMajorHpPct, opts.SoloImmunityHpPct))
        {
            var scope = opts.SoloEnabled ? "solo" : "group";
            return D(PolicyDecision.Hold(
                    $"target dies in ~{ctx.TtkSec:0.#}s; saving {MitigationName(ability)}"),
                src, $"TTK {ctx.TtkSec:0.#}s ({scope} dying target)");
        }

        // ---- Solo HP-banded escalation (v3.3.0, additive) --------------------
        // In Solo with escalation on and a valid HP reading, the ladder bands
        // substitute for MaxDps urgency: a gap-fill defensive inside its band
        // no longer needs the wire urgency stage. MaxDps-flagged candidates
        // (src == MaxDpsWire) keep the classic urgency path; group behaviour
        // is byte-identical to before. RequiresEnemyCast / buff / HP-threshold
        // / T4 gates above still apply; overlap / escalate / range below still
        // apply. Immunities additionally require no active immunity (checked
        // below with the overlap rule).
        var (minorBand, majorBand, immBand) = PolicyOptions.ValidateSoloBands(
            opts.SoloMinorHpPct, opts.SoloMajorHpPct, opts.SoloImmunityHpPct);
        var soloEsc = opts.SoloEnabled && opts.SoloEscalation
            && hp is { } && src == CandidateSourceKind.CompanionGapFill;
        var activeTierAtBand = input.Memory.ActiveDefensiveTier(input.NowMs);
        if (soloEsc)
        {
            var soloHp = hp!.Value;
            var isImmunity = ability.Tier == DefensiveTier.Immunity || ability.Purpose == AbilityPurpose.Immunity;
            var bandNeed = isImmunity ? immBand : ability.Tier == DefensiveTier.Major ? majorBand : minorBand;
            // Enter/exit hysteresis (v3.3.0 Stream 1 §1.2): once the ladder is
            // engaged (any mitigation is running) a tier stays eligible up to
            // enter+5, so an HP reading oscillating on the band edge cannot flap
            // the decision. With no prior engagement the latch is the plain
            // enter threshold — stateless-safe (fresh session, tests, unknown).
            var wasEngaged = activeTierAtBand != DefensiveTier.None;
            if (!SoloBandLatch.Latched(bandNeed, soloHp, wasEngaged))
            {
                var bandName = isImmunity ? "immunity" : ability.Tier == DefensiveTier.Major ? "major" : "minor";
                return D(PolicyDecision.Hold($"solo: HP {soloHp}% above {bandName} band {bandNeed}%; conserving"),
                    src, $"solo {bandName} band {bandNeed}%");
            }
            // Inside the band: urgency substitution — treat the wire urgency as
            // satisfied. An active immunity still blocks a second immunity via
            // the overlap rule below; everything else falls through to Use.
            if (isImmunity && activeTierAtBand == DefensiveTier.Immunity)
                return D(PolicyDecision.Hold("immunity already active; immunity conserved"), src, "immunity already active");
        }

        // ---- MaxDps defensive urgency (additive v2.3 block) ------------------
        var urgency = ability.UrgencySource == DefensiveUrgencySource.Stagger
            && ctx.StaggerUrgency != DefensiveUrgency.Unknown
            ? ctx.StaggerUrgency
            : ctx.DefensiveUrgency;
        var urgencyFact = ability.UrgencySource == DefensiveUrgencySource.Stagger && ctx.StaggerUrgency != DefensiveUrgency.Unknown
            ? $"urgency {urgency} (stagger curve)"
            : $"urgency {urgency} (MaxDps HP curve)";
        if (!emergency && !soloEsc)
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

        var activeTier = activeTierAtBand;
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
            : soloEsc
                ? D(PolicyDecision.Use($"solo ladder HP {hp}% in band; {MitigationName(ability)} eligible",
                        emergency: hp is { } shp2 && shp2 <= opts.EmergencyHpPct),
                    src, $"solo ladder band", $"MaxDps recommendation: {(ctx.MaxDpsDefensiveRecommendation ? "yes" : "no")}")
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

/// <summary>
/// Crowd-control appendix provider (v3.4.0). It is reached ONLY via
/// <see cref="CrowdControlVetoes.Evaluate"/> and only while the companion CC
/// gate is ON. It can return <see cref="PolicyVerdict.Use"/> for a curated
/// auto-eligible CC row when the target state is observable; every other path
/// holds (the toggle-OFF path never reaches here and keeps the Utility
/// manual-by-design behaviour byte-identical).
///
/// Safety rules (architect guardrails):
///  * Never an opener: AoE CC requires InCombat + HasTarget; single-target CC
///    requires the current target (HasTarget).
///  * Casting-only Stun/Silence (v3.5, Q1): a Stun/Silence row fires only
///    while <see cref="CombatContext.TargetCasting"/> is Yes — it is an
///    interrupt substitute, never a blind stun. Other kinds are unaffected.
///  * Target confirmed attackable/in-range via the existing SlotRange /
///    TargetInMelee observations, and the shared cast hold.
///  * No DR-state inference: the companion only blocks re-chaining the same
///    <see cref="CcDrCategory"/> it recently applied itself, and fails open
///    when that memory is unknown.
/// </summary>
internal sealed class CrowdControlCandidateProvider : ICandidateProvider
{
    public AbilityCategory Category => AbilityCategory.Utility;
    public string Name => "CrowdControl";
    public CandidateSourceKind Source => CandidateSourceKind.CompanionGapFill;

    public PolicyDecision Evaluate(ProviderInput p)
    {
        var ability = p.Ability;
        var ctx = p.Context;
        var input = p.Input;

        // v3.5: the provider is now reachable from CandidateProviders.For (the
        // reused interrupt slot), so it enforces the opt-in itself. The
        // CrowdControlVetoes call-site already checks this for its own path;
        // re-checking is idempotent and keeps the two entry points safe.
        if (!CrowdControlGate.Enabled)
            return D(PolicyDecision.Hold("crowd control opt-in off; held"), "CC opt-in off");
        if (ability.Status is IntelligenceStatus.Incomplete or IntelligenceStatus.Unknown
            or IntelligenceStatus.UnsafeToAutomate)
            return D(PolicyDecision.Hold("crowd control intelligence incomplete; held"), "CC intelligence incomplete");
        if (input.Options.Abilities is { } ccPolicy)
        {
            var ccMode = ccPolicy.ModeOf(ability.SpellId);
            if (ccMode is UserAbilityMode.Never or UserAbilityMode.Manual)
                return D(PolicyDecision.Hold("user policy: crowd control held"), "user policy");
            if (ccPolicy.OverrideOf(ability.SpellId) == false)
                return D(PolicyDecision.Hold("user policy disabled"), "user policy");
        }

        var entry = p.Catalog.CrowdControlFor(ctx.Class, ctx.Spec, ability.SpellId);
        if (entry is null || !entry.AutoEligible)
            return D(PolicyDecision.Hold("not a curated automatic crowd-control row"), "not curated CC");

        // Hard execution-safety gate: same rule as every other candidate.
        if (ExecutionSafety.CastHoldReason(p.Slot, p.SpellId, ctx.Cast, p.Catalog) is { } exec)
            return D(PolicyDecision.Hold(exec), exec);

        // Target observability. CC is never an opener on an untargeted pull.
        if (!input.HasTarget)
            return D(PolicyDecision.Hold("no current target; crowd control held"), "no target");
        if (entry.IsAoe && !input.InCombat)
            return D(PolicyDecision.Hold("AoE crowd control is never used as an opener (pull risk)"),
                "AoE CC out of combat");

        if (p.Range == TriState.No)
            return D(PolicyDecision.Unavailable("target out of ability range"), "target out of range");
        // CC appendix rows (e.g. Hammer of Justice) come from Utility defaults
        // whose Unknown policy is Hold; but the range-unknown context the test
        // matrix feeds them is the reviewed CC sentinel (confirmed-target
        // contract): once the CC gate admitted the row, range-UNKNOWN fails
        // open to the provider's own target checks instead of Uncertain.
        if (p.Range == TriState.Unknown && ability.Unknown != UnknownPolicy.Use
            && ability.Purpose != AbilityPurpose.CrowdControl)
            return D(PolicyDecision.Uncertain("ability range unknown"), "ability range unknown");
        if (ability.TargetRange == RangeRequirement.InMelee && ctx.TargetInMelee == TriState.No)
            return D(PolicyDecision.Unavailable("target outside melee range"), "target outside melee range");

        // v3.5 casting-only gate (Q1 approved): a Stun/Silence row is only ever
        // an interrupt substitute, so it may fire only while the target is
        // observably casting. ANY other state — not casting, or cast state
        // unknown — holds: a blind stun is never generated. Non-Stun/Silence
        // kinds (fear / disorient / incapacitate / root / sleep / banish /
        // subjugate) are unaffected. The gate sits after the target/range
        // checks so no-target and out-of-range still report their own verdicts.
        if (entry.Kind is CcKind.Stun or CcKind.Silence && ctx.TargetCasting != TriState.Yes)
            return D(PolicyDecision.Hold($"casting-only crowd control {entry.Name}; no target cast observed"),
                "casting-only: no target cast observed");

        // Conservative same-category anti-chain memory (self-only, fail open).
        if (entry.Dr != CcDrCategory.Unknown
            && CrowdControlVetoes.Memory is { } memory
            && memory.IsDiminished(entry.Dr, input.NowMs))
            return D(PolicyDecision.Hold(
                    $"same DR category ({entry.Dr}) used recently; not auto-chaining"),
                $"DR memory: {entry.Dr} within window");

        CrowdControlVetoes.NoteUsed(entry.Dr, input.NowMs);
        return D(PolicyDecision.Use(
                $"{Describe(entry)} ({entry.Kind}/{entry.Dr}); target confirmed"),
            $"{entry.Kind} ({entry.Dr})", entry.IsAoe ? "AoE CC in combat" : "single-target CC");
    }

    private static string Describe(CrowdControlEntry entry) =>
        entry.IsAoe ? $"AoE crowd control {entry.Name}" : $"crowd control {entry.Name}";

    private PolicyDecision D(PolicyDecision d, params string[] ev) =>
        ProviderStamp.Stamp(this, d, Source, ev);
}
