using System.Text.Json;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// v3.0.0 Workstream C1: curated self-sustain extras and the spell-alias map.
/// Pins that every spec except the documented nones carries at least one
/// self-heal candidate, that every candidate id is verified against the
/// live-client DB2 export (name match when present), and that the two
/// corrections from the research pass hold (774 = Rejuvenation, not Regrowth;
/// Regrowth = 8936; Victory Rush = 34428).
/// </summary>
public class KnowledgeExtrasTests
{
    private static AbilityCatalog Catalog => AbilityCatalog.Default;

    /// <summary>Specs that deliberately have no self-heal candidate (documented).</summary>
    /// v3.3.0: mages sustain via spec barriers (Arcane 235450 / Fire 235313 /
    /// Frost 11426), so only Demon Hunter Havoc/Devourer remain documented
    /// nones (no verified solo self-heal id exists for them yet).
    private static readonly HashSet<(string Class, string Spec)> DocumentedNones =
    [
        ("DEMONHUNTER", "Havoc"), ("DEMONHUNTER", "Devourer"),
    ];

    [Fact]
    public void Every_Spec_Except_Documented_Nones_Has_A_SelfHeal_Candidate()
    {
        foreach (var className in AbilityCatalog.ClassOrder.Skip(1))
        {
            foreach (var spec in AbilityCatalog.SpecOrder[className].Skip(1))
            {
                var heals = Catalog.Extras(className, spec, AbilityCategory.SelfHeal);
                if (DocumentedNones.Contains((className, spec)))
                {
                    Assert.Empty(heals);
                    continue;
                }
                Assert.True(heals.Length >= 1, $"{className}/{spec} has no self-heal extra");
            }
        }
    }

    [Fact]
    public void Every_SelfHeal_Extra_Is_Catalogued_And_Autonomous()
    {
        foreach (var className in AbilityCatalog.ClassOrder.Skip(1))
        {
            foreach (var spec in AbilityCatalog.SpecOrder[className].Skip(1))
            {
                foreach (var id in Catalog.Extras(className, spec, AbilityCategory.SelfHeal))
                {
                    var ability = Catalog.TryGet(id);
                    Assert.NotNull(ability);
                    Assert.Equal(AutomationContext.Autonomous, ability!.Automation);
                }
            }
        }
    }

    [Fact]
    public void Every_Extra_Id_Present_In_The_Live_Client_Export_Matches_Its_Name()
    {
        var verification = LoadVerificationNames();
        foreach (var className in AbilityCatalog.ClassOrder.Skip(1))
        {
            foreach (var spec in AbilityCatalog.SpecOrder[className].Skip(1))
            {
                foreach (var id in Catalog.Extras(className, spec, AbilityCategory.Mobility)
                             .Concat(Catalog.Extras(className, spec, AbilityCategory.SelfHeal)))
                {
                    if (!verification.TryGetValue(id, out var official)) continue;
                    var ability = Catalog.TryGet(id)!;
                    Assert.Equal(official, ability.Name);
                }
            }
        }
    }

    [Fact]
    public void Regrowth_Is_8936_And_774_Is_Rejuvenation()
    {
        var regrowth = Catalog.TryGet(8936);
        Assert.NotNull(regrowth);
        Assert.Equal("Regrowth", regrowth!.Name);

        var rejuvenation = Catalog.TryGet(774);
        if (rejuvenation is not null) Assert.Equal("Rejuvenation", rejuvenation.Name);

        // 774 must never be the druid self-sustain candidate.
        foreach (var spec in AbilityCatalog.SpecOrder["DRUID"].Skip(1))
        {
            var heals = Catalog.Extras("DRUID", spec, AbilityCategory.SelfHeal);
            Assert.Contains(8936, heals);
            Assert.DoesNotContain(774, heals);
        }
    }

    [Fact]
    public void Victory_Rush_Is_A_Curated_Self_Heal()
    {
        var victoryRush = Catalog.TryGet(34428);
        Assert.NotNull(victoryRush);
        Assert.Equal("Victory Rush", victoryRush!.Name);
        Assert.Equal(AbilityCategory.SelfHeal, victoryRush.Category);
        Assert.Equal(AbilityPurpose.SelfHeal, victoryRush.Purpose);
        Assert.Equal(10, victoryRush.HealPctMaxHp);
        Assert.Equal(0, victoryRush.CooldownMs);
        Assert.True(victoryRush.RequiresTarget);
        Assert.Equal(RangeKind.Melee, victoryRush.Range);
        Assert.False(string.IsNullOrWhiteSpace(victoryRush.SourceUrl));
    }

    [Fact]
    public void Spell_Aliases_Are_Curated_And_Emitted()
    {
        Assert.True(Catalog.Aliases.TryGetValue(202168, out var impending));
        Assert.Contains(34428, impending!);
        Assert.True(Catalog.Aliases.TryGetValue(34428, out var victoryRush));
        Assert.Contains(202168, victoryRush!);
        Assert.True(Catalog.Aliases.TryGetValue(19647, out var oldSpellLock));
        Assert.Contains(119910, oldSpellLock!);

        var lua = CatalogLuaGenerator.Generate(Catalog);
        Assert.Contains("MDB.SpellAliases", lua);
        Assert.Contains("[202168] = { 34428 }", lua);
    }

    private static Dictionary<int, string> LoadVerificationNames()
    {
        using var stream = typeof(ClassSpellBook).Assembly
            .GetManifestResourceStream(ClassSpellBook.VerificationResourceName)!;
        using var doc = JsonDocument.Parse(stream);
        var result = new Dictionary<int, string>();
        if (!doc.RootElement.TryGetProperty("entries", out var entries)) return result;
        foreach (var row in entries.EnumerateArray())
        {
            if (!row.TryGetProperty("id", out var idProp)) continue;
            var id = idProp.GetInt32();
            var name = row.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
            if (id > 0 && name.Length > 0) result[id] = name;
        }
        return result;
    }
}
