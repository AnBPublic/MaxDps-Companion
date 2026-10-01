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
    /// Solo HP-banded escalation (v3.3.0): the survival ladder widens beyond
    /// self-heals. Minor absorbs at/below this HP, majors lower, immunities
    /// lowest. Requires SoloEnabled + SoloEscalation.
    /// </summary>
    public bool SoloEscalation { get; init; } = true;

    /// <summary>At/below this HP% Solo offers Minor absorbs (default 75).</summary>
    public int SoloMinorHpPct { get; init; } = 75;

    /// <summary>At/below this HP% Solo offers Major defensives (default 50).</summary>
    public int SoloMajorHpPct { get; init; } = 50;

    /// <summary>At/below this HP% Solo offers immunities (default 30).</summary>
    public int SoloImmunityHpPct { get; init; } = 30;

    /// <summary>
    /// Per-ability user automation policy (explicit ON/OFF overrides). Null =
    /// every ability follows its curated default (tests / legacy recordings).
    /// </summary>
    public AbilityPolicy? Abilities { get; init; }

    /// <summary>
    /// Companion-side restrict-only rotation preset (Stream 3). Full = the
    /// historical behaviour. Burst holds major offensives/consumable/trinket
    /// until the TTK estimate is valid.
    /// </summary>
    public RotationPreset Preset { get; init; } = RotationPreset.Full;

    /// <summary>
    /// Companion-side restrict-only target preset (Stream 3). SingleTarget =
    /// the historical behaviour. Aoe conserves catalogued single-target-only
    /// offensive cooldowns.
    /// </summary>
    public TargetPreset TargetPreset { get; init; } = TargetPreset.SingleTarget;

    /// <summary>
    /// [TimeToKill] Fallback (v3.6.0, spec §4): what the TTK guard does when no
    /// estimate is available. Threaded by the evaluator into
    /// <see cref="ProviderInput.Fallback"/> so <c>ConserveMajors</c> reaches the
    /// offensive grace hold; default is the documented fail-open.
    /// </summary>
    public TtkFallback TimeToKillFallback { get; init; } = TtkPolicy.DefaultFallback;

    public static PolicyOptions Standard { get; } = new();

    public static PolicyOptions FromSettings(AppSettings settings) => new()
    {
        SoloEnabled = settings.SoloEnabled,
        EmergencyHpPct = settings.SoloEmergencyHpPct,
        SelfSustainHpPct = settings.SoloSelfSustainHpPct,
        DefensiveEscalateHpPct = settings.SoloDefensiveEscalateHpPct,
        SoloEscalation = settings.SoloEscalationEnabled,
        SoloMinorHpPct = EffectiveSoloBand(settings.SoloMinorHpPct, 75),
        SoloMajorHpPct = EffectiveSoloBand(settings.SoloMajorHpPct, 50),
        SoloImmunityHpPct = EffectiveSoloBand(settings.SoloImmunityHpPct, 30),
        Abilities = settings.Abilities,
        Preset = settings.ModePreset,
        TargetPreset = settings.TargetMode,
        TimeToKillFallback = settings.TimeToKillFallback,
    };

    /// <summary>
    /// The ordering validator: Immunity &lt; Major &lt; Minor must hold, else the
    /// band that violates it falls back to its default so the ladder can never
    /// invert (a major firing above a minor would defeat escalation).
    /// </summary>
    internal static (int Minor, int Major, int Immunity) ValidateSoloBands(int minor, int major, int immunity)
    {
        if (immunity >= major) immunity = 30;
        if (major >= minor) major = 50;
        if (minor <= major) minor = 75;
        if (immunity >= major) immunity = Math.Min(30, major - 1);
        return (minor, major, immunity);
    }

    private static int EffectiveSoloBand(int value, int fallback) =>
        value is >= 5 and <= 99 ? value : fallback;
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
///
/// The catalog <see cref="CastKind"/> is honoured: an ability that is itself a
/// CastTime/Channel cannot be started while a cast/channel is already running
/// (holding is unconditional for it, even if it were flagged off-GCD). An
/// Instant is only ever held because it rides the GCD; a *verified* off-GCD
/// instant neither rides the GCD nor cancels the active cast/channel and is
/// therefore exempt.
///
/// Movement is covered through the cast state only: the bridge has no
/// movement flag on the wire, and the companion never reads a game API. The
/// "movement cancels a cast/channel" rule is enforced by holding gap-closer /
/// movement / escape abilities during any cast/channel (below) — no separate
/// movement signal is invented.
/// </summary>
internal static class ExecutionSafety
{
    /// <summary>Hold reason for an active hard cast.</summary>
    internal const string CastReason = "player cast in progress";

    /// <summary>Hold reason for an active channel.</summary>
    internal const string ChannelReason = "player channel in progress";

    /// <summary>Reason when a melee ability's target is confirmed outside melee reach.</summary>
    internal const string MeleeRangeReason = "target out of melee range";

    /// <summary>Reason this candidate must be held for the active cast state, else null.</summary>
    public static string? CastHoldReason(Slot slot, int spellId, PlayerCastState cast, AbilityCatalog catalog)
    {
        if (cast is not (PlayerCastState.Casting or PlayerCastState.Channeling)) return null;
        if (slot is Slot.Interrupt or Slot.Consumable or Slot.Trinket) return null;

        var ability = spellId > 0 ? catalog.TryGet(spellId) : null;

        // An uncatalogued ability keeps the pre-intelligence conservative hold.
        if (ability is null) return HoldReasonFor(cast);

        // Movement abilities are never exempt: movement cancels a hard cast
        // and a channel, whichever GCD they ride.
        if (ability.Purpose is AbilityPurpose.GapCloser or AbilityPurpose.Movement or AbilityPurpose.Escape)
            return HoldReasonFor(cast);

        // A CastTime/Channel ability cannot be started while a cast/channel is
        // already in progress, even if it were off-GCD.
        if (ability.Cast is CastKind.CastTime or CastKind.Channel)
            return HoldReasonFor(cast);

        // The only exemption: a *verified* off-GCD instant. It neither rides
        // the GCD nor cancels the active cast/channel. An unverified off-GCD
        // (default guess) or an on-GCD ability rides the GCD and is held.
        if (ability.Gcd == GcdKind.OffGcd && ability.GcdVerified)
            return null;

        return HoldReasonFor(cast);
    }

    private static string HoldReasonFor(PlayerCastState cast) =>
        cast == PlayerCastState.Channeling ? ChannelReason : CastReason;

    /// <summary>
    /// Central melee-position gate (AWARENESS stream). A melee ability cannot
    /// land on a target confirmed outside melee reach, so the candidate is
    /// structurally Unavailable ("target out of melee range"). The gate is
    /// catalog-driven (<see cref="AbilityDefinition.Range"/> /
    /// <see cref="AbilityDefinition.TargetRange"/>), so every class/talent
    /// entry inherits it with no per-class list.
    ///
    /// Fail-open: only a confirmed <see cref="TriState.No"/> holds; Yes and
    /// Unknown allow (the bridge may not have a melee probe on a legacy frame).
    /// Gap-closer / movement / escape abilities are excluded — they are *used*
    /// when the target is out of melee and the Mobility provider owns that
    /// decision.
    ///
    /// A positive per-ability range probe wins: when the bridge's
    /// <c>IsSpellInRange</c> for this exact slot already says Yes, the spell is
    /// confirmed in range and the generic melee estimate (a separate
    /// CheckInteractDistance reading) must not override it. The melee gate thus
    /// only fires when the ability is not positively in range (probe No or
    /// Unknown).
    ///
    /// Range (SlotRange) and line of sight are deliberately not otherwise
    /// handled here. The ranged/enemy SlotRange gate already lives in every
    /// catalogued provider with its own reason ("target out of range" /
    /// "target out of ability range"); hoisting it centrally would rewrite
    /// those byte-identical reasons. LoS is absent entirely because it is not
    /// observable in Midnight (no safe unit line-of-sight API; the bridge never
    /// probes it), and the companion must not invent a signal it cannot read.
    /// </summary>
    public static string? MeleeRangeHoldReason(AbilityDefinition? ability, Slot slot, CombatContext ctx)
    {
        if (ability is null) return null;
        if (ctx.TargetInMelee != TriState.No) return null;
        if (ability.Purpose is AbilityPurpose.GapCloser or AbilityPurpose.Movement or AbilityPurpose.Escape)
            return null;
        if (ability.Range != RangeKind.Melee && ability.TargetRange != RangeRequirement.InMelee)
            return null;
        var index = (int)slot;
        if (index >= 0 && index < ctx.SlotRange.Length && ctx.SlotRange[index] == TriState.Yes)
            return null;
        return MeleeRangeReason;
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
///  * Main rotation: never blocked by knowledge. Only three gates exist —
///    a confirmed out-of-melee target for a melee-only ability (UNAVAILABLE),
///    out of range (UNAVAILABLE) and an active cast/channel (HOLD, see
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

        if (CrowdControlVetoes.Evaluate(input, ability, catalog) is { } cc) return cc;

        var provider = CandidateProviders.For(input.Slot, ability);
        var pinput = new ProviderInput
        {
            Input = input,
            Ability = ability,
            Catalog = catalog,
            Range = range,
            Fallback = opts.TimeToKillFallback,
        };

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
                || (input.Slot == Slot.Defensive && ctx.DefensiveCatalogSource)
                || (input.Slot == Slot.Offensive
                    && catalog.IsOffensiveGapFill(ctx.Class, ctx.Spec, ability.SpellId));
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

        // ---- Melee-position awareness gate (AWARENESS stream) --------------
        // Hoisted before the provider so every catalogued melee ability is
        // gated identically (the providers' own Melee checks remain for the
        // paths the companion builds directly). Only a confirmed out-of-melee
        // holds; Unknown fails open. Mobility is excluded (used out of melee).
        var melee = ExecutionSafety.MeleeRangeHoldReason(ability, input.Slot, ctx);
        if (melee is not null) return ProviderStamp.Stamp(provider, PolicyDecision.Unavailable(melee), melee);

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
