using System.Drawing;

namespace MaxDpsCompanion.Mac;

// IScreenSampler for the Mac host.
//
// PRIMARY PATH: read the newest frame of the persistent ScreenCaptureKit
// stream (window-filtered, region bounded to the strip). Nothing is captured
// here — the tick does a seqlock read of a small BGRA buffer, so the sampler
// has no window-server round trip and cannot stall on the game.
//
// Coordinates: the engine speaks global screen POINTS (top-left origin, the
// same space as CGWindowList bounds); the stream frame is WINDOW-RELATIVE
// DEVICE PIXELS. Everything therefore works unchanged on any monitor: same
// window layout -> same window-relative rect, whatever the resolution or
// display scale. `cellSize` from the engine is in points (the addon's UI
// size); the pixel step is `cellSize * scale`.
//
// FALLBACK PATH: the legacy CGWindowListCreateImage one-shot, used ONLY while
// no stream exists (native library missing, Screen Recording denied, or before
// the supervisor has attached). While a stream is running the tick never falls
// back to it — an uncovered region answers with an empty frame and the host
// reconfigures off the tick.
internal sealed class MacScreenSampler : IScreenSampler
{
    private volatile MacCaptureStream? _stream;
    private readonly MacFrameReader _reader = new();
    private readonly ColorProfile? _profile;

    public MacScreenSampler(MacCaptureStream? stream = null, ColorProfile? profile = null)
    {
        _stream = stream;
        _profile = profile;
    }

    /// <summary>
    /// Frames whose nibbles had to be repaired because the macOS colour
    /// pipeline lifted a channel (see MacPixelRepair). Diagnostics only; a
    /// non-zero value on a correct display means the ICC pipeline is
    /// distorting the strip and the wire is being rescued frame by frame.
    /// </summary>
    public int Repairs { get; private set; }

    /// <summary>Cells of the last sample that decoded only after repair (diagnostics).</summary>
    public int LastRepairCells { get; private set; }

    /// <summary>Last grid computation + the raw row it sampled (diagnostics).</summary>
    public string Trace { get; private set; } = "";

    /// <summary>
    /// Builds <see cref="Trace"/> (string concatenation per sample). OFF for
    /// the engine tick; probe/diagnose turn it on. Tick purity: no per-sample
    /// string allocation unless somebody is actually reading the trace.
    /// </summary>
    public static bool Diagnostics { get; set; }

    /// <summary>
    /// Current capture stream. The engine keeps ONE adapter instance for its
    /// lifetime, so the host swaps the stream here when it restarts capture
    /// (game restart, stream error) instead of rewiring the engine.
    /// </summary>
    public MacCaptureStream? Stream
    {
        get => _stream;
        set => _stream = value;
    }

    public (int Mask, int Epoch, int Blocked)? Ext3Mirror { get; private set; }
    public bool Ext3MirrorPresent { get; private set; }

    /// <summary>True while samples come from the SCK stream (probe/diagnostics).</summary>
    public bool UsingStream => _stream is { Running: true };

    // Ext5 (4.6.0): capture the widest frame the protocol understands (58
    // cells) so the additive insurance/landing cells are readable, exactly like
    // the Windows sampler. The width comes from the ONE shared ladder
    // (PixelProtocol.V5Widths[0], currently Ext5) rather than a local literal:
    // this host once pinned itself to the locator's first entry (43) and made
    // Ext4 unreachable, and a hardcoded 51 would do the same to Ext5.
    public Color[] Sample(Point origin, int cellSize) =>
        SampleCells(origin, cellSize, PixelProtocol.V5Widths[0]);

    public Color[] SampleV1(Point origin, int cellSize) =>
        SampleCells(origin, cellSize, 8);

    private Color[] SampleCells(Point origin, int cellSize, int cellCount)
    {
        if (cellSize <= 0) return new Color[cellCount];
        Color[] cells;
        if (_stream is { Running: true } stream)
        {
            // Stream mode: never touch the (slow, blocking) legacy capture on
            // the tick path. A region that has to grow answers with an empty
            // frame for this one tick; the next tick reads the grown region.
            cells = SampleFromStream(stream, origin, cellSize, cellCount) ?? new Color[cellCount];
        }
        else
        {
            cells = SampleLegacy(origin, cellSize, cellCount);
        }

        // macOS colour-pipeline repair (no-op when the frame decodes as-is).
        // Gate on the WIDEST DECODABLE FRAME, not on one exact width: the
        // extensions are additive (cells 0-42 are the Ext3 frame, 43-50 the
        // Ext4 block, 51-57 the Ext5 block, each gated on its own presence
        // bit), so any width >= Ext3 can carry the Ext3 mirror and needs the
        // same repair. An exact `== CellCountExt3` test silently disabled BOTH
        // once Sample() grew to 51 cells.
        if (cellCount >= PixelProtocol.CellCountExt3)
        {
            LastRepairCells = 0;
            if (MacPixelRepair.Repair(cells, _profile, cellCount) is { } repaired)
            {
                Repairs++;
                LastRepairCells = CountDifferences(cells, repaired, cellCount);
                cells = repaired;
            }
            // The Ext3 mirror is published from ONE decode per tick, on both
            // the stream and the legacy path (it used to be legacy-only, so
            // the stream mode reported "no mirror" forever).
            UpdateMirror(cells);
        }
        return cells;
    }

    private static int CountDifferences(Color[] a, Color[] b, int cellCount)
    {
        var n = 0;
        for (var i = 0; i < cellCount; i++) if (a[i] != b[i]) n++;
        return n;
    }

    /// <summary>
    /// Reads the request straight out of the newest stream frame. Returns null
    /// when the region has to be (re)configured — the caller falls back to the
    /// old capture for this tick so the engine never loses a frame.
    /// </summary>
    private Color[]? SampleFromStream(MacCaptureStream stream, Point origin, int cellSize, int cellCount)
    {
        if (!stream.RefreshGeometry()) return null;
        var scale = stream.Scale;
        var win = stream.WindowOrigin;

        // Strip rect in window-relative points + a 2-point tap margin.
        const int margin = 2;
        var relX = origin.X - win.X;
        var relY = origin.Y - win.Y;
        var wantX = relX - margin;
        var wantY = relY - margin;
        var wantW = cellSize * cellCount + margin * 2;
        var wantH = cellSize + margin * 2;

        var (rx, ry, rw, rh) = stream.Region;
        var covered = relX >= rx && relY >= ry
            && relX + cellSize * cellCount <= rx + rw
            && relY + cellSize <= ry + rh;

        if (!covered)
        {
            // Ask for exactly what the engine asked for; the host's capture
            // supervisor performs the reconfigure OFF the tick (the engine
            // retries next tick). Never block here: a tick must not wait on a
            // capture syscall.
            stream.RequestRegion(wantX, wantY, wantW, wantH);
            return null;
        }

        var cells = ReadCells(stream, relX, relY, (int)Math.Round((relX - rx) * scale), (int)Math.Round((relY - ry) * scale),
            cellSize * scale, cellCount);
        if (cells is not null && IsBlank(cells)) return null;   // pre-paint frame: not data
        return cells;
    }

    private Color[]? ReadCells(MacCaptureStream stream, double relX, double relY, int px, int py, double step, int cellCount)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (!stream.TryFrame(out var frame)) return null;
            if (!frame.Valid) return null;
            if (px < 0 || py < 0 || px >= frame.Width || py >= frame.Height) return null;

            _reader.Bind(frame);
            var colors = SampleGridFromReader(_reader.Read, frame, px, py, step, cellCount);
            if (stream.StillCurrent(frame.Seq)) return colors;
            // Torn read (a new frame landed mid-read): retry on the fresh one.
        }
        return null;
    }

    /// <summary>
    /// Fractional-grid sample over the bound frame (same rule as the shared
    /// PixelSampler, but reading the frame directly: strip-local (x,y) here
    /// are frame-local pixel coordinates).
    /// </summary>
    private Color[] SampleGridFromReader(Func<int, int, Color> read, CaptureFrame frame, int px, int py, double step, int cellCount)
    {
        Color At(int x, int y) => read(px + x, py + y);
        var limitX = frame.Width - px;
        var limitY = frame.Height - py;
        var run = PixelSampler.MagicRun(At, y: 1, limit: limitX);
        var baseStep = run > 0 ? run : Math.Max(1, (int)Math.Round(step));
        var height = Math.Min((int)Math.Max(1, step), limitY);
        if (Diagnostics)
        {
            // Diagnostic-only string building (probe/diagnose paths). Never on
            // the engine tick: it allocates several strings per sample.
            var probeGrid = PixelSampler.SampleGrid(At, Math.Min(cellCount, 6), baseStep + 0.5, height);
            var col = string.Join(",", Enumerable.Range(0, Math.Min(8, limitY)).Select(y => { var c = At(4, y); return $"{c.R}/{c.G}/{c.B}"; }));
            Trace = $"px={px} py={py} run={run} step={baseStep} height={height} grid0=" +
                string.Join(",", probeGrid.Select(c => $"{c.R}/{c.G}/{c.B}")) +
                $" col4=" + col +
                $" row1=" + string.Join(",", new[] { 0, 3, 4, 7, 8, 11, 12 }.Select(x => { var c = At(x, 1); return $"{c.R}/{c.G}/{c.B}"; }));
        }

        foreach (var f in new[] { 0.0, 0.5, 0.25, 0.75, 0.99 })
        {
            var cells = PixelSampler.SampleGrid(At, cellCount, baseStep + f, height);
            if (PixelProtocol.Decode(cells, _profile) is not null) return cells;
        }
        return PixelSampler.SampleGrid(At, cellCount, baseStep + 0.5, height);
    }

    private Color[] SampleLegacy(Point origin, int cellSize, int cellCount)
    {
        var want = new Size(Math.Max(1, cellSize * cellCount), Math.Max(1, cellSize));
        if (!MacLegacyCapture.TryCapture(origin, want, out var bgra, out var iw, out var ih, out var stride))
            return new Color[cellCount];

        _reader.Bind(bgra, iw, ih, stride);
        var cells = SampleGridFromBytes(_reader.Read, iw, ih, cellSize, cellCount);
        // The Ext3 mirror is published by SampleCells (one decode per tick).
        return cells;
    }

    private Color[] SampleGridFromBytes(Func<int, int, Color> read, int iw, int ih, int cellSize, int cellCount)
    {
        var run = PixelSampler.MagicRun(read, y: 1, limit: iw);
        var baseStep = run > 0 ? run : Math.Max(1, cellSize);
        var cells = PixelSampler.SampleGrid(read, cellCount, baseStep + 0.5, ih);
        foreach (var f in new[] { 0.0, 0.5, 0.25, 0.75, 0.99 })
        {
            var candidate = PixelSampler.SampleGrid(read, cellCount, baseStep + f, ih);
            if (PixelProtocol.Decode(candidate, _profile) is not null) return candidate;
        }
        return cells;
    }

    /// <summary>
    /// True when every sampled cell is near-white or near-black: the window had
    /// not painted yet (or is blanked), so the frame carries no wire content.
    /// </summary>
    private static bool IsBlank(Color[] cells)
    {
        if (cells.Length == 0) return false;
        var blank = 0;
        foreach (var c in cells)
        {
            var luma = (c.R + c.G + c.B) / 3;
            if (luma <= 6 || luma >= 249) blank++;
        }
        return blank == cells.Length;
    }

    private void UpdateMirror(Color[] colors)
    {
        try
        {
            var frame = PixelProtocol.Decode(colors, profile: _profile);
            Ext3Mirror = frame?.Ext3 is { } e ? (e.Mask, e.Epoch, e.Blocked) : null;
            Ext3MirrorPresent = frame?.Ext3Present ?? false;
        }
        catch
        {
            Ext3Mirror = null;
            Ext3MirrorPresent = false;
        }
    }

    public void Reset()
    {
        Ext3Mirror = null;
        Ext3MirrorPresent = false;
        _reader.Reset();
        MacPixelRepair.Forget();
    }

    public void Dispose() => Reset();
}

/// <summary>
/// Zero-allocation BGRA reader bound either to a stream frame (native pointer,
/// BGRA byte order per kCVPixelFormatType_32BGRA) or to a legacy byte array.
/// The bound delegate is created once and reused, so the tick allocates
/// nothing.
/// </summary>
internal sealed unsafe class MacFrameReader
{
    private IntPtr _base;
    private byte[]? _array;
    private int _width, _height, _stride;

    public MacFrameReader()
    {
        Read = ReadPixel;
        ReadBgra = ReadPixelRaw;
    }

    public Func<int, int, Color> Read { get; }

    /// <summary>BlockScanner.ReadPixel shape (B, G, R order) for the sweep core.</summary>
    public BlockScanner.ReadPixel ReadBgra { get; }

    public void Bind(CaptureFrame frame) => Bind(frame.Ptr, frame.Width, frame.Height, frame.Stride);

    public void Bind(IntPtr basePtr, int width, int height, int stride)
    {
        _base = basePtr;
        _array = null;
        _width = width; _height = height; _stride = stride;
    }

    public void Bind(byte[] data, int width, int height, int stride)
    {
        _array = data;
        _base = IntPtr.Zero;
        _width = width; _height = height; _stride = stride;
    }

    public void Reset()
    {
        _base = IntPtr.Zero;
        _array = null;
        _width = _height = _stride = 0;
    }

    private void ReadPixelRaw(int x, int y, out byte b, out byte g, out byte r)
    {
        if ((uint)x >= (uint)_width || (uint)y >= (uint)_height) { b = g = r = 0; return; }
        if (_array is { } arr)
        {
            var i = y * _stride + x * 4;
            if (i + 3 < arr.Length) { b = arr[i]; g = arr[i + 1]; r = arr[i + 2]; return; }
            b = g = r = 0;
            return;
        }
        if (_base == IntPtr.Zero) { b = g = r = 0; return; }
        var p = (byte*)_base + (long)y * _stride + (long)x * 4;
        b = p[0]; g = p[1]; r = p[2];
    }

    /// <summary>BGRA byte order (B at +0, G at +1, R at +2) — the SCK 32BGRA layout.</summary>
    private Color ReadPixel(int x, int y)
    {
        if ((uint)x >= (uint)_width || (uint)y >= (uint)_height) return Color.Empty;
        if (_array is { } arr)
        {
            var i = y * _stride + x * 4;
            return i + 3 < arr.Length ? Color.FromArgb(arr[i + 2], arr[i + 1], arr[i]) : Color.Empty;
        }
        if (_base == IntPtr.Zero) return Color.Empty;
        var p = (byte*)_base + (long)y * _stride + (long)x * 4;
        return Color.FromArgb(p[2], p[1], p[0]);
    }
}
