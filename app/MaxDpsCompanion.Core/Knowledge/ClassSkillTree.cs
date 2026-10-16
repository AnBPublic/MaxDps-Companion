namespace MaxDpsCompanion;

/// <summary>Which section of the class-skill screen an ability belongs to.</summary>
internal enum ClassSkillGroup
{
    Main = 0,
    Offensive,
    Defensive,
    Movement,
}

/// <summary>
/// The class-skill screen's membership model. Two sources feed it:
///  * the ability catalog (vendor Cooldowns + curated overrides + merged class
///    spells), which knows the class/spec membership of everything it carries;
///  * the curated per-spec extra lists (Mobility / SelfHeal / Defensive),
///    which name the companion-only slot candidates.
/// The tree merges them, groups by purpose and splits "shared by every spec"
/// (shown once for the class) from per-spec entries, so nothing is duplicated
/// on screen. Pure functions — no game state, no UI.
/// </summary>
internal static class ClassSkillTree
{
    /// <summary>The screen section for an ability (purpose-driven, data not UI).</summary>
    public static ClassSkillGroup GroupOf(AbilityDefinition definition) => definition.Purpose switch
    {
        AbilityPurpose.MajorOffensive or AbilityPurpose.MinorOffensive or AbilityPurpose.Interrupt => ClassSkillGroup.Offensive,
        AbilityPurpose.DefensiveMajor or AbilityPurpose.DefensiveMinor or AbilityPurpose.Immunity
            or AbilityPurpose.Absorb or AbilityPurpose.Reflect or AbilityPurpose.SelfHeal
            or AbilityPurpose.External => ClassSkillGroup.Defensive,
        AbilityPurpose.GapCloser or AbilityPurpose.Movement or AbilityPurpose.Escape => ClassSkillGroup.Movement,
        _ => ClassSkillGroup.Main,
    };

    public static readonly ClassSkillGroup[] SectionOrder =
        [ClassSkillGroup.Main, ClassSkillGroup.Offensive, ClassSkillGroup.Defensive, ClassSkillGroup.Movement];

    public static string SectionTitle(ClassSkillGroup group) => group switch
    {
        ClassSkillGroup.Main => "Main rotation",
        ClassSkillGroup.Offensive => "Offensive",
        ClassSkillGroup.Defensive => "Defensive",
        ClassSkillGroup.Movement => "Movement",
        _ => group.ToString(),
    };

    /// <summary>Every catalogued ability that belongs to a class+spec, through any layer.</summary>
    public static HashSet<int> Members(AbilityCatalog catalog, ClassSpellBook book, string? className, string? specName)
    {
        var members = new HashSet<int>();
        if (className is null || specName is null) return members;

        foreach (var definition in catalog.All)
        {
            if (!Contains(definition.Classes, className) || !Contains(definition.Specs, specName)) continue;
            members.Add(definition.SpellId);
        }
        foreach (var category in new[] { AbilityCategory.Mobility, AbilityCategory.SelfHeal, AbilityCategory.Defensive })
        {
            foreach (var id in catalog.Extras(className, specName, category)) members.Add(id);
        }
        foreach (var entry in book.ForSpec(className, specName))
        {
            if (!entry.IsJunk && catalog.TryGet(entry.SpellId) is not null) members.Add(entry.SpellId);
        }
        return members;
    }

    /// <summary>Ability ids present for EVERY spec of the class (the "shared" bucket).</summary>
    public static HashSet<int> SharedIds(AbilityCatalog catalog, ClassSpellBook book, string? className)
    {
        var shared = new HashSet<int>();
        if (className is null) return shared;
        var specs = SpecList(className);
        if (specs.Count == 0) return shared;

        HashSet<int>? intersection = null;
        foreach (var spec in specs)
        {
            var members = Members(catalog, book, className, spec);
            if (intersection is null) intersection = members;
            else intersection.IntersectWith(members);
        }
        if (intersection is not null) shared.UnionWith(intersection);
        return shared;
    }

    /// <summary>
    /// The screen's data for one class+spec: the class-shared ids (rendered in
    /// the Shared section) and the per-spec ids grouped by section, both
    /// ordered by display name then id.
    /// </summary>
    public static SpecSkillList Build(AbilityCatalog catalog, ClassSpellBook book, string? className, string? specName)
    {
        var shared = SharedIds(catalog, book, className);
        var members = Members(catalog, book, className, specName);
        var groups = new Dictionary<ClassSkillGroup, List<int>>();
        var sharedDefinitions = new List<AbilityDefinition>();
        var groupDefinitions = new Dictionary<ClassSkillGroup, List<AbilityDefinition>>();

        foreach (var id in members)
        {
            if (catalog.TryGet(id) is not { } definition) continue;
            if (shared.Contains(id))
            {
                sharedDefinitions.Add(definition);
                continue;
            }
            var group = GroupOf(definition);
            if (!groupDefinitions.TryGetValue(group, out var list))
            {
                list = [];
                groupDefinitions[group] = list;
            }
            list.Add(definition);
        }

        Sort(sharedDefinitions);
        foreach (var list in groupDefinitions.Values) Sort(list);

        return new SpecSkillList(sharedDefinitions, groupDefinitions);
    }

    /// <summary>The spec names to consider "all specs of the class" (wire order).</summary>
    public static IReadOnlyList<string> SpecList(string className) =>
        AbilityCatalog.SpecOrder.TryGetValue(className, out var specs)
            ? specs.Where(s => s.Length > 0).ToArray()
            : [];

    private static void Sort(List<AbilityDefinition> definitions) =>
        definitions.Sort((a, b) =>
        {
            // Proven abilities first (curated / vendor Cooldowns), then the
            // modeled class-list entries — the screen opens on real abilities
            // and the long modeled tail sinks below them.
            var rank = ProvenanceRank(a.Provenance).CompareTo(ProvenanceRank(b.Provenance));
            if (rank != 0) return rank;
            var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : a.SpellId.CompareTo(b.SpellId);
        });

    private static int ProvenanceRank(AbilityProvenance provenance) =>
        provenance == AbilityProvenance.ClassSpell ? 1 : 0;

    private static bool Contains(IReadOnlyList<string> members, string name)
    {
        for (var i = 0; i < members.Count; i++)
        {
            if (string.Equals(members[i], name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}

/// <summary>One class+spec's screen data: shared rows plus per-section rows.</summary>
internal sealed record SpecSkillList(
    IReadOnlyList<AbilityDefinition> Shared,
    IReadOnlyDictionary<ClassSkillGroup, List<AbilityDefinition>> Groups);
