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
/// lives inside Details, and Calibrate moved out of the transport row into the
/// same disclosure.
/// 4.5.0 "Voidlight": Details also carries the changelog. The open height is
/// clamped so the log keeps its reserve, and only the disclosure body scrolls.
/// </summary>
internal sealed class ExpConsoleView : UserControl
{
    // Old ConsoleHome Exp constants: collapsed 24 / expanded 54. 4.4.0 fixed the
    // expanded height so the 250 ms timer path could not reflow the card.
    // 4.5.0 keeps that contract but adds the changelog: the disclosure opens at
    // DetailsWanted and is clamped so the log always keeps LogReserve DIP; the
    // disclosure body scrolls when the clamp bites (small window), so nothing is
    // ever clipped mid-line.
    private const double DetailsCollapsed = 24;
    private const double DetailsWanted = 252;
    /// <summary>Rows above the disclosure (~104 DIP) plus the log's 60 DIP minimum.</summary>
    private const double LogReserve = 164;

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
    private readonly ScrollViewer _detailsScroll = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollMode = ScrollMode.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        HorizontalScrollMode = ScrollMode.Disabled,
    };
    /// <summary>4.5.1: always-visible slim thumb for the disclosure scroller (WinUI's own bar is hover-only).</summary>
    private readonly Border _detailsThumb = new()
    {
        Width = 3,
        CornerRadius = new CornerRadius(1.5),
        Background = ControlTheme.Brush(ControlTheme.AccentRing),
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 2, 2, 0),
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
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
        _detailsBody.Children.Add(BuildChangelog());
        _detailsScroll.Content = _detailsBody;

        var table = new Grid();
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0 eyebrow
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1 state row
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2 divider
        table.RowDefinitions.Add(_detailsRow);                                    // 3 details
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4 divider
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 5 log header
        table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 6 log

        // Eyebrow: 10px uppercase, tracked (brief §3); 4.5.0 tints it amethyst.
        var header = new TextBlock
        {
            Text = "STATUS",
            FontSize = 10,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ControlTheme.Brush(ControlTheme.WithAlpha(ControlTheme.Accent, 0xCC)),
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
        var detailsHost = new Grid();
        detailsHost.Children.Add(_detailsScroll);
        detailsHost.Children.Add(_detailsThumb);
        Grid.SetRow(detailsHost, 1);
        details.Children.Add(_detailsToggle);
        details.Children.Add(detailsHost);
        _detailsScroll.ViewChanged += (_, _) => UpdateDetailsThumb();
        _detailsScroll.SizeChanged += (_, _) => UpdateDetailsThumb();
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

        // 4.5.1: the disclosure height is a function of the card height, so a
        // live window resize (or a DPI change) must re-clamp it instead of
        // leaving the card overflowing/clipping its own content.
        SizeChanged += (_, _) => ResizeDetails();
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

    /// <summary>
    /// 4.5.0: the in-app changelog. Current release's highlights plus one muted
    /// tail line with the earlier versions. Copy comes from
    /// <see cref="ReleaseNotes"/>; the long form lives in docs/CHANGELOG.md.
    /// The whole block scrolls with the Details disclosure, so there is no
    /// nested scroller and nothing clips mid-line.
    /// </summary>
    private static FrameworkElement BuildChangelog()
    {
        var current = ReleaseNotes.Current;
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };

        stack.Children.Add(new TextBlock
        {
            Text = $"WHAT'S NEW \u00b7 {current.Label}",
            FontSize = 9.5,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            CharacterSpacing = 80,
            Foreground = ControlTheme.Brush(ControlTheme.Accent),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 4, 0, 0),
        });

        foreach (var line in current.Highlights)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "\u2022  " + line,
                FontSize = 10.5,
                Foreground = ControlTheme.Brush(ControlTheme.TextSecondary),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = "Earlier: " + ReleaseNotes.EarlierSummary,
            FontSize = 10,
            Foreground = ControlTheme.Brush(ControlTheme.TextMuted),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        });

        AutomationProperties.SetName(stack, $"Changelog for {current.Label}");
        AutomationProperties.SetHelpText(stack,
            "Newest first. The full release history is in docs/CHANGELOG.md");
        return stack;
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
        if (_detailsExpanded) ApplyDetailsHeight();
        else _detailsRow.Height = new GridLength(DetailsCollapsed);
    }

    /// <summary>
    /// 4.5.0: the open disclosure asks for <see cref="DetailsWanted"/> but never
    /// takes the log's reserve. 4.5.1: the Details body scrolls when the clamp
    /// bites, the row is re-clamped live on resize, and the scrollbar is forced
    /// visible exactly while the body cannot show everything (so a cut-off line
    /// always announces that more content is scrollable).
    /// </summary>
    private double ExpandedHeight()
    {
        if (ActualHeight <= 0) return DetailsWanted;
        var inner = ActualHeight - 38; // 2x bezel (5) + 2x card padding (14)
        var max = Math.Max(DetailsCollapsed + 60, inner - LogReserve);
        return Math.Min(DetailsWanted, max);
    }

    /// <summary>Re-clamps the open disclosure after a resize; no-op when collapsed.</summary>
    private void ResizeDetails()
    {
        if (!_detailsExpanded) return;
        ApplyDetailsHeight();
    }

    private void ApplyDetailsHeight()
    {
        var height = ExpandedHeight();
        _detailsRow.Height = new GridLength(height);
        var cramped = height < DetailsWanted - 0.5;
        _detailsScroll.VerticalScrollBarVisibility = cramped
            ? ScrollBarVisibility.Visible
            : ScrollBarVisibility.Auto;
        // Let the new row height settle before measuring the thumb.
        DispatcherQueue.TryEnqueue(UpdateDetailsThumb);
    }

    /// <summary>
    /// Slim disclosure scroll indicator: WinUI's own scrollbar is hover-only, so
    /// a cramped Details body would look truncated with no affordance. The thumb
    /// tracks extent/viewport and hides when everything fits.
    /// </summary>
    private void UpdateDetailsThumb()
    {
        var extent = _detailsScroll.ExtentHeight;
        var viewport = _detailsScroll.ViewportHeight;
        var overflow = _detailsExpanded && extent > viewport + 0.5 && viewport > 0;
        _detailsThumb.Visibility = overflow ? Visibility.Visible : Visibility.Collapsed;
        if (!overflow) return;

        var track = Math.Max(24, _detailsScroll.ActualHeight - 4);
        var thumb = Math.Max(18, track * (viewport / extent));
        _detailsThumb.Height = thumb;
        var maxOffset = extent - viewport;
        var ratio = maxOffset > 0 ? _detailsScroll.VerticalOffset / maxOffset : 0;
        _detailsThumb.Margin = new Thickness(0, 2 + Math.Clamp(ratio, 0, 1) * (track - thumb), 2, 0);
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
        ControlTheme.ApplyAppStyle(this, "UiClickableStyle");
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
        AutomationProperties.SetHelpText(this, "Expand the read-only bridge, mask and rate details and the changelog");
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
