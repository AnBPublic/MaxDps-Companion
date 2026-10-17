namespace MaxDpsCompanion;

/// <summary>
/// Minimal Exp (in-game-config) theme: one source of truth for the dark
/// terminal palette and the transport radii, so the pill, transport buttons
/// and the console card cannot drift (WinUI port of Ui/ExpTheme.cs; the old
/// WinForms shell read it in MainForm, now deleted).
///
/// This is the C# half of the Exp palette; Theme.xaml mirrors the same values
/// as Exp* resources (ExpRadiusInner/ExpControlHeight/ExpMonoFontFamily live
/// there because only XAML consumes them). The classic shell keeps
/// <see cref="ControlTheme"/> untouched: nothing here is read outside the Exp
/// code path.
/// </summary>
internal static class ExpTheme
{
    // ----- colour (dark terminal) -----
    public static readonly Color Bg        = Color.FromArgb(0x0B, 0x11, 0x10);
    public static readonly Color Card      = Color.FromArgb(0x13, 0x1C, 0x1A);
    public static readonly Color Border    = Color.FromArgb(0x25, 0x31, 0x2E);
    public static readonly Color Accent    = Color.FromArgb(0xE8, 0xC5, 0x58);
    public static readonly Color Danger    = Color.FromArgb(0xE5, 0x60, 0x4F);
    public static readonly Color Ok        = Color.FromArgb(0x4C, 0xC3, 0x8A);
    public static readonly Color Text      = Color.FromArgb(0xED, 0xEF, 0xF0);
    public static readonly Color Secondary = Color.FromArgb(0x9A, 0xA5, 0xA1);
    public static readonly Color MonoStamp = Color.FromArgb(0x5E, 0x6B, 0x67);

    // ----- geometry -----
    // v4.0.1: the Exp card follows the shared double-bezel radii (outer 20,
    // inner = outer - 5), so the token no longer tightens the card to 12.
    public const int RadiusOuter = 20;
}
