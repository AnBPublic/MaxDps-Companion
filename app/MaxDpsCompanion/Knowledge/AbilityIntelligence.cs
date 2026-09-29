using System.Text;

namespace MaxDpsCompanion;

/// <summary>How serious an audit finding is.</summary>
internal enum AuditSeverity
{
    /// <summary>Inventory line: the entry exists and is coherent.</summary>
    Ok = 0,
    /// <summary>Honest gap or delegation (Incomplete MaxDps-only rows, derived heuristics).</summary>
    Info,
    /// <summary>Data quality issue that does not change runtime behaviour.</summary>
    Warning,
    /// <summary>Registry/runtime agreement broken — must be zero for the build to be acceptable.</summary>
    Violation,
}

/// <summary>One audit finding for one ability.</summary>
internal sealed record AuditFinding(string Check, AuditSeverity Severity, string Detail);

/// <summary>One registry entry's audit view (also used by the ability inspector).</summary>
internal sealed record AuditRow(
    int SpellId,
    string Name,
    AbilityCategory Category,
    AbilityProvenance Provenance,
    IntelligenceStatus Status,
    AutomationContext Automation,
    MaxDpsRelationship MaxDps,
    bool Manual,
    bool Automatic,
    bool Defensive,
    bool Interrupt,
    bool Offensive,
    bool SelfSustain,
    bool Mobility,
    bool Utility,
    IReadOnlyList<AuditFinding> Findings)
{
    /// <summary>v2.7 ownership axis (Companion / MaxDps / Shared / Manual / Unavailable).</summary>
    public IntelligenceOwnership Ownership { get; init; } = IntelligenceOwnership.Unavailable;

    /// <summary>v2.7 completeness axis (Complete / Partial / Delegated / ...).</summary>
    public IntelligenceCompleteness Completeness { get; init; } = IntelligenceCompleteness.ResearchPending;

    /// <summary>Why the decision is delegated (mandatory for MaxDps ownership).</summary>
    public DelegationReason Delegation { get; init; } = DelegationReason.None;

    /// <summary>Why the ability is manual (mandatory for Manual ownership).</summary>
    public ManualReason ManualReason { get; init; } = ManualReason.None;

    /// <summary>Patch the knowledge belongs to.</summary>
    public string? SourcePatch { get; init; }

    /// <summary>True only when a live pass recorded this ability.</summary>
    public bool LiveVerified { get; init; }

    public bool HasViolation
    {
        get
        {
            for (var i = 0; i < Findings.Count; i++)
                if (Findings[i].Severity == AuditSeverity.Violation) return true;
            return false;
        }
    }
}

/// <summary>Aggregate counters for the coverage/audit report.</summary>
internal sealed record AuditSummary(
    int Total,
    int Automatic,
    int Manual,
    int Defensive,
    int Interrupts,
    int OffensiveCooldowns,
    int SelfSustain,
    int Mobility,
    int Utility,
    int CompanionGenerated,
    int MaxDpsOnly,
    int ResearchBacked,
    int Verified,
    int MaxDpsBacked,
    int CompanionRule,
    int ManualByDesign,
    int Incomplete,
    int Unknown,
    int Violations,
    int Warnings,
    // ---- v2.7 coverage axes ----
    int CompanionOwned,
    int MaxDpsOwned,
    int SharedOwned,
    int ManualOwned,
    int UnavailableOwned,
    int DelegatedWithoutReason,
    int ManualWithoutReason,
    int LiveVerified,
    int LiveUnverified,
    int Missing,
    int Stale);

/// <summary>The complete machine-generated audit.</summary>
internal sealed record AuditReport(
    string GamePatch,
    int InterfaceVersion,
    string? MaxDpsVersion,
    string? VerifiedDate,
    DateTime GeneratedUtc,
    AuditSummary Summary,
    IReadOnlyList<AuditRow> Rows,
    CoverageReport? Coverage = null)
{
    /// <summary>Row violations plus coverage violations (missing/stale) — the release bar is zero.</summary>
    public int TotalViolations => Summary.Violations + (Coverage?.Missing ?? 0) + (Coverage?.Stale ?? 0);

    /// <summary>True when the audit is clean (no row violations, nothing missing or stale).</summary>
    public bool Clean => TotalViolations == 0;
}

/// <summary>
/// The machine-checkable "intelligence complete" audit (registry §33/§54/§58).
/// It turns registry completeness into an enforceable property:
///
///  * every automatic entry must have a non-Incomplete/non-Unknown status
///    unless it is MaxDps-only (delegated — the companion never generates it);
///  * every interrupt-capable entry must state how it stops a cast;
///  * every companion-backed offensive must state its usage class (vendor-only
///    offensives may delegate to MaxDps);
///  * every manual-by-design entry must be structurally unable to auto-send;
///  * relations must point at catalogued spells.
///
/// The report is written by <c>--ability-audit=&lt;path&gt;</c> and pinned by
/// AbilityRegistryTests. Violations must be zero; Info/Warning counts are
/// reported honestly (no fabricated zeroes).
/// </summary>
internal static class AbilityIntelligence
{
    /// <summary>Audits one ability (used by the audit and by <c>--ability-info</c>).</summary>
    public static AuditRow Audit(AbilityDefinition ability, AbilityCatalog catalog)
    {
        var findings = new List<AuditFinding>();

        if (ability.SpellId <= 0)
            findings.Add(new AuditFinding("spell-id", AuditSeverity.Violation, "spell id must be positive"));
        if (string.IsNullOrWhiteSpace(ability.Name))
            findings.Add(new AuditFinding("name", AuditSeverity.Violation, "name must not be empty"));
        if (ability.Classes.Count == 0 && ability.Specs.Count == 0)
            findings.Add(new AuditFinding("class-spec", AuditSeverity.Warning, "no class or spec membership"));
        if (ability.Status == IntelligenceStatus.Unknown)
            findings.Add(new AuditFinding("status", AuditSeverity.Violation, "intelligence status is Unknown (no silent generic default)"));

        // v2.7 §5: no unexplained automatable abilities.
        if (ability.Ownership == IntelligenceOwnership.Unavailable && !ability.ManualByDesign)
            findings.Add(new AuditFinding("ownership", AuditSeverity.Violation,
                "no intelligence ownership (Companion/MaxDps/Shared/Manual expected)"));
        if (ability.MaxDpsOwned && !ability.HasDelegationReason)
            findings.Add(new AuditFinding("delegation-reason", AuditSeverity.Violation,
                "MaxDps-owned entry has no delegation reason (no bare MaxDpsOnly)"));
        if (ability.Ownership == IntelligenceOwnership.Manual && !ability.NeverAutomatic)
            findings.Add(new AuditFinding("manual-ownership", AuditSeverity.Violation,
                "Manual ownership requires the structural NeverAutomatic flag"));
        if (ability.Ownership == IntelligenceOwnership.Manual && !ability.HasManualReason)
            findings.Add(new AuditFinding("manual-reason", AuditSeverity.Violation,
                "manual entry has no manual reason"));
        if (ability.Automatable && !ability.HasCandidatePath)
            findings.Add(new AuditFinding("candidate-path", AuditSeverity.Violation,
                "automatable entry has no candidate path and no explicit MaxDps delegation"));
        if (string.IsNullOrWhiteSpace(ability.SourcePatch))
            findings.Add(new AuditFinding("patch-metadata", AuditSeverity.Violation,
                "entry has no source patch metadata (stale intelligence must be detectable)"));
        else if (AbilityCoverage.IsStale(ability, catalog.GamePatch))
            findings.Add(new AuditFinding("stale", AuditSeverity.Violation,
                $"knowledge patch {ability.SourcePatch} / last validated {ability.LastValidatedPatch ?? "-"} is older than supported {catalog.GamePatch}"));
        else if (AbilityCoverage.IsNewer(ability, catalog.GamePatch))
            findings.Add(new AuditFinding("patch-newer", AuditSeverity.Violation,
                $"knowledge patch {ability.SourcePatch} is newer than supported {catalog.GamePatch}; it must not silently drive this build"));

        var automatic = !ability.NeverAutomatic && ability.Automation != AutomationContext.Manual;
        var blockingStatus = ability.Status is IntelligenceStatus.Incomplete
            or IntelligenceStatus.Unknown or IntelligenceStatus.UnsafeToAutomate;
        if (automatic && blockingStatus)
        {
            if (ability.Automation == AutomationContext.MaxDpsOnly)
                findings.Add(new AuditFinding("automatic-intelligence", AuditSeverity.Info,
                    "Incomplete but MaxDps-only: the companion never generates it independently"));
            else
                findings.Add(new AuditFinding("automatic-intelligence", AuditSeverity.Violation,
                    $"automatic with status {ability.Status}; automatic situational use requires intelligence"));
        }

        if (ability.NeverAutomatic && ability.Automation != AutomationContext.Manual)
            findings.Add(new AuditFinding("manual-structure", AuditSeverity.Violation,
                "NeverAutomatic must carry AutomationContext.Manual"));

        if (ability.Automation == AutomationContext.Autonomous && ability.Provenance == AbilityProvenance.ClassSpell)
            findings.Add(new AuditFinding("companion-source", AuditSeverity.Violation,
                "the modeled class-spell layer must never be an autonomous companion candidate"));

        if (ability.Purpose == AbilityPurpose.DefensiveMajor && ability.Tier is not (DefensiveTier.Major or DefensiveTier.Immunity))
            findings.Add(new AuditFinding("defensive-tier", AuditSeverity.Violation,
                "a major defensive must carry a Major/Immunity tier"));

        // Interrupt capability must state how it stops a cast.
        if (ability.HasInterruptCapability && ability.InterruptKind == InterruptKind.Unknown)
            findings.Add(new AuditFinding("interrupt-intelligence", AuditSeverity.Violation,
                "interrupt-capable ability has no interrupt kind"));

        // Companion-backed offensives must state their usage class; vendor-only
        // rows and the MaxDps-only modeled tail may delegate the when-to-use
        // question to MaxDps itself.
        if (ability.HasOffensiveCapability && ability.OffensiveUsage == OffensiveUsage.Unknown
            && ability.Automation == AutomationContext.Autonomous
            && ability.Status is not IntelligenceStatus.MaxDpsBacked)
            findings.Add(new AuditFinding("offensive-intelligence", AuditSeverity.Violation,
                "companion-backed offensive has no usage class"));

        // Self-sustain should be quantified enough to gate an overheal decision.
        if (ability.Purpose == AbilityPurpose.SelfHeal && automatic
            && ability.HealPctMaxHp <= 0 && ability.UseBelowHpPct is null
            && !ability.EmergencyCapability && ability.HoldAboveHpPct is null
            && ability.Status is not IntelligenceStatus.MaxDpsBacked)
            findings.Add(new AuditFinding("self-sustain-intelligence", AuditSeverity.Warning,
                "self-heal has no quantified heal/HP data"));

        // Trusted automatic entries should carry a checked date when their
        // status claims research.
        if (ability.Status is IntelligenceStatus.ResearchBacked or IntelligenceStatus.Verified
            && string.IsNullOrWhiteSpace(ability.PatchVerified))
            findings.Add(new AuditFinding("patch-verified", AuditSeverity.Warning,
                "research-backed entry has no verification date"));

        // v2.7 hygiene: a delegation reason on a companion/shared entry is
        // meaningless (it would imply a delegation that is not happening);
        // researched entries should state how confident their source is.
        if (ability.HasDelegationReason && ability.Ownership != IntelligenceOwnership.MaxDps)
            findings.Add(new AuditFinding("delegation-hygiene", AuditSeverity.Warning,
                $"delegation reason {ability.Delegation} recorded on a {ability.Ownership}-owned entry"));
        if (ability.Status == IntelligenceStatus.ResearchBacked && ability.SourceConfidence == SourceConfidence.Unknown)
            findings.Add(new AuditFinding("source-confidence", AuditSeverity.Warning,
                "research-backed entry has no source confidence"));

        foreach (var relation in ability.Relations)
        {
            if (relation.SpellId <= 0)
                findings.Add(new AuditFinding("relation", AuditSeverity.Warning, "relation has no target spell"));
            else if (catalog.TryGet(relation.SpellId) is null)
                findings.Add(new AuditFinding("relation", AuditSeverity.Warning,
                    $"{relation.Kind} -> {relation.SpellId} is not in the catalog"));
        }

        if (findings.Count == 0)
            findings.Add(new AuditFinding("coherent", AuditSeverity.Ok, "registry entry coherent"));

        return new AuditRow(
            ability.SpellId, ability.Name, ability.Category, ability.Provenance,
            ability.Status, ability.Automation, ability.MaxDps,
            ability.NeverAutomatic, automatic,
            ability.HasDefensiveCapability, ability.HasInterruptCapability,
            ability.HasOffensiveCapability, ability.HasSelfSustainCapability,
            ability.HasMobilityCapability, ability.Category == AbilityCategory.Utility,
            findings)
        {
            Ownership = ability.Ownership,
            Completeness = ability.Completeness,
            Delegation = ability.Delegation,
            ManualReason = ability.ManualReason,
            SourcePatch = ability.SourcePatch,
            LiveVerified = ability.LiveVerified,
        };
    }

    /// <summary>Audits the whole catalog and aggregates the summary counters.</summary>
    public static AuditReport Audit(AbilityCatalog catalog, DateTime? generatedUtc = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var rows = new List<AuditRow>(catalog.Count);
        int automatic = 0, manual = 0, defensive = 0, interrupts = 0, offensive = 0,
            selfSustain = 0, mobility = 0, utility = 0, companionGenerated = 0, maxDpsOnly = 0,
            research = 0, verified = 0, maxDpsBacked = 0, companionRule = 0, manualByDesign = 0,
            incomplete = 0, unknown = 0, violations = 0, warnings = 0;
        int companionOwned = 0, maxDpsOwned = 0, sharedOwned = 0, manualOwned = 0, unavailableOwned = 0,
            delegatedWithoutReason = 0, manualWithoutReason = 0, liveVerified = 0, liveUnverified = 0;

        foreach (var ability in catalog.All)
        {
            var row = Audit(ability, catalog);
            rows.Add(row);
            if (row.Automatic) automatic++; else manual++;
            if (row.Defensive) defensive++;
            if (row.Interrupt) interrupts++;
            if (row.Offensive) offensive++;
            if (row.SelfSustain) selfSustain++;
            if (row.Mobility) mobility++;
            if (row.Utility) utility++;
            switch (row.Automation)
            {
                case AutomationContext.Autonomous: companionGenerated++; break;
                case AutomationContext.MaxDpsOnly: maxDpsOnly++; break;
            }
            switch (row.Status)
            {
                case IntelligenceStatus.ResearchBacked: research++; break;
                case IntelligenceStatus.Verified: verified++; break;
                case IntelligenceStatus.MaxDpsBacked: maxDpsBacked++; break;
                case IntelligenceStatus.CompanionRule: companionRule++; break;
                case IntelligenceStatus.ManualByDesign: manualByDesign++; break;
                case IntelligenceStatus.Incomplete: incomplete++; break;
                case IntelligenceStatus.Unknown: unknown++; break;
            }
            switch (row.Ownership)
            {
                case IntelligenceOwnership.Companion: companionOwned++; break;
                case IntelligenceOwnership.MaxDps: maxDpsOwned++; break;
                case IntelligenceOwnership.Shared: sharedOwned++; break;
                case IntelligenceOwnership.Manual: manualOwned++; break;
                default: unavailableOwned++; break;
            }
            if (row.Ownership == IntelligenceOwnership.MaxDps && row.Delegation == DelegationReason.None) delegatedWithoutReason++;
            if (row.Ownership == IntelligenceOwnership.Manual && row.ManualReason == ManualReason.None) manualWithoutReason++;
            if (row.LiveVerified) liveVerified++;
            else if (ability.Automatable && ability.Ownership != IntelligenceOwnership.MaxDps) liveUnverified++;
            if (row.HasViolation) violations++;
            foreach (var finding in row.Findings)
                if (finding.Severity == AuditSeverity.Warning) warnings++;
        }

        rows.Sort((a, b) => a.SpellId.CompareTo(b.SpellId));
        var coverage = AbilityCoverage.Build(catalog);
        var summary = new AuditSummary(
            rows.Count, automatic, manual, defensive, interrupts, offensive, selfSustain,
            mobility, utility, companionGenerated, maxDpsOnly, research, verified,
            maxDpsBacked, companionRule, manualByDesign, incomplete, unknown, violations, warnings,
            companionOwned, maxDpsOwned, sharedOwned, manualOwned, unavailableOwned,
            delegatedWithoutReason, manualWithoutReason, liveVerified, liveUnverified,
            coverage.Missing, coverage.Stale);
        return new AuditReport(
            catalog.GamePatch, catalog.InterfaceVersion, catalog.MaxDpsVersion, catalog.VerifiedDate,
            generatedUtc ?? DateTime.UtcNow, summary, rows, coverage);
    }

    /// <summary>Human/agent-readable inspector text for one spell (<c>--ability-info</c>).</summary>
    public static string Describe(int spellId, AbilityCatalog catalog)
    {
        var ability = catalog.TryGet(spellId);
        if (ability is null) return $"spell {spellId}: not in the ability registry";
        var row = Audit(ability, catalog);
        var sb = new StringBuilder();
        sb.AppendLine($"Ability       : {ability.Name} ({ability.SpellId})");
        sb.AppendLine($"Class/Spec    : {string.Join(", ", ability.Classes)} / {string.Join(", ", ability.Specs)}");
        sb.AppendLine($"Kind/Category : {ability.Kind} / {ability.Category} ({ability.Purpose})");
        sb.AppendLine($"Provenance    : {ability.Provenance}");
        sb.AppendLine($"Intelligence  : {ability.Status}  (automation: {ability.Automation}, MaxDps: {ability.MaxDps})");
        sb.AppendLine($"Ownership     : {ability.Ownership}  (completeness: {ability.Completeness}, candidate path: {ability.HasCandidatePath})");
        if (ability.HasDelegationReason)
            sb.AppendLine($"Delegation    : {ability.Delegation}{(ability.DelegationNote is { Length: > 0 } dn ? $" — {dn}" : "")}");
        if (ability.Ownership == IntelligenceOwnership.Manual || ability.HasManualReason)
            sb.AppendLine($"Manual reason : {ability.ManualReason}");
        sb.AppendLine($"Live verified : {ability.LiveVerified}  (offline evidence is not live proof)");
        sb.AppendLine($"Tier/Urgency  : {ability.Tier} / min {ability.MinimumUrgency}{(ability.MinimumUrgencyCurated ? " (curated)" : "")}");
        sb.AppendLine($"GCD/Range/Cast: {ability.Gcd}{(ability.GcdVerified ? " (verified)" : "")} / {ability.Range} / {ability.Cast}");
        sb.AppendLine($"Cooldown      : {ability.CooldownMs} ms ({ability.CooldownClass}), duration {ability.DurationMs} ms");
        sb.AppendLine($"Heal/HP gates : heal {ability.HealPctMaxHp}% / below {ability.UseBelowHpPct?.ToString() ?? "-"}% / hold above {ability.HoldAboveHpPct?.ToString() ?? "-"}%");
        sb.AppendLine($"Requirements  : {ability.Requires}");
        sb.AppendLine($"Opportunity   : {ability.Opportunity}");
        if (ability.HasInterruptCapability) sb.AppendLine($"Interrupt     : {ability.InterruptKind}");
        if (ability.HasOffensiveCapability)
            sb.AppendLine($"Offensive     : {ability.OffensiveUsage}{(ability.EnemyCountMin is { } n ? $" (enemies >= {n})" : "")}{(ability.HoldForBurst ? " (hold for burst window)" : "")}");
        if (ability.HasMobilityCapability) sb.AppendLine($"Mobility      : {ability.MobilityKind}");
        if (ability.TalentNote is { Length: > 0 } talent) sb.AppendLine($"Talent        : {talent}");
        if (ability.HeroTalentNote is { Length: > 0 } hero) sb.AppendLine($"Hero talent   : {hero}");
        sb.AppendLine($"Patch checked : {ability.PatchVerified ?? "-"}");
        sb.AppendLine("Patch data    : " +
            $"introduced {ability.IntroducedPatch ?? "-"} / source {ability.SourcePatch ?? "-"} / last validated {ability.LastValidatedPatch ?? "-"}" +
            (AbilityCoverage.IsStale(ability, catalog.GamePatch) ? "  [STALE]" : ""));
        sb.AppendLine($"Source        : {ability.SourceType} ({ability.SourceConfidence}){ (ability.SourceUrl is { Length: > 0 } url ? " " + url : "") }");
        foreach (var relation in ability.Relations)
            sb.AppendLine($"Relation      : {relation.Kind} -> {relation.SpellId}{(relation.Note is { Length: > 0 } note ? $" ({note})" : "")}");
        sb.AppendLine($"Context       : requires {ability.Requires}; unknown policy {ability.Unknown}");
        if (ability.Note is { Length: > 0 } note2) sb.AppendLine($"Note          : {note2}");
        if (ability.Source is { Length: > 0 } source) sb.AppendLine($"Source        : {source}");
        sb.AppendLine("Audit         :");
        foreach (var finding in row.Findings)
            sb.AppendLine($"  [{finding.Severity}] {finding.Check}: {finding.Detail}");
        return sb.ToString();
    }

    /// <summary>Renders the audit report as markdown (docs/research/ABILITY_REGISTRY_AUDIT.md).</summary>
    public static string ToMarkdown(AuditReport report, AbilityCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var sb = new StringBuilder(64 * 1024);
        sb.AppendLine("# Ability Registry Audit");
        sb.AppendLine();
        sb.AppendLine($"Generated (UTC): {report.GeneratedUtc:yyyy-MM-dd HH:mm:ss}  ");
        sb.AppendLine($"Game patch: {report.GamePatch} (interface {report.InterfaceVersion})  ");
        sb.AppendLine($"MaxDps version: {report.MaxDpsVersion ?? "-"}  ");
        sb.AppendLine($"Registry verified: {report.VerifiedDate ?? "-"}  ");
        sb.AppendLine();
        sb.AppendLine("Machine-generated by `MaxDpsCompanion.exe --ability-audit=<path>` from the embedded");
        sb.AppendLine("registry. Do not hand-edit; regenerate after any curated change.");
        sb.AppendLine();

        var s = report.Summary;
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Count |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Registry entries | {s.Total} |");
        sb.AppendLine($"| Automatic (eligible) | {s.Automatic} |");
        sb.AppendLine($"| Manual / never automatic | {s.Manual} |");
        sb.AppendLine($"| Defensive capability | {s.Defensive} |");
        sb.AppendLine($"| Interrupt capability | {s.Interrupts} |");
        sb.AppendLine($"| Offensive capability | {s.OffensiveCooldowns} |");
        sb.AppendLine($"| Self-sustain capability | {s.SelfSustain} |");
        sb.AppendLine($"| Mobility capability | {s.Mobility} |");
        sb.AppendLine($"| Utility (CC/purge/threat/dispel) | {s.Utility} |");
        sb.AppendLine($"| Companion-generated eligible (Autonomous) | {s.CompanionGenerated} |");
        sb.AppendLine($"| MaxDps-only (delegated) | {s.MaxDpsOnly} |");
        sb.AppendLine();

        sb.AppendLine("## Intelligence ownership (v2.7)");
        sb.AppendLine();
        sb.AppendLine("| Ownership | Count |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Companion (rules decide) | {s.CompanionOwned} |");
        sb.AppendLine($"| MaxDps (delegated) | {s.MaxDpsOwned} |");
        sb.AppendLine($"| Shared (MaxDps surfaces, companion gates) | {s.SharedOwned} |");
        sb.AppendLine($"| Manual (user decides) | {s.ManualOwned} |");
        sb.AppendLine($"| Unavailable (audit violation) | {s.UnavailableOwned} |");
        sb.AppendLine();
        sb.AppendLine($"Delegated without a reason: {s.DelegatedWithoutReason} (must be 0).  ");
        sb.AppendLine($"Manual without a reason: {s.ManualWithoutReason} (must be 0).  ");
        sb.AppendLine($"Live verified: {s.LiveVerified}; live unverified (automatic, companion/shared): {s.LiveUnverified}.");
        sb.AppendLine();

        if (report.Coverage is { } cov)
        {
            sb.AppendLine("## Coverage manifest (v2.7)");
            sb.AppendLine();
            sb.AppendLine("| Class | Count |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| DISCOVERED (union of sources) | {cov.Discovered} |");
            sb.AppendLine($"| REGISTERED | {cov.Registered} |");
            sb.AppendLine($"| AUTOMATABLE | {cov.Automatable} |");
            sb.AppendLine($"| COMPANION-GENERATED | {cov.CompanionGenerated} |");
            sb.AppendLine($"| MAXDPS-DELEGATED | {cov.MaxDpsDelegated} |");
            sb.AppendLine($"| SHARED-GATED | {cov.SharedGated} |");
            sb.AppendLine($"| MANUAL | {cov.Manual} |");
            sb.AppendLine($"| UNOBSERVABLE | {cov.Unobservable} |");
            sb.AppendLine($"| RESEARCH-PENDING | {cov.ResearchPending} |");
            sb.AppendLine($"| STALE | {cov.Stale} |");
            sb.AppendLine($"| MISSING | {cov.Missing} |");
            sb.AppendLine($"| FILTERED (documented exclusions) | {cov.Filtered} |");
            sb.AppendLine($"| duplicate display names (warning) | {cov.Duplicates} |");
            sb.AppendLine();
            if (cov.MissingIds.Count > 0)
            {
                sb.AppendLine($"Missing ids: {string.Join(", ", cov.MissingIds)}");
                sb.AppendLine();
            }
            if (cov.DuplicateDetails.Count > 0)
            {
                sb.AppendLine($"Duplicate-name findings ({cov.DuplicateDetails.Count} total; link them with relations instead of merging by name):");
                sb.AppendLine();
                foreach (var detail in cov.DuplicateDetails.Take(20))
                    sb.AppendLine($"- {detail}");
                if (cov.DuplicateDetails.Count > 20)
                    sb.AppendLine($"- ... and {cov.DuplicateDetails.Count - 20} more (see the JSON coverage manifest)");
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Intelligence status");
        sb.AppendLine();
        sb.AppendLine("| Status | Count |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Verified | {s.Verified} |");
        sb.AppendLine($"| ResearchBacked | {s.ResearchBacked} |");
        sb.AppendLine($"| MaxDpsBacked | {s.MaxDpsBacked} |");
        sb.AppendLine($"| CompanionRule | {s.CompanionRule} |");
        sb.AppendLine($"| ManualByDesign | {s.ManualByDesign} |");
        sb.AppendLine($"| Incomplete | {s.Incomplete} |");
        sb.AppendLine($"| Unknown | {s.Unknown} |");
        sb.AppendLine();
        sb.AppendLine($"**Violations: {report.TotalViolations}** (row violations {s.Violations} + missing {s.Missing} + stale {s.Stale}; must be 0).  ");
        sb.AppendLine($"Warnings: {s.Warnings} (data quality; do not change runtime behaviour).");
        sb.AppendLine();
        sb.AppendLine("Incomplete entries are the honestly-labeled modeled class-spell tail");
        sb.AppendLine("(`MaxDpsOnly`): the companion never generates them independently; they are");
        sb.AppendLine("acted on only when MaxDps itself suggests them.");
        sb.AppendLine();

        if (catalog is not null)
        {
            sb.AppendLine("## Coverage by class and spec");
            sb.AppendLine();
            sb.AppendLine("| Class | Specs | Entries (class total) | Automatic | Interrupt | Defensive | Offensive | Sustain | Mobility | Utility |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var className in AbilityCatalog.ClassOrder)
            {
                if (className.Length == 0) continue;
                if (!AbilityCatalog.SpecOrder.TryGetValue(className, out var specs)) continue;
                int total = 0, auto = 0, intr = 0, def = 0, off = 0, sus = 0, mob = 0, utl = 0;
                int specCount = 0;
                foreach (var spec in specs)
                {
                    if (spec.Length == 0) continue;
                    specCount++;
                    foreach (var ability in catalog.All)
                    {
                        if (!MembersContain(ability.Classes, className) || !MembersContain(ability.Specs, spec)) continue;
                        total++;
                        if (!ability.NeverAutomatic) auto++;
                        if (ability.HasInterruptCapability) intr++;
                        if (ability.HasDefensiveCapability) def++;
                        if (ability.HasOffensiveCapability) off++;
                        if (ability.HasSelfSustainCapability) sus++;
                        if (ability.HasMobilityCapability) mob++;
                        if (ability.Category == AbilityCategory.Utility) utl++;
                    }
                }
                sb.AppendLine($"| {className} | {specCount} | {total} | {auto} | {intr} | {def} | {off} | {sus} | {mob} | {utl} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Violations");
        sb.AppendLine();
        var violations = report.Rows.Where(r => r.HasViolation).ToList();
        if (violations.Count == 0)
        {
            sb.AppendLine("None. Every automatic entry has meaningful intelligence or is explicitly");
            sb.AppendLine("MaxDps-only; every interrupt states its kind; every companion-backed");
            sb.AppendLine("offensive states its usage class; manual entries cannot auto-send.");
        }
        else
        {
            sb.AppendLine("| Spell | Name | Category | Status | Finding |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var row in violations)
                foreach (var finding in row.Findings.Where(f => f.Severity == AuditSeverity.Violation))
                    sb.AppendLine($"| {row.SpellId} | {row.Name} | {row.Category} | {row.Status} | {finding.Check}: {finding.Detail} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Warnings");
        sb.AppendLine();
        var warningRows = report.Rows
            .SelectMany(r => r.Findings.Where(f => f.Severity == AuditSeverity.Warning).Select(f => (Row: r, Finding: f)))
            .ToList();
        if (warningRows.Count == 0)
        {
            sb.AppendLine("None.");
        }
        else
        {
            sb.AppendLine("| Spell | Name | Finding |");
            sb.AppendLine("|---|---|---|");
            foreach (var (row, finding) in warningRows)
                sb.AppendLine($"| {row.SpellId} | {row.Name} | {finding.Check}: {finding.Detail} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Full registry");
        sb.AppendLine();
        sb.AppendLine("| Spell | Name | Category | Status | Automation | MaxDps | Tier | GCD | CD ms | Interrupt | Offensive | Mobility |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var row in report.Rows)
        {
            var ability = catalog?.TryGet(row.SpellId);
            sb.AppendLine(string.Join(" | ",
                $"| {row.SpellId}",
                row.Name,
                row.Category.ToString(),
                row.Status.ToString(),
                row.Automation.ToString(),
                row.MaxDps.ToString(),
                ability?.Tier.ToString() ?? "-",
                ability?.Gcd.ToString() ?? "-",
                ability?.CooldownMs.ToString() ?? "-",
                row.Interrupt ? (ability?.InterruptKind.ToString() ?? "Yes") : "-",
                row.Offensive ? (ability?.OffensiveUsage.ToString() ?? "Yes") : "-",
                row.Mobility ? (ability?.MobilityKind.ToString() ?? "Yes") : "-") + " |");
        }
        return sb.ToString();
    }

    private static bool MembersContain(IReadOnlyList<string> members, string name)
    {
        for (var i = 0; i < members.Count; i++)
            if (string.Equals(members[i], name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
