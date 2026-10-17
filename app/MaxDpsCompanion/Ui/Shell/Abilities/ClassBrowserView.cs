using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// S6 Class Browser host callbacks (old Ui/ClassBrowserView.cs :13-29): reads
/// the resolved registry state and writes the user knobs. The view never
/// evaluates a scheduler gate — it consumes the already-resolved
/// <see cref="ClassOverlayEntry"/> and the live verdict the shell reads from the
/// engine's published snapshot.
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
/// P2-D WinUI Class Browser: Class | Spec | Mode selectors and a virtualized row
/// list (<see cref="AbilityListView"/>) with the quick knobs (min HP%, urgency
/// floor, solo-only / normal-only / always) editing the selected row's
/// overrides. Control-map port of the old Ui/ClassBrowserView.cs; the enclosing
/// <see cref="OverlayShell"/> owns the single title/Back header, so this view
/// carries none.
/// </summary>
internal sealed class ClassBrowserView : UserControl
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

    private readonly OwnedComboBox _classBox = new();
    private readonly OwnedComboBox _specBox = new();
    private readonly OwnedComboBox _modeBox = new();
    private readonly NumberBox _hpFloor;
    private readonly OwnedComboBox _urgencyBox = new() { Width = 108 };
    private readonly OwnedComboBox _scopeBox = new() { Width = 118 };
    private readonly ChamferButton _reset;
    private readonly TextBlock _empty = new();
    private readonly AbilityListView _list;

    private bool _syncing;

    // D6 warm-open: the (class, spec, mode, hpFloor) the current list was built
    // for; a reopen with the same selection keeps the list instead of redoing
    // the filter + sort.
    private string? _builtClass;
    private string? _builtSpec;
    private string? _builtMode;
    private int _builtHpFloor = -1;

    private IReadOnlyList<AbilityDefinition> _items = [];

    public ClassBrowserView(AbilityCatalog catalog, ClassBrowserHost host, AbilityRowsPrecompute precompute)
    {
        _catalog = catalog;
        _host = host;
        Precompute = precompute;

        _list = new AbilityListView("Ability list", allowExpand: false);
        _list.SetEnabledSource(host.IsEnabled);
        _list.Toggled += (ability, on) =>
        {
            _host.SetEnabled(ability, on);
            _list.RefreshToggle(ability);
        };
        _list.Selected += _ => SyncKnobsFromSelection();

        _hpFloor = AdvancedUi.Number(100, 0, 100, "Minimum HP percent");
        _hpFloor.Width = 68;
        _hpFloor.ValueChanged += (_, _) => Rebuild();

        _reset = AdvancedUi.Button("Reset row", ButtonRole.Ghost, ResetSelected);
        _reset.Width = 96;
        _reset.Height = 28;
        _reset.IsEnabled = false;

        var content = new Grid { Padding = new Thickness(14, 8, 14, 12), RowSpacing = 6 };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var toolbar = BuildToolbar();
        var knobs = BuildKnobs();
        Grid.SetRow(toolbar, 0);
        Grid.SetRow(knobs, 1);
        Grid.SetRow(_empty, 2);
        Grid.SetRow(_list, 3);
        content.Children.Add(toolbar);
        content.Children.Add(knobs);
        content.Children.Add(_empty);
        content.Children.Add(_list);

        _empty.FontSize = ControlTheme.Dip(DesignTokens.BodySize);
        _empty.Foreground = ControlTheme.Brush(ControlTheme.TextSecondary);
        _empty.Text = "Pick a class and spec to browse its abilities.";
        _empty.Visibility = Visibility.Collapsed;
        _empty.Margin = new Thickness(2, 4, 2, 4);

        Content = content;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(this, "Class browser");
    }

    internal AbilityRowsPrecompute Precompute { get; }

    /// <summary>Wall-clock of the last <see cref="Open"/> (warm-open budget &lt; 150 ms).</summary>
    internal double LastOpenMs { get; private set; }

    internal int RowCount => _list.Count;

    internal string SelectedModeForTest => _modeBox.SelectedItem as string ?? "";

    internal string SelectedClassForTest => SelectedClass ?? "";

    internal IReadOnlyList<string> ModeLabelsForTest { get; } = ModeOptions.Select(m => m.Label).ToArray();

    internal string DebugState =>
        $"rows={_list.Count} class={SelectedClass ?? "-"} spec={SelectedSpec ?? "-"} " +
        $"mode={SelectedModeForTest} hpFloor={_hpFloor.Value}";

    private Grid BuildToolbar()
    {
        var toolbar = new Grid { ColumnSpacing = 8, RowSpacing = 4 };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(188) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _classBox.MinWidth = 180;
        _classBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(_classBox, "Class");
        _classBox.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            PopulateSpecs();
            Rebuild();
        };

        _specBox.MinWidth = 160;
        _specBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(_specBox, "Specialization");
        _specBox.SelectionChanged += (_, _) => { if (!_syncing) Rebuild(); };

        _modeBox.MinWidth = 140;
        _modeBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(_modeBox, "Ability mode");
        foreach (var option in ModeOptions) _modeBox.Items.Add(option.Label);
        _modeBox.SelectedIndex = 0;
        _modeBox.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            Rebuild();
        };

        AddCell(toolbar, Caption("Class"), 0, 0);
        AddCell(toolbar, _classBox, 1, 0);
        AddCell(toolbar, Caption("Spec"), 2, 0);
        AddCell(toolbar, _specBox, 3, 0);
        AddCell(toolbar, Caption("Mode"), 4, 0);
        AddCell(toolbar, _modeBox, 5, 0);
        return toolbar;
    }

    private WrapFlow BuildKnobs()
    {
        // Urgency floor / scope: enabled only for a selected row (old
        // SyncKnobsFromSelection :505-536).
        _urgencyBox.IsEnabled = false;
        AutomationProperties.SetName(_urgencyBox, "Urgency floor");
        foreach (var option in UrgencyOptions) _urgencyBox.Items.Add(option.Label);
        _urgencyBox.SelectedIndex = 0;
        _urgencyBox.SelectionChanged += (_, _) =>
        {
            if (_syncing || _list.Selection is not { } ability) return;
            var value = UrgencyOptions[Math.Max(0, _urgencyBox.SelectedIndex)].Value;
            _host.SetUrgencyFloor(ability.SpellId, value < 0 ? null : value);
            RefreshLive();
        };

        _scopeBox.IsEnabled = false;
        AutomationProperties.SetName(_scopeBox, "Solo or normal scope");
        foreach (var option in ScopeOptions) _scopeBox.Items.Add(option.Label);
        _scopeBox.SelectedIndex = 0;
        _scopeBox.SelectionChanged += (_, _) =>
        {
            if (_syncing || _list.Selection is not { } ability) return;
            _host.SetMode(ability.SpellId, ScopeOptions[Math.Max(0, _scopeBox.SelectedIndex)].Mode);
            RefreshLive();
        };

        var hint = new TextBlock
        {
            Text = "Knobs edit the selected row; the scheduler still owns every gate.",
            FontSize = ControlTheme.Dip(DesignTokens.MetaSize),
            FontStyle = Windows.UI.Text.FontStyle.Italic,
            Foreground = ControlTheme.Brush(ControlTheme.TextMuted),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };

        var knobs = new WrapFlow { Gap = 8, RowGap = 6 };
        knobs.Children.Add(KnobCaption("Min HP%"));
        knobs.Children.Add(_hpFloor);
        knobs.Children.Add(KnobCaption("Urgency floor"));
        knobs.Children.Add(_urgencyBox);
        knobs.Children.Add(KnobCaption("Scope"));
        knobs.Children.Add(_scopeBox);
        knobs.Children.Add(_reset);
        knobs.Children.Add(hint);
        return knobs;
    }

    private static void AddCell(Grid grid, FrameworkElement element, int column, int row)
    {
        Grid.SetColumn(element, column);
        Grid.SetRow(element, row);
        grid.Children.Add(element);
    }

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        FontSize = ControlTheme.Dip(DesignTokens.CaptionSize),
        Foreground = ControlTheme.Brush(ControlTheme.TextMuted),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock KnobCaption(string text) => new()
    {
        Text = text,
        FontSize = ControlTheme.Dip(DesignTokens.MetaSize),
        Foreground = ControlTheme.Brush(ControlTheme.TextMuted),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(6, 0, 0, 0),
    };

    // ----- open / select -----------------------------------------------------

    /// <summary>
    /// Shows the browser, preselecting the live class/spec and, when
    /// <paramref name="preset"/> names a mode ("Offensive", "Self-heal",
    /// "Crowd control", ...), that mode (old Open :327-365).
    /// </summary>
    public void Open(string? liveClass, string? liveSpec, string? preset = null)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
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
        Rebuild();
        LastOpenMs = clock.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// WS-C drill-through seam: opens the browser on a class/spec and selects
    /// the mode named by an Intelligence tile or a hero bubble tag
    /// (old ApplyPreset :371-402).
    /// </summary>
    public void ApplyPreset(string tag, string? className = null, string? specName = null)
    {
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

    /// <summary>Re-reads the live verdicts and repaints without rebuilding rows (old RefreshLive :405-416).</summary>
    public void RefreshLive()
    {
        var verdicts = new Dictionary<int, string>();
        foreach (var ability in _items)
        {
            var verdict = _host.LiveVerdict(ability);
            if (!string.IsNullOrWhiteSpace(verdict)) verdicts[ability.SpellId] = verdict!;
        }
        _list.SetVerdicts(verdicts);
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

    // ----- filter / rebuild --------------------------------------------------

    private void Rebuild()
    {
        var className = SelectedClass;
        var specName = SelectedSpec;
        var mode = ModeOptions[Math.Max(0, _modeBox.SelectedIndex)].Tag;
        var hpFloor = (int)_hpFloor.Value;

        // D6: last request wins; rebuild the list only when something the list
        // depends on changed (old :453-462). The row build runs through the
        // off-thread precompute so a warm reopen reuses the cached rows.
        var changed = !string.Equals(_builtClass, className, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_builtSpec, specName, StringComparison.Ordinal)
            || !string.Equals(_builtMode, mode, StringComparison.Ordinal)
            || _builtHpFloor != hpFloor
            || _items.Count == 0;
        _builtClass = className;
        _builtSpec = specName;
        _builtMode = mode;
        _builtHpFloor = hpFloor;

        if (!changed)
        {
            RefreshLive();
            return;
        }

        var rows = Precompute.GetBrowserRows(className, specName, mode, hpFloor);
        _items = rows.Items;
        _list.SetItems(rows);
        _empty.Text = _items.Count == 0
            ? $"No {ModeOptions[Math.Max(0, _modeBox.SelectedIndex)].Label} abilities for " +
              $"{(className is null ? "this class" : AbilityViewPresets.ClassDisplay(className))}" +
              $"{(specName is null ? "" : "/" + specName)}."
            : "";
        _empty.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshLive();
        SyncKnobsFromSelection();
    }

    /// <summary>Mirrors the selected row's resolved override state into the knobs (old :505-536).</summary>
    private void SyncKnobsFromSelection()
    {
        var ability = _list.Selection;
        var hasSelection = ability is not null;
        _urgencyBox.IsEnabled = hasSelection;
        _scopeBox.IsEnabled = hasSelection;
        _reset.IsEnabled = hasSelection;
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

    private void ResetSelected()
    {
        if (_list.Selection is not { } ability) return;
        _host.ResetOverrides(ability.SpellId);
        SyncKnobsFromSelection();
        RefreshLive();
    }
}
