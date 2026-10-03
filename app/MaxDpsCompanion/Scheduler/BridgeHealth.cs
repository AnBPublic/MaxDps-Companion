namespace MaxDpsCompanion;

/// <summary>
/// Stale / skew UX copy (Stream 2, task 2.3). Pure and side-effect free: it
/// turns existing decode counters (checksum/commit faults, sampled version)
/// into a warning STRING, and never throws or hard-fails. The engine already
/// decides whether to surface it; keeping the copy here makes it testable and
/// keeps the repair path (install-addon.ps1 + /reload + the calibration
/// wizard) in one place.
/// </summary>
internal static class BridgeHealth
{
    /// <summary>Minimum samples before a mismatch rate is meaningful.</summary>
    public const int MismatchMinSamples = 30;

    /// <summary>
    /// Suggest a recalibration once checksum/commit mismatches exceed this
    /// fraction of samples. A healthy strip is ~0; occasional torn frames at a
    /// real display-mode change are normal, so the bar is deliberately
    /// conservative.
    /// </summary>
    public const double MismatchRateThreshold = 0.05;

    /// <summary>Repair path shared by every stale/skew notice.</summary>
    public const string RepairHint =
        "Run install-addon.ps1, then /reload in game.";

    /// <summary>Second-stage repair when the addon is current but colours drift.</summary>
    public const string CalibrateHint =
        "If colours still misread, open the companion and run Recalibrate (calibration wizard).";

    /// <summary>
    /// Warn (never fail) that the sampled addon width is older than the
    /// companion's supported version. Returns null when there is no skew.
    /// </summary>
    public static string? VersionSkewNotice(int sampledVersion, int expectedVersion)
    {
        if (sampledVersion <= 0 || expectedVersion <= 0 || sampledVersion == expectedVersion)
            return null;
        return $"Your in-game addon is v{sampledVersion}, the companion expects v{expectedVersion}. "
            + RepairHint + " " + CalibrateHint;
    }

    /// <summary>
    /// Suggest a recalibration when the checksum/commit mismatch rate over the
    /// supplied counters exceeds <see cref="MismatchRateThreshold"/>. Warns
    /// only: below the sample floor or the rate threshold it returns null.
    /// </summary>
    public static string? MismatchNotice(int checksumOrCommitFaults, int samples)
    {
        if (samples < MismatchMinSamples || checksumOrCommitFaults < 0) return null;
        var rate = (double)checksumOrCommitFaults / samples;
        if (rate <= MismatchRateThreshold) return null;
        return $"{rate:P0} of the last {samples} strip reads failed their checksum/commit "
            + "(display chain or addon skew). " + RepairHint + " " + CalibrateHint;
    }
}
