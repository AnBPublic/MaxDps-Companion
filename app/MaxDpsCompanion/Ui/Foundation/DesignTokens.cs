namespace MaxDpsCompanion;

/// <summary>
/// Frozen numeric design tokens (spacing, radii, type scale) plus the semantic
/// <see cref="StatusTone"/>. WinUI completion of the old Ui/DesignTokens.cs
/// ("Ethereal Glass × brass"): the colour/font half of that file is gone with
/// the WinForms shell (2026-10-11 WinUI 3 migration, P3). Colours live in
/// <see cref="ControlTheme"/>, the typeface in Theme.xaml
/// (<see cref="ControlTheme.UiFamily"/>); point sizes here are converted to
/// DIPs with <see cref="ControlTheme.Dip"/>. Values are unchanged from v3 so
/// the frozen width tiers (UiScale) and the frozen toggle model keep parity.
/// </summary>
internal static class DesignTokens
{
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
    public const float TitleSize = 15F;
    public const float SectionSize = 13F;
    public const float LabelSize = 11F;
    public const float BodySize = 10F;
    public const float CaptionSize = 9F;
    public const float MetaSize = 8.5F;
    public const float MicroSize = 8F;
}

/// <summary>
/// Semantic status tones; never used as the only signal (glyph/text accompanies).
/// Wave B: declared ONCE in MaxDpsCompanion.Core (StatusTone.cs) and consumed
/// here through the project reference - the duplicate declaration moved there.
/// </summary>
