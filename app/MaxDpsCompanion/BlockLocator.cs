using System.Drawing.Imaging;

namespace MaxDpsCompanion;

internal readonly record struct BlockLocation(int OffsetX, int OffsetY, int CellSize);

/// <summary>
/// Sweeps the game's client area for the addon's magenta magic cell, then measures
/// the cell size off the pixels themselves. This removes every reason to hand-align
/// offsets, and copes with the client rendering at a different resolution than the
/// desktop, with UI scale changes, and with the strip having been moved by /mdb offset.
/// </summary>
internal static class BlockLocator
{
    // Aethys values: 1px cells admit every magenta-ish pixel as a candidate.
    private const int MinCellSize = 2;
    private const int MaxCellSize = 32;

    /// <summary>
    /// Hard cap on the swept rectangle. A per-monitor-DPI change or a stale
    /// client rect can hand us an absurd area; refusing it prevents a giant
    /// CopyFromScreen allocation instead of thrashing the box.
    /// </summary>
    private const int MaxCaptureEdge = 8192;

    /// <summary>
    /// At least this fraction of the requested rectangle must lie on the
    /// virtual screen. A DPI/scale change can leave the cached client origin
    /// mostly off-screen; a mostly-offscreen sweep is a coordinate-scale
    /// artifact, not a hidden strip, so it fails fast (the engine then falls
    /// back to Relocate on the next tick). Clamping alone would silently sweep
    /// the wrong pixels.
    /// </summary>
    private const double MinOnScreenFraction = 0.25;

    /// <summary>
    /// Scans <paramref name="area"/> (screen coordinates) and returns the block's
    /// position relative to <paramref name="origin"/>, or null if it is not there.
    /// </summary>
    public static BlockLocation? Locate(Point origin, Size area) =>
        Locate(origin, area, profile: null);

    /// <summary>
    /// Profile-aware sweep: the learned magic reference replaces the fixed
    /// contrast test, and Verify decodes with the profile's per-channel ramps.
    /// </summary>
    public static BlockLocation? Locate(Point origin, Size area, ColorProfile? profile)
    {
        if (area.Width <= 0 || area.Height <= 0) return null;
        if (area.Width > MaxCaptureEdge || area.Height > MaxCaptureEdge) return null;

        // DPI / scale guard: the capture rect must lie (mostly) on the virtual
        // screen at the origin we were handed. A monitor/DPI change can shift
        // the client origin; sweeping the resulting strip of desktop would
        // either find a false magenta or wipe a huge allocation.
        if (!TryClampCapture(origin, area, SystemInformation.VirtualScreen,
                out var captureOrigin, out var captureArea))
            return null;

        using var bitmap = new Bitmap(captureArea.Width, captureArea.Height, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            try
            {
                graphics.CopyFromScreen(captureOrigin, Point.Empty, captureArea, CopyPixelOperation.SourceCopy);
            }
            catch (Exception)
            {
                return null;
            }
        }

        var data = bitmap.LockBits(new Rectangle(Point.Empty, captureArea), ImageLockMode.ReadOnly,
            PixelFormat.Format32bppRgb);
        try
        {
            // Scan offset is relative to the capture rect, which is the same
            // origin when the guard clamped nothing (the common case).
            var offsetX = captureOrigin.X - origin.X;
            var offsetY = captureOrigin.Y - origin.Y;
            var found = Scan(data, captureArea, profile);
            return found is { } location && location.OffsetX >= 0 && location.OffsetY >= 0
                ? location with { OffsetX = location.OffsetX + offsetX, OffsetY = location.OffsetY + offsetY }
                : found;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>
    /// Clamps a requested capture rectangle to <paramref name="bounds"/> and
    /// rejects a mostly-offscreen request (see <see cref="MinOnScreenFraction"/>).
    /// Pure and testable: no GDI, no screen.
    /// </summary>
    internal static bool TryClampCapture(Point origin, Size area, Rectangle bounds,
        out Point captureOrigin, out Size captureArea)
    {
        captureOrigin = origin;
        captureArea = area;
        if (area.Width <= 0 || area.Height <= 0
            || area.Width > MaxCaptureEdge || area.Height > MaxCaptureEdge)
            return false;
        if (bounds.Width <= 0 || bounds.Height <= 0) return false;

        var left = Math.Max(origin.X, bounds.Left);
        var top = Math.Max(origin.Y, bounds.Top);
        var right = Math.Min(origin.X + area.Width, bounds.Right);
        var bottom = Math.Min(origin.Y + area.Height, bounds.Bottom);
        var visibleW = right - left;
        var visibleH = bottom - top;
        if (visibleW <= 0 || visibleH <= 0) return false;

        var requested = (double)area.Width * area.Height;
        var visible = (double)visibleW * visibleH;
        if (requested > 0 && visible / requested < MinOnScreenFraction) return false;

        captureOrigin = new Point(left, top);
        captureArea = new Size(visibleW, visibleH);
        return true;
    }

    private static unsafe BlockLocation? Scan(BitmapData data, Size area, ColorProfile? profile)
    {
        var scan = (byte*)data.Scan0;
        var stride = data.Stride;

        for (var y = 0; y < area.Height; y++)
        {
            for (var x = 0; x < area.Width; x++)
            {
                if (!IsMagenta(scan, stride, x, y, profile)) continue;

                // Only consider the top-left corner of a magenta run, so a wide
                // cell does not produce one candidate per pixel.
                if (x > 0 && IsMagenta(scan, stride, x - 1, y, profile)) continue;
                if (y > 0 && IsMagenta(scan, stride, x, y - 1, profile)) continue;

                var width = RunLength(scan, stride, x, y, 1, 0, area.Width - x, profile);
                var height = RunLength(scan, stride, x, y, 0, 1, area.Height - y, profile);

                // The strip is a single horizontal ROW of cells, so the magic
                // cell's VERTICAL run is its true size. The horizontal run can
                // bleed into a following slot cell: under the legacy (unlearned)
                // gate a slot colour like (51,34,136) also satisfies the loose
                // magenta test, so the run reads 2x the cell. Rejecting any
                // width!=height blob therefore threw away the real strip and
                // produced "no pixel block" on a freshly calibrated/legacy
                // install. Use the smaller run as the size and only reject
                // wildly non-square blobs; Verify (strict Decode/Classify)
                // rejects genuine false positives.
                var size = Math.Min(width, height);
                if (size < MinCellSize || size > MaxCellSize) continue;
                if (Math.Max(width, height) > size * 4) continue;

                if (Verify(scan, stride, area, x, y, size, profile))
                    return new BlockLocation(x, y, size);
            }
        }

        return null;
    }

    /// <summary>
    /// Confirms the candidate really is our strip by decoding all cells.
    /// Tries v5 (35 cells) first, then v4 (9 cells) and v1 (8 cells, older
    /// stale addon): the sweep must find a stale strip too, or recalibrate
    /// can never repair the version skew it is meant to diagnose.
    /// </summary>
    private static unsafe bool Verify(byte* scan, int stride, Size area, int x, int y, int size, ColorProfile? profile)
    {
        var centre = size / 2;
        var statusY = Math.Min(y + centre, area.Height - 1);

        Color[] ReadWindow(int count)
        {
            var cells = new Color[count];
            for (var i = 0; i < count; i++)
            {
                var px = Math.Min(x + size * i + centre, area.Width - 1);
                cells[i] = Read(scan, stride, px, statusY);
            }
            return cells;
        }

        // v5/Ext2 window (current addon, 40 cells). Decode validates via magic
        // + both checksums; a clipped read fails there. A 35-cell stale addon
        // reads its extra cells as background and decodes through the same path.
        var current = ReadWindow(PixelProtocol.CellCountExt2);
        if (PixelProtocol.Decode(current, profile) is not null) return true;
        if (ColorLearner.Classify(current, profile) >= 0) return true;

        // v4 window (stale addon): 9 cells starting at the same magic cell.
        var v4 = ReadWindow(PixelProtocol.CellCountV4);
        if (PixelProtocol.Decode(v4, profile) is not null) return true;
        if (ColorLearner.Classify(v4, profile) >= 0) return true;

        // v1 window (older stale addon): 8 cells.
        var v1 = ReadWindow(PixelProtocol.CellCountV1);
        if (PixelProtocol.Decode(v1, profile) is not null) return true;
        return ColorLearner.Classify(v1, profile) >= 0;
    }

    private static unsafe int RunLength(byte* scan, int stride, int x, int y, int dx, int dy, int limit, ColorProfile? profile)
    {
        var length = 0;
        while (length < limit && length <= MaxCellSize
               && IsMagenta(scan, stride, x + dx * length, y + dy * length, profile))
        {
            length++;
        }
        return length;
    }

    private static unsafe bool IsMagenta(byte* scan, int stride, int x, int y, ColorProfile? profile)
    {
        var pixel = scan + (long)y * stride + (long)x * 4;
        // 32bppRgb is stored B, G, R, unused. Candidate gate, deliberately
        // looser than either decoder: the sweep must find the strip through
        // an unlearned ReShade shift, and Verify (strict Decode or pattern
        // Classify) rejects false positives. A learned profile still matches
        // by distance first.
        var color = Color.FromArgb(pixel[2], pixel[1], pixel[0]);
        if (profile is { IsLearned: true })
            return PixelProtocol.IsMagic(color, profile);
        return color.R + color.B > color.G * 2 + 32 && color.R >= 48 && color.B >= 48;
    }

    private static unsafe Color Read(byte* scan, int stride, int x, int y)
    {
        var pixel = scan + (long)y * stride + (long)x * 4;
        return Color.FromArgb(pixel[2], pixel[1], pixel[0]);
    }
}
