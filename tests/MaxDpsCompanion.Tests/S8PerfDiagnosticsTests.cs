extern alias win;

using System.Drawing;
using win::MaxDpsCompanion.Ui.Shell;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// S8 perf + diagnostics: the off-thread ability-row precompute cache, the
/// scaled-icon cache, the why-not-firing explainer and the install doctor.
/// Pure logic only — no engine, no wire, no scheduler, no XAML.
/// P2-E port (docs/plans/2026-10-11-winui3-v4-migration.md §6).
///
/// FINAL FATES (16 original tests):
///  * REINSTATED (15): the 4 ClassBrowserPrecompute tests now target the P2-D
///    AbilityRowsPrecompute (same warm/cache/invalidate/Key contract), the 4
///    WhyNotFiring explainer tests target the ported Ui.Shell type, the 6
///    InstallDoctor tests are unchanged, and ScaledIcon_Missing_Source_Or_Id_
///    Is_Null still holds for the WinUI cache (null-source/null-id early
///    returns never construct a BitmapImage).
///  * DELETED (1): ScaledIcon_Get_Scales_And_Reuses asserted GDI+ Bitmap
///    width/height and instance reuse. The WinUI cache returns a
///    Microsoft.UI.Xaml.Media.Imaging.BitmapImage whose pixels decode
///    asynchronously on the UI thread, so the assertion cannot run in xunit
///    (no XAML/STA-in-xunit tests per the migration plan). The same-property
///    image reuse/eviction is exercised live by --ui-snapshot-page=abilities.
/// </summary>
public class S8PerfDiagnosticsTests
{
    // ---- AbilityRowsPrecompute (old ClassBrowserPrecompute) ----

    private static (string Class, string Spec) FirstSpec()
    {
        var cls = AbilityCatalog.ClassOrder.First(c => c.Length > 0);
        return (cls, AbilityCatalog.SpecOrder[cls][1]);
    }

    [Fact]
    public void Precompute_Build_Twice_Returns_Same_Cached_Instance()
    {
        var (cls, spec) = FirstSpec();
        var pre = new AbilityRowsPrecompute(AbilityCatalog.Default);

        var first = pre.GetBrowserRows(cls, spec, "All", 100);
        var second = pre.GetBrowserRows(cls, spec, "All", 100);

        Assert.Same(first, second);
        Assert.Equal(1, pre.CachedCount);
    }

    [Fact]
    public void Precompute_Invalidate_Drops_The_Cache()
    {
        var (cls, spec) = FirstSpec();
        var pre = new AbilityRowsPrecompute(AbilityCatalog.Default);
        pre.GetBrowserRows(cls, spec, "All", 100);

        pre.Invalidate();

        Assert.Equal(0, pre.CachedCount);
        var rebuilt = pre.GetBrowserRows(cls, spec, "All", 100);
        Assert.True(rebuilt.Items.Count > 0);
        Assert.Equal(1, pre.CachedCount);
    }

    [Fact]
    public void Precompute_Warm_Populates_The_Cache_Off_Thread()
    {
        var (cls, spec) = FirstSpec();
        var pre = new AbilityRowsPrecompute(AbilityCatalog.Default);
        Assert.Equal(0, pre.CachedCount);

        pre.WarmBrowser(cls, spec, "All", 100);

        Assert.True(SpinWait.SpinUntil(() => pre.CachedCount > 0, 5000));
    }

    [Fact]
    public void Precompute_Key_Handles_Nulls()
    {
        Assert.Equal("b|-|-|All|100", AbilityRowsPrecompute.BrowserKey(null, null, "All", 100));
        Assert.Equal("b|WARRIOR|Fury|All|100", AbilityRowsPrecompute.BrowserKey("WARRIOR", "Fury", "All", 100));
        Assert.Equal("e|q|All||-|-", AbilityRowsPrecompute.ExplorerKey("q", "All", [], null, null));
    }

    // ---- ScaledIconCache ----

    [Fact]
    public void ScaledIcon_Missing_Source_Or_Id_Is_Null()
    {
        var cache = new ScaledIconCache(_ => null);
        Assert.Null(cache.Get(42, 8));
        Assert.Null(cache.Get(0, 8));
        Assert.Null(cache.Get(42, 0));
    }

    // ---- WhyNotFiring ----

    private static WhyNotFiringFacts Facts(
        bool toggleOn = true, bool running = true, bool paused = false,
        string? reason = "MainRotation", string? head = "Bloodthirst",
        int frameAgeMs = 100, int staleAfterMs = 1500) =>
        new("Bloodthirst", toggleOn, running, paused, reason, head, frameAgeMs, staleAfterMs);

    [Fact]
    public void WhyNot_Toggle_Off_Explains_Prohibition_Only()
    {
        var lines = WhyNotFiring.Explain(Facts(toggleOn: false));
        Assert.Single(lines);
        Assert.Contains("OFF", lines[0]);
    }

    [Fact]
    public void WhyNot_Engine_Stopped_Stops_Before_Scheduler()
    {
        var lines = WhyNotFiring.Explain(Facts(running: false));
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.Contains("stopped"));
    }

    [Fact]
    public void WhyNot_Stale_Names_The_Age_And_Still_Reports_The_Verdict()
    {
        var lines = WhyNotFiring.Explain(Facts(frameAgeMs: 9000));
        Assert.Contains(lines, l => l.Contains("stale") && l.Contains("9000"));
        Assert.Contains(lines, l => l.Contains("MainRotation") && l.Contains("Bloodthirst"));
    }

    [Fact]
    public void WhyNot_Clean_Tick_Reports_Fresh_And_Scheduler_Verdict()
    {
        var lines = WhyNotFiring.Explain(Facts());
        Assert.Contains(lines, l => l.Contains("fresh"));
        Assert.Contains(lines, l => l.Contains("MainRotation"));
    }

    // ---- InstallDoctor ----

    private static DoctorInputs Inputs(
        string? exe = "58bddba", string? head = "58bddba", string? app = "3.4.0", string? addon = "3.4.0",
        int cellSize = 8, int? located = 8, int appMask = 0x0FFF, int? mirror = 0x0FFF, bool mirrorValid = true) =>
        new(exe, head, app, addon, cellSize, located, appMask, mirror, mirrorValid);

    [Fact]
    public void Doctor_All_Healthy_Is_Ok()
    {
        var findings = InstallDoctor.Audit(Inputs());
        Assert.All(findings, f => Assert.Equal(DoctorSeverity.Ok, f.Severity));
        Assert.StartsWith("Install doctor: OK", InstallDoctor.Summarize(findings));
    }

    [Fact]
    public void Doctor_Exe_Behind_Head_Warns()
    {
        var findings = InstallDoctor.Audit(Inputs(exe: "f4734af", head: "58bddba"));
        var f = findings.Single(x => x.Check == "Exe vs HEAD");
        Assert.Equal(DoctorSeverity.Warn, f.Severity);
        Assert.Contains("rebuild", f.Detail);
    }

    [Fact]
    public void Doctor_CellSize_Mismatch_Fails()
    {
        var findings = InstallDoctor.Audit(Inputs(cellSize: 8, located: 10));
        var f = findings.Single(x => x.Check == "Cell size");
        Assert.Equal(DoctorSeverity.Fail, f.Severity);
        Assert.Contains("10 px", f.Detail);
    }

    [Fact]
    public void Doctor_Toggle_Mismatch_Names_The_Differing_Bits()
    {
        // App has Main(0), Solo(8), OOC(9); mirror has only Main.
        var findings = InstallDoctor.Audit(Inputs(appMask: 0x0301, mirror: 0x0001));
        var f = findings.Single(x => x.Check == "Toggle mirror");
        Assert.Equal(DoctorSeverity.Warn, f.Severity);
        Assert.Contains("Solo", f.Detail);
        Assert.Contains("OOC", f.Detail);
    }

    [Fact]
    public void Doctor_Addon_Version_Behind_Warns()
    {
        var findings = InstallDoctor.Audit(Inputs(app: "3.5.0", addon: "3.3.0"));
        var f = findings.Single(x => x.Check == "Addon version");
        Assert.Equal(DoctorSeverity.Warn, f.Severity);
    }

    [Fact]
    public void Doctor_Missing_Mirror_And_Head_Are_Info_Not_Warnings()
    {
        var findings = InstallDoctor.Audit(Inputs(head: null, mirrorValid: false, mirror: null));
        Assert.Equal(DoctorSeverity.Info, findings.Single(x => x.Check == "Exe vs HEAD").Severity);
        Assert.Equal(DoctorSeverity.Info, findings.Single(x => x.Check == "Toggle mirror").Severity);
    }
}
