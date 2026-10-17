using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// P2-E hosted-CLI hooks (docs/plans/2026-10-11-winui3-v4-migration.md §5):
/// the shell exposes just enough surface for <see cref="UiCliRunner"/> to wait
/// for a real shown window, resize it for snapshots, and inspect overlay state.
/// Everything here is inert for the normal interactive launch.
/// </summary>
public sealed partial class ShellWindow
{
    /// <summary>
    /// CLI snapshot/bench runs resize the window to synthetic sizes; those must
    /// never be persisted into settings.ini as the user's remembered size.
    /// </summary>
    internal bool SuppressSizePersistence { get; set; }

    /// <summary>The visual root (RenderTargetBitmap target + validation tree root).</summary>
    internal FrameworkElement CliRoot => RootGrid;

    internal bool AdvancedOverlayOpen => _advancedOverlay is { IsOpen: true };

    internal bool AbilitiesOverlayOpen => _abilitiesOverlay is { IsOpen: true };

    internal bool SettingsInGameConfigMode => _settings.InGameConfigMode;

    /// <summary>
    /// Waits until the shell is shown and has completed its first layout pass.
    /// RenderTargetBitmap needs a loaded XamlRoot; the two delays give the
    /// compositor a frame to pick the layout up before the first render.
    /// </summary>
    internal async Task WaitForShellReadyAsync()
    {
        if (!RootGrid.IsLoaded)
        {
            var loaded = new TaskCompletionSource();
            void OnLoaded(object sender, RoutedEventArgs e)
            {
                RootGrid.Loaded -= OnLoaded;
                loaded.TrySetResult();
            }
            RootGrid.Loaded += OnLoaded;
            await loaded.Task;
        }
        await Task.Delay(200); // first composition pass
        RootGrid.UpdateLayout();
        await Task.Delay(100); // compositor frame with the settled layout
    }

    /// <summary>Resizes the hosted window for a snapshot pass (physical pixels).</summary>
    internal void ResizeForCli(int width, int height) =>
        AppWindow.Resize(new SizeInt32(Math.Max(320, width), Math.Max(400, height)));
}
