namespace MaxDpsCompanion;

/// <summary>Live data the Home page renders (v2.7 §29).</summary>
internal sealed record HomeSnapshot(
    StatusTone ConnectionTone, string ConnectionText,
    StatusTone MaxDpsTone, string MaxDpsText,
    StatusTone CompanionTone, string CompanionText,
    string ClassSpecText,
    string ModeText,
    string AutomationText,
    string CurrentActionText,
    string LastActionText,
    string WhyText,
    string CoverageText,
    string PatchText,
    string MessageText,
    StatusTone MessageTone);

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

    /// <summary>
    /// Defensive scroll normalization (v2.8.1): the scroll offset must never
    /// sit above the content origin (negative Y), which renders as a large
    /// empty band above the first card. Focus-driven ScrollControlIntoView can
    /// produce it at startup; the live status refresh calls this cheaply.
    /// </summary>
    public void NormalizeScroll()
    {
        if (ScrollArea.AutoScrollPosition.Y < 0)
            ScrollArea.AutoScrollPosition = Point.Empty;
    }

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

/// <summary>
/// Home page (v2.7 §29, v2.8 bento): an asymmetric two-column composition that
/// collapses to one column on narrow windows. One glance answers: is it
/// working, why is it acting, what will it do automatically.
/// </summary>
internal sealed class HomePage : StackPage
{
    private readonly StatusPill _connection = new("Checking", StatusTone.Muted);
    private readonly StatusPill _maxdps = new("Unknown", StatusTone.Muted);
    private readonly StatusPill _companion = new("Stopped", StatusTone.Muted);
    private readonly KvRow _classSpec = new("Class / spec");
    private readonly KvRow _mode = new("Mode");
    private readonly KvRow _automation = new("Automation");
    private readonly KvRow _currentAction = new("Current action");
    private readonly KvRow _lastAction = new("Last action");
    private readonly KvRow _why = new("Why");
    private readonly KvRow _coverage = new("Coverage");
    private readonly KvRow _patch = new("Patch / catalog");
    private readonly Label _message;
    private readonly BentoSplit _bento = new();

    /// <summary>Host for the window's Start/Stop buttons (owned by MainForm).</summary>
    public WrapFlow ControlHost { get; } = new() { Gap = DesignTokens.SpaceS };

    protected override string HeaderTitle => "Home";
    protected override string HeaderSubtitle => "Connection, live action and intelligence health at a glance";

    public HomePage()
    {
        var statusRow = new WrapFlow { Gap = DesignTokens.SpaceS };
        foreach (var pill in new[] { _connection, _maxdps, _companion }) statusRow.Controls.Add(pill);

        _message = new Label
        {
            Text = "-",
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Font = DesignTokens.Type(DesignTokens.BodySize),
            ForeColor = DesignTokens.TextSecondary,
            BackColor = Color.Transparent,
        };

        var status = new GlassCard("Status", "Connection");
        status.Add(statusRow);
        status.Add(_message);

        var identity = new GlassCard("Identity", "Character");
        identity.Add(_classSpec);
        identity.Add(_mode);
        identity.Add(_automation);

        var live = new GlassCard("Live action", "Decision");
        _currentAction.Emphasize = true;
        live.Add(_currentAction);
        live.Add(_lastAction);
        live.Add(_why);

        var intelligence = new GlassCard("Intelligence health", "Registry");
        intelligence.Add(_coverage);
        intelligence.Add(_patch);

        var controls = new GlassCard("Controls", "Engine");
        controls.Add(ControlHost);

        _bento.Left.Controls.Add(status);
        _bento.Left.Controls.Add(live);
        _bento.Right.Controls.Add(identity);
        _bento.Right.Controls.Add(intelligence);
        _bento.Full.Controls.Add(controls);
        AddContent(_bento);
    }

    public void Update(HomeSnapshot snapshot)
    {
        Set(_connection, snapshot.ConnectionText, snapshot.ConnectionTone);
        Set(_maxdps, snapshot.MaxDpsText, snapshot.MaxDpsTone);
        Set(_companion, snapshot.CompanionText, snapshot.CompanionTone);
        _classSpec.Value = snapshot.ClassSpecText;
        _mode.Value = snapshot.ModeText;
        _automation.Value = snapshot.AutomationText;
        _currentAction.Value = snapshot.CurrentActionText;
        _lastAction.Value = snapshot.LastActionText;
        _why.Value = snapshot.WhyText;
        _coverage.Value = snapshot.CoverageText;
        _patch.Value = snapshot.PatchText;
        if (_message.Text != snapshot.MessageText) _message.Text = snapshot.MessageText;
        _message.ForeColor = DesignTokens.StatusColor(snapshot.MessageTone);
        _message.AccessibleName = snapshot.MessageText;
        PerformLayout();
    }

    private static void Set(StatusPill pill, string text, StatusTone tone)
    {
        if (pill.Text == text && pill.Tone == tone) return;
        pill.Text = text;
        pill.Tone = tone;
        pill.AccessibleName = text;
        pill.PerformLayout();
        pill.Invalidate();
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
