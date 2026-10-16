using System.Diagnostics;

namespace MaxDpsCompanion;

// Default platform adapters for RotationEngine's optional ctor parameters.
// WINDOWS-ONLY implementations live in the Windows app (GDI/BitBlt,
// PostMessage/SendInput, HWND lookup, waitable timer); the Mac app supplies
// its own (CGWindowList, CGEventPost, NSWorkspace). These Core fallbacks
// exist so `new RotationEngine(settings)` compiles and unit-tests anywhere:
// they throw PlatformNotSupportedException only when actually driven.

// NOTE: the real Windows adapters are thin wrappers over the existing
// Windows files (WowWindow, ScreenSampler, KeySender, MovementGuard,
// BlockLocator, Stopwatch). They are NOT duplicated here — the Windows app
// references this Core project and passes its own adapters explicitly.

/// <summary>Wall clock over Stopwatch (portable; the waitable-timer pacing
/// precision lives in the platform Wait implementation, not here).</summary>
internal sealed class StopwatchClock : IClock
{
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    public long ElapsedMs => _sw.ElapsedMilliseconds;
    public void Sleep(int milliseconds) => Thread.Sleep(milliseconds);
    public void Wait(int milliseconds) => Thread.Sleep(milliseconds);
}

/// <summary>Throws on use: no capture without a platform sampler.</summary>
internal sealed class UnimplementedSampler : IScreenSampler
{
    public Color[] Sample(System.Drawing.Point origin, int cellSize) =>
        throw new PlatformNotSupportedException("No IScreenSampler for this platform.");
    public Color[] SampleV1(System.Drawing.Point origin, int cellSize) =>
        throw new PlatformNotSupportedException("No IScreenSampler for this platform.");
    public (int Mask, int Epoch, int Blocked)? Ext3Mirror => null;
    public bool Ext3MirrorPresent => false;
    public void Reset() { }
    public void Dispose() { }
}

/// <summary>Throws on use: no window without a platform implementation.</summary>
internal sealed class UnimplementedWindow : IGameWindow
{
    public IntPtr Handle => IntPtr.Zero;
    public bool IsValid => false;
    public bool IsForeground => false;
    public uint ProcessId => 0;
    public bool IsGameWindow(IntPtr candidate) => false;
    public bool Refresh(string processName) => false;
    public bool TryGetClientOrigin(out System.Drawing.Point origin, out System.Drawing.Size clientSize)
    {
        origin = System.Drawing.Point.Empty;
        clientSize = System.Drawing.Size.Empty;
        return false;
    }
    public void Reset() { }
}

/// <summary>Throws on use: no input without a platform sender.</summary>
internal sealed class UnimplementedKeys : IKeySender
{
    public bool SendToWindow(IntPtr target, uint expectedProcessId, Keys.KeyRef stroke, int holdMilliseconds) => false;
    public void Send(Keys.KeyRef stroke, int holdMilliseconds) { }
    public bool IsMouse(byte virtualKey) => Keys.IsMouse(virtualKey);
    public string DescribeStroke(Keys.KeyRef stroke) => Keys.Describe(stroke);
}

/// <summary>Never-held fallback (fail-safe: never synthesize a bogus KEYUP).</summary>
internal sealed class UnimplementedPhysical : IPhysicalKeys
{
    public bool IsPhysicallyDown(byte virtualKey) => false;
}

/// <summary>Null sweep (fail-safe: engine reports "no pixel block").</summary>
internal sealed class UnimplementedScanner : IBlockScanner
{
    public (int OffsetX, int OffsetY, int CellSize)? Locate(System.Drawing.Point origin, System.Drawing.Size area) => null;
}
