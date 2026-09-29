using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// [Intelligence] and [Solo] settings: intelligence is ON by default (v2.0
/// product behaviour), solo is OFF by default; both parse and round-trip
/// through Save.
/// </summary>
public class AppSettingsTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"mdb-settings-{Guid.NewGuid():N}.ini");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Intelligence_Defaults_To_Enabled_And_Solo_To_Disabled()
    {
        var settings = AppSettings.Load(_path);

        Assert.True(settings.IntelligenceEnabled);
        Assert.Equal(1500, settings.IntelligenceStaleAfterMs);
        Assert.False(settings.SoloEnabled);
        Assert.Equal(35, settings.SoloEmergencyHpPct);
        Assert.Equal(65, settings.SoloSelfSustainHpPct);
        Assert.Equal(60, settings.SoloDefensiveEscalateHpPct);
        Assert.Equal(8, settings.SlotEnabled.Length);
    }

    [Fact]
    public void Load_Parses_Intelligence_Section()
    {
        File.WriteAllText(_path, "[Intelligence]\nEnabled=1\nStaleAfterMs=2000\n");

        var settings = AppSettings.Load(_path);

        Assert.True(settings.IntelligenceEnabled);
        Assert.Equal(2000, settings.IntelligenceStaleAfterMs);
    }

    [Fact]
    public void Invalid_Values_Keep_Defaults()
    {
        File.WriteAllText(_path, "[Intelligence]\nEnabled=maybe\nStaleAfterMs=abc\n");

        var settings = AppSettings.Load(_path);

        Assert.True(settings.IntelligenceEnabled);
        Assert.Equal(1500, settings.IntelligenceStaleAfterMs);
    }

    [Fact]
    public void Save_Round_Trips_Intelligence()
    {
        var settings = AppSettings.Load(_path);
        settings.IntelligenceEnabled = true;
        settings.IntelligenceStaleAfterMs = 900;
        settings.Save();

        var reloaded = AppSettings.Load(_path);

        Assert.True(reloaded.IntelligenceEnabled);
        Assert.Equal(900, reloaded.IntelligenceStaleAfterMs);
    }

    [Fact]
    public void Save_Round_Trips_Solo_And_Slot_Toggles()
    {
        var settings = AppSettings.Load(_path);
        settings.SoloEnabled = true;
        settings.SoloEmergencyHpPct = 30;
        settings.SoloSelfSustainHpPct = 70;
        settings.SoloDefensiveEscalateHpPct = 55;
        settings.SlotEnabled[6] = false;   // Mobility
        settings.SlotEnabled[7] = false;   // SelfHeal
        settings.Save();

        var reloaded = AppSettings.Load(_path);

        Assert.True(reloaded.SoloEnabled);
        Assert.Equal(30, reloaded.SoloEmergencyHpPct);
        Assert.Equal(70, reloaded.SoloSelfSustainHpPct);
        Assert.Equal(55, reloaded.SoloDefensiveEscalateHpPct);
        Assert.False(reloaded.SlotEnabled[6]);
        Assert.False(reloaded.SlotEnabled[7]);
        Assert.True(reloaded.SlotEnabled[0]);
    }

    [Fact]
    public void Solo_Thresholds_Are_Clamped()
    {
        File.WriteAllText(_path, "[Solo]\nEnabled=1\nEmergencyHpPct=1\nSelfSustainHpPct=200\nDefensiveEscalateHpPct=0\n");

        var settings = AppSettings.Load(_path);

        Assert.True(settings.SoloEnabled);
        Assert.Equal(5, settings.SoloEmergencyHpPct);
        Assert.Equal(99, settings.SoloSelfSustainHpPct);
        Assert.Equal(5, settings.SoloDefensiveEscalateHpPct);
    }

    // ----- [Window] Layout (v3 classic shell) ------------------------------

    [Fact]
    public void WindowLayout_Defaults_To_Empty()
    {
        var settings = AppSettings.Load(_path);

        Assert.Equal("", settings.WindowLayout);
    }

    [Fact]
    public void Save_Round_Trips_WindowLayout()
    {
        var settings = AppSettings.Load(_path);
        settings.WindowLayout = "classic3";
        settings.Save();

        var reloaded = AppSettings.Load(_path);

        Assert.Equal("classic3", reloaded.WindowLayout);
    }

    // ----- [Intelligence] HpCurve (v3.0.0) ---------------------------------

    [Fact]
    public void HpCurve_Defaults_To_True()
    {
        var settings = AppSettings.Load(_path);

        Assert.True(settings.HpCurve);
    }

    [Fact]
    public void Save_Round_Trips_HpCurve_Disabled()
    {
        var settings = AppSettings.Load(_path);
        settings.HpCurve = false;
        settings.Save();

        var reloaded = AppSettings.Load(_path);

        Assert.False(reloaded.HpCurve);
    }

    // ----- [Telemetry] (v1.5.0): OFF by default is the safety contract -----

    [Fact]
    public void Telemetry_Defaults_To_Disabled()
    {
        var settings = AppSettings.Load(_path);

        Assert.False(settings.TelemetryEnabled);
        Assert.Equal(10_000, settings.TelemetryCapacity);
    }

    [Fact]
    public void Load_Parses_Telemetry_Section()
    {
        File.WriteAllText(_path, "[Telemetry]\nEnabled=1\nCapacity=20000\n");

        var settings = AppSettings.Load(_path);

        Assert.True(settings.TelemetryEnabled);
        Assert.Equal(20_000, settings.TelemetryCapacity);
    }

    [Fact]
    public void Save_Round_Trips_Telemetry()
    {
        var settings = AppSettings.Load(_path);
        settings.TelemetryEnabled = true;
        settings.TelemetryCapacity = 4096;
        settings.Save();

        var reloaded = AppSettings.Load(_path);

        Assert.True(reloaded.TelemetryEnabled);
        Assert.Equal(4096, reloaded.TelemetryCapacity);
    }

    [Fact]
    public void Telemetry_Capacity_Is_Clamped_To_A_Sane_Ring()
    {
        File.WriteAllText(_path, "[Telemetry]\nCapacity=1\n");

        var settings = AppSettings.Load(_path);

        Assert.Equal(64, settings.TelemetryCapacity);
    }

    // ----- [Abilities] (v2.3.0): per-ability automatic-use overrides -------

    [Fact]
    public void Load_Parses_Abilities_Overrides_Tolerating_Noise()
    {
        File.WriteAllText(_path,
            "[Abilities]\n" +
            "On= 1966, 871 , 871, notanumber, -5, 0, 999999\n" +
            "Off=1856, 1856, , garbage\n");

        var settings = AppSettings.Load(_path);

        Assert.True(settings.Abilities.HasOverrides);
        // Whitespace, duplicates, non-numeric, non-positive and unknown ids are
        // all tolerated: only the distinct positive ids survive.
        Assert.Equal(new[] { 871, 1966, 999999 }, settings.Abilities.ExplicitOn.OrderBy(id => id));
        Assert.Equal(new[] { 1856 }, settings.Abilities.ExplicitOff.OrderBy(id => id));
        Assert.Null(settings.Abilities.OverrideOf(12345));
    }

    [Fact]
    public void Save_Round_Trips_Abilities_Overrides_Sorted()
    {
        var settings = AppSettings.Load(_path);
        settings.Abilities = AbilityPolicy.FromIds("1966,871", "31224,1856");
        settings.Save();

        var reloaded = AppSettings.Load(_path);

        Assert.Equal("871,1966", reloaded.Abilities.EncodeOn());
        Assert.Equal("1856,31224", reloaded.Abilities.EncodeOff());
        Assert.True(reloaded.Abilities.OverrideOf(1966));
        Assert.False(reloaded.Abilities.OverrideOf(1856));
    }

    [Fact]
    public void AbilityPolicy_With_Clears_An_Override_At_Its_Default()
    {
        var catalog = AbilityCatalog.Default;
        var vanish = catalog.TryGet(1856)!;   // manual by design (NeverAutomatic)
        var feint = catalog.TryGet(1966)!;    // automatic by curated default
        Assert.True(vanish.NeverAutomatic);
        Assert.False(feint.NeverAutomatic);

        // A default-OFF ability explicitly turned ON, then toggled back OFF:
        // the override is cleared and IsEnabled follows the curated default.
        var enabled = AbilityPolicy.Default.With(1856, enabled: true, defaultEnabled: false);
        Assert.True(enabled.IsEnabled(vanish));
        Assert.Equal(true, enabled.OverrideOf(1856));

        var restoredVanish = enabled.With(1856, enabled: false, defaultEnabled: false);
        Assert.Null(restoredVanish.OverrideOf(1856));
        Assert.False(restoredVanish.IsEnabled(vanish));
        Assert.False(restoredVanish.HasOverrides);

        // A default-ON ability explicitly turned OFF, then toggled back ON.
        var disabled = AbilityPolicy.Default.With(1966, enabled: false, defaultEnabled: true);
        Assert.False(disabled.IsEnabled(feint));
        Assert.Equal(false, disabled.OverrideOf(1966));

        var restoredFeint = disabled.With(1966, enabled: true, defaultEnabled: true);
        Assert.Null(restoredFeint.OverrideOf(1966));
        Assert.True(restoredFeint.IsEnabled(feint));
        Assert.False(restoredFeint.HasOverrides);
    }

    [Fact]
    public void Toggling_Back_To_Default_Removes_The_Override()
    {
        var policy = AbilityPolicy.Default.With(871, enabled: false, defaultEnabled: true);
        Assert.True(policy.HasOverrides);
        Assert.Single(policy.ExplicitOff);
        Assert.Contains(871, policy.ExplicitOff);

        var toggled = policy.With(871, enabled: true, defaultEnabled: true);

        Assert.False(toggled.HasOverrides);
        Assert.Empty(toggled.ExplicitOff);
        Assert.Null(toggled.OverrideOf(871));
    }
}
