using System.Runtime.InteropServices;

namespace MaxDpsCompanion;

/// <summary>
/// Metrics shim for the frozen engine (P3 audit: still required — no engine
/// behavior edit is permitted and WinForms is gone from the build,
/// UseWindowsForms=false). <see cref="BlockLocator"/> calls the single WinForms
/// API the frozen engine ever used: <c>SystemInformation.VirtualScreen</c>
/// (BlockLocator.cs:56). This type provides the identical value via
/// GetSystemMetrics, matching System.Windows.Forms.SystemInformation
/// (fallback chain included). The process is PerMonitorV2, so the metrics are
/// physical pixels, exactly what the DPI guard needs.
/// </summary>
internal static class SystemInformation
{
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    /// <summary>Union of all display monitors, in physical pixels.</summary>
    internal static Rectangle VirtualScreen
    {
        get
        {
            var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            if (width == 0) width = GetSystemMetrics(SM_CXSCREEN);
            if (width == 0) width = 1024;
            var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            if (height == 0) height = GetSystemMetrics(SM_CYSCREEN);
            if (height == 0) height = 768;
            return new Rectangle(
                GetSystemMetrics(SM_XVIRTUALSCREEN),
                GetSystemMetrics(SM_YVIRTUALSCREEN),
                width,
                height);
        }
    }
}
