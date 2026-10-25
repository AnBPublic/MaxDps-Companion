using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WColor = Windows.UI.Color;
using MediaFontFamily = Microsoft.UI.Xaml.Media.FontFamily;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// P2-C WinUI port of the Exp (minimal) console home (old ConsoleHome.cs
/// minimal branch :585-879): ONE status card with the short human state line,
/// the collapsed read-only "Details" disclosure (bridge + mask + rate witness,
/// the Now line and the compact Calibrate action), and the single mono rolling
/// log. No spec header, no preset strip and no update-history feed — exactly
/// the minimal shell contract.
/// Fed by <see cref="ShellWindow"/> from the same timer path as the classic
/// console (MainForm.UpdateHero :3081-3109 + UpdateExpReadouts :971-1019).
///
/// 4.4.0 "Wayfinder": the always-visible Now row is GONE from the card; Now
/// lives inside Details (fixed 128 DIP expanded height so a wrapping Now can
/// never re-lay out the log), and Calibrate moved out of the transport row
/// into the same disclosure.
/// </summary>
internal sealed class ExpConsoleView : UserControl
{
    // Old ConsoleHome Exp constants: collapsed 24 / expanded 54. 4.4.0: the
    // expanded height is FIXED at 140 (details + up to 2 wrapped Now lines +
    // the 32 DIP calibrate pill + spacing), never Auto, so the timer path
    // cannot reflow; 140 leaves headroom for the largest width tier.
    private const double DetailsCollapsed = 24;
    private const double DetailsExpanded = 140;

    private readonly LinkLamp _lamp = new() { DotSize = 8 };
    private readonly TextBlock _stateValue = new()
    {
        FontSize = 15,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = ControlTheme.Brush(ControlTheme.TextPrimary),
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly ExpDetailsToggle _detailsToggle = new();
    private readonly TextBlock _detailsValue;
    private readonly TextBlock _nowValue = new()
    {
        Text = "Now: idle - waiting for a MaxDps suggestion.",
        FontSize = ControlTheme.Dip(DesignTokens.BodySize - 1f),
        Foreground = ControlTheme.Brush(Exp(ExpTheme.Secondary)),
        TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = 2,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly ChamferButton _calibrate = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(0, 2, 0, 0),
    };
    private readonly StackPanel _detailsBody = new()
    {
        Orientation = Orientation.Vertical,
        Spacing = 6,
        Visibility = Visibility.Collapsed,
    };
    private readonly RollingLogView _log = new()
    {
        UseMono = true,
        StampTone = ControlTheme.TextWhite40,
        EmptyText = "No events yet - waiting for bridge samples.",
        EmptyTone = Exp(ExpTheme.MonoStamp),
        Capacity = 200,
    };
    private readonly RowDefinition _detailsRow = new() { Height = new GridLength(DetailsCollapsed) };

    private string _className = "";
    private string _specName = "";
    private bool _detailsExpanded;
    private bool _calibrating;

    /// <summary>Raised by the compact Calibrate pill inside Details.</summary>
    public event Action? CalibrateRequested;

    public ExpConsoleView()
    {
        // v4.0.1 double-bezel card (brief §1): the shared RoundedCard now draws
        // the white/4 outer wash + white/6 ring and the top-edge highlight; the
        // Exp card keeps its opaque terminal fill on the concentric core.
        var card = new RoundedCard
        {
            FillColor = Exp(ExpTheme.Card),
            Padding = new Thickness(16, 14, 16, 14),
            // 4.4.0: the table fills the card so the rolling log owns the space
            // freed by moving Now/Calibrate into the Details disclosure. The
            // RoundedCard default is Top (hero-card behaviour); the Exp console
            // is a single full-height log surface.
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };

        _detailsValue = new TextBlock
        {
            FontSize = ControlTheme.Dip(DesignTokens.MicroSize),
            FontFamily = new MediaFontFamily("Cascadia Mono, Consolas"),
            Foreground = ControlTheme.Brush(ControlTheme.TextWhite40),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(_nowValue, _nowValue.Text);
        AutomationProperties.SetName(_detailsValue, "Details");
        AutomationProperties.SetName(_stateValue, "Engine state");
        AutomationProperties.SetName(_nowValue, "Current suggestion");
        AutomationProperties.SetName(_detailsToggle, "Details");
        _detailsToggle.Click += (_, _) => ToggleDetails();

        // Compact disclosure action (4.4.0 brief §2): ghost fill on the Exp card
        // surface, 32 DIP tall, accessible name from SetLabel ("◉ Calibrate").
        _calibrate.SetLabel("\u25C9", "Calibrate");
        // ChamferButton's own ctor owns Height/Padding/FontSize (42/label), so
        // the compact 32 DIP disclosure size is applied AFTER construction.
        _calibrate.Height = 32;
        _calibrate.MinHeight = 32;
        _calibrate.Padding = new Thickness(10, 0, 12, 0);
        _calibrate.FontSize = ControlTheme.Dip(DesignTokens.MicroSize + 1f);
        _calibrate.Role = ButtonRole.Ghost;
        _calibrate.AccentColor = Exp(ExpTheme.Accent);
        _calibrate.GhostSurface = Exp(ExpTheme.Card);
        AutomationProperties.SetHelpText(_calibrate,
            "Start the colour calibration pattern in game; press again to cancel");
        _calibrate.Click += (_, _) => CalibrateRequested?.Invoke();

        _detailsBody.Children.Add(_detailsValue);
        _detailsBody.Children.Add(_nowValue);
        _detailsBody.Children.Add(_calibrate);

        var table = new Grid();
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0 eyebrow
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1 state row
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2 divider
        table.RowDefinitions.Add(_detailsRow);                                    // 3 details
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4 divider
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 5 log header
        table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 6 log

        // Eyebrow: 10px uppercase white/45, tracked (brief §3).
        var header = new TextBlock
        {
            Text = "STATUS",
            FontSize = 10,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ControlTheme.Brush(ControlTheme.TextWhite45),
            CharacterSpacing = 80,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(header, 0);
        table.Children.Add(header);

        _lamp.VerticalAlignment = VerticalAlignment.Center;
        _lamp.Margin = new Thickness(0, 0, 8, 0);
        var stateRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, MinHeight = 22 };
        stateRow.Children.Add(_lamp);
        stateRow.Children.Add(_stateValue);
        Grid.SetRow(stateRow, 1);
        table.Children.Add(stateRow);

        AddDivider(table, 2);

        var details = new Grid();
        details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _detailsToggle.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(_detailsToggle, 0);
        Grid.SetRow(_detailsBody, 1);
        details.Children.Add(_detailsToggle);
        details.Children.Add(_detailsBody);
        Grid.SetRow(details, 3);
        table.Children.Add(details);

        AddDivider(table, 4);

        var logHeader = new TextBlock
        {
            Text = "LOG",
            FontSize = 10,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ControlTheme.Brush(ControlTheme.TextWhite40),
            CharacterSpacing = 80,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(logHeader, 5);
        table.Children.Add(logHeader);

        Grid.SetRow(_log, 6);
        table.Children.Add(_log);

        card.Add(table);
        Content = card;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(this, "Exp console");
    }

    /// <summary>1px hairline divider between card sections (white 6%, brief §1).</summary>
    private static void AddDivider(Grid table, int row)
    {
        var divider = new Border
        {
            Height = 1,
            Background = ControlTheme.Brush(ControlTheme.Ring),
            Margin = new Thickness(0, 10, 0, 10),
            VerticalAlignment = VerticalAlignment.Center,
        };
        table.Children.Add(divider);
        Grid.SetRow(divider, row);
    }

    /// <summary>Exp test seam parity (old DetailsForTest :492).</summary>
    public string DetailsForTest => _detailsValue.Text;

    /// <summary>Exp test seam parity (old StateLineForTest :495).</summary>
    public string StateLineForTest => _stateValue.Text;

    /// <summary>Exp test seam parity (old DetailsExpandedForTest :498).</summary>
    public bool DetailsExpandedForTest => _detailsExpanded;

    public void ToggleDetailsForTest() => ToggleDetails();

    /// <summary>
    /// Exp spec line (old minimal SetSpec :441-460): the single log keeps one
    /// "[spec] ..." line per change; no header, no update-history note.
    /// </summary>
    public void SetSpec(string? className, string? specName)
    {
        var cls = className ?? "";
        var spec = specName ?? "";
        if (_className == cls && _specName == spec) return;
        _className = cls;
        _specName = spec;
        var (role, _) = ConsoleRole.ForSpec(className, specName);
        var title = string.IsNullOrEmpty(spec) ? "AUTO DETECT" : $"{cls} \u00b7 {spec}";
        AppendLog($"[spec] {title} - role {role}", ControlTheme.Info);
    }

    /// <summary>Exp short state phrase + lamp (old SetState :462-470). v4.0.1: the 8px dot pulse-loops while Running.</summary>
    public void SetState(string state, WColor tone)
    {
        if (_stateValue.Text != state)
        {
            _stateValue.Text = state;
            _stateValue.Foreground = ControlTheme.Brush(tone);
        }
        _lamp.Dot = tone;
        if (string.Equals(state, "Running", StringComparison.OrdinalIgnoreCase)) _lamp.PulseLoop();
        else _lamp.StopPulse();
    }

    /// <summary>Exp details witness line (old SetDetails :485-489).</summary>
    public void SetDetails(string text)
    {
        if (_detailsValue.Text != text) _detailsValue.Text = text;
    }

    public void SetNow(string action, string why)
    {
        var text = string.IsNullOrEmpty(action) || action == "-"
            ? "Now: idle - waiting for a MaxDps suggestion."
            : $"Now: {action} \u2014 {why}";
        if (_nowValue.Text == text) return;
        _nowValue.Text = text;
        // The row is capped at 2 lines; the tooltip/HelpText carry the full
        // copy, while the stable accessible NAME stays "Current suggestion"
        // (screen readers and the UIA contract must not see it churn).
        ToolTipService.SetToolTip(_nowValue, text);
        AutomationProperties.SetHelpText(_nowValue, text);
    }

    /// <summary>
    /// 4.4.0: the details Calibrate pill doubles as Cancel while the worker
    /// runs (label swap only; it always stays enabled so a run can be cancelled).
    /// </summary>
    public void SetCalibrating(bool running)
    {
        if (_calibrating == running) return;
        _calibrating = running;
        _calibrate.SetLabel(running ? "\u25A0" : "\u25C9", running ? "Cancel" : "Calibrate");
        _calibrate.IsEnabled = true;
    }

    public void AppendLog(string text, WColor tone)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        _log.Append($"{stamp}  {text}", tone);
    }

    public void ApplyScale(UiScale scale)
    {
        // 15px state minimum keeps the brief's hierarchy at every width tier.
        _stateValue.FontSize = Math.Max(15, ControlTheme.Dip(Math.Max(8f, scale.BaseFont + 2f)));
        _nowValue.FontSize = ControlTheme.Dip(Math.Max(8f, scale.BaseFont - 1f));
        _log.ApplyScale(scale);
    }

    /// <summary>Collapsed disclosure (old ToggleDetails :887-895); the reveal fades in (150 ms).</summary>
    private void ToggleDetails()
    {
        _detailsExpanded = !_detailsExpanded;
        _detailsToggle.Expanded = _detailsExpanded;
        _detailsBody.Visibility = _detailsExpanded ? Visibility.Visible : Visibility.Collapsed;
        if (_detailsExpanded) Motion.Reveal(_detailsBody);
        _detailsRow.Height = new GridLength(_detailsExpanded ? DetailsExpanded : DetailsCollapsed);
    }

    private static WColor Exp(System.Drawing.Color color) =>
        WColor.FromArgb(color.A, color.R, color.G, color.B);
}

/// <summary>
/// Collapsed disclosure row (old DetailsDisclosure :149-188); v4.0.1: constant
/// "Details" label + a circular chevron chip that rotates ~90° with the shared
/// easing curve on expand (UIA name stays "Details").
/// </summary>
internal sealed class ExpDetailsToggle : UiClickable
{
    public const string CollapsedText = "Details";

    private const double ChipSize = 22;

    private readonly TextBlock _text;
    private readonly Border _chip;
    private bool _expanded;

    public ExpDetailsToggle()
    {
        _text = new TextBlock
        {
            Text = CollapsedText,
            FontSize = ControlTheme.Dip(DesignTokens.MicroSize),
            FontFamily = new MediaFontFamily("Cascadia Mono, Consolas"),
            Foreground = ControlTheme.Brush(ControlTheme.TextWhite45),
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Segoe Fluent Icons chevron (E76C = ChevronRight); the chip rotates to
        // point down when expanded instead of swapping glyphs.
        var chevron = new FontIcon
        {
            Glyph = "\uE76C",
            FontFamily = new MediaFontFamily("Segoe Fluent Icons"),
            FontSize = 10,
            Foreground = ControlTheme.Brush(ControlTheme.TextWhite80),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _chip = new Border
        {
            Width = ChipSize,
            Height = ChipSize,
            CornerRadius = new CornerRadius(ChipSize / 2),
            Background = ControlTheme.Brush(ControlTheme.ChipLight),
            Child = chevron,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_text);
        row.Children.Add(_chip);

        Height = ChipSize;
        Padding = new Thickness(0);
        HorizontalAlignment = HorizontalAlignment.Left;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        AutomationProperties.SetName(this, "Details");
        AutomationProperties.SetHelpText(this, "Expand the read-only bridge, mask and rate details");
        Content = row;
    }

    public bool Expanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value) return;
            _expanded = value;
            Motion.Rotate(_chip, value ? 90 : 0, ChipSize, ChipSize);
        }
    }
}
