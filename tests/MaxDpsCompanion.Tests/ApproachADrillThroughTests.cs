using MaxDpsCompanion.Ui.Shell;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// P2-E port of the v3.4.0 Approach A drill-through tests
/// (docs/plans/2026-10-11-winui3-v4-migration.md §6).
///
/// FINAL FATES (4 original tests):
///  * REINSTATED (3): Preset_Vocabulary_Normalizes_And_Matches and
///    ConditionLine_Uses_Catalog_Fields are pure AbilityViewPresets logic;
///    Explorer_Preset_Type_Filter_And_EmptyState_Names_Class now asserts the
///    Explorer row filter through AbilityRowsPrecompute (the UI empty-state
///    string "No Trinket skills for Mage/Fire" is built in AbilityListView and
///    is reviewed via --ui-snapshot-page=abilities).
///  * DELETED (1): ClassSkills_Preset_Filters_And_Names_Toggle — the Class
///    skills screen was already unreachable and is dropped with ClassSkillsView
///    (migration plan §0); no WinUI surface exists to test.
/// </summary>
public class ApproachADrillThroughTests
{
    [Fact]
    public void Preset_Vocabulary_Normalizes_And_Matches()
    {
        Assert.Equal("Self Sustain", AbilityViewPresets.NormalizeCategory("Self-heal"));
        Assert.Equal("CrowdControl", AbilityViewPresets.NormalizeCategory("Crowd control"));
        Assert.True(AbilityViewPresets.IsCategoryTag("Offensive"));
        Assert.True(AbilityViewPresets.IsCategoryTag("Solo"));
        Assert.False(AbilityViewPresets.IsCategoryTag("Companion"));
        Assert.False(AbilityViewPresets.IsCategoryTag("Warnings"));

        var catalog = AbilityCatalog.Default;
        var offensive = catalog.All.First(a => a.HasOffensiveCapability);
        Assert.True(AbilityViewPresets.MatchesCategory(offensive, "Offensive", catalog, null, null));

        var polymorph = catalog.TryGet(118);
        Assert.NotNull(polymorph);
        Assert.True(AbilityViewPresets.MatchesCategory(polymorph!, "Crowd control", catalog, "MAGE", "Fire"));
        Assert.True(AbilityViewPresets.MatchesCategory(polymorph!, "CrowdControl", catalog, "MAGE", "Fire"));

        var solo = catalog.All.First(a => a.Purpose == AbilityPurpose.SelfHeal || a.HasTag("SelfSustain"));
        Assert.True(AbilityViewPresets.MatchesCategory(solo, "Solo", catalog, null, null));
    }

    [Fact]
    public void ConditionLine_Uses_Catalog_Fields()
    {
        var catalog = AbilityCatalog.Default;

        var below = catalog.All.First(a => a.UseBelowHpPct is not null);
        Assert.StartsWith("Below ", AbilityViewPresets.ConditionLine(below));

        var aoe = catalog.All.FirstOrDefault(a => a.OffensiveUsage == OffensiveUsage.AoeOnly);
        if (aoe is not null)
            Assert.Contains("AoE only", AbilityViewPresets.ConditionLine(aoe));

        var polymorph = catalog.TryGet(118);
        Assert.NotNull(polymorph);
        var dr = AbilityViewPresets.ConditionLine(polymorph!, catalog.CrowdControlFor("MAGE", "Fire", 118));
        Assert.Contains("DR: ", dr);
    }

    [Fact]
    public void Explorer_Preset_Type_Filter_And_EmptyState_Names_Class()
    {
        var precompute = new AbilityRowsPrecompute(AbilityCatalog.Default);
        var offensive = precompute.GetExplorerRows("", "All", ["Offensive"], null, null).Items.Count;
        var trinket = precompute.GetExplorerRows("", "All", ["Trinket"], "MAGE", "Fire").Items;
        var emptyLabel = AbilityViewPresets.CategoryDisplay("Trinket");
        var classLabel = AbilityViewPresets.ClassDisplay("MAGE");

        Assert.True(offensive > 0, "the catalog should contain offensive abilities");
        Assert.Empty(trinket);
        // The empty-state names come from the same vocabulary the view uses.
        Assert.Equal("Trinket", emptyLabel);
        Assert.Equal("Mage", classLabel);
    }
}
