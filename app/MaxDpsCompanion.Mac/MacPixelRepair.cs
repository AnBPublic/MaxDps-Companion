using System.Drawing;

namespace MaxDpsCompanion.Mac;

// Capture-side nibble repair (macOS only).
//
// WHY: the wire encodes each channel as nibble * 17 and the companion recovers
// the nibble by rounding. That assumes the display chain returns the painted
// value. On macOS it does not always: the window server color-manages window
// content through the display's ICC profile, and saturated / near-primary
// colours are gamut-mapped, which LIFTS an idle channel by several levels.
// Measured on the reference machine (Philips 272S1 profile, sRGB content):
//   painted (255,0,0)   -> captured (255,10,1)    (green lifted, 10/255)
//   painted (0,0,255)   -> captured (0,68,255)    (green lifted 68 = 4 levels)
//   painted (255,0,255) -> captured (255,70,255)
//   greys and mixed colours -> exact (17, 34, ... 255 round-trip)
// A lifted channel decodes as a wrong nibble, the frame checksum fails, and
// the frame is dropped — the companion reports "no strip" although capture
// works. Windows has no such pipeline, which is why the wire is unaffected
// there.
//
// SIGNATURE REPAIRED: a SMALL channel level (1..6) in a cell whose sibling
// channel sits near maximum (>= 12). That is the gamut-lift shape; it is the
// only distortion the reference display produced.
//
// SAFETY
//   * Corrections are hypotheses; the protocol's own checksum is the oracle —
//     a candidate is used only when the whole frame decodes from it.
//   * Channels are only moved by whole levels (never to an arbitrary byte) and
//     the caller's array is always restored exactly, so a failed attempt
//     cannot corrupt the sample.
//   * A candidate must repeat on two consecutive samples before it is accepted
//     (the checksum is only 4 bits; requiring two identical hits makes a
//     random collision astronomically unlikely). Cost: one tick (33 ms) on the
//     repaired path only.
internal static class MacPixelRepair
{
    private const int SmallLevelMax = 6;
    private const int HighLevelMin = 12;
    private const int MaxAttempts = 400;

    private readonly record struct Hypothesis(int Cell, int Axis, byte Value);

    /// <summary>
    /// Confirmation state: the last repaired cell set, so a candidate must be
    /// produced twice in a row before it is accepted.
    /// </summary>
    private static Color[]? _pending;
    private static int _pendingCount;

    /// <summary>Drops the confirmation state (stream restart / locate reset).</summary>
    public static void Forget()
    {
        _pending = null;
        _pendingCount = 0;
    }

    /// <summary>
    /// Returns a corrected colour set that decodes, or null when the plain
    /// frame already decodes / no bounded correction works (or a correction is
    /// still awaiting confirmation). Never throws, never leaves
    /// <paramref name="cells"/> modified.
    /// </summary>
    public static Color[]? Repair(Color[] cells, ColorProfile? profile, int cellCount)
    {
        if (cells.Length < cellCount) return null;
        // Never touch a frame that already decodes: the repair is only for the
        // case where the display pipeline corrupted an otherwise valid frame.
        if (PixelProtocol.Decode(Slice(cells, cellCount), profile) is not null)
        {
            _pending = null;
            return null;
        }

        var hypotheses = Build(cells, cellCount, profile);
        if (hypotheses.Count == 0)
        {
            _pending = null;
            return null;
        }

        var candidate = TrySingles(cells, hypotheses, profile, cellCount)
            ?? TryPairs(cells, hypotheses, profile, cellCount);
        if (candidate is null)
        {
            _pending = null;
            return null;
        }

        // Confirmation: the same candidate must appear on two consecutive
        // samples. (The caller samples every tick, so this is one tick.)
        if (_pending is not null && _pendingCount == cellCount && Same(_pending, candidate))
        {
            _pending = null;
            return candidate;
        }
        _pending = candidate;
        _pendingCount = cellCount;
        return null;
    }

    private static bool Same(Color[] a, Color[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static Color[]? TrySingles(Color[] cells, List<Hypothesis> hypotheses, ColorProfile? profile, int cellCount)
    {
        foreach (var h in hypotheses)
        {
            var previous = SetLevel(cells, h);
            var ok = PixelProtocol.Decode(Slice(cells, cellCount), profile) is not null;
            SetByte(cells, h.Cell, h.Axis, previous);       // exact restore
            if (ok) return Apply(cells, new List<Hypothesis> { h }, cellCount);
        }
        return null;
    }

    private static Color[]? TryPairs(Color[] cells, List<Hypothesis> hypotheses, ColorProfile? profile, int cellCount)
    {
        var attempts = 0;
        for (var i = 0; i < hypotheses.Count && attempts < MaxAttempts; i++)
        {
            for (var j = i + 1; j < hypotheses.Count && attempts < MaxAttempts; j++)
            {
                var pi = SetLevel(cells, hypotheses[i]);
                var pj = SetLevel(cells, hypotheses[j]);
                attempts++;
                var ok = PixelProtocol.Decode(Slice(cells, cellCount), profile) is not null;
                SetByte(cells, hypotheses[j].Cell, hypotheses[j].Axis, pj);
                SetByte(cells, hypotheses[i].Cell, hypotheses[i].Axis, pi);
                if (ok) return Apply(cells, new List<Hypothesis> { hypotheses[i], hypotheses[j] }, cellCount);
            }
        }
        return null;
    }

    /// <summary>Copies the cells and applies the winning hypotheses to the copy.</summary>
    private static Color[] Apply(Color[] cells, List<Hypothesis> winners, int cellCount)
    {
        var result = new Color[cellCount];
        Array.Copy(cells, result, cellCount);
        foreach (var h in winners) SetLevel(result, h);
        return result;
    }

    /// <summary>
    /// Candidate corrections: small channels next to a near-max sibling, in the
    /// two shapes the reference display produced (level 1..6 -> 0, and
    /// level 1..6 -> level-1 as the conservative variant).
    /// </summary>
    private static List<Hypothesis> Build(Color[] cells, int cellCount, ColorProfile? profile)
    {
        var list = new List<Hypothesis>(6);
        for (var i = 0; i < cellCount; i++)
        {
            var levels = new[]
            {
                Level(cells[i].R, 0, profile),
                Level(cells[i].G, 1, profile),
                Level(cells[i].B, 2, profile),
            };
            var high = Math.Max(Math.Max(levels[0], levels[1]), levels[2]) >= HighLevelMin;
            if (!high) continue;
            for (var axis = 0; axis < 3; axis++)
            {
                var level = levels[axis];
                if (level < 1 || level > SmallLevelMax) continue;
                if (list.Count >= 6) return list;
                list.Add(new Hypothesis(i, axis, 0));                                  // full lift (blue case: 4 -> 0)
                if (level > 1) list.Add(new Hypothesis(i, axis, (byte)(level - 1)));    // partial lift
            }
        }
        return list;
    }

    /// <summary>Sets a channel to LEVEL * 17 and returns the previous byte.</summary>
    private static byte SetLevel(Color[] cells, Hypothesis h) =>
        SetByte(cells, h.Cell, h.Axis, (byte)Math.Clamp(h.Value * 17, 0, 255));

    private static byte SetByte(Color[] cells, int cell, int axis, byte value)
    {
        var c = cells[cell];
        var previous = axis switch { 0 => c.R, 1 => c.G, _ => c.B };
        cells[cell] = axis switch
        {
            0 => Color.FromArgb(value, c.G, c.B),
            1 => Color.FromArgb(c.R, value, c.B),
            _ => Color.FromArgb(c.R, c.G, value),
        };
        return previous;
    }

    private static byte Channel(Color c, int axis) => axis switch { 0 => c.R, 1 => c.G, _ => c.B };

    private static int Level(byte channel, int axis, ColorProfile? profile)
    {
        if (profile is { IsLearned: true }) return profile.Nibble(channel, axis);
        return Math.Clamp((int)Math.Round(channel * 15.0 / 255.0), 0, 15);
    }

    private static Color[] Slice(Color[] cells, int cellCount)
    {
        if (cells.Length == cellCount) return cells;
        var slice = new Color[cellCount];
        Array.Copy(cells, slice, cellCount);
        return slice;
    }
}
