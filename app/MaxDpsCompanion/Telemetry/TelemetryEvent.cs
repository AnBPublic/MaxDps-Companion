using System.Text.Json.Serialization;

namespace MaxDpsCompanion;

/// <summary>
/// Local rotation telemetry (format v1), opt-in and off by default.
///
/// PRIVACY CONTRACT: every value here comes from this process or from the
/// decoded pixel protocol (player-bound key Strokes, engine state flags,
/// timestamps, our own status messages). No Blizzard API return values, no
/// secret/protected data, no names, no chat, no memory, and no network —
/// recording never leaves the machine.
///
/// The raw cell colours are deliberately not recorded: decode failures carry
/// a <see cref="DecodeFault"/> classification instead, which is the diagnostic
/// part without shipping screen content.
/// </summary>
internal static class TelemetryFormat
{
    public const int Version = 1;
}

internal static class TelemetryKind
{
    public const string Session = "session";
    public const string Tick = "tick";
    public const string Send = "send";
    public const string Link = "link";
}

/// <summary>A decoded keybind: virtual-key code + modifier flags only.</summary>
internal sealed record TelemetryStroke
{
    [JsonPropertyName("vk")] public byte VirtualKey { get; init; }
    [JsonPropertyName("sh")] public bool Shift { get; init; }
    [JsonPropertyName("ct")] public bool Ctrl { get; init; }
    [JsonPropertyName("alt")] public bool Alt { get; init; }

    public static TelemetryStroke? From(KeyStroke? stroke) =>
        stroke is { } s ? new TelemetryStroke { VirtualKey = s.VirtualKey, Shift = s.Shift, Ctrl = s.Ctrl, Alt = s.Alt } : null;

    public KeyStroke ToKeyStroke() => new(VirtualKey, Shift, Ctrl, Alt);

    public string Describe() => ToKeyStroke().Describe();
}

/// <summary>One slot observation: the decoded stroke plus tracker bookkeeping.</summary>
internal sealed record TelemetryCandidate
{
    [JsonPropertyName("slot")] public Slot Slot { get; init; }
    [JsonPropertyName("key")] public TelemetryStroke? Stroke { get; init; }
    [JsonPropertyName("en")] public bool Enabled { get; init; }
    [JsonPropertyName("act")] public bool Actionable { get; init; }
    [JsonPropertyName("seen")] public long FirstSeenMs { get; init; }
    [JsonPropertyName("chg")] public long LastChangedMs { get; init; }
    [JsonPropertyName("pressedMs")] public long LastPressedMs { get; init; }
    [JsonPropertyName("pressed")] public bool EverPressed { get; init; }
}

/// <summary>The decision result for the tick (legacy or intelligence).</summary>
internal sealed record TelemetryDecision
{
    [JsonPropertyName("src")] public string Source { get; init; } = "";
    [JsonPropertyName("sel")] public Slot? Selected { get; init; }
    [JsonPropertyName("reason")] public DecisionReason? Reason { get; init; }
    [JsonPropertyName("conf")] public int? Confidence { get; init; }
    [JsonPropertyName("stale")] public bool DemotedStale { get; init; }
    [JsonPropertyName("order")] public Slot[] Order { get; init; } = [];
}

/// <summary>One recorded policy verdict (slot + ability + decision + reason).</summary>
internal sealed record TelemetryVerdict
{
    [JsonPropertyName("slot")] public Slot Slot { get; init; }
    [JsonPropertyName("spell")] public int SpellId { get; init; }
    [JsonPropertyName("v")] public string Verdict { get; init; } = "";
    [JsonPropertyName("r")] public string Reason { get; init; } = "";

    /// <summary>Provider that produced the verdict (v2.7; omitted for legacy/unknown).</summary>
    [JsonPropertyName("prov")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Provider { get; init; }

    /// <summary>Candidate source kind (v2.7; omitted when None).</summary>
    [JsonPropertyName("src")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; init; }

    /// <summary>Human-readable "why" facts (v2.7; omitted when empty).</summary>
    [JsonPropertyName("why")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Evidence { get; init; }

    /// <summary>Ext2 (v3.0.0): this verdict is the SelfHeal2 alternate (omitted when false).</summary>
    [JsonPropertyName("alt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Alternate { get; init; }

    /// <summary>Ext2: the alternate's own range tri-state ('U'/'I'/'O'); null for a primary verdict.</summary>
    [JsonPropertyName("arng")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AlternateRange { get; init; }

    /// <summary>R2 (sustain-cd): SelfHeal verdict was in the cooldown wait (omitted when false).</summary>
    [JsonPropertyName("cdWait")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CdWait { get; init; }

    /// <summary>R2: when the SelfHeal stroke was last tried (ms); omitted when never.</summary>
    [JsonPropertyName("lastTriedMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? LastTriedMs { get; init; }

    /// <summary>R2: optional curated reset hint for the ability (informational; omitted when null).</summary>
    [JsonPropertyName("resetHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResetHint { get; init; }
}

/// <summary>
/// The solo/self-sustain thresholds the policy ran with, recorded so a replay
/// recomputes with the exact same options.
/// </summary>
internal sealed record TelemetryOptions
{
    [JsonPropertyName("solo")] public bool Solo { get; init; }
    [JsonPropertyName("em")] public int EmergencyHpPct { get; init; }
    [JsonPropertyName("sus")] public int SelfSustainHpPct { get; init; }
    [JsonPropertyName("esc")] public int DefensiveEscalateHpPct { get; init; }

    /// <summary>Explicit per-ability ON overrides (sorted comma list; null = none).</summary>
    [JsonPropertyName("on")] public string? AbilitiesOn { get; init; }

    /// <summary>Explicit per-ability OFF overrides (sorted comma list; null = none).</summary>
    [JsonPropertyName("off")] public string? AbilitiesOff { get; init; }
}

/// <summary>
/// The scheduler/policy outcome for the tick: the plan head and reason, the
/// combat context the policy saw, and the per-candidate verdicts. This is the
/// explainability record for "why was this situation ability pressed or not".
/// The range/buff/intr fields are the per-slot sensor observations the policy
/// read; they carry no secret data (the bridge already degraded every sensor to
/// a plain value or UNKNOWN before the wire).
/// </summary>
internal sealed record TelemetryPolicy
{
    [JsonPropertyName("fresh")] public bool Fresh { get; init; }
    [JsonPropertyName("sel")] public Slot? Selected { get; init; }
    [JsonPropertyName("reason")] public string Reason { get; init; } = "";
    [JsonPropertyName("conf")] public int Confidence { get; init; }
    [JsonPropertyName("suppressed")] public int Suppressed { get; init; }
    [JsonPropertyName("held")] public int Held { get; init; }
    [JsonPropertyName("skipped")] public int Skipped { get; init; }
    [JsonPropertyName("detail")] public string? Detail { get; init; }

    /// <summary>Provider of the plan head action (v2.7; omitted when none/legacy).</summary>
    [JsonPropertyName("prov")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Provider { get; init; }

    /// <summary>Human-readable "why" facts of the plan head action (v2.7; omitted when empty).</summary>
    [JsonPropertyName("why")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Why { get; init; }

    [JsonPropertyName("hp")] public int? HpPct { get; init; }
    [JsonPropertyName("hpKnown")] public bool HpKnown { get; init; }

    /// <summary>Ext2 (v3.0.0): where the HP reading came from (Plain/Curve/Unknown); omitted on legacy records.</summary>
    [JsonPropertyName("hpSrc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HpSource { get; init; }

    /// <summary>Ext2: upper bound of a curve HP band (overheal guard); null on legacy/plain records.</summary>
    [JsonPropertyName("hpUp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? HpPctUpper { get; init; }
    [JsonPropertyName("cast")] public string? Cast { get; init; }
    [JsonPropertyName("melee")] public string? Melee { get; init; }
    [JsonPropertyName("tcast")] public string? TargetCast { get; init; }
    [JsonPropertyName("tint")] public string? TargetInterruptible { get; init; }
    [JsonPropertyName("ctx")] public bool ContextValid { get; init; }

    /// <summary>Per-slot range: U=unknown, I=in range, O=out of range.</summary>
    [JsonPropertyName("rang")] public string? Range { get; init; }

    /// <summary>Per-slot self-buff active bits as 8 chars ('0'/'1').</summary>
    [JsonPropertyName("buff")] public string? Buffs { get; init; }

    /// <summary>
    /// Additive (v2.3) MaxDps defensive urgency (HP curve). Null marks a policy
    /// record written before v2.3 recorded urgency: replay treats those as
    /// legacy and does not recompute their verdicts (documented in
    /// docs/TELEMETRY.md).
    /// </summary>
    [JsonPropertyName("du")] public string? DefensiveUrgency { get; init; }

    /// <summary>Additive (v2.3) stagger-curve urgency (Purifying Brew).</summary>
    [JsonPropertyName("dsu")] public string? StaggerUrgency { get; init; }

    /// <summary>True when the Defensive slot came from the catalog gap-fill, omitted otherwise.</summary>
    [JsonPropertyName("dsrc")] public bool? DefensiveCatalogSource { get; init; }

    /// <summary>
    /// v3.0.0 additive: the decoded class/spec the tick ran under. The
    /// Offensive gap-fill has no wire source bit, so its source is derived by
    /// id membership in the per-spec list — replay needs the same class/spec to
    /// re-derive it. Omitted on legacy records (null = not recorded).
    /// </summary>
    [JsonPropertyName("cls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Class { get; init; }

    /// <summary>v3.0.0 additive: spec name for the tick (see <see cref="Class"/>).</summary>
    [JsonPropertyName("spec")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Spec { get; init; }

    /// <summary>
    /// R2 (sustain-cd): HP is in the sustain window and no ready self-heal
    /// candidate exists (omitted on legacy records / when false).
    /// </summary>
    [JsonPropertyName("cdWait")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CdWait { get; init; }

    /// <summary>R2: last time the SelfHeal slot was attempted/sent (ms); omitted when never.</summary>
    [JsonPropertyName("lastTriedMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? LastTriedMs { get; init; }

    [JsonPropertyName("opts")] public TelemetryOptions? Options { get; init; }
    [JsonPropertyName("verdicts")] public TelemetryVerdict[]? Verdicts { get; init; }
}

/// <summary>One input send (spell / target / interact) with its pacing interval.</summary>
internal sealed record TelemetrySend
{
    [JsonPropertyName("what")] public string What { get; init; } = "";
    [JsonPropertyName("slot")] public Slot? Slot { get; init; }
    [JsonPropertyName("key")] public TelemetryStroke Key { get; init; } = new();
    [JsonPropertyName("intervalMs")] public long IntervalMs { get; init; }

    /// <summary>Ability identity that was sent (0 = unknown/not a spell; omitted then).</summary>
    [JsonPropertyName("sp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int SpellId { get; init; }
}

/// <summary>One JSONL telemetry line. Null sections are omitted on write.</summary>
internal sealed record TelemetryEvent
{
    [JsonPropertyName("fmt")] public int Format { get; init; } = TelemetryFormat.Version;
    [JsonPropertyName("kind")] public string Kind { get; init; } = "";
    [JsonPropertyName("seq")] public long Seq { get; init; }
    [JsonPropertyName("tMs")] public long TMs { get; init; }
    [JsonPropertyName("utc")] public string? Utc { get; init; }

    // ----- tick context (protocol-observable state only) -----
    [JsonPropertyName("proto")] public int? Proto { get; init; }
    [JsonPropertyName("state")] public string? State { get; init; }
    [JsonPropertyName("hb")] public int? Heartbeat { get; init; }
    [JsonPropertyName("inCombat")] public bool? InCombat { get; init; }
    [JsonPropertyName("onGcd")] public bool? OnGcd { get; init; }
    [JsonPropertyName("hasTarget")] public bool? HasTarget { get; init; }
    [JsonPropertyName("link")] public bool? Link { get; init; }
    [JsonPropertyName("note")] public string? Note { get; init; }
    [JsonPropertyName("err")] public string? Error { get; init; }
    [JsonPropertyName("fault")] public string? Fault { get; init; }

    [JsonPropertyName("slots")] public TelemetryStroke?[]? Slots { get; init; }
    [JsonPropertyName("cands")] public TelemetryCandidate[]? Candidates { get; init; }
    [JsonPropertyName("dec")] public TelemetryDecision? Decision { get; init; }
    [JsonPropertyName("pol")] public TelemetryPolicy? Policy { get; init; }
    [JsonPropertyName("send")] public TelemetrySend? Send { get; init; }
    [JsonPropertyName("staleAfterMs")] public int? StaleAfterMs { get; init; }

    // ----- session metadata -----
    [JsonPropertyName("app")] public string? App { get; init; }
    [JsonPropertyName("events")] public int? Capacity { get; init; }

    /// <summary>Ability-catalog revision of the recording (replay skew warning).</summary>
    [JsonPropertyName("cat")] public int? CatalogVersion { get; init; }

    public static TelemetryEvent Session(long tMs, string app, int proto, int capacity, bool recording, string? note = null, int catalogVersion = 0) => new()
    {
        Kind = TelemetryKind.Session,
        TMs = tMs,
        App = app,
        Proto = proto,
        Capacity = capacity,
        Link = recording,
        Note = note ?? (recording ? "start" : "stop"),
        CatalogVersion = catalogVersion > 0 ? catalogVersion : null,
    };

    public static TelemetryEvent Tick(
        long tMs,
        BridgeFrame? frame,
        DecodeFault fault,
        DecisionContext? context,
        DecisionResult? decision,
        bool intelligenceEnabled,
        ActionCandidate[] candidates,
        string note,
        bool visible,
        TelemetryPolicy? policy = null) => new()
    {
        Kind = TelemetryKind.Tick,
        TMs = tMs,
        Proto = frame?.Version,
        State = frame?.State.ToString(),
        Heartbeat = frame?.Heartbeat,
        InCombat = frame?.InCombat,
        OnGcd = frame?.OnGcd,
        HasTarget = frame?.HasTarget,
        Link = visible,
        Note = note,
        Fault = fault == DecodeFault.None ? null : fault.ToString(),
        Slots = frame?.Slots.Select(TelemetryStroke.From).ToArray(),
        Candidates = candidates.Select(ToTelemetry).ToArray(),
        Decision = decision is { } d ? ToTelemetry(d, intelligenceEnabled) : null,
        Policy = policy,
        StaleAfterMs = (int?)context?.StaleAfterMs,
    };

    /// <summary>Builds the explainability record from the scheduler plan + context.</summary>
    public static TelemetryPolicy BuildPolicy(SchedulePlan plan, bool fresh, CombatContext? combat, PolicyOptions? options)
    {
        TelemetryVerdict[]? verdicts = null;
        if (plan.Verdicts.Length > 0)
        {
            verdicts = new TelemetryVerdict[plan.Verdicts.Length];
            for (var i = 0; i < plan.Verdicts.Length; i++)
            {
                var entry = plan.Verdicts[i];
                verdicts[i] = new TelemetryVerdict
                {
                    Slot = entry.Slot,
                    SpellId = entry.SpellId,
                    Verdict = entry.Verdict.ToString(),
                    Reason = entry.Reason,
                    Provider = string.IsNullOrEmpty(entry.Provider) ? null : entry.Provider,
                    Source = entry.Source == CandidateSourceKind.None ? null : entry.Source.ToString(),
                    Evidence = entry.Evidence.Count > 0 ? entry.Evidence.ToArray() : null,
                    Alternate = entry.Alternate,
                    AlternateRange = entry.Alternate ? EncodeTriState(entry.Range) : null,
                    CdWait = entry.CdWait,
                    LastTriedMs = entry.LastTriedMs > 0 ? entry.LastTriedMs : null,
                    ResetHint = string.IsNullOrEmpty(entry.ResetHint) ? null : entry.ResetHint,
                };
            }
        }
        var head = plan.Actions.Length > 0 ? plan.Actions[0] : default;
        var headProvider = plan.Actions.Length > 0 && head.Provider.Length > 0 ? head.Provider : null;
        var headWhy = plan.Actions.Length > 0 && head.Evidence.Count > 0 ? head.Evidence.ToArray() : null;
        return new TelemetryPolicy
        {
            Fresh = fresh,
            Selected = plan.Selected,
            Reason = plan.Reason.ToString(),
            Confidence = plan.Confidence,
            Suppressed = plan.Suppressed,
            Held = plan.PolicyHeld,
            Skipped = plan.PolicySkipped,
            Detail = plan.PolicyDetail,
            Provider = headProvider,
            Why = headWhy,
            HpPct = combat is { HpValid: true } c ? c.HpPct : null,
            HpKnown = combat?.HpValid ?? false,
            HpSource = combat is { HpValid: true } csrc ? csrc.HpSource.ToString() : null,
            HpPctUpper = combat is { HpValid: true } cup ? cup.HpPctUpper : null,
            Cast = combat?.Cast.ToString(),
            Melee = combat?.TargetInMelee.ToString(),
            TargetCast = combat?.TargetCasting.ToString(),
            TargetInterruptible = combat?.TargetCastInterruptible.ToString(),
            ContextValid = combat?.ContextValid ?? false,
            Range = combat is null ? null : EncodeRange(combat.SlotRange),
            Buffs = combat is null ? null : EncodeBuffs(combat.SlotBuffActive),
            DefensiveUrgency = combat?.DefensiveUrgency.ToString(),
            StaggerUrgency = combat?.StaggerUrgency.ToString(),
            DefensiveCatalogSource = combat is { DefensiveCatalogSource: true } ? true : null,
            Class = combat?.Class,
            Spec = combat?.Spec,
            CdWait = plan.SelfHealCoolingDown,
            LastTriedMs = plan.SelfHealLastTriedMs > 0 ? plan.SelfHealLastTriedMs : null,
            Options = options is null ? null : new TelemetryOptions
            {
                Solo = options.SoloEnabled,
                EmergencyHpPct = options.EmergencyHpPct,
                SelfSustainHpPct = options.SelfSustainHpPct,
                DefensiveEscalateHpPct = options.DefensiveEscalateHpPct,
                AbilitiesOn = EncodeOverrides(options.Abilities?.EncodeOn()),
                AbilitiesOff = EncodeOverrides(options.Abilities?.EncodeOff()),
            },
            Verdicts = verdicts,
        };
    }

    /// <summary>Empty override sets are omitted (old line shape preserved).</summary>
    private static string? EncodeOverrides(string? encoded) =>
        string.IsNullOrEmpty(encoded) ? null : encoded;

    private static string EncodeTriState(TriState value) => value switch
    {
        TriState.Yes => "I",
        TriState.No => "O",
        _ => "U",
    };

    private static string EncodeRange(TriState[] range)
    {
        var chars = new char[PixelProtocol.SlotCount];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = (int)range.Length > i ? range[i] switch
            {
                TriState.Yes => 'I',
                TriState.No => 'O',
                _ => 'U',
            } : 'U';
        return new string(chars);
    }

    private static string EncodeBuffs(TriState[] buffs)
    {
        // Telemetry keeps the v1 '1'/'0' character shape: Yes -> '1', anything
        // else (No or Unknown) -> '0'. The buff gate treats Unknown exactly like
        // No (documented fail-open), so a replayed verdict is deterministic even
        // though Unknown itself is not preserved in the recording.
        var chars = new char[PixelProtocol.SlotCount];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = (int)buffs.Length > i && buffs[i] == TriState.Yes ? '1' : '0';
        return new string(chars);
    }

    public static TelemetryEvent Sent(long tMs, string what, Slot? slot, KeyStroke stroke, long intervalMs, int spellId = 0) => new()
    {
        Kind = TelemetryKind.Send,
        TMs = tMs,
        Send = new TelemetrySend
        {
            What = what,
            Slot = slot,
            Key = new TelemetryStroke { VirtualKey = stroke.VirtualKey, Shift = stroke.Shift, Ctrl = stroke.Ctrl, Alt = stroke.Alt },
            IntervalMs = intervalMs,
            SpellId = spellId > 0 ? spellId : 0,
        },
    };

    public static TelemetryEvent LinkEvent(long tMs, bool visible, string? note, DecodeFault fault = DecodeFault.None) => new()
    {
        Kind = TelemetryKind.Link,
        TMs = tMs,
        Link = visible,
        Note = note,
        Fault = fault == DecodeFault.None ? null : fault.ToString(),
    };

    private static TelemetryCandidate ToTelemetry(ActionCandidate candidate) => new()
    {
        Slot = candidate.Slot,
        Stroke = TelemetryStroke.From(candidate.Stroke),
        Enabled = candidate.Enabled,
        Actionable = candidate.Actionable,
        FirstSeenMs = candidate.FirstSeenMs,
        LastChangedMs = candidate.LastChangedMs,
        LastPressedMs = candidate.LastPressedMs,
        EverPressed = candidate.EverPressed,
    };

    private static TelemetryDecision ToTelemetry(DecisionResult decision, bool intelligenceEnabled) => new()
    {
        Source = intelligenceEnabled ? "intelligence" : "legacy",
        Selected = decision.Selected,
        Reason = decision.Reason,
        Confidence = decision.Confidence,
        DemotedStale = decision.DemotedStale,
        Order = decision.Order,
    };
}
