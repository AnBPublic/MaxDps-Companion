using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Stream 2 (perf + bridge reliability) offline contracts: sampler constants,
/// the BlockLocator DPI/scale guard, the preserved colour tolerance, the
/// stale/skew copy (warn-only) and the SchedulerBench p95 regression gate.
/// No game, no screen, no wall clock.
/// </summary>
public class StreamTwoPerfTests
{
    [Fact]
    public void ColorProfile_Default_Tolerance_Is_Preserved()
    {
        Assert.Equal(64, ColorProfile.DefaultTolerance);
        Assert.Equal(ColorProfile.DefaultTolerance, new ColorProfile().Tolerance);
    }

    [Fact]
    public void ScreenSampler_Median_Threshold_Is_Documented_And_Stable()
    {
        Assert.Equal(5, ScreenSampler.TapsPerCell);
        Assert.True(ScreenSampler.TapsPerCell % 2 == 1, "median tap count must stay odd");
        Assert.Equal(2, ScreenSampler.MedianMinCellSize);
        Assert.Equal(33, ScreenSampler.RecommendedPollIntervalMs);
    }

    [Fact]
    public void BlockLocator_Clamp_Accepts_Fully_OnScreen()
    {
        var bounds = new Rectangle(0, 0, 1920, 1080);
        var ok = BlockLocator.TryClampCapture(new Point(10, 10), new Size(200, 50), bounds,
            out var origin, out var area);
        Assert.True(ok);
        Assert.Equal(new Point(10, 10), origin);
        Assert.Equal(new Size(200, 50), area);
    }

    [Fact]
    public void BlockLocator_Clamp_Clamps_Partially_OffScreen()
    {
        var bounds = new Rectangle(0, 0, 1000, 1000);
        var ok = BlockLocator.TryClampCapture(new Point(-5, -5), new Size(100, 100), bounds,
            out var origin, out var area);
        Assert.True(ok);
        Assert.Equal(new Point(0, 0), origin);
        Assert.Equal(new Size(95, 95), area);
    }

    [Fact]
    public void BlockLocator_Clamp_Rejects_Mostly_OffScreen_And_Huge()
    {
        var bounds = new Rectangle(0, 0, 1000, 1000);
        // 1000x1000 at -900,-900: only 100x100 visible (1%) -> a scale artifact.
        Assert.False(BlockLocator.TryClampCapture(new Point(-900, -900), new Size(1000, 1000), bounds,
            out _, out _));
        // Fully off-screen.
        Assert.False(BlockLocator.TryClampCapture(new Point(-400, -10), new Size(200, 50), bounds,
            out _, out _));
        // Absurd area is refused before any allocation.
        Assert.False(BlockLocator.TryClampCapture(new Point(0, 0), new Size(9000, 50), bounds,
            out _, out _));
        // Degenerate area.
        Assert.False(BlockLocator.TryClampCapture(new Point(1, 1), new Size(0, 50), bounds,
            out _, out _));
    }

    [Fact]
    public void BridgeHealth_VersionSkew_Warns_And_Points_At_Repair()
    {
        Assert.Null(BridgeHealth.VersionSkewNotice(5, 5));
        var notice = BridgeHealth.VersionSkewNotice(4, 5);
        Assert.NotNull(notice);
        Assert.Contains("install-addon.ps1", notice);
        Assert.Contains("/reload", notice);
        Assert.Contains("Recalibrate", notice);
    }

    [Fact]
    public void BridgeHealth_Mismatch_Warns_Only_Above_Threshold()
    {
        Assert.Null(BridgeHealth.MismatchNotice(0, 10));                 // below sample floor
        Assert.Null(BridgeHealth.MismatchNotice(1, 1000));               // 0.1% healthy
        var notice = BridgeHealth.MismatchNotice(200, 1000);             // 20% drifting
        Assert.NotNull(notice);
        Assert.Contains("install-addon.ps1", notice);
        Assert.Contains("/reload", notice);
        Assert.Contains("Recalibrate", notice);
    }

    [Fact]
    public void SchedulerBench_Baseline_P95_Does_Not_Regress()
    {
        var lines = SchedulerBench.Run(27_000);
        var baseline = lines.Single(line => line.StartsWith("baseline:", StringComparison.Ordinal));
        Assert.EndsWith("-> PASS", baseline);

        // Parse "p95 {n}ms" from the baseline line and re-assert the ceiling.
        var token = baseline.Split("p95 ")[1].Split("ms")[0];
        var p95 = long.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
        var ceiling = (long)Math.Ceiling(SchedulerBench.BaselineP95Ms * (1 + SchedulerBench.BaselineTolerance));
        Assert.True(p95 <= ceiling, $"p95 {p95}ms regressed past the {ceiling}ms ceiling");
    }
}
