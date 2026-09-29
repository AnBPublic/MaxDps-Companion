using System.Text.Json.Serialization;

namespace MaxDpsCompanion;

/// <summary>
/// Which MaxDps slot an ability can arrive in. Mirrors <see cref="Slot"/> plus
/// the two companion-only slots (Mobility, SelfHeal) whose keybinds the bridge
/// resolves from the generated catalog's per-spec extra lists.
/// </summary>
internal enum AbilityCategory
{
    Main = 0,
    Offensive,
    Defensive,
    Interrupt,
    Consumable,
    Trinket,
    Mobility,
    SelfHeal,
    /// <summary>Manual utility (CC/purge/threat/dispel): never automatic by default.</summary>
    Utility,
}

/// <summary>
/// What the ability is FOR. Purpose drives the policy rule that is applied;
/// category only drives the fallback. This is the field that lets the engine
/// answer "should this be used NOW?" instead of "is it available?".
/// </summary>
internal enum AbilityPurpose
{
    Unknown = 0,
    /// <summary>Core rotation damage — MaxDps is authoritative; minimal gating.</summary>
    Rotational,
    /// <summary>Big damage cooldown (burst window).</summary>
    MajorOffensive,
    /// <summary>Smaller damage cooldown / sustain cooldown.</summary>
    MinorOffensive,
    /// <summary>Big damage reduction or major defensive.</summary>
    DefensiveMajor,
    /// <summary>Small/medium damage reduction, short defensive.</summary>
    DefensiveMinor,
    /// <summary>Complete immunity or near-immunity.</summary>
    Immunity,
    /// <summary>Absorb shield.</summary>
    Absorb,
    /// <summary>Reflects incoming spells (Spell Reflection, Diffuse Magic).</summary>
    Reflect,
    /// <summary>Heals the player (active self-sustain).</summary>
    SelfHeal,
    /// <summary>Interrupt / kick.</summary>
    Interrupt,
    /// <summary>Closes distance to the current target (Charge, Shadowstep...).</summary>
    GapCloser,
    /// <summary>Escape / repositioning away (Vanish, Vengeful Retreat, Disengage).</summary>
    Escape,
    /// <summary>Movement utility with no safe automatic trigger (Sprint, Blink, Steed).</summary>
    Movement,
    /// <summary>Dispel (root/snare/magic). Kept manual — debuff type is not observable.</summary>
    Dispel,
    /// <summary>Crowd control (fear/poly/root/stun). Manual by design: target state is not observable.</summary>
    CrowdControl,
    /// <summary>Enemy purge/spellsteal. Manual by design: buff identity is not observable.</summary>
    Purge,
    /// <summary>Threat/aggro manipulation (taunt, threat resets). Manual by design.</summary>
    Threat,
    /// <summary>External defensive aimed at an ally; never automatic without ally state.</summary>
    External,
    Consumable,
    Trinket,
}

/// <summary>
/// What an entry in the registry IS. The catalog only ever carries
/// player-usable combat abilities plus the explicitly curated manual utilities;
/// the remaining kinds exist so a future input (or an audit correction) can
/// classify an entry honestly instead of silently treating everything as
/// active. Passives/auras/talent modifiers never appear as suggestions; when
/// they materially change an active ability they are recorded as
/// <see cref="AbilityDefinition.TalentNote"/> / <see cref="AbilityDefinition.Relations"/>.
/// </summary>
internal enum AbilityKind
{
    Unknown = 0,
    ActiveCombatAbility,
    Passive,
    Aura,
    TalentModifier,
    InternalSpell,
    QuestSpell,
    ProfessionSpell,
    NonCombatAbility,
    Deprecated,
}

/// <summary>
/// How well this ability is understood — the registry's honesty field. There
/// must be no silent "generic default" hiding an unresearched ability: an
/// entry whose intelligence is not yet meaningful says so and cannot drive
/// automatic situational use.
///
///  * Verified        — cross-checked against the live client DB2 + a second
///                      current source (spell id, name, class, behaviour).
///  * ResearchBacked  — a current 12.1 research record exists (source + date).
///  * MaxDpsBacked    — MaxDps itself carries the ability's use intelligence
///                      (vendor category membership); Companion adds no rule.
///  * CompanionRule   — a deliberate Companion policy rule (curated defaults).
///  * ConservativeSafety — the rule exists only to prevent waste/harm; it is
///                      not a claim about optimal use.
///  * Heuristic       — a best-effort classification without a second source.
///  * ManualByDesign  — never automatic; the user must decide.
///  * UnsafeToAutomate— automation is known to be dangerous.
///  * Incomplete      — no meaningful intelligence yet (visible, never silent).
///  * Unknown         — not classified at all (should not occur for automatic).
/// </summary>
internal enum IntelligenceStatus
{
    Unknown = 0,
    Incomplete,
    MaxDpsBacked,
    CompanionRule,
    ConservativeSafety,
    Heuristic,
    ResearchBacked,
    Verified,
    ManualByDesign,
    UnsafeToAutomate,
}

/// <summary>Who is allowed to offer this ability as an automatic action.</summary>
internal enum AutomationContext
{
    /// <summary>The companion may generate it independently (curated extras / gap-fill).</summary>
    Autonomous = 0,
    /// <summary>Only ever acted on when MaxDps itself surfaced the suggestion.</summary>
    MaxDpsOnly,
    /// <summary>Never automatic under any source (manual-by-design).</summary>
    Manual,
}

/// <summary>
/// Who owns the WHEN-TO-USE intelligence for this ability (v2.7 registry §4).
/// Ownership is deliberately orthogonal to <see cref="IntelligenceCompleteness"/>:
/// an ability can be MaxDps-owned and fully delegated, or Companion-owned and
/// merely live-unverified. Ownership answers "who decides?"; completeness
/// answers "how good is that decision knowledge?".
/// </summary>
internal enum IntelligenceOwnership
{
    /// <summary>No usable intelligence and no delegation (should not occur; audit violation).</summary>
    Unavailable = 0,
    /// <summary>Companion rules decide when the ability is eligible (interrupts, sustains, mobility, curated).</summary>
    Companion,
    /// <summary>MaxDps decides; the companion only allows, safety-blocks, holds or observes.</summary>
    MaxDps,
    /// <summary>Both decide: MaxDps surfaces the recommendation, companion gates it (defensives).</summary>
    Shared,
    /// <summary>Never automatic by design; the user decides (CC/purge/dispel/threat/external).</summary>
    Manual,
}

/// <summary>
/// How complete the ability's intelligence is (v2.7 registry §4) — the honesty
/// axis. Independent from ownership: Recklessness is MaxDps-owned and fully
/// Delegated; Pummel is Companion-owned and Complete; Intimidating Shout is
/// Manual and ManualByDesign; a modeled class-spell row is ResearchPending.
/// </summary>
internal enum IntelligenceCompleteness
{
    /// <summary>No meaningful intelligence yet (Incomplete/Unknown status).</summary>
    ResearchPending = 0,
    /// <summary>Required facts exist and are sourced (live DB2 verified or current research).</summary>
    Complete,
    /// <summary>Some facts are deliberate conservative rules / heuristics, not a full claim.</summary>
    Partial,
    /// <summary>The companion explicitly delegates the when-to-use decision to MaxDps.</summary>
    Delegated,
    /// <summary>Never automatic by design.</summary>
    ManualByDesign,
    /// <summary>Automation is structurally impossible (unsafe, or a context that cannot be observed).</summary>
    Unobservable,
    /// <summary>Rules exist but no live-client behavior pass has been recorded for them yet.</summary>
    LiveUnverified,
}

/// <summary>
/// Why an ability's when-to-use decision is delegated to MaxDps (v2.7 §3).
/// Every MaxDps-owned entry must carry at least one reason; the tail entries
/// get deterministic reasons from their category/usage, curated entries can
/// state exact ones. A bare unexplained "MaxDpsOnly" is an audit violation.
/// </summary>
[Flags]
internal enum DelegationReason
{
    None = 0,
    /// <summary>The ability IS the MaxDps main rotation suggestion.</summary>
    MainRotation = 1 << 0,
    /// <summary>Firing order inside the rotation matters more than availability.</summary>
    RotationOrdering = 1 << 1,
    /// <summary>Resource/energy/rune pooling decisions the companion cannot observe.</summary>
    ResourceOptimization = 1 << 2,
    /// <summary>The ability only matters during a proc the companion cannot rank.</summary>
    ProcInteraction = 1 << 3,
    /// <summary>Talent/hero-talent conditional variants change the decision.</summary>
    TalentInteraction = 1 << 4,
    /// <summary>Fires inside a complex burst/buff window MaxDps models.</summary>
    ComplexBuffWindow = 1 << 5,
    /// <summary>Correct target selection is not observable/safe for the companion.</summary>
    TargetSelectionUnavailable = 1 << 6,
    /// <summary>The use condition depends on enemy count, which is not observable.</summary>
    EnemyCountUnavailable = 1 << 7,
    /// <summary>Behavior depends on secret values the companion must not read.</summary>
    SecretValueLimitation = 1 << 8,
    /// <summary>MaxDps already models this ability's decision; duplicating it adds risk, not value.</summary>
    MaxDpsAlreadyModelsThis = 1 << 9,
    /// <summary>Not worth duplicating: short cooldown, minor effect, or pure optimization.</summary>
    NotWorthDuplicating = 1 << 10,
    /// <summary>The required context (buff/debuff/ally/positioning) cannot be safely observed.</summary>
    InsufficientObservableContext = 1 << 11,
}

/// <summary>Why an ability stays manual — the mandatory reason for every Manual entry (v2.7 §5).</summary>
internal enum ManualReason
{
    None = 0,
    /// <summary>CC requires a target state (diminishing returns, boss immunity) that is not observable.</summary>
    CrowdControlTargetStateUnobservable,
    /// <summary>Dispel requires debuff identity/type that is not observable.</summary>
    DebuffIdentityUnobservable,
    /// <summary>Purge/spellsteal requires enemy buff identity that is not observable.</summary>
    BuffIdentityUnobservable,
    /// <summary>Taunt/threat tools depend on group threat state that is not observable.</summary>
    ThreatStateUnobservable,
    /// <summary>External defensives depend on ally state that is not observable.</summary>
    AllyStateUnobservable,
    /// <summary>Raid/group coordination decision (usage could harm the group).</summary>
    RaidCoordination,
    /// <summary>A safety rule: automatic use is known to be risky (movement/escape/immunity by default).</summary>
    SafetyRule,
    /// <summary>The user's own choice to keep it manual.</summary>
    UserChoice,
    /// <summary>Other documented reason (see the entry note).</summary>
    Other,
}

/// <summary>What a registry entry's knowledge is based on (v2.7 §7 provenance fields).</summary>
internal enum AbilitySourceType
{
    /// <summary>Derived/modeled from a token layer (class-spell tail) — not a direct source claim.</summary>
    DerivedModel = 0,
    /// <summary>The pinned MaxDps vendor tables (membership/category/names).</summary>
    VendorPin,
    /// <summary>Hand-curated Companion policy knowledge.</summary>
    Curated,
    /// <summary>Current-patch research with a recorded source.</summary>
    Research,
    /// <summary>Official live-client DB2 export (spell-verification.json).</summary>
    LiveClientDb2,
}

/// <summary>How much confidence the source evidence carries (v2.7 §7).</summary>
internal enum SourceConfidence
{
    Unknown = 0,
    Low,
    Medium,
    High,
}

/// <summary>Which MaxDps surface (if any) carries this ability's own use intelligence.</summary>
internal enum MaxDpsRelationship
{
    NotSurfaced = 0,
    MainRotation,
    OffensiveBucket,
    DefensiveBucket,
    InterruptBucket,
    ConsumableBucket,
    TrinketBucket,
}

/// <summary>
/// Cooldown opportunity cost: what is given up by spending the ability now.
/// Major defensives/offensives are High/Critical; short tools are Low/Medium.
/// Curated where researched, otherwise derived from tier and cooldown length.
/// </summary>
internal enum OpportunityCost
{
    Unknown = 0,
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>How an interrupt-capable ability stops a cast.</summary>
internal enum InterruptKind
{
    Unknown = 0,
    /// <summary>A dedicated interrupt (kick): stops the cast and locks the school.</summary>
    Dedicated,
    /// <summary>A silence (also usable as an interrupt when the target is casting).</summary>
    Silence,
    /// <summary>A stun used as an interrupt tool.</summary>
    Stun,
    /// <summary>A knockback/displacement that breaks the cast.</summary>
    Displacement,
    /// <summary>An incapacitate that breaks the cast.</summary>
    Incapacitate,
}

/// <summary>What kind of offensive tool an ability is (offensive cooldown intelligence).</summary>
internal enum OffensiveUsage
{
    Unknown = 0,
    MajorBurst,
    MinorBurst,
    ShortCooldown,
    Execute,
    AoeOnly,
    SingleTargetOnly,
    ProcDriven,
    ResourceDriven,
    WindowDriven,
    DefensiveOffensiveHybrid,
    Summon,
    Transformation,
    Manual,
}

/// <summary>What a mobility ability actually does.</summary>
internal enum MobilityKind
{
    Unknown = 0,
    GapCloser,
    Disengage,
    Teleport,
    SpeedBurst,
    MovementImmunity,
    Escape,
}

/// <summary>
/// Explicit ability→ability relationships (registry §8/§24). Spell ID is the
/// canonical identity — never merge by display name. The first nine kinds are
/// the v2.6 vocabulary; the rest were added in v2.7. Historical aliases
/// (Spell Lock 19647 → 119910) are recorded here, never as duplicate rows.
/// </summary>
internal enum RelationshipKind
{
    Requires = 0,
    Enhances,
    Replaces,
    ConflictsWith,
    SynergizesWith,
    Consumes,
    Protects,
    Follows,
    Precedes,
    // ---- v2.7 additions ----
    /// <summary>Using this ability makes the target ability possible (e.g. builder → spender).</summary>
    Enables,
    /// <summary>Designed to be used together (paired cooldowns / burst windows).</summary>
    PairsWith,
    /// <summary>The reverse of <see cref="Replaces"/>: this entry was replaced by the target id.</summary>
    ReplacedBy,
    /// <summary>This entry supersedes the target id (newer variant, same role).</summary>
    Supersedes,
    /// <summary>This entry was superseded by the target id.</summary>
    SupersededBy,
    /// <summary>Requires a specific buff/aura (identity recorded in the note).</summary>
    RequiresBuff,
    /// <summary>Requires a target state (casting, below HP, in melee, ...).</summary>
    RequiresTargetState,
    /// <summary>Requires a resource state (rage/energy/combo points, ...).</summary>
    RequiresResourceState,
    /// <summary>Requires a specific talent (note names it).</summary>
    RequiresTalent,
    /// <summary>Requires a specific hero talent (note names it).</summary>
    RequiresHeroTalent,
    /// <summary>Shares a cooldown/buff window with the target id.</summary>
    SharesWindowWith,
    /// <summary>Must not be stacked with the target id (overlap waste / exclusivity).</summary>
    DoNotStackWith,
    /// <summary>An alias of the same live ability under another id (name traps, historical ids).</summary>
    Alias,
    /// <summary>A duplicate row for the same live ability (id-keyed merge candidate).</summary>
    Duplicate,
    /// <summary>A talent variant of the same ability under another id.</summary>
    TalentVariant,
    /// <summary>A hero-talent variant of the same ability under another id.</summary>
    HeroTalentVariant,
    /// <summary>A spec variant of the same ability under another id.</summary>
    SpecVariant,
    /// <summary>An id that existed historically for this ability (recorded, never merged by name).</summary>
    HistoricalId,
}

/// <summary>One directed relationship to another spell.</summary>
internal sealed record AbilityRelation(RelationshipKind Kind, int SpellId, string? Note = null);

/// <summary>
/// Contextual requirements beyond the dedicated flags already on the record.
/// Derived from the existing booleans and unioned with curated entries; this is
/// the machine-checkable "what context it requires" set (registry §6).
/// </summary>
[Flags]
internal enum AbilityRequirement
{
    None = 0,
    Target = 1,
    Enemy = 2,
    FriendlyTarget = 4,
    Combat = 8,
    Movement = 16,
    IncomingDamage = 32,
    EnemyCast = 64,
    Aura = 128,
    Talent = 256,
    Form = 512,
    SpecificCast = 1024,
}

/// <summary>Cooldown length band (derived; drives opportunity-cost defaults).</summary>
internal enum CooldownClass
{
    Unknown = 0,
    None,
    Short,
    Medium,
    Long,
    Major,
}

/// <summary>What the ability is aimed at (derived unless curated).</summary>
internal enum TargetType
{
    Unknown = 0,
    Self,
    Enemy,
    Friendly,
    Area,
}

/// <summary>
/// Future per-ability user modes. The UI persists ON/OFF today
/// (<see cref="AbilityPolicy"/> On=/Off= sets); the model already parses and
/// honours these richer modes so adding UI later cannot break storage.
/// </summary>
internal enum UserAbilityMode
{
    /// <summary>Follow the curated default (the ON/OFF override then applies).</summary>
    Default = 0,
    /// <summary>Eligible always; reserved (evaluates as Automatic until it has distinct semantics).</summary>
    Always,
    /// <summary>Absolute automatic-use prohibition (same as OFF).</summary>
    Never,
    /// <summary>Eligible for the normal gates (same as ON).</summary>
    Automatic,
    /// <summary>Eligible only while Solo mode is on.</summary>
    SoloOnly,
    /// <summary>Eligible only while Solo mode is off.</summary>
    NormalOnly,
    /// <summary>Never automatic; user must press it (distinct from OFF only in telemetry).</summary>
    Manual,
}

/// <summary>Which knowledge layer supplied an ability (UI tooltip + tests).</summary>
internal enum AbilityProvenance
{
    /// <summary>From the pinned vendor Cooldowns/interrupt tables.</summary>
    Vendor = 0,
    /// <summary>From the curated abilities.json policy layer (or its extras).</summary>
    Curated,
    /// <summary>From the generated class-spells book (vendor SpellData tokens).</summary>
    ClassSpell,
}

/// <summary>Defensive strength tier used for sequencing (smallest sufficient first).</summary>
internal enum DefensiveTier
{
    None = 0,
    Minor = 1,
    Major = 2,
    Immunity = 3,
}

/// <summary>
/// MaxDps defensive urgency. MaxDps has no discrete enum: it renders every
/// defensive through <c>GlowDefensiveHPMidnight</c> (vendor Buttons.lua:1056)
/// whose colour curve has exactly three control points — 0.3 = red (alpha 1),
/// 0.5 = yellow (alpha 0.5), 1.0 = green (alpha 0, invisible). The bridge
/// stages that rendered colour at the curve's own control points, so the
/// boundaries below are the vendor's, not invented HP bands:
///
///  * Red    = at/past the 0.3 red point (player HP ≤ 30%).
///  * Orange = the red→yellow interpolation band (30% < HP &lt; 50%); the
///             linear blend renders orange there.
///  * Yellow = the yellow→transparent fade (50% ≤ HP &lt; 100%).
///  * White  = the transparent end (HP = 100%): MaxDps renders nothing,
///             so the companion holds every automatic defensive (hard rule).
///  * Unknown = no reading (v5 frame, secret HP, failed probe). Unknown is
///             never treated as White or Red; majors hold, minors keep the
///             legacy gates.
///
/// Stagger-based abilities (Purifying Brew, vendor special case) use the
/// reversed curve: stagger ≥ 100% = Red, 50-100% = Orange, 30-50% = Yellow,
/// &lt; 30% = White.
/// </summary>
internal enum DefensiveUrgency
{
    Unknown = 0,
    White = 1,
    Yellow = 2,
    Orange = 3,
    Red = 4,
}

/// <summary>Which MaxDps curve defines an ability's defensive urgency.</summary>
internal enum DefensiveUrgencySource
{
    /// <summary>UnitHealthPercent("player") through the vendor HP curve (default).</summary>
    Hp = 0,
    /// <summary>UnitStagger / UnitHealthMax through the vendor reversed curve (Purifying Brew).</summary>
    Stagger,
}

/// <summary>Whether the ability starts/consumes the global cooldown.</summary>
internal enum GcdKind
{
    Unknown = 0,
    OnGcd,
    OffGcd,
}

/// <summary>Static range hint (runtime range comes from the protocol's per-slot probe).</summary>
internal enum RangeKind
{
    Unknown = 0,
    Melee,
    Short,
    Ranged,
    SelfOnly,
}

/// <summary>Cast behaviour hint used by the cast/channel policy.</summary>
internal enum CastKind
{
    Unknown = 0,
    Instant,
    CastTime,
    Channel,
}

/// <summary>Positional requirement against the current target.</summary>
internal enum RangeRequirement
{
    Any = 0,
    InMelee,
    OutOfMelee,
}

/// <summary>What to do when a required observable signal is UNKNOWN (Midnight secret / no data).</summary>
internal enum UnknownPolicy
{
    /// <summary>Fail open: proceed (MaxDps already gated the ability).</summary>
    Use = 0,
    /// <summary>Hold this tick; re-evaluate next tick.</summary>
    Hold,
    /// <summary>Skip; do not consider this ability without live evidence.</summary>
    Skip,
}

/// <summary>
/// One ability's deterministic policy record. Materialized by
/// <see cref="AbilityCatalog"/> from the vendor base table
/// (<c>Knowledge/vendor-abilities.json</c>) merged with the curated policy
/// overrides (<c>Knowledge/abilities.json</c>). Every field has a safe default;
/// only fields that differ from the category default need to be curated.
/// </summary>
internal sealed record AbilityDefinition(
    int SpellId,
    string Name,
    AbilityCategory Category,
    AbilityPurpose Purpose,
    DefensiveTier Tier,
    GcdKind Gcd,
    RangeKind Range,
    CastKind Cast,
    int CooldownMs,
    int DurationMs,
    int HealPctMaxHp,
    bool RequiresEnemyCast,
    RangeRequirement TargetRange,
    bool RequiresTarget,
    int? UseBelowHpPct,
    int? HoldAboveHpPct,
    bool HoldWhenBuffActive,
    bool NeverAutomatic,
    string? ConflictGroup,
    int Priority,
    UnknownPolicy Unknown,
    IReadOnlyList<string> Classes,
    IReadOnlyList<string> Specs,
    string? Note,
    string? Source)
{
    /// <summary>True when this ability consumes the global cooldown (unknown counts as yes for cast holds).</summary>
    public bool RidesGcd => Gcd != GcdKind.OffGcd;

    /// <summary>
    /// True when the off-GCD classification is *verified*: curated explicitly,
    /// or an intrinsically off-GCD category (interrupts and item activations
    /// are off-GCD by game design). Only verified off-GCD abilities are exempt
    /// from the cast/channel execution-safety hold — a default guess must not
    /// let an ability clip a channel.
    /// </summary>
    public bool GcdVerified { get; init; }

    /// <summary>Which knowledge layer supplied this ability (tooltip/test provenance).</summary>
    public AbilityProvenance Provenance { get; init; } = AbilityProvenance.Vendor;

    // ---- v2.6 ability intelligence registry (all init-only; ctor unchanged) ----

    /// <summary>What the entry is. The catalog carries active combat abilities; kinds beyond that exist for audit honesty.</summary>
    public AbilityKind Kind { get; init; } = AbilityKind.ActiveCombatAbility;

    /// <summary>Honesty field: how well this ability is understood (see <see cref="IntelligenceStatus"/>).</summary>
    public IntelligenceStatus Status { get; init; } = IntelligenceStatus.Unknown;

    /// <summary>Who may offer the ability automatically (autonomous / MaxDps-only / manual).</summary>
    public AutomationContext Automation { get; init; } = AutomationContext.Autonomous;

    /// <summary>Which MaxDps surface carries the ability's own use intelligence.</summary>
    public MaxDpsRelationship MaxDps { get; init; } = MaxDpsRelationship.NotSurfaced;

    /// <summary>What is given up by spending this ability now.</summary>
    public OpportunityCost Opportunity { get; init; } = OpportunityCost.Unknown;

    /// <summary>How this ability stops a cast (interrupt capability; Unknown for non-interrupts).</summary>
    public InterruptKind InterruptKind { get; init; } = InterruptKind.Unknown;

    /// <summary>What kind of offensive tool this is (Unknown for non-offensives).</summary>
    public OffensiveUsage OffensiveUsage { get; init; } = OffensiveUsage.Unknown;

    /// <summary>What a mobility ability does (Unknown otherwise).</summary>
    public MobilityKind MobilityKind { get; init; } = MobilityKind.Unknown;

    /// <summary>Contextual requirements beyond the dedicated flags (derived + curated union).</summary>
    public AbilityRequirement Requires { get; init; } = AbilityRequirement.None;

    /// <summary>Talent/hero-talent dependency note ("requires X"; null = base kit or unknown).</summary>
    public string? TalentNote { get; init; }

    /// <summary>Hero-talent dependency note (separate from <see cref="TalentNote"/>).</summary>
    public string? HeroTalentNote { get; init; }

    /// <summary>When the entry's data was last checked against a live source (ISO date).</summary>
    public string? PatchVerified { get; init; }

    /// <summary>Explicit ability relationships (curated; empty when none are known).</summary>
    public AbilityRelation[] Relations { get; init; } = [];

    /// <summary>Extra capability tags beyond the purpose-derived ones (e.g. "Offensive", "SelfSustain").</summary>
    public string[] CapabilityTags { get; init; } = [];

    /// <summary>Offensives: minimum enemies the use condition assumes (null = none; not observable today).</summary>
    public int? EnemyCountMin { get; init; }

    /// <summary>Offensives: the ability is planned around a burst/buff window, not used on sight.</summary>
    public bool HoldForBurst { get; init; }

    /// <summary>
    /// Minimum MaxDps defensive urgency at which this ability may fire
    /// automatically (tier default: Major/Immunity = Red, Minor/None = Yellow;
    /// curated override per ability). A live MaxDps recommendation of the
    /// exact ability lowers the requirement by one stage but never below
    /// Yellow (White always holds).
    /// </summary>
    public DefensiveUrgency MinimumUrgency { get; init; } = DefensiveUrgency.Yellow;

    /// <summary>
    /// True when <see cref="MinimumUrgency"/> was curated for this ability
    /// (not the tier default). A curated requirement is not lowered by a live
    /// MaxDps recommendation — the curation is the deliberate per-ability
    /// opportunity-cost decision.
    /// </summary>
    public bool MinimumUrgencyCurated { get; init; }

    /// <summary>Which vendor curve supplies this ability's urgency (Hp unless curated).</summary>
    public DefensiveUrgencySource UrgencySource { get; init; } = DefensiveUrgencySource.Hp;

    // ---- v2.7 intelligence coverage registry (orthogonal disposition axes) ----

    /// <summary>Who owns the when-to-use decision (Companion / MaxDps / Shared / Manual / Unavailable).</summary>
    public IntelligenceOwnership Ownership { get; init; } = IntelligenceOwnership.Unavailable;

    /// <summary>How complete that decision knowledge is (Complete / Partial / Delegated / ...).</summary>
    public IntelligenceCompleteness Completeness { get; init; } = IntelligenceCompleteness.ResearchPending;

    /// <summary>Why the decision was delegated to MaxDps (mandatory for MaxDps ownership).</summary>
    public DelegationReason Delegation { get; init; } = DelegationReason.None;

    /// <summary>Optional curated explanation of the delegation set.</summary>
    public string? DelegationNote { get; init; }

    /// <summary>Why this ability is manual (mandatory for Manual ownership).</summary>
    public ManualReason ManualReason { get; init; } = ManualReason.None;

    /// <summary>Patch the ability first appeared in (curated only; null = unknown/not stated).</summary>
    public string? IntroducedPatch { get; init; }

    /// <summary>Patch the entry's knowledge belongs to (defaults to the catalog patch).</summary>
    public string? SourcePatch { get; init; }

    /// <summary>Patch the entry was last validated against (stale detection for the patch guard).</summary>
    public string? LastValidatedPatch { get; init; }

    /// <summary>What the entry's knowledge is based on.</summary>
    public AbilitySourceType SourceType { get; init; } = AbilitySourceType.DerivedModel;

    /// <summary>Recorded evidence URL (research/curation only).</summary>
    public string? SourceUrl { get; init; }

    /// <summary>Confidence carried by the recorded source evidence.</summary>
    public SourceConfidence SourceConfidence { get; init; } = SourceConfidence.Unknown;

    /// <summary>
    /// True only when a live-client behavior pass recorded this ability. Derived
    /// and researched rules are otherwise honestly reported as live-unverified
    /// (§53: offline evidence is never live proof).
    /// </summary>
    public bool LiveVerified { get; init; }

    /// <summary>True when the entry's when-to-use decision is explicitly delegated to MaxDps.</summary>
    public bool MaxDpsOwned => Ownership == IntelligenceOwnership.MaxDps;

    /// <summary>True when a delegation reason is recorded (mandatory for <see cref="MaxDpsOwned"/>).</summary>
    public bool HasDelegationReason => Delegation != DelegationReason.None;

    /// <summary>True when a manual reason is recorded (mandatory for <see cref="IntelligenceOwnership.Manual"/>).</summary>
    public bool HasManualReason => ManualReason != ManualReason.None;

    /// <summary>May this ability ever be pressed automatically by any source?</summary>
    public bool Automatable => !NeverAutomatic && Automation != AutomationContext.Manual;

    /// <summary>
    /// True when there is a real candidate path behind an automatable entry:
    /// companion-generated entries need non-blocking intelligence; MaxDps-only
    /// entries need an explicit delegation reason. Manual/Unavailable have none.
    /// </summary>
    public bool HasCandidatePath => Automation switch
    {
        AutomationContext.Autonomous => AutomationAllowed,
        AutomationContext.MaxDpsOnly => HasDelegationReason,
        _ => false,
    };

    /// <summary>Heuristic: does this ability target the current enemy?</summary>
    public bool TargetsEnemy => TargetRange is RangeRequirement.InMelee or RangeRequirement.OutOfMelee
        || Range is RangeKind.Melee or RangeKind.Short or RangeKind.Ranged
        || Category is AbilityCategory.Main or AbilityCategory.Offensive or AbilityCategory.Interrupt;

    public bool IsDefensive => Purpose is AbilityPurpose.DefensiveMajor or AbilityPurpose.DefensiveMinor
        or AbilityPurpose.Immunity or AbilityPurpose.Absorb or AbilityPurpose.Reflect;

    /// <summary>Anything the survival rules apply to (mitigation or an active heal).</summary>
    public bool IsSurvival => IsDefensive || Purpose == AbilityPurpose.SelfHeal;

    // ---- derived intelligence (registry §6/§33/§41/§58) --------------------

    /// <summary>Cooldown length band (derived from <see cref="CooldownMs"/>).</summary>
    public CooldownClass CooldownClass => CooldownMs switch
    {
        < 1500 => CooldownClass.None,
        < 30_000 => CooldownClass.Short,
        < 90_000 => CooldownClass.Medium,
        < 180_000 => CooldownClass.Long,
        _ => CooldownClass.Major,
    };

    /// <summary>Manual-by-design: never offered automatically (curated default).</summary>
    public bool ManualByDesign => NeverAutomatic;

    /// <summary>
    /// May this ability ever be automated (by any source)? False for
    /// manual-by-design entries and for entries whose intelligence is not
    /// meaningful (Incomplete/Unknown/UnsafeToAutomate). Self-registry
    /// enforcement also rejects companion-generated candidates for these.
    /// </summary>
    public bool AutomationAllowed =>
        !NeverAutomatic && Status is not (IntelligenceStatus.Incomplete
            or IntelligenceStatus.Unknown or IntelligenceStatus.UnsafeToAutomate);

    /// <summary>Extra capability tags as a set for lookups.</summary>
    public bool HasTag(string tag)
    {
        for (var i = 0; i < CapabilityTags.Length; i++)
            if (string.Equals(CapabilityTags[i], tag, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public bool HasDefensiveCapability =>
        IsDefensive || Purpose == AbilityPurpose.External || HasTag("Defensive");
    public bool HasInterruptCapability =>
        Category == AbilityCategory.Interrupt || Purpose == AbilityPurpose.Interrupt || HasTag("Interrupt");
    public bool HasOffensiveCapability =>
        Purpose is AbilityPurpose.MajorOffensive or AbilityPurpose.MinorOffensive
        || Category == AbilityCategory.Offensive || HasTag("Offensive");
    public bool HasMobilityCapability =>
        Category == AbilityCategory.Mobility
        || Purpose is AbilityPurpose.GapCloser or AbilityPurpose.Movement or AbilityPurpose.Escape
        || HasTag("Mobility");
    public bool HasSelfSustainCapability =>
        Purpose == AbilityPurpose.SelfHeal || HealPctMaxHp > 0 || HasTag("SelfSustain");

    /// <summary>Usable as an emergency survival tool (immunity, major mitigation, emergency heal).</summary>
    public bool EmergencyCapability =>
        Purpose == AbilityPurpose.Immunity
        || Tier is DefensiveTier.Major or DefensiveTier.Immunity
        || (Purpose == AbilityPurpose.SelfHeal && (UseBelowHpPct is not null || CooldownMs >= 60_000))
        || (ConflictGroup?.StartsWith("Heal.Emergency", StringComparison.OrdinalIgnoreCase) ?? false)
        || HasTag("Emergency");

    /// <summary>What the ability is aimed at (derived; curated entries can refine via relations).</summary>
    public TargetType TargetKind =>
        Range == RangeKind.SelfOnly ? TargetType.Self
        : Purpose == AbilityPurpose.External ? TargetType.Friendly
        : TargetsEnemy ? TargetType.Enemy
        : TargetType.Self;

    /// <summary>True when the entry's intelligence is meaningful enough to drive a decision.</summary>
    public bool HasIntelligence =>
        Status is not (IntelligenceStatus.Incomplete or IntelligenceStatus.Unknown);
}

/// <summary>Slot ↔ category bridging helpers.</summary>
internal static class AbilityCategories
{
    public static AbilityCategory Of(Slot slot) => slot switch
    {
        Slot.Main => AbilityCategory.Main,
        Slot.Offensive => AbilityCategory.Offensive,
        Slot.Defensive => AbilityCategory.Defensive,
        Slot.Consumable => AbilityCategory.Consumable,
        Slot.Trinket => AbilityCategory.Trinket,
        Slot.Interrupt => AbilityCategory.Interrupt,
        Slot.Mobility => AbilityCategory.Mobility,
        Slot.SelfHeal => AbilityCategory.SelfHeal,
        _ => AbilityCategory.Main,
    };
}
