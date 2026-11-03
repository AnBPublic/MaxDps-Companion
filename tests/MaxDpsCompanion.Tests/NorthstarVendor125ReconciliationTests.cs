using System.Text.Json;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Source reconciliation is not spell/talent behavior certification. These
/// checks pin the *upstream* 11.3.58 inventory independently from the older
/// 11.3.49/12.1 curated policy and from any live-retail validation.
/// </summary>
public class NorthstarVendor125ReconciliationTests
{
    private static JsonDocument Resource(string name)
    {
        using var stream = typeof(AbilityCatalog).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidDataException($"missing embedded resource: {name}");
        return JsonDocument.Parse(stream);
    }

    [Fact]
    public void Extracted_Cooldowns_Are_Tagged_For_The_Actual_12_1_5_Upstream()
    {
        using var document = Resource(AbilityCatalog.VendorResourceName);
        var root = document.RootElement;
        Assert.Equal(AbilityCatalog.ExpectedExtractedVendorVersion,
            root.GetProperty("maxDpsVersion").GetString());
        Assert.Equal(AbilityCatalog.ExpectedExtractedVendorPatch,
            root.GetProperty("gamePatch").GetString());
        Assert.Equal(AbilityCatalog.ExpectedExtractedVendorInterface,
            root.GetProperty("interface").GetInt32());

        var entries = root.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(368, entries.Length);
        var breath = Assert.Single(entries.Where(e =>
            e.GetProperty("class").GetString() == "DEATHKNIGHT"
            && e.GetProperty("spec").GetString() == "Frost"
            && e.GetProperty("category").GetString() == "Offensive"
            && e.GetProperty("name").GetString() == "Breath of Sindragosa"));
        Assert.Equal(1249658, breath.GetProperty("id").GetInt32());
        Assert.DoesNotContain(entries, e => e.GetProperty("id").GetInt32() == 152279);
    }

    [Fact]
    public void Unchanged_Retail_SpellData_Remains_Pinned_Without_Fake_Talent_Refresh()
    {
        using var document = Resource(AbilityCatalog.ClassSpellsResourceName);
        var entries = document.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(8070, entries.Length);
        Assert.Contains(entries, e => e.GetProperty("class").GetString() == "DEATHKNIGHT"
            && e.GetProperty("spec").GetString() == "Frost"
            && e.GetProperty("token").GetString() == "BreathofSindragosa"
            && e.GetProperty("id").GetInt32() == 1249658);
    }

    [Fact]
    public void Frost_Breath_Is_Delegated_And_Not_Falsely_Live_Certified()
    {
        var catalog = AbilityCatalog.Default;
        var breath = catalog.TryGet(1249658);
        Assert.NotNull(breath);
        Assert.Equal(AbilityCategory.Offensive, breath!.Category);
        Assert.Equal(IntelligenceOwnership.MaxDps, breath.Ownership);
        Assert.Contains("DEATHKNIGHT", breath.Classes);
        Assert.Contains("Frost", breath.Specs);
        Assert.False(breath.LiveVerified);
        Assert.False(catalog.IsOffensiveGapFill("DEATHKNIGHT", "Frost", 1249658));
        Assert.Null(catalog.TryGet(152279));

        // The curated policy was NOT silently restamped to 12.1.5.
        Assert.Equal("12.1", catalog.GamePatch);
        Assert.Equal("11.3.49", catalog.MaxDpsVersion);
        Assert.False(AbilityDecisionManifest.Build(catalog)
            .Single(r => r.SpellId == 1249658).CurrentPatchLiveVerified);
    }

    [Theory]
    [InlineData("11.3.49", "12.1.5", 120105)]
    [InlineData("11.3.58", "12.1", 120105)]
    [InlineData("11.3.58", "12.1.5", 120100)]
    public void Incorrect_Stamped_Upstream_Cannot_Enter_The_Registry(
        string version, string patch, int interfaceVersion)
    {
        var vendor = JsonSerializer.Serialize(new
        {
            maxDpsVersion = version,
            gamePatch = patch,
            @interface = interfaceVersion,
            entries = Array.Empty<object>()
        });
        const string curated = """{"gamePatch":"12.1","interface":120100,"abilities":[]}""";
        Assert.Throws<InvalidDataException>(() => AbilityCatalog.Load(vendor, curated));
    }
}
