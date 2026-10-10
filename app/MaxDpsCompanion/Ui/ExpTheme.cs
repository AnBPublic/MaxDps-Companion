namespace MaxDpsCompanion;

/// <summary>
/// Minimal Exp (in-game-config) theme. One source of truth for the dark
/// terminal palette, radii and the transport control height, so the pill,
/// transport buttons and the console card cannot drift. The classic shell
/// keeps <see cref="DesignTokens"/>/<see cref="ConsolePalette"/> untouched:
/// nothing here is read outside the Exp code path.
///
/// WinForms-safe: only Color/const values and a font resolver (no layout
/// logic, no timers, no new control types).
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
    public const int RadiusOuter = 12;
    public const int RadiusInner = 10;
    public const int ControlHeight = 42;

    private static string? _mono;

    /// <summary>Resolved monospace family for the log/timestamps.</summary>
    public static string MonoFamily
    {
        get
        {
            if (_mono is not null) return _mono;
            foreach (var candidate in new[] { "Cascadia Mono", "Consolas", "Courier New" })
            {
                try
                {
                    using var probe = new FontFamily(candidate);
                    if (probe.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                        return _mono = candidate;
                }
                catch (ArgumentException) { /* family missing: try next */ }
            }
            return _mono = FontFamily.GenericMonospace.Name;
        }
    }

    public static Font Mono(float size, FontStyle style = FontStyle.Regular) =>
        new(MonoFamily, size, style);
}
