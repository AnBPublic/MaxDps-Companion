namespace MaxDpsCompanion;

/// <summary>Explicit inventory vs verification, across all 40 specializations.</summary>
internal enum CombatRole { Damage, Tank, Healer }

internal sealed record SpecReadinessRow(
    string ClassName, string Spec, CombatRole Role,
    int Registered, int Delegated, int CompanionEligible,
    int Manual, int Unresolved, int OlderPatchMetadata)
{
    public bool HasInventory => Registered > 0;
    public bool LiveFullyVerified => false; // no exhaustive 12.1.5 field evidence
}

internal static class SpecReadiness
{
    public static CombatRole RoleOf(string cls, string spec)
    {
        if (CooldownRoles.IsTank(cls, spec)) return CombatRole.Tank;
        return (cls.ToUpperInvariant(), spec.ToUpperInvariant()) switch
        {
            ("DRUID", "RESTORATION") or ("EVOKER", "PRESERVATION")
                or ("MONK", "MISTWEAVER") or ("PALADIN", "HOLY")
                or ("PRIEST", "DISCIPLINE") or ("PRIEST", "HOLY")
                or ("SHAMAN", "RESTORATION") => CombatRole.Healer,
            _ => CombatRole.Damage,
        };
    }

    public static IReadOnlyList<SpecReadinessRow> Build(AbilityCatalog catalog, ClassSpellBook spellBook)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(spellBook);
        var rows = new List<SpecReadinessRow>(40);
        foreach (var className in AbilityCatalog.ClassOrder.Skip(1))
        foreach (var spec in AbilityCatalog.SpecOrder[className].Skip(1))
        {
            var ids = ClassSkillTree.Members(catalog, spellBook, className, spec);
            var delegated = 0; var eligible = 0; var manual = 0;
            var unresolved = 0; var older = 0;
            foreach (var id in ids)
            {
                var a = catalog.TryGet(id);
                if (a is null) continue;
                if (a.Ownership == IntelligenceOwnership.MaxDps) delegated++;
                if (a.Automatable && (a.Ownership is IntelligenceOwnership.Companion or IntelligenceOwnership.Shared))
                    eligible++;
                if (a.NeverAutomatic || a.Automation == AutomationContext.Manual) manual++;
                if (a.Status is IntelligenceStatus.Incomplete or IntelligenceStatus.Unknown) unresolved++;
                if (AbilityCoverage.IsStale(a, AbilityCatalog.TargetRuntimeGamePatch)) older++;
            }
            rows.Add(new SpecReadinessRow(className, spec, RoleOf(className, spec),
                ids.Count, delegated, eligible, manual, unresolved, older));
        }
        return rows;
    }
}
