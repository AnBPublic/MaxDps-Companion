namespace MaxDpsCompanion;

/// <summary>
/// Design tokens (v2.8, "Ethereal Glass × brass"). One source of truth for the
/// colours, spacing, radii and type scale. The ground is a near-OLED ink with a
/// single brass accent; surfaces are layered with hairline separation instead
/// of heavy borders. Member names used by other screens are preserved so code
/// outside the redesigned surfaces keeps compiling with the new values.
/// </summary>
internal static class DesignTokens
{
    // ----- colour: OLED ground, layered glass surfaces, one brass accent -----
    public static readonly Color Background = Color.FromArgb(0x07, 0x09, 0x0B);
    public static readonly Color Surface = Color.FromArgb(0x10, 0x15, 0x19);
    public static readonly Color SurfaceElevated = Color.FromArgb(0x17, 0x1D, 0x23);
    public static readonly Color SurfaceShell = Color.FromArgb(0x12, 0x17, 0x1B);
    public static readonly Color Border = Color.FromArgb(0x23, 0x2C, 0x33);
    public static readonly Color TextPrimary = Color.FromArgb(0xF4, 0xEF, 0xE6);
    public static readonly Color TextSecondary = Color.FromArgb(0xA9, 0xB4, 0xBB);
    public static readonly Color TextMuted = Color.FromArgb(0x6E, 0x7A, 0x82);
    public static readonly Color Accent = Color.FromArgb(0xC9, 0xA2, 0x4A);
    public static readonly Color AccentSoft = Color.FromArgb(0xE6, 0xC8, 0x78);
    public static readonly Color Success = Color.FromArgb(0x4F, 0xD8, 0x98);
    public static readonly Color Warning = Color.FromArgb(0xE8, 0xB1, 0x4B);
    public static readonly Color Danger = Color.FromArgb(0xE3, 0x6A, 0x6F);
    public static readonly Color Info = Color.FromArgb(0x6A, 0xAE, 0xF0);
    public static readonly Color Disabled = Color.FromArgb(0x49, 0x54, 0x5C);

    /// <summary>Hairline stroke over painted surfaces (alpha, drawn with GDI+).</summary>
    public static readonly Color Hairline = Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF);

    /// <summary>Inner top highlight for glass cores ("machined" edge).</summary>
    public static readonly Color InnerHighlight = Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF);

    /// <summary>Soft shadow line under elevated cores.</summary>
    public static readonly Color ShadowLine = Color.FromArgb(0x59, 0x00, 0x00, 0x00);

    /// <summary>Ambient mesh glow colours (painted once on the shell).</summary>
    public static readonly Color GlowBrass = Color.FromArgb(0x14, 0xC9, 0xA2, 0x4A);
    public static readonly Color GlowTeal = Color.FromArgb(0x10, 0x38, 0x8C, 0x78);

    // ----- spacing scale -----
    public const int SpaceXs = 4;
    public const int SpaceS = 8;
    public const int SpaceM = 12;
    public const int SpaceL = 16;
    public const int SpaceXl = 24;
    public const int SpaceXxl = 32;
    public const int SpaceXxxl = 40;

    // ----- radii (concentric pairs) -----
    public const int RadiusOuter = 20;
    public const int RadiusInner = 14;
    public const int RadiusTile = 12;
    public const int RadiusControl = 10;

    // ----- type scale (pt) -----
    public const float DisplaySize = 20F;
    public const float SectionSize = 13F;
    public const float LabelSize = 11F;
    public const float BodySize = 10F;
    public const float MetaSize = 8.5F;

    private static string? _family;

    /// <summary>
    /// Resolved UI typeface: bundled Geist when its faces loaded, otherwise the
    /// system variable font chain (same fallback semantics as before).
    /// </summary>
    public static string FamilyName
    {
        get
        {
            if (_family is not null) return _family;
            if (UiFonts.FamilyAvailable) return _family = UiFonts.FamilyName;
            foreach (var candidate in new[] { "Segoe UI Variable", "Segoe UI Variable Text", "Segoe UI" })
            {
                try
                {
                    using var probe = new FontFamily(candidate);
                    if (probe.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                        return _family = candidate;
                }
                catch (ArgumentException) { /* family missing: try next */ }
            }
            return _family = "Segoe UI";
        }
    }

    /// <summary>Reset the cached family (font load timing / tests).</summary>
    public static void InvalidateFamily() => _family = null;

    /// <summary>
    /// A font from the type scale. Callers own nothing: WinForms does not
    /// dispose <see cref="Control.Font"/>, so a fresh Font per control is safe.
    /// </summary>
    public static Font Type(float size, FontStyle style = FontStyle.Regular) => new(FamilyName, size, style);

    public static Font Display => Type(DisplaySize, FontStyle.Bold);
    public static Font Section => Type(SectionSize, FontStyle.Bold);
    public static Font Label => Type(LabelSize, FontStyle.Bold);
    public static Font Body => Type(BodySize);
    public static Font Meta => Type(MetaSize);

    /// <summary>Lighten a colour toward white by a 0..1 amount (hover feedback).</summary>
    public static Color Lighten(Color c, float amount) => Color.FromArgb(
        c.A,
        (int)Math.Min(255, c.R + (255 - c.R) * amount),
        (int)Math.Min(255, c.G + (255 - c.G) * amount),
        (int)Math.Min(255, c.B + (255 - c.B) * amount));

    /// <summary>Darken a colour toward black by a 0..1 amount (pressed feedback).</summary>
    public static Color Darken(Color c, float amount) => Color.FromArgb(
        c.A,
        (int)(c.R * (1 - amount)),
        (int)(c.G * (1 - amount)),
        (int)(c.B * (1 - amount)));

    /// <summary>27% blend of <paramref name="c"/> over the surface (badge fills).</summary>
    public static Color Tint(Color c, Color surface) => Color.FromArgb(
        0xFF,
        (c.R + surface.R * 2) / 3,
        (c.G + surface.G * 2) / 3,
        (c.B + surface.B * 2) / 3);

    /// <summary>Blend <paramref name="c"/> over <paramref name="back"/> at the given 0..1 opacity.</summary>
    public static Color Blend(Color c, Color back, float opacity)
    {
        var t = Math.Clamp(opacity, 0f, 1f);
        return Color.FromArgb(
            0xFF,
            (int)(back.R + (c.R - back.R) * t),
            (int)(back.G + (c.G - back.G) * t),
            (int)(back.B + (c.B - back.B) * t));
    }

    /// <summary>Human-readable status colour for a semantic state.</summary>
    public static Color StatusColor(StatusTone tone) => tone switch
    {
        StatusTone.Success => Success,
        StatusTone.Warning => Warning,
        StatusTone.Danger => Danger,
        StatusTone.Info => Info,
        StatusTone.Muted => TextMuted,
        _ => TextPrimary,
    };
}

/// <summary>Semantic status tones; never used as the only signal (glyph/text accompanies).</summary>
internal enum StatusTone { Neutral, Success, Warning, Danger, Info, Muted }
