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
