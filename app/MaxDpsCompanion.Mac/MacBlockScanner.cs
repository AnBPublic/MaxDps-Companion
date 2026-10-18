using System.Drawing;

namespace MaxDpsCompanion.Mac;

// IBlockScanner for the Mac host: find the addon's pixel strip inside the
// game window.
//
// WINDOW-RELATIVE + SCREEN-AGNOSTIC: every scan is expressed as a rect inside
// the WoW window (points), captured through the window-filtered stream or a
// window one-shot. Nothing depends on which display the game is on, the
// resolution, or the display scale — the addon anchors the strip to the
// window's top-left corner, so the same window-relative band finds it on the
// 1080p monitor and on the MacBook panel alike.
//
// COST MODEL (hard goal: never disturb the game or the tick)
//   1. The band (top-left corner: 2048 x 400 pt, clamped to the window) is
//      already the stream's start region, so the common case is a read of the
//      newest frame + a scan that returns on the first verified candidate —
//      microseconds when the strip is where the addon put it.
//   2. Only when the band misses does the ladder escalate (20% of the window,
//      then the full window) through blocking one-shots. That is throttled by
//      the engine to at most once every 5 s and only while the strip is
//      missing, and the rung is remembered so repeated failures stay cheap.
//   3. After a hit the stream shrinks to the strip rect, so steady-state
//      capture is a few KB per frame.
//
// Results are reported in POINTS (the engine's coordinate space): offsets are
// window-relative, cell size is the addon's UI size (pixels / display scale).
internal sealed class MacBlockScanner : IBlockScanner
{
    private volatile MacCaptureStream? _stream;
    private readonly ColorProfile? _profile;
    private readonly MacFrameReader _reader = new();

    private int _rung;                  // 0 = micro band, 1 = wide band, 2 = 20%-tall, 3 = full window
    private long _lastHitMs;
    private bool _escalationRequested;
    private (int OffsetX, int OffsetY, int CellSize)? _cachedHit;

    public MacBlockScanner(MacCaptureStream? stream = null, ColorProfile? profile = null)
    {
        _stream = stream;
        _profile = profile;
    }

    /// <summary>Current capture stream (swapped by the host on a capture restart).</summary>
    public MacCaptureStream? Stream
    {
        get => _stream;
        set
        {
            _stream = value;
            _rung = 0;             // a new stream starts on the small band again
            _cachedHit = null;
            _escalationRequested = false;
        }
    }

    public (int OffsetX, int OffsetY, int CellSize)? Locate(Point origin, Size area)
    {
        if (area.Width <= 0 || area.Height <= 0) return null;

        if (_stream is { Running: true } stream && stream.RefreshGeometry())
        {
            // 0. A hit the supervisor's escalation found is authoritative (and
            //    consumed exactly once).
            if (_cachedHit is { } cached)
            {
                _cachedHit = null;
                return cached;
            }

            // 1. Newest stream frame: already captured, torn-safe read, the
            //    normal case costs microseconds.
            if (stream.TryFrame(out var frame) && frame.Valid
                && ScanFrame(stream, frame) is { } hit)
            {
                return Accept(stream, hit.CellPx, hit.XPx, hit.YPx, origin.X - stream.WindowOrigin.X, origin.Y - stream.WindowOrigin.Y);
            }

            // 2. Miss: hand the escalation ladder to the host's capture
            //    supervisor. One-shot captures take 20-60 ms each and must
            //    never run on the engine tick.
            _escalationRequested = true;
            return null;
        }
        return LocateLegacy(origin, area);
    }

    /// <summary>
    /// Supervisor-side escalation: run the one-shot ladder and cache a hit.
    /// Blocking by design (SUPERVISOR THREAD ONLY - never call from the tick).
    /// </summary>
    public bool ServiceEscalation(bool force = false)
    {
        if (!force && !_escalationRequested) return false;
        _escalationRequested = false;
        var stream = _stream;
        if (stream is not { Running: true } || !stream.RefreshGeometry()) return false;

        var size = stream.WindowSize;
        for (var rung = _rung; rung <= 3; rung++)
        {
            var (rw, rh) = RungRect(rung, size.Width, size.Height);
            if (!stream.TryOnceCapture(0, 0, rw, rh, out var bgra, out var iw, out var ih, out var stride))
                return false;
            _reader.Bind(bgra, iw, ih, stride);
            var found = BlockScanner.Scan(_reader.ReadBgra, iw, ih, _profile, (x, y, cell) => Verify(iw, ih, x, y, cell));
            if (found is { } f)
            {
                var hit = Accept(stream, f.CellSize, f.X, f.Y, 0, 0, onSupervisor: true);   // one-shots are window-relative
                if (hit is { } h)
                {
                    _cachedHit = h;
                    return true;
                }
                return false;
            }
            _rung = Math.Min(3, rung + 1);
        }
        return false;
    }

    /// <summary>Marks that the next supervisor pass should escalate (engine saw a miss).</summary>
    public void RequestEscalation() => _escalationRequested = true;

    /// <summary>
    /// Escalation ladder, smallest first: micro band (1024x128 pt), wide band
    /// (2048x400 pt), 20%-tall band, full window. Each rung is only used while
    /// the strip has not been found; after a hit the stream shrinks back to the
    /// strip rect itself.
    /// </summary>
    private static (int W, int H) RungRect(int rung, int winW, int winH)
    {
        var w = Math.Max(64, winW);
        var h = Math.Max(64, winH);
        return rung switch
        {
            0 => (Math.Min(w, MacCaptureStream.DefaultBandWidth), Math.Min(h, MacCaptureStream.DefaultBandHeight)),
            1 => (Math.Min(w, MacCaptureStream.WideBandWidth), Math.Min(h, MacCaptureStream.WideBandHeight)),
            2 => (Math.Min(w, MacCaptureStream.WideBandWidth), Math.Min(h, Math.Max(MacCaptureStream.WideBandHeight, h / 5))),
            _ => (w, h),
        };
    }

    /// <summary>
    /// Converts a pixel hit into engine coordinates (window-relative points),
    /// queues the shrink-to-strip region and returns the hit.
    /// OFF-TICK RULE: the engine tick only ever REQUESTS the region
    /// (<see cref="MacCaptureStream.RequestRegion"/>); the capture supervisor
    /// applies it (250 ms cadence). The supervisor's own escalation passes
    /// <paramref name="onSupervisor"/> = true and applies it inline, since it
    /// is already off the tick.
    /// </summary>
    private (int, int, int)? Accept(MacCaptureStream stream, int cellPx, int xPx, int yPx, double clientRelX, double clientRelY, bool onSupervisor = false)
    {
        var scale = stream.Scale <= 0 ? 1 : stream.Scale;
        var cellPt = Math.Max(1, (int)Math.Round(cellPx / scale));
        var xPt = (int)Math.Round(xPx / scale + clientRelX);
        var yPt = (int)Math.Round(yPx / scale + clientRelY);

        // Shrink the stream to exactly the strip (+2 pt tap margin) so every
        // later tick reads a few KB instead of the band. Never a blocking
        // reconfigure on the tick: queue it for the supervisor.
        // WIDTH = Ext4 (51), not Ext3 (43): the sampler requests 51 cells, and
        // its "covered" test needs `relX + 51*cellSize <= region`. A 43-cell
        // region crops the Ext4 block AND fails that test on every tick, which
        // turns into a permanent region request and empty frames (the mirror
        // never publishes). The region is the widest frame we decode; a 43/40/
        // 35-cell addon simply leaves the tail as background.
        var margin = 2;
        stream.RequestRegion(xPt - margin, yPt - margin,
            cellPt * PixelProtocol.CellCountExt4 + margin * 2, cellPt + margin * 2);
        if (onSupervisor) stream.ApplyPendingRegion();
        _rung = 0;                          // next loss starts cheap again
        _lastHitMs = Environment.TickCount64;
        return (xPt, yPt, cellPt);
    }

    /// <summary>Scans a stream frame (frame-local pixels).</summary>
    private (int XPx, int YPx, int CellPx)? ScanFrame(MacCaptureStream stream, CaptureFrame frame)
    {
        _reader.Bind(frame);
        var found = BlockScanner.Scan(_reader.ReadBgra, frame.Width, frame.Height, _profile, (x, y, cell) => Verify(frame.Width, frame.Height, x, y, cell));
        // Never accept a candidate read across a buffer swap.
        if (!stream.StillCurrent(frame.Seq)) return null;
        if (found is not { } f) return null;
        return (f.X, f.Y, f.CellSize);
    }

    /// <summary>
    /// v5-family window widths, widest first: Ext4 (51), Ext3 (43), Ext2 (40)
    /// and the v5 core (35) — the same ladder the Windows BlockLocator uses.
    /// PixelSampler.CaptureWidths stops at Ext3 (it is the locator's own list),
    /// so the host keeps its own copy here; the acceptance rule must be able
    /// to verify the 51-cell frame it now samples.
    /// </summary>
    private static readonly int[] V5WindowWidths =
    {
        PixelProtocol.CellCountExt4,
        PixelProtocol.CellCountExt3,
        PixelProtocol.CellCountExt2,
        PixelProtocol.CellCount,
    };

    /// <summary>
    /// Strict acceptance: a candidate counts only when a width from the v5
    /// family (51/43/40/35) decodes or the calibrate pattern classifies, in the
    /// integer-grid window and in the fractional-step variants the sampler
    /// uses. Identical acceptance to the Windows BlockLocator port.
    /// </summary>
    private bool Verify(int iw, int ih, int x, int y, int size)
    {
        Color Read(int px, int py)
        {
            if ((uint)px >= (uint)iw || (uint)py >= (uint)ih) return Color.Empty;
            return _reader.Read(px, py);
        }

        var centre = size / 2;
        var statusY = Math.Min(y + centre, ih - 1);

        Color[] ReadWindow(int count)
        {
            var cells = new Color[count];
            for (var i = 0; i < count; i++)
                cells[i] = Read(Math.Min(x + size * i + centre, iw - 1), statusY);
            return cells;
        }

        Color[] ReadFractional(int count, double step)
        {
            var cells = new Color[count];
            for (var i = 0; i < count; i++)
                cells[i] = Read(Math.Min(x + (int)(step * i) + (int)(step / 2), iw - 1), statusY);
            return cells;
        }

        foreach (var width in V5WindowWidths)
        {
            var window = ReadWindow(width);
            if (PixelProtocol.Decode(window, _profile) is not null) return true;
            if (ColorLearner.Classify(window, _profile) >= 0) return true;
            foreach (var f in new[] { 0.5, 0.25, 0.75, 0.99 })
            {
                var frac = ReadFractional(width, size + f);
                if (PixelProtocol.Decode(frac, _profile) is not null) return true;
                if (ColorLearner.Classify(frac, _profile) >= 0) return true;
            }
        }
        var v4 = ReadWindow(PixelProtocol.CellCountV4);
        if (PixelProtocol.Decode(v4, _profile) is not null) return true;
        if (ColorLearner.Classify(v4, _profile) >= 0) return true;
        var v1 = ReadWindow(PixelProtocol.CellCountV1);
        if (PixelProtocol.Decode(v1, _profile) is not null) return true;
        return ColorLearner.Classify(v1, _profile) >= 0;
    }

    /// <summary>Fallback path (no stream): one legacy client-area sweep.</summary>
    private (int, int, int)? LocateLegacy(Point origin, Size area)
    {
        // The legacy API returns device pixels for a point rect; sweep the
        // top-left band first (cheap) and only then the whole client area.
        foreach (var (w, h) in new[]
        {
            (Math.Min(area.Width, MacCaptureStream.DefaultBandWidth), Math.Min(area.Height, MacCaptureStream.DefaultBandHeight)),
            (area.Width, area.Height),
        })
        {
            if (!MacLegacyCapture.TryCapture(origin, new Size(Math.Max(1, w), Math.Max(1, h)),
                    out var bgra, out var iw, out var ih, out var stride))
                continue;
            _reader.Bind(bgra, iw, ih, stride);
            var found = BlockScanner.Scan(_reader.ReadBgra, iw, ih, _profile, (x, y, cell) => Verify(iw, ih, x, y, cell));
            if (found is { } f) return (f.X, f.Y, f.CellSize);
        }
        return null;
    }

    /// <summary>Milliseconds since the last successful locate (diagnostics).</summary>
    public long MsSinceLastHit => _lastHitMs == 0 ? -1 : Environment.TickCount64 - _lastHitMs;
}
