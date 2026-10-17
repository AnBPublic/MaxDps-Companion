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
// (link state, now-action + why, last key), a minimal settings block
// (pause / target / interact key) and a rolling log.
// The engine runs on its own thread via MacEngineHost; UI updates are
// marshalled with Dispatcher.UIThread.Post. Start/Stop are invoked off the UI
// thread: both can block on a capture syscall (SCShareableContent, one-shot
// locate, stream stop), and a frozen window reads as "the app hangs".

internal sealed class MacApp : Application
{
    private MacEngineHost? _host;
    private AppSettings _settings = null!;
    private Window _window = null!;
    private readonly MacAppPaths _paths = new();
    private readonly MacPermissions _permissions = new();

    private TextBlock _stateValue = null!;
    private TextBlock _permValue = null!;
    private TextBlock _classValue = null!;
    private TextBlock _nowValue = null!;
    private TextBlock _lastKeyValue = null!;
    private TextBlock _log = null!;
    private TextBox _pauseKey = null!;
    private TextBox _targetKey = null!;
    private TextBox _interactKey = null!;
    private Button _start = null!;
    private Button _pause = null!;
    private Button _stop = null!;
    private readonly List<string> _logLines = new();

    /// <summary>True while a Start/Stop transition runs off the UI thread.</summary>
    private bool _busy;

    /// <summary>Set when the last Start was refused: the engine is NOT running.</summary>
    private bool _blockedByPermission;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _settings = Program.LoadSettings();
        _host = new MacEngineHost(_settings);
        _host.StatusChanged += OnStatus;
        // Fail-closed send diagnostics: a keyboard stroke the Mac cannot
        // deliver (unmapped VK / no target pid) is reported once per VK.
        MacVkMap.Unmapped += OnDiagnostic;
        MacKeySender.Dropped += OnDiagnostic;
        _window = BuildWindow();
        _window.Closed += (_, _) =>
        {
            PersistSettings();
            _host?.Dispose();
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _window;
            _window.Show();
        }

        RefreshPermissions();
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
        _permValue = ValueText("—");
        _classValue = ValueText("—");
        _nowValue = ValueText("—");
        _lastKeyValue = ValueText("—");
        var status = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Row("State", _stateValue),
                Row("Permissions", _permValue),
                Row("Class / Spec", _classValue),
                Row("Now", _nowValue),
                Row("Last key", _lastKeyValue),
            },
        };

        // Transport row (Windows: Start | Pause | Stop).
        _start = MakeButton("Start", OnStart, primary: true);
        _pause = MakeButton("Pause", OnPause);
        _stop = MakeButton("Stop", OnStop);
        _stop.IsEnabled = false;
        _pause.IsEnabled = false;
        var transport = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 14, 0, 8),
            Children = { _start, _pause, _stop },
        };

        // Minimal settings block: the three strokes a user actually binds.
        _pauseKey = MakeKeyBox(_settings.PauseHotkey);
        _targetKey = MakeKeyBox(_settings.TargetKey);
        _interactKey = MakeKeyBox(_settings.InteractKey);
        var save = MakeButton("Save settings", OnSaveSettings);
        var settingsBox = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(0, 4, 0, 8),
            Children =
            {
                Row("Pause key", _pauseKey),
                Row("Target key", _targetKey),
                Row("Interact key", _interactKey),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { save },
                },
            },
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
            Child = new ScrollViewer { Content = _log, Height = 130 },
        };

        var body = new StackPanel
        {
            Margin = new Thickness(18),
            Children = { title, sub, status, transport, settingsBox, secondary, logBox },
        };

        return new Window
        {
            Title = "MaxDPS Companion",
            Width = 600,
            Height = 640,
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

    private static TextBox MakeKeyBox(string value) => new()
    {
        Text = value,
        Width = 140,
        FontSize = 12,
        Watermark = "e.g. F, Tab, SHIFT-1",
    };

    private static Control Row(string label, Control value)
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
            Spacing = 6,
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

    /// <summary>Shows the two TCC gates; never prompts (Start does).</summary>
    private void RefreshPermissions()
    {
        if (_permValue is null) return;
        var capture = SafeCheck(_permissions.CheckCapturePermission);
        var input = SafeCheck(_permissions.CheckInputPermission);
        _permValue.Text =
            $"Screen Recording {(capture ? "granted" : "NOT granted")} · Accessibility {(input ? "granted" : "NOT granted")}";
        _permValue.Foreground = capture && input ? Brushes.Gray : Brushes.OrangeRed;
    }

    private static bool SafeCheck(Func<bool> check)
    {
        try { return check(); } catch { return false; }
    }

    private async void OnStart()
    {
        if (_host is null || _busy) return;
        if (!_host.EnsurePermissions(out var msg))
        {
            // FAIL CLOSED: a missing permission must not leave a started
            // engine behind (no capture -> nothing decodes; no grant -> every
            // send holds). The UI keeps Start enabled so the user can retry.
            _blockedByPermission = true;
            _stateValue.Text = "Blocked: permission missing";
            RefreshPermissions();
            Log(msg);
            return;
        }

        _blockedByPermission = false;
        RefreshPermissions();
        Log(msg);
        _busy = true;
        _start.IsEnabled = false;
        _stateValue.Text = "Starting…";
        try
        {
            // EnsureStream + the warm locate = blocking capture calls. Run them
            // off the UI thread so the window never freezes.
            await Task.Run(() => _host.Start());
            _stop.IsEnabled = true;
            _pause.IsEnabled = true;
            _pause.Content = "Pause";
            _stateValue.Text = "Running";
        }
        catch (Exception ex)
        {
            _stateValue.Text = "Start failed";
            _start.IsEnabled = true;
            _stop.IsEnabled = false;
            _pause.IsEnabled = false;
            Log($"start failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally { _busy = false; }
    }

    private void OnPause()
    {
        if (_host is null) return;
        _host.Engine.Paused = !_host.Engine.Paused;
        _pause.Content = _host.Engine.Paused ? "Resume" : "Pause";
        _stateValue.Text = _host.Engine.Paused ? "Paused" : "Running";
    }

    private async void OnStop()
    {
        if (_host is null || _busy) return;
        _busy = true;
        _stop.IsEnabled = false;
        _stateValue.Text = "Stopping…";
        try
        {
            // Stop joins the supervisor and tears the stream down: blocking.
            await Task.Run(() => _host.Stop());
        }
        catch (Exception ex) { Log($"stop failed: {ex.GetType().Name}: {ex.Message}"); }
        finally
        {
            _busy = false;
            _start.IsEnabled = true;
            _stop.IsEnabled = false;
            _pause.IsEnabled = false;
            _pause.Content = "Pause";
            _stateValue.Text = _blockedByPermission ? "Blocked: permission missing" : "Stopped";
        }
    }

    /// <summary>Writes the three key fields into settings.ini (per machine,
    /// next to the app data — never the tracked seed).</summary>
    private void OnSaveSettings()
    {
        PersistSettings();
        Log($"settings saved: {_paths.SettingsPath}");
    }

    private void PersistSettings()
    {
        if (_settings is null || _pauseKey is null) return;
        try
        {
            var pause = (_pauseKey.Text ?? "").Trim();
            var target = (_targetKey.Text ?? "").Trim();
            var interact = (_interactKey.Text ?? "").Trim();
            if (pause.Length > 0) _settings.PauseHotkey = pause;
            if (target.Length > 0) _settings.TargetKey = target;
            if (interact.Length > 0) _settings.InteractKey = interact;
            Directory.CreateDirectory(_paths.AppDir);
            _settings.Save();
        }
        catch (Exception ex) { Log($"settings save failed: {ex.GetType().Name}: {ex.Message}"); }
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

    private void OnDiagnostic(string line)
    {
        try { Dispatcher.UIThread.Post(() => Log(line)); }
        catch { /* shutting down: the status line is gone */ }
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
