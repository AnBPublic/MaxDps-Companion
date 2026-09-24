namespace MaxDpsCompanion;

/// <summary>
/// Copies the addon's pixel strip off the screen with GDI and returns the
/// median colour of each cell. Aethys-proven 5-tap median (centre +
/// 4-neighbourhood): kills HDR dither, tolerates 1px misalignment at 8px
/// cells. For 1px cells (legacy min) the median would bleed into
/// neighbours, so those fall back to the exact centre pixel.
///
/// PERF (v1.3.9): DIB-section capture. One BitBlt fills a memory-mapped
/// buffer and every tap is read straight from the pointer. Before this the
/// sampler issued 45 <c>GetPixel</c> syscalls per tick (9 cells x 5 taps)
/// at up to 20-100 Hz — a needless CPU burst on the same machine that is
/// rendering the game. There are also no more per-tap LINQ allocations
/// (was 27 arrays/tick of managed garbage, i.e. GC pressure = frametime
/// spikes in WoW).
/// </summary>
internal sealed class ScreenSampler : IDisposable
{
    private IntPtr _screenDc = IntPtr.Zero;
    private IntPtr _memoryDc = IntPtr.Zero;
    private IntPtr _bitmap = IntPtr.Zero;
    private IntPtr _previous = IntPtr.Zero;
    private IntPtr _bits = IntPtr.Zero;
    private Size _bitmapSize;

    /// <summary>Samples taken per cell at 2px+; the median wins. Must stay odd.</summary>
    public const int TapsPerCell = 5;

    // Reused tap buffer (single-threaded sampler): zero per-tick garbage.
    private readonly byte[] _tapR = new byte[TapsPerCell];
    private readonly byte[] _tapG = new byte[TapsPerCell];
    private readonly byte[] _tapB = new byte[TapsPerCell];

    public Color[] Sample(Point origin, int cellSize) =>
        SampleCells(origin, cellSize, (x, y) => ReadPixel(x, y));

    /// <summary>
    /// Samples a full client-area bitmap reader (used by the calibrate-pattern
    /// learner) with the same per-cell rule.
    /// </summary>
    public static Color[] SampleRegion(Func<int, int, Color> read, int blockX, int blockY, int cellSize)
    {
        var colors = new Color[PixelProtocol.CellCount];
        for (var i = 0; i < PixelProtocol.CellCount; i++)
            colors[i] = SampleCell((x, y) => read(blockX + x, blockY + y), i, cellSize);
        return colors;
    }

    public static Color[] SampleRegionV1(Func<int, int, Color> read, int blockX, int blockY, int cellSize)
    {
        var colors = new Color[PixelProtocol.CellCountV1];
        for (var i = 0; i < PixelProtocol.CellCountV1; i++)
            colors[i] = SampleCell((x, y) => read(blockX + x, blockY + y), i, cellSize);
        return colors;
    }

    /// <summary>v1-width capture (8 cells) for the stale-addon fallback.</summary>
    public Color[] SampleV1(Point origin, int cellSize) =>
        SampleCells(origin, cellSize, PixelProtocol.CellCountV1, (x, y) => ReadPixel(x, y));

    private Color[] SampleCells(Point origin, int cellSize, Func<int, int, Color> read) =>
        SampleCells(origin, cellSize, PixelProtocol.CellCount, read);

    private Color[] SampleCells(Point origin, int cellSize, int cellCount, Func<int, int, Color> read)
    {
        var width = cellSize * cellCount;
        var height = cellSize;

        EnsureSurface(width, height);

        var colors = new Color[cellCount];
        if (_bits == IntPtr.Zero) return colors;

        if (!Native.BitBlt(_memoryDc, 0, 0, width, height, _screenDc, origin.X, origin.Y,
                Native.SRCCOPY | Native.CAPTUREBLT))
        {
            return colors;
        }

        for (var i = 0; i < cellCount; i++)
            colors[i] = SampleCellInstance((x, y) => ReadPixel(x, y), i, cellSize);
        return colors;
    }

    private Color SampleCellInstance(Func<int, int, Color> read, int cell, int cellSize) =>
        cellSize < 2 ? CentrePixel(read, cell, cellSize) : MedianSample(read, cell, cellSize);

    private static Color SampleCell(Func<int, int, Color> read, int cell, int cellSize) =>
        cellSize < 2 ? CentrePixel(read, cell, cellSize) : MedianSampleStatic(read, cell, cellSize);

    /// <summary>Allocating variant for the static region paths (learner).</summary>
    private static Color MedianSampleStatic(Func<int, int, Color> read, int cell, int cellSize)
    {
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
        Array.Sort(r); Array.Sort(g); Array.Sort(b);
        return Color.FromArgb(r[2], g[2], b[2]);
    }

    private Color MedianSample(Func<int, int, Color> read, int cell, int cellSize)
    {
        // Centre plus 4-neighbourhood, clamped inside the cell.
        var cx = cell * cellSize + cellSize / 2;
        var cy = cellSize / 2;
        _tapR[0] = read(cx, cy).R; _tapG[0] = read(cx, cy).G; _tapB[0] = read(cx, cy).B;
        var l = Math.Max(cell * cellSize, cx - 1);
        var r = Math.Min(cell * cellSize + cellSize - 1, cx + 1);
        var u = Math.Max(0, cy - 1);
        var d = Math.Min(cellSize - 1, cy + 1);
        var c1 = read(l, cy); _tapR[1] = c1.R; _tapG[1] = c1.G; _tapB[1] = c1.B;
        var c2 = read(r, cy); _tapR[2] = c2.R; _tapG[2] = c2.G; _tapB[2] = c2.B;
        var c3 = read(cx, u); _tapR[3] = c3.R; _tapG[3] = c3.G; _tapB[3] = c3.B;
        var c4 = read(cx, d); _tapR[4] = c4.R; _tapG[4] = c4.G; _tapB[4] = c4.B;

        // Reused buffers + insertion sort for 5 elements: no allocation.
        return Color.FromArgb(Median5(_tapR), Median5(_tapG), Median5(_tapB));
    }

    private static byte Median5(byte[] v)
    {
        // Insertion sort of five values (median = index 2). No LINQ, no GC.
        for (var i = 1; i < TapsPerCell; i++)
        {
            var key = v[i];
            var j = i - 1;
            while (j >= 0 && v[j] > key) { v[j + 1] = v[j]; j--; }
            v[j + 1] = key;
        }
        return v[TapsPerCell / 2];
    }

    private static Color CentrePixel(Func<int, int, Color> read, int cell, int cellSize)
    {
        var cx = cell * cellSize + cellSize / 2;
        var cy = cellSize / 2;
        return read(cx, cy);
    }

    /// <summary>Reads a pixel directly from the mapped DIB buffer (no GDI call).</summary>
    private Color ReadPixel(int x, int y)
    {
        if (_bits == IntPtr.Zero) return Color.Empty;
        unsafe
        {
            var p = (byte*)_bits + (long)y * (_bitmapSize.Width * 4) + (long)x * 4;
            return Color.FromArgb(p[2], p[1], p[0]);   // B, G, R, unused
        }
    }

    private void EnsureSurface(int width, int height)
    {
        if (_screenDc == IntPtr.Zero)
            _screenDc = Native.GetDC(IntPtr.Zero);

        if (_bitmap != IntPtr.Zero && _bitmapSize.Width == width && _bitmapSize.Height == height)
            return;

        ReleaseSurface();

        _memoryDc = Native.CreateCompatibleDC(_screenDc);
        var header = new Native.BITMAPINFOHEADER
        {
            biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height,           // top-down rows: y grows downward
            biPlanes = 1,
            biBitCount = 32,
            biCompression = Native.BI_RGB,
        };
        _bitmap = Native.CreateDIBSection(_screenDc, ref header, Native.DIB_RGB_COLORS, out _bits, IntPtr.Zero, 0);
        if (_bitmap == IntPtr.Zero)
        {
            // Fallback: plain compatible bitmap (ReadPixel degrades safely
            // to Empty via _bits == 0 — the strip just reads as not found).
            _bitmap = Native.CreateCompatibleBitmap(_screenDc, width, height);
            _bits = IntPtr.Zero;
        }
        _previous = Native.SelectObject(_memoryDc, _bitmap);
        _bitmapSize = new Size(width, height);
    }

    private void ReleaseSurface()
    {
        if (_memoryDc != IntPtr.Zero && _previous != IntPtr.Zero)
            Native.SelectObject(_memoryDc, _previous);
        if (_bitmap != IntPtr.Zero)
            Native.DeleteObject(_bitmap);
        if (_memoryDc != IntPtr.Zero)
            Native.DeleteDC(_memoryDc);

        _bitmap = _memoryDc = _previous = _bits = IntPtr.Zero;
        _bitmapSize = Size.Empty;
    }

    public void Dispose()
    {
        ReleaseSurface();
        if (_screenDc != IntPtr.Zero)
        {
            Native.ReleaseDC(IntPtr.Zero, _screenDc);
            _screenDc = IntPtr.Zero;
        }
    }
}
