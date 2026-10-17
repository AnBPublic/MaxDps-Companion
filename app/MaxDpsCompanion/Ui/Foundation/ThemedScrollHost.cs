using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MaxDpsCompanion;

/// <summary>
/// P2-B port of the S5 ThemedScrollHost (old UiFoundation.cs): ScrollViewer is
/// sealed in WinUI 3, so the host is a thin UserControl that owns a vertical
/// viewer (<see cref="Viewer"/>) with the classic vertical-only behaviour and
/// DesignTokens-tinted scrollbar resources, plus a <see cref="Items"/> stack
/// for page content (old host + AutoScroll children).
/// </summary>
public sealed class ThemedScrollHost : UserControl
{
    public ScrollViewer Viewer { get; } = new();

    /// <summary>Content stack: add rows/primitives here (old Controls.Add).</summary>
    public VertStack Items { get; } = new();

    public ThemedScrollHost()
    {
        Viewer.HorizontalScrollMode = ScrollMode.Disabled;
        Viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Viewer.VerticalScrollMode = ScrollMode.Auto;
        Viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Viewer.ZoomMode = ZoomMode.Disabled;
        Viewer.Content = Items;

        Viewer.Resources["ScrollBarTrackFill"] = ControlTheme.Brush(ControlTheme.ScrollTrack);
        Viewer.Resources["ScrollBarTrackFillPointerOver"] = ControlTheme.Brush(ControlTheme.ScrollTrack);
        Viewer.Resources["ScrollBarTrackFillPressed"] = ControlTheme.Brush(ControlTheme.ScrollTrack);
        Viewer.Resources["ScrollBarTrackStroke"] = ControlTheme.Brush(Microsoft.UI.Colors.Transparent);
        Viewer.Resources["ScrollBarThumbFill"] = ControlTheme.Brush(ControlTheme.ScrollThumb);
        Viewer.Resources["ScrollBarThumbFillPointerOver"] = ControlTheme.Brush(ControlTheme.AccentSoft);
        Viewer.Resources["ScrollBarThumbFillPressed"] = ControlTheme.Brush(ControlTheme.Accent);

        Content = Viewer;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    public void Add(UIElement child) => Items.Children.Add(child);
}
