using System.Text;

namespace MaxDpsCompanion;

/// <summary>
/// Master-detail inspector (v2.7 §28): the full canonical intelligence view of
/// one ability. Built from <see cref="AbilityDefinition"/> plus
/// <see cref="AbilityIntelligence.Audit(AbilityDefinition, AbilityCatalog)"/>.
/// </summary>
internal sealed class InspectorPanel : Panel
{
    private readonly Panel _content = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.Transparent };
    private readonly Label _empty = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Text = "Select an ability to inspect its intelligence.",
        Font = DesignTokens.Type(DesignTokens.BodySize),
        ForeColor = DesignTokens.TextMuted,
        BackColor = Color.Transparent,
    };

    public InspectorPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = DesignTokens.Surface;
        Padding = new Padding(DesignTokens.SpaceM);
        AccessibleRole = AccessibleRole.Pane;
        AccessibleName = "Ability inspector";
        SetStyle(ControlStyles.ResizeRedraw, true);
        _content.Visible = false;
        Controls.Add(_content);
        Controls.Add(_empty);
    }

    public AbilityDefinition? Current { get; private set; }

    public void Clear()
    {
        Current = null;
        _content.Controls.Clear();
        _content.Visible = false;
        _empty.Visible = true;
    }

    public void Show(AbilityDefinition ability, AbilityCatalog catalog, Func<AbilityDefinition, bool> isEnabled)
    {
        Current = ability;
        _empty.Visible = false;
        _content.Visible = true;
        _content.Controls.Clear();

        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 0, DesignTokens.SpaceS, 0),
        };
        _content.Controls.Add(stack);

        var width = Math.Max(220, _content.ClientSize.Width - DesignTokens.SpaceL - 20);
        void Add(string text, float size, FontStyle style, Color color)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(width, 0),
                Font = DesignTokens.Type(size, style),
                ForeColor = color,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 6),
            };
            stack.Controls.Add(label);
        }
        void Section(string title)
        {
            Add(title.ToUpperInvariant(), DesignTokens.MetaSize, FontStyle.Bold, DesignTokens.Accent);
        }
        void KeyValue(string key, string value) => Add($"{key}: {value}", DesignTokens.BodySize, FontStyle.Regular, DesignTokens.TextSecondary);

        Add(ability.Name, 15F, FontStyle.Bold, DesignTokens.TextPrimary);
        Add($"#{ability.SpellId}  \u00B7  {ability.Category}  \u00B7  {string.Join(", ", ability.Classes.DefaultIfEmpty("(shared)"))}",
            DesignTokens.MetaSize, FontStyle.Regular, DesignTokens.TextSecondary);

        Section("Ownership");
        KeyValue("Owner", ability.Ownership.ToString());
        KeyValue("Completeness", ability.Completeness.ToString());
        KeyValue("Automation", ability.Automation + (ability.Automatable ? " (automatable)" : " (not automatable)"));
        KeyValue("Enabled", isEnabled(ability) ? "ON" : "OFF");
        if (ability.HasDelegationReason) KeyValue("Delegation", ability.Delegation + (string.IsNullOrEmpty(ability.DelegationNote) ? "" : $" \u2014 {ability.DelegationNote}"));
        if (ability.HasManualReason) KeyValue("Manual reason", ability.ManualReason.ToString());

        Section("Candidate path");
        KeyValue("Can fire", ability.HasCandidatePath ? "yes" : "no");
        KeyValue("Source", $"{ability.Provenance} / {ability.SourceType} ({ability.SourceConfidence})");
        KeyValue("Live", ability.LiveVerified ? "verified" : "unverified");

        Section("Requirements");
        KeyValue("Context", ability.Requires == AbilityRequirement.None ? "none" : ability.Requires.ToString());
        KeyValue("Target", ability.TargetKind + (ability.RequiresTarget ? " (requires target)" : ""));
        KeyValue("GCD", ability.RidesGcd ? "on GCD" : "off GCD");
        KeyValue("Cooldown", ability.CooldownMs > 0 ? $"{ability.CooldownMs / 1000.0:0.#} s ({ability.CooldownClass})" : "none");

        Section("Relationships");
        if (ability.Relations.Length == 0) Add("none recorded", DesignTokens.BodySize, FontStyle.Regular, DesignTokens.TextMuted);
        else foreach (var relation in ability.Relations)
            Add($"{relation.Kind} \u2192 #{relation.SpellId}{(string.IsNullOrEmpty(relation.Note) ? "" : $" ({relation.Note})")}",
                DesignTokens.BodySize, FontStyle.Regular, DesignTokens.TextSecondary);

        Section("Patch & evidence");
        KeyValue("Patch", ability.SourcePatch ?? "?");
        KeyValue("Introduced", ability.IntroducedPatch ?? "unknown");
        KeyValue("Last validated", ability.LastValidatedPatch ?? "unknown");
        if (!string.IsNullOrEmpty(ability.SourceUrl)) Add(ability.SourceUrl, DesignTokens.BodySize, FontStyle.Regular, DesignTokens.Info);

        Section("Telemetry note");
        Add("Local decisions for this ability are recorded only while telemetry is armed; nothing leaves the machine.",
            DesignTokens.BodySize, FontStyle.Regular, DesignTokens.TextMuted);

        Section("Audit findings");
        var audit = AbilityIntelligence.Audit(ability, catalog);
        if (audit.Findings.Count == 0) Add("no findings", DesignTokens.BodySize, FontStyle.Regular, DesignTokens.TextMuted);
        else foreach (var finding in audit.Findings)
        {
            var color = finding.Severity switch
            {
                AuditSeverity.Violation => DesignTokens.Danger,
                AuditSeverity.Warning => DesignTokens.Warning,
                AuditSeverity.Info => DesignTokens.Info,
                _ => DesignTokens.TextSecondary,
            };
            Add($"[{finding.Severity}] {finding.Check}: {finding.Detail}", DesignTokens.BodySize, FontStyle.Regular, color);
        }

        foreach (Control control in stack.Controls)
            if (control is Label label && label.MaximumSize.Width != width)
                label.MaximumSize = new Size(width, 0);
    }
}

/// <summary>
/// The ability explorer page body (v2.7 §28): debounced search, filter chips,
/// a virtualized list and a master-detail inspector. The filtered array is
/// rebuilt only when filters change — never per paint or per keystroke.
/// </summary>
internal sealed class AbilityExplorer : Panel
{
    private static readonly string[] StatusTags = ["All", "Automatic", "Companion", "MaxDps", "Manual", "Incomplete", "Unverified", "Warnings"];
    private static readonly string[] CategoryTags = ["Defensive", "Self Sustain", "Interrupt", "Offensive", "Mobility", "Utility"];

    private readonly AbilityFilterState _filter = new();
    private readonly TextBox _search = new();
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 150 };
    private readonly VirtualAbilityList _list;
    private readonly InspectorPanel _inspector = new();
    private readonly List<FilterChip> _statusChips = [];
    private readonly List<FilterChip> _categoryChips = [];
    private readonly Label _count = new();

    private readonly Func<AbilityDefinition, bool> _isEnabled;
    private readonly Action<AbilityDefinition, bool> _setEnabled;

    public event Action<int>? FilteredCountChanged;

    public AbilityExplorer(Func<AbilityDefinition, bool> isEnabled, Action<AbilityDefinition, bool> setEnabled)
    {
        _isEnabled = isEnabled;
        _setEnabled = setEnabled;
        Dock = DockStyle.Fill;
        BackColor = DesignTokens.Background;
        Padding = new Padding(DesignTokens.SpaceL, DesignTokens.SpaceS, DesignTokens.SpaceL, DesignTokens.SpaceM);
        AccessibleRole = AccessibleRole.Pane;
        AccessibleName = "Ability explorer";

        _list = new VirtualAbilityList(isEnabled) { Dock = DockStyle.Fill };
        _list.Toggled += (ability, on) => { _setEnabled(ability, on); _list.Invalidate(); };
        _list.Selected += ability => _inspector.Show(ability, AbilityCatalog.Default, _isEnabled);

        var searchRow = BuildSearchRow();
        var chips = BuildChips();

        var left = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        left.Controls.Add(_list);
        left.Controls.Add(chips);
        left.Controls.Add(searchRow);

        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
        split.Controls.Add(left, 0, 0);
        split.Controls.Add(_inspector, 1, 0);

        Controls.Add(split);

        _debounce.Tick += (_, _) => { _debounce.Stop(); Rebuild(); };
        Rebuild();
    }

    public int FilteredCount { get; private set; }

    private Control BuildSearchRow()
    {
        var row = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Color.Transparent };
        _search.Dock = DockStyle.Fill;
        _search.Font = DesignTokens.Type(DesignTokens.BodySize);
        _search.BackColor = DesignTokens.SurfaceElevated;
        _search.ForeColor = DesignTokens.TextPrimary;
        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.PlaceholderText = "Search name, spell id, class, spec, category, owner or status\u2026";
        _search.AccessibleName = "Search abilities";
        _search.AccessibleDescription = "Filter the ability list as you type";
        _search.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };

        _count.AutoSize = false;
        _count.Dock = DockStyle.Right;
        _count.Width = 96;
        _count.TextAlign = ContentAlignment.MiddleRight;
        _count.Font = DesignTokens.Type(DesignTokens.MetaSize);
        _count.ForeColor = DesignTokens.TextMuted;
        _count.BackColor = Color.Transparent;
        _count.Text = "0";

        var box = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 4), BackColor = Color.Transparent };
        box.Controls.Add(_search);
        row.Controls.Add(box);
        row.Controls.Add(_count);
        return row;
    }

    private Control BuildChips()
    {
        // Wrapping chip strips (v2.8): fixed-height no-wrap rows clipped the
        // right-most chips on narrow windows; both strips now measure.
        var host = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
        };
        var status = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            AutoScroll = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        foreach (var tag in StatusTags)
        {
            var chip = new FilterChip(tag, tag == "All") { AccessibleDescription = $"{tag} abilities" };
            chip.Click += (_, _) => SelectStatus(tag);
            _statusChips.Add(chip);
            status.Controls.Add(chip);
        }
        var category = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            AutoScroll = false,
            BackColor = Color.Transparent,
        };
        foreach (var tag in CategoryTags)
        {
            var chip = new FilterChip(tag, false) { AccessibleDescription = $"Category filter: {tag}" };
            chip.Click += (_, _) => ToggleCategory(tag);
            _categoryChips.Add(chip);
            category.Controls.Add(chip);
        }
        host.Controls.Add(category);
        host.Controls.Add(status);
        return host;
    }

    public void FocusSearch() => _search.Focus();

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
        Rebuild();
    }

    public void ToggleCategory(string tag)
    {
        if (!_filter.Categories.Add(tag)) _filter.Categories.Remove(tag);
        foreach (var chip in _categoryChips) chip.Selected = _filter.Categories.Contains(chip.Text);
        Rebuild();
    }

    /// <summary>Preset applied when navigating from the Intelligence dashboard.</summary>
    public void ApplyPreset(string tag)
    {
        switch (tag)
        {
            case "All":
            case "Automatic":
            case "Companion":
            case "MaxDps":
            case "Manual":
            case "Incomplete":
            case "Unverified":
            case "Warnings":
                SelectStatus(tag);
                break;
            default:
                _filter.Categories.Clear();
                _filter.Categories.Add(tag);
                foreach (var chip in _categoryChips) chip.Selected = _filter.Categories.Contains(chip.Text);
                Rebuild();
                break;
        }
    }

    private void Rebuild()
    {
        var query = _filter.Query = _search.Text.Trim();
        var list = new List<AbilityDefinition>(128);
        foreach (var ability in AbilityCatalog.Default.All)
        {
            if (!MatchesStatus(ability)) continue;
            if (_filter.Categories.Count > 0 && !MatchesAnyCategory(ability)) continue;
            if (query.Length > 0 && !MatchesQuery(ability, query)) continue;
            list.Add(ability);
        }
        list.Sort(static (a, b) =>
        {
            var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : a.SpellId.CompareTo(b.SpellId);
        });
        FilteredCount = list.Count;
        _count.Text = $"{list.Count} shown";
        _list.SetItems(list);
        FilteredCountChanged?.Invoke(list.Count);
    }

    private bool MatchesStatus(AbilityDefinition ability) => _filter.Status switch
    {
        "Automatic" => ability.Automatable,
        "Companion" => ability.Automation == AutomationContext.Autonomous && ability.HasCandidatePath,
        "MaxDps" => ability.Ownership == IntelligenceOwnership.MaxDps,
        "Manual" => ability.Automation == AutomationContext.Manual,
        "Incomplete" => !ability.HasIntelligence,
        "Unverified" => ability.Automatable && !ability.LiveVerified && ability.Ownership != IntelligenceOwnership.MaxDps,
        "Warnings" => HasWarning(ability),
        _ => true,
    };

    private static bool HasWarning(AbilityDefinition ability)
        => !ability.HasIntelligence
           || (ability.Ownership == IntelligenceOwnership.Unavailable && !ability.ManualByDesign)
           || (ability.MaxDpsOwned && !ability.HasDelegationReason)
           || (ability.Ownership == IntelligenceOwnership.Manual && !ability.HasManualReason);

    private bool MatchesAnyCategory(AbilityDefinition ability)
    {
        foreach (var tag in _filter.Categories)
        {
            var match = tag switch
            {
                "Defensive" => ability.HasDefensiveCapability,
                "Self Sustain" => ability.HasSelfSustainCapability,
                "Interrupt" => ability.HasInterruptCapability,
                "Offensive" => ability.HasOffensiveCapability,
                "Mobility" => ability.HasMobilityCapability,
                "Utility" => ability.Category == AbilityCategory.Utility || ability.Automation == AutomationContext.Manual,
                _ => false,
            };
            if (match) return true;
        }
        return false;
    }

    private static bool MatchesQuery(AbilityDefinition ability, string query)
    {
        if (ability.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        if (ability.SpellId.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        if (ability.Category.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        if (ability.Ownership.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        if (ability.Status.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var cls in ability.Classes)
            if (cls.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var spec in ability.Specs)
            if (spec.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _debounce.Dispose();
        base.Dispose(disposing);
    }
}
