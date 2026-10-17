using MaxDpsCompanion.Ui.Shell;
using Microsoft.UI.Xaml;

namespace MaxDpsCompanion;

/// <summary>
/// WinUI 3 application object (P1 shell). Program.Main runs every headless CLI
/// mode before <c>Application.Start</c> and hands the loaded settings over via
/// <see cref="Settings"/>; the shell window owns the rest of the lifecycle
/// (docs/plans/2026-10-11-winui3-v4-migration.md §3).
/// </summary>
public partial class App : Application
{
    /// <summary>Settings loaded once by Program.Main; shared by the shell (P2 screens read the same instance).</summary>
    internal static AppSettings? Settings { get; set; }

    private ShellWindow? _shell;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var settings = Settings ?? AppSettings.Load(Path.Combine(Program.AppDir, "settings.ini"));
        UiCliRunner.BeginShellBuild();
        _shell = new ShellWindow(settings);
        _shell.Activate();
        UiCliRunner.EndShellBuild();
        // Hosted CLI modes (--ui-snapshot*, --ui-smoke-test, --bench-ui) drive
        // the shown shell and close it when done (plan §5).
        if (UiCliRunner.Request is { Mode: not UiCliMode.None } request)
            _ = UiCliRunner.RunAsync(_shell, request);
    }
}
