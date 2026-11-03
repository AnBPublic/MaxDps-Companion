using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// MaxDps 11.3.58/12.1.5 core Cooldowns audit. These tests prove source
/// reconciliation, NOT talent correctness or in-game behavior.
/// </summary>
public class Northstar1215UpstreamSyncTests
{
    [Fact]
    public void Current_Upstream_Core_Uses_The_New_Frost_Breath_Of_Sindragosa_Id()
    {
        var catalog = AbilityCatalog.Default;
        Assert.Equal("11.3.58", catalog.MaxDpsVersion);
        var breath = catalog.TryGet(1249658);
        Assert.NotNull(breath);
        Assert.Equal(AbilityCategory.Offensive, breath!.Category);
        Assert.Contains("DEATHKNIGHT", breath.Classes);
        Assert.Contains("Frost", breath.Specs);
        Assert.Null(catalog.TryGet(152279));
        // The upstream table does not establish a live gameplay pass.
        Assert.False(breath.LiveVerified);
    }

    [Fact]
    public void New_Vendor_Source_Does_Not_Falsely_Certify_Old_Curated_Research()
    {
        var catalog = AbilityCatalog.Default;
        Assert.Equal("12.1.5", AbilityCatalog.TargetRuntimeGamePatch);
        Assert.Equal(120105, AbilityCatalog.TargetRuntimeInterfaceVersion);
        Assert.Equal("12.1", catalog.GamePatch);
        Assert.Equal(120100, catalog.InterfaceVersion);
        Assert.Contains(AbilityDecisionManifest.Build(catalog),
            row => row.SpellId == 1249658 && !row.CurrentPatchLiveVerified);
    }

    [Theory]
    [InlineData("11.3.49", 120105)]
    [InlineData("11.3.58", 120100)]
    public void Mismatched_Upstream_Source_Metadata_Fails_Closed(string version, int iface)
    {
        var vendor = $$"""
            {"sourceVersion":"{{version}}","sourceInterface":{{iface}},"entries":[]}
            """;
        const string curated = """{"gamePatch":"12.1","interface":120100,"abilities":[]}""";
        Assert.Throws<InvalidDataException>(() => AbilityCatalog.Load(vendor, curated));
    }
}
