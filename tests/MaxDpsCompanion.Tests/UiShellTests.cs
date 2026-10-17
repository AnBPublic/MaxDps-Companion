extern alias win;

using win::MaxDpsCompanion.Ui.Shell;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// P2-E port of the v3 shell tests (docs/plans/2026-10-11-winui3-v4-migration.md §6).
///
/// FINAL FATES (7 original tests):
///  * REINSTATED (2): Explorer_Filters_Searches_And_Keeps_Catalog_Responsive
///    (AbilityRowsPrecompute/AbilityViewPresets model) and
///    Intelligence_Report_Matches_Catalog (AbilityIntelligence.Audit).
///  * DELETED (5): Shell_Smoke_Test_Has_No_Structural_Findings moved to the
///    hosted CLI (--ui-smoke-test + tools/ui_smoke.ps1, ShellValidation);
///    Popups_Open_Close_And_Esc and Intelligence_Tile_Click_Drills are click
///    plumbing — the overlay open/close drill lives in --ui-smoke-test and Esc
///    stays OWED live; Toggle_Switch_Mouse_Click_Toggles and
///    Wrapping_Label_Re_Measures are WinForms control/text-measure internals —
///    ToggleModel semantics are pinned by ClassicUiTests, XAML owns wrapping.
/// </summary>
public class UiShellTests
{
    /// <summary>Old Explorer_Filters_Searches test, now against the P2-D row precompute (no controls).</summary>
    [Fact]
    public void Explorer_Filters_Searches_And_Keeps_Catalog_Responsive()
    {
        var precompute = new AbilityRowsPrecompute(AbilityCatalog.Default);
        var all = precompute.GetExplorerRows("", "All", [], null, null).Items.Count;
        var automatic = precompute.GetExplorerRows("", "Automatic", [], null, null).Items.Count;
        var searched = precompute.GetExplorerRows("Charge", "All", [], null, null).Items.Count;
        var numeric = precompute.GetExplorerRows("0", "All", [], null, null).Items.Count;

        Assert.True(all > 100, $"expected a large catalog, got {all}");
        Assert.True(automatic > 0);
        Assert.True(automatic <= all);
        Assert.True(searched > 0);
        Assert.True(numeric > 0);
    }

    [Fact]
    public void Intelligence_Report_Matches_Catalog()
    {
        var catalog = AbilityCatalog.Default;
        var report = AbilityIntelligence.Audit(catalog);

        Assert.Equal(catalog.Count, report.Summary.Total);
        Assert.True(report.Summary.Automatic > 0);
        Assert.True(report.Summary.Automatic <= report.Summary.Total);
    }
}
