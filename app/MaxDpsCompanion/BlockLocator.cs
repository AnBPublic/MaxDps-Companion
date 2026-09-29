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

        using var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            try
            {
                graphics.CopyFromScreen(origin, Point.Empty, area, CopyPixelOperation.SourceCopy);
            }
            catch (Exception)
            {
                return null;
            }
        }

        var data = bitmap.LockBits(new Rectangle(Point.Empty, area), ImageLockMode.ReadOnly,
            PixelFormat.Format32bppRgb);
        try
        {
            return Scan(data, area, profile);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
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

        // v5 window (current addon). Decode validates via magic + both
        // checksums; a clipped read fails there.
        var current = ReadWindow(PixelProtocol.CellCount);
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
