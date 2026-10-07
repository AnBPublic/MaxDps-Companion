using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Exp (in-game-config) shell opt-in (spec
/// docs/plans/2026-10-03-ingame-config.md §1/§14): the settings.ini key
/// InGameConfigMode=0/1, the --exp CLI override, and the contract that the Exp
/// layout mounts zero toggle controls (the in-game overlay owns toggles).
/// </summary>
public class ExpModeTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"mdb-exp-{Guid.NewGuid():N}.ini");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void InGameConfigMode_Defaults_To_False()
    {
        var settings = AppSettings.Load(_path);

        Assert.False(settings.InGameConfigMode);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("1", true)]
    public void InGameConfigMode_Is_Parsed_From_Ini(string value, bool expected)
    {
        File.WriteAllText(_path, $"[Window]\nInGameConfigMode={value}\n");

        var settings = AppSettings.Load(_path);

        Assert.Equal(expected, settings.InGameConfigMode);
    }

    [Fact]
    public void Exp_Argument_Forces_Mode_On_Regardless_Of_Ini()
    {
        File.WriteAllText(_path, "[Window]\nInGameConfigMode=0\n");
        var settings = AppSettings.Load(_path);
        Assert.False(settings.InGameConfigMode);

        Program.ApplyExpFlag(settings, new[] { "--exp" });

        Assert.True(settings.InGameConfigMode);
    }

    [Fact]
    public void Exp_Argument_Absent_Leaves_Ini_Value_Untouched()
    {
        File.WriteAllText(_path, "[Window]\nInGameConfigMode=1\n");
        var settings = AppSettings.Load(_path);

        Program.ApplyExpFlag(settings, new[] { "--probe" });

        Assert.True(settings.InGameConfigMode);
    }

    [Fact]
    public void Save_Round_Trips_InGameConfigMode()
    {
        var settings = AppSettings.Load(_path);
        settings.InGameConfigMode = true;
        settings.Save();

        Assert.True(AppSettings.Load(_path).InGameConfigMode);
    }

    [Fact]
    public void Exp_Layout_Contains_Zero_Toggle_Controls()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings { InGameConfigMode = true };
            using var form = new MainForm(settings);
            form.CreateControl();
            // Exercise the popup entry points: in Exp mode they must neither
            // build nor show the toggle-bearing Advanced/Abilities content.
            // Count the WHOLE form tree (popups included), not just the body,
            // so a leaked popup toggle fails the contract.
            form.OpenAdvancedForTest();
            form.OpenAbilitiesForTest();
            return (Toggles: CountToggles(form), PopupVisible: form.AnyPopupVisibleForTest);
        });

        Assert.Null(error);
        Assert.Equal(0, result.Toggles);
        Assert.False(result.PopupVisible);
    }

    [Fact]
    public void Exp_Layout_Is_Minimal_Single_Log_And_One_Status_Pill()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings { InGameConfigMode = true };
            using var form = new MainForm(settings);
            form.CreateControl();
            // Popup entry points must stay inert and must not construct the
            // toggle-bearing popups at all in Exp.
            form.OpenAdvancedForTest();
            form.OpenAbilitiesForTest();
            return (
                Toggles: CountToggles(form),
                PopupVisible: form.AnyPopupVisibleForTest,
                Logs: CountType<RollingLog>(form),
                StatusPills: CountAccessibleName(form, "Exp status pill"),
                AdvancedBuilt: form.AdvancedTabsForTest is not null,
                AbilitiesBuilt: form.AbilitiesTabsForTest is not null);
        });

        Assert.Null(error);
        Assert.Equal(0, result.Toggles);
        Assert.False(result.PopupVisible);
        // ONE log: the rolling log only, no History duplicate feed.
        Assert.Equal(1, result.Logs);
        // The five RICE pills collapse to a single status pill.
        Assert.Equal(1, result.StatusPills);
        // Exp builds no popups (guards :ShowAdvanced/:ShowAbilities).
        Assert.False(result.AdvancedBuilt, "Exp must not construct the Advanced popup");
        Assert.False(result.AbilitiesBuilt, "Exp must not construct the Abilities popup");
    }

    [Fact]
    public void Exp_Button_Row_Has_Wired_OpenGame_Last()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings { InGameConfigMode = true };
            using var form = new MainForm(settings);
            form.CreateControl();
            var launchCalled = false;
            form.Launcher = _ => { launchCalled = true; return (true, "stub-open-game"); };
            var button = form.ExpLaunchGameForTest;
            var label = button.Text;
            // The form is never shown, so raise Click through the protected
            // OnClick (PerformClick requires CanSelect, i.e. a visible tree).
            typeof(Control)
                .GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(button, new object[] { EventArgs.Empty });
            return (Label: label, LaunchCalled: launchCalled, Status: form.StatusLineForTest);
        });

        Assert.Null(error);
        // The full label survives: no ellipsis, trailing "OPEN GAME" intact.
        Assert.EndsWith("OPEN GAME", result.Label);
        Assert.DoesNotContain("\u2026", result.Label);
        Assert.True(result.LaunchCalled, "OPEN GAME must be wired to the launcher");
        Assert.Contains("stub-open-game", result.Status);
    }

    [Fact]
    public void Exp_Status_Pill_Shows_State_And_Fps_Only()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings { InGameConfigMode = true };
            using var form = new MainForm(settings);
            form.CreateControl();
            // The status timer path is the only writer; run one tick.
            form.RefreshStatusForTest();
            return (Pill: form.ExpStatusPillForTest.Text, Details: form.ExpDetailsForTest,
                Expanded: form.ExpDetailsExpandedForTest);
        });

        Assert.Null(error);
        // State + fps: filled/hollow dot, one of the four states, rate suffix.
        Assert.Matches(@"^(\u25CF|\u25CB) (Running|Stopped|Paused|Calibrating)", result.Pill);
        Assert.Contains("fps", result.Pill);
        // The mask witness is NEVER in the pill...
        Assert.DoesNotContain("mask", result.Pill, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0x", result.Pill, StringComparison.OrdinalIgnoreCase);
        // ...it lives in the status card's collapsed Details disclosure.
        Assert.False(result.Expanded, "Details must be collapsed by default");
        Assert.Contains("mask 0x", result.Details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Exp_Status_Line_Is_A_Short_Phrase_With_No_Hex_And_Details_Collapsed()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings { InGameConfigMode = true };
            using var form = new MainForm(settings);
            form.CreateControl();
            form.RefreshStatusForTest();
            var collapsed = form.ExpDetailsExpandedForTest;
            var state = form.ExpStateLineForTest;
            var details = form.ExpDetailsForTest;
            // The disclosure is click-driven; opening it must reveal the witness.
            form.ToggleExpDetailsForTest();
            return (State: state, Details: details,
                Collapsed: collapsed,
                ExpandedAfterClick: form.ExpDetailsExpandedForTest,
                DetailsAfterClick: form.ExpDetailsForTest);
        });

        Assert.Null(error);
        // Default state line: a short human phrase — no hex, no mask/bridge jargon.
        Assert.DoesNotContain("0x", result.State, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mask", result.State, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bridge", result.State, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Calibrated", result.State, StringComparison.OrdinalIgnoreCase);
        // Default view is collapsed; the hex witness only lives in the details.
        Assert.False(result.Collapsed, "Details must be collapsed by default");
        Assert.Contains("mask 0x", result.Details, StringComparison.OrdinalIgnoreCase);
        // Clicking the disclosure opens it.
        Assert.True(result.ExpandedAfterClick, "Details must expand on toggle");
        Assert.Contains("mask 0x", result.DetailsAfterClick, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Exp_Transport_Is_Two_Rows_Of_Three_Columns()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings { InGameConfigMode = true };
            using var form = new MainForm(settings);
            form.CreateControl();
            var t = form.ExpTransportForTest;
            return (
                Columns: t.ColumnCount,
                Rows: t.RowCount,
                StartAt00: ReferenceEquals(form.ExpStartButtonForTest, t.GetControlFromPosition(0, 0)),
                PauseAt10: ReferenceEquals(form.ExpPauseButtonForTest, t.GetControlFromPosition(1, 0)),
                StopAt20: ReferenceEquals(form.ExpStopButtonForTest, t.GetControlFromPosition(2, 0)),
                CalAt01: ReferenceEquals(form.ExpCalibrateButtonForTest, t.GetControlFromPosition(0, 1)),
                OpenAt11: ReferenceEquals(form.ExpLaunchGameForTest, t.GetControlFromPosition(1, 1)),
                Spacer21: t.GetControlFromPosition(2, 1) is null,
                OpenLabel: form.ExpLaunchGameForTest.Text);
        });

        Assert.Null(error);
        Assert.Equal(3, result.Columns);
        Assert.Equal(2, result.Rows);
        Assert.True(result.StartAt00 && result.PauseAt10 && result.StopAt20,
            "row 1 must be Start/Pause/Stop");
        Assert.True(result.CalAt01 && result.OpenAt11,
            "row 2 must be Calibrate/Open Game");
        Assert.True(result.Spacer21, "row 2 cell 3 is the spacer");
        Assert.EndsWith("OPEN GAME", result.OpenLabel);
    }

    [Fact]
    public void Exp_Stop_Is_Enabled_Only_While_Running()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings { InGameConfigMode = true };
            using var form = new MainForm(settings);
            form.CreateControl();
            form.RefreshStatusForTest();
            return (Stop: form.StopEnabledForTest, Start: form.StartEnabledForTest, Running: form.EngineRunningForTest);
        });

        Assert.Null(error);
        // Stopped shell: Stop is inert, Start is live; the invariant is the
        // engine state, which is false here.
        Assert.False(result.Running);
        Assert.False(result.Stop);
        Assert.True(result.Start);
    }

    [Fact]
    public void Classic_Layout_Still_Has_Two_Logs()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings { InGameConfigMode = false };
            using var form = new MainForm(settings);
            form.CreateControl();
            return (Logs: CountType<RollingLog>(form), StatusPills: CountAccessibleName(form, "Exp status pill"));
        });

        Assert.Null(error);
        // The classic console home keeps Rolling log + History.
        Assert.Equal(2, result.Logs);
        Assert.Equal(0, result.StatusPills);
    }

    private static int CountToggles(Control root)
    {
        var count = root is ToggleSwitch ? 1 : 0;
        foreach (Control child in root.Controls) count += CountToggles(child);
        return count;
    }

    private static int CountType<T>(Control root) where T : Control
    {
        var count = root is T ? 1 : 0;
        foreach (Control child in root.Controls) count += CountType<T>(child);
        return count;
    }

    private static int CountAccessibleName(Control root, string name)
    {
        var count = string.Equals(root.AccessibleName, name, StringComparison.Ordinal) ? 1 : 0;
        foreach (Control child in root.Controls) count += CountAccessibleName(child, name);
        return count;
    }

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
}
