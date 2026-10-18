namespace MaxDpsCompanion;

/// <summary>What the last <see cref="ScreenSampler.Sample(Point,int,IntPtr)"/> produced.</summary>
internal enum CaptureOutcome
{
    /// <summary>A capture was taken (the frame may still fail to decode).</summary>
    Sampled,

    /// <summary>The game window is minimized; the capture was skipped entirely.</summary>
    Minimized,

    /// <summary>The capture is entirely black: minimized, occluded or display asleep.</summary>
    Black,
}

/// <summary>
/// 4.1 A2.12: capture robustness. A minimized window and a black/occluded strip
/// are distinct, actionable states — they used to read as the generic
/// "no pixel block - client must be windowed or borderless".
/// </summary>
internal static class CaptureDiagnostics
{
    /// <summary>Channel value at/below which a cell counts as black (HDR-safe headroom).</summary>
    public const int BlackChannel = 24;

    public const string MinimizedMessage = "game window is minimized - restore it to resume";

    public const string BlackFrameMessage =
        "strip occluded: screen capture is black (window covered, minimized or display asleep)";

    /// <summary>True when every cell is near-black (an empty array counts as black).</summary>
    public static bool IsBlackFrame(Color[]? cells)
    {
        if (cells is null || cells.Length == 0) return true;
        foreach (var cell in cells)
            if (cell.R > BlackChannel || cell.G > BlackChannel || cell.B > BlackChannel) return false;
        return true;
    }
}
