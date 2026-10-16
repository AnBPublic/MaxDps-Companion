using System.Drawing;
using System.Runtime.InteropServices;

namespace MaxDpsCompanion.Mac;

// Debug aid: writes one PNG of whatever CGWindowListCreateImage actually
// returns for the client area, so a "no strip" sweep can be inspected
// directly (blank = permission/scale problem; game visible but no magenta
// = strip position/addon problem). Probe-only; never in the send path.

internal static class MacDebugShot
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ImageIO =
        "/System/Library/Frameworks/ImageIO.framework/ImageIO";

    private const int kCGWindowListOptionOnScreenOnly = 1 << 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct CgRect
    {
        public double X, Y, Width, Height;
    }

    [DllImport(CoreGraphics, EntryPoint = "CGWindowListCreateImage")]
    private static extern IntPtr CGWindowListCreateImage(CgRect rect, int listOption, uint windowID, int imageOption);

    [DllImport(CoreGraphics, EntryPoint = "CGImageRelease")]
    private static extern void CGImageRelease(IntPtr image);

    [DllImport(ImageIO, EntryPoint = "CGImageDestinationCreateWithURL")]
    private static extern IntPtr CGImageDestinationCreateWithURL(IntPtr url, IntPtr type, nuint count, IntPtr options);

    [DllImport(ImageIO, EntryPoint = "CGImageDestinationAddImage")]
    private static extern void CGImageDestinationAddImage(IntPtr dest, IntPtr image, IntPtr properties);

    [DllImport(ImageIO, EntryPoint = "CGImageDestinationFinalize")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGImageDestinationFinalize(IntPtr dest);

    [DllImport(CoreFoundation, EntryPoint = "CFURLCreateFromFileSystemRepresentation")]
    private static extern IntPtr CFURLCreateFromFileSystemRepresentation(IntPtr alloc, byte[] buf, nint len, bool isDir);

    [DllImport(CoreFoundation, EntryPoint = "CFStringCreateWithCString")]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, int encoding);

    [DllImport(CoreFoundation, EntryPoint = "CFRelease")]
    private static extern void CFRelease(IntPtr obj);

    public static string Capture(Point origin, Size size)
    {
        try
        {
            var w = Math.Min(size.Width, 1920);
            var h = Math.Min(size.Height, 1080);
            if (w <= 0 || h <= 0) return "skip (empty area)";
            var rect = new CgRect { X = origin.X, Y = origin.Y, Width = w, Height = h };
            var image = CGWindowListCreateImage(rect, kCGWindowListOptionOnScreenOnly, 0, 0);
            if (image == IntPtr.Zero) return "failed (null image — Screen Recording likely denied)";
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library/Application Support/MaxDpsCompanion");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "debugshot.png");
                var bytes = System.Text.Encoding.UTF8.GetBytes(path);
                var url = CFURLCreateFromFileSystemRepresentation(IntPtr.Zero, bytes, bytes.Length, false);
                if (url == IntPtr.Zero) return "failed (url)";
                try
                {
                    var type = CFStringCreateWithCString(IntPtr.Zero, "public.png", 0x08000100);
                    if (type == IntPtr.Zero) return "failed (type)";
                    try
                    {
                        var dest = CGImageDestinationCreateWithURL(url, type, 1, IntPtr.Zero);
                        if (dest == IntPtr.Zero) return "failed (dest)";
                        try
                        {
                            CGImageDestinationAddImage(dest, image, IntPtr.Zero);
                            return CGImageDestinationFinalize(dest)
                                ? path
                                : "failed (finalize)";
                        }
                        finally { CFRelease(dest); }
                    }
                    finally { CFRelease(type); }
                }
                finally { CFRelease(url); }
            }
            finally { CGImageRelease(image); }
        }
        catch (Exception ex)
        {
            return $"failed ({ex.GetType().Name})";
        }
    }
}
