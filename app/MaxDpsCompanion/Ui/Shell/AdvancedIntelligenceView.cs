using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// P2-C Advanced "Intelligence" tab: real content replacing the placeholder.
/// WinUI port of IntelligencePage (old Ui/Pages.cs :104-217): the 13-bucket
/// registry health tiles, the single ProportionBar composition and the
/// patch/catalog provenance rows. The coverage report is built once on first
/// open (never per paint) and a tile click drills through to the Abilities
/// overlay (old DrillRequested -> ShowAbilitiesWithPreset :320).
/// </summary>
internal sealed class AdvancedIntelligenceView : UserControl
{
    private readonly Dictionary<string, MetricTile> _tiles = new();
    private readonly KvRow _patchRow = new("Patch");
    private readonly KvRow _catalogRow = new("Catalog revision");
    private readonly KvRow _verifiedRow = new("Registry verified");
    private readonly ProportionBarView _bar = new();
    private readonly WrapFlow _tileFlow = new() { Gap = DesignTokens.SpaceS, RowGap = DesignTokens.SpaceS };
    private bool _built;

    public event Action<string>? DrillRequested;

    public AdvancedIntelligenceView()
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
            _tileFlow.Children.Add(tile);
        }

        var content = new VertStack { Gap = 12 };
        content.Children.Add(AdvancedUi.Card("Registry health", "Coverage", _tileFlow));
        content.Children.Add(AdvancedUi.Card("Proportion", "Composition", _bar));
        content.Children.Add(AdvancedUi.Card("Patch & catalog", "Provenance", _patchRow, _catalogRow, _verifiedRow));

        var host = new ThemedScrollHost();
        AdvancedUi.Scroll(content, host);
        Content = host;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(this, "Intelligence page");
    }

    /// <summary>The built coverage report (null until <see cref="EnsureBuilt"/>).</summary>
    public CoverageReport? Report { get; private set; }

    /// <summary>Test hook: the clickable metric tiles, keyed by coverage bucket.</summary>
    internal IReadOnlyDictionary<string, MetricTile> TilesForTest => _tiles;

    private static string DrillTagFor(string key) => key switch
    {
        "Companion" => "Companion",
        "MaxDps" => "MaxDps",
        "Manual" => "Manual",
        "Stale" or "Missing" or "Duplicates" => "Warnings",
        _ => "All",
    };

    /// <summary>Builds the coverage report once (old EnsureBuilt :173-207).</summary>
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
        _bar.SetSegments(
            (report.CompanionGenerated, ControlTheme.Success, "companion"),
            (report.SharedGated, ControlTheme.Accent, "shared"),
            (report.MaxDpsDelegated, ControlTheme.Info, "maxdps"),
            (report.Manual, ControlTheme.TextMuted, "manual"),
            (report.ResearchPending, ControlTheme.Warning, "research"));
    }

    private void Set(string key, int value, StatusTone tone)
    {
        if (!_tiles.TryGetValue(key, out var tile)) return;
        tile.Value = value.ToString();
        tile.Tone = tone;
        AutomationProperties.SetName(tile, $"{tile.Label}: {value}");
    }
}
