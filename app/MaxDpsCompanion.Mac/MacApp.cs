using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace MaxDpsCompanion.Mac;

// Avalonia desktop app. Mirrors the Windows companion's control set:
// Start / Pause / Stop, Launch Game, Open Folder, plus a live status block
// (link state, now-action + why, last key) and a rolling log.
// The engine runs on its own thread via MacEngineHost; UI updates are
// marshalled with Dispatcher.UIThread.Post.

internal sealed class MacApp : Application
{
    private MacEngineHost? _host;
    private AppSettings _settings = null!;
    private Window _window = null!;
    private readonly MacAppPaths _paths = new();

    private TextBlock _stateValue = null!;
    private TextBlock _classValue = null!;
    private TextBlock _nowValue = null!;
    private TextBlock _lastKeyValue = null!;
    private TextBlock _log = null!;
    private Button _start = null!;
    private Button _pause = null!;
    private Button _stop = null!;
    private readonly List<string> _logLines = new();

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _settings = Program.LoadSettings();
        _host = new MacEngineHost(_settings);
        _host.StatusChanged += OnStatus;
        _window = BuildWindow();
        _window.Closed += (_, _) => _host?.Dispose();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _window;
            _window.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private Window BuildWindow()
    {
        var title = new TextBlock
        {
            Text = "MaxDPS Companion",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 2),
        };
        var sub = new TextBlock
        {
            Text = $"v{AppVersionInfo.AppVersion} {AppVersionInfo.Codename} — Mac",
            FontSize = 12,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 12),
        };

        // Status block (mirrors the Windows hero card rows).
        _stateValue = ValueText("Stopped");
        _classValue = ValueText("—");
        _nowValue = ValueText("—");
        _lastKeyValue = ValueText("—");
        var status = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Row("State", _stateValue),
                Row("Class / Spec", _classValue),
                Row("Now", _nowValue),
                Row("Last key", _lastKeyValue),
            },
        };

        // Transport row (Windows: Start | Pause | Stop).
        _start = MakeButton("Start", OnStart, primary: true);
        _pause = MakeButton("Pause", OnPause);
        _stop = MakeButton("Stop", OnStop);
        var transport = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 14, 0, 8),
            Children = { _start, _pause, _stop },
        };

        // Secondary row (Windows: Launch Game | Open Folder).
        var launch = MakeButton("Launch Game", OnLaunch);
        var folder = MakeButton("Open Folder", OnOpenFolder);
        var secondary = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { launch, folder },
        };

        _log = new TextBlock
        {
            FontFamily = new FontFamily("Menlo, monospace"),
            FontSize = 11,
            TextWrapping = TextWrapping.NoWrap,
            Foreground = Brushes.Gray,
            Text = "Ready.",
        };
        var logBox = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x16, 0x1A)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 12, 0, 0),
            Child = new ScrollViewer { Content = _log, Height = 150 },
        };

        var body = new StackPanel
        {
            Margin = new Thickness(18),
            Children = { title, sub, status, transport, secondary, logBox },
        };

        return new Window
        {
            Title = "MaxDPS Companion",
            Width = 560,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = body,
            CanResize = true,
        };
    }

    private static TextBlock ValueText(string text) => new()
    {
        Text = text,
        FontSize = 13,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static Control Row(string label, TextBlock value)
    {
        var l = new TextBlock
        {
            Text = label,
            Width = 110,
            FontSize = 12,
            Foreground = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
        };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { l, value },
        };
    }

    private static Button MakeButton(string text, Action onClick, bool primary = false)
    {
        var b = new Button { Content = text, MinWidth = 96, Padding = new Thickness(12, 6) };
        if (primary) b.Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x7D, 0x6A));
        b.Click += (_, _) => onClick();
        return b;
    }

    private void OnStart()
    {
        if (_host is null) return;
        if (!_host.EnsurePermissions(out var msg))
            Log(msg);
        else
            Log(msg);
        _host.Start();
        _start.IsEnabled = false;
        _stop.IsEnabled = true;
        _pause.IsEnabled = true;
        _stateValue.Text = "Running";
    }

    private void OnPause()
    {
        if (_host is null) return;
        _host.Engine.Paused = !_host.Engine.Paused;
        _pause.Content = _host.Engine.Paused ? "Resume" : "Pause";
        _stateValue.Text = _host.Engine.Paused ? "Paused" : "Running";
    }

    private void OnStop()
    {
        if (_host is null) return;
        _host.Stop();
        _start.IsEnabled = true;
        _stop.IsEnabled = false;
        _pause.IsEnabled = false;
        _pause.Content = "Pause";
        _stateValue.Text = "Stopped";
    }

    private void OnLaunch()
    {
        var ok = MacLauncher.TryLaunch(_settings.BNetPath, out var msg);
        Log(msg + (ok ? "" : " (set the path in settings.ini if this is wrong)"));
    }

    private void OnOpenFolder()
    {
        try
        {
            Directory.CreateDirectory(_paths.AppDir);
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("open", $"\"{_paths.AppDir}\"")
                { UseShellExecute = false });
        }
        catch (Exception ex) { Log($"open folder failed: {ex.Message}"); }
    }

    private void OnStatus(EngineStatus s)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _stateValue.Text = s.BridgeVisible ? $"{s.State}" : s.Message;
            _nowValue.Text = string.IsNullOrWhiteSpace(s.Decision) ? "—" : s.Decision;
            _lastKeyValue.Text = string.IsNullOrWhiteSpace(s.LastKeySent) ? "—" : s.LastKeySent;
            if (_host?.Engine.TryGetLiveClass(out var cls) == true)
            {
                _host.Engine.TryGetLiveSpec(out var spec);
                _classValue.Text = $"{cls} / {spec}";
            }
            Log(s.Message);
        });
    }

    private void Log(string line)
    {
        _logLines.Add($"{DateTime.Now:HH:mm:ss}  {line}");
        if (_logLines.Count > 200) _logLines.RemoveRange(0, _logLines.Count - 200);
        if (_log is not null) _log.Text = string.Join("\n", _logLines);
    }
}
