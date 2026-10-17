using System.Drawing;
using System.Runtime.InteropServices;

namespace MaxDpsCompanion.Mac;

// LEGACY capture path: CGWindowListCreateImage one-shots.
//
// Kept ONLY as a fallback for hosts where the native ScreenCaptureKit library
// is missing or refused (macOS < 12.3, or a build without libmdc_capture).
// The API was obsoleted in macOS 15 and is slow (a blocking window-server
// round trip of 20-60 ms per call), which is exactly why the primary path is
// the persistent SCK stream. Behaviour is unchanged from the original MVP
// port: same pixel strides, same byte-order detection, same Y handling.
internal static class MacLegacyCapture
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const int kCGWindowListOptionOnScreenOnly = 1 << 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct CgRect
    {
        public double X, Y, Width, Height;
    }

    [DllImport(CoreGraphics, EntryPoint = "CGWindowListCreateImage")]
    private static extern IntPtr CGWindowListCreateImage(CgRect rect, int listOption, uint windowID, int imageOption);

    [DllImport(CoreGraphics, EntryPoint = "CGImageGetWidth")]
    private static extern nuint CGImageGetWidth(IntPtr image);

    [DllImport(CoreGraphics, EntryPoint = "CGImageGetHeight")]
    private static extern nuint CGImageGetHeight(IntPtr image);

    [DllImport(CoreGraphics, EntryPoint = "CGImageGetBytesPerRow")]
    private static extern nuint CGImageGetBytesPerRow(IntPtr image);

    [DllImport(CoreGraphics, EntryPoint = "CGImageGetBitmapInfo")]
    private static extern uint CGImageGetBitmapInfo(IntPtr image);

    [DllImport(CoreGraphics, EntryPoint = "CGImageGetDataProvider")]
    private static extern IntPtr CGImageGetDataProvider(IntPtr image);

    [DllImport(CoreGraphics, EntryPoint = "CGImageRelease")]
    private static extern void CGImageRelease(IntPtr image);

    [DllImport(CoreGraphics, EntryPoint = "CGDataProviderCopyData")]
    private static extern IntPtr CGDataProviderCopyData(IntPtr provider);

    [DllImport(CoreFoundation, EntryPoint = "CFDataGetBytePtr")]
    private static extern IntPtr CFDataGetBytePtr(IntPtr data);

    [DllImport(CoreFoundation, EntryPoint = "CFDataGetLength")]
    private static extern nint CFDataGetLength(IntPtr data);

    [DllImport(CoreFoundation, EntryPoint = "CFRelease")]
    private static extern void CFRelease(IntPtr obj);

    /// <summary>Byte offsets of B/G/R inside a pixel for a given CG bitmap info.</summary>
    internal static bool TryLayout(uint info, out int b, out int g, out int r)
    {
        var alpha = info & 0x1Fu;
        var order = info & 0x7000u;
        if (order == 0x2000) { b = 0; g = 1; r = 2; return true; }        // BGRA (little-endian flag)
        if (order == 0x1000) { b = 1; g = 2; r = 3; return true; }        // ARGB (big-endian flag)
        if (order == 0 && alpha is 1 or 2 or 3) { b = 2; g = 1; r = 0; return true; } // R,G,B,A / R,G,B,X
        b = g = r = 0;
        return false;
    }

    /// <summary>Copies the given global-points rect into a fresh BGRA byte array.</summary>
    internal static bool TryCapture(Point origin, Size size, out byte[] bgra, out int w, out int h, out int stride)
    {
        bgra = Array.Empty<byte>();
        w = h = stride = 0;
        if (size.Width <= 0 || size.Height <= 0) return false;
        if (size.Width > BlockScanner.MaxCaptureEdge || size.Height > BlockScanner.MaxCaptureEdge) return false;

        var rect = new CgRect { X = origin.X, Y = origin.Y, Width = size.Width, Height = size.Height };
        var image = CGWindowListCreateImage(rect, kCGWindowListOptionOnScreenOnly, 0, 0);
        if (image == IntPtr.Zero) return false;
        try
        {
            var iw = (int)CGImageGetWidth(image);
            var ih = (int)CGImageGetHeight(image);
            var s = (int)CGImageGetBytesPerRow(image);
            var info = CGImageGetBitmapInfo(image);
            if (iw < size.Width || ih < size.Height || s <= 0) return false;
            if (!TryLayout(info, out var bo, out var go, out var ro)) return false;

            var provider = CGImageGetDataProvider(image);
            if (provider == IntPtr.Zero) return false;
            var data = CGDataProviderCopyData(provider);
            if (data == IntPtr.Zero) return false;
            try
            {
                var len = (int)CFDataGetLength(data);
                if (len < s * ih) return false;
                var ptr = CFDataGetBytePtr(data);
                if (ptr == IntPtr.Zero) return false;

                // Normalise to a packed BGRA buffer so both capture paths feed
                // the identical reader.
                var buf = new byte[iw * ih * 4];
                unsafe
                {
                    var src = (byte*)ptr;
                    for (var y = 0; y < ih; y++)
                    {
                        var rowSrc = src + (long)y * s;
                        var rowDst = (long)y * iw * 4;
                        for (var x = 0; x < iw; x++)
                        {
                            var p = rowSrc + (long)x * 4;
                            var q = rowDst + (long)x * 4;
                            buf[q + 0] = p[bo];
                            buf[q + 1] = p[go];
                            buf[q + 2] = p[ro];
                            buf[q + 3] = 255;
                        }
                    }
                }
                bgra = buf;
                w = iw; h = ih; stride = iw * 4;
                return true;
            }
            finally { CFRelease(data); }
        }
        finally { CGImageRelease(image); }
    }
}
