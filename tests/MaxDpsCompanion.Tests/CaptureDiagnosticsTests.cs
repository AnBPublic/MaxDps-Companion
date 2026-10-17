using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 4.1 A2.12: capture robustness. A minimized window is skipped before the
/// BitBlt (no surface is even acquired) and an all-black capture is reported as
/// "strip occluded" instead of the generic "no pixel block".
/// </summary>
public class CaptureDiagnosticsTests
{
    [Fact]
    public void All_Black_Is_A_Black_Frame()
    {
        var cells = Enumerable.Repeat(Color.FromArgb(0, 0, 0), PixelProtocol.CellCountExt4).ToArray();
        Assert.True(CaptureDiagnostics.IsBlackFrame(cells));
    }

    [Fact]
    public void Near_Black_Within_The_Headroom_Is_Still_Black()
    {
        var cell = Color.FromArgb(CaptureDiagnostics.BlackChannel, CaptureDiagnostics.BlackChannel - 1, 0);
        Assert.True(CaptureDiagnostics.IsBlackFrame([cell, cell]));
    }

    [Fact]
    public void One_Live_Cell_Is_Not_A_Black_Frame()
    {
        var cells = new Color[PixelProtocol.CellCountExt4];
        cells[0] = Color.FromArgb(255, 0, 255);   // the magic cell
        Assert.False(CaptureDiagnostics.IsBlackFrame(cells));
    }

    [Fact]
    public void Empty_Or_Null_Capture_Counts_As_Black()
    {
        Assert.True(CaptureDiagnostics.IsBlackFrame([]));
        Assert.True(CaptureDiagnostics.IsBlackFrame(null));
    }

    [Fact]
    public void Minimized_Window_Skips_The_Capture_Entirely()
    {
        using var sampler = new ScreenSampler { IsMinimizedProbe = _ => true };

        var cells = sampler.Sample(new Point(10, 10), 8, new IntPtr(0x1234));

        Assert.Equal(PixelProtocol.CellCountExt4, cells.Length);
        Assert.All(cells, cell => Assert.Equal(Color.Empty, cell));
        Assert.Equal(CaptureOutcome.Minimized, sampler.LastOutcome);
        // The blit path was never entered: no screen DC was acquired.
        Assert.False(sampler.HasSurface);
    }

    [Fact]
    public void Not_Minimized_Does_Not_Report_Minimized()
    {
        using var sampler = new ScreenSampler { IsMinimizedProbe = _ => false };

        sampler.Sample(new Point(10, 10), 8, new IntPtr(0x1234));

        Assert.NotEqual(CaptureOutcome.Minimized, sampler.LastOutcome);
    }

    [Fact]
    public void Messages_Distinguish_Occluded_From_Missing_Strip()
    {
        Assert.Contains("minimized", CaptureDiagnostics.MinimizedMessage);
        Assert.Contains("occluded", CaptureDiagnostics.BlackFrameMessage);
        Assert.DoesNotContain("no pixel block", CaptureDiagnostics.BlackFrameMessage);
    }
}
