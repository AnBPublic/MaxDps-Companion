namespace MaxDpsCompanion;

/// <summary>
/// Copies the addon's pixel strip off the screen with GDI and returns the
/// colour of each cell. At 1px cells a multi-tap median would bleed into
/// neighbouring cells, so the sampler reads the exact cell centre pixel;
/// HDR dither is rejected downstream by the nibble quantiser + checksum
/// (a dithered read fails the checksum and the frame is dropped, not
/// mis-sent). For larger cells the centre pixel is still the most stable
/// point — edges catch UI-scale blending.
/// </summary>
internal sealed class ScreenSampler : IDisposable
{
    private IntPtr _screenDc = IntPtr.Zero;
    private IntPtr _memoryDc = IntPtr.Zero;
    private IntPtr _bitmap = IntPtr.Zero;
    private IntPtr _previous = IntPtr.Zero;
    private Size _bitmapSize;

    public Color[] Sample(Point origin, int cellSize) =>
        SampleCells(origin, cellSize, (x, y) => ReadPixel(x, y));

    /// <summary>
    /// Samples a full client-area bitmap reader (used by the calibrate-pattern
    /// learner) with the same centre-pixel rule.
    /// </summary>
    public static Color[] SampleRegion(Func<int, int, Color> read, int blockX, int blockY, int cellSize)
    {
        var colors = new Color[PixelProtocol.CellCount];
        for (var i = 0; i < PixelProtocol.CellCount; i++)
            colors[i] = CentrePixel((x, y) => read(blockX + x, blockY + y), i, cellSize);
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
            colors[i] = CentrePixel((x, y) => ReadPixel(x, y), i, cellSize);
        return colors;
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
