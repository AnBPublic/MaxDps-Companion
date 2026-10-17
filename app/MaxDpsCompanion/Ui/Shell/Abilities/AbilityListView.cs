using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// P2-D virtualized ability list (stock <see cref="ListView"/> with a pooled
/// <see cref="AbilityRowView"/> per realized container): only visible rows are
/// materialized and rows are recycled on scroll, so the 3279-entry catalog
/// stays responsive (old owner-drawn VirtualAbilityList :20-113). Selection is
/// the stock ListView selection; clicking a row raises <see cref="Selected"/>,
/// the row toggle raises <see cref="Toggled"/>; the explorer also toggles its
/// inline details on item click.
/// </summary>
internal sealed class AbilityListView : UserControl
{
    private readonly ListView _list = new();
    private readonly List<AbilityRowView> _pool = new();
    private readonly List<AbilityRowView> _realized = new();
    private readonly HashSet<int> _expanded = new();
    private readonly bool _allowExpand;

    private Func<AbilityDefinition, bool> _isEnabled = _ => true;
    private IReadOnlyDictionary<int, string> _conditions = new Dictionary<int, string>();
    private IReadOnlyDictionary<int, string> _verdicts = new Dictionary<int, string>();

    public event Action<AbilityDefinition>? Selected;
    public event Action<AbilityDefinition, bool>? Toggled;

    public AbilityListView(string accessibleName, bool allowExpand)
    {
        _allowExpand = allowExpand;
        _list.SelectionMode = ListViewSelectionMode.Single;
        _list.IsItemClickEnabled = true;
        _list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _list.ContainerContentChanging += OnContainerContentChanging;
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is AbilityDefinition ability) Selected?.Invoke(ability);
        };
        _list.ItemClick += (_, args) =>
        {
            if (!_allowExpand || args.ClickedItem is not AbilityDefinition ability) return;
            if (!_expanded.Add(ability.SpellId)) _expanded.Remove(ability.SpellId);
            Rebless(ability);
        };
        AutomationProperties.SetName(_list, accessibleName);
        // Engine icon flow: a downloaded icon evicts its scaled copies and
        // raises IconReady on a background thread; realized rows repaint their
        // bound image (old ClassSkillsView OnIconReady :1053-1058 pattern, kept
        // on the list so both the browser and the explorer get it).
        ScaledIconCache.Instance.IconReady += OnIconReady;
        Loaded += (_, _) =>
        {
            ScaledIconCache.Instance.IconReady -= OnIconReady;
            ScaledIconCache.Instance.IconReady += OnIconReady;
        };
        Unloaded += (_, _) => ScaledIconCache.Instance.IconReady -= OnIconReady;
        Content = _list;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    private void OnIconReady(int spellId)
    {
        var queue = DispatcherQueue;
        if (queue is null) return;
        if (queue.HasThreadAccess) RefreshIcon(spellId);
        else queue.TryEnqueue(() => RefreshIcon(spellId));
    }

    public AbilityDefinition? Selection => _list.SelectedItem as AbilityDefinition;

    public int Count => _list.Items.Count;

    public void SetEnabledSource(Func<AbilityDefinition, bool> isEnabled) => _isEnabled = isEnabled;

    /// <summary>Swaps the row set; the expanded inline row closes (old SetItems :68-76).</summary>
    public void SetItems(AbilityRows rows)
    {
        _conditions = rows.Conditions;
        _expanded.Clear();
        _list.SelectedItem = null;
        _list.ItemsSource = rows.Items;
    }

    /// <summary>Per-spell live verdicts supplied by the host (old Verdicts property).</summary>
    public void SetVerdicts(IReadOnlyDictionary<int, string> verdicts)
    {
        _verdicts = verdicts;
        foreach (var row in _realized.ToArray())
        {
            if (row.Ability is not { } ability) continue;
            row.UpdateVerdict(verdicts.TryGetValue(ability.SpellId, out var verdict) ? verdict : null);
        }
    }

    /// <summary>Value-only toggle refresh for one spell (old Invalidate repaint).</summary>
    public void RefreshToggle(AbilityDefinition ability)
    {
        foreach (var row in _realized.ToArray())
        {
            if (row.Ability is { } current && current.SpellId == ability.SpellId)
                row.SetToggleState(_isEnabled(current), current.Automatable);
        }
    }

    /// <summary>Refreshes the realized rows carrying one icon (engine IconReady).</summary>
    public void RefreshIcon(int spellId)
    {
        foreach (var row in _realized.ToArray())
        {
            if (row.Ability is { } ability && ability.SpellId == spellId) row.RefreshIcon();
        }
    }

    private void Rebless(AbilityDefinition ability)
    {
        foreach (var row in _realized.ToArray())
        {
            if (row.Ability is { } current && current.SpellId == ability.SpellId)
                row.SetExpanded(_expanded.Contains(ability.SpellId));
        }
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
        {
            if (args.ItemContainer.Content is AbilityRowView recycled)
            {
                args.ItemContainer.Content = null;
                recycled.Unbind();
                _realized.Remove(recycled);
                _pool.Add(recycled);
            }
            return;
        }

        if (args.Handled || args.Item is not AbilityDefinition ability) return;

        var row = _pool.Count > 0 ? Pop() : new AbilityRowView();
        args.ItemContainer.Content = row;
        args.ItemContainer.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        args.ItemContainer.Padding = new Thickness(0);
        args.ItemContainer.MinHeight = 0;
        // The ListViewItem peer names the container from the data item
        // (AbilityDefinition.ToString dump); keep the accessible name the clean
        // row caption instead.
        AutomationProperties.SetName(args.ItemContainer, $"{ability.Name}, {ability.Category}");
        _realized.Add(row);

        row.Bind(
            ability,
            _conditions.TryGetValue(ability.SpellId, out var condition) ? condition : null,
            _isEnabled(ability),
            ability.Automatable,
            _verdicts.TryGetValue(ability.SpellId, out var verdict) ? verdict : null,
            _expanded.Contains(ability.SpellId),
            on => Toggled?.Invoke(ability, on),
            () => { });
        args.Handled = true;
    }

    private AbilityRowView Pop()
    {
        var row = _pool[^1];
        _pool.RemoveAt(_pool.Count - 1);
        return row;
    }
}
