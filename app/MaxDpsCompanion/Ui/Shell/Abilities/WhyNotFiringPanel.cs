using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// P2-D "why isn't this firing?" panel (WinUI port of the old owner-drawn
/// Ui/WhyNotFiringPanel.cs): renders <see cref="WhyNotFiring.Explain"/> — toggle
/// state, scheduler verdict and candidate staleness — for the last observed
/// plan head. No game read, no scheduler change; the shell pushes a
/// <see cref="WhyNotFiringFacts"/> snapshot once per UI tick.
/// </summary>
internal sealed class WhyNotFiringPanel : UserControl
{
    private readonly TextBlock _title = new()
    {
        Text = "Why isn't this firing?",
        FontSize = ControlTheme.Dip(10.5f),
        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        Foreground = ControlTheme.Brush(ControlTheme.TextPrimary),
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly StackPanel _lines = new();

    public WhyNotFiringPanel()
    {
        var stack = new VertStack { Gap = 2, Padding = new Thickness(2, 2, 2, 2) };
        stack.Children.Add(_title);
        stack.Children.Add(_lines);
        Content = stack;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Top;
        MinHeight = 96;
        AutomationProperties.SetName(this, "Why isn't this firing");
        Ready = true;
    }

    /// <summary>True once a facts snapshot was rendered (diagnostics witness).</summary>
    public bool Ready { get; private set; }

    /// <summary>Pushes one snapshot and re-renders the honest list of reasons.</summary>
    public void Update(in WhyNotFiringFacts facts)
    {
        _title.Text = $"Why isn't {facts.Label} firing?";
        Accessibility(facts);

        var lines = WhyNotFiring.Explain(facts);
        _lines.Children.Clear();
        foreach (var line in lines)
        {
            _lines.Children.Add(new TextBlock
            {
                Text = "\u00B7 " + line,
                FontSize = ControlTheme.Dip(DesignTokens.MetaSize),
                Foreground = ControlTheme.Brush(ControlTheme.TextSecondary),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        Ready = true;
    }

    private void Accessibility(in WhyNotFiringFacts facts)
    {
        AutomationProperties.SetName(this, WhyNotFiring.Summary(facts));
    }
}
