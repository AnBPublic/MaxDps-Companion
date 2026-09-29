using System.Drawing.Drawing2D;
using System.Text;

namespace MaxDpsCompanion;

/// <summary>
/// The "Class skills" screen: pick a class + spec, then toggle every known
/// ability of that spec, grouped by section (Shared / Main rotation /
/// Offensive / Defensive / Movement). Rows carry the game skill icon on the
/// left (downloaded once into <c>assets/icons</c>, placeholder tile until
/// then), a thick name, a one-line "Recommendation: On/Off" subtitle, a
/// compact intelligence-status line and the toggle. Shared abilities are
/// shown once for the class; spec-only abilities live in the spec panel.
///
/// Motion (skill: apple-design): screen enter/exit is a scrim fade + a short
/// settle slide, a spec change settles the list, and wheel scrolling is
/// eased — all driven by one 15 ms timer and fully interruptible. Nothing
/// here reads game state; the caller supplies the catalog, book and policy.
/// </summary>
internal sealed class ClassSkillsView : Panel
{
    private readonly AbilityCatalog _catalog;
    private readonly ClassSpellBook _book;
    private readonly Func<AbilityDefinition, bool> _isEnabled;
    private readonly Action<AbilityDefinition, bool> _setEnabled;

    private readonly Panel _shell = new() { Dock = DockStyle.Fill, BackColor = ConsolePalette.Iron };
    private readonly Panel _header = new();
    private readonly TableLayoutPanel _toolbar = new();
    private readonly ComboBox _classBox = new();
    private readonly ComboBox _specBox = new();
    private readonly SmoothScrollPanel _scroll = new() { Dock = DockStyle.Fill };
    private readonly TableLayoutPanel _stack = new();
    private readonly ToolTip _tip = new();
    private readonly Label _empty = new();

    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };
    private float _fade;
    private float _slide;
    private bool _closing;

    public event Action? Closed;

    /// <summary>Short display names for the wire class tokens.</summary>
    private static readonly Dictionary<string, string> ClassDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DEATHKNIGHT"] = "Death Knight",
        ["DEMONHUNTER"] = "Demon Hunter",
        ["WARRIOR"] = "Warrior",
        ["PALADIN"] = "Paladin",
        ["HUNTER"] = "Hunter",
        ["ROGUE"] = "Rogue",
        ["PRIEST"] = "Priest",
        ["SHAMAN"] = "Shaman",
        ["MAGE"] = "Mage",
        ["WARLOCK"] = "Warlock",
        ["MONK"] = "Monk",
        ["DRUID"] = "Druid",
        ["EVOKER"] = "Evoker",
    };

    public ClassSkillsView(
        AbilityCatalog catalog,
        ClassSpellBook book,
        Func<AbilityDefinition, bool> isEnabled,
        Action<AbilityDefinition, bool> setEnabled)
    {
        _catalog = catalog;
        _book = book;
        _isEnabled = isEnabled;
        _setEnabled = setEnabled;

        Dock = DockStyle.Fill;
        BackColor = Color.Transparent;
        Visible = false;
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);

        BuildShell();
        _anim.Tick += (_, _) => AnimTick();
    }

    /// <summary>
    /// The transition dim is painted by the screen itself, BEHIND the shell:
    /// a separate transparent scrim control made the sibling tree skip during
    /// composited renders (the shell's window was erased by the scrim's
    /// simulated-transparency repaint), and painting here keeps the fade
    /// exactly one layer deep.
    /// </summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_fade > 0.01f)
        {
            var alpha = (int)Math.Round(_fade * 0.55f * 255);
            using var brush = new SolidBrush(Color.FromArgb(Math.Clamp(alpha, 0, 255), 4, 10, 15));
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }
    }

    // ---- shell -------------------------------------------------------------

    private void BuildShell()
    {
        _header.Dock = DockStyle.Top;
        _header.Height = 48;
        _header.BackColor = Color.Transparent;

        var back = new ChamferButton
        {
            Text = "Back",
            Role = ButtonRole.Ghost,
            Dock = DockStyle.Right,
            Width = 96,
            AutoSize = false,
            Margin = new Padding(0, 8, 8, 8),
        };
        back.Click += (_, _) => Close();

        // Same title-bar language as the Advanced screen: generated app icon,
        // thick title, Back at the right edge.
        var icon = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(24, 24),
            Location = new Point(0, 12),
            BackColor = Color.Transparent,
            Image = MainForm.AppIconForUi(),
        };

        var title = new Label
        {
            Text = "Class skills",
            AutoSize = false,
            Location = new Point(34, 0),
            Size = new Size(300, 48),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(MainForm.UiFontPublic, 11.5F, FontStyle.Bold),
            ForeColor = ConsolePalette.Bone,
            BackColor = Color.Transparent,
        };

        _header.Controls.Add(title);
        _header.Controls.Add(icon);
        _header.Controls.Add(back);

        _toolbar.Dock = DockStyle.Top;
        _toolbar.Height = 44;
        _toolbar.ColumnCount = 5;
        _toolbar.RowCount = 1;
        _toolbar.BackColor = Color.Transparent;
        _toolbar.Margin = Padding.Empty;
        _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        _toolbar.Controls.Add(FieldCaption("Class"), 0, 0);
        StyleCombo(_classBox);
        _toolbar.Controls.Add(_classBox, 1, 0);
        _toolbar.Controls.Add(FieldCaption("Spec"), 2, 0);
        StyleCombo(_specBox);
        _toolbar.Controls.Add(_specBox, 3, 0);

        var legend = new Label
        {
            Text = "Verified = live-client checked · MaxDps-backed = MaxDps decides · Manual = never automatic · Incomplete = not yet researched",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(MainForm.UiFontPublic, 8.25F),
            ForeColor = ConsolePalette.Tidewash,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
            Margin = new Padding(14, 0, 6, 0),
        };
        _toolbar.Controls.Add(legend, 4, 0);

        _classBox.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing) return;
            PopulateSpecs();
            Rebuild();
            SettleList();
        };
        _specBox.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing) return;
            Rebuild();
            SettleList();
        };

        _stack.Dock = DockStyle.Top;
        _stack.ColumnCount = 1;
        _stack.AutoSize = true;
        _stack.BackColor = Color.Transparent;
        _stack.Padding = Padding.Empty;

        _empty.Text = "Pick a class and spec to see its abilities.";
        _empty.AutoSize = false;
        _empty.AutoEllipsis = true;
        _empty.Dock = DockStyle.Top;
        _empty.Height = 60;
        _empty.TextAlign = ContentAlignment.MiddleLeft;
        _empty.ForeColor = ConsolePalette.Tidewash;
        _empty.BackColor = Color.Transparent;
        _empty.Visible = false;

        _scroll.AutoScroll = true;
        _scroll.BackColor = Color.Transparent;
        _scroll.Padding = new Padding(0, 0, 6, 0);
        _scroll.Controls.Add(_empty);
        _scroll.Controls.Add(_stack);

        _shell.Controls.Add(_scroll);
        _shell.Controls.Add(_toolbar);
        _shell.Controls.Add(_header);
        Controls.Add(_shell);
        _shell.BringToFront();
    }

    private static Label FieldCaption(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight,
        Font = new Font(MainForm.UiFontPublic, 9F),
        ForeColor = ConsolePalette.Tidewash,
        BackColor = Color.Transparent,
        Margin = new Padding(0, 0, 3, 0),
    };

    private static void StyleCombo(ComboBox box)
    {
        box.DropDownStyle = ComboBoxStyle.DropDownList;
        box.FlatStyle = FlatStyle.Flat;
        box.BackColor = ConsolePalette.Iron;
        box.ForeColor = ConsolePalette.Bone;
        box.Dock = DockStyle.Fill;
        box.Margin = new Padding(0, 6, 0, 6);
        box.IntegralHeight = false;
        box.MaxDropDownItems = 14;
        // Owner-drawn so the dropdown matches the dark theme (the native
        // DropDownList paints a system-white edit box even with BackColor set).
        box.DrawMode = DrawMode.OwnerDrawFixed;
        box.ItemHeight = 22;
        box.DrawItem += (_, e) =>
        {
            var selected = (e.State & DrawItemState.Selected) != 0;
            using var fill = new SolidBrush(selected ? ConsolePalette.Field : ConsolePalette.Iron);
            e.Graphics.FillRectangle(fill, e.Bounds);
            if (e.Index >= 0)
            {
                TextRenderer.DrawText(e.Graphics, box.Items[e.Index]?.ToString() ?? "", box.Font,
                    e.Bounds, ConsolePalette.Bone,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        };
    }

    /// <summary>Snapshot/UI-test diagnostics: what the screen currently holds.</summary>
    internal string DebugState
    {
        get
        {
            var first = _stack.Controls.Count > 0 ? _stack.Controls[0] : null;
            var firstBounds = first is null ? "-" : string.Join(",", first.Bounds.X, first.Bounds.Y, first.Bounds.Width, first.Bounds.Height);
            return
                $"visible={Visible} wantVisible={_visibleRequested} header={_header.Controls.Count} rows={_stack.Controls.Count} " +
                $"class={SelectedClass ?? "-"} spec={SelectedSpec ?? "-"} scroll={_scroll.Controls.Count} " +
                $"shell={_shell.Bounds.X},{_shell.Bounds.Y},{_shell.Bounds.Width}x{_shell.Bounds.Height} " +
                $"scrollB={_scroll.Bounds.X},{_scroll.Bounds.Y},{_scroll.Bounds.Width}x{_scroll.Bounds.Height} " +
                $"stackH={_stack.Height} stackVis={_stack.Visible} stackTop={_stack.Top} " +
                $"first={firstBounds} shellZ={Controls.GetChildIndex(_shell)} " +
                $"handles self={IsHandleCreated} shell={_shell.IsHandleCreated} scroll={_scroll.IsHandleCreated} stack={_stack.IsHandleCreated}";
        }
    }

    /// <summary>Deterministic snapshot state: jumps the transition to its end.</summary>
    internal void SnapToShown()
    {
        _closing = false;
        _fade = 1f;
        _slide = 0f;
        _settle = 0f;
        _settleTarget = 0f;
        _shell.Padding = Padding.Empty;
        _scroll.Padding = new Padding(0, 0, 6, 0);
        _anim.Stop();
        PerformLayout();
        Invalidate(true);
        _shell.CreateControl();
        _header.CreateControl();
        _scroll.CreateControl();
        _stack.CreateControl();
        _shell.PerformLayout();
        _scroll.PerformLayout();

    }

    // ---- open / close + animation -----------------------------------------

    /// <summary>Shows the screen, preselecting the live class/spec when known.</summary>
    public void Open(string? liveClass, string? liveSpec)
    {
        _syncing = true;
        try
        {
            _classBox.Items.Clear();
            foreach (var className in AbilityCatalog.ClassOrder)
            {
                if (className.Length == 0) continue;
                _classBox.Items.Add(ClassDisplayNames.TryGetValue(className, out var display) ? display : className);
            }
            var index = liveClass is null ? 0 : Array.IndexOf(AbilityCatalog.ClassOrder, liveClass.ToUpperInvariant()) - 1;
            if (index < 0 || index >= _classBox.Items.Count) index = 0;
            _classBox.SelectedIndex = index;
            PopulateSpecs();
            if (liveSpec is not null)
            {
                var specIndex = _specBox.Items.IndexOf(liveSpec);
                if (specIndex >= 0) _specBox.SelectedIndex = specIndex;
            }
        }
        finally
        {
            _syncing = false;
        }
        // D6 perf: recreating every ability row + its window handles on each
        // popup open is multi-second. The tree only depends on the selected
        // class/spec, so rebuild only when that selection actually changes.
        var sameSelection = string.Equals(_builtClass, SelectedClass, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_builtSpec, SelectedSpec, StringComparison.Ordinal);
        if (!sameSelection) Rebuild();
        _closing = false;
        _fade = 0f;
        _slide = 14f;
        _visibleRequested = true;
        Visible = true;
        BringToFront();
        _anim.Start();
    }

    public void Close()
    {
        if (!Visible || _closing) return;
        _closing = true;
        _anim.Start();
    }

    private void AnimTick()
    {
        var fadeTarget = _closing ? 0f : 1f;
        var slideTarget = _closing ? 14f : 0f;
        _fade += (fadeTarget - _fade) * 0.30f;
        _slide += (slideTarget - _slide) * 0.30f;

        var fadeDone = Math.Abs(_fade - fadeTarget) < 0.02f && Math.Abs(_slide - slideTarget) < 0.3f;
        if (fadeDone)
        {
            _fade = fadeTarget;
            _slide = slideTarget;
        }

        var settleDone = Math.Abs(_settle - _settleTarget) < 0.5f;
        if (!settleDone)
        {
            _settle += (_settleTarget - _settle) * 0.35f;
            if (Math.Abs(_settle - _settleTarget) < 0.5f) _settle = _settleTarget;
            _scroll.Padding = new Padding(0, (int)Math.Round(_settle), 6, 0);
        }

        if (fadeDone && settleDone) _anim.Stop();

        Invalidate();
        _shell.Padding = new Padding(0, (int)Math.Round(_slide), 0, 0);

        if (fadeDone && _closing)
        {
            _visibleRequested = false;
            Visible = false;
            Closed?.Invoke();
        }
    }

    private bool _syncing;

    // D6: the (class, spec) the current row tree was built for; a reopen with
    // the same selection keeps the tree instead of recreating every row.
    private string? _builtClass;
    private string? _builtSpec;

    private void PopulateSpecs()
    {
        var wasSyncing = _syncing;
        _syncing = true;
        try
        {
            _specBox.Items.Clear();
            var className = SelectedClass;
            if (className is null) return;
            if (!AbilityCatalog.SpecOrder.TryGetValue(className, out var specs)) return;
            for (var i = 1; i < specs.Length; i++) _specBox.Items.Add(specs[i]);
            if (_specBox.Items.Count > 0) _specBox.SelectedIndex = 0;
        }
        finally
        {
            _syncing = wasSyncing;
        }
    }

    private string? SelectedClass =>
        _classBox.SelectedIndex < 0 ? null : AbilityCatalog.ClassOrder[_classBox.SelectedIndex + 1];

    private string? SelectedSpec =>
        _specBox.SelectedItem as string;

    private void SettleList()
    {
        _scroll.AutoScrollPosition = new Point(0, 0);
        _scroll.Padding = new Padding(0, 10, 6, 0);
        _settle = 10f;
        _settleTarget = 0f;
        _anim.Start();
    }

    private float _settle;
    private float _settleTarget;
    private bool _visibleRequested;

    // ---- list --------------------------------------------------------------

    private void Rebuild()
    {
        _stack.SuspendLayout();
        try
        {
            foreach (var control in _stack.Controls.Cast<Control>().ToArray()) control.Dispose();
            _stack.Controls.Clear();
            _stack.RowStyles.Clear();
            _stack.RowCount = 0;

            var className = SelectedClass;
            var specName = SelectedSpec;
            _builtClass = className;
            _builtSpec = specName;
            if (className is null || specName is null)
            {
                _empty.Visible = true;
                return;
            }

            var build = ClassSkillTree.Build(_catalog, _book, className, specName);
            // Stream 1 §1.1: no speculative self-heal ids. When the curated
            // catalog has no verified solo self-heal for the spec (DH today),
            // say so instead of inventing an owed spell id.
            var noSelfHeal = _catalog.Extras(className, specName, AbilityCategory.SelfHeal).Length == 0;
            var rowCount = 1 + build.Shared.Count
                + build.Groups.Sum(g => g.Value.Count + 1)
                + (noSelfHeal ? 1 : 0);
            _stack.RowCount = rowCount;

            var row = 0;
            AddSectionHeader($"Shared — all {ClassDisplay(className)} specs", ref row);
            foreach (var ability in build.Shared) AddAbilityRow(ability, ref row);
            foreach (var group in ClassSkillTree.SectionOrder)
            {
                if (!build.Groups.TryGetValue(group, out var list) || list.Count == 0) continue;
                AddSectionHeader(ClassSkillTree.SectionTitle(group), ref row);
                foreach (var ability in list) AddAbilityRow(ability, ref row);
            }
            if (noSelfHeal) AddSelfHealNoteRow(className, specName, ref row);

            _empty.Visible = rowCount <= 1;
            if (_empty.Visible)
            {
                _empty.Text = "No abilities known for this class/spec yet.";
                _empty.BringToFront();
            }
        }
        finally
        {
            _stack.ResumeLayout();
        }
    }

    private static string ClassDisplay(string wire) =>
        ClassDisplayNames.TryGetValue(wire, out var display) ? display : wire;

    private void AddSectionHeader(string title, ref int row)
    {
        var header = new SectionHeader(title) { Dock = DockStyle.Fill, Margin = new Padding(0, 16, 0, 4) };
        _stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        _stack.Controls.Add(header, 0, row);
        row++;
    }

    /// <summary>
    /// Honest empty-state for a spec with no verified solo self-heal (DH
    /// Havoc/Devourer today): the companion will not invent an owed spell id,
    /// so the screen states the gap instead of fabricating a row.
    /// </summary>
    private void AddSelfHealNoteRow(string className, string specName, ref int row)
    {
        var note = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = $"No verified solo self-heal for {ClassDisplay(className)} / {specName} — "
                + "the companion will not invent one; MaxDps and your own kit decide heals.",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ConsolePalette.Tidewash,
            BackColor = Color.Transparent,
            Font = new Font(MainForm.UiFontPublic, 9F, FontStyle.Italic),
            Padding = new Padding(4, 0, 4, 0),
            Margin = new Padding(0, 2, 0, 2),
        };
        _stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        _stack.Controls.Add(note, 0, row);
        row++;
    }

    private void AddAbilityRow(AbilityDefinition ability, ref int row)
    {
        var item = new AbilityToggleRow(ability, _isEnabled(ability), _tip, (on) =>
        {
            _setEnabled(ability, on);
            return _isEnabled(ability);
        });
        item.Dock = DockStyle.Fill;
        item.Margin = new Padding(0, 1, 0, 1);
        _stack.RowStyles.Add(new RowStyle(SizeType.Absolute, AbilityToggleRow.RowHeight + 2));
        _stack.Controls.Add(item, 0, row);
        row++;
    }
}

/// <summary>Eased wheel scrolling for the ability list (never fights a drag).</summary>
internal sealed class SmoothScrollPanel : Panel
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private int _target;
    private bool _running;

    public SmoothScrollPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        _timer.Tick += (_, _) => Tick();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        var current = -AutoScrollPosition.Y;
        if (!_running) _target = current;
        _target = Math.Max(0, _target - e.Delta);
        _target = Math.Min(_target, VerticalScroll.Maximum);
        _running = true;
        _timer.Start();
    }

    private void Tick()
    {
        var current = -AutoScrollPosition.Y;
        var next = (int)Math.Round(current + (_target - current) * 0.35);
        if (Math.Abs(_target - next) <= 1)
        {
            next = _target;
            _running = false;
            _timer.Stop();
        }
        AutoScrollPosition = new Point(0, next);
    }
}

/// <summary>Thick section headline with a hairline rule.</summary>
internal sealed class SectionHeader : Control
{
    public SectionHeader(string text)
    {
        Text = text;
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var font = new Font(MainForm.UiFontPublic, 12.5F, FontStyle.Bold);
        var y = Math.Max(0, (Height - font.Height + 12) / 2 - 3);
        TextRenderer.DrawText(e.Graphics, Text, font,
            new Point(2, y), ConsolePalette.Brass,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        using var pen = new Pen(Color.FromArgb(70, 158, 122, 46));
        e.Graphics.DrawLine(pen, 0, Height - 5, Width, Height - 5);
        font.Dispose();
    }
}

/// <summary>44×44 skill icon tile; paints a placeholder until the icon is cached.</summary>
internal sealed class IconTile : Control
{
    private Image? _image;

    public IconTile()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(44, 44);
    }

    public void SetImage(Image? image)
    {
        _image = image;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (_image is { } image)
        {
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(image, rect);
        }
        else
        {
            using var fill = new SolidBrush(Color.FromArgb(26, 52, 68));
            e.Graphics.FillRectangle(fill, rect);
            using var font = new Font(MainForm.UiFontPublic, 13F, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, "?", font, rect, ConsolePalette.Tidewash,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        using var border = new Pen(Color.FromArgb(64, 120, 143, 160));
        e.Graphics.DrawRectangle(border, rect);
    }
}

/// <summary>
/// One ability row: icon on the left spanning the text block, the name in
/// semibold, a one-line "Recommendation: On/Off" subtitle, a compact
/// intelligence-status line ("Research-backed · Major · 3m", muted italic for
/// manual-by-design rows) and the toggle on the right. The full why-block
/// (context, requirements, source, relations) lives in the hover tooltip only.
/// </summary>
internal sealed class AbilityToggleRow : Panel
{
    public const int RowHeight = 88;

    private readonly AbilityToggleRowState _state;
    private readonly IconTile _icon = new();
    private readonly Label _name = new();
    private readonly Label _subtitle = new();
    private readonly Label _status = new();
    private readonly ToggleSwitch _toggle = new();
    private readonly AbilityDefinition _ability;
    private readonly Func<bool, bool> _apply;

    public AbilityToggleRow(AbilityDefinition ability, bool enabled, ToolTip tip, Func<bool, bool> apply)
    {
        _ability = ability;
        _apply = apply;
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Height = RowHeight;

        _name.Text = ability.Name;
        _name.AutoSize = false;
        _name.AutoEllipsis = true;
        _name.TextAlign = ContentAlignment.MiddleLeft;
        _name.Font = new Font(MainForm.UiFontPublic, 11.5F, FontStyle.Bold);
        _name.ForeColor = ConsolePalette.Bone;
        _name.BackColor = Color.Transparent;

        _subtitle.AutoSize = false;
        _subtitle.AutoEllipsis = true;
        _subtitle.TextAlign = ContentAlignment.MiddleLeft;
        _subtitle.Font = new Font(MainForm.UiFontPublic, 9.75F);
        _subtitle.ForeColor = ConsolePalette.Tidewash;
        _subtitle.BackColor = Color.Transparent;

        var manual = ability.NeverAutomatic || ability.Automation == AutomationContext.Manual;
        _status.Text = RowStatusLabel(ability);
        _status.AutoSize = false;
        _status.AutoEllipsis = true;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Font = new Font(MainForm.UiFontPublic, 8.75F, manual ? FontStyle.Italic : FontStyle.Regular);
        _status.ForeColor = manual ? Color.FromArgb(0x6E, 0x7D, 0x86) : ConsolePalette.Tidewash;
        _status.BackColor = Color.Transparent;

        _toggle.Checked = enabled;
        _toggle.AccessibleName = $"{ability.Name} automatic use";
        _toggle.CheckedChanged += (_, _) =>
        {
            var effective = _apply(_toggle.Checked);
            if (effective != _toggle.Checked) _toggle.Checked = effective;   // converges on the second pass
            UpdateSubtitle();
        };

        var tooltipText = DescribeTooltip(ability)
            + "\nVeto: OFF prohibits automatic use (companion AND addon; an addon veto wins).";
        tip.SetToolTip(_name, tooltipText);
        tip.SetToolTip(_subtitle, tooltipText);
        tip.SetToolTip(_status, tooltipText);
        tip.SetToolTip(_toggle, tooltipText);
        tip.SetToolTip(this, tooltipText);

        Controls.Add(_icon);
        Controls.Add(_name);
        Controls.Add(_subtitle);
        Controls.Add(_status);
        Controls.Add(_toggle);

        _state = new AbilityToggleRowState(ability.SpellId, _icon);
        SpellIconCache.Instance.IconReady += _state.OnIconReady;
        UpdateSubtitle();

        var image = SpellIconCache.Instance.TryGet(ability.SpellId);
        if (image is not null) _icon.SetImage(image);
    }

    private static string SourceLabel(AbilityProvenance provenance) => provenance switch
    {
        AbilityProvenance.Curated => "Curated",
        AbilityProvenance.Vendor => "MaxDps data",
        _ => "Class list",
    };

    private void UpdateSubtitle() =>
        _subtitle.Text = _toggle.Checked
            ? "Veto: off · automatic use allowed"
            : "Veto: on · never automatic";

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        const int iconSize = 44;
        _icon.Bounds = new Rectangle(4, (Height - iconSize) / 2, iconSize, iconSize);
        var textX = _icon.Right + 14;
        var toggleX = Width - 8 - _toggle.Width;
        var textWidth = Math.Max(40, toggleX - 14 - textX);
        _name.Bounds = new Rectangle(textX, 10, textWidth, 24);
        _subtitle.Bounds = new Rectangle(textX, 34, textWidth, 18);
        _status.Bounds = new Rectangle(textX, 54, textWidth, 18);
        _toggle.Location = new Point(toggleX, (Height - _toggle.Height) / 2);
    }

    // ---- row status + tooltip (pure formatting, no locks) ------------------

    private static string RowStatusLabel(AbilityDefinition ability)
    {
        var parts = new List<string> { StatusLabel(ability.Status) };
        if (ability.IsSurvival && ability.Tier != DefensiveTier.None) parts.Add(TierLabel(ability.Tier));
        if (ability.CooldownMs > 0) parts.Add(CooldownLabel(ability.CooldownMs));
        return string.Join(" · ", parts);
    }

    private static string StatusLabel(IntelligenceStatus status) => status switch
    {
        IntelligenceStatus.Verified => "Verified",
        IntelligenceStatus.ResearchBacked => "Research-backed",
        IntelligenceStatus.MaxDpsBacked => "MaxDps-backed",
        IntelligenceStatus.CompanionRule => "Companion rule",
        IntelligenceStatus.ConservativeSafety => "Safety rule",
        IntelligenceStatus.Heuristic => "Heuristic",
        IntelligenceStatus.ManualByDesign => "Manual by design",
        IntelligenceStatus.UnsafeToAutomate => "Unsafe",
        IntelligenceStatus.Incomplete => "Incomplete",
        _ => "Unknown",
    };

    private static string TierLabel(DefensiveTier tier) => tier switch
    {
        DefensiveTier.Minor => "Minor",
        DefensiveTier.Major => "Major",
        DefensiveTier.Immunity => "Immunity",
        _ => "",
    };

    private static string CooldownLabel(int cooldownMs)
    {
        var seconds = (int)Math.Round(cooldownMs / 1000.0);
        if (seconds < 1) return "<1s";
        if (seconds < 60) return $"{seconds}s";
        var minutes = seconds / 60;
        var rest = seconds % 60;
        if (rest == 0 || minutes >= 10) return $"{minutes}m";
        return $"{minutes}m {rest}s";
    }

    private static string DescribeTooltip(AbilityDefinition ability)
    {
        var text = new StringBuilder(384);
        text.Append(ability.Name).Append('\n')
            .Append("Spell ID ").Append(ability.SpellId).Append('\n')
            .Append("Provenance: ").Append(SourceLabel(ability.Provenance)).Append('\n')
            .Append("Default: ").Append(ability.NeverAutomatic ? "Off" : "On");
        if (!string.IsNullOrWhiteSpace(ability.Note)) AppendWrapped(text, ability.Note!, 100);

        foreach (var line in WhenLines(ability)) AppendWrapped(text, line, 100);

        var requires = RequirementLabel(ability.Requires);
        if (requires.Length > 0) text.Append('\n').Append("Requires: ").Append(requires);
        if (ability.InterruptKind != InterruptKind.Unknown)
            text.Append('\n').Append("Interrupt: ").Append(InterruptLabel(ability.InterruptKind));
        if (ability.OffensiveUsage != OffensiveUsage.Unknown)
            text.Append('\n').Append("Offensive: ").Append(OffensiveLabel(ability.OffensiveUsage));
        if (ability.MobilityKind != MobilityKind.Unknown)
            text.Append('\n').Append("Mobility: ").Append(MobilityLabel(ability.MobilityKind));
        if (!string.IsNullOrWhiteSpace(ability.Source))
            AppendWrapped(text, "Source: " + Ellipsize(ability.Source!, 160), 100);
        if (!string.IsNullOrWhiteSpace(ability.PatchVerified))
            text.Append('\n').Append("Patch verified: ").Append(ability.PatchVerified);
        if (ability.Opportunity != OpportunityCost.Unknown)
            text.Append('\n').Append("Opportunity: ").Append(ability.Opportunity);
        if (!string.IsNullOrWhiteSpace(ability.TalentNote))
            AppendWrapped(text, "Talent: " + ability.TalentNote!, 100);
        if (!string.IsNullOrWhiteSpace(ability.HeroTalentNote))
            AppendWrapped(text, "Hero talent: " + ability.HeroTalentNote!, 100);
        if (ability.Relations.Length > 0)
            AppendWrapped(text, "Relations: " + RelationLabel(ability.Relations), 100);
        return text.ToString();
    }

    private static IReadOnlyList<string> WhenLines(AbilityDefinition ability)
    {
        var lines = new List<string>();
        if (ability.Automation == AutomationContext.Manual || ability.NeverAutomatic)
        {
            lines.Add("When it uses: manual — never automatic");
            return lines;
        }

        if (ability.IsSurvival)
        {
            var tier = ability.Tier == DefensiveTier.None ? "" : $" (tier {TierLabel(ability.Tier)})";
            lines.Add("When it uses: the player is " + UrgencyLabel(ability.MinimumUrgency) + tier);
            lines.Add(ability.MinimumUrgency == DefensiveUrgency.White
                ? "When it holds: at full health"
                : "When it holds: until the urgency floor is crossed");
            if (ability.UseBelowHpPct is int below) lines.Add($"When it uses: below {below}% health");
        }
        else if (ability.HasOffensiveCapability)
        {
            if (ability.HoldForBurst)
            {
                lines.Add("When it holds: for the burst window");
                lines.Add("When it uses: the burst window is open");
            }
            else
            {
                lines.Add(ability.MaxDps != MaxDpsRelationship.NotSurfaced
                    ? "When it uses: MaxDps decides"
                    : "When it uses: a companion rule opens it");
            }
            if (ability.EnemyCountMin is int enemies) lines.Add($"When it uses: {enemies}+ enemies");
        }
        else if (ability.HasMobilityCapability)
        {
            lines.Add("When it uses: " + MobilityUse(ability.MobilityKind));
            lines.Add("When it skips: no movement is needed");
        }
        else if (ability.HasInterruptCapability)
        {
            lines.Add("When it uses: the target is casting");
            lines.Add("When it skips: no interruptible cast is active");
        }
        else if (ability.HasSelfSustainCapability)
        {
            lines.Add(ability.UseBelowHpPct is int healBelow
                ? $"When it uses: below {healBelow}% health"
                : "When it uses: on a companion heal gate");
        }
        else if (ability.MaxDps != MaxDpsRelationship.NotSurfaced)
        {
            lines.Add("When it uses: MaxDps decides");
        }
        else
        {
            lines.Add("When it uses: on a companion rule");
        }

        if (ability.HealPctMaxHp > 0) lines.Add($"Heals: up to {ability.HealPctMaxHp}% max health");
        return lines;
    }

    private static string UrgencyLabel(DefensiveUrgency urgency) => urgency switch
    {
        DefensiveUrgency.Red => "at or below 30% health",
        DefensiveUrgency.Orange => "below 50% health",
        DefensiveUrgency.Yellow => "below full health",
        DefensiveUrgency.White => "at full health",
        _ => "at an unread health level",
    };

    private static string MobilityUse(MobilityKind kind) => kind switch
    {
        MobilityKind.GapCloser => "a gap closer is needed",
        MobilityKind.Disengage => "disengaging from the target",
        MobilityKind.Teleport => "repositioning is needed",
        MobilityKind.SpeedBurst => "extra movement speed is needed",
        MobilityKind.MovementImmunity => "movement must not be stopped",
        MobilityKind.Escape => "escaping a threat",
        _ => "movement is needed",
    };

    private static string InterruptLabel(InterruptKind kind) => kind switch
    {
        InterruptKind.Dedicated => "dedicated kick",
        InterruptKind.Silence => "silence",
        InterruptKind.Stun => "stun",
        InterruptKind.Displacement => "displacement",
        InterruptKind.Incapacitate => "incapacitate",
        _ => "unknown",
    };

    private static string OffensiveLabel(OffensiveUsage usage) => usage switch
    {
        OffensiveUsage.MajorBurst => "major burst",
        OffensiveUsage.MinorBurst => "minor burst",
        OffensiveUsage.ShortCooldown => "short cooldown",
        OffensiveUsage.Execute => "execute",
        OffensiveUsage.AoeOnly => "area only",
        OffensiveUsage.SingleTargetOnly => "single target only",
        OffensiveUsage.ProcDriven => "proc driven",
        OffensiveUsage.ResourceDriven => "resource driven",
        OffensiveUsage.WindowDriven => "window driven",
        OffensiveUsage.DefensiveOffensiveHybrid => "defensive/offensive hybrid",
        OffensiveUsage.Summon => "summon",
        OffensiveUsage.Transformation => "transformation",
        OffensiveUsage.Manual => "manual",
        _ => "unknown",
    };

    private static string MobilityLabel(MobilityKind kind) => kind switch
    {
        MobilityKind.GapCloser => "gap closer",
        MobilityKind.Disengage => "disengage",
        MobilityKind.Teleport => "teleport",
        MobilityKind.SpeedBurst => "speed burst",
        MobilityKind.MovementImmunity => "movement immunity",
        MobilityKind.Escape => "escape",
        _ => "unknown",
    };

    private static string RequirementLabel(AbilityRequirement requires)
    {
        if (requires == AbilityRequirement.None) return "";
        var parts = new List<string>();
        foreach (var flag in RequirementOrder)
            if ((requires & flag) != 0) parts.Add(RequirementName(flag));
        return string.Join(", ", parts);
    }

    private static readonly AbilityRequirement[] RequirementOrder =
    [
        AbilityRequirement.Target,
        AbilityRequirement.Enemy,
        AbilityRequirement.FriendlyTarget,
        AbilityRequirement.Combat,
        AbilityRequirement.Movement,
        AbilityRequirement.IncomingDamage,
        AbilityRequirement.EnemyCast,
        AbilityRequirement.Aura,
        AbilityRequirement.Talent,
        AbilityRequirement.Form,
        AbilityRequirement.SpecificCast,
    ];

    private static string RequirementName(AbilityRequirement flag) => flag switch
    {
        AbilityRequirement.FriendlyTarget => "friendly target",
        AbilityRequirement.IncomingDamage => "incoming damage",
        AbilityRequirement.EnemyCast => "enemy cast",
        AbilityRequirement.SpecificCast => "specific cast",
        _ => flag.ToString().ToLowerInvariant(),
    };

    private static string RelationLabel(AbilityRelation[] relations)
    {
        var parts = new List<string>(relations.Length);
        foreach (var relation in relations) parts.Add($"{relation.Kind} -> {relation.SpellId}");
        return string.Join(", ", parts);
    }

    private static string Ellipsize(string value, int max)
    {
        value = value.Trim();
        return value.Length <= max ? value : value[..(max - 3)].TrimEnd() + "...";
    }

    private static void AppendWrapped(StringBuilder text, string value, int width)
    {
        value = value.Trim();
        if (value.Length == 0) return;
        var start = 0;
        while (start < value.Length)
        {
            var end = Math.Min(value.Length, start + width);
            if (end < value.Length)
            {
                var space = value.LastIndexOf(' ', end - 1, end - start);
                if (space > start) end = space;
            }
            text.Append('\n').Append(value[start..end].Trim());
            start = end;
            while (start < value.Length && value[start] == ' ') start++;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) SpellIconCache.Instance.IconReady -= _state.OnIconReady;
        base.Dispose(disposing);
    }

    /// <summary>Keeps the icon tile in sync with the background cache.</summary>
    private sealed class AbilityToggleRowState
    {
        private readonly int _spellId;
        private readonly IconTile _tile;

        public AbilityToggleRowState(int spellId, IconTile tile)
        {
            _spellId = spellId;
            _tile = tile;
        }

        public void OnIconReady(int spellId)
        {
            if (spellId != _spellId || !_tile.IsHandleCreated) return;
            try
            {
                _tile.BeginInvoke(() =>
                {
                    var image = SpellIconCache.Instance.TryGet(_spellId);
                    if (image is not null) _tile.SetImage(image);
                });
            }
            catch
            {
                // the row vanished before the icon arrived: nothing to paint
            }
        }
    }
}
