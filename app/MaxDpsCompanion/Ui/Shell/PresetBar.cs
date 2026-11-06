using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MaxDpsCompanion.Ui.Shell;

internal sealed class PresetBar : UserControl
{
    private readonly List<(ConsolePreset Preset, ToggleButton Button)> _buttons = new();
    private readonly TextBlock _summary = new()
    {
        Text = "Choose a preset; in-game settings can further restrict it.",
        TextWrapping = TextWrapping.Wrap,
        FontSize = 11,
        Foreground = ControlTheme.Brush(ControlTheme.TextSecondary),
    };
    public event Action<ConsolePreset>? PresetRequested;

    public PresetBar()
    {
        var stack = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 8) };
        var row = new Grid { ColumnSpacing = 6 };
        foreach (var bundle in ConsolePresets.All)
        {
            var index = row.ColumnDefinitions.Count;
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var button = new ToggleButton
            {
                Content = bundle.Name,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = 32,
                Padding = new Thickness(4, 0, 4, 0),
                FontSize = 12,
            };
            AutomationProperties.SetName(button, $"Preset: {bundle.Name}");
            ToolTipService.SetToolTip(button, bundle.Summary);
            button.Click += (_, _) => PresetRequested?.Invoke(bundle.Preset);
            Grid.SetColumn(button, index);
            row.Children.Add(button);
            _buttons.Add((bundle.Preset, button));
        }
        stack.Children.Add(row);
        stack.Children.Add(_summary);
        Content = stack;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(this, "Play presets");
    }

    public void SetActivePreset(ConsolePreset? preset)
    {
        foreach (var item in _buttons) item.Button.IsChecked = item.Preset == preset;
        _summary.Text = preset is { } selected ? ConsolePresets.Get(selected).Summary : "Choose a preset; in-game settings can further restrict it.";
    }
}
