using System.Drawing;
using System.Runtime.InteropServices;

namespace MaxDpsCompanion.Mac;

// Remaining platform adapters: physical key state, clock, permissions.
// All P/Invoke-local (no AppKit/ObjC needed).
//
// The strip sweep lives in MacBlockScanner.cs (stream-first); the legacy
// CGWindowList capture path it falls back to lives in MacLegacyCapture.cs.

/// <summary>Live key state via CGEventSourceKeyState (fail-safe: unknown reads as not-held).</summary>
internal sealed class MacPhysicalKeys : IPhysicalKeys
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    private const int kCGEventSourceStateHIDSystemState = 1;

    [DllImport(CoreGraphics, EntryPoint = "CGEventSourceKeyState")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGEventSourceKeyState(int stateID, ushort keyCode);

    public bool IsPhysicallyDown(byte virtualKey)
    {
        try
        {
            var code = MacVkMap.Map(virtualKey);
            if (code is null) return false;
            return CGEventSourceKeyState(kCGEventSourceStateHIDSystemState, code.Value);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>Wall clock over Stopwatch; Wait is Thread.Sleep (macOS sleep
/// granularity is ~1ms-class, so the 33ms cadence is unaffected).</summary>
internal sealed class MacClock : IClock
{
    private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
    public long ElapsedMs => _sw.ElapsedMilliseconds;
    public void Sleep(int milliseconds) => Thread.Sleep(milliseconds);
    public void Wait(int milliseconds) => Thread.Sleep(milliseconds);
}

/// <summary>TCC permission gates. Check variants never prompt (probe-safe);
/// the engine host prompts explicitly at Start when a check fails.</summary>
internal sealed class MacPermissions : IPermissions
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string AppServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    [DllImport(CoreGraphics, EntryPoint = "CGPreflightScreenCaptureAccess")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGPreflightScreenCaptureAccess();

    [DllImport(CoreGraphics, EntryPoint = "CGRequestScreenCaptureAccess")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGRequestScreenCaptureAccess();

    [DllImport(AppServices, EntryPoint = "AXIsProcessTrusted")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrusted();

    public bool CheckCapturePermission()
    {
        try { return CGPreflightScreenCaptureAccess(); }
        catch (EntryPointNotFoundException) { return true; } // pre-10.15: no gate
        catch { return false; }
    }

    public bool CheckInputPermission()
    {
        try { return AXIsProcessTrusted(); }
        catch { return false; }
    }

    internal static bool RequestCapture()
    {
        try { return CGRequestScreenCaptureAccess(); }
        catch { return false; }
    }
}
