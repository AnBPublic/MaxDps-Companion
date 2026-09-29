namespace MaxDpsCompanion;

/// <summary>
/// What the policy decided for one candidate this tick. The five states mirror
/// the registry's decision model (registry §11/§12) — they are never collapsed
/// into a boolean:
///  * Use         — conditions are sufficiently proven.
///  * Hold        — valid ability, wrong moment (condition may clear).
///  * Skip        — deliberately not used in this situation (redundant /
///                  user policy / not interruptible).
///  * Unavailable — cannot be used now for a structural reason (range, melee
///                  position); retry when the state changes.
///  * Unknown     — the system lacks safe information needed to decide.
/// The scheduler schedules only Use; all other states carry their reason into
/// telemetry and are re-evaluated next tick.
/// </summary>
internal enum PolicyVerdict
{
    /// <summary>Insert the candidate into the schedule.</summary>
    Use = 0,
    /// <summary>Exclude this tick but re-evaluate next tick (condition may clear).</summary>
    Hold,
    /// <summary>Exclude and mark the condition as a deliberate skip (out of range / redundant).</summary>
    Skip,
    /// <summary>Structurally unavailable now (confirmed out of range / wrong position).</summary>
    Unavailable,
    /// <summary>Required context is unknown; cannot decide safely.</summary>
    Unknown,
}

/// <summary>One deterministic policy decision with a human-readable reason (telemetry/UI).</summary>
internal readonly record struct PolicyDecision(PolicyVerdict Verdict, string Reason, bool Emergency)
{
    /// <summary>Name of the provider that produced this decision (v2.7 traceability).</summary>
    public string Provider { get; init; } = "";

    /// <summary>Where the candidate came from (v2.7). Diagnostic only; never affects the verdict.</summary>
    public CandidateSourceKind Source { get; init; } = CandidateSourceKind.None;

    /// <summary>Human-readable "why" facts, renderable by the UI (no secret values).</summary>
    public IReadOnlyList<string> Evidence { get; init; } = [];

    public static PolicyDecision Use(string reason, bool emergency = false) => new(PolicyVerdict.Use, reason, emergency);
    public static PolicyDecision Hold(string reason) => new(PolicyVerdict.Hold, reason, false);
    public static PolicyDecision Skip(string reason) => new(PolicyVerdict.Skip, reason, false);
    public static PolicyDecision Unavailable(string reason) => new(PolicyVerdict.Unavailable, reason, false);
    public static PolicyDecision Uncertain(string reason) => new(PolicyVerdict.Unknown, reason, false);
}

/// <summary>User-tunable policy thresholds (mirrors [Intelligence]/[Solo] settings).</summary>
internal sealed class PolicyOptions
{
    public bool SoloEnabled { get; init; }
    public int EmergencyHpPct { get; init; } = 35;
    public int SelfSustainHpPct { get; init; } = 65;
    public int DefensiveEscalateHpPct { get; init; } = 60;

    /// <summary>
    /// Per-ability user automation policy (explicit ON/OFF overrides). Null =
    /// every ability follows its curated default (tests / legacy recordings).
    /// </summary>
    public AbilityPolicy? Abilities { get; init; }

    public static PolicyOptions Standard { get; } = new();

    public static PolicyOptions FromSettings(AppSettings settings) => new()
    {
        SoloEnabled = settings.SoloEnabled,
        EmergencyHpPct = settings.SoloEmergencyHpPct,
        SelfSustainHpPct = settings.SoloSelfSustainHpPct,
        DefensiveEscalateHpPct = settings.SoloDefensiveEscalateHpPct,
        Abilities = settings.Abilities,
    };
}

/// <summary>
/// The companion's own recent-use memory, feeding defensive sequencing and
/// offensive pairing. A pure in-memory state: it records only what this
/// process already sent and the knowledge base's duration for that ability —
/// it never reads a game value. Owned by the scheduler (one instance per
/// session, reset on Start).
/// </summary>
internal sealed class PolicyMemory
{
    /// <summary>Assumed coverage when the knowledge base has no duration.</summary>
    internal const int DefaultDefensiveMs = 4000;
    internal const int DefaultOffensiveMs = 8000;

    private readonly Dictionary<string, (long UntilMs, DefensiveTier Tier)> _defensives = new();
    private readonly Dictionary<string, long> _offensives = new();

    /// <summary>Records that an ability was actually sent now (engine calls this on every successful send).</summary>
    public void NoteUse(AbilityDefinition ability, long nowMs)
    {
        switch (ability.Purpose)
        {
            case AbilityPurpose.DefensiveMajor:
            case AbilityPurpose.DefensiveMinor:
            case AbilityPurpose.Immunity:
            case AbilityPurpose.Absorb:
            case AbilityPurpose.Reflect:
            {
                var group = ability.ConflictGroup ?? "Defensive";
                var duration = ability.DurationMs > 0 ? ability.DurationMs : DefaultDefensiveMs;
                var until = nowMs + duration;
                if (!_defensives.TryGetValue(group, out var current)
                    || until >= current.UntilMs
                    || ability.Tier > current.Tier)
                {
                    _defensives[group] = (until, ability.Tier);
                }
                break;
            }
            case AbilityPurpose.MajorOffensive:
            case AbilityPurpose.MinorOffensive:
            {
                var group = ability.ConflictGroup ?? "Offensive";
                var duration = ability.DurationMs > 0 ? ability.DurationMs : DefaultOffensiveMs;
                var until = nowMs + duration;
                if (!_offensives.TryGetValue(group, out var current) || until > current)
                    _offensives[group] = until;
                break;
            }
            case AbilityPurpose.Trinket:
            {
                // On-use trinkets share a 20 s activation lockout in game; the
                // memory keeps a second trinket from firing into the lockout
                // when MaxDps cannot see the item cooldown.
                _offensives["Trinket.OnUse"] = Math.Max(
                    _offensives.TryGetValue("Trinket.OnUse", out var current) ? current : 0,
                    nowMs + 20_000);
                break;
            }
        }
    }

    /// <summary>Records that an on-use trinket fired (shared 20 s lockout).</summary>
    public void NoteTrinketUse(long nowMs) =>
        _offensives["Trinket.OnUse"] = Math.Max(
            _offensives.TryGetValue("Trinket.OnUse", out var current) ? current : 0,
            nowMs + 20_000);

    /// <summary>Strongest defensive tier currently running (None when nothing is active).</summary>
    public DefensiveTier ActiveDefensiveTier(long nowMs)
    {
        var tier = DefensiveTier.None;
        foreach (var (until, candidate) in _defensives.Values)
            if (nowMs < until && candidate > tier) tier = candidate;
        return tier;
    }

    /// <summary>True when an offensive cooldown of the same pair-group was used inside its window.</summary>
    public bool OffensiveActive(string? group, long nowMs) =>
        group is not null && _offensives.TryGetValue(group, out var until) && nowMs < until;

    /// <summary>Drops expired entries (housekeeping; cheap, called from Advance).</summary>
    public void ClearExpired(long nowMs)
    {
        if (_defensives.Count > 0)
        {
            List<string>? dead = null;
            foreach (var (group, entry) in _defensives)
                if (nowMs >= entry.UntilMs) (dead ??= []).Add(group);
            if (dead is not null) foreach (var group in dead) _defensives.Remove(group);
        }
        if (_offensives.Count > 0)
        {
            List<string>? dead = null;
            foreach (var (group, until) in _offensives)
                if (nowMs >= until) (dead ??= []).Add(group);
            if (dead is not null) foreach (var group in dead) _offensives.Remove(group);
        }
    }

    public void Reset()
    {
        _defensives.Clear();
        _offensives.Clear();
    }
}

/// <summary>
/// Hard execution-safety gates shared by the policy (intelligence on) and the
/// scheduler (intelligence off). These are the only checks allowed to delay a
/// MaxDps main suggestion — they exist because sending a GCD-riding key while
/// the player is casting or channeling cannot help and can clip a channel.
///
/// Off-GCD exemptions are deliberately narrow and *verified*:
///  * Interrupt — off-GCD by game design; a kick must land mid-cast/channel.
///  * Consumable / Trinket — item activations are off-GCD and do not cancel a
///    cast or channel.
///  * A catalogued ability whose GCD is curated as OffGcd — but never a
///    gap-closer / movement / escape: those move the player, and movement
///    cancels both a hard cast and a channel.
/// </summary>
internal static class ExecutionSafety
{
    /// <summary>Hold reason for an active hard cast.</summary>
    internal const string CastReason = "player cast in progress";

    /// <summary>Hold reason for an active channel.</summary>
    internal const string ChannelReason = "player channel in progress";

    /// <summary>Reason this candidate must be held for the active cast state, else null.</summary>
    public static string? CastHoldReason(Slot slot, int spellId, PlayerCastState cast, AbilityCatalog catalog)
    {
        if (cast is not (PlayerCastState.Casting or PlayerCastState.Channeling)) return null;
        if (slot is Slot.Interrupt or Slot.Consumable or Slot.Trinket) return null;

        var ability = spellId > 0 ? catalog.TryGet(spellId) : null;
        if (ability is { Gcd: GcdKind.OffGcd, GcdVerified: true }
            && ability.Purpose is not (AbilityPurpose.GapCloser or AbilityPurpose.Movement or AbilityPurpose.Escape))
            return null;

        return cast == PlayerCastState.Channeling ? ChannelReason : CastReason;
    }

    public static bool IsCastReason(string reason) =>
        reason == CastReason || reason == ChannelReason;
}

/// <summary>Everything the evaluator may read for one candidate.</summary>
internal sealed class PolicyInput
{
    public required Slot Slot { get; init; }
    /// <summary>Decoded ability identity (0 when the protocol knew no spell id).</summary>
    public required int SpellId { get; init; }
    public required CombatContext Context { get; init; }
    public required PolicyOptions Options { get; init; }
    public required PolicyMemory Memory { get; init; }
    public required long NowMs { get; init; }
    public required bool InCombat { get; init; }
    public required bool HasTarget { get; init; }
}

/// <summary>
/// The situational intelligence: decides USE / HOLD / SKIP for one candidate
/// from the ability knowledge base and the observable combat context.
///
/// Deterministic, allocation-light and pure (the only mutable input is
/// <see cref="PolicyMemory"/>, which records the companion's own send
/// history). UNKNOWN observations always degrade to a documented behaviour:
/// companion-only slots hold; MaxDps-gated slots fail open to MaxDps's own
/// readiness gate, which stays the authority for them — the policy never
/// invents certainty beyond that:
///
///  * Main rotation: never blocked by knowledge. Only two gates exist —
///    out of range (SKIP) and an active cast/channel (HOLD, see
///    <see cref="ExecutionSafety"/>). Identity unknown changes nothing.
///  * Defensives: MaxDps's own HP gate is the primary trigger; the policy
///    only prevents *wasteful* use (redundant buff, overlapping mitigation)
///    and gates reflect-type abilities on an observed incoming cast.
///  * Interrupts: vetoed when the observed target cast is explicitly
///    not interruptible or has already ended (v5 sensors).
///  * Offensives: prevented from firing during a cast/channel and while a
///    paired cooldown window is already running.
///  * Mobility: only target-reaching gap closers are ever automatic, and
///    only when the target is confirmed outside melee and the ability is
///    confirmed in range. Escape/repositioning utilities are never automatic.
///  * Self-heals: only exist in Solo mode, gated on player HP with an
///    overheal guard and an active-immunity guard.
/// </summary>
internal static class PolicyEvaluator
{
    public static PolicyDecision Evaluate(PolicyInput input, AbilityCatalog catalog)
    {
        var ability = input.SpellId > 0 ? catalog.TryGet(input.SpellId) : null;
        if (ability is null)
        {
            var generic = Generic(input, catalog);
            return ProviderStamp.Stamp(CandidateProviders.Generic, generic, SourceForSlot(input.Slot), EvidenceForSlot(input.Slot));
        }
        return Known(input, ability, catalog);
    }

    /// <summary>Interrupt vetoes shared by the catalogued and generic paths.</summary>
    internal static PolicyDecision? InterruptVetoes(PolicyInput input)
    {
        var ctx = input.Context;
        if (ctx.TargetCastInterruptible == TriState.No)
            return PolicyDecision.Skip("observed target cast is not interruptible");
        // Only trust the "no cast" verdict when the v5 sensor block was
        // actually computed (ContextValid) — a v4/v1 frame is all-UNKNOWN and
        // must keep failing open.
        if (ctx.ContextValid && ctx.TargetCasting == TriState.No)
            return PolicyDecision.Skip("no live target cast observed (stale MaxDps flag)");
        return null;
    }

    /// <summary>
    /// Fallback for abilities the catalog does not know (main-rotation spells,
    /// vendor drift, no spell id on a stale frame). Behaviour is exactly the
    /// pre-intelligence policy plus the hard execution-safety cast hold:
    /// MaxDps suggestions are authoritative.
    /// </summary>
    private static PolicyDecision Generic(PolicyInput input, AbilityCatalog catalog)
    {
        var ctx = input.Context;
        var range = ctx.SlotRange[(int)input.Slot];
        var exec = ExecutionSafety.CastHoldReason(input.Slot, input.SpellId, ctx.Cast, catalog);
        if (exec is not null) return PolicyDecision.Hold(exec);
        switch (input.Slot)
        {
            case Slot.Main:
                if (range == TriState.No) return PolicyDecision.Unavailable("target out of range");
                return PolicyDecision.Use("MaxDps main candidate; actionable");
            case Slot.Interrupt:
                if (InterruptVetoes(input) is { } veto) return veto;
                if (range == TriState.No) return PolicyDecision.Unavailable("target out of ability range");
                return PolicyDecision.Use("interrupt candidate; MaxDps gate says a live interruptible cast exists");
            case Slot.Offensive:
                if (ctx.Cast is PlayerCastState.Casting or PlayerCastState.Channeling)
                    return PolicyDecision.Hold("player cast/channel in progress");
                if (range == TriState.No) return PolicyDecision.Unavailable("target out of range");
                return PolicyDecision.Use("offensive candidate; no conflict observed");
            case Slot.Defensive:
                // Uncatalogued defensive: no tier knowledge exists, so any
                // companion-recorded mitigation blocks a second one (an
                // emergency HP reading overrides, as in the catalogued path).
                if ((!ctx.HpValid || ctx.HpPct > input.Options.EmergencyHpPct)
                    && input.Memory.ActiveDefensiveTier(input.NowMs) != DefensiveTier.None)
                    return PolicyDecision.Hold("another mitigation is already active");
                return PolicyDecision.Use("MaxDps defensive gate fired");
            case Slot.Consumable:
                return PolicyDecision.Use("consumable candidate; MaxDps gate passed");
            case Slot.Trinket:
                // On-use trinkets share a 20 s activation lockout in game; two
                // different trinkets cannot both fire, and MaxDps cannot see
                // the item lockout — the memory supplies it.
                if (input.Memory.OffensiveActive("Trinket.OnUse", input.NowMs))
                    return PolicyDecision.Skip("another on-use trinket was used inside the shared lockout");
                return PolicyDecision.Use("trinket candidate; MaxDps gate passed");
            default:
                return PolicyDecision.Hold("uncatalogued ability; holding (knowledge required)");
        }
    }

    private static PolicyDecision Known(PolicyInput input, AbilityDefinition ability, AbilityCatalog catalog)
    {
        var ctx = input.Context;
        var opts = input.Options;
        var slot = (int)input.Slot;
        var range = ctx.SlotRange[slot];

        var provider = CandidateProviders.For(input.Slot, ability);
        var pinput = new ProviderInput { Input = input, Ability = ability, Catalog = catalog, Range = range };

        // ---- User ability policy (absolute, checked before everything) -----
        // OFF is a hard automatic-use prohibition: no urgency, MaxDps
        // recommendation, Solo mode or emergency HP overrides it. ON is only
        // eligibility — the normal gates below still decide USE/HOLD/SKIP. The
        // curated NeverAutomatic flag is the DEFAULT state, not a veto: a user
        // can explicitly enable even a manual-by-design ability.
        var mode = opts.Abilities?.ModeOf(ability.SpellId) ?? UserAbilityMode.Default;
        var enabled = opts.Abilities?.IsEnabled(ability) ?? !ability.NeverAutomatic;
        if (!enabled)
        {
            var reason = mode switch
            {
                UserAbilityMode.Never => "user policy disabled",
                UserAbilityMode.Manual => "user policy: manual",
                _ => opts.Abilities?.OverrideOf(ability.SpellId) == false
                    ? "user policy disabled"
                    : "ability is manual by design",
            };
            return ProviderStamp.Stamp(provider, PolicyDecision.Skip(reason), $"user policy: {reason}");
        }
        // Richer user modes (§24): eligible, but only in their own game mode.
        if (mode == UserAbilityMode.SoloOnly && !opts.SoloEnabled)
            return ProviderStamp.Stamp(provider, PolicyDecision.Hold("user policy: solo only"), "user policy: solo only");
        if (mode == UserAbilityMode.NormalOnly && opts.SoloEnabled)
            return ProviderStamp.Stamp(provider, PolicyDecision.Hold("user policy: normal only"), "user policy: normal only");

        // ---- Registry enforcement (v2.6) ------------------------------------
        // An entry whose intelligence is not meaningful (Incomplete / Unknown /
        // UnsafeToAutomate) can never be generated by the companion's own
        // candidate sources. On a MaxDps slot it keeps exactly the pre-registry
        // generic behaviour (MaxDps's own gate stays authoritative), which is
        // how the modeled class-spell rotation tail is still followed.
        if (ability.Status is IntelligenceStatus.Incomplete or IntelligenceStatus.Unknown
            or IntelligenceStatus.UnsafeToAutomate)
        {
            var companionOnly = input.Slot is Slot.Mobility or Slot.SelfHeal
                || (input.Slot == Slot.Defensive && ctx.DefensiveCatalogSource);
            if (companionOnly || ability.Automation == AutomationContext.Manual)
                return ProviderStamp.Stamp(provider, PolicyDecision.Skip("ability intelligence incomplete; companion never generates it"),
                    "registry: intelligence incomplete, companion never generates it");
            return ProviderStamp.Stamp(provider, Generic(input, catalog), "registry: delegated to MaxDps gate");
        }

        // ---- Hard execution-safety gate (first: nothing overrides it) ------
        // Holds every GCD-riding / unverified ability while the player is
        // casting or channeling — including the main rotation. The main is
        // held too: a channel is not ours to clip, and a queued press adds
        // nothing (the next tick after the cast ends fires immediately).
        // Verified off-GCD tools (interrupts, items, curated off-GCD
        // cooldowns that do not move the player) stay usable.
        var exec = ExecutionSafety.CastHoldReason(input.Slot, input.SpellId, ctx.Cast, catalog);
        if (exec is not null) return ProviderStamp.Stamp(provider, PolicyDecision.Hold(exec), exec);

        // The category branch itself lives in the explicit provider; every
        // branch, reason string and evidence list is byte-identical to the
        // inline policy it replaced.
        return provider.Evaluate(pinput);
    }

    /// <summary>Name of the provider that owns an uncatalogued slot (generic path).</summary>
    private static CandidateSourceKind SourceForSlot(Slot slot) => slot switch
    {
        Slot.Main or Slot.Offensive or Slot.Defensive or Slot.Interrupt or Slot.Consumable or Slot.Trinket
            => CandidateSourceKind.MaxDpsWire,
        _ => CandidateSourceKind.CompanionGapFill,
    };

    private static string[] EvidenceForSlot(Slot slot) => slot switch
    {
        Slot.Main or Slot.Offensive or Slot.Defensive or Slot.Interrupt or Slot.Consumable or Slot.Trinket
            => ["uncatalogued identity; MaxDps gate authoritative"],
        _ => ["uncatalogued companion slot; knowledge required"],
    };

}
