using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>Mutable filter state for the ability explorer. Rebuilt only on change (debounced).</summary>
internal sealed class AbilityFilterState
{
    public string Query { get; set; } = "";
    public string Status { get; set; } = "All";
    public readonly HashSet<string> Categories = new(StringComparer.OrdinalIgnoreCase);

    public bool IsDefault => Query.Length == 0 && Status == "All" && Categories.Count == 0;
}

/// <summary>
/// P2-D WinUI ability explorer (port of the old Ui/AbilityInspector.cs
/// AbilityExplorer :289-579): debounced search, filter chips, the virtualized
/// <see cref="AbilityListView"/> and the master-detail
/// <see cref="InspectorPanel"/>. The filtered array is rebuilt only when
/// filters change — never per paint or per keystroke — through the off-thread
/// <see cref="AbilityRowsPrecompute"/> cache, with the inline build as the
/// never-blank fallback.
/// </summary>
internal sealed class AbilityExplorer : UserControl
{
    private static readonly string[] StatusTags = ["All", "Automatic", "Companion", "MaxDps", "Manual", "Incomplete", "Unverified", "Warnings"];
    // v3.4.0 Approach A §4-§6: the hero bubble vocabulary is discoverable here
    // too (Consumable/Trinket/CrowdControl/Solo) so a preset visibly highlights.
    private static readonly string[] CategoryTags = ["Defensive", "Self Sustain", "Interrupt", "Offensive", "Mobility", "Consumable", "Trinket", "CrowdControl", "Solo", "Utility"];

    private readonly AbilityFilterState _filter = new();
    private string? _presetClass;
    private string? _presetSpec;
    private readonly TextBox _search = new();
    private readonly TextBlock _count = new();
    private readonly TextBlock _emptyHint = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer? _debounce;
    private readonly AbilityListView _list;
    private readonly InspectorPanel _inspector = new();
    private readonly List<FilterChip> _statusChips = [];
    private readonly List<FilterChip> _categoryChips = [];

    private readonly Func<AbilityDefinition, bool> _isEnabled;
    private readonly Action<AbilityDefinition, bool> _setEnabled;

    public event Action<int>? FilteredCountChanged;

    public AbilityExplorer(
        AbilityCatalog catalog,
        Func<AbilityDefinition, bool> isEnabled,
        Action<AbilityDefinition, bool> setEnabled,
        AbilityRowsPrecompute precompute)
    {
        _isEnabled = isEnabled;
        _setEnabled = setEnabled;
        Catalog = catalog;
        Precompute = precompute;

        _list = new AbilityListView("Ability list", allowExpand: true);
        _list.SetEnabledSource(isEnabled);
        _list.Toggled += (ability, on) =>
        {
            _setEnabled(ability, on);
            _list.RefreshToggle(ability);
        };
        _list.Selected += ability => _inspector.Show(ability, Catalog, _isEnabled);

        _search.FontSize = ControlTheme.Dip(DesignTokens.BodySize);
        _search.PlaceholderText = "Search name, spell id, class, spec, category, owner or status\u2026";
        AutomationProperties.SetHelpText(_search, "Filter the ability list as you type");
        _search.TextChanged += (_, _) => { _debounce?.Stop(); _debounce?.Start(); };

        _count.FontSize = ControlTheme.Dip(DesignTokens.MetaSize);
        _count.Foreground = ControlTheme.Brush(ControlTheme.TextMuted);
        _count.VerticalAlignment = VerticalAlignment.Center;
        _count.Text = "0";
        _count.Margin = new Thickness(8, 0, 0, 0);

        _emptyHint.Text = "No abilities match the current filters.";
        _emptyHint.FontSize = ControlTheme.Dip(DesignTokens.BodySize);
        _emptyHint.Foreground = ControlTheme.Brush(ControlTheme.TextMuted);
        _emptyHint.TextWrapping = TextWrapping.Wrap;
        _emptyHint.Margin = new Thickness(6, 8, 6, 0);
        _emptyHint.VerticalAlignment = VerticalAlignment.Top;
        _emptyHint.Visibility = Visibility.Collapsed;

        var searchRow = new Grid { ColumnSpacing = 8 };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_search, 0);
        Grid.SetColumn(_count, 1);
        searchRow.Children.Add(_search);
        searchRow.Children.Add(_count);

        var chips = new StackPanel { Spacing = 4 };
        chips.Children.Add(BuildChipRow(StatusTags, _statusChips, SelectStatus));
        chips.Children.Add(BuildChipRow(CategoryTags, _categoryChips, ToggleCategory));

        var listColumn = new Grid();
        listColumn.Children.Add(_list);
        listColumn.Children.Add(_emptyHint);

        var split = new Grid { ColumnSpacing = 10 };
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        Grid.SetColumn(listColumn, 0);
        Grid.SetColumn(_inspector, 1);
        split.Children.Add(listColumn);
        split.Children.Add(_inspector);

        var root = new Grid { Padding = new Thickness(14, 10, 14, 12), RowSpacing = 6 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(searchRow, 0);
        Grid.SetRow(chips, 1);
        Grid.SetRow(split, 2);
        root.Children.Add(searchRow);
        root.Children.Add(chips);
        root.Children.Add(split);

        Content = root;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(this, "Ability explorer");
        AutomationProperties.SetName(_search, "Search abilities");
        AutomationProperties.SetName(_list, "Ability list");

        if (DispatcherQueue is { } queue)
        {
            _debounce = queue.CreateTimer();
            _debounce.Interval = TimeSpan.FromMilliseconds(150);
            _debounce.IsRepeating = false;
            _debounce.Tick += (_, _) =>
            {
                _debounce.Stop();
                WarmCurrent();
                Rebuild();
            };
        }

        Rebuild();
    }

    internal AbilityCatalog Catalog { get; }

    internal AbilityRowsPrecompute Precompute { get; }

    public int FilteredCount { get; private set; }

    /// <summary>Test seam: the active filter categories (v3.4.0 §6).</summary>
    internal IReadOnlyCollection<string> CategoriesForTest => _filter.Categories;

    /// <summary>Test seam: the empty-state copy the list would show (v3.4.0 §6).</summary>
    internal string EmptyStateForTest => EmptyStateText();

    public void FocusSearch() => _search.Focus(FocusState.Programmatic);

    /// <summary>Test/snapshot hook: apply a query immediately (debounce bypassed).</summary>
    public void SetQuery(string query)
    {
        _search.Text = query;
        _filter.Query = query;
        Rebuild();
    }

    public void SelectStatus(string tag)
    {
        _filter.Status = tag;
        foreach (var chip in _statusChips) chip.Selected = chip.Text == tag;
        WarmCurrent();
        Rebuild();
    }

    public void ToggleCategory(string tag)
    {
        if (!_filter.Categories.Add(tag)) _filter.Categories.Remove(tag);
        foreach (var chip in _categoryChips) chip.Selected = _filter.Categories.Contains(chip.Text);
        WarmCurrent();
        Rebuild();
    }

    /// <summary>
    /// Preset applied when navigating from the Intelligence dashboard or a
    /// clickable hero bubble (old ApplyPreset :472-488). Registry-status tags
    /// keep the old status behaviour; category tags ("Offensive", "Self-heal",
    /// "CrowdControl", "Solo", …) filter the set, optionally scoped to the live
    /// class/spec.
    /// </summary>
    public void ApplyPreset(string tag, string? className = null, string? specName = null)
    {
        if (!AbilityViewPresets.IsCategoryTag(tag))
        {
            _presetClass = null;
            _presetSpec = null;
            SelectStatus(tag);
            return;
        }
        var category = AbilityViewPresets.NormalizeCategory(tag);
        _filter.Categories.Clear();
        _filter.Categories.Add(category);
        _presetClass = className;
        _presetSpec = specName;
        foreach (var chip in _categoryChips) chip.Selected = _filter.Categories.Contains(chip.Text);
        WarmCurrent();
        Rebuild();
    }

    /// <summary>Prebuilds the row set the current filters would select, off the UI thread.</summary>
    public void WarmCurrent()
    {
        Precompute.WarmExplorer(
            _search.Text.Trim(), _filter.Status, _filter.Categories, _presetClass, _presetSpec);
    }

    private static WrapFlow BuildChipRow(string[] tags, List<FilterChip> chips, Action<string> activate)
    {
        var row = new WrapFlow { Gap = 6, RowGap = 6 };
        foreach (var tag in tags)
        {
            var chip = new FilterChip(tag, selected: tag == "All");
            AutomationProperties.SetHelpText(chip, $"Filter: {tag}");
            var captured = tag;
            chip.Click += (_, _) => activate(captured);
            chips.Add(chip);
            row.Children.Add(chip);
        }
        return row;
    }

    private void Rebuild()
    {
        var query = _filter.Query = _search.Text.Trim();
        var rows = Precompute.GetExplorerRows(query, _filter.Status, _filter.Categories, _presetClass, _presetSpec);
        FilteredCount = rows.Items.Count;
        _count.Text = $"{FilteredCount} shown";
        _emptyHint.Text = EmptyStateText();
        _emptyHint.Visibility = FilteredCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        _list.SetItems(rows);
        FilteredCountChanged?.Invoke(FilteredCount);
    }

    /// <summary>v3.4.0 §6: the empty state names the active preset + class/spec.</summary>
    private string EmptyStateText()
    {
        if (_filter.Categories.Count == 0) return "No abilities match the current filters.";
        var what = AbilityViewPresets.CategoryDisplay(_filter.Categories.First());
        var who = _presetClass is null
            ? ""
            : $" for {AbilityViewPresets.ClassDisplay(_presetClass)}" + (_presetSpec is null ? "" : $"/{_presetSpec}");
        return $"No {what} skills{who}.";
    }
}
