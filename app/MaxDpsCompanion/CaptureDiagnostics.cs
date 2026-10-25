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
///
/// 4.4.0 "Wayfinder": the same messages now double as the TRANSIENT-state
/// vocabulary. Detection pauses while the game is not foreground, so
/// alt-tabbing produces a burst of states that are expected, not events; the
/// Exp/classic log suppresses exactly these (see <see cref="IsTransientStatus"/>)
/// while the status trackers still advance (A → transient → A must not re-log A).
/// </summary>
internal static class CaptureDiagnostics
{
    /// <summary>Channel value at/below which a cell counts as black (HDR-safe headroom).</summary>
    public const int BlackChannel = 24;

    /// <summary>
    /// 4.4.0 hard foreground gate: sampling is paused (not failing) because the
    /// game window is not the foreground window.
    /// </summary>
    public const string BackgroundMessage = "game in background - detection paused";

    /// <summary>The strip was not found anywhere in the client area (windowed/borderless required).</summary>
    public const string NoBlockMessage = "no pixel block - client must be windowed or borderless";

    /// <summary>The capture chain went stale and is re-attached from scratch.</summary>
    public const string ReattachMessage = "re-attaching to the game window";

    public const string MinimizedMessage = "game window is minimized - restore it to resume";

    public const string BlackFrameMessage =
        "strip occluded: screen capture is black (window covered, minimized or display asleep)";

    /// <summary>The window resolved but its client area is empty (mid-resize / hidden).</summary>
    public const string NoClientAreaMessage = "game window has no client area";

    /// <summary>The attached window handle vanished (relog / display-mode change).</summary>
    public const string WindowLostMessage = "game window lost";

    /// <summary>
    /// True for the exact status strings that describe a TRANSIENT capture gap
    /// (background, minimized, occluded, re-attach) rather than an event worth
    /// a log line. Ordinary statuses ("Engine started.", "block found,
    /// re-aligned", "holding (out of combat)", "link lost (addon heartbeat
    /// frozen)") return false.
    ///
    /// Matching is ORDINAL, case-insensitive, and whole-string only: the shell
    /// filters its displayed status line, which it capitalises ("Game in
    /// background - detection paused"), while the engine reports the lowercase
    /// constants. A longer sentence that merely contains such a phrase is a
    /// real event and stays loggable.
    /// </summary>
    public static bool IsTransientStatus(string? message)
    {
        if (message is null) return false;
        return Matches(BackgroundMessage)
            || Matches(NoBlockMessage)
            || Matches(ReattachMessage)
            || Matches(MinimizedMessage)
            || Matches(BlackFrameMessage)
            || Matches(NoClientAreaMessage)
            || Matches(WindowLostMessage);

        bool Matches(string candidate) =>
            string.Equals(message, candidate, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The shell decorates a paused engine message as "Paused - &lt;engine&gt;";
    /// strip that prefix before classifying so a paused background tick is
    /// still recognised as transient (never logged as "no pixel block").
    /// </summary>
    public static bool IsTransientStatusLine(string? statusLine)
    {
        const string pausedPrefix = "Paused - ";
        if (statusLine is null) return false;
        var raw = statusLine.StartsWith(pausedPrefix, StringComparison.Ordinal)
            ? statusLine[pausedPrefix.Length..]
            : statusLine;
        return IsTransientStatus(raw);
    }

    /// <summary>True when every cell is near-black (an empty array counts as black).</summary>
    public static bool IsBlackFrame(Color[]? cells)
    {
        if (cells is null || cells.Length == 0) return true;
        foreach (var cell in cells)
            if (cell.R > BlackChannel || cell.G > BlackChannel || cell.B > BlackChannel) return false;
        return true;
    }
}
