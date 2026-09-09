using System.Drawing.Imaging;

namespace MaxDpsCompanion;

/// <summary>
/// Learns the display-chain colour profile from the addon's `/mdb calibrate`
/// pattern: black step, white step, then per-channel 0..15 ramps. Each step is
/// found by its flat level on slot cells 1-5 while cell 0 holds the magic
/// reference and cell 6 (status) holds the Paused encoding. Median sampling
/// rejects HDR dither.
///
/// ReShade / RenoDX / HDR shift the whole chain (lifted blacks, tinted
/// whites, crosstalk into idle channels), so Classify accepts the pattern by
/// *shape* — slots 1-5 flat and equal, one channel dominant per ramp step —
/// instead of absolute levels. Learn watches the flat level over time and
/// records every distinct level it settles on, so the cycle yields a profile
/// in seconds instead of needing a full pass.
/// </summary>
internal static class ColorLearner
{
    public const int MinSeparation = 48;

    /// <summary>
    /// How long one dwell step lasts on the addon side (Bridge.lua advances
    /// every ~8 ticks at 50 ms/update). The sampler polls faster than this so
    /// each step is seen several times before it changes.
    /// </summary>
    public const int StepDwellMs = 400;

    public sealed record LearnResult(ColorProfile Profile, string Note);

    /// <summary>
    /// Runs the full learn at a known block position. The caller must have put
    /// the addon into calibrate mode and stopped the engine first.
    /// Cooperative cancellation via <paramref name="cancel"/> (polled between
    /// samples); never blocks longer than one sample interval.
    ///
    /// Stability rule: a step is recorded once it reads identically twice in
    /// a row (flat renders are deterministic, so same-class + same-level is
    /// a stable hold, not a transition). Distinct levels are kept — repeats
    /// replace, so black/white re-anchors win over stale values.
    /// Returns as soon as black AND white anchors plus at least one ramp step
    /// per channel are known, without waiting out the deadline.
    /// </summary>
    public static LearnResult? Learn(
        Func<Point, int, Color[]> sample,
        Point block,
        int cellSize,
        ColorProfile? profile = null,
        int tolerance = 64,
        Func<bool>? cancel = null)
    {
        var frames = new List<Color[]>();
        // Full pattern cycle is 54 steps x ~400 ms ≈ 21.6 s; the deadline
        // must cover a worst-case entry point plus margin. Early exit on
        // anchors keeps the typical run to a few seconds.
        var deadline = Environment.TickCount64 + 30_000;
        var lastStep = -1;
        var lastLevel = Color.Empty;
        var stable = 0;

        while (Environment.TickCount64 < deadline && frames.Count < 60)
        {
            if (cancel?.Invoke() == true) return null;
            var cells = sample(block, cellSize);
            if (cells.Length != PixelProtocol.CellCount) return null;
            var step = Classify(cells, profile);
            var level = step >= 0 ? Average(cells, 1, 5) : Color.Empty;
            if (step < 0)
            {
                stable = 0;
                lastStep = -1;
                if (SleepBreak(100, cancel)) return null;
                continue;
            }
            // Same step class and (for ramps) same quantised level: one more
            // steady sighting. A flat render repeats exactly; transitions and
            // dither flicker between classes/levels and reset the streak.
            var sameLevel = step != 2 || (Math.Abs(level.R - lastLevel.R) <= 12
                && Math.Abs(level.G - lastLevel.G) <= 12
                && Math.Abs(level.B - lastLevel.B) <= 12);
            if (step == lastStep && sameLevel && ++stable >= 1)
            {
                stable = 0;
                lastStep = -1;
                AddOrReplace(frames, cells);
                if (HaveAnchors(frames, profile))
                    break;
            }
            else if (step != lastStep || !sameLevel)
            {
                lastStep = step;
                lastLevel = level;
                stable = 0;
            }
            if (SleepBreak(100, cancel)) return null;
        }

        if (cancel?.Invoke() == true) return null;
        return Build(frames, profile, tolerance);
    }

    private static bool SleepBreak(int ms, Func<bool>? cancel)
    {
        if (cancel is null) { Thread.Sleep(ms); return false; }
        var waited = 0;
        while (waited < ms)
        {
            if (cancel()) return true;
            Thread.Sleep(20);
            waited += 20;
        }
        return false;
    }

    /// <summary>
    /// Records a settled frame, replacing a previous frame at the same level
    /// so repeated anchors (black/white render twice per cycle) stay fresh
    /// instead of double-counting.
    /// </summary>
    internal static void AddOrReplace(List<Color[]> frames, Color[] cells)
    {
        var level = Average(cells, 1, 5);
        for (var i = 0; i < frames.Count; i++)
        {
            var other = Average(frames[i], 1, 5);
            if (Math.Abs(other.R - level.R) <= 12
                && Math.Abs(other.G - level.G) <= 12
                && Math.Abs(other.B - level.B) <= 12)
            {
                frames[i] = cells;
                return;
            }
        }
        frames.Add(cells);
    }

    /// <summary>
    /// True once the captured frames pin both anchors and every channel axis:
    /// a near-black flat, a near-white flat, and at least one single-channel
    /// dominant step per channel. Learning can stop there — the rest of the
    /// cycle only refines extremes Build already tracks.
    /// </summary>
    internal static bool HaveAnchors(List<Color[]> frames, ColorProfile? matcher = null)
    {
        var black = false;
        var white = false;
        var r = false;
        var g = false;
        var b = false;
        foreach (var cells in frames)
        {
            if (cells.Length != PixelProtocol.CellCount) continue;
            if (Classify(cells, matcher) < 0) continue;
            var level = Average(cells, 1, 5);
            var brightness = (level.R + level.G + level.B) / 3;
            var channels = new[] { level.R, level.G, level.B };
            Array.Sort(channels);
            var brightest = channels[2];
            var runnerUp = channels[1];
            if (brightest - runnerUp >= ChannelLead)
            {
                if (level.R >= level.G && level.R >= level.B) r = true;
                else if (level.G >= level.R && level.G >= level.B) g = true;
                else b = true;
            }
            else if (brightness <= BlackBrightness) black = true;
            else if (brightness >= WhiteBrightness) white = true;
        }
        return black && white && r && g && b && frames.Count >= 5;
    }

    /// <summary>
    /// Builds a profile from already-captured frames (one per pattern step).
    /// Classifies each frame, picks black/white/channel extremes and the
    /// magic reference, and validates separation.
    ///
    /// Needs only the two anchors plus one dominant step per channel axis
    /// (5+ usable frames): Build takes the darkest flat as black and the
    /// brightest flat as white, so mid-cycle captures learn without waiting
    /// for the exact endpoints.
    /// </summary>
    public static LearnResult? Build(List<Color[]> frames, int tolerance = 64) =>
        Build(frames, matcher: null, tolerance);

    /// <summary>
    /// Profile-aware build: an existing (stale) profile may recognise the
    /// magic cell when the legacy band no longer can.
    /// </summary>
    public static LearnResult? Build(List<Color[]> frames, ColorProfile? matcher, int tolerance = 64)
    {
        Color? black = null;
        Color? white = null;
        Color? magic = null;
        var blackLevel = int.MaxValue;
        var whiteLevel = int.MinValue;
        var rMin = 255; var rMax = 0;
        var gMin = 255; var gMax = 0;
        var bMin = 255; var bMax = 0;
        var flats = 0;

        foreach (var cells in frames)
        {
            if (cells.Length != PixelProtocol.CellCount) continue;
            var step = Classify(cells, matcher);
            if (step < 0) continue;
            flats++;
            magic = cells[0];
            var level = Average(cells, 1, 5);
            var brightness = (level.R + level.G + level.B) / 3;
            // Anchors by brightness, not just exact class: a "black" step
            // through a lifting chain reads grey, a "white" step reads tinted,
            // but darkest-flat and brightest-flat are still the right range
            // endpoints.
            if (step == 0 || brightness < blackLevel) { black = level; blackLevel = brightness; }
            if (step == 1 || brightness > whiteLevel) { white = level; whiteLevel = brightness; }
            rMin = Math.Min(rMin, level.R); rMax = Math.Max(rMax, level.R);
            gMin = Math.Min(gMin, level.G); gMax = Math.Max(gMax, level.G);
            bMin = Math.Min(bMin, level.B); bMax = Math.Max(bMax, level.B);
        }

        if (flats < 5 || black is not { } bk || white is not { } wt || magic is not { } mg)
            return null;
        // Same flat twice is not a profile: require real spread on at least
        // one axis so noise around a single level cannot learn.
        if (rMax - rMin < 24 && gMax - gMin < 24 && bMax - bMin < 24)
            return null;

        var learned = new ColorProfile
        {
            MagicR = mg.R,
            MagicG = mg.G,
            MagicB = mg.B,
            Tolerance = Math.Clamp(tolerance, 16, 128),
            LearnedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
        };
        // Per-channel ranges come from the ramps: the channel that moves
        // defines that axis, the others keep the black/white flats.
        learned.Black[0] = rMin; learned.White[0] = Math.Max(rMax, wt.R);
        learned.Black[1] = gMin; learned.White[1] = Math.Max(gMax, wt.G);
        learned.Black[2] = bMin; learned.White[2] = Math.Max(bMax, wt.B);
        for (var i = 0; i < 3; i++)
            if (learned.White[i] <= learned.Black[i])
                learned.White[i] = Math.Min(255, learned.Black[i] + 1);

        var sep = learned.Separation();
        if (sep < MinSeparation) return null;

        return new LearnResult(learned, $"learned {learned.LearnedAt}, separation {sep}");
    }

    /// <summary>
    /// Classifies a calibrate-pattern frame: 0 = black, 1 = white, 2 = ramp.
    /// Returns -1 when the frame is not a stable pattern step.
    ///
    /// Shape-based so post-processing cannot hide the pattern: slot cells 1-5
    /// must be flat and equal (spread allows HDR dither), the status cell
    /// (index 6) must read Paused, and the verdict comes from channel
    /// dominance, not absolute levels. A ReShade chain lifts blacks towards
    /// grey and tints whites, but a flat is still flat and a lit channel
    /// still leads the other two.
    /// </summary>
    public const int ChannelLead = 16;
    public const int BlackBrightness = 96;
    public const int WhiteBrightness = 140;
    public const int CellSpread = 64;
    public const int AnchorBalance = 48;

    public static int Classify(Color[] cells, ColorProfile? profile = null)
    {
        if (cells.Length != PixelProtocol.CellCount) return -1;
        // Magic cell must still read magenta-ish; a learned profile matches by
        // distance, otherwise a loose dominance band so a shifted chain is
        // still recognisable before any profile exists. Absolute floors are
        // low on purpose: ReShade pulls magenta down and lifts green.
        var m = cells[0];
        if (profile is { IsLearned: true } learned)
        {
            if (!learned.IsMagic(m)) return -1;
        }
        else
        {
            if (m.G > 200) return -1;
            if (m.R < m.G + 16 || m.B < m.G + 16) return -1;
            if (m.R < 48 || m.B < 48) return -1;
        }

        // Calibrate mode holds the bridge Paused: the status cell must say so.
        // Without this gate a live rotation frame with five equal slots would
        // misclassify as a pattern step.
        if (!PixelProtocol.IsPausedStatus(cells[PixelProtocol.StatusCellIndex], m, profile))
            return -1;

        var level = Average(cells, 1, 5);
        var spread = Spread(cells);
        if (spread > CellSpread) return -1;

        // A ramp step lights one channel above the other two, including dark
        // steps whose brightness alone would read as black. Crosstalk lifts
        // idle channels together, so the lead threshold stays low. White is
        // the one flat both bright and balanced (tinted whites keep a loose
        // balance bound); black is whatever flat is dark.
        var channels = new[] { level.R, level.G, level.B };
        Array.Sort(channels);
        var brightest = channels[2];
        var runnerUp = channels[1];
        if (brightest - runnerUp >= ChannelLead) return 2;

        var brightness = (level.R + level.G + level.B) / 3;
        if (brightness <= BlackBrightness) return 0;
        if (brightness >= WhiteBrightness && brightest - runnerUp < AnchorBalance) return 1;
        return -1;
    }

    private static Color Average(Color[] cells, int from, int to)
    {
        var r = 0; var g = 0; var b = 0; var n = 0;
        for (var i = from; i <= to; i++)
        {
            r += cells[i].R; g += cells[i].G; b += cells[i].B; n++;
        }
        return Color.FromArgb(r / n, g / n, b / n);
    }

    private static int Spread(Color[] cells)
    {
        var spread = 0;
        for (var i = 1; i <= 5; i++)
            for (var j = i + 1; j <= 5; j++)
                spread = Math.Max(spread, Math.Max(
                    Math.Abs(cells[i].R - cells[j].R),
                    Math.Max(Math.Abs(cells[i].G - cells[j].G),
                             Math.Abs(cells[i].B - cells[j].B))));
        return spread;
    }

    /// <summary>
    /// Captures the client area once for region-based sampling (used to find
    /// the strip while calibrate mode is on, when the status cell is Paused).
    /// </summary>
    public static Bitmap? CaptureClient(Point origin, Size area)
    {
        if (area.Width <= 0 || area.Height <= 0) return null;
        var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(origin, Point.Empty, area, CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            return null;
        }
    }
}
