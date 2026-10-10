using System.Collections;
using System.Drawing.Drawing2D;

namespace MaxDpsCompanion;

// ---------------------------------------------------------------------------
// S5 UI foundation: the owner-drawn replacements for the native controls whose
// simulated transparency garbled the popups.
//
// Root cause (MainForm.cs scrim + ClassSkillsView ComboBox): a Panel with an
// alpha BackColor is painted by WinForms' "transparent" hack, which asks the
// PARENT to render its background — it does not composite the child windows.
// Native TabControl / ComboBox children are separate HWNDs and never take part
// in that paint, so their pixels came out garbled against the animated scrim.
// The fix everywhere below is the same rule: ONE opaque layer, owner-drawn,
// no alpha BackColor over a native child.
// ---------------------------------------------------------------------------

/// <summary>
/// Owner-drawn segmented tab strip. Replaces <see cref="TabControl"/>: the
/// native control's themed tab header could not be drawn reliably over the
/// (now opaque) popup scrim, and its selection was a separate HWND paint. The
/// segmented strip and the selected page share one parent surface, so the
/// popup is a single paint layer.
///
/// API parity with the callsites: <see cref="TabPages"/>, <see cref="SelectedIndex"/>
/// and inherited <see cref="Control.Font"/>.
/// </summary>
internal sealed class SegmentedTabs : Control
{
    private const int HeaderHeight = 40;
    private readonly SegmentedTabPageCollection _pages;
    private int _selectedIndex = -1;

    public event EventHandler? SelectedIndexChanged;

    public SegmentedTabs()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor
            | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        BackColor = DesignTokens.Background;
        _pages = new SegmentedTabPageCollection(this);
        AccessibleRole = AccessibleRole.PageTabList;
        AccessibleName = "Tabs";
        Font = DesignTokens.Type(DesignTokens.BodySize);
    }

    public SegmentedTabPageCollection TabPages => _pages;

    public SegmentedTabPage? SelectedTab =>
        _selectedIndex >= 0 && _selectedIndex < _pages.Count ? _pages[_selectedIndex] : null;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_pages.Count == 0)
            {
                _selectedIndex = -1;
                return;
            }
            var index = Math.Clamp(value, 0, _pages.Count - 1);
            if (index == _selectedIndex) return;
            _selectedIndex = index;
            ApplySelection();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void AddPage(SegmentedTabPage page)
    {
        page.Visible = false;
        _pages.Items.Add(page);
        Controls.Add(page);
        if (_selectedIndex < 0) _selectedIndex = 0;
        ApplySelection();
        Invalidate();
    }

    private void ApplySelection()
    {
        for (var i = 0; i < _pages.Count; i++) _pages[i].Visible = i == _selectedIndex;
        PerformLayout();
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var content = new Rectangle(0, HeaderHeight, Math.Max(1, ClientSize.Width),
            Math.Max(1, ClientSize.Height - HeaderHeight));
        foreach (var page in _pages.Items) page.Bounds = content;
    }

    // ----- segment geometry (shared by paint + hit-test) -----

    private int SegmentWidth(SegmentedTabPage page, int available)
    {
        var measured = TextRenderer.MeasureText(page.Title, Font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width + 34;
        var want = Math.Max(84, measured);
        if (_pages.Count > 0 && want * _pages.Count > available)
            want = Math.Max(48, available / _pages.Count);
        return want;
    }

    private Rectangle SegmentBounds(int index, out int x)
    {
        x = 0;
        for (var i = 0; i < index; i++) x += SegmentWidth(_pages[i], ClientSize.Width);
        var width = SegmentWidth(_pages[index], ClientSize.Width);
        var max = Math.Max(0, ClientSize.Width - x);
        return new Rectangle(x, 0, Math.Min(width, max), HeaderHeight);
    }

    private int HitTestSegment(int mouseX)
    {
        var x = 0;
        for (var i = 0; i < _pages.Count; i++)
        {
            var width = SegmentWidth(_pages[i], ClientSize.Width);
            if (mouseX >= x && mouseX < x + width) return i;
            x += width;
        }
        return -1;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && e.Y < HeaderHeight)
        {
            var index = HitTestSegment(e.X);
            if (index >= 0) SelectedIndex = index;
        }
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var strip = new SolidBrush(DesignTokens.Background))
            e.Graphics.FillRectangle(strip, new Rectangle(0, 0, ClientSize.Width, HeaderHeight));

        for (var i = 0; i < _pages.Count; i++)
        {
            var bounds = SegmentBounds(i, out _);
            var selected = i == _selectedIndex;
            if (selected)
            {
                using var fill = new SolidBrush(DesignTokens.Blend(Color.White, DesignTokens.Background, 0.05f));
                e.Graphics.FillRectangle(fill, bounds);
            }
            var textColor = selected ? DesignTokens.TextPrimary : DesignTokens.TextMuted;
            TextRenderer.DrawText(e.Graphics, _pages[i].Title, Font,
                new Rectangle(bounds.X + 8, bounds.Y, Math.Max(10, bounds.Width - 16), bounds.Height),
                textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (selected)
            {
                using var underline = new SolidBrush(DesignTokens.Accent);
                e.Graphics.FillRectangle(underline, bounds.X + 8, HeaderHeight - 3, Math.Max(8, bounds.Width - 16), 2);
            }
        }

        using var rule = new Pen(DesignTokens.Hairline, 1F);
        e.Graphics.DrawLine(rule, 0, HeaderHeight - 1, ClientSize.Width, HeaderHeight - 1);
    }
}

/// <summary>One page hosted by <see cref="SegmentedTabs"/>. A plain opaque Panel.</summary>
internal sealed class SegmentedTabPage : Panel
{
    public string Title { get; }

    public SegmentedTabPage(string title)
    {
        Title = title;
        Text = title;
        Dock = DockStyle.Fill;
        BackColor = DesignTokens.Background;
        Visible = false;
    }
}

/// <summary>Collection facade kept binary-shape-compatible with TabControl.TabPages usage.</summary>
internal sealed class SegmentedTabPageCollection : IEnumerable<SegmentedTabPage>
{
    internal readonly List<SegmentedTabPage> Items = new();
    private readonly SegmentedTabs _owner;

    internal SegmentedTabPageCollection(SegmentedTabs owner) => _owner = owner;

    public void Add(SegmentedTabPage page) => _owner.AddPage(page);

    public int Count => Items.Count;

    public SegmentedTabPage this[int index] => Items[index];

    public IEnumerator<SegmentedTabPage> GetEnumerator() => Items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Items.GetEnumerator();
}

/// <summary>
/// Owner-drawn combo box with a tier-scaled item height. The native
/// DropDownList painted a system-white selection box and a fixed item height
/// that ignored the width tier; this draws the dropdown itself and grows the
/// row with the tier.
/// </summary>
internal sealed class OwnedComboBox : ComboBox
{
    public OwnedComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        IntegralHeight = false;
        MaxDropDownItems = 14;
        BackColor = DesignTokens.Surface;
        ForeColor = DesignTokens.TextPrimary;
        Font = DesignTokens.Type(DesignTokens.BodySize);
        ItemHeight = Math.Max(20, Font.Height + 8);
        AccessibleRole = AccessibleRole.ComboBox;
    }

    /// <summary>Applies the width tier: type step and matching item height.</summary>
    public void ApplyScale(UiScale scale)
    {
        Font = DesignTokens.Type(scale.BaseFont);
        ItemHeight = Math.Max(20, (int)Math.Round(scale.BaseFont * 1.6f) + 8);
        Invalidate();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var selected = (e.State & DrawItemState.Selected) != 0;
        using var fill = new SolidBrush(selected ? DesignTokens.SurfaceElevated : DesignTokens.Surface);
        e.Graphics.FillRectangle(fill, e.Bounds);
        if (e.Index >= 0 && e.Index < Items.Count)
        {
            TextRenderer.DrawText(e.Graphics, Items[e.Index]?.ToString() ?? "", Font, e.Bounds,
                DesignTokens.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}

/// <summary>
/// Owner-drawn tooltip replacing a bare <see cref="ToolTip"/>. The full,
/// unwrapped sentence is handed to the native host and the bubble is sized to
/// the measured text in the <see cref="ToolTip.Popup"/> handler, so a long hint
/// can never be clipped at a fixed character width. It paints the themed
/// surface instead of the system bubble.
/// </summary>
internal sealed class OwnedToolTip : IDisposable
{
    // Padding reserved inside the custom paint frame; the Popup handler adds
    // exactly this much to the measured text so the last line never clips.
    private const int PadX = 9;
    private const int PadY = 7;

    // Cap the text column so a very long hint stays readable. The bubble grows
    // vertically (multi-line) instead of running off the screen edge.
    private const int MaxTextWidth = 380;

    private readonly ToolTip _tip = new()
    {
        OwnerDraw = true,
        AutoPopDelay = 20000,
        InitialDelay = 350,
        ReshowDelay = 100,
    };

    public OwnedToolTip()
    {
        _tip.Draw += OnDraw;
        _tip.Popup += OnPopup;
    }

    public void SetToolTip(Control control, string text)
    {
        // Keep the full, unwrapped sentence for accessibility and tests; the
        // bubble is sized to it in OnPopup (no hard wrap).
        control.AccessibleDescription = text;
        _tip.SetToolTip(control, text);
    }

    public void Dispose() => _tip.Dispose();

    private void OnPopup(object? sender, PopupEventArgs e)
    {
        if (e.AssociatedControl is null) return;
        var text = _tip.GetToolTip(e.AssociatedControl);
        if (string.IsNullOrEmpty(text)) return;
        var measured = TextRenderer.MeasureText(text, DesignTokens.Meta,
            new Size(MaxTextWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
        e.ToolTipSize = new Size(
            measured.Width + PadX * 2,
            measured.Height + PadY * 2);
    }

    private void OnDraw(object? sender, DrawToolTipEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var fill = new SolidBrush(DesignTokens.SurfaceElevated);
        g.FillRectangle(fill, e.Bounds);
        using var border = new Pen(DesignTokens.Accent, 1F);
        g.DrawRectangle(border, 0, 0, Math.Max(1, e.Bounds.Width - 1), Math.Max(1, e.Bounds.Height - 1));
        TextRenderer.DrawText(g, e.ToolTipText, DesignTokens.Meta,
            new Rectangle(PadX, PadY,
                Math.Max(10, e.Bounds.Width - PadX * 2),
                Math.Max(10, e.Bounds.Height - PadY * 2)),
            DesignTokens.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix
                | TextFormatFlags.TextBoxControl);
    }
}

/// <summary>
/// Owner-drawn vertical scrollbar. Replaces the grey system bar that AutoScroll
/// injected into the class-skill list, which clashed with the dark console
/// surface.
/// </summary>
internal sealed class ThemedScrollBar : Control
{
    private int _minimum;
    private int _maximum = 100;
    private int _largeChange = 10;
    private int _value;
    private bool _dragging;
    private int _grabOffset;

    public event EventHandler? ValueChanged;

    public ThemedScrollBar()
    {
        Width = 10;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        BackColor = DesignTokens.Background;
        AccessibleRole = AccessibleRole.ScrollBar;
        AccessibleName = "Scroll";
        Cursor = Cursors.Hand;
    }

    public int Minimum { get => _minimum; set { _minimum = value; Invalidate(); } }
    public int Maximum { get => _maximum; set { _maximum = Math.Max(value, _minimum); Invalidate(); } }
    public int LargeChange { get => _largeChange; set { _largeChange = Math.Max(1, value); Invalidate(); } }

    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, _minimum, Math.Max(_minimum, _maximum));
            if (clamped == _value) return;
            _value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int Range => Math.Max(1, _maximum - _minimum + _largeChange);
    private int ThumbHeight => Math.Min(Height, Math.Max(20, (int)((long)Height * _largeChange / Range)));
    private int ThumbTop
    {
        get
        {
            var span = Math.Max(0, Height - ThumbHeight);
            var rawRange = Math.Max(1, _maximum - _minimum);
            return Math.Clamp((int)((long)span * (_value - _minimum) / rawRange), 0, span);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var track = new SolidBrush(DesignTokens.ScrollTrack);
        e.Graphics.FillRectangle(track, ClientRectangle);
        var thumb = new Rectangle(2, ThumbTop, Math.Max(2, Width - 4), ThumbHeight);
        using var fill = new SolidBrush(_dragging ? DesignTokens.AccentSoft : DesignTokens.ScrollThumb);
        e.Graphics.FillRectangle(fill, thumb);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) { base.OnMouseDown(e); return; }
        var top = ThumbTop;
        if (e.Y >= top && e.Y < top + ThumbHeight)
        {
            _dragging = true;
            _grabOffset = e.Y - top;
        }
        else
        {
            Value += e.Y < top ? -LargeChange : LargeChange;
        }
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            var span = Math.Max(1, Height - ThumbHeight);
            var pos = Math.Clamp(e.Y - _grabOffset, 0, span);
            var rawRange = Math.Max(1, _maximum - _minimum);
            Value = _minimum + (int)Math.Round((double)pos / span * rawRange);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        Invalidate();
        base.OnMouseUp(e);
    }
}

/// <summary>
/// AutoScroll host that hides the system scrollbar and drives a themed one.
/// The native bar is still the scroll engine (so <c>AutoScrollPosition</c> keeps
/// working for the eased wheel), but it is never shown.
/// </summary>
internal class ThemedScrollHost : Panel
{
    protected ThemedScrollBar Bar { get; }

    private VScrollBar? _native;

    protected ThemedScrollHost()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        Bar = new ThemedScrollBar { Dock = DockStyle.Right, Visible = false };
        Bar.ValueChanged += (_, _) => AutoScrollPosition = new Point(0, Bar.Value);
        Controls.Add(Bar);
        // Set AutoScroll last: enabling it forces a layout, and OnLayout syncs
        // the bar, so the bar must already exist.
        AutoScroll = true;
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is VScrollBar native)
        {
            _native = native;
            native.Visible = false;
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        SyncBar();
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        SyncBar();
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        SyncBar();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        SyncBar();
    }

    protected void SyncBar()
    {
        if (Bar is null || _syncingBar) return; // layout can fire from the base ctor before assignment
        _syncingBar = true;
        try
        {
            SyncBarCore();
        }
        finally
        {
            _syncingBar = false;
        }
    }

    private bool _syncingBar;

    private void SyncBarCore()
    {
        if (_native is null)
        {
            foreach (Control child in Controls)
            {
                if (child is VScrollBar native) { _native = native; break; }
            }
        }
        if (_native is { Visible: true }) _native.Visible = false;

        var max = Math.Max(0, VerticalScroll.Maximum - VerticalScroll.LargeChange + 1);
        if (max <= 0)
        {
            Bar.Visible = false;
            return;
        }
        Bar.Visible = true;
        Bar.Minimum = 0;
        Bar.Maximum = Math.Max(1, VerticalScroll.Maximum);
        Bar.LargeChange = Math.Max(1, ClientSize.Height);
        Bar.Value = Math.Clamp(-AutoScrollPosition.Y, 0, Bar.Maximum);
    }
}

/// <summary>
/// Multi-column legend (S5): the single ellipsised sentence ran out of width
/// and hid half the key. Each entry is a brass/neutral marker plus its word.
/// </summary>
internal sealed class LegendGrid : Control
{
    private readonly (string Text, Color Mark)[] _items;

    public int Columns { get; set; } = 2;

    public LegendGrid(params (string Text, Color Mark)[] items)
    {
        _items = items;
        Font = DesignTokens.Type(Math.Max(8f, DesignTokens.BodySize - 1f));
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor
            | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Color.Transparent;
        ForeColor = DesignTokens.TextSecondary;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = string.Join(". ", System.Array.ConvertAll(items, i => i.Text));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var cols = Math.Max(1, Columns);
        var rows = (int)Math.Ceiling(_items.Length / (double)cols);
        var rowHeight = Math.Max(12, Height / Math.Max(1, rows));
        var colWidth = Math.Max(24, Width / cols);
        for (var i = 0; i < _items.Length; i++)
        {
            var col = i % cols;
            var row = i / cols;
            var x = col * colWidth;
            var y = row * rowHeight + Math.Max(0, (rowHeight - Font.Height) / 2);
            using var mark = new SolidBrush(_items[i].Mark);
            e.Graphics.FillRectangle(mark, x, y + Font.Height / 2 - 3, 6, 6);
            TextRenderer.DrawText(e.Graphics, _items[i].Text, Font,
                new Rectangle(x + 11, y, Math.Max(10, colWidth - 14), Font.Height + 2),
                ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }
}
