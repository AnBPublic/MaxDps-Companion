namespace MaxDpsCompanion;

/// <summary>
/// Width-tier scale for the classic shell (v3.0.0 D5). The client width picks
/// one of four tiers; each tier sets a base type step, row heights, card
/// padding, button heights and the toggle size. Recomputed only on a debounced
/// resize (never mid-drag) and applied to the main window, the Advanced popup
/// and the Abilities popup. Fonts stay on the system Segoe UI Variable chain
/// (no bundled faces); a tier only moves the point size.
/// </summary>
internal enum UiTier
{
    /// <summary>&lt;= 560 px — compact.</summary>
    Compact,

    /// <summary>&lt;= 700 px — classic (the 660 default).</summary>
    Classic,

    /// <summary>&lt;= 950 px — roomy.</summary>
    Roomy,

    /// <summary>&gt; 950 px — wide.</summary>
    Wide,
}

internal readonly struct UiScale
{
    public UiTier Tier { get; }

    /// <summary>Point-size delta from <see cref="DesignTokens.BodySize"/>: -1 / 0 / +1 / +2.</summary>
    public float FontStep { get; }

    /// <summary>Base point size for body copy at this tier.</summary>
    public float BaseFont => DesignTokens.BodySize + FontStep;

    /// <summary>Minimum height of a two-toggle setting row (content may need more).</summary>
    public int RowHeight { get; }

    public int CardPadding { get; }
    public int ButtonHeight { get; }
    public int ButtonRow2Height { get; }
    public int StatusHeight { get; }
    public int LiveHeight { get; }
    public int StripHeight { get; }
    public int HeaderHeight { get; }
    public int StatusLineHeight { get; }
    public Size ToggleSize { get; }

    private UiScale(
        UiTier tier, float fontStep, int rowHeight, int cardPadding,
        int buttonHeight, int buttonRow2Height, int statusHeight, int liveHeight,
        int stripHeight, int headerHeight, int statusLineHeight, Size toggleSize)
    {
        Tier = tier;
        FontStep = fontStep;
        RowHeight = rowHeight;
        CardPadding = cardPadding;
        ButtonHeight = buttonHeight;
        ButtonRow2Height = buttonRow2Height;
        StatusHeight = statusHeight;
        LiveHeight = liveHeight;
        StripHeight = stripHeight;
        HeaderHeight = headerHeight;
        StatusLineHeight = statusLineHeight;
        ToggleSize = toggleSize;
    }

    public static UiTier TierFor(int clientWidth) =>
        clientWidth <= 560 ? UiTier.Compact
        : clientWidth <= 700 ? UiTier.Classic
        : clientWidth <= 950 ? UiTier.Roomy
        : UiTier.Wide;

    public static UiScale For(int clientWidth) => TierFor(clientWidth) switch
    {
        UiTier.Compact => new(UiTier.Compact, -1f, 56, 12, 40, 38, 38, 38, 36, 22, 24, new Size(46, 26)),
        UiTier.Roomy => new(UiTier.Roomy, 1f, 74, 20, 46, 42, 46, 46, 44, 26, 28, new Size(58, 34)),
        UiTier.Wide => new(UiTier.Wide, 2f, 82, 24, 52, 46, 50, 50, 48, 28, 30, new Size(64, 38)),
        _ => new(UiTier.Classic, 0f, 66, 18, 40, 38, 42, 42, 40, 24, 26, new Size(52, 30)),
    };
}
