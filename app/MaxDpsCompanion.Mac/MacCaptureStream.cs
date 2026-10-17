using System.Drawing;
using System.Runtime.InteropServices;

namespace MaxDpsCompanion.Mac;

// Native ScreenCaptureKit bridge (libmdc_capture.dylib, see native/MdcCapture.swift).
//
// WHY: CGWindowListCreateImage was obsoleted in macOS 15 and current SDKs
// mark it unavailable; ScreenCaptureKit is the only supported capture API.
// SCK is ObjC + delegates, so the native surface is a small C ABI and this
// file is its managed half. Everything is window-relative: the stream is
// filtered to the WoW window, so the strip is found at the same place on any
// monitor, resolution or display scale — the host never sweeps "the screen".
//
// PERFORMANCE: the engine tick reads the newest delivered frame through a
// seqlock pointer (no syscall, no allocation). Region is bounded to a small
// rectangle and shrinks to the discovered strip rect after locate.
internal static class MdcNative
{
    private const string Lib = "libmdc_capture";

    // error codes (mirror MdcCapture.swift)
    internal const int ErrOk = 0;
    internal const int ErrUnavailable = -1;
    internal const int ErrNotAuthorized = -2;
    internal const int ErrNoWindow = -3;
    internal const int ErrStream = -4;
    internal const int ErrTimeout = -5;
    internal const int ErrTooSmall = -6;
    internal const int ErrNoFrame = -7;
    internal const int ErrBadArgs = -8;

    [DllImport(Lib, EntryPoint = "mdc_cap_available")]
    internal static extern int Available();

    [DllImport(Lib, EntryPoint = "mdc_cap_authorized")]
    internal static extern int Authorized();

    [DllImport(Lib, EntryPoint = "mdc_cap_frontmost_pid")]
    internal static extern int FrontmostPid();

    [DllImport(Lib, EntryPoint = "mdc_window_frame")]
    internal static extern int WindowFrame(uint pid, out double x, out double y, out double w, out double h);

    [DllImport(Lib, EntryPoint = "mdc_cap_start")]
    internal static extern int Start(uint pid, double x, double y, double w, double h, int fps);

    [DllImport(Lib, EntryPoint = "mdc_cap_stop")]
    internal static extern int Stop();

    [DllImport(Lib, EntryPoint = "mdc_cap_set_region")]
    internal static extern int SetRegion(double x, double y, double w, double h);

    [DllImport(Lib, EntryPoint = "mdc_cap_region")]
    internal static extern int Region(out int w, out int h);

    [DllImport(Lib, EntryPoint = "mdc_cap_snapshot")]
    internal static extern int Snapshot(out ulong seq, out IntPtr ptr, out int w, out int h, out int stride);

    [DllImport(Lib, EntryPoint = "mdc_cap_frame_age_ms")]
    internal static extern long FrameAgeMs();

    [DllImport(Lib, EntryPoint = "mdc_cap_stats")]
    internal static extern int Stats(out ulong delivered, out ulong skipped, out int fps);

    [DllImport(Lib, EntryPoint = "mdc_cap_dead")]
    internal static extern int Dead();

    [DllImport(Lib, EntryPoint = "mdc_cap_once")]
    internal static extern int Once(uint pid, double x, double y, double w, double h,
        IntPtr dst, int dstCap, out int outW, out int outH, out int outStride);

    [DllImport(Lib, EntryPoint = "mdc_cap_last_error")]
    private static extern IntPtr LastErrorPtr();

    [DllImport(Lib, EntryPoint = "mdc_cap_last_skip")]
    private static extern IntPtr LastSkipPtr();

    [DllImport(Lib, EntryPoint = "mdc_cap_has_session")]
    internal static extern int HasSessionRaw();

    private static int _probeState; // 0 unknown, 1 available, 2 missing
    private static string _probeError = "";

    internal static bool AvailableSafe
    {
        get
        {
            if (_probeState == 0)
            {
                try
                {
                    _probeState = Available() == 1 ? 1 : 2;
                }
                catch (Exception ex)
                {
                    _probeState = 2;
                    _probeError = $"{ex.GetType().Name}: {ex.Message}";
                }
            }
            return _probeState == 1;
        }
    }

    internal static string ProbeError => _probeError;

    internal static string LastError()
    {
        try
        {
            var p = LastErrorPtr();
            return p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p) ?? "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Why the last delivered frame was skipped (diagnostics).</summary>
    internal static string LastSkip()
    {
        try
        {
            var p = LastSkipPtr();
            return p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p) ?? "";
        }
        catch
        {
            return "";
        }
    }

    internal static string Describe(int code) => code switch
    {
        ErrOk => "ok",
        ErrUnavailable => "capture backend unavailable",
        ErrNotAuthorized => "Screen Recording NOT granted",
        ErrNoWindow => "game window not found",
        ErrStream => "ScreenCaptureKit stream error",
        ErrTimeout => "capture timed out",
        ErrTooSmall => "buffer too small",
        ErrNoFrame => "no frame yet",
        ErrBadArgs => "bad arguments",
        _ => $"code {code}",
    };
}

/// <summary>One published frame: BGRA pixels, region-local, top-left origin.</summary>
internal readonly struct CaptureFrame
{
    public readonly IntPtr Ptr;
    public readonly int Width;
    public readonly int Height;
    public readonly int Stride;
    public readonly ulong Seq;

    public CaptureFrame(IntPtr ptr, int width, int height, int stride, ulong seq)
    {
        Ptr = ptr; Width = width; Height = height; Stride = stride; Seq = seq;
    }

    public bool Valid => Ptr != IntPtr.Zero && Width > 0 && Height > 0 && Stride > 0;
}

/// <summary>
/// Owns the native capture stream: window-relative region management, the
/// newest-frame reader, geometry (window frame in points + display scale) and
/// the one-shot capture used by the locate escalation ladder.
///
/// All coordinates in this class are WINDOW-RELATIVE POINTS unless a name says
/// otherwise ("global" = Cocoa global screen points, "pixels" = device px of
/// the captured frame).
/// </summary>
internal sealed class MacCaptureStream : IDisposable
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [StructLayout(LayoutKind.Sequential)]
    private struct CgRect
    {
        public double X, Y, Width, Height;
    }

    [DllImport(CoreGraphics, EntryPoint = "CGGetActiveDisplayList")]
    private static extern int CGGetActiveDisplayList(uint maxDisplays, [Out] uint[]? displays, out uint count);

    [DllImport(CoreGraphics, EntryPoint = "CGDisplayBounds")]
    private static extern CgRect CGDisplayBounds(uint display);

    [DllImport(CoreGraphics, EntryPoint = "CGDisplayPixelsWide")]
    private static extern nuint CGDisplayPixelsWide(uint display);

    /// <summary>
    /// Locate band: the top-left corner of the window. "As small as possible,
    /// as big as necessary" - the first rung covers every realistic strip
    /// (43 cells up to 23 px + a small offset); the scanner's ladder widens it
    /// only when the strip is not there.
    /// </summary>
    public const int DefaultBandWidth = 1024;
    public const int DefaultBandHeight = 128;

    /// <summary>Second rung: full-width band for large cell sizes / offsets.</summary>
    public const int WideBandWidth = 2048;
    public const int WideBandHeight = 400;

    private readonly int _pid;
    private readonly int _fps;
    private Point _windowOrigin;      // global points
    private Size _windowSize;         // points
    private double _scale = 1;        // device pixels per point
    private double _regionX, _regionY, _regionW, _regionH;   // window-relative points
    private bool _started;
    private int _lastError;
    private string _lastErrorText = "";

    public MacCaptureStream(int pid, int fps = 60)
    {
        _pid = pid;
        _fps = Math.Clamp(fps, 1, 120);
    }

    public bool NativeAvailable => MdcNative.AvailableSafe;
    public bool Running => _started;
    public ulong FramesSeen { get; private set; }
    public int LastErrorCode => _lastError;
    public string LastErrorText => _lastErrorText;
    public double Scale => _scale;
    public Point WindowOrigin => _windowOrigin;
    public Size WindowSize => _windowSize;

    /// <summary>Pixels per point inside the current region (1 when unscaled).</summary>
    public double RegionScale => _scale;

    /// <summary>Region in window-relative points (current stream configuration).</summary>
    public (double X, double Y, double W, double H) Region => (_regionX, _regionY, _regionW, _regionH);

    /// <summary>Pixel dimensions of the current region's frame.</summary>
    public (int W, int H) RegionPixels => ((int)Math.Round(_regionW * _scale), (int)Math.Round(_regionH * _scale));

    /// <summary>
    /// Reads the window frame (points, global) + display scale and starts the
    /// stream on <paramref name="bandW"/>x<paramref name="bandH"/> at the
    /// window's top-left corner. Cheap after the first call: the geometry is
    /// cached until <see cref="RefreshGeometry"/> says otherwise.
    /// </summary>
    public bool Start(int bandW = DefaultBandWidth, int bandH = DefaultBandHeight)
    {
        if (!NativeAvailable || _pid <= 0) return false;
        if (!RefreshGeometry()) return false;
        var w = Math.Min(bandW, Math.Max(64, _windowSize.Width));
        var h = Math.Min(bandH, Math.Max(16, _windowSize.Height));
        var code = MdcNative.Start((uint)_pid, 0, 0, w, h, _fps);
        if (code != MdcNative.ErrOk)
        {
            Fail(code);
            return false;
        }
        _regionX = 0; _regionY = 0; _regionW = w; _regionH = h;
        _started = true;
        _lastError = MdcNative.ErrOk;
        _lastErrorText = "";
        return true;
    }

    public void Stop()
    {
        if (!_started) return;
        try { MdcNative.Stop(); } catch { /* best effort */ }
        _started = false;
    }

    /// <summary>Re-reads the window frame + display scale (1 s throttle).</summary>
    public bool RefreshGeometry(bool force = false)
    {
        var now = Environment.TickCount64;
        if (!force && _windowSize.Width > 0 && now - _lastGeomMs < 1000) return true;
        _lastGeomMs = now;
        if (NativeMac.WowBoundsRaw() is not { } b) return false;
        _windowOrigin = new Point((int)Math.Round(b.X), (int)Math.Round(b.Y));
        _windowSize = new Size((int)Math.Round(b.W), (int)Math.Round(b.H));
        _scale = DisplayScale(b.X, b.Y, b.W, b.H);
        return _windowSize.Width > 0 && _windowSize.Height > 0;
    }

    private long _lastGeomMs;
    private double _pendingX, _pendingY, _pendingW, _pendingH;
    private bool _hasPendingRegion;

    /// <summary>
    /// Queues a region change for the host's capture supervisor. The engine
    /// tick must never block on a capture syscall, so Sample/Locate only ever
    /// REQUEST a region here; <see cref="ApplyPendingRegion"/> performs it off
    /// the tick.
    /// </summary>
    public void RequestRegion(double x, double y, double w, double h)
    {
        if (!_started) return;
        _pendingX = x; _pendingY = y; _pendingW = w; _pendingH = h;
        _hasPendingRegion = true;
    }

    public bool HasPendingRegion => _hasPendingRegion;

    /// <summary>
    /// Applies a queued region change (blocking; supervisor thread only).
    /// Returns true when a reconfigure was attempted successfully.
    /// </summary>
    public bool ApplyPendingRegion()
    {
        if (!_started || !_hasPendingRegion) return false;
        _hasPendingRegion = false;
        return SetRegion(_pendingX, _pendingY, _pendingW, _pendingH);
    }

    /// <summary>Skip reason of the last delivered frame (diagnostics).</summary>
    public string LastSkipReason => MdcNative.LastSkip();

    /// <summary>
    /// Cheap check (CGWindowList metadata, no capture) that the cached window
    /// frame still matches reality — the stream has to be rebuilt after a
    /// resolution switch or a window move.
    /// </summary>
    public bool GeometryMatchesWindow()
    {
        if (_windowSize.Width <= 0 || _windowSize.Height <= 0) return false;
        if (NativeMac.WowBoundsRaw() is not { } b) return false;
        return (int)Math.Round(b.X) == _windowOrigin.X
            && (int)Math.Round(b.Y) == _windowOrigin.Y
            && (int)Math.Round(b.W) == _windowSize.Width
            && (int)Math.Round(b.H) == _windowSize.Height;
    }

    /// <summary>Pixels per point for the display holding the given global rect.</summary>
    /// <summary>Pixels per point for the display holding the given global rect.</summary>
    public static double DisplayScaleFor(double x, double y, double w, double h) => DisplayScale(x, y, w, h);

    /// <summary>
    /// Pixels per point for a window rect. Mirrors the native rule exactly
    /// (maximum backing scale over the displays the rect intersects), because
    /// the two values drive the same region -> pixel mapping: a mismatch on a
    /// window straddling displays of different scale would offset every sample.
    /// </summary>
    private static double DisplayScale(double x, double y, double w, double h)
    {
        try
        {
            var ids = new uint[8];
            if (CGGetActiveDisplayList(8, ids, out var count) != 0) return 1;
            double best = 1;
            for (var i = 0; i < count; i++)
            {
                var b = CGDisplayBounds(ids[i]);
                if (b.Width <= 0 || b.Height <= 0) continue;
                var intersects = x < b.X + b.Width && x + w > b.X && y < b.Y + b.Height && y + h > b.Y;
                if (!intersects) continue;
                var px = (double)CGDisplayPixelsWide(ids[i]);
                if (px <= 0) continue;
                best = Math.Max(best, Math.Max(1, Math.Round(px / b.Width * 2) / 2));
            }
            return best;
        }
        catch { /* fall through: 1:1 is the safe assumption */ }
        return 1;
    }

    /// <summary>
    /// Reconfigures the stream to a window-relative rect (points). Shrinking
    /// to the discovered strip keeps every later frame tiny; a wider rect is
    /// only used while (re)locating.
    /// </summary>
    public bool SetRegion(double x, double y, double w, double h)
    {
        if (!_started) return false;
        var clamp = ClampToWindow(x, y, w, h);
        var code = MdcNative.SetRegion(clamp.X, clamp.Y, clamp.W, clamp.H);
        if (code != MdcNative.ErrOk)
        {
            Fail(code);
            return false;
        }
        _regionX = clamp.X; _regionY = clamp.Y; _regionW = clamp.W; _regionH = clamp.H;
        return true;
    }

    /// <summary>Window-relative point rect clamped to the window bounds.</summary>
    public (double X, double Y, double W, double H) ClampToWindow(double x, double y, double w, double h)
    {
        var x0 = Math.Max(0, x);
        var y0 = Math.Max(0, y);
        var x1 = Math.Min(_windowSize.Width, x + w);
        var y1 = Math.Min(_windowSize.Height, y + h);
        if (x1 - x0 < 8) x1 = Math.Min(_windowSize.Width, x0 + 8);
        if (y1 - y0 < 8) y1 = Math.Min(_windowSize.Height, y0 + 8);
        return (x0, y0, Math.Max(8, x1 - x0), Math.Max(8, y1 - y0));
    }

    /// <summary>Newest published frame (pointer valid for reading; verify with <see cref="StillCurrent"/>).</summary>
    public bool TryFrame(out CaptureFrame frame)
    {
        frame = default;
        if (!_started) return false;
        try
        {
            var code = MdcNative.Snapshot(out var seq, out var ptr, out var w, out var h, out var stride);
            if (code != MdcNative.ErrOk || ptr == IntPtr.Zero) return false;
            frame = new CaptureFrame(ptr, w, h, stride, seq);
            FramesSeen = seq;
            return true;
        }
        catch (Exception ex)
        {
            _lastErrorText = ex.Message;
            _started = false;
            return false;
        }
    }

    /// <summary>True while the frame we read is still the published one (no torn read).</summary>
    public bool StillCurrent(ulong seq)
    {
        try
        {
            return MdcNative.Snapshot(out var now, out _, out _, out _, out _) == MdcNative.ErrOk && now == seq;
        }
        catch { return false; }
    }

    public long FrameAgeMs
    {
        get
        {
            try { return MdcNative.FrameAgeMs(); }
            catch { return -1; }
        }
    }

    public (ulong Delivered, ulong Skipped, int Fps) Stats()
    {
        try
        {
            // Out params cannot be discarded in a single call site.
            var code = MdcNative.Stats(out var d, out var s, out var f);
            return code == MdcNative.ErrOk ? (d, s, f) : (0, 0, 0);
        }
        catch { return (0, 0, 0); }
    }

    public bool StreamDead
    {
        get
        {
            try { return MdcNative.Dead() == 1; }
            catch { return true; }
        }
    }

    private void Fail(int code)
    {
        _lastError = code;
        _lastErrorText = MdcNative.Describe(code) + " " + MdcNative.LastError();
    }

    /// <summary>
    /// One-shot capture of a window-relative rect. Blocking; callers run it on
    /// a worker, never on the engine tick. Returns BGRA bytes + dims + stride.
    /// </summary>
    public bool TryOnceCapture(double x, double y, double w, double h, out byte[] bgra, out int pxW, out int pxH, out int stride)
    {
        bgra = Array.Empty<byte>();
        pxW = pxH = stride = 0;
        if (!NativeAvailable || _pid <= 0) return false;
        if (!RefreshGeometry()) return false;
        var clamp = ClampToWindow(x, y, w, h);
        var wPx = (int)Math.Round(clamp.W * _scale);
        var hPx = (int)Math.Round(clamp.H * _scale);
        // The native one-shot packs rows (stride = width*4), so the buffer can
        // be sized from the point dimensions with a small allowance.
        var cap = wPx * hPx * 4 + 1024;
        if (cap <= 1024) return false;
        var buf = new byte[cap];
        var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try
        {
            var code = MdcNative.Once((uint)_pid, clamp.X, clamp.Y, clamp.W, clamp.H,
                handle.AddrOfPinnedObject(), cap, out pxW, out pxH, out stride);
            if (code != MdcNative.ErrOk)
            {
                Fail(code);
                return false;
            }
            bgra = buf;
            return true;
        }
        catch (Exception ex)
        {
            _lastErrorText = ex.Message;
            return false;
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>True while this process's app is the frontmost app (focus gate).</summary>
    public static bool GameIsFrontmost(int pid)
    {
        if (!MdcNative.AvailableSafe || pid <= 0) return false;
        try { return MdcNative.FrontmostPid() == pid; }
        catch { return false; }
    }

    /// <summary>
    /// Debug helper: hex RGBA of the first <paramref name="count"/> pixels of a
    /// frame row, straight out of the published buffer (probe diagnostics only).
    /// </summary>
    public unsafe string RawRowDump(int row, int count)
    {
        if (!TryFrame(out var frame) || !frame.Valid) return "no frame";
        if (row < 0 || row >= frame.Height) return "row out of range";
        var n = Math.Min(count, frame.Width);
        var sb = new System.Text.StringBuilder(n * 12);
        var p = (byte*)frame.Ptr + (long)row * frame.Stride;
        for (var x = 0; x < n; x++)
        {
            var q = p + x * 4;
            sb.Append(q[2].ToString("x2")).Append(q[1].ToString("x2")).Append(q[0].ToString("x2")).Append(' ');
        }
        return $"seq={frame.Seq} row={row} {frame.Width}x{frame.Height} stride={frame.Stride}: {sb}";
    }

    public void Dispose() => Stop();
}
