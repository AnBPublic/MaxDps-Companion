using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// P2-D master-detail inspector (WinUI port of the old Ui/AbilityInspector.cs
/// InspectorPanel :142-282): the full canonical intelligence view of one
/// ability, built from <see cref="AbilityDefinition"/> plus
/// <see cref="AbilityIntelligence.Audit(AbilityDefinition, AbilityCatalog)"/>.
/// </summary>
internal sealed class InspectorPanel : UserControl
{
    private readonly StackPanel _content = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock _empty = new()
    {
        Text = "Select an ability to inspect its intelligence.",
        FontSize = ControlTheme.Dip(DesignTokens.BodySize),
        Foreground = ControlTheme.Brush(ControlTheme.TextMuted),
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        Margin = new Thickness(12),
    };

    public InspectorPanel()
    {
        var scroll = new ScrollViewer
        {
            Content = _content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
        };
        var host = new Grid();
        host.Background = ControlTheme.Brush(ControlTheme.Surface);
        host.Children.Add(_empty);
        host.Children.Add(scroll);
        Content = host;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(this, "Ability inspector");
    }

    public AbilityDefinition? Current { get; private set; }

    public void Clear()
    {
        Current = null;
        _content.Children.Clear();
        _content.Visibility = Visibility.Collapsed;
        _empty.Visibility = Visibility.Visible;
    }

    public void Show(AbilityDefinition ability, AbilityCatalog catalog, Func<AbilityDefinition, bool> isEnabled)
    {
        Current = ability;
        _content.Children.Clear();
        _empty.Visibility = Visibility.Collapsed;
        _content.Visibility = Visibility.Visible;
        _content.Padding = new Thickness(12);

        void Add(string text, float size, bool bold, Windows.UI.Color color, bool muted = false)
        {
            _content.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = ControlTheme.Dip(size),
                FontWeight = bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = ControlTheme.Brush(color),
                TextWrapping = TextWrapping.Wrap,
                FontStyle = muted ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
                Margin = new Thickness(0, 0, 0, 4),
            });
        }
        void Section(string title) => Add(title.ToUpperInvariant(), DesignTokens.MetaSize, true, ControlTheme.Accent);
        void KeyValue(string key, string value) => Add($"{key}: {value}", DesignTokens.BodySize, false, ControlTheme.TextSecondary);

        Add(ability.Name, 15f, true, ControlTheme.TextPrimary);
        Add(
            $"#{ability.SpellId}  \u00B7  {ability.Category}  \u00B7  {string.Join(", ", ability.Classes.DefaultIfEmpty("(shared)"))}",
            DesignTokens.MetaSize, false, ControlTheme.TextSecondary);

        Section("Ownership");
        KeyValue("Owner", ability.Ownership.ToString());
        KeyValue("Completeness", ability.Completeness.ToString());
        KeyValue("Automation", ability.Automation + (ability.Automatable ? " (automatable)" : " (not automatable)"));
        KeyValue("Enabled", isEnabled(ability) ? "ON" : "OFF");
        if (ability.HasDelegationReason)
            KeyValue("Delegation", ability.Delegation + (string.IsNullOrEmpty(ability.DelegationNote) ? "" : $" \u2014 {ability.DelegationNote}"));
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
        var cc = catalog.CrowdControlFor(ability.Classes.FirstOrDefault(), ability.Specs.FirstOrDefault(), ability.SpellId);
        var condition = AbilityViewPresets.ConditionLine(ability, cc);
        if (condition.Length > 0) KeyValue("Condition", condition);

        Section("Relationships");
        if (ability.Relations.Length == 0)
        {
            Add("none recorded", DesignTokens.BodySize, false, ControlTheme.TextMuted);
        }
        else
        {
            foreach (var relation in ability.Relations)
                Add($"{relation.Kind} \u2192 #{relation.SpellId}{(string.IsNullOrEmpty(relation.Note) ? "" : $" ({relation.Note})")}",
                    DesignTokens.BodySize, false, ControlTheme.TextSecondary);
        }

        Section("Patch & evidence");
        KeyValue("Patch", ability.SourcePatch ?? "?");
        KeyValue("Introduced", ability.IntroducedPatch ?? "unknown");
        KeyValue("Last validated", ability.LastValidatedPatch ?? "unknown");
        if (!string.IsNullOrEmpty(ability.SourceUrl))
            Add(ability.SourceUrl, DesignTokens.BodySize, false, ControlTheme.Info);

        Section("Telemetry note");
        Add("Local decisions for this ability are recorded only while telemetry is armed; nothing leaves the machine.",
            DesignTokens.BodySize, false, ControlTheme.TextMuted, muted: true);

        Section("Audit findings");
        var audit = AbilityIntelligence.Audit(ability, catalog);
        if (audit.Findings.Count == 0)
        {
            Add("no findings", DesignTokens.BodySize, false, ControlTheme.TextMuted);
        }
        else
        {
            foreach (var finding in audit.Findings)
            {
                var color = finding.Severity switch
                {
                    AuditSeverity.Violation => ControlTheme.Danger,
                    AuditSeverity.Warning => ControlTheme.Warning,
                    AuditSeverity.Info => ControlTheme.Info,
                    _ => ControlTheme.TextSecondary,
                };
                Add($"[{finding.Severity}] {finding.Check}: {finding.Detail}", DesignTokens.BodySize, false, color);
            }
        }
    }
}
