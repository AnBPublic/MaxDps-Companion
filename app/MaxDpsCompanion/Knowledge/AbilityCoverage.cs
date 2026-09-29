using System.Reflection;
using System.Text;
using System.Text.Json;

namespace MaxDpsCompanion;

/// <summary>
/// One ability's coverage disposition in the v2.7 manifest (§6). The manifest
/// exists so the registry can never be mistaken for "3279 intelligent
/// abilities": every row says which kind of coverage it actually has.
/// </summary>
internal enum CoverageDisposition
{
    /// <summary>Discovered, registered, coherent; not automatically usable.</summary>
    Registered = 0,
    /// <summary>Automatable and the companion may generate it independently.</summary>
    CompanionGenerated,
    /// <summary>Automatable via MaxDps's own suggestion; companion only gates safety.</summary>
    MaxDpsDelegated,
    /// <summary>Automatable, MaxDps surfaces it and the companion gates it (defensives).</summary>
    SharedGated,
    /// <summary>Never automatic by design; the user decides.</summary>
    Manual,
    /// <summary>Automation is structurally impossible (unsafe / unobservable).</summary>
    Unobservable,
    /// <summary>Registered but the intelligence is still modeled/incomplete.</summary>
    ResearchPending,
    /// <summary>Patch metadata is older than the supported patch (must not drive runtime).</summary>
    Stale,
    /// <summary>Declared by a discovery source but absent from the registry (audit violation).</summary>
    Missing,
    /// <summary>Deliberately excluded by a documented filter (unverified/junk class-spell ids).</summary>
    Filtered,
}

/// <summary>One manifest row.</summary>
internal sealed record CoverageRow(int SpellId, string Name, CoverageDisposition Disposition, string Detail);

/// <summary>The generated coverage manifest (v2.7 §6).</summary>
internal sealed record CoverageReport(
    int Discovered,
    int Registered,
    int Automatable,
    int CompanionGenerated,
    int MaxDpsDelegated,
    int SharedGated,
    int Manual,
    int Unobservable,
    int ResearchPending,
    int Stale,
    int Missing,
    int Duplicates,
    int LiveVerified,
    int LiveUnverified,
    int Filtered,
    IReadOnlyList<int> MissingIds,
    IReadOnlyList<int> StaleIds,
    IReadOnlyList<string> DuplicateDetails,
    IReadOnlyList<CoverageRow> Rows)
{
    /// <summary>True when no discovered ability is missing and no entry is stale.</summary>
    public bool Clean => Missing == 0 && Stale == 0;
}

/// <summary>
/// Builds the generated coverage manifest (v2.7 §6) from the actual discovery
/// sources: the vendor extraction, the curated layer, the verified class-spell
/// book, the live-client verification and the embedded 12.1 research staging.
/// Labels a row <see cref="CoverageDisposition.Missing"/> whenever a source
/// declared an ability the registry does not carry — the audit fails on it.
/// </summary>
internal static class AbilityCoverage
{
    public const string ResearchResourceName = "MaxDpsCompanion.Knowledge.registry-research.json";

    public static CoverageReport Build(AbilityCatalog catalog, string? researchJson = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        researchJson ??= TryReadEmbedded();
        var researchIds = ParseResearchIds(researchJson);

        var discovered = new SortedSet<int>(catalog.SourceVendorIds);
        discovered.UnionWith(catalog.SourceCuratedIds);
        discovered.UnionWith(catalog.SourceClassSpellIds);
        discovered.UnionWith(researchIds);

        var registered = new HashSet<int>();
        foreach (var ability in catalog.All) registered.Add(ability.SpellId);

        var missingIds = discovered.Where(id => !registered.Contains(id)).OrderBy(id => id).ToArray();
        var staleIds = catalog.All.Where(a => IsStale(a, catalog.GamePatch)).Select(a => a.SpellId).OrderBy(id => id).ToArray();
        var duplicates = FindDuplicateNames(catalog);

        int companionGenerated = 0, maxDpsDelegated = 0, sharedGated = 0, manual = 0,
            unobservable = 0, researchPending = 0, liveVerified = 0, liveUnverified = 0;
        var rows = new List<CoverageRow>(catalog.Count + missingIds.Length);
        foreach (var ability in catalog.All)
        {
            // RESEARCH-PENDING is a status counter independent of the disposition:
            // the delegated class-spell tail is both MaxDps-delegated and pending.
            if (ability.Status is IntelligenceStatus.Incomplete or IntelligenceStatus.Unknown) researchPending++;
            var disposition = Classify(ability);
            switch (disposition)
            {
                case CoverageDisposition.CompanionGenerated: companionGenerated++; break;
                case CoverageDisposition.MaxDpsDelegated: maxDpsDelegated++; break;
                case CoverageDisposition.SharedGated: sharedGated++; break;
                case CoverageDisposition.Manual: manual++; break;
                case CoverageDisposition.Unobservable: unobservable++; break;
                case CoverageDisposition.ResearchPending: researchPending++; break;
            }
            if (ability.LiveVerified) liveVerified++;
            else if (ability.Automatable && ability.Ownership != IntelligenceOwnership.MaxDps) liveUnverified++;
            if (IsStale(ability, catalog.GamePatch))
                rows.Add(new CoverageRow(ability.SpellId, ability.Name, CoverageDisposition.Stale,
                    $"knowledge patch {ability.SourcePatch ?? "?"} / last validated {ability.LastValidatedPatch ?? "?"}; supported {catalog.GamePatch}"));
            else
                rows.Add(new CoverageRow(ability.SpellId, ability.Name, disposition, Describe(ability)));
        }
        foreach (var id in missingIds)
            rows.Add(new CoverageRow(id, "(discovered, not registered)", CoverageDisposition.Missing, "declared by a discovery source"));

        var automatable = 0;
        foreach (var ability in catalog.All)
            if (ability.Automatable) automatable++;

        return new CoverageReport(
            discovered.Count, catalog.Count, automatable, companionGenerated, maxDpsDelegated,
            sharedGated, manual, unobservable, researchPending, staleIds.Length, missingIds.Length,
            duplicates.Count, liveVerified, liveUnverified, catalog.SourceClassSpellFilteredIds.Count,
            missingIds, staleIds, duplicates, rows);
    }

    private static CoverageDisposition Classify(AbilityDefinition ability)
    {
        if (ability.Automation == AutomationContext.Manual || ability.NeverAutomatic)
            return CoverageDisposition.Manual;
        if (ability.Ownership == IntelligenceOwnership.MaxDps)
            return CoverageDisposition.MaxDpsDelegated;
        if (ability.Ownership == IntelligenceOwnership.Unavailable
            || ability.Completeness == IntelligenceCompleteness.Unobservable)
            return CoverageDisposition.Unobservable;
        if (ability.Ownership == IntelligenceOwnership.Shared)
            return CoverageDisposition.SharedGated;
        if (ability.Automation == AutomationContext.Autonomous)
            return ability.Status is IntelligenceStatus.Incomplete or IntelligenceStatus.Unknown
                ? CoverageDisposition.ResearchPending
                : CoverageDisposition.CompanionGenerated;
        return CoverageDisposition.Registered;
    }

    private static string Describe(AbilityDefinition a) => a.Ownership switch
    {
        IntelligenceOwnership.Manual => $"manual: {a.ManualReason}",
        IntelligenceOwnership.MaxDps => $"delegated: {a.Delegation}",
        IntelligenceOwnership.Shared => $"shared gating ({a.Status})",
        _ => $"{a.Status} / {a.Completeness}",
    };

    /// <summary>
    /// Same-name different-id entries are legitimate talent/spec variants, so
    /// this is a warning list, not a violation: it exists so an accidental
    /// duplicate can be seen and linked via relations instead of merged by name.
    /// </summary>
    private static List<string> FindDuplicateNames(AbilityCatalog catalog)
    {
        var byName = new Dictionary<string, List<AbilityDefinition>>(StringComparer.OrdinalIgnoreCase);
        foreach (var ability in catalog.All)
        {
            if (string.IsNullOrWhiteSpace(ability.Name)) continue;
            if (!byName.TryGetValue(ability.Name.Trim(), out var list))
            {
                list = [];
                byName[ability.Name.Trim()] = list;
            }
            list.Add(ability);
        }
        var details = new List<string>();
        foreach (var (name, list) in byName)
        {
            if (list.Count < 2) continue;
            // A variant is "linked" when at least one side records a relation to another id in the group.
            var ids = list.Select(a => a.SpellId).ToHashSet();
            var unlinked = list.Where(a => !a.Relations.Any(r => ids.Contains(r.SpellId) && r.SpellId != a.SpellId)).ToList();
            if (unlinked.Count == list.Count)
                details.Add($"{name}: ids {string.Join(", ", list.Select(a => a.SpellId).OrderBy(id => id))} share a display name with no linking relation");
        }
        details.Sort(StringComparer.OrdinalIgnoreCase);
        return details;
    }

    /// <summary>Stale = both the knowledge patch and the last-validated patch are older than the supported patch.</summary>
    internal static bool IsStale(AbilityDefinition ability, string supportedPatch)
    {
        if (ParsePatch(supportedPatch) is not { } supported) return false;
        var source = ParsePatch(ability.SourcePatch);
        var validated = ParsePatch(ability.LastValidatedPatch);
        if (source is null && validated is null) return false;
        var sourceOld = source is { } s && Compare(s, supported) < 0;
        var validatedOld = validated is { } v && Compare(v, supported) < 0;
        return sourceOld && validatedOld;
    }

    /// <summary>True when the entry's knowledge claims a patch NEWER than the supported one (must not silently drive an older build).</summary>
    internal static bool IsNewer(AbilityDefinition ability, string supportedPatch)
    {
        if (ParsePatch(supportedPatch) is not { } supported) return false;
        if (ParsePatch(ability.SourcePatch) is { } source && Compare(source, supported) > 0) return true;
        return ParsePatch(ability.LastValidatedPatch) is { } validated && Compare(validated, supported) > 0;
    }

    private static (int Major, int Minor, int Patch)? ParsePatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Trim().Split('.');
        if (parts.Length is < 1 or > 3) return null;
        var nums = new int[3];
        for (var i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], out nums[i]) || nums[i] < 0) return null;
        return (nums[0], nums[1], nums[2]);
    }

    private static int Compare((int Major, int Minor, int Patch) a, (int Major, int Minor, int Patch) b)
    {
        var major = a.Major.CompareTo(b.Major);
        if (major != 0) return major;
        var minor = a.Minor.CompareTo(b.Minor);
        if (minor != 0) return minor;
        return a.Patch.CompareTo(b.Patch);
    }

    /// <summary>Spell ids declared by the embedded research staging file (live abilities only; corrections may reference history).</summary>
    public static IReadOnlyCollection<int> ParseResearchIds(string? researchJson)
    {
        var ids = new SortedSet<int>();
        if (string.IsNullOrWhiteSpace(researchJson)) return ids;
        try
        {
            using var doc = JsonDocument.Parse(researchJson);
            var root = doc.RootElement;
            foreach (var section in new[] { "interrupts", "mobility", "offensives", "utility" })
            {
                if (!root.TryGetProperty(section, out var array) || array.ValueKind != JsonValueKind.Array) continue;
                foreach (var record in array.EnumerateArray())
                    if (record.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var id) && id > 0)
                        ids.Add(id);
            }
        }
        catch (JsonException)
        {
            // A malformed staging file must not crash the audit; the audit
            // reports the parse failure through the Warnings channel instead.
        }
        return ids;
    }

    private static string? TryReadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResearchResourceName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Machine-readable manifest for CLI/CI (stable key order).</summary>
    public static string ToJson(CoverageReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var sb = new StringBuilder(4096);
        sb.Append('{').AppendLine();
        sb.Append("  \"discovered\": ").Append(report.Discovered).AppendLine(",");
        sb.Append("  \"registered\": ").Append(report.Registered).AppendLine(",");
        sb.Append("  \"automatable\": ").Append(report.Automatable).AppendLine(",");
        sb.Append("  \"companionGenerated\": ").Append(report.CompanionGenerated).AppendLine(",");
        sb.Append("  \"maxDpsDelegated\": ").Append(report.MaxDpsDelegated).AppendLine(",");
        sb.Append("  \"sharedGated\": ").Append(report.SharedGated).AppendLine(",");
        sb.Append("  \"manual\": ").Append(report.Manual).AppendLine(",");
        sb.Append("  \"unobservable\": ").Append(report.Unobservable).AppendLine(",");
        sb.Append("  \"researchPending\": ").Append(report.ResearchPending).AppendLine(",");
        sb.Append("  \"stale\": ").Append(report.Stale).AppendLine(",");
        sb.Append("  \"missing\": ").Append(report.Missing).AppendLine(",");
        sb.Append("  \"duplicates\": ").Append(report.Duplicates).AppendLine(",");
        sb.Append("  \"liveVerified\": ").Append(report.LiveVerified).AppendLine(",");
        sb.Append("  \"liveUnverified\": ").Append(report.LiveUnverified).AppendLine(",");
        sb.Append("  \"filtered\": ").Append(report.Filtered).AppendLine(",");
        sb.Append("  \"clean\": ").Append(report.Clean ? "true" : "false").AppendLine(",");
        sb.Append("  \"missingIds\": [").Append(string.Join(",", report.MissingIds)).AppendLine("],");
        sb.Append("  \"staleIds\": [").Append(string.Join(",", report.StaleIds)).AppendLine("],");
        sb.Append("  \"duplicateDetails\": [").Append(string.Join(",", report.DuplicateDetails.Select(d => "\"" + d.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""))).AppendLine("],");
        sb.Append("  \"rows\": [").AppendLine();
        for (var i = 0; i < report.Rows.Count; i++)
        {
            var row = report.Rows[i];
            sb.Append("    {\"id\": ").Append(row.SpellId)
                .Append(", \"name\": \"").Append(row.Name.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"')
                .Append(", \"disposition\": \"").Append(row.Disposition).Append('"')
                .Append(", \"detail\": \"").Append(row.Detail.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"')
                .Append('}').Append(i == report.Rows.Count - 1 ? "" : ",").AppendLine();
        }
        sb.Append("  ]").AppendLine();
        sb.Append('}');
        return sb.ToString();
    }
}
