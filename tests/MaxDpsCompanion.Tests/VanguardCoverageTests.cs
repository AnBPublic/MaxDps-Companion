using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>Version 5 cross-spec static inventory and honest patch provenance.</summary>
public class VanguardCoverageTests
{
    [Fact]
    public void All_40_Specs_Are_Represented_And_Have_Registered_Spells()
    {
        var rows = SpecCoverageMatrix.Build(AbilityCatalog.Default, ClassSpellBook.Default);
        Assert.Equal(40, rows.Count);
        Assert.Equal(13, rows.Select(r => r.Class).Distinct().Count());
        Assert.Equal(6, rows.Count(r => r.Role == "Tank"));
        Assert.Equal(7, rows.Count(r => r.Role == "Healer"));
        Assert.Equal(27, rows.Count(r => r.Role == "DPS"));
        Assert.All(rows, row =>
        {
            Assert.True(row.Registered > 0, $"missing spells: {row.Class}/{row.Spec}");
            // The old pinned MaxDps 11.3.49 vendor has NO Devourer
            // class-spell entries. Record and enforce the gap rather than
            // fabricate spells to make the gate pass. Everything else must
            // have a real non-junk class-spell inventory.
            if (row.Class == "DEMONHUNTER" && row.Spec == "Devourer")
                Assert.Equal(0, row.KnownSpellRows);
            else
                Assert.True(row.KnownSpellRows > 0, $"missing spellbook: {row.Class}/{row.Spec}");
            Assert.InRange(row.VerifiedLive, 0, row.Registered);
            Assert.InRange(row.MaxDpsDelegated, 0, row.Registered);
        });
        Assert.Contains(rows, row => row.Class == "DEMONHUNTER" && row.Spec == "Devourer");
    }

    [Fact]
    public void Retail_Client_Patch_Does_Not_Falsify_Old_Vendor_Metadata()
    {
        Assert.Equal("12.1.5", AbilityCatalog.TargetRetailPatch);
        Assert.Equal(120105, AbilityCatalog.TargetRetailInterface);
        Assert.Equal("12.1", AbilityCatalog.Default.GamePatch);
        Assert.Equal(120100, AbilityCatalog.Default.InterfaceVersion);
        Assert.True(AbilityCatalog.NeedsPatchRevalidation);
        Assert.False(AbilityCoverage.Build(AbilityCatalog.Default).CurrentPatchLiveVerified);
    }

    [Theory]
    [InlineData("DEATHKNIGHT", "Blood", "Tank")]
    [InlineData("DEMONHUNTER", "Devourer", "DPS")]
    [InlineData("EVOKER", "Preservation", "Healer")]
    [InlineData("PRIEST", "Holy", "Healer")]
    [InlineData("PALADIN", "Retribution", "DPS")]
    public void Role_Mapping_Is_Explicit(string cls, string spec, string expected) =>
        Assert.Equal(expected, SpecCoverageMatrix.Role(cls, spec));
}
