using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WColor = Windows.UI.Color;
using MediaFontFamily = Microsoft.UI.Xaml.Media.FontFamily;
using WPoint = Windows.Foundation.Point;

namespace MaxDpsCompanion;

/// <summary>
/// P2-B WinUI mirror of the frozen token sources for the new control layer:
/// Ui/DesignTokens.cs plus the ConsolePalette values from the legacy
/// UiControls.cs :11-24. WinForms Color maps 1:1 onto Windows.UI.Color; point
/// sizes convert to DIPs with <see cref="Dip"/>. The .cs token files stay the
/// single source of truth — values here are literal copies
/// (docs/plans/2026-10-11-winui3-v4-migration.md §7-P2-B).
///
/// 4.5.0 "Voidlight": the grey/brass "Ethereal Glass" palette is replaced by
/// the World of Warcraft: Midnight palette - void-black ground, amethyst
/// accent, Sunwell gold for warnings, moonlit-silver text
/// (docs/plans/2026-10-25-midnight-ui-4.5.0.md). Every gradient in the client
/// is built here so the XAML mirror and the code path cannot drift.
/// </summary>
internal static class ControlTheme
{
    // ----- DesignTokens: colours -----

    public static readonly WColor Background = WColor.FromArgb(0xFF, 0x0A, 0x07, 0x14);
    public static readonly WColor Surface = WColor.FromArgb(0xFF, 0x14, 0x10, 0x22);
    public static readonly WColor SurfaceElevated = WColor.FromArgb(0xFF, 0x1D, 0x17, 0x35);
    public static readonly WColor SurfaceShell = WColor.FromArgb(0xFF, 0x11, 0x0C, 0x1F);
    public static readonly WColor Border = WColor.FromArgb(0xFF, 0x2B, 0x22, 0x44);
    public static readonly WColor TextPrimary = WColor.FromArgb(0xFF, 0xF3, 0xEE, 0xFB);
    public static readonly WColor TextSecondary = WColor.FromArgb(0xFF, 0xB3, 0xA9, 0xCB);
    public static readonly WColor TextMuted = WColor.FromArgb(0xFF, 0x7C, 0x72, 0x94);
    /// <summary>Midnight amethyst: focus rings, eyebrows, ticks, active states.</summary>
    public static readonly WColor Accent = WColor.FromArgb(0xFF, 0xA7, 0x8B, 0xFA);
    public static readonly WColor AccentSoft = WColor.FromArgb(0xFF, 0xDD, 0xD0, 0xFF);
    /// <summary>Deep violet used as the dark end of accent gradients.</summary>
    public static readonly WColor AccentStrong = WColor.FromArgb(0xFF, 0x6D, 0x38, 0xE0);
    public static readonly WColor Success = WColor.FromArgb(0xFF, 0x4F, 0xD6, 0xA8);
    public static readonly WColor Warning = WColor.FromArgb(0xFF, 0xF0, 0xC0, 0x5A);
    public static readonly WColor Danger = WColor.FromArgb(0xFF, 0xD9, 0x45, 0x5E);
    public static readonly WColor Info = WColor.FromArgb(0xFF, 0x8C, 0xB8, 0xFF);
    public static readonly WColor Disabled = WColor.FromArgb(0xFF, 0x4A, 0x42, 0x60);
    public static readonly WColor Hairline = WColor.FromArgb(0x1F, 0xFF, 0xFF, 0xFF);
    public static readonly WColor InnerHighlight = WColor.FromArgb(0x26, 0xFF, 0xFF, 0xFF);
    public static readonly WColor ShadowLine = WColor.FromArgb(0x59, 0x00, 0x00, 0x00);
    public static readonly WColor Scrim = WColor.FromArgb(0xFF, 0x0A, 0x07, 0x14);
    public static readonly WColor ScrollTrack = Blend(Microsoft.UI.Colors.White, Background, 0.03f);
    public static readonly WColor ScrollThumb = WColor.FromArgb(0x4D, 0xFF, 0xFF, 0xFF);

    // ----- 4.5.0 Midnight ground / chrome gradient endpoints -----

    /// <summary>Top of the window ground gradient (violet-tinted void).</summary>
    public static readonly WColor GroundTop = WColor.FromArgb(0xFF, 0x17, 0x10, 0x29);
    /// <summary>Bottom of the window ground gradient (void black).</summary>
    public static readonly WColor GroundBottom = WColor.FromArgb(0xFF, 0x0A, 0x07, 0x14);
    /// <summary>Violet aurora wash painted over the ground (upper-left).</summary>
    public static readonly WColor GroundGlow = WColor.FromArgb(0x33, 0x3A, 0x1F, 0x73);
    /// <summary>Title-bar gradient top.</summary>
    public static readonly WColor TitleBarTop = WColor.FromArgb(0xFF, 0x1B, 0x13, 0x33);
    /// <summary>Title-bar gradient bottom (meets the body).</summary>
    public static readonly WColor TitleBarBottom = WColor.FromArgb(0xFF, 0x11, 0x0C, 0x1F);
    /// <summary>Amethyst hairline under the title bar and around elevated pills.</summary>
    public static readonly WColor AccentRing = WColor.FromArgb(0x38, 0xA7, 0x8B, 0xFA);

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

    public static readonly WColor Iron = WColor.FromArgb(0xFF, 0x17, 0x11, 0x29);
    public static readonly WColor ConsoleTitleBar = WColor.FromArgb(0xFF, 0x15, 0x0F, 0x26);
    public static readonly WColor ConsolePanel = WColor.FromArgb(0xFF, 0x20, 0x18, 0x33);
    public static readonly WColor ConsoleField = WColor.FromArgb(0xFF, 0x25, 0x1C, 0x3D);
    public static readonly WColor Keyline = WColor.FromArgb(0xFF, 0x3A, 0x2F, 0x58);
    public static readonly WColor Bone = WColor.FromArgb(0xFF, 0xF0, 0xEA, 0xFB);
    public static readonly WColor Tidewash = WColor.FromArgb(0xFF, 0xA7, 0x9F, 0xBE);
    public static readonly WColor Ember = WColor.FromArgb(0xFF, 0xC2, 0x45, 0x5C);
    public static readonly WColor EmberLight = WColor.FromArgb(0xFF, 0xEE, 0x7A, 0x8C);

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

    // ----- gradients (4.5.0 Midnight) -----

    /// <summary>
    /// Screen-space linear gradient (GDI+ parity: 0 deg = left-to-right,
    /// 90 deg = top-to-bottom). <paramref name="stops"/> are offset/colour pairs.
    /// </summary>
    public static LinearGradientBrush Gradient(double angleDeg, params (double Offset, WColor Color)[] stops)
    {
        var radians = angleDeg * Math.PI / 180;
        var dx = Math.Cos(radians);
        var dy = Math.Sin(radians); // y grows downward
        var brush = new LinearGradientBrush
        {
            StartPoint = new WPoint(0.5 - dx / 2, 0.5 - dy / 2),
            EndPoint = new WPoint(0.5 + dx / 2, 0.5 + dy / 2),
        };
        foreach (var (offset, color) in stops)
            brush.GradientStops.Add(new GradientStop { Offset = offset, Color = color });
        return brush;
    }

    /// <summary>Simple two-stop vertical gradient (top -> bottom).</summary>
    public static LinearGradientBrush Vertical(WColor top, WColor bottom)
        => Gradient(90, (0, top), (1, bottom));

    /// <summary>
    /// The shared window ground: a violet-to-void diagonal with a soft aurora
    /// wash in the upper-left. Both shells (classic body and Exp) paint it.
    /// </summary>
    public static LinearGradientBrush Ground() => Gradient(110,
        (0.00, GroundTop),
        (0.55, Background),
        (1.00, WColor.FromArgb(0xFF, 0x1A, 0x10, 0x30)));

    /// <summary>Aurora overlay laid over <see cref="Ground"/> (upper-left glow).</summary>
    public static LinearGradientBrush GroundAurora() => Gradient(25,
        (0.00, GroundGlow),
        (0.55, WithAlpha(GroundGlow, 0x10)),
        (1.00, WColor.FromArgb(0x00, 0x3A, 0x1F, 0x73)));

    /// <summary>Title-bar gradient, reused by the XAML mirror (TitleBarBrush).</summary>
    public static LinearGradientBrush TitleBar() => Gradient(90,
        (0.00, TitleBarTop),
        (1.00, TitleBarBottom));

    /// <summary>
    /// Button pill fill: a vertical ramp derived from the role colour - 10 %
    /// lighter at the top, 28 % darker at the bottom. <paramref name="lift"/>
    /// is the hover (+0.06) / press (-0.10) shift applied to the whole ramp.
    /// </summary>
    public static LinearGradientBrush Pill(WColor fill, float lift = 0f)
    {
        var top = Lighten(fill, 0.10f + lift);
        var bottom = Darken(fill, 0.28f - (lift / 2));
        return Vertical(top, bottom);
    }

    /// <summary>
    /// Ghost pill: the surface lifted toward white at the top so the button
    /// reads as a raised key on the card, never a flat grey slab.
    /// </summary>
    public static LinearGradientBrush GhostPill(WColor surface, bool hover)
        => Vertical(
            Blend(Microsoft.UI.Colors.White, surface, hover ? 0.14f : 0.085f),
            Blend(Microsoft.UI.Colors.White, surface, hover ? 0.045f : 0.02f));

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
                // WinUI resolves the element's XAML type, which for an INTERNAL
                // subclass falls back to its Button base; a style naming the
                // subclass is rejected ("cannot apply ... to an object of type
                // Button") and the throw used to be swallowed. Shared styles that
                // must serve several internal subclasses therefore target Button,
                // and the concrete control opts in from its own constructor.
                control.Style = style;
            }
        }
        catch (Exception)
        {
            // Malformed/missing resource hosts keep the default template.
        }
    }
}
