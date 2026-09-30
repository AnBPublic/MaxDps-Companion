using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// S7 Console-home tests. They pin the named preset bundles, the inferred
/// role badge, the rolling/update logs, and that MainForm applies a preset by
/// moving the SAME hero toggles the user can move by hand. Honesty: no copy
/// anywhere may claim the companion is hidden or undetectable.
/// </summary>
public class ConsoleHomeTests
{
    private static (T? Result, Exception? Error) RunOnSta<T>(Func<T> action)
    {
        T? result = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "STA UI thread timed out");
        return (result, error);
    }

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

    [Fact]
    public void ConsoleHome_Spec_Role_And_Logs_Update()
    {
        var (result, error) = RunOnSta(() =>
        {
            var home = new ConsoleHome();
            home.SetSpec("Mage", "Frost");
            var dsp = (home.RoleForTest, home.SpecTitleForTest, home.UpdateLog.Lines.Count);
            home.SetSpec("Warrior", "Protection");
            var tank = home.RoleForTest;
            home.SetActivePreset(ConsolePreset.Solo);
            var active = home.PresetChips.Where(c => c.Active).Select(c => c.Preset).ToArray();
            var lastChange = home.LastChangeForTest;
            return (dsp, tank, active, lastChange);
        });

        Assert.Null(error);
        Assert.Equal("DPS", result.dsp.Item1);
        Assert.Contains("Frost", result.dsp.Item2);
        Assert.True(result.dsp.Item3 > 0);
        Assert.Equal("TANK", result.tank);
        Assert.Equal(new[] { ConsolePreset.Solo }, result.active);
        Assert.Contains("Spec detected", result.lastChange);
    }

    [Fact]
    public void MainForm_Console_Home_Defaults_On_And_Preset_Moves_Toggles()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.ShowInTaskbar = false;
            form.Show();
            System.Windows.Forms.Application.DoEvents();
            var defaultVisible = form.ConsoleVisibleForTest;
            form.ApplyPresetForTest(ConsolePreset.Raid);
            var raidSolo = form.SoloToggleForTest.Checked;
            form.ApplyPresetForTest(ConsolePreset.Solo);
            var soloSolo = form.SoloToggleForTest.Checked;
            var log = form.ConsoleForTest.LastChangeForTest;
            form.ToggleConsoleForTest();
            var toggled = form.ConsoleVisibleForTest;
            return (defaultVisible, raidSolo, soloSolo, log, toggled);
        });

        Assert.Null(error);
        Assert.True(result.defaultVisible, "the console home is the default view");
        Assert.False(result.raidSolo, "Raid preset leaves Solo off");
        Assert.True(result.soloSolo, "Solo preset turns Solo on");
        Assert.Contains("Preset", result.log);
        Assert.False(result.toggled, "the Console nav item hides the console");
    }
}
