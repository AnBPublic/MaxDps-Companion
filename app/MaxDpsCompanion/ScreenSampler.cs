namespace MaxDpsCompanion;

/// <summary>
/// Copies the addon's pixel strip off the screen with GDI and returns the
/// median colour of each cell. Aethys-proven 5-tap median (centre +
/// 4-neighbourhood): kills HDR dither, tolerates 1px misalignment at 8px
/// cells. For 1px cells (legacy min) the median would bleed into
/// neighbours, so those fall back to the exact centre pixel.
/// </summary>
internal sealed class ScreenSampler : IDisposable
{
    private IntPtr _screenDc = IntPtr.Zero;
    private IntPtr _memoryDc = IntPtr.Zero;
    private IntPtr _bitmap = IntPtr.Zero;
    private IntPtr _previous = IntPtr.Zero;
    private Size _bitmapSize;

    /// <summary>Samples taken per cell at 2px+; the median wins. Must stay odd.</summary>
    public const int TapsPerCell = 5;

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

    private Color[] SampleCells(Point origin, int cellSize, Func<int, int, Color> read)
    {
        var width = cellSize * PixelProtocol.CellCount;
        var height = cellSize;

        EnsureSurface(width, height);

        var colors = new Color[PixelProtocol.CellCount];

        if (!Native.BitBlt(_memoryDc, 0, 0, width, height, _screenDc, origin.X, origin.Y,
                Native.SRCCOPY | Native.CAPTUREBLT))
        {
            return colors;
        }

        for (var i = 0; i < PixelProtocol.CellCount; i++)
            colors[i] = SampleCell((x, y) => ReadPixel(x, y), i, cellSize);
        return colors;
    }

    private static Color SampleCell(Func<int, int, Color> read, int cell, int cellSize) =>
        cellSize < 2 ? CentrePixel(read, cell, cellSize) : MedianSample(read, cell, cellSize);

    private static Color MedianSample(Func<int, int, Color> read, int cell, int cellSize)
    {
        // Centre plus 4-neighbourhood, clamped inside the cell.
        var cx = cell * cellSize + cellSize / 2;
        var cy = cellSize / 2;
        var taps = new Color[TapsPerCell];
        taps[0] = read(cx, cy);
        taps[1] = read(Math.Max(cell * cellSize, cx - 1), cy);
        taps[2] = read(Math.Min(cell * cellSize + cellSize - 1, cx + 1), cy);
        taps[3] = read(cx, Math.Max(0, cy - 1));
        taps[4] = read(cx, Math.Min(cellSize - 1, cy + 1));

        int Median(byte[] values)
        {
            Array.Sort(values);
            return values[values.Length / 2];
        }

        return Color.FromArgb(
            Median(taps.Select(t => t.R).ToArray()),
            Median(taps.Select(t => t.G).ToArray()),
            Median(taps.Select(t => t.B).ToArray()));
    }

    private static Color CentrePixel(Func<int, int, Color> read, int cell, int cellSize)
    {
        var cx = cell * cellSize + cellSize / 2;
        var cy = cellSize / 2;
        return read(cx, cy);
    }

    private Color ReadPixel(int x, int y)
    {
        // GetPixel returns 0x00BBGGRR.
        var raw = Native.GetPixel(_memoryDc, x, y);
        if (raw == 0xFFFFFFFF) return Color.Empty;

        return Color.FromArgb(
            (int)(raw & 0xFF),
            (int)((raw >> 8) & 0xFF),
            (int)((raw >> 16) & 0xFF));
    }

    private void EnsureSurface(int width, int height)
    {
        if (_screenDc == IntPtr.Zero)
            _screenDc = Native.GetDC(IntPtr.Zero);

        if (_bitmap != IntPtr.Zero && _bitmapSize.Width == width && _bitmapSize.Height == height)
            return;

        ReleaseSurface();

        _memoryDc = Native.CreateCompatibleDC(_screenDc);
        _bitmap = Native.CreateCompatibleBitmap(_screenDc, width, height);
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

        _bitmap = _memoryDc = _previous = IntPtr.Zero;
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
