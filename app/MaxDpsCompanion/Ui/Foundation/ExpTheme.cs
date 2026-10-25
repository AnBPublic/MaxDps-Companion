namespace MaxDpsCompanion;

/// <summary>
/// Minimal Exp (in-game-config) theme: one source of truth for the palette and
/// the transport radii, so the pill, transport buttons and the console card
/// cannot drift (WinUI port of Ui/ExpTheme.cs; the old WinForms shell read it in
/// MainForm, now deleted).
///
/// 4.5.0 "Voidlight": the green-black terminal tokens moved to the same
/// World of Warcraft: Midnight family as <see cref="ControlTheme"/> - void
/// ground, amethyst accent, Sunwell gold warning, moonlit-silver text. The
/// *Fill tokens are the button bases (deep enough for light text on the
/// gradient's midpoint); the plain tokens are text/status colours.
///
/// This is the C# half of the Exp palette; Theme.xaml mirrors the same values
/// as Exp* resources (ExpRadiusInner/ExpControlHeight/ExpMonoFontFamily live
/// there because only XAML consumes them).
/// </summary>
internal static class ExpTheme
{
    // ----- colour (Midnight) -----
    public static readonly Color Bg        = Color.FromArgb(0x0A, 0x07, 0x14);
    public static readonly Color Card      = Color.FromArgb(0x17, 0x11, 0x2A);
    public static readonly Color Border    = Color.FromArgb(0x2B, 0x22, 0x44);
    public static readonly Color Accent    = Color.FromArgb(0xA7, 0x8B, 0xFA);
    public static readonly Color Danger    = Color.FromArgb(0xD9, 0x45, 0x5E);
    public static readonly Color Ok        = Color.FromArgb(0x4F, 0xD6, 0xA8);
    public static readonly Color Text      = Color.FromArgb(0xF3, 0xEE, 0xFB);
    public static readonly Color Secondary = Color.FromArgb(0xB3, 0xA9, 0xCB);
    public static readonly Color MonoStamp = Color.FromArgb(0x7C, 0x72, 0x94);

    // ----- button fills (gradient bases) -----
    // Deep enough that the white label keeps AA contrast at the ramp's midpoint
    // (OkFill #237257 -> ~5.3:1, DangerFill #C9405A -> ~5.2:1).
    public static readonly Color OkFill     = Color.FromArgb(0x23, 0x72, 0x57);
    public static readonly Color DangerFill = Color.FromArgb(0xC9, 0x40, 0x5A);
    public static readonly Color AccentFill = Color.FromArgb(0x8B, 0x5C, 0xF6);

    // ----- geometry -----
    // v4.0.1: the Exp card follows the shared double-bezel radii (outer 20,
    // inner = outer - 5), so the token no longer tightens the card to 12.
    public const int RadiusOuter = 20;
}
