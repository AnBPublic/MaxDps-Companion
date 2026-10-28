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

        // 4.6.0: the sampler captures the widest frame the protocol defines
        // (Ext5 = 58 cells); this assertion used to pin CellCountExt4 (51).
        Assert.Equal(PixelProtocol.CellCountExt5, cells.Length);
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

    [Fact]
    public void Every_Transient_Capture_Status_Is_Classified()
    {
        Assert.True(CaptureDiagnostics.IsTransientStatus(CaptureDiagnostics.BackgroundMessage));
        Assert.True(CaptureDiagnostics.IsTransientStatus(CaptureDiagnostics.NoBlockMessage));
        Assert.True(CaptureDiagnostics.IsTransientStatus(CaptureDiagnostics.ReattachMessage));
        Assert.True(CaptureDiagnostics.IsTransientStatus(CaptureDiagnostics.MinimizedMessage));
        Assert.True(CaptureDiagnostics.IsTransientStatus(CaptureDiagnostics.BlackFrameMessage));
        Assert.True(CaptureDiagnostics.IsTransientStatus(CaptureDiagnostics.NoClientAreaMessage));
        Assert.True(CaptureDiagnostics.IsTransientStatus(CaptureDiagnostics.WindowLostMessage));
        // The literal copy the engine used before the constants existed.
        Assert.True(CaptureDiagnostics.IsTransientStatus("game window is minimized - restore it to resume"));
    }

    [Fact]
    public void The_Capitalised_Status_Line_Is_Also_Classified()
    {
        // The shell logs its CAPITALISED status line, not the raw engine copy.
        Assert.True(CaptureDiagnostics.IsTransientStatus("Game in background - detection paused"));
        Assert.True(CaptureDiagnostics.IsTransientStatus("No pixel block - client must be windowed or borderless"));
        Assert.True(CaptureDiagnostics.IsTransientStatus("Re-attaching to the game window"));
        Assert.True(CaptureDiagnostics.IsTransientStatus("Game window is minimized - restore it to resume"));
        Assert.True(CaptureDiagnostics.IsTransientStatus("Game window has no client area"));
        Assert.True(CaptureDiagnostics.IsTransientStatus("Game window lost"));
    }

    [Fact]
    public void Normal_Status_Presets_Are_Not_Transient()
    {
        Assert.False(CaptureDiagnostics.IsTransientStatus("Engine started."));
        Assert.False(CaptureDiagnostics.IsTransientStatus("block found, re-aligned"));
        Assert.False(CaptureDiagnostics.IsTransientStatus("holding (out of combat)"));
        Assert.False(CaptureDiagnostics.IsTransientStatus("link lost (addon heartbeat frozen)"));
        Assert.False(CaptureDiagnostics.IsTransientStatus(null));
        Assert.False(CaptureDiagnostics.IsTransientStatus(""));
        // Exact-match vocabulary: a longer sentence that merely contains a
        // transient phrase is a real event and must still reach the log.
        Assert.False(CaptureDiagnostics.IsTransientStatus(
            "no pixel block - client must be windowed or borderless (retrying)"));
    }

    [Fact]
    public void The_Paused_Status_Prefix_Is_Stripped_Before_Classifying()
    {
        // RefreshStatus decorates a paused engine message as "Paused - <msg>";
        // the transient filter must still see the underlying capture state.
        Assert.True(CaptureDiagnostics.IsTransientStatusLine(
            "Paused - Game in background - detection paused"));
        Assert.True(CaptureDiagnostics.IsTransientStatusLine(
            "Paused - no pixel block - client must be windowed or borderless"));
        Assert.False(CaptureDiagnostics.IsTransientStatusLine("Paused - holding (out of combat)"));
        Assert.False(CaptureDiagnostics.IsTransientStatusLine("Paused."));
        Assert.False(CaptureDiagnostics.IsTransientStatusLine(null));
    }
}
