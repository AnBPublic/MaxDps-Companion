using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using WinRT.Interop;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// P1 borderless shell window (docs/plans/2026-10-11-winui3-v4-migration.md §3).
/// WinForms MainForm parity for the P1 surface:
/// <list type="bullet">
///   <item>borderless, resizable; 660x920 DIP default, 520x560 DIP minimum;</item>
///   <item>custom title bar (icon + "MaxDps Companion {DisplayVersion}"),
///     drag via InputNonClientPointerSource caption rects, minimize/close
///     buttons as Passthrough rects;</item>
///   <item>DWM rounded corners (UiControls.WindowChrome approach);</item>
///   <item>minimize hides to tray (MainForm.OnResize :2257-2272), tray
///     double-click / Show restores (MainForm.Restore :2248);</item>
///   <item>size persistence through the existing AppSettings keys
///     WindowWidth/WindowHeight/WindowLayout, debounced, classic3 stamping
///     (MainForm.FitToScreen :3151-3177, SaveWindowSize :3179-3188);</item>
///   <item>pause hotkey registration (MainForm.RegisterPauseHotkey), with
///     <see cref="PauseHotkeyPressed"/> as the public engine hookup point
///     (engine wiring lands in P2-C).</item>
/// </list>
/// </summary>
public sealed partial class ShellWindow : Window
{
    // Classic v3 defaults (MainForm ClassicWantWidth/Height). Internal so the
    // P2-E test layer can pin them without constructing a WinUI window;
    // `const` access is compile-time inlined, so tests never touch the XAML
    // runtime just to read a number.
    internal const int DefaultWidthDip = 660;
    internal const int DefaultHeightDip = 920;
    private const string ClassicLayoutTag = "classic3";
    private const int SaveDebounceMs = 600;

    /// <summary>
    /// Single-sourced window title: "MaxDPS Companion " + the csproj version and
    /// codename (Native.DisplayVersion). The real runtime value is reported by
    /// <c>--ui-smoke-test</c> (ui-smoke.txt) and pinned by ReleaseIdentityTests.
    /// </summary>
    internal static string WindowTitle => "MaxDPS Companion " + Native.DisplayVersion;

    // Settings writes are serialized process-wide (MainForm.SettingsSaveGate
    // semantics): the shell and any P2 screen must not truncate settings.ini.
    private static readonly object SettingsSaveGate = new();

    private readonly AppSettings _settings;
    private readonly Win32Host _win32;
    private readonly TrayIcon _tray;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _saveDebounce;
    private InputNonClientPointerSource? _nonClientSource;
    private bool _hiddenToTray;
    private bool _closing;

    internal ShellWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        Title = WindowTitle;
        TitleText.Text = Title;

        var hwnd = WindowNative.GetWindowHandle(this);
        _win32 = new Win32Host(hwnd);
        // WM_HOTKEY = the global pause toggle (MainForm.WndProc :3232-3234).
        _win32.PauseHotkeyPressed += TogglePauseHotkey;
        _win32.RegisterPauseHotkey(_settings.PauseHotkey);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            // Borderless first, THEN restore resizability: IsResizable re-applies
            // WS_THICKFRAME, which the system sizing loop needs for edge resize
            // (Win32Host WM_NCHITTEST). No maximize affordance (v3 parity).
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = true;
            presenter.IsMaximizable = false;
        }

        _tray = new TrayIcon(hwnd, "MaxDPS Companion")
        {
            Visible = false, // MainForm: tray appears only when minimized
        };
        _tray.RestoreRequested += RestoreFromTray;
        // MainForm tray "Start/Stop" slot (:2224): ToggleEngine. The menu label
        // is TrayIcon's "Pause" (P1), same slot WinForms NotifyIcon used.
        _tray.PauseRequested += ToggleEngine;
        _tray.QuitRequested += CloseShell;
        _win32.MessageFilter = (msg, wParam, lParam) => _tray.HandleMessage(msg, wParam, lParam);

        _saveDebounce = DispatcherQueue.CreateTimer();
        _saveDebounce.Interval = TimeSpan.FromMilliseconds(SaveDebounceMs);
        _saveDebounce.IsRepeating = false;
        _saveDebounce.Tick += (_, _) => SaveWindowSize();

        AppWindow.Changed += OnAppWindowChanged;
        AppWindow.Closing += (_, _) =>
        {
            _calCancel = true; // MainForm.OnFormClosing :2498-2502
            // Flush any pending _settingsDebounce edit before the window dies:
            // SaveSoon coalesces Advanced-page edits and OnClosed stops the
            // timer, so without this a field changed <600 ms before close would
            // never reach settings.ini (SaveWindowSize only persists the size).
            if (_settingsDebounce is { IsRunning: true }) SaveNow();
            SaveWindowSize();
        };
        Closed += OnClosed;
        RootGrid.Loaded += (_, _) => HookTitleBarRegions();

        // P2-C: build the main screen, wire the engine and start the status pump.
        InitializeContent();
        FitToDisplay();
    }

    // ----- geometry (MainForm.FitToScreen parity) -----

    /// <summary>
    /// Restores the remembered size (honoured only under the classic3 layout
    /// tag, which is stamped + persisted on first launch with any other tag),
    /// clamps it into the display work area and centers the window
    /// (MainForm StartPosition=CenterScreen). DIP defaults and the minimum are
    /// scaled to physical pixels for the launch monitor; saved values are
    /// already physical (AppSettings semantics).
    /// </summary>
    private void FitToDisplay()
    {
        var classic = string.Equals(_settings.WindowLayout, ClassicLayoutTag, StringComparison.OrdinalIgnoreCase);
        if (!classic)
        {
            // Size migration (MainForm.FitToScreen): a remembered pre-WinUI size
            // is never re-honoured; stamp the tag through the normal save path.
            _settings.WindowLayout = ClassicLayoutTag;
            SaveSettings();
        }

        var dpi = _win32.Dpi;
        var savedSize = classic && _settings.WindowWidth > 0 && _settings.WindowHeight > 0;
        var wantWidth = savedSize ? _settings.WindowWidth : Win32Host.Scale(DefaultWidthDip, dpi);
        var wantHeight = savedSize ? _settings.WindowHeight : Win32Host.Scale(DefaultHeightDip, dpi);

        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Max(Win32Host.Scale(Win32Host.MinWidthDip, dpi), Math.Min(wantWidth, area.Width));
        var height = Math.Max(Win32Host.Scale(Win32Host.MinHeightDip, dpi), Math.Min(wantHeight, area.Height));

        AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        AppWindow.Move(new Windows.Graphics.PointInt32(
            area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2));
    }

    /// <summary>MainForm.SaveWindowSize parity (:3179-3188): normal state only, physical pixels, debounced by the caller.</summary>
    private void SaveWindowSize()
    {
        if (SuppressSizePersistence) return; // --ui-snapshot/--bench-ui synthetic sizes are not user sizes
        if (_hiddenToTray) return;
        if (AppWindow.Presenter is OverlappedPresenter presenter
            && presenter.State != OverlappedPresenterState.Restored) return;

        var size = AppWindow.Size;
        if (size.Width < Win32Host.Scale(Win32Host.MinWidthDip, _win32.Dpi) || size.Height < 200) return;
        if (_settings.WindowWidth == size.Width && _settings.WindowHeight == size.Height) return;

        _settings.WindowWidth = size.Width;
        _settings.WindowHeight = size.Height;
        SaveSettings();
    }

    private void SaveSettings()
    {
        lock (SettingsSaveGate)
        {
            try { _settings.Save(); }
            catch (IOException) { /* locked/unwritable settings.ini: the next save retries */ }
            catch (UnauthorizedAccessException) { /* read-only install: keep the session alive */ }
        }
    }

    // ----- app window events -----

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange
            && sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
        {
            HideToTray();
        }

        if (args.DidSizeChange && !_hiddenToTray && !SuppressSizePersistence
            && sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            _saveDebounce.Stop();
            _saveDebounce.Start();
        }
    }

    /// <summary>MainForm.OnResize :2257-2272: Hide + ShowInTaskbar=false + tray visible.</summary>
    private void HideToTray()
    {
        _hiddenToTray = true;
        _tray.Visible = true;
        AppWindow.Hide();
    }

    /// <summary>MainForm.Restore :2248-2255: show, normal state, tray hidden, activate.</summary>
    private void RestoreFromTray()
    {
        if (_closing) return;
        _hiddenToTray = false;
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        Activate();
        Native.SetForegroundWindow(_win32.Handle);
        _tray.Visible = false;
    }

    // ----- title bar -----

    private void HookTitleBarRegions()
    {
        if (RootGrid.XamlRoot is null) return;
        try
        {
            _nonClientSource = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        }
        catch (Exception)
        {
            return; // non-client input APIs unavailable: the rest of the shell still runs
        }
        TitleBar.SizeChanged += (_, _) => UpdateTitleBarRegions();
        RootGrid.XamlRoot.Changed += (_, _) => UpdateTitleBarRegions();
        UpdateTitleBarRegions();
    }

    /// <summary>
    /// Caption = the whole title bar (drag), Passthrough = the two buttons so
    /// they keep receiving XAML pointer input. Rects are physical pixels
    /// relative to the borderless window's top-left.
    /// </summary>
    private void UpdateTitleBarRegions()
    {
        if (_nonClientSource is null || TitleBar.ActualWidth <= 0 || TitleBar.ActualHeight <= 0) return;
        var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;

        Windows.Graphics.RectInt32 ToPhysical(Rect r) => new(
            (int)Math.Round(r.X * scale),
            (int)Math.Round(r.Y * scale),
            (int)Math.Round(r.Width * scale),
            (int)Math.Round(r.Height * scale));

        var caption = TitleBar.TransformToVisual(RootGrid)
            .TransformBounds(new Rect(0, 0, TitleBar.ActualWidth, TitleBar.ActualHeight));
        _nonClientSource.SetRegionRects(NonClientRegionKind.Caption, [ToPhysical(caption)]);

        var passthrough = new List<Windows.Graphics.RectInt32>();
        foreach (var button in new[] { MinimizeButton, CloseButton })
        {
            if (button.ActualWidth <= 0 || button.ActualHeight <= 0) continue;
            passthrough.Add(ToPhysical(button.TransformToVisual(RootGrid)
                .TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight))));
        }
        if (passthrough.Count > 0)
            _nonClientSource.SetRegionRects(NonClientRegionKind.Passthrough, passthrough.ToArray());
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.Minimize();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseShell();

    // ----- teardown -----

    private void CloseShell()
    {
        if (_closing) return;
        _closing = true;
        SaveWindowSize();
        Close();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closing = true;
        _calCancel = true;      // MainForm.OnFormClosing :2498-2502
        _saveDebounce.Stop();
        _statusPump?.Stop();    // P2-C status pump
        _settingsDebounce?.Stop(); // P2-C advanced-settings debounce
        _engine?.Dispose();     // MainForm.OnFormClosed :3252
        _win32.Dispose();
        _tray.Dispose();
        Application.Current.Exit();
    }
}
