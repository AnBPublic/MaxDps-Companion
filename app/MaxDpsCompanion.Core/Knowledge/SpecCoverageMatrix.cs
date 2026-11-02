namespace MaxDpsCompanion;

/// <summary>
/// v5 audit-only inventory of every protocol-known specialization. A
/// discovered spell is NOT proof it is learned, talented, bound, usable or
/// verified in the live 12.1.5 client. Never used in the hot rotation loop.
/// </summary>
internal sealed record SpecCoverageRow(
    string Class, string Spec, string Role,
    int Registered, int VerifiedLive, int PendingResearch,
    int MaxDpsDelegated, int Manual, int ExtraCandidates,
    int KnownSpellRows, int TalentAnnotations);

internal static class SpecCoverageMatrix
{
    public static IReadOnlyList<SpecCoverageRow> Build(
        AbilityCatalog catalog, ClassSpellBook book)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(book);
        var rows = new List<SpecCoverageRow>(40);
        foreach (var cls in AbilityCatalog.ClassOrder.Skip(1))
        foreach (var spec in AbilityCatalog.SpecOrder[cls].Skip(1))
        {
            var members = ClassSkillTree.Members(catalog, book, cls, spec);
            var registered = 0;
            var verified = 0;
            var pending = 0;
            var delegated = 0;
            var manual = 0;
            var annotated = 0;
            foreach (var id in members)
            {
                var ability = catalog.TryGet(id);
                if (ability is null) continue;
                registered++;
                if (ability.LiveVerified) verified++;
                if (ability.Status is IntelligenceStatus.Incomplete or IntelligenceStatus.Unknown) pending++;
                if (ability.Ownership == IntelligenceOwnership.MaxDps) delegated++;
                if (ability.NeverAutomatic || ability.ManualByDesign) manual++;
                if (!string.IsNullOrWhiteSpace(ability.TalentNote)
                    || !string.IsNullOrWhiteSpace(ability.HeroTalentNote)) annotated++;
            }
            var extras = new HashSet<int>();
            foreach (var category in new[] { AbilityCategory.Mobility,
                AbilityCategory.SelfHeal, AbilityCategory.Offensive })
                extras.UnionWith(catalog.Extras(cls, spec, category));
            rows.Add(new SpecCoverageRow(cls, spec, Role(cls, spec),
                registered, verified, pending, delegated, manual,
                extras.Count,
                book.ForSpec(cls, spec).Count(e => !e.IsJunk),
                annotated));
        }
        return rows;
    }

    public static string Role(string cls, string spec) =>
        CooldownRoles.IsTank(cls, spec) ? "Tank"
            : (cls.ToUpperInvariant(), spec.ToUpperInvariant()) switch
            {
                ("DRUID", "RESTORATION") or
                ("EVOKER", "PRESERVATION") or
                ("MONK", "MISTWEAVER") or
                ("PALADIN", "HOLY") or
                ("PRIEST", "DISCIPLINE") or
                ("PRIEST", "HOLY") or
                ("SHAMAN", "RESTORATION") => "Healer",
                _ => "DPS",
            };
}
