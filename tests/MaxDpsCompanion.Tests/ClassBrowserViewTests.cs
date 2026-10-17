using System.Diagnostics;
using MaxDpsCompanion.Ui.Shell;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// P2-E port of the S6 Class Browser tests (docs/plans/2026-10-11-winui3-v4-migration.md §6).
///
/// FINAL FATES (4 original tests):
///  * REINSTATED (3) against the P2-D row precompute/model: row building per
///    class/spec/mode, the mode vocabulary (ClassBrowserView.ModeOptions), and
///    the warm-reopen budget (cached row set reuse, no control construction).
///  * DELETED (1): Browser_Paint_Smoke_No_Garble (WinForms DrawToBitmap +
///    GetPixel) — replaced by the RenderTargetBitmap non-blank PNG assertion
///    in --ui-smoke-test / --ui-snapshot-page.
/// </summary>
public class ClassBrowserViewTests
{
    [Fact]
    public void Browser_Builds_Rows_For_Class_Spec_And_Mode()
    {
        var precompute = new AbilityRowsPrecompute(AbilityCatalog.Default);
        var all = precompute.GetBrowserRows("ROGUE", "Outlaw", "All", 100).Items.Count;
        var offensive = precompute.GetBrowserRows("ROGUE", "Outlaw", "Offensive", 100).Items.Count;
        var trinket = precompute.GetBrowserRows("ROGUE", "Outlaw", "Trinket", 100).Items.Count;

        Assert.True(all > 0, "the browser should list Rogue/Outlaw abilities");
        Assert.True(offensive > 0, "Rogue/Outlaw should have offensive abilities");
        Assert.True(offensive <= all, "a mode must only narrow the list");
        Assert.True(trinket >= 0);
        // The selection the list was built for is part of the cache key the view
        // uses to decide a warm reopen can keep the list (old DebugState).
        Assert.Contains("ROGUE", AbilityRowsPrecompute.BrowserKey("ROGUE", "Outlaw", "All", 100));
        Assert.Contains("Outlaw", AbilityRowsPrecompute.BrowserKey("ROGUE", "Outlaw", "All", 100));
    }

    [Fact]
    public void Browser_Exposes_Every_Mode()
    {
        var modes = ClassBrowserView.ModeOptions.Select(m => m.Label).ToArray();
        Assert.Equal(
            new[]
            {
                "All", "Main", "Offensive", "Defensive", "Interrupt", "CC", "Mobility",
                "Solo self-sustain", "Consumable", "Trinket", "Utility/manual",
            },
            modes);
    }

    /// <summary>
    /// S6 gate: a warm reopen (same class/spec/mode) reuses the precomputed row
    /// set and stays under 150 ms; the first build pays the filter + sort.
    /// </summary>
    [Fact]
    public void Browser_Warm_Open_Under_150ms()
    {
        var precompute = new AbilityRowsPrecompute(AbilityCatalog.Default);
        var cold = precompute.GetBrowserRows("ROGUE", "Outlaw", "All", 100);

        var sw = Stopwatch.StartNew();
        var warm = precompute.GetBrowserRows("ROGUE", "Outlaw", "All", 100);
        sw.Stop();

        Assert.True(cold.Items.Count > 0);
        Assert.Same(cold, warm);
        Assert.Equal(1, precompute.CachedCount);
        Assert.True(sw.Elapsed.TotalMilliseconds < 150,
            $"Class Browser warm open took {sw.Elapsed.TotalMilliseconds:F1} ms (target < 150)");
    }
}
