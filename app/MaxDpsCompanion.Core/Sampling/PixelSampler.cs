using System.Drawing;

namespace MaxDpsCompanion;

// Pure per-cell median sampler extracted from ScreenSampler (ScreenSampler.cs
// SampleCell/MedianSampleStatic/Median5/CentrePixel). No GDI, no BitBlt, no
// IntPtr: the caller supplies a pixel reader, so this compiles and
// unit-tests on any OS.
//
// Rule preserved byte-for-byte: the Aethys-proven 5-tap median (centre +
// 4-neighbourhood, clamped inside the cell) at/above MedianMinCellSize kills
// HDR dither and tolerates 1px misalignment at 8px cells; below it the taps
// would bleed into the neighbouring cell, so the exact centre pixel wins.

/// <summary>Pure per-cell median sampler; reader-supplied, no screen access.</summary>
internal static class PixelSampler
{
    /// <summary>Samples taken per cell at 2px+; the median wins. Must stay odd.</summary>
    public const int TapsPerCell = 5;

    /// <summary>
    /// Cell size at/above which the 5-tap centre+4-neighbourhood median is
    /// used. Below this the taps would bleed into the neighbouring cell, so
    /// the exact centre pixel is read instead. Must stay 2.
    /// </summary>
    public const int MedianMinCellSize = 2;

    /// <summary>
    /// Recommended sampler cadence in ms. The bridge repaints its strip every
    /// 33 ms; polling faster only re-reads identical frames, slower adds
    /// input latency. Floor, not a hard rate.
    /// </summary>
    public const int RecommendedPollIntervalMs = 33;

    /// <summary>
    /// v5-family capture widths, widest first: Ext3 (43), Ext2 (40), v5 core
    /// (35). Literals here: Core P0a has no PixelProtocol yet — P0b wires the
    /// real PixelProtocol.CellCountExt3/CellCountExt2/CellCount constants to
    /// these positions.
    /// </summary>
    public static readonly int[] CaptureWidths = { 43, 40, 35 };

    /// <summary>
    /// Median colour of cell <paramref name="cell"/> read through
    /// <paramref name="read"/> (strip-local coordinates). 5-tap median at
    /// 2px+, exact centre pixel below.
    /// </summary>
    public static Color SampleCell(Func<int, int, Color> read, int cell, int cellSize) =>
        cellSize < MedianMinCellSize ? CentrePixel(read, cell, cellSize) : MedianSample(read, cell, cellSize);

    /// <summary>Exact centre pixel of cell <paramref name="cell"/> (the sub-2px fallback).</summary>
    public static Color CentrePixel(Func<int, int, Color> read, int cell, int cellSize)
    {
        var cx = cell * cellSize + cellSize / 2;
        var cy = cellSize / 2;
        return read(cx, cy);
    }

    private static Color MedianSample(Func<int, int, Color> read, int cell, int cellSize)
    {
        // Centre plus 4-neighbourhood, clamped inside the cell.
        var cx = cell * cellSize + cellSize / 2;
        var cy = cellSize / 2;
        var t0 = read(cx, cy);
        var t1 = read(Math.Max(cell * cellSize, cx - 1), cy);
        var t2 = read(Math.Min(cell * cellSize + cellSize - 1, cx + 1), cy);
        var t3 = read(cx, Math.Max(0, cy - 1));
        var t4 = read(cx, Math.Min(cellSize - 1, cy + 1));
        var r = new[] { t0.R, t1.R, t2.R, t3.R, t4.R };
        var g = new[] { t0.G, t1.G, t2.G, t3.G, t4.G };
        var b = new[] { t0.B, t1.B, t2.B, t3.B, t4.B };
        return Color.FromArgb(Median5(r), Median5(g), Median5(b));
    }

    /// <summary>
    /// Median of exactly <see cref="TapsPerCell"/> values, via insertion
    /// sort (median = index 2). Sorts in place; no LINQ, no GC.
    /// </summary>
    public static byte Median5(byte[] v)
    {
        for (var i = 1; i < TapsPerCell; i++)
        {
            var key = v[i];
            var j = i - 1;
            while (j >= 0 && v[j] > key) { v[j + 1] = v[j]; j--; }
            v[j + 1] = key;
        }
        return v[TapsPerCell / 2];
    }

    /// <summary>
    /// Samples <paramref name="cellCount"/> cells on a FRACTIONAL grid: cell
    /// i spans <c>[trunc(step*i), trunc(step*(i+1))-1]</c> with its centre at
    /// the middle, so an 11.5px step is tracked exactly instead of drifting.
    /// The game renders at an integer UI scale but the physical cell size is
    /// fractional, and boundaries fall on the truncation (verified live:
    /// step 11.5 gives runs 11,12,11,12...). 5-tap median within the cell's
    /// own span; centre pixel when the span is below MedianMinCellSize.
    /// </summary>
    public static Color[] SampleGrid(Func<int, int, Color> read, int cellCount, double step, int height)
    {
        var colors = new Color[cellCount];
        if (step <= 0) return colors;
        var span = Math.Max(1, (int)step);
        for (var i = 0; i < cellCount; i++)
        {
            var lo = (int)(step * i);
            var hi = (int)(step * (i + 1)) - 1;
            if (hi < lo) hi = lo;
            colors[i] = SampleSpan(read, lo, hi, height, span);
        }
        return colors;
    }

    /// <summary>Median colour of one cell span [lo,hi] at the row centre.</summary>
    private static Color SampleSpan(Func<int, int, Color> read, int lo, int hi, int height, int span)
    {
        var cx = (lo + hi) / 2;
        var cy = Math.Min(height / 2, Math.Max(0, height - 1));
        if (span < MedianMinCellSize) return read(cx, cy);

        // 5-tap centre + 4-neighbourhood, clamped inside the cell span.
        var t0 = read(cx, cy);
        var t1 = read(Math.Max(lo, cx - 1), cy);
        var t2 = read(Math.Min(hi, cx + 1), cy);
        var t3 = read(cx, Math.Max(0, cy - 1));
        var t4 = read(cx, Math.Min(height - 1, cy + 1));
        var r = new[] { t0.R, t1.R, t2.R, t3.R, t4.R };
        var g = new[] { t0.G, t1.G, t2.G, t3.G, t4.G };
        var b = new[] { t0.B, t1.B, t2.B, t3.B, t4.B };
        return Color.FromArgb(Median5(r), Median5(g), Median5(b));
    }

    /// <summary>
    /// Length of the leading magenta run on a row (the magic cell's rendered
    /// width). The true fractional step s satisfies <c>trunc(s) == run</c>,
    /// so it lies in [run, run+1) — the caller searches that range and lets
    /// Decode's checksum pick the exact value.
    /// </summary>
    public static int MagicRun(Func<int, int, Color> read, int y, int limit, int max = 64)
    {
        var run = 0;
        while (run < limit && run < max && IsMagentaAt(read, run, y)) run++;
        return run;
    }

    private static bool IsMagentaAt(Func<int, int, Color> read, int x, int y)
    {
        var c = read(x, y);
        return c.R + c.B > c.G * 2 + 32 && c.R >= 48 && c.B >= 48;
    }
}
