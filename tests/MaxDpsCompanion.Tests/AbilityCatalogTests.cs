using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Validates the two-layer ability knowledge base (vendor extraction +
/// curated policy) and the generated bridge catalog. These tests fail loudly
/// on any data error: a bad rule here would reach the live policy engine.
/// </summary>
public class AbilityCatalogTests
{
    private static AbilityCatalog Catalog => AbilityCatalog.Default;

    [Fact]
    public void Default_Catalog_Loads_Every_Class_And_Spec()
    {
        var catalog = Catalog;
        Assert.True(catalog.Count >= 240, $"catalog unexpectedly small: {catalog.Count}");
        foreach (var className in AbilityCatalog.ClassOrder.Skip(1))
        {
            Assert.True(AbilityCatalog.SpecOrder.ContainsKey(className));
            var specs = AbilityCatalog.SpecOrder[className];
            Assert.True(specs.Length >= 3, $"{className} has {specs.Length - 1} specs");
        }
    }

    [Fact]
    public void Vendor_Defensives_Materialize_As_Defensive_Defaults()
    {
        var catalog = Catalog;
        var shieldWall = catalog.TryGet(871);
        Assert.NotNull(shieldWall);
        Assert.Equal(AbilityPurpose.DefensiveMajor, shieldWall!.Purpose);
        Assert.Equal(DefensiveTier.Major, shieldWall.Tier);
        Assert.Equal("WARRIOR", Assert.Single(shieldWall.Classes));
        Assert.Contains("Protection", shieldWall.Specs);
    }

    [Fact]
    public void Curated_Overrides_Apply_On_Top_Of_Vendor_Rows()
    {
        var catalog = Catalog;
        var reflect = catalog.TryGet(23920); // Spell Reflection
        Assert.NotNull(reflect);
        Assert.Equal(AbilityPurpose.Reflect, reflect!.Purpose);
        Assert.True(reflect.RequiresEnemyCast);
        Assert.Equal(UnknownPolicy.Hold, reflect.Unknown);
        Assert.Equal(GcdKind.OffGcd, reflect.Gcd);

        var shadowstep = catalog.TryGet(36554);
        Assert.NotNull(shadowstep);
        Assert.Equal(AbilityPurpose.GapCloser, shadowstep!.Purpose);
    }

    [Fact]
    public void Extras_Are_Curated_And_Catalogued_For_Every_Spec()
    {
        var catalog = Catalog;
        var warriorMobility = catalog.Extras("WARRIOR", "Fury", AbilityCategory.Mobility);
        Assert.Contains(100, warriorMobility);   // Charge
        Assert.Contains(6544, warriorMobility);  // Heroic Leap
        Assert.Contains(202168, catalog.Extras("WARRIOR", "Arms", AbilityCategory.SelfHeal));

        foreach (var className in AbilityCatalog.ClassOrder.Skip(1))
        {
            foreach (var spec in AbilityCatalog.SpecOrder[className].Skip(1))
            {
                foreach (var id in catalog.Extras(className, spec, AbilityCategory.Mobility))
                    Assert.NotNull(catalog.TryGet(id));
                foreach (var id in catalog.Extras(className, spec, AbilityCategory.SelfHeal))
                    Assert.NotNull(catalog.TryGet(id));
            }
        }
    }

    [Fact]
    public void Wire_Ids_Round_Trip()
    {
        foreach (var className in AbilityCatalog.ClassOrder.Skip(1))
        {
            var classId = AbilityCatalog.ClassId(className);
            Assert.True(classId > 0);
            Assert.Equal(className, AbilityCatalog.ClassName(classId));
            foreach (var (spec, index) in AbilityCatalog.SpecOrder[className].Skip(1).Select((s, i) => (s, i + 1)))
            {
                Assert.Equal(index, AbilityCatalog.SpecId(className, spec));
                Assert.Equal(spec, AbilityCatalog.SpecName(className, index));
            }
        }
        Assert.Equal(0, AbilityCatalog.ClassId("NOTACLASS"));
    }

    [Fact]
    public void Every_Definition_Is_Internally_Consistent()
    {
        foreach (var ability in Catalog.All)
        {
            Assert.True(ability.SpellId > 0);
            Assert.False(string.IsNullOrWhiteSpace(ability.Name), $"spell {ability.SpellId} has no name");
            if (ability.Tier is DefensiveTier.Major or DefensiveTier.Immunity)
                Assert.True(ability.IsSurvival || ability.Purpose == AbilityPurpose.External,
                    $"{ability.Name} carries {ability.Tier} but is not a survival ability");
            if (ability.Purpose == AbilityPurpose.GapCloser)
                Assert.Equal(RangeRequirement.OutOfMelee, ability.TargetRange);
            if (ability.UseBelowHpPct is { } below)
                Assert.InRange(below, 1, 99);
        }
    }

    [Fact]
    public void Gcd_Verification_Is_Conservative_For_Cast_Protection()
    {
        var catalog = Catalog;
        // Curated GCD data is verified.
        Assert.True(catalog.TryGet(871)!.GcdVerified);      // Shield Wall, curated off-GCD
        Assert.True(catalog.TryGet(2565)!.GcdVerified);     // Shield Block, curated on-GCD
        Assert.True(catalog.TryGet(23920)!.GcdVerified);    // Spell Reflection, curated off-GCD
        // Interrupts and item activations are off-GCD by game design.
        Assert.True(catalog.TryGet(6552)!.GcdVerified);
        // An uncurated defensive's off-GCD default is a guess and must stay
        // unverified, so the cast/channel hold treats it conservatively.
        var enragedRegen = catalog.TryGet(184364);
        Assert.NotNull(enragedRegen);
        Assert.Equal(GcdKind.OffGcd, enragedRegen!.Gcd);
        Assert.False(enragedRegen.GcdVerified);
    }

    [Fact]
    public void Generated_Lua_Matches_Committed_Catalog_File()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "Catalog.lua");
        Assert.True(File.Exists(path), $"missing {path}; regenerate with --gen-catalog");
        static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd();
        Assert.Equal(
            Normalize(CatalogLuaGenerator.Generate(Catalog)),
            Normalize(File.ReadAllText(path)));
    }
}
