using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// P2-E port of the S7 Console-home tests (docs/plans/2026-10-11-winui3-v4-migration.md §6).
///
/// FINAL FATES (4 original tests):
///  * REINSTATED (3): Presets_Are_Five_Named_Bundles and
///    Console_Role_Is_Inferred_From_Spec assert the frozen pure models
///    (ConsolePresets/ConsoleRole); MainForm_Console_Home_Defaults_On_And_
///    Preset_Moves_Toggles became Console_Preset_Bundles_Move_The_Same_
///    Toggle_Model (bundle data -> ToggleModel -> settings; the shell applies
///    exactly this path in ShellWindow.ApplyPreset).
///  * DELETED (1): ConsoleHome_Spec_Role_And_Logs_Update (ConsoleHome control
///    internals: chips/update-log/LastChange) — ConsoleRole is pinned by the
///    test above, the log copy is UI-only and reviewed via --ui-snapshot-page.
/// </summary>
public class ConsoleHomeTests
{
    [Fact]
    public void Presets_Are_Five_Named_Bundles()
    {
        var names = ConsolePresets.All.Select(b => b.Name).ToArray();
        Assert.Equal(new[] { "Solo", "Levelling", "Dungeon", "Raid", "Tank" }, names);
        foreach (var bundle in ConsolePresets.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(bundle.Summary));
            Assert.DoesNotContain("undetect", bundle.Summary, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Console_Role_Is_Inferred_From_Spec()
    {
        Assert.Equal("TANK", ConsoleRole.ForSpec("Warrior", "Protection").Role);
        Assert.Equal("HEALER", ConsoleRole.ForSpec("Priest", "Discipline").Role);
        Assert.Equal("DPS", ConsoleRole.ForSpec("Mage", "Frost").Role);
        Assert.Equal("AUTO", ConsoleRole.ForSpec(null, null).Role);
    }

    /// <summary>
    /// Old MainForm_Console_Home_Defaults_On_And_Preset_Moves_Toggles: the Raid
    /// preset leaves Solo off, the Solo preset turns it on, and the bundle is
    /// applied to the SAME 14-switch model the user can flip by hand.
    /// </summary>
    [Fact]
    public void Console_Preset_Bundles_Move_The_Same_Toggle_Model()
    {
        var raid = ConsolePresets.Get(ConsolePreset.Raid);
        var solo = ConsolePresets.Get(ConsolePreset.Solo);
        Assert.False(raid.Solo, "Raid preset leaves Solo off");
        Assert.True(solo.Solo, "Solo preset turns Solo on");

        // ShellWindow.ApplyPreset: bundle fields -> ToggleModel -> settings.
        var settings = new AppSettings();
        var model = ToggleModel.FromSettings(settings);
        model.Solo = solo.Solo;
        model.TimeToKill = solo.TimeToKill;
        model.CrowdControl = solo.CrowdControl;
        model.ApplyToSettings(settings);

        Assert.True(settings.SoloEnabled);
        Assert.True(settings.TimeToKillEnabled);
        Assert.False(settings.CrowdControlEnabled);
    }
}
