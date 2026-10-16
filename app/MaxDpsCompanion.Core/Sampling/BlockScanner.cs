// P1 port of the Windows BlockLocator sweep core (BlockLocator.cs Scan /
// Verify / RunLength / IsMagenta / Read + TryClampCapture), operating on a
// caller-supplied BGRA reader instead of a GDI Bitmap. No Bitmap, no
// Graphics, no CopyFromScreen, no SystemInformation: the Mac host captures
// via CGWindowListCreateImage and feeds bytes here.
//
// Logic deltas vs Windows (documented, reviewable):
// - Scan walks (x, y, readPixel) instead of (BitmapData scan, stride).
// - Verify reads centre pixels through the same reader (same windows,
//   same widths, same Decode/Classify acceptance).
// - Locate orchestration (clamp + capture + offset) lives in the Mac host
//   (MacBlockScanner), which owns the CGImage lifetime; this file is the
//   pure scan/verify core only.

namespace MaxDpsCompanion;

/// <summary>Portable strip-sweep core; reader-supplied, no screen access.</summary>
internal static class BlockScanner
{
    public const int MinCellSize = 2;
    public const int MaxCellSize = 32;
    public const int MaxCaptureEdge = 8192;
    public const double MinOnScreenFraction = 0.25;

    /// <summary>BGRA reader in capture-local coordinates (B, G, R order).</summary>
    public delegate void ReadPixel(int x, int y, out byte b, out byte g, out byte r);

    public readonly record struct Found(int X, int Y, int CellSize);

    /// <summary>
    /// Clamps a requested capture rectangle to <paramref name="bounds"/> and
    /// rejects a mostly-offscreen request. Pure and testable: no capture.
    /// </summary>
    public static bool TryClampCapture(
        int originX, int originY, int areaW, int areaH,
        int boundsX, int boundsY, int boundsW, int boundsH,
        out int capX, out int capY, out int capW, out int capH)
    {
        capX = originX; capY = originY; capW = areaW; capH = areaH;
        if (areaW <= 0 || areaH <= 0 || areaW > MaxCaptureEdge || areaH > MaxCaptureEdge)
            return false;
        if (boundsW <= 0 || boundsH <= 0) return false;

        var left = Math.Max(originX, boundsX);
        var top = Math.Max(originY, boundsY);
        var right = Math.Min(originX + areaW, boundsX + boundsW);
        var bottom = Math.Min(originY + areaH, boundsY + boundsH);
        var visibleW = right - left;
        var visibleH = bottom - top;
        if (visibleW <= 0 || visibleH <= 0) return false;

        var requested = (double)areaW * areaH;
        var visible = (double)visibleW * visibleH;
        if (requested > 0 && visible / requested < MinOnScreenFraction) return false;

        capX = left; capY = top; capW = visibleW; capH = visibleH;
        return true;
    }

    /// <summary>
    /// Sweeps the capture for the magic cell; returns strip-local top-left +
    /// cell size. <paramref name="verify"/> confirms a candidate (strict
    /// Decode/Classify); kept as a callback so this core never references
    /// PixelProtocol/ColorLearner directly.
    /// </summary>
    public static Found? Scan(
        ReadPixel read, int areaW, int areaH,
        ColorProfile? profile, Func<int, int, int, bool> verify)
    {
        for (var y = 0; y < areaH; y++)
        {
            for (var x = 0; x < areaW; x++)
            {
                if (!IsMagenta(read, x, y, profile)) continue;
                if (x > 0 && IsMagenta(read, x - 1, y, profile)) continue;
                if (y > 0 && IsMagenta(read, x, y - 1, profile)) continue;

                var width = RunLength(read, x, y, 1, 0, areaW - x, profile);
                var height = RunLength(read, x, y, 0, 1, areaH - y, profile);
                var size = Math.Min(width, height);
                if (size < MinCellSize || size > MaxCellSize) continue;
                if (Math.Max(width, height) > size * 4) continue;

                if (verify(x, y, size))
                    return new Found(x, y, size);
            }
        }
        return null;
    }

    public static int RunLength(ReadPixel read, int x, int y, int dx, int dy, int limit, ColorProfile? profile)
    {
        var length = 0;
        while (length < limit && length <= MaxCellSize
               && IsMagenta(read, x + dx * length, y + dy * length, profile))
        {
            length++;
        }
        return length;
    }

    public static bool IsMagenta(ReadPixel read, int x, int y, ColorProfile? profile)
    {
        read(x, y, out var b, out var g, out var r);
        var color = System.Drawing.Color.FromArgb(r, g, b);
        if (profile is { IsLearned: true })
            return PixelProtocol.IsMagic(color, profile);
        return color.R + color.B > color.G * 2 + 32 && color.R >= 48 && color.B >= 48;
    }
}
