namespace MaxDpsCompanion;

/// <summary>
/// Display-chain colour profile learned from the addon's `/mdb calibrate`
/// pattern. RenoDX, RTX HDR and ICC calibration shift each channel
/// independently, which defeats the legacy single black/white pair taken
/// from the magic cell — so every channel gets its own black/white range,
/// and the magic cell is matched by distance from a learned reference
/// instead of a fixed contrast check.
/// When <see cref="IsLearned"/> is false the legacy fixed behaviour applies
/// and existing settings files keep working untouched.
/// </summary>
internal sealed class ColorProfile
{
    /// <summary>Legacy fixed contrast floor, kept for unlearned profiles.</summary>
    public const int LegacyMinContrast = 96;

    /// <summary>Magic reference: measured magic cell at learn time.</summary>
    public int MagicR { get; set; } = 255;
    public int MagicG { get; set; }
    public int MagicB { get; set; } = 255;

    /// <summary>Per-channel measured black (pattern level 0) and white (level 15).</summary>
    public int[] Black { get; } = [0, 0, 0];
    public int[] White { get; } = [255, 255, 255];

    /// <summary>Max per-channel distance from the magic reference that still counts.</summary>
    public int Tolerance { get; set; } = 64;

    /// <summary>When the profile was learned (local time, informational).</summary>
    public string LearnedAt { get; set; } = "";

    public bool IsLearned => LearnedAt.Length > 0;

    /// <summary>Largest per-channel gap between magic reference and black.</summary>
    public int Separation()
    {
        var sep = 0;
        var magic = new[] { MagicR, MagicG, MagicB };
        for (var i = 0; i < 3; i++)
            sep = Math.Max(sep, Math.Abs(magic[i] - Black[i]));
        return sep;
    }

    public bool IsMagic(Color color)
    {
        if (!IsLearned)
            return color.R - color.G >= LegacyMinContrast
                && color.B - color.G >= LegacyMinContrast;

        // Chebyshev match against the learned reference, then a structural
        // guard: the learned magenta-vs-green dominance must be preserved
        // proportionally. Without the guard, a tolerant box around a shifted
        // magenta also accepts near-white/warm greys (R≈G≈B).
        if (Math.Abs(color.R - MagicR) > Tolerance
            || Math.Abs(color.G - MagicG) > Tolerance
            || Math.Abs(color.B - MagicB) > Tolerance)
            return false;
        var refR = MagicR - MagicG;
        var refB = MagicB - MagicG;
        if (refR <= 0 || refB <= 0) return true; // degenerate reference: box only
        var keepR = (int)(refR * 0.5);
        var keepB = (int)(refB * 0.5);
        return color.R - color.G >= keepR && color.B - color.G >= keepB;
    }

    public int Nibble(byte channel, int axis)
    {
        var black = IsLearned ? Black[axis] : 0;
        var white = IsLearned ? White[axis] : 255;
        if (white <= black) return channel >= black ? 15 : 0;
        var scaled = (channel - black) * 15.0 / (white - black);
        return Math.Clamp((int)Math.Round(scaled), 0, 15);
    }

    /// <summary>Legacy shared-ramp levels, used only when no profile is learned.</summary>
    public (int Black, int White) LegacyLevels(Color magic)
    {
        var black = magic.G;
        var white = (magic.R + magic.B) / 2;
        return (black, white);
    }

    public ColorProfile Clone()
    {
        var copy = new ColorProfile
        {
            MagicR = MagicR,
            MagicG = MagicG,
            MagicB = MagicB,
            Tolerance = Tolerance,
            LearnedAt = LearnedAt,
        };
        for (var i = 0; i < 3; i++)
        {
            copy.Black[i] = Black[i];
            copy.White[i] = White[i];
        }
        return copy;
    }
}
