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
// SIGNATURE THIS REPAIRS: a SMALL channel level (1..6) in a cell whose sibling
// channel sits near maximum (>= 12). That is the gamut-lift shape; it is the
// only distortion the reference display produced.
//
// SAFETY: corrections are hypotheses, and the protocol's own checksum is the
// acceptance oracle — a candidate is used only when the whole frame decodes
// from it. Unambiguous nonsense cannot be repaired (we never move a channel
// that already sits on its level), the candidate set is bounded (<= 6
// hypotheses, singles then pairs), and the exact case never enters this path.
internal static class MacPixelRepair
{
    private const int SmallLevelMax = 6;
    private const int HighLevelMin = 12;
    private const int MaxAttempts = 400;

    private readonly record struct Hypothesis(int Cell, int Axis, byte Target);

    /// <summary>
    /// Returns a corrected colour set that decodes, or null when the plain
    /// frame already decodes / no bounded correction works. Never throws.
    /// </summary>
    public static Color[]? Repair(Color[] cells, ColorProfile? profile, int cellCount)
    {
        if (cells.Length < cellCount) return null;

        var hypotheses = Build(cells, cellCount, profile);
        if (hypotheses.Count == 0) return null;

        // Singles first: the common case is exactly one lifted cell.
        foreach (var h in hypotheses)
        {
            var original = Swap(cells, h, h.Target);
            if (PixelProtocol.Decode(Slice(cells, cellCount), profile) is not null) return Slice(cells, cellCount);
            Swap(cells, h, original);
        }

        // Then pairs, bounded.
        var attempts = 0;
        for (var i = 0; i < hypotheses.Count && attempts < MaxAttempts; i++)
        {
            for (var j = i + 1; j < hypotheses.Count && attempts < MaxAttempts; j++)
            {
                var oi = Swap(cells, hypotheses[i], hypotheses[i].Target);
                var oj = Swap(cells, hypotheses[j], hypotheses[j].Target);
                attempts++;
                var hit = PixelProtocol.Decode(Slice(cells, cellCount), profile) is not null;
                Swap(cells, hypotheses[j], oj);
                Swap(cells, hypotheses[i], oi);
                if (hit)
                {
                    Swap(cells, hypotheses[i], hypotheses[i].Target);
                    Swap(cells, hypotheses[j], hypotheses[j].Target);
                    return Slice(cells, cellCount);
                }
            }
        }
        return null;
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
                list.Add(new Hypothesis(i, axis, 0));                     // full lift (blue case: 4 -> 0)
                if (level > 1) list.Add(new Hypothesis(i, axis, (byte)(level - 1))); // partial lift
            }
        }
        return list;
    }

    /// <summary>Sets a channel to the given level and returns the previous byte.</summary>
    private static byte Swap(Color[] cells, Hypothesis h, byte level)
    {
        var c = cells[h.Cell];
        var value = (byte)Math.Clamp(level * 17, 0, 255);
        var previous = h.Axis switch
        {
            0 => c.R,
            1 => c.G,
            _ => c.B,
        };
        cells[h.Cell] = h.Axis switch
        {
            0 => Color.FromArgb(value, c.G, c.B),
            1 => Color.FromArgb(c.R, value, c.B),
            _ => Color.FromArgb(c.R, c.G, value),
        };
        return previous;
    }

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
