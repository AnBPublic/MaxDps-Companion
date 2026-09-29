using System.Drawing.Drawing2D;

namespace MaxDpsCompanion;

/// <summary>The five top-level destinations of the control centre (v2.7 §27).</summary>
internal enum PageId { Home = 0, Abilities, Intelligence, Configuration, Diagnostics }

/// <summary>
/// Hand-drawn 1.4px navigation icons (ultra-light strokes, no font glyphs):
/// house / list / diamond / gear-lite / heartbeat. Colour follows the active
/// state; shape is unique per page so state is never colour-only.
/// </summary>
internal static class NavIcons
{
    public static void Draw(Graphics g, PageId page, Rectangle r, Color color)
    {
        using var pen = new Pen(color, 1.4F)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        var cx = r.Left + r.Width / 2f;
        var cy = r.Top + r.Height / 2f;
        var s = Math.Min(r.Width, r.Height) / 2f - 1.5f;
        switch (page)
        {
            case PageId.Home:
                g.DrawLines(pen, [
                    new PointF(cx - s, cy - s * 0.15f),
                    new PointF(cx, cy - s),
                    new PointF(cx + s, cy - s * 0.15f),
                ]);
                g.DrawLines(pen, [
                    new PointF(cx - s * 0.62f, cy - s * 0.05f),
                    new PointF(cx - s * 0.62f, cy + s * 0.9f),
                    new PointF(cx + s * 0.62f, cy + s * 0.9f),
                    new PointF(cx + s * 0.62f, cy - s * 0.05f),
                ]);
                break;
            case PageId.Abilities:
                for (var i = -1; i <= 1; i++)
                {
                    var y = cy + i * s * 0.62f;
                    g.DrawEllipse(pen, cx - s * 0.9f, y - 0.9f, 1.8f, 1.8f);
                    g.DrawLine(pen, cx - s * 0.45f, y, cx + s * 0.9f, y);
                }
                break;
            case PageId.Intelligence:
                g.DrawPolygon(pen, [
                    new PointF(cx, cy - s),
                    new PointF(cx + s, cy),
                    new PointF(cx, cy + s),
                    new PointF(cx - s, cy),
                ]);
                g.DrawEllipse(pen, cx - 1.6f, cy - 1.6f, 3.2f, 3.2f);
                break;
            case PageId.Configuration:
                g.DrawEllipse(pen, cx - s * 0.58f, cy - s * 0.58f, s * 1.16f, s * 1.16f);
                for (var i = 0; i < 4; i++)
                {
                    var angle = (i * 90 + 45) * Math.PI / 180.0;
                    var x1 = cx + (float)Math.Cos(angle) * s * 0.72f;
                    var y1 = cy + (float)Math.Sin(angle) * s * 0.72f;
                    var x2 = cx + (float)Math.Cos(angle) * s;
                    var y2 = cy + (float)Math.Sin(angle) * s;
                    g.DrawLine(pen, x1, y1, x2, y2);
                }
                break;
            case PageId.Diagnostics:
                g.DrawLines(pen, [
                    new PointF(cx - s, cy),
                    new PointF(cx - s * 0.4f, cy),
                    new PointF(cx - s * 0.15f, cy - s * 0.72f),
                    new PointF(cx + s * 0.15f, cy + s * 0.72f),
                    new PointF(cx + s * 0.4f, cy),
                    new PointF(cx + s, cy),
                ]);
                break;
        }
    }
}

/// <summary>One navigation rail entry.</summary>
internal sealed class NavRailItem : UiClickable, IUiTextFit
{
    private bool _active;

    public NavigationRail? Rail { get; set; }
    public string Glyph { get; set; } = "\u2022";
    public string Description { get; set; } = "";
    public PageId Page { get; init; }

    /// <summary>Compact rail mode: only the glyph is drawn (labels are hidden, not clipped).</summary>
    public bool Compact { get; set; }

    /// <summary>Smoke check (§47): a labelled rail item must fit glyph + label.</summary>
    public int RequiredTextWidth => Compact
        ? 40
        : 44 + TextRenderer.MeasureText(Text, DesignTokens.Type(DesignTokens.LabelSize)).Width + 12;

    public string FittedText => Compact ? "" : (Text ?? "");

    public bool Active
    {
        get => _active;
        set { _active = value; Invalidate(); }
    }

    public NavRailItem(PageId page, string text, string glyph, string description)
    {
        Page = page;
        Text = text;
        Glyph = glyph;
        Description = description;
        Dock = DockStyle.Top;
        Height = 44;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PageTab;
        AccessibleName = text;
        AccessibleDescription = description;
        TabStop = true;
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Up: Rail?.MoveSelection(this, -1); e.Handled = true; break;
            case Keys.Down: Rail?.MoveSelection(this, 1); e.Handled = true; break;
            case Keys.Home: Rail?.MoveSelection(this, int.MinValue); e.Handled = true; break;
            case Keys.End: Rail?.MoveSelection(this, int.MaxValue); e.Handled = true; break;
            case Keys.Enter:
            case Keys.Space:
                Rail?.Activate(this);
                e.Handled = true;
                break;
        }
        base.OnKeyDown(e);
    }

    // Mouse activation (v2.8.1 fix): v2.7/v2.8 only wired Enter/Space, so a
    // real click raised Click but nothing subscribed it to Activate — the rail
    // was mouse-dead while Ctrl+digit and keyboard still worked.
    protected override void OnClick(EventArgs e)
    {
        Rail?.Activate(this);
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (_active || Hovered)
        {
            var bounds = new Rectangle(6, 2, Math.Max(1, Width - 12), Height - 4);
            using var path = Ui.Rounded(bounds, DesignTokens.RadiusControl);
            var fill = _active
                ? DesignTokens.Tint(DesignTokens.Accent, DesignTokens.Surface)
                : DesignTokens.Blend(Color.White, DesignTokens.Background, 0.05f);
            using var brush = new SolidBrush(fill);
            e.Graphics.FillPath(brush, path);
            if (_active)
            {
                using var border = new Pen(Color.FromArgb(110, DesignTokens.Accent), 1F);
                e.Graphics.DrawPath(border, path);
            }
        }
        if (_active)
        {
            using var marker = new SolidBrush(DesignTokens.Accent);
            e.Graphics.FillRectangle(marker, 2, 10, 3, Math.Max(4, Height - 20));
        }
        if (Compact)
        {
            NavIcons.Draw(e.Graphics, Page, new Rectangle((Width - 22) / 2, (Height - 22) / 2, 22, 22),
                _active ? DesignTokens.AccentSoft : DesignTokens.TextSecondary);
        }
        else
        {
            NavIcons.Draw(e.Graphics, Page, new Rectangle(18, (Height - 18) / 2, 18, 18),
                _active ? DesignTokens.Accent : DesignTokens.TextMuted);
            TextRenderer.DrawText(e.Graphics, Text, DesignTokens.Type(DesignTokens.LabelSize, _active ? FontStyle.Bold : FontStyle.Regular),
                new Rectangle(44, 0, Math.Max(10, Width - 52), Height),
                _active ? DesignTokens.TextPrimary : DesignTokens.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
        if (Focused)
        {
            using var focus = new Pen(DesignTokens.Accent, 1.5F);
            using var path = Ui.Rounded(new Rectangle(6, 2, Math.Max(1, Width - 12), Height - 4), DesignTokens.RadiusControl);
            e.Graphics.DrawPath(focus, path);
        }
    }
}

/// <summary>Vertical navigation rail with keyboard traversal (Up/Down/Home/End) and
/// a compact (icon-only) mode for narrow windows.</summary>
internal sealed class NavigationRail : Panel
{
    private readonly List<NavRailItem> _items = [];
    private readonly System.Windows.Forms.Timer _widthAnim = new() { Interval = 16 };
    private int _widthTarget;
    private bool _compact;

    public event Action<NavRailItem>? Activated;

    public bool Compact => _compact;

    public NavigationRail()
    {
        Dock = DockStyle.Left;
        Width = 200;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = DesignTokens.Background;
        DoubleBuffered = true;
        Padding = new Padding(0, 56, 0, 0);
        AccessibleRole = AccessibleRole.Grouping;
        AccessibleName = "Navigation";
        _widthTarget = Width;
        _widthAnim.Tick += (_, _) => WidthTick();
    }

    public IReadOnlyList<NavRailItem> Items => _items;

    /// <summary>Switches between the labelled rail and the compact glyph rail.</summary>
    public void SetCompact(bool compact)
    {
        if (_compact == compact) return;
        _compact = compact;
        foreach (var item in _items) item.Compact = compact;
        _widthTarget = compact ? 64 : 200;
        _widthAnim.Stop();
        _widthAnim.Start();
    }

    private void WidthTick()
    {
        var next = (int)(Width + (_widthTarget - Width) * 0.35f);
        if (Math.Abs(_widthTarget - next) <= 1)
        {
            next = _widthTarget;
            _widthAnim.Stop();
        }
        Width = next;
        foreach (var item in _items) item.Invalidate();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if (_compact)
        {
            using var dot = new SolidBrush(DesignTokens.Accent);
            e.Graphics.FillRectangle(dot, (Width - 12) / 2, 24, 12, 12);
            return;
        }
        using var mark = new SolidBrush(DesignTokens.Accent);
        e.Graphics.FillRectangle(mark, 18, 26, 10, 10);
        TextRenderer.DrawText(e.Graphics, "MAXDPS", DesignTokens.Type(DesignTokens.MetaSize, FontStyle.Bold),
            new Rectangle(34, 22, Width - 42, 18), DesignTokens.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(e.Graphics, "COMPANION", DesignTokens.Type(DesignTokens.MetaSize),
            new Rectangle(34, 36, Width - 42, 18), DesignTokens.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    public void Add(NavRailItem item)
    {
        item.Rail = this;
        item.TabIndex = _items.Count;
        _items.Add(item);
        Controls.Add(item);
        Controls.SetChildIndex(item, 0);
    }

    public void SetActive(PageId page)
    {
        foreach (var item in _items)
        {
            var active = item.Page == page;
            if (item.Active == active) continue;
            item.Active = active;
        }
    }

    public void Activate(NavRailItem item)
    {
        Activated?.Invoke(item);
        item.Focus();
    }

    public void MoveSelection(NavRailItem from, int delta)
    {
        if (_items.Count == 0) return;
        var index = _items.IndexOf(from);
        if (index < 0) index = 0;
        var next = delta == int.MinValue ? 0
            : delta == int.MaxValue ? _items.Count - 1
            : Math.Clamp(index + delta, 0, _items.Count - 1);
        _items[next].Focus();
    }

    public void FocusFirst() => (_items.Count > 0 ? _items[0] : null)?.Focus();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _widthAnim.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Host panel that lays the active page out at a transition offset.</summary>
internal sealed class PageHost : Panel
{
    private Control? _page;
    private int _offset;

    public PageHost()
    {
        Dock = DockStyle.Fill;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        AutoScroll = false;
        AccessibleRole = AccessibleRole.Pane;
        AccessibleName = "Page content";
    }

    public Control? Page => _page;
    public int Offset => _offset;

    public void SetPage(Control page, int offset)
    {
        if (!ReferenceEquals(_page, page))
        {
            if (_page is not null) _page.Visible = false;
            _page = page;
            if (!Controls.Contains(page)) Controls.Add(page);
        }
        _page.Visible = true;
        _offset = offset;
        _page.BringToFront();
        PerformLayout();
    }

    public void SetOffset(int offset)
    {
        _offset = offset;
        PerformLayout();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_page is not null && ClientSize.Width > 0 && ClientSize.Height > 0)
            _page.Bounds = new Rectangle(_offset, 0, ClientSize.Width, ClientSize.Height);
    }
}

/// <summary>
/// App shell (v2.7 §27): navigation rail + page host + short interruptible
/// slide transition. The shell owns no domain state; pages are supplied by the
/// window so engine/settings wiring stays in one place.
/// </summary>
internal sealed class AppShell : Panel
{
    private readonly PageHost _host = new();
    private readonly Dictionary<PageId, Control> _pages = [];
    private readonly System.Windows.Forms.Timer _transition = new() { Interval = 16 };
    private PageId _active;
    private bool _initialised;

    /// <summary>True while the engine updates at high frequency: transitions are skipped.</summary>
    public bool SuppressTransitions { get; set; }

    public NavigationRail Rail { get; } = new();
    public ToastHost? Toast { get; private set; }
    public PageId ActivePage => _active;
    public event Action<PageId>? PageChanged;

    public AppShell()
    {
        Dock = DockStyle.Fill;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = DesignTokens.Background;
        DoubleBuffered = true;
        Rail.Activated += item => Navigate(item.Page);
        Controls.Add(_host);
        Controls.Add(Rail);
        _transition.Tick += (_, _) => TransitionTick();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // The ambient layer is the shell's own background, not a child control.
        AmbientBackdrop.Paint(e.Graphics, ClientSize);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        Rail.SetCompact(ClientSize.Width > 0 && ClientSize.Width < 880);
    }

    public void AttachToast(ToastHost toast)
    {
        Toast = toast;
        toast.Dock = DockStyle.Bottom;
        Controls.Add(toast);
    }

    public void AddPage(PageId id, Control page)
    {
        page.Visible = false;
        page.Dock = DockStyle.None;
        _pages[id] = page;
        Rail.Add(new NavRailItem(id, TitleFor(id), GlyphFor(id), DescriptionFor(id)));
    }

    /// <summary>Shows the first page without animation (initial population, §27).</summary>
    public void Initialise(PageId id)
    {
        _initialised = true;
        SetActive(id, 0);
        Rail.SetActive(id);
    }

    public void Navigate(PageId id)
    {
        if (!_pages.ContainsKey(id)) return;
        Rail.SetActive(id);
        if (!_initialised || SuppressTransitions)
        {
            SetActive(id, 0);
            _initialised = true;
        }
        else
        {
            SetActive(id, Math.Max(8, (int)(14 * (DeviceDpi / 96f))));
            _transition.Stop();
            _transition.Start();
        }
        PageChanged?.Invoke(id);
    }

    public void NavigateByDigit(int digit)
    {
        var ids = (PageId[])Enum.GetValues(typeof(PageId));
        if (digit >= 1 && digit <= ids.Length) Navigate(ids[digit - 1]);
    }

    private void SetActive(PageId id, int offset)
    {
        if (!_pages.TryGetValue(id, out var page)) return;
        _active = id;
        _host.SetPage(page, offset);
        page.TabIndex = 0;
    }

    private void TransitionTick()
    {
        var offset = _host.Offset;
        var next = offset <= 1 ? 0 : (int)(offset * 0.5f);
        _host.SetOffset(next);
        if (next == 0) _transition.Stop();
    }

    private static string TitleFor(PageId id) => id switch
    {
        PageId.Home => "Home",
        PageId.Abilities => "Abilities",
        PageId.Intelligence => "Intelligence",
        PageId.Configuration => "Configuration",
        PageId.Diagnostics => "Diagnostics",
        _ => id.ToString(),
    };

    private static string GlyphFor(PageId id) => id switch
    {
        PageId.Home => "\u2302",
        PageId.Abilities => "\u2726",
        PageId.Intelligence => "\u25C7",
        PageId.Configuration => "\u2699",
        PageId.Diagnostics => "\u2691",
        _ => "\u2022",
    };

    private static string DescriptionFor(PageId id) => id switch
    {
        PageId.Home => "Connection, action and intelligence at a glance",
        PageId.Abilities => "Search and inspect every ability",
        PageId.Intelligence => "Registry coverage dashboard",
        PageId.Configuration => "Automation, combat, safety and bridge settings",
        PageId.Diagnostics => "Protocol, telemetry and calibration tools",
        _ => "",
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing) _transition.Dispose();
        base.Dispose(disposing);
    }
}
