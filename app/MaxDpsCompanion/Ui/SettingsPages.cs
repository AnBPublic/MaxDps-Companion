namespace MaxDpsCompanion;

/// <summary>
/// Configuration page (v2.7 §31): existing settings regrouped by intent.
/// Every original setting is preserved and still saves through the same path;
/// content is composed by the window that owns the controls so the write path
/// stays single-sourced.
/// </summary>
internal sealed class ConfigurationPage : StackPage
{
    protected override string HeaderTitle => "Configuration";
    protected override string HeaderSubtitle => "Automation, combat, safety, targeting, input and bridge";
}

/// <summary>
/// Diagnostics page (v2.7 §32): protocol/bridge, telemetry, patch/registry
/// audit summary, calibration tools, slots/last key/decision lines and the raw
/// readouts that used to sit under Advanced.
/// </summary>
internal sealed class DiagnosticsPage : StackPage
{
    protected override string HeaderTitle => "Diagnostics";
    protected override string HeaderSubtitle => "Protocol, telemetry, calibration and raw readouts";
}

/// <summary>
/// Solo survival-band sliders (Stream 1 spec, wired companion-side in Stream 3):
/// Minor 40-99, Major 20-90, Immunity 5-60. A pure pass-through to the existing
/// <see cref="AppSettings"/> Solo options; apply runs
/// <see cref="PolicyOptions.ValidateSoloBands"/> so an inverted ladder is
/// corrected on write (never silently saved). It only edits existing options —
/// no new setting, no wire change.
/// </summary>
internal sealed class SoloBandEditor : Panel, IUiMeasured
{
    private readonly NumericUpDown _minor = Band(40, 99);
    private readonly NumericUpDown _major = Band(20, 90);
    private readonly NumericUpDown _immunity = Band(5, 60);
    private readonly VertStack _stack = new() { Gap = DesignTokens.SpaceS };

    public int LabelWidth { get; set; } = 220;

    public SoloBandEditor()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        _stack.Controls.Add(new FieldRowPanel("Minor absorb at/below HP% (40-99)", _minor, LabelWidth));
        _stack.Controls.Add(new FieldRowPanel("Major defensive at/below HP% (20-90)", _major, LabelWidth));
        _stack.Controls.Add(new FieldRowPanel("Immunity at/below HP% (5-60)", _immunity, LabelWidth));
        Controls.Add(_stack);
    }

    /// <summary>Test/review hooks for the three sliders.</summary>
    internal (NumericUpDown Minor, NumericUpDown Major, NumericUpDown Immunity) SlidersForTest => (_minor, _major, _immunity);

    private static NumericUpDown Band(int min, int max) =>
        new WheelSafeNumeric { Minimum = min, Maximum = max, Width = 72, TextAlign = HorizontalAlignment.Right };

    /// <summary>Pushes the current settings into the sliders (pass-through read).</summary>
    public void LoadFrom(AppSettings settings)
    {
        _minor.Value = Math.Clamp(settings.SoloMinorHpPct, (int)_minor.Minimum, (int)_minor.Maximum);
        _major.Value = Math.Clamp(settings.SoloMajorHpPct, (int)_major.Minimum, (int)_major.Maximum);
        _immunity.Value = Math.Clamp(settings.SoloImmunityHpPct, (int)_immunity.Minimum, (int)_immunity.Maximum);
    }

    /// <summary>
    /// Validates the ladder, writes the corrected values back into
    /// <paramref name="settings"/> and returns the validated options tuple.
    /// </summary>
    public (int Minor, int Major, int Immunity) ApplyTo(AppSettings settings)
    {
        var (minor, major, immunity) = PolicyOptions.ValidateSoloBands((int)_minor.Value, (int)_major.Value, (int)_immunity.Value);
        // Reflect the correction in the controls so the user sees the saved state.
        _minor.Value = minor;
        _major.Value = major;
        _immunity.Value = immunity;
        settings.SoloMinorHpPct = minor;
        settings.SoloMajorHpPct = major;
        settings.SoloImmunityHpPct = immunity;
        return (minor, major, immunity);
    }

    public int MeasuredHeight(int width)
    {
        _stack.Padding = new Padding(0);
        return _stack.MeasuredHeight(width);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        _stack.Bounds = new Rectangle(0, 0, Math.Max(10, ClientSize.Width), _stack.MeasuredHeight(ClientSize.Width));
    }
}

/// <summary>
/// Companion-side crowd-control appendix toggle (v3.4.0). Default OFF: CC is an
/// explicit opt-in. A pure pass-through to <see cref="AppSettings.CrowdControlEnabled"/>;
/// it only edits the existing setting (no wire change, no new field). The
/// in-game addon toggle can only further restrict.
/// </summary>
internal sealed class CrowdControlToggle : Panel, IUiMeasured
{
    private readonly CheckBox _enabled = new()
    {
        AutoSize = true,
        BackColor = Color.Transparent,
        Text = "Allow curated crowd control on a confirmed target",
    };

    private readonly VertStack _stack = new() { Gap = DesignTokens.SpaceS };

    public int LabelWidth { get; set; } = 220;

    public CrowdControlToggle()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        _stack.Controls.Add(_enabled);
        Controls.Add(_stack);
    }

    /// <summary>Test/review hook for the checkbox.</summary>
    internal CheckBox ToggleForTest => _enabled;

    /// <summary>Pushes the current setting into the checkbox (pass-through read).</summary>
    public void LoadFrom(AppSettings settings) => _enabled.Checked = settings.CrowdControlEnabled;

    /// <summary>Writes the checkbox back into <paramref name="settings"/>.</summary>
    public bool ApplyTo(AppSettings settings)
    {
        settings.CrowdControlEnabled = _enabled.Checked;
        return _enabled.Checked;
    }

    public int MeasuredHeight(int width)
    {
        _stack.Padding = new Padding(0);
        return _stack.MeasuredHeight(width);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        _stack.Bounds = new Rectangle(0, 0, Math.Max(10, ClientSize.Width), _stack.MeasuredHeight(ClientSize.Width));
    }
}
