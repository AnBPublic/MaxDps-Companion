using System.Diagnostics;

namespace MaxDpsCompanion;

/// <summary>
/// S6 (v3.5): the callbacks the Class Browser uses to read the resolved
/// registry state and to write the user knobs. The view never evaluates a
/// scheduler gate — it consumes the already-resolved <see cref="ClassOverlayEntry"/>
/// (registry + overlay + overrides via <see cref="AbilityOverrides.Apply"/>) and
/// the live verdict the host reads from the engine's published snapshot. The
/// scheduler owns evaluation.
/// </summary>
internal sealed class ClassBrowserHost
{
    public required Func<AbilityDefinition, bool> IsEnabled { get; init; }
    public required Action<AbilityDefinition, bool> SetEnabled { get; init; }

    /// <summary>The resolved registry entry for a spell (overlay + override applied), or null.</summary>
    public required Func<string, string, int, ClassOverlayEntry?> RegistryEntry { get; init; }

    /// <summary>Live why-held / why-ready verdict for a row, or null when unknown.</summary>
    public required Func<AbilityDefinition, string?> LiveVerdict { get; init; }

    public required Func<int, UserAbilityMode> ModeOf { get; init; }
    public required Action<int, UserAbilityMode> SetMode { get; init; }
    public required Func<int, int?> UrgencyFloorOf { get; init; }
    public required Action<int, int?> SetUrgencyFloor { get; init; }
    public required Action<int> ResetOverrides { get; init; }
}

/// <summary>
/// S6 (v3.5) Class Browser: one window with Class | Spec | Mode selectors and a
/// virtualized, owner-drawn row list (reusing <see cref="VirtualAbilityList"/>).
/// Each row shows icon, name, cooldown, tier + ownership badges, the live
/// why-held verdict and the ON/OFF toggle. Quick knobs (min HP%, urgency floor,
/// solo-only / normal-only / always) edit the selected row's overrides.
///
/// This supersedes the separate "Class skills" and "Explorer" screens and adds
/// no header of its own: the enclosing popup owns the single title/Back header.
/// </summary>
internal sealed class ClassBrowserView : Panel
{
    /// <summary>Mode selector: display label -> the ability tag the filter uses.</summary>
    internal static readonly (string Label, string Tag)[] ModeOptions =
    [
        ("All", "All"),
        ("Main", "Main"),
        ("Offensive", "Offensive"),
        ("Defensive", "Defensive"),
        ("Interrupt", "Interrupt"),
        ("CC", "CrowdControl"),
        ("Mobility", "Mobility"),
        ("Solo self-sustain", "Self Sustain"),
        ("Consumable", "Consumable"),
        ("Trinket", "Trinket"),
        ("Utility/manual", "Utility"),
    ];

    private static readonly (string Label, UserAbilityMode Mode)[] ScopeOptions =
    [
        ("Default", UserAbilityMode.Default),
        ("Always", UserAbilityMode.Always),
        ("Solo only", UserAbilityMode.SoloOnly),
        ("Normal only", UserAbilityMode.NormalOnly),
    ];

    private static readonly (string Label, int Value)[] UrgencyOptions =
    [
        ("Default", -1),
        ("White", 0),
        ("Yellow", 1),
        ("Orange", 2),
        ("Red", 3),
    ];

    private readonly AbilityCatalog _catalog;
    private readonly ClassBrowserHost _host;

    private readonly TableLayoutPanel _toolbar = new();
    private readonly OwnedComboBox _classBox = new();
    private readonly OwnedComboBox _specBox = new();
    private readonly OwnedComboBox _modeBox = new();
    private readonly Label _modeHint = new();
    private readonly NumericUpDown _hpFloor = new();
    private readonly OwnedComboBox _urgencyBox = new();
    private readonly OwnedComboBox _scopeBox = new();
    private readonly ChamferButton _reset = new() { Text = "Reset row", Role = ButtonRole.Ghost };
    private readonly Label _empty = new();
    private readonly VirtualAbilityList _list;
    private readonly OwnedToolTip _tip = new();

    private bool _syncing;
    private string? _presetTag;

    // D6 warm-open: the (class, spec, mode, hpFloor) the current list was built
    // for; a reopen with the same selection keeps the list instead of redoing
    // the filter + sort.
    private string? _builtClass;
    private string? _builtSpec;
    private string? _builtMode;
    private int _builtHpFloor = -1;

    private List<AbilityDefinition> _items = [];

    public ClassBrowserView(AbilityCatalog catalog, ClassBrowserHost host)
    {
        _catalog = catalog;
        _host = host;

        Dock = DockStyle.Fill;
        BackColor = DesignTokens.Background;
        Visible = false;
        Padding = new Padding(DesignTokens.SpaceS, DesignTokens.SpaceS, DesignTokens.SpaceS, DesignTokens.SpaceS);
        AccessibleRole = AccessibleRole.Pane;
        AccessibleName = "Class browser";

        _list = new VirtualAbilityList(host.IsEnabled) { Dock = DockStyle.Fill };
        _list.Toggled += (ability, on) => { _host.SetEnabled(ability, on); _list.Invalidate(); };
        _list.Selected += _ => SyncKnobsFromSelection();

        BuildShell();
    }

    /// <summary>Wall-clock of the last <see cref="Open"/> (warm-open budget &lt; 150 ms).</summary>
    internal double LastOpenMs { get; private set; }

    internal int RowCount => _list.Count;
    internal string SelectedModeForTest => _modeBox.SelectedItem as string ?? "";
    internal string SelectedClassForTest => SelectedClass ?? "";
    internal IReadOnlyList<string> ModeLabelsForTest { get; } = ModeOptions.Select(m => m.Label).ToArray();

    internal string DebugState =>
        $"visible={Visible} rows={_list.Count} class={SelectedClass ?? "-"} spec={SelectedSpec ?? "-"} " +
        $"mode={SelectedModeForTest} hpFloor={_hpFloor.Value} handles={IsHandleCreated}";

    /// <summary>S8/S5 seam: scale the selector type with the popup width tier.</summary>
    internal void ApplyScale(UiScale scale)
    {
        _classBox.ApplyScale(scale);
        _specBox.ApplyScale(scale);
        _modeBox.ApplyScale(scale);
        _urgencyBox.ApplyScale(scale);
        _scopeBox.ApplyScale(scale);
        _hpFloor.Font = DesignTokens.Type(Math.Max(8f, scale.BaseFont - 1f));
    }

    // ---- shell -------------------------------------------------------------

    private void BuildShell()
    {
        _toolbar.Dock = DockStyle.Top;
        _toolbar.Height = 78;
        _toolbar.ColumnCount = 6;
        _toolbar.RowCount = 2;
        _toolbar.BackColor = Color.Transparent;
        _toolbar.Margin = Padding.Empty;
        // Caption columns must fit "Class"/"Spec"/"Mode" at CaptionSize; 54 px
        // (50 px after the 4 px right margin) clears the widest, "Mode".
        _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
        _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 186));
        _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
        _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 166));
        _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
        _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        _toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));

        _toolbar.Controls.Add(FieldCaption("Class"), 0, 0);
        StyleCombo(_classBox);
        _classBox.AccessibleName = "Class";
        _toolbar.Controls.Add(_classBox, 1, 0);
        _toolbar.Controls.Add(FieldCaption("Spec"), 2, 0);
        StyleCombo(_specBox);
        _specBox.AccessibleName = "Specialization";
        _toolbar.Controls.Add(_specBox, 3, 0);
        _toolbar.Controls.Add(FieldCaption("Mode"), 4, 0);
        StyleCombo(_modeBox);
        _modeBox.AccessibleName = "Ability mode";
        foreach (var option in ModeOptions) _modeBox.Items.Add(option.Label);
        _modeBox.SelectedIndex = 0;
        _toolbar.Controls.Add(_modeBox, 5, 0);

        _classBox.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing) return;
            PopulateSpecs();
            Rebuild();
        };
        _specBox.SelectedIndexChanged += (_, _) => { if (!_syncing) Rebuild(); };
        _modeBox.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing) return;
            _presetTag = null;
            Rebuild();
        };

        BuildKnobRow();

        _empty.Text = "Pick a class and spec to browse its abilities.";
        _empty.Dock = DockStyle.Top;
        _empty.Height = 40;
        _empty.TextAlign = ContentAlignment.MiddleLeft;
        _empty.ForeColor = DesignTokens.TextSecondary;
        _empty.BackColor = Color.Transparent;
        _empty.Visible = false;

        _list.Dock = DockStyle.Fill;
        Controls.Add(_list);
        Controls.Add(_empty);
        Controls.Add(_toolbar);
    }

    private void BuildKnobRow()
    {
        var knobs = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(0, 2, 0, 0),
        };

        knobs.Controls.Add(KnobCaption("Min HP%"));
        _hpFloor.Minimum = 0;
        _hpFloor.Maximum = 100;
        _hpFloor.Value = 100;
        _hpFloor.Width = 64;
        _hpFloor.Font = DesignTokens.Type(DesignTokens.BodySize);
        _hpFloor.AccessibleName = "Minimum HP percent";
        _hpFloor.ValueChanged += (_, _) => Rebuild();
        knobs.Controls.Add(_hpFloor);

        knobs.Controls.Add(KnobCaption("Urgency floor"));
        StyleCombo(_urgencyBox);
        _urgencyBox.Width = 96;
        _urgencyBox.AccessibleName = "Urgency floor";
        foreach (var option in UrgencyOptions) _urgencyBox.Items.Add(option.Label);
        _urgencyBox.SelectedIndex = 0;
        _urgencyBox.Enabled = false;
        _urgencyBox.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing || _list.Selection is not { } ability) return;
            var value = UrgencyOptions[Math.Max(0, _urgencyBox.SelectedIndex)].Value;
            _host.SetUrgencyFloor(ability.SpellId, value < 0 ? null : value);
            RefreshLive();
        };
        knobs.Controls.Add(_urgencyBox);

        knobs.Controls.Add(KnobCaption("Scope"));
        StyleCombo(_scopeBox);
        _scopeBox.Width = 108;
        _scopeBox.AccessibleName = "Solo or normal scope";
        foreach (var option in ScopeOptions) _scopeBox.Items.Add(option.Label);
        _scopeBox.SelectedIndex = 0;
        _scopeBox.Enabled = false;
        _scopeBox.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing || _list.Selection is not { } ability) return;
            _host.SetMode(ability.SpellId, ScopeOptions[Math.Max(0, _scopeBox.SelectedIndex)].Mode);
            RefreshLive();
        };
        knobs.Controls.Add(_scopeBox);

        _reset.Enabled = false;
        _reset.AutoSize = false;
        _reset.Width = 96;
        _reset.Height = 26;
        _reset.Click += (_, _) =>
        {
            if (_list.Selection is not { } ability) return;
            _host.ResetOverrides(ability.SpellId);
            SyncKnobsFromSelection();
            RefreshLive();
        };
        knobs.Controls.Add(_reset);

        _modeHint.AutoSize = true;
        _modeHint.Margin = new Padding(12, 6, 0, 0);
        _modeHint.Font = DesignTokens.Type(DesignTokens.MetaSize, FontStyle.Italic);
        _modeHint.ForeColor = DesignTokens.TextMuted;
        _modeHint.Text = "Knobs edit the selected row; the scheduler still owns every gate.";
        knobs.Controls.Add(_modeHint);

        _toolbar.Controls.Add(knobs, 0, 1);
        _toolbar.SetColumnSpan(knobs, 6);
    }

    private static Label FieldCaption(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight,
        Font = DesignTokens.Type(DesignTokens.CaptionSize),
        ForeColor = DesignTokens.TextMuted,
        BackColor = Color.Transparent,
        Margin = new Padding(0, 0, 4, 0),
    };

    private static Label KnobCaption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = DesignTokens.Type(DesignTokens.MetaSize),
        ForeColor = DesignTokens.TextMuted,
        BackColor = Color.Transparent,
        Margin = new Padding(8, 6, 4, 0),
    };

    private static void StyleCombo(OwnedComboBox box)
    {
        box.Dock = DockStyle.Fill;
        box.Margin = new Padding(0, 4, 0, 4);
    }

    // ---- open / select -----------------------------------------------------

    /// <summary>
    /// Shows the browser, preselecting the live class/spec and, when
    /// <paramref name="preset"/> names a mode ("Offensive", "Self-heal",
    /// "Crowd control", …), that mode.
    /// </summary>
    public void Open(string? liveClass, string? liveSpec, string? preset = null)
    {
        var clock = Stopwatch.StartNew();
        _syncing = true;
        try
        {
            if (_classBox.Items.Count == 0) PopulateClasses();
            var classIndex = liveClass is null
                ? 0
                : Array.IndexOf(AbilityCatalog.ClassOrder, liveClass.ToUpperInvariant()) - 1;
            if (classIndex < 0 || classIndex >= _classBox.Items.Count) classIndex = 0;
            if (_classBox.SelectedIndex != classIndex) _classBox.SelectedIndex = classIndex;
            PopulateSpecs();
            if (liveSpec is not null)
            {
                var specIndex = _specBox.Items.IndexOf(liveSpec);
                if (specIndex >= 0 && _specBox.SelectedIndex != specIndex) _specBox.SelectedIndex = specIndex;
            }
            if (preset is not null)
            {
                var tag = AbilityViewPresets.NormalizeCategory(preset);
                for (var i = 0; i < ModeOptions.Length; i++)
                    if (string.Equals(ModeOptions[i].Tag, tag, StringComparison.Ordinal))
                    {
                        _modeBox.SelectedIndex = i;
                        break;
                    }
            }
        }
        finally
        {
            _syncing = false;
        }
        _presetTag = preset;
        Rebuild();
        Visible = true;
        BringToFront();
        LastOpenMs = clock.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// WS-C drill-through seam: opens the browser on a class/spec and selects
    /// the mode named by an Intelligence tile or a hero bubble tag.
    /// </summary>
    public void ApplyPreset(string tag, string? className = null, string? specName = null)
    {
        _presetTag = tag;
        _syncing = true;
        try
        {
            if (_classBox.Items.Count == 0) PopulateClasses();
            if (className is not null)
            {
                var classIndex = Array.IndexOf(AbilityCatalog.ClassOrder, className.ToUpperInvariant()) - 1;
                if (classIndex >= 0 && classIndex < _classBox.Items.Count) _classBox.SelectedIndex = classIndex;
            }
            PopulateSpecs();
            if (specName is not null)
            {
                var specIndex = _specBox.Items.IndexOf(specName);
                if (specIndex >= 0) _specBox.SelectedIndex = specIndex;
            }
            var normalized = AbilityViewPresets.NormalizeCategory(tag);
            for (var i = 0; i < ModeOptions.Length; i++)
                if (string.Equals(ModeOptions[i].Tag, normalized, StringComparison.Ordinal))
                {
                    _modeBox.SelectedIndex = i;
                    break;
                }
        }
        finally
        {
            _syncing = false;
        }
        Rebuild();
    }

    /// <summary>Re-reads the live verdicts and repaints without rebuilding rows.</summary>
    public void RefreshLive()
    {
        var verdicts = new Dictionary<int, string>();
        foreach (var ability in _items)
        {
            var verdict = _host.LiveVerdict(ability);
            if (!string.IsNullOrWhiteSpace(verdict)) verdicts[ability.SpellId] = verdict!;
        }
        _list.EmptyText = _empty.Text;
        _list.Verdicts = verdicts;
        _list.Invalidate();
    }

    private void PopulateClasses()
    {
        _classBox.Items.Clear();
        foreach (var className in AbilityCatalog.ClassOrder)
        {
            if (className.Length == 0) continue;
            _classBox.Items.Add(AbilityViewPresets.ClassDisplay(className));
        }
        if (_classBox.Items.Count > 0) _classBox.SelectedIndex = 0;
    }

    private void PopulateSpecs()
    {
        var className = SelectedClass;
        _specBox.Items.Clear();
        if (className is null) return;
        if (!AbilityCatalog.SpecOrder.TryGetValue(className, out var specs)) return;
        for (var i = 1; i < specs.Length; i++) _specBox.Items.Add(specs[i]);
        if (_specBox.Items.Count > 0) _specBox.SelectedIndex = 0;
    }

    private string? SelectedClass =>
        _classBox.SelectedIndex < 0 ? null : AbilityCatalog.ClassOrder[_classBox.SelectedIndex + 1];

    private string? SelectedSpec => _specBox.SelectedItem as string;

    // ---- filter / rebuild --------------------------------------------------

    private void Rebuild()
    {
        var className = SelectedClass;
        var specName = SelectedSpec;
        var mode = ModeOptions[Math.Max(0, _modeBox.SelectedIndex)].Tag;
        var hpFloor = (int)_hpFloor.Value;

        // D6: skip the filter + sort when nothing the list depends on changed.
        if (string.Equals(_builtClass, className, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_builtSpec, specName, StringComparison.Ordinal)
            && string.Equals(_builtMode, mode, StringComparison.Ordinal)
            && _builtHpFloor == hpFloor
            && _items.Count > 0)
        {
            RefreshLive();
            return;
        }
        _builtClass = className;
        _builtSpec = specName;
        _builtMode = mode;
        _builtHpFloor = hpFloor;

        var list = new List<AbilityDefinition>(96);
        foreach (var ability in _catalog.All)
        {
            if (!AbilityViewPresets.MatchesClassSpec(ability, className, specName)) continue;
            if (!MatchesMode(ability, mode, className, specName)) continue;
            if (hpFloor < 100 && ability.UseBelowHpPct is int gate && gate > hpFloor) continue;
            list.Add(ability);
        }
        list.Sort(static (a, b) =>
        {
            var byCategory = a.Category.CompareTo(b.Category);
            if (byCategory != 0) return byCategory;
            var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : a.SpellId.CompareTo(b.SpellId);
        });

        _items = list;
        _list.EmptyText = list.Count == 0
            ? $"No {ModeOptions[Math.Max(0, _modeBox.SelectedIndex)].Label} abilities for " +
              $"{(className is null ? "this class" : AbilityViewPresets.ClassDisplay(className))}" +
              $"{(specName is null ? "" : "/" + specName)}."
            : "";
        _empty.Text = _list.EmptyText;
        _empty.Visible = list.Count == 0;
        _list.SetItems(list);
        RefreshLive();
        SyncKnobsFromSelection();
    }

    private bool MatchesMode(AbilityDefinition ability, string mode, string? className, string? specName)
    {
        if (mode == "All") return true;
        if (mode == "Main") return ability.Category == AbilityCategory.Main;
        return AbilityViewPresets.MatchesCategory(ability, mode, _catalog, className, specName);
    }

    /// <summary>Mirrors the selected row's resolved override state into the knobs.</summary>
    private void SyncKnobsFromSelection()
    {
        var ability = _list.Selection;
        var hasSelection = ability is not null;
        _urgencyBox.Enabled = hasSelection;
        _scopeBox.Enabled = hasSelection;
        _reset.Enabled = hasSelection;
        if (!hasSelection)
        {
            _syncing = true;
            try
            {
                _urgencyBox.SelectedIndex = 0;
                _scopeBox.SelectedIndex = 0;
            }
            finally { _syncing = false; }
            return;
        }

        _syncing = true;
        try
        {
            var urgency = _host.UrgencyFloorOf(ability!.SpellId);
            var urgencyIndex = urgency is null ? 0 : Array.FindIndex(UrgencyOptions, o => o.Value == urgency.Value);
            _urgencyBox.SelectedIndex = urgencyIndex < 0 ? 0 : urgencyIndex;

            var mode = _host.ModeOf(ability.SpellId);
            var scopeIndex = Array.FindIndex(ScopeOptions, o => o.Mode == mode);
            _scopeBox.SelectedIndex = scopeIndex < 0 ? 0 : scopeIndex;
        }
        finally { _syncing = false; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}
