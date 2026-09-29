namespace MaxDpsCompanion;

/// <summary>
/// Base for settings-style pages: a page header plus a scrollable measured
/// stack of glass cards. Layout is fully measured (v2.8): the stack sizes to
/// the scroll viewport width and its own measured height, so content can never
/// collapse into an invisible zero-width subtree (the v2.7 defect class).
/// </summary>
internal abstract class StackPage : Panel
{
    protected readonly VertStack Stack;

    /// <summary>The scrollable host (snapshot/tests scroll this to a section).</summary>
    public Panel ScrollArea { get; }

    protected StackPage()
    {
        Dock = DockStyle.Fill;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;

        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.Transparent,
        };
        ScrollArea = scroll;
        Stack = new VertStack { Gap = DesignTokens.SpaceL };
        scroll.Controls.Add(Stack);
        Controls.Add(scroll);
        Controls.Add(new PageHeader(HeaderTitle, HeaderSubtitle));
    }

    protected abstract string HeaderTitle { get; }
    protected abstract string HeaderSubtitle { get; }

    /// <summary>Adds a glass card to the page stack and returns it for content.</summary>
    public GlassCard AddCard(string title, string eyebrow = "")
    {
        var card = new GlassCard(title, eyebrow);
        Stack.Controls.Add(card);
        return card;
    }

    /// <summary>Adds a raw measured control to the page stack (bento splits, etc.).</summary>
    public T AddContent<T>(T control) where T : Control
    {
        Stack.Controls.Add(control);
        return control;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        SizeStack();
    }

    private void SizeStack()
    {
        var width = Math.Max(120, ScrollArea.ClientSize.Width - ScrollArea.Padding.Horizontal);
        var height = Stack.MeasuredHeight(width);
        var bounds = new Rectangle(0, 0, width, height);
        if (Stack.Bounds != bounds) Stack.Bounds = bounds;
    }
}

/// <summary>Single stacked proportion bar (no decorative charts).</summary>
internal sealed class ProportionBar : Control, IUiMeasured
{
    public (int Value, Color Color, string Label)[] Segments { get; set; } = [];

    public ProportionBar()
    {
        Height = 22;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Color.Transparent;
        DoubleBuffered = true;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = "Coverage proportion";
    }

    public int MeasuredHeight(int width) => 22;

    protected override void OnPaint(PaintEventArgs e)
    {
        var total = 0;
        foreach (var (value, _, _) in Segments) total += Math.Max(0, value);
        if (total <= 0) return;
        var x = 0;
        for (var i = 0; i < Segments.Length; i++)
        {
            var (value, color, _) = Segments[i];
            if (value <= 0) continue;
            var w = i == Segments.Length - 1 ? Width - x : (int)((long)Width * value / total);
            if (w < 1) w = 1;
            using var brush = new SolidBrush(color);
            e.Graphics.FillRectangle(brush, x, 2, w - (i == Segments.Length - 1 ? 0 : 1), Height - 4);
            x += w;
        }
    }
}

/// <summary>Intelligence registry-health dashboard (v2.7 §30, v2.8 bento).</summary>
internal sealed class IntelligencePage : StackPage
{
    private readonly Dictionary<string, MetricTile> _tiles = [];
    private readonly KvRow _patchRow = new("Patch");
    private readonly KvRow _catalogRow = new("Catalog revision");
    private readonly KvRow _verifiedRow = new("Registry verified");
    private readonly ProportionBar _bar = new();
    private readonly WrapFlow _tileFlow = new() { Gap = DesignTokens.SpaceS, RowGap = DesignTokens.SpaceS };
    private bool _built;

    /// <summary>The built coverage report (null until <see cref="EnsureBuilt"/>).</summary>
    public CoverageReport? Report { get; private set; }

    /// <summary>Test hook: the clickable metric tiles, keyed by coverage bucket.</summary>
    internal IReadOnlyDictionary<string, MetricTile> TilesForTest => _tiles;

    public event Action<string>? DrillRequested;

    protected override string HeaderTitle => "Intelligence";
    protected override string HeaderSubtitle => "Registry coverage and patch status";

    public IntelligencePage()
    {
        foreach (var (key, label) in new[]
        {
            ("Automatic", "Automatic"),
            ("Companion", "Companion-generated"),
            ("MaxDps", "MaxDps-delegated"),
            ("Shared", "Shared-gated"),
            ("Manual", "Manual"),
            ("Unobservable", "Unobservable"),
            ("Incomplete", "Research-pending"),
            ("Stale", "Stale"),
            ("Missing", "Missing"),
            ("Duplicates", "Duplicate names"),
            ("LiveVerified", "Live-verified"),
            ("LiveUnverified", "Live-unverified"),
            ("Filtered", "Filtered"),
        })
        {
            var tile = new MetricTile(label, "\u2013", StatusTone.Neutral, clickable: true);
            tile.Click += (_, _) => DrillRequested?.Invoke(DrillTagFor(key));
            _tiles[key] = tile;
            _tileFlow.Controls.Add(tile);
        }

        var health = AddCard("Registry health", "Coverage");
        health.Add(_tileFlow);

        var proportion = AddCard("Proportion", "Composition");
        proportion.Add(_bar);

        var meta = AddCard("Patch & catalog", "Provenance");
        meta.Add(_patchRow);
        meta.Add(_catalogRow);
        meta.Add(_verifiedRow);
    }

    private static string DrillTagFor(string key) => key switch
    {
        "Companion" => "Companion",
        "MaxDps" => "MaxDps",
        "Manual" => "Manual",
        "Stale" or "Missing" or "Duplicates" => "Warnings",
        _ => "All",
    };

    /// <summary>Builds the coverage report once (never per paint; §30).</summary>
    public void EnsureBuilt()
    {
        if (_built) return;
        _built = true;
        var catalog = AbilityCatalog.Default;
        var report = AbilityCoverage.Build(catalog);
        Report = report;
        Set("Automatic", report.Automatable, StatusTone.Success);
        Set("Companion", report.CompanionGenerated, StatusTone.Success);
        Set("MaxDps", report.MaxDpsDelegated, StatusTone.Info);
        Set("Shared", report.SharedGated, StatusTone.Info);
        Set("Manual", report.Manual, StatusTone.Muted);
        Set("Unobservable", report.Unobservable, StatusTone.Warning);
        Set("Incomplete", report.ResearchPending, StatusTone.Warning);
        Set("Stale", report.Stale, report.Stale > 0 ? StatusTone.Danger : StatusTone.Success);
        Set("Missing", report.Missing, report.Missing > 0 ? StatusTone.Danger : StatusTone.Success);
        Set("Duplicates", report.Duplicates, report.Duplicates > 0 ? StatusTone.Warning : StatusTone.Success);
        Set("LiveVerified", report.LiveVerified, StatusTone.Success);
        Set("LiveUnverified", report.LiveUnverified, StatusTone.Warning);
        Set("Filtered", report.Filtered, StatusTone.Muted);

        _patchRow.Value = catalog.GamePatch;
        _catalogRow.Value = $"v{AbilityCatalog.CatalogVersion}";
        _verifiedRow.Value = string.IsNullOrEmpty(catalog.VerifiedDate) ? "unknown" : catalog.VerifiedDate;
        _bar.Segments =
        [
            (report.CompanionGenerated, DesignTokens.Success, "companion"),
            (report.SharedGated, DesignTokens.Accent, "shared"),
            (report.MaxDpsDelegated, DesignTokens.Info, "maxdps"),
            (report.Manual, DesignTokens.TextMuted, "manual"),
            (report.ResearchPending, DesignTokens.Warning, "research"),
        ];
        PerformLayout();
        _bar.Invalidate();
    }

    private void Set(string key, int value, StatusTone tone)
    {
        if (!_tiles.TryGetValue(key, out var tile)) return;
        tile.Value = value.ToString();
        tile.Tone = tone;
        tile.AccessibleName = $"{tile.Label}: {value}";
        tile.Invalidate();
    }
}
