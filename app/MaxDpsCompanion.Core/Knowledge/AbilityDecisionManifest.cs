using System.Text;

namespace MaxDpsCompanion;

/// <summary>
/// One factual policy contract per registered spell. ALL rows are accounted
/// for, including passive/manual/incomplete, not merely the curated 146.
/// This is an offline audit; no spell data or per-tick runtime work is added.
/// </summary>
internal sealed record AbilityDecisionContract(
    int SpellId, string Name, string ClassMembership, string SpecMembership,
    string Category, string Purpose, string Disposition,
    string RequiredEvidence, string IntelligenceStatus,
    string Ownership, string SourcePatch, string LastValidatedPatch,
    bool CurrentPatchLiveVerified, bool CompanionMayOffer, string Limitation);

internal static class AbilityDecisionManifest
{
    public static IReadOnlyList<AbilityDecisionContract> Build(AbilityCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var rows = new List<AbilityDecisionContract>(catalog.Count);
        foreach (var ability in catalog.All.OrderBy(a => a.SpellId))
        {
            var disposition = DispositionOf(ability);
            var pending = ability.Status is IntelligenceStatus.Incomplete
                or IntelligenceStatus.Unknown or IntelligenceStatus.UnsafeToAutomate;
            var current = ability.LiveVerified
                && string.Equals(ability.LastValidatedPatch,
                    AbilityCatalog.TargetRuntimeGamePatch, StringComparison.Ordinal);
            // Only modeled/curated Companion or Shared statuses can propose
            // extra actions. Incomplete/unobservable always remain delegated
            // or blocked, NEVER silently called fully intelligent.
            var autonomous = ability.Automatable && !pending
                && ability.Ownership is IntelligenceOwnership.Companion
                    or IntelligenceOwnership.Shared;
            var limitation = disposition == "Manual" ? "requires player decision"
                : pending ? "incomplete or unobservable; no independent action"
                : !current ? "no verified live 12.1.5 build evidence"
                : "eligible under observed runtime gates; not a play guarantee";
            rows.Add(new AbilityDecisionContract(
                ability.SpellId, ability.Name,
                string.Join(",", ability.Classes), string.Join(",", ability.Specs),
                ability.Category.ToString(), ability.Purpose.ToString(), disposition,
                EvidenceFor(ability), ability.Status.ToString(),
                ability.Ownership.ToString(), ability.SourcePatch ?? "",
                ability.LastValidatedPatch ?? "", current, autonomous, limitation));
        }
        return rows;
    }

    internal static string DispositionOf(AbilityDefinition a) =>
        a.NeverAutomatic || a.Automation == AutomationContext.Manual
            || a.Ownership == IntelligenceOwnership.Manual
            ? "Manual"
            : a.Status is IntelligenceStatus.Incomplete or IntelligenceStatus.Unknown
                or IntelligenceStatus.UnsafeToAutomate
                ? a.Ownership == IntelligenceOwnership.MaxDps ? "MaxDpsDelegated" : "ResearchPending"
            : a.Ownership switch
            {
                IntelligenceOwnership.MaxDps => "MaxDpsDelegated",
                IntelligenceOwnership.Shared => "SharedSafetyGate",
                IntelligenceOwnership.Companion => "CompanionConditional",
                _ => "Unobservable",
            };

    private static string EvidenceFor(AbilityDefinition a) =>
        DispositionOf(a) switch
        {
            "Manual" => "manual player decision; no autonomous trigger",
            "ResearchPending" => "no safe contextual recommendation until research",
            "Unobservable" => "no safe observable trigger",
            _ => a.Purpose switch
            {
                AbilityPurpose.Interrupt =>
                    "MaxDps trusted kick suggestion; target cast/range veto; readiness",
                AbilityPurpose.DefensiveMinor or AbilityPurpose.DefensiveMajor
                    or AbilityPurpose.Immunity or AbilityPurpose.Absorb =>
                    "live HP/urgency, own-buff and mitigation overlap, timing",
                AbilityPurpose.SelfHeal =>
                    "Solo/emergency, current HP, cure threshold, overheal limit",
                AbilityPurpose.GapCloser =>
                    "target present, outside melee, movement in range and ready",
                AbilityPurpose.CrowdControl =>
                    "observed cast, immunity/DR bounds; manual without evidence",
                AbilityPurpose.MajorOffensive or AbilityPurpose.MinorOffensive =>
                    "MaxDps context and cooldown; optional verified TTK Burst gate",
                AbilityPurpose.Rotational =>
                    "current real MaxDps glow/pick, bound and cast-safe",
                _ => "live source recommendation and shared no-waste gates",
            },
        };

    /// <summary>Deterministic TSV for agents to diff and research, not
    /// appended to a player's live combat/session telemetry.</summary>
    public static void WriteTsv(string path, AbilityCatalog catalog)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        if (parent is not null) Directory.CreateDirectory(parent);
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false));
        writer.WriteLine("spellId\\tname\\tclasses\\tspecs\\tcategory\\tpurpose\\tdisposition\\trequiredEvidence\\tstatus\\towner\\tsourcePatch\\tvalidatedPatch\\tlive12_1_5\\tcompanionMayOffer\\tlimitation".Replace("\\t","\t"));
        foreach (var r in Build(catalog))
        {
            static string Escape(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
            writer.WriteLine(string.Join("\t", new[]
            {
                r.SpellId.ToString(), Escape(r.Name), Escape(r.ClassMembership),
                Escape(r.SpecMembership), r.Category, r.Purpose, r.Disposition,
                r.RequiredEvidence, r.IntelligenceStatus, r.Ownership,
                r.SourcePatch, r.LastValidatedPatch,
                r.CurrentPatchLiveVerified ? "yes" : "no",
                r.CompanionMayOffer ? "yes" : "no", r.Limitation,
            }));
        }
    }
}
