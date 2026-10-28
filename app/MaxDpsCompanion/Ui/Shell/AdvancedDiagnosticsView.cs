using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// P2-C Advanced "Diagnostics" tab: real content replacing the placeholder.
/// WinUI port of DiagnosticsPage (old Ui/SettingsPages.cs :20-24) filled with
/// the MainForm diagnostics cards (BuildDiagnosticsContent :1928-2015):
/// protocol/bridge live readouts + the Stream-3 semantic banner, telemetry
/// (recorder toggle + export/replay/audit load), calibration tools with the
/// colour status, timing/strip numerics, the S8 install doctor and the decoded
/// bridge strip.
///
/// Live values are value-only writes on the 250 ms tick (old RefreshStatus
/// :2917-2938); the doctor file reads are throttled by the shell exactly like
/// the old `_doctorTick >= 8` rule.
/// </summary>
internal sealed class AdvancedDiagnosticsView : UserControl
{
    private readonly BannerView _banner = new("Bridge health: unknown", StatusTone.Info);
    private readonly KvRow _protocol = new("Protocol");
    private readonly KvRow _bridgeState = new("Bridge state");
    private readonly KvRow _slots = new("Slots");
    private readonly KvRow _lastKey = new("Last key");
    private readonly KvRow _decision = new("Decision");
    private readonly KvRow _raw = new("Raw sample");
    private readonly CheckBox _telemetry;
    private readonly TextBlock _telemetryStatus = AdvancedUi.Text("-", DesignTokens.BodySize, ControlTheme.TextSecondary);
    private readonly TextBlock _colorStatus = AdvancedUi.Text("-", DesignTokens.BodySize, ControlTheme.TextSecondary);
    private readonly TextBlock _doctorValue = AdvancedUi.Text("-", DesignTokens.BodySize, ControlTheme.TextSecondary);
    private readonly NumberBox _tolerance;
    private readonly NumberBox _poll;
    private readonly NumberBox _keyHold;
    private readonly NumberBox _minKeyGap;
    private readonly NumberBox _cellSize;
    private readonly NumberBox _offsetX;
    private readonly NumberBox _offsetY;
    private readonly TextBox _bnetPath;
    private readonly CheckBox _insurance;
    private readonly NumberBox _insuranceAge;
    private readonly CheckBox _landingAuto;
    private readonly CheckBox _adaptivePolling;
    private readonly CheckBox _openerRelease;
    private readonly ChamferButton _calibrateColors;
    private readonly ChamferButton _calibrateAdv;
    private readonly CastAuditView _castAudit = new();
    private readonly WhyNotFiringPanel _whyNotFiring = new();
    private bool _loading;

    public StripView Strip { get; } = new();

    /// <summary>Raised by every interactive change; the shell runs SaveNow.</summary>
    public event Action? Changed;

    /// <summary>Telemetry checkbox moved; the shell re-syncs the recorder + saves.</summary>
    public event Action? TelemetryChanged;

    public event Action? CalibrateRequested;
    public event Action? ResetColorsRequested;
    public event Action? FindStripRequested;
    public event Action? ExportRequested;
    public event Action? ReplayRequested;
    public event Action? LoadAuditRequested;
    public event Action? BrowseRequested;
    public event Action? LaunchRequested;
    public event Action? OpenFolderRequested;
    public event Action? DoctorRefreshRequested;

    public AdvancedDiagnosticsView()
    {
        _telemetry = AdvancedUi.Check("Record rotation telemetry (in memory, off by default)",
            "Bounded in-memory JSONL ring: decoded keybinds, state flags and timing only. No network, no Blizzard values.");
        _tolerance = AdvancedUi.Number(64, 16, 128, "Colour tolerance");
        _poll = AdvancedUi.Number(200, 10, 1000, "Poll interval in milliseconds");
        _keyHold = AdvancedUi.Number(30, 0, 200, "Key hold in milliseconds");
        _minKeyGap = AdvancedUi.Number(120, 20, 5000, "Minimum key gap in milliseconds");
        _cellSize = AdvancedUi.Number(12, 2, 64, "Cell size in pixels");
        _offsetX = AdvancedUi.Number(0, -4000, 4000, "Block offset X");
        _offsetY = AdvancedUi.Number(0, -4000, 4000, "Block offset Y");
        _bnetPath = AdvancedUi.Field("", "Battle.net path");

        // 4.6.0 "Cadence" timing (all default OFF = 4.5.8 behaviour).
        _insurance = AdvancedUi.Check("Interrupt insurance (press the bound CC when no kick is available)",
            "Uses the addon's Ext5 CC entry on a confirmed interruptible cast when no interrupt candidate exists or the last kick failed. Never bypasses a CC veto; default off.");
        _insuranceAge = AdvancedUi.Number(1000, 250, 3000, "Insurance minimum cast age in milliseconds (250-3000)");
        _landingAuto = AdvancedUi.Check("Auto-land on the GCD (fire just before the predicted GCD end)",
            "Off = every stroke is paced by the minimum key interval only (4.5.8 behaviour). Auto = pre-arm the head candidate and fire it once per GCD, using the Ext5 GCD bucket or the estimator.");
        _adaptivePolling = AdvancedUi.Check("Adaptive polling near the GCD edge (16 ms)",
            "Polls at 16 ms inside the predicted GCD edge window and at the configured Poll interval elsewhere. The duty-cycle guard still caps total work.");
        _openerRelease = AdvancedUi.Check("Release held openers on a boss pull (worldboss / elite-skull)",
            "On a rank 6 (worldboss) or rank 7 (elite-skull) pull the out-of-combat opener hold is released for 2.5 s. Trash, elite and unknown ranks keep today's behaviour.");

        _calibrateColors = AdvancedUi.Button("Calibrate colors", ButtonRole.Ghost, () => CalibrateRequested?.Invoke());
        _calibrateAdv = AdvancedUi.Button("Recalibrate", ButtonRole.Ghost, () => CalibrateRequested?.Invoke());
        var reset = AdvancedUi.Button("Reset", ButtonRole.Ghost, () => ResetColorsRequested?.Invoke());
        var findStrip = AdvancedUi.Button("Find strip", ButtonRole.Ghost, () => FindStripRequested?.Invoke());
        var export = AdvancedUi.Button("Export\u2026", ButtonRole.Ghost, () => ExportRequested?.Invoke());
        var replay = AdvancedUi.Button("Replay\u2026", ButtonRole.Ghost, () => ReplayRequested?.Invoke());
        var loadAudit = AdvancedUi.Button("Load audit from export\u2026", ButtonRole.Ghost, () => LoadAuditRequested?.Invoke());
        var browse = AdvancedUi.Button("Browse", ButtonRole.Ghost, () => BrowseRequested?.Invoke());
        var launch = AdvancedUi.Button("Launch Game", ButtonRole.Ghost, () => LaunchRequested?.Invoke());
        var openFolder = AdvancedUi.Button("Open Folder", ButtonRole.Ghost, () => OpenFolderRequested?.Invoke());
        var doctorRefresh = AdvancedUi.Button("Run install doctor", ButtonRole.Ghost, () => DoctorRefreshRequested?.Invoke());

        var buildIdentity = AdvancedUi.Text($"Build {Native.BuildVersion}", DesignTokens.MetaSize, ControlTheme.TextMuted);

        var content = new VertStack { Gap = 12 };
        content.Children.Add(AdvancedUi.Card("Protocol / bridge", "Live",
            _banner,
            _protocol,
            _bridgeState,
            _slots,
            _lastKey,
            _decision,
            _raw));
        content.Children.Add(AdvancedUi.Card("Telemetry", "Recording",
            _telemetry,
            AdvancedUi.HintTip("In-memory timing log only. No network, no Blizzard values.",
                "Bounded in-memory JSONL ring: decoded keybinds, state flags and timing only. No network, no Blizzard values."),
            AdvancedUi.ButtonRow(export, replay, loadAudit),
            _telemetryStatus,
            _castAudit));
        content.Children.Add(AdvancedUi.Card("Calibration", "Strip",
            AdvancedUi.ButtonRow(_calibrateColors, reset),
            AdvancedUi.ButtonRow(findStrip, _calibrateAdv),
            AdvancedUi.Row("Tolerance", _tolerance),
            _colorStatus));
        content.Children.Add(AdvancedUi.Card("Timing", "Cadence",
            AdvancedUi.Row("Poll (ms)", _poll),
            AdvancedUi.Row("Key hold (ms)", _keyHold),
            AdvancedUi.Row("Min key gap (ms)", _minKeyGap)));
        content.Children.Add(AdvancedUi.Card("Strip", "Pixels",
            AdvancedUi.Row("Cell size (px)", _cellSize),
            AdvancedUi.Row("Block offset X", _offsetX),
            AdvancedUi.Row("Block offset Y", _offsetY)));
        // 4.6.0 "Cadence": insurance + GCD landing + opener release. One row per
        // settings key ([Interrupt] InsuranceEnabled/InsuranceMinCastAgeMs,
        // [Scheduler] Landing/AdaptivePolling/OpenerRelease); the numeric one is
        // a labelled NumberBox row, the same pattern as the Timing card.
        content.Children.Add(AdvancedUi.Card("Cadence", "4.6",
            _insurance,
            AdvancedUi.Row("Min cast age (ms)", _insuranceAge),
            _landingAuto,
            _adaptivePolling,
            _openerRelease,
            AdvancedUi.HintTip("All off by default: 4.5.8 behaviour is unchanged.",
                "Insurance presses the addon's own bound CC on an interruptible cast when no kick is available; Auto landing fires the head candidate once per GCD just before its predicted end; adaptive polling refines the tick near that edge; opener release only fires on worldboss/elite-skull pulls.")));
        // S8 diagnostics: per-toggle why-not-firing (scheduler verdict + toggle
        // state + candidate staleness), old BuildDiagnosticsContent :1976-1979.
        content.Children.Add(AdvancedUi.Card("Why not firing", "Explain", _whyNotFiring));
        content.Children.Add(AdvancedUi.Card("Install doctor", "Version / wiring",
            AdvancedUi.ButtonRow(doctorRefresh),
            _doctorValue,
            buildIdentity,
            AdvancedUi.HintTip("Checks build, cell size, toggle mask and addon version.",
                "Checks the exe build vs HEAD, configured vs emitted cell size, the app mask vs the Ext3 mirror and the addon version.")));
        content.Children.Add(AdvancedUi.Card("Bridge strip", "Live", Strip));
        content.Children.Add(AdvancedUi.Card("Patch / registry audit", "Provenance",
            AdvancedUi.Hint("Coverage is computed from AbilityCatalog.Default via AbilityCoverage.Build."),
            AdvancedUi.Text("-", DesignTokens.BodySize, ControlTheme.TextSecondary)));
        content.Children.Add(AdvancedUi.Card("Battle.net / tools", "Launch",
            AdvancedUi.Row("BNet path", _bnetPath),
            AdvancedUi.ButtonRow(browse, launch, openFolder),
            AdvancedUi.HintTip("Opens Battle.net for WoW. No credentials stored.",
                "Launch Game opens Battle.net for WoW. No credentials are stored - the launcher's remembered account is used.")));

        var host = new ThemedScrollHost();
        AdvancedUi.Scroll(content, host);
        Content = host;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(this, "Diagnostics page");
        AutomationProperties.SetName(_telemetryStatus, "Telemetry status");
        AutomationProperties.SetName(_colorStatus, "Colour calibration status");
        AutomationProperties.SetName(_doctorValue, "Install doctor summary");

        WireChanges();
    }

    internal NumberBox ToleranceForTest => _tolerance;

    public void LoadFrom(AppSettings settings)
    {
        _loading = true;
        try
        {
            _telemetry.IsChecked = settings.TelemetryEnabled;
            _tolerance.Value = Math.Clamp(settings.Color.Tolerance, 16, 128);
            _poll.Value = settings.PollIntervalMs;
            _keyHold.Value = settings.KeyPressMs;
            _minKeyGap.Value = settings.MinKeyIntervalMs;
            _cellSize.Value = settings.CellSize;
            _offsetX.Value = settings.OffsetX;
            _offsetY.Value = settings.OffsetY;
            _bnetPath.Text = settings.BNetPath;
            _insurance.IsChecked = settings.InsuranceEnabled;
            _insuranceAge.Value = Math.Clamp(settings.InsuranceMinCastAgeMs, 250, 3000);
            _landingAuto.IsChecked = settings.Landing == LandingMode.Auto;
            _adaptivePolling.IsChecked = settings.SchedulerAdaptivePolling;
            _openerRelease.IsChecked = settings.SchedulerOpenerRelease;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Writes the page's fields back (old ApplyToSettings timing/strip/tolerance/bnet block).</summary>
    public void ApplyTo(AppSettings settings)
    {
        settings.TelemetryEnabled = _telemetry.IsChecked == true;
        settings.PollIntervalMs = (int)NumberOr(_poll, settings.PollIntervalMs);
        settings.KeyPressMs = (int)NumberOr(_keyHold, settings.KeyPressMs);
        settings.MinKeyIntervalMs = (int)NumberOr(_minKeyGap, settings.MinKeyIntervalMs);
        settings.CellSize = (int)NumberOr(_cellSize, settings.CellSize);
        settings.OffsetX = (int)NumberOr(_offsetX, settings.OffsetX);
        settings.OffsetY = (int)NumberOr(_offsetY, settings.OffsetY);
        // 4.1 A2.10: copy-on-write — a new profile instance replaces the
        // reference; the engine's per-tick snapshot never sees a field change.
        settings.Color = settings.Color.WithTolerance((int)NumberOr(_tolerance, settings.Color.Tolerance));
        settings.BNetPath = _bnetPath.Text.Trim();
        settings.InsuranceEnabled = _insurance.IsChecked == true;
        settings.InsuranceMinCastAgeMs =
            Math.Clamp((int)NumberOr(_insuranceAge, settings.InsuranceMinCastAgeMs), 250, 3000);
        settings.Landing = _landingAuto.IsChecked == true ? LandingMode.Auto : LandingMode.Off;
        settings.SchedulerAdaptivePolling = _adaptivePolling.IsChecked == true;
        settings.SchedulerOpenerRelease = _openerRelease.IsChecked == true;
    }

    /// <summary>Live protocol/bridge raw readouts (old RefreshStatus :2917-2927).</summary>
    public void UpdateLive(EngineStatus status, bool running)
    {
        _protocol.Value = $"v{PixelProtocol.SupportedVersion} (supported)";
        _bridgeState.Value = running ? status.State.ToString() : "stopped";
        _slots.Value = string.IsNullOrEmpty(status.SlotSummary) ? "-" : status.SlotSummary;
        _lastKey.Value = string.IsNullOrEmpty(status.LastKeySent) ? "-" : status.LastKeySent;
        _decision.Value = string.IsNullOrEmpty(status.Decision) ? "-" : status.Decision;
        _raw.Value = string.IsNullOrEmpty(status.RawSample) ? "-" : status.RawSample;
        Strip.Sample = status.RawSample ?? "";
    }

    public void SetBanner(string title, StatusTone tone, string detail)
    {
        if (_banner.Title != title) _banner.Title = title;
        _banner.Tone = tone;
        if (!string.Equals(_banner.Detail, detail, StringComparison.Ordinal)) _banner.Detail = detail;
    }

    public void SetTelemetryStatus(string text)
    {
        if (_telemetryStatus.Text != text) _telemetryStatus.Text = text;
    }

    public void SetTelemetryEnabled(bool enabled) => _telemetry.IsChecked = enabled;

    public void SetColorStatus(string text)
    {
        if (_colorStatus.Text != text) _colorStatus.Text = text;
    }

    public void SetColorTolerance(int value) =>
        _tolerance.Value = Math.Clamp(value, 16, 128);

    /// <summary>Old ApplyLocation :2883-2885: keep the strip editors in step with a located block.</summary>
    public void SetLocationValues(int offsetX, int offsetY, int cellSize)
    {
        _offsetX.Value = offsetX;
        _offsetY.Value = offsetY;
        _cellSize.Value = cellSize;
    }

    /// <summary>Old SetCalibrating :2316-2329 button copy swap.</summary>
    public void SetCalibrating(bool running)
    {
        _calibrateColors.Content = running ? "Cancel calibration" : "Calibrate colors";
        _calibrateAdv.Content = running ? "Cancel" : "Recalibrate";
    }

    public void SetDoctorText(string text, bool ok)
    {
        _doctorValue.Text = text;
        _doctorValue.Foreground = ControlTheme.Brush(ok ? ControlTheme.Success : ControlTheme.Warning);
    }

    /// <summary>S8: push one why-not-firing snapshot (old UpdateWhyNotFiring :2021-2042).</summary>
    public void SetWhyNotFiring(in WhyNotFiringFacts facts) => _whyNotFiring.Update(facts);

    /// <summary>Read-only suggested-vs-cast grid fed by a loaded export (old LoadCastAudit :2812-2832).</summary>
    public void SetCastAudit(CastAuditReport report) => _castAudit.Load(report);

    private static double NumberOr(NumberBox box, double fallback) =>
        double.IsNaN(box.Value) ? fallback : box.Value;

    private void WireChanges()
    {
        void On() { if (!_loading) Changed?.Invoke(); }

        _telemetry.Checked += (_, _) => { if (!_loading) TelemetryChanged?.Invoke(); };
        _telemetry.Unchecked += (_, _) => { if (!_loading) TelemetryChanged?.Invoke(); };
        _tolerance.ValueChanged += (_, _) => On();
        _poll.ValueChanged += (_, _) => On();
        _keyHold.ValueChanged += (_, _) => On();
        _minKeyGap.ValueChanged += (_, _) => On();
        _cellSize.ValueChanged += (_, _) => On();
        _offsetX.ValueChanged += (_, _) => On();
        _offsetY.ValueChanged += (_, _) => On();
        _bnetPath.TextChanged += (_, _) => On();
        _insurance.Checked += (_, _) => On();
        _insurance.Unchecked += (_, _) => On();
        _insuranceAge.ValueChanged += (_, _) => On();
        _landingAuto.Checked += (_, _) => On();
        _landingAuto.Unchecked += (_, _) => On();
        _adaptivePolling.Checked += (_, _) => On();
        _adaptivePolling.Unchecked += (_, _) => On();
        _openerRelease.Checked += (_, _) => On();
        _openerRelease.Unchecked += (_, _) => On();
    }
}
