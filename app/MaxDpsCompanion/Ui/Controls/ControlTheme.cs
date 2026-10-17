using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WColor = Windows.UI.Color;
using MediaFontFamily = Microsoft.UI.Xaml.Media.FontFamily;

namespace MaxDpsCompanion;

/// <summary>
/// P2-B WinUI mirror of the frozen token sources for the new control layer:
/// Ui/DesignTokens.cs (Ethereal Glass x brass) plus the ConsolePalette values
/// from the legacy UiControls.cs :11-24. WinForms Color maps 1:1 onto
/// Windows.UI.Color; point sizes convert to DIPs with <see cref="Dip"/>.
/// The .cs token files stay the single source of truth — values here are
/// literal copies (docs/plans/2026-10-11-winui3-v4-migration.md §7-P2-B).
/// </summary>
internal static class ControlTheme
{
    // ----- DesignTokens: colours -----

    public static readonly WColor Background = WColor.FromArgb(0xFF, 0x07, 0x09, 0x0B);
    public static readonly WColor Surface = WColor.FromArgb(0xFF, 0x10, 0x15, 0x19);
    public static readonly WColor SurfaceElevated = WColor.FromArgb(0xFF, 0x17, 0x1D, 0x23);
    public static readonly WColor SurfaceShell = WColor.FromArgb(0xFF, 0x12, 0x17, 0x1B);
    public static readonly WColor Border = WColor.FromArgb(0xFF, 0x23, 0x2C, 0x33);
    public static readonly WColor TextPrimary = WColor.FromArgb(0xFF, 0xF4, 0xEF, 0xE6);
    public static readonly WColor TextSecondary = WColor.FromArgb(0xFF, 0xA9, 0xB4, 0xBB);
    public static readonly WColor TextMuted = WColor.FromArgb(0xFF, 0x6E, 0x7A, 0x82);
    public static readonly WColor Accent = WColor.FromArgb(0xFF, 0xC9, 0xA2, 0x4A);
    public static readonly WColor AccentSoft = WColor.FromArgb(0xFF, 0xE6, 0xC8, 0x78);
    public static readonly WColor Success = WColor.FromArgb(0xFF, 0x4F, 0xD8, 0x98);
    public static readonly WColor Warning = WColor.FromArgb(0xFF, 0xE8, 0xB1, 0x4B);
    public static readonly WColor Danger = WColor.FromArgb(0xFF, 0xE3, 0x6A, 0x6F);
    public static readonly WColor Info = WColor.FromArgb(0xFF, 0x6A, 0xAE, 0xF0);
    public static readonly WColor Disabled = WColor.FromArgb(0xFF, 0x49, 0x54, 0x5C);
    public static readonly WColor Hairline = WColor.FromArgb(0x1F, 0xFF, 0xFF, 0xFF);
    public static readonly WColor InnerHighlight = WColor.FromArgb(0x26, 0xFF, 0xFF, 0xFF);
    public static readonly WColor ShadowLine = WColor.FromArgb(0x59, 0x00, 0x00, 0x00);
    public static readonly WColor Scrim = WColor.FromArgb(0xFF, 0x07, 0x09, 0x0B);
    public static readonly WColor ScrollTrack = Blend(Microsoft.UI.Colors.White, Background, 0.03f);
    public static readonly WColor ScrollThumb = WColor.FromArgb(0x4D, 0xFF, 0xFF, 0xFF);

    // ----- v4.0.1 glass polish (hairlines + alpha text tiers) -----
    // No mid-gray 1px borders: every card/keyline ring is white at a low
    // alpha so it reads as a hairline on any ground.

    /// <summary>Hairline ring on bezel borders and dividers (white 6%).</summary>
    public static readonly WColor Ring = WColor.FromArgb(0x0F, 0xFF, 0xFF, 0xFF);
    /// <summary>Outer bezel wash (white 4%).</summary>
    public static readonly WColor BezelWash = WColor.FromArgb(0x0A, 0xFF, 0xFF, 0xFF);
    /// <summary>Top-edge highlight at the top of a bezel core (white 9%).</summary>
    public static readonly WColor TopEdgeHighlight = WColor.FromArgb(0x16, 0xFF, 0xFF, 0xFF);
    /// <summary>Bottom of the top-edge highlight gradient (white 3%).</summary>
    public static readonly WColor TopEdgeFade = WColor.FromArgb(0x08, 0xFF, 0xFF, 0xFF);
    /// <summary>Glyph chip fill on dark pills (white 10%).</summary>
    public static readonly WColor ChipLight = WColor.FromArgb(0x1A, 0xFF, 0xFF, 0xFF);
    /// <summary>Glyph chip fill on the primary (accent) pill (black 12%).</summary>
    public static readonly WColor ChipDark = WColor.FromArgb(0x1F, 0x00, 0x00, 0x00);
    /// <summary>Text hierarchy tiers (white 80 / 45 / 40 / 25).</summary>
    public static readonly WColor TextWhite80 = WColor.FromArgb(0xCC, 0xFF, 0xFF, 0xFF);
    public static readonly WColor TextWhite45 = WColor.FromArgb(0x73, 0xFF, 0xFF, 0xFF);
    public static readonly WColor TextWhite40 = WColor.FromArgb(0x66, 0xFF, 0xFF, 0xFF);
    public static readonly WColor TextWhite25 = WColor.FromArgb(0x40, 0xFF, 0xFF, 0xFF);
    /// <summary>Disabled content opacity (crisp: ring stays, content fades).</summary>
    public const double DisabledContentOpacity = 0.38;

    // ----- ConsolePalette (legacy UiControls.cs) -----

    public static readonly WColor Iron = WColor.FromArgb(0xFF, 0x1A, 0x26, 0x2C);
    public static readonly WColor ConsoleTitleBar = WColor.FromArgb(0xFF, 0x14, 0x1E, 0x23);
    public static readonly WColor ConsolePanel = WColor.FromArgb(0xFF, 0x21, 0x2F, 0x36);
    public static readonly WColor ConsoleField = WColor.FromArgb(0xFF, 0x24, 0x34, 0x3C);
    public static readonly WColor Keyline = WColor.FromArgb(0xFF, 0x3A, 0x4A, 0x52);
    public static readonly WColor Bone = WColor.FromArgb(0xFF, 0xE9, 0xE3, 0xD3);
    public static readonly WColor Tidewash = WColor.FromArgb(0xFF, 0x93, 0xA5, 0xAE);
    public static readonly WColor Ember = WColor.FromArgb(0xFF, 0xB2, 0x3A, 0x2C);
    public static readonly WColor EmberLight = WColor.FromArgb(0xFF, 0xE0, 0x68, 0x4E);

    /// <summary>Resolved UI typeface (Theme.xaml UiFontFamily parity; DirectWrite falls back).</summary>
    public static MediaFontFamily UiFamily { get; } = new("Segoe UI Variable Text");

    public static SolidColorBrush Brush(WColor color) => new(color);

    public static SolidColorBrush TransparentBrush() => new(Microsoft.UI.Colors.Transparent);

    /// <summary>Point -> DIP (pt * 96 / 72), the Theme.xaml conversion rule.</summary>
    public static double Dip(float points) => Math.Round(points * 4.0 / 3.0, 2);

    /// <summary>Lighten a colour toward white by a 0..1 amount (hover feedback).</summary>
    public static WColor Lighten(WColor c, float amount) => WColor.FromArgb(
        c.A,
        (byte)Math.Min(255, c.R + (255 - c.R) * amount),
        (byte)Math.Min(255, c.G + (255 - c.G) * amount),
        (byte)Math.Min(255, c.B + (255 - c.B) * amount));

    /// <summary>Darken a colour toward black by a 0..1 amount (pressed feedback).</summary>
    public static WColor Darken(WColor c, float amount) => WColor.FromArgb(
        c.A,
        (byte)(c.R * (1 - amount)),
        (byte)(c.G * (1 - amount)),
        (byte)(c.B * (1 - amount)));

    /// <summary>27% blend of <paramref name="c"/> over the surface (badge fills).</summary>
    public static WColor Tint(WColor c, WColor surface) => WColor.FromArgb(
        0xFF,
        (byte)((c.R + surface.R * 2) / 3),
        (byte)((c.G + surface.G * 2) / 3),
        (byte)((c.B + surface.B * 2) / 3));

    /// <summary>Blend <paramref name="c"/> over <paramref name="back"/> at the given 0..1 opacity.</summary>
    public static WColor Blend(WColor c, WColor back, float opacity)
    {
        var t = Math.Clamp(opacity, 0f, 1f);
        return WColor.FromArgb(
            0xFF,
            (byte)(back.R + (c.R - back.R) * t),
            (byte)(back.G + (c.G - back.G) * t),
            (byte)(back.B + (c.B - back.B) * t));
    }

    /// <summary>Keep the colour, replace alpha.</summary>
    public static WColor WithAlpha(WColor c, byte alpha) => WColor.FromArgb(alpha, c.R, c.G, c.B);

    /// <summary>Human-readable status colour for a semantic state (DesignTokens parity).</summary>
    public static WColor StatusColor(StatusTone tone) => tone switch
    {
        StatusTone.Success => Success,
        StatusTone.Warning => Warning,
        StatusTone.Danger => Danger,
        StatusTone.Info => Info,
        StatusTone.Muted => TextMuted,
        _ => TextPrimary,
    };

    /// <summary>Uppercase glyph/word for a tone; colour is never the only signal.</summary>
    public static string GlyphFor(StatusTone tone) => tone switch
    {
        StatusTone.Success => "\u2713", // check
        StatusTone.Warning => "!",
        StatusTone.Danger => "\u00D7",  // cross
        StatusTone.Info => "i",
        StatusTone.Muted => "-",
        _ => "\u2022",
    };

    /// <summary>
    /// Assigns a style out of the merged app resources (Theme.xaml). Guarded:
    /// CLI/headless paths construct no controls, but a missing Application must
    /// degrade to the stock template instead of throwing.
    /// </summary>
    public static void ApplyAppStyle(Control control, string key)
    {
        try
        {
            if (Application.Current?.Resources is { } resources
                && resources.TryGetValue(key, out var value)
                && value is Style style)
            {
                control.Style = style;
            }
        }
        catch (Exception)
        {
            // Malformed/missing resource hosts keep the default template.
        }
    }
}
