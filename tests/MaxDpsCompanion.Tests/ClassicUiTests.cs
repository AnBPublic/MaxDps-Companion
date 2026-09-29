using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Classic-UI tests (v3.0.0 workstream A). The headline case is the v2.8.1
/// scroll defect: a focus-driven auto-scroll offset was silently reset by the
/// live status refresh, leaving a large empty band above the first card.
/// Rule, enforced for life: no AutoScrollPosition writes on any timer path.
/// </summary>
public class ClassicUiTests
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
    public void ClassicUi_ScrollSurvivesRefresh()
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
            form.OpenAdvancedForTest();
            System.Windows.Forms.Application.DoEvents();
            var panel = form.ConfigurationForTest.ScrollArea;
            // Guarantee a scrollable range regardless of the headless form size.
            panel.AutoScrollMinSize = new Size(0, 4000);
            panel.PerformLayout();
            panel.AutoScrollPosition = new Point(0, 300);
            System.Windows.Forms.Application.DoEvents();
            var set = panel.VerticalScroll.Value;

            for (var i = 0; i < 20; i++) form.RefreshStatusForTest();
            return (Set: set, After: panel.VerticalScroll.Value);
        });

        Assert.Null(error);
        Assert.True(result.Set == 300, $"scroll did not take (got {result.Set})");
        Assert.Equal(300, result.After);
    }

    [Fact]
    public void ClassicUi_DefaultAndMinimum_Sizes()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            form.CreateControl();
            return (form.MinimumSizeForTest, form.ClientSize, form.DefaultClientSizeForTest);
        });

        Assert.Null(error);
        Assert.Equal(new Size(520, 560), result.MinimumSizeForTest);
        Assert.True(result.ClientSize.Width >= 520, $"client width {result.ClientSize.Width} below minimum");
        Assert.True(result.ClientSize.Height >= 560, $"client height {result.ClientSize.Height} below minimum");
        Assert.Equal(new Size(660, 920), result.DefaultClientSizeForTest);
    }

    [Fact]
    public void ClassicUi_BottomButtons_ExactLabels_AndEnabledByState()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            form.CreateControl();
            return (Labels: form.BottomButtonLabelsForTest.ToArray(), Start: form.StartEnabledForTest, Stop: form.StopEnabledForTest);
        });

        Assert.Null(error);
        Assert.Equal(
            new[] { "Start", "Stop", "Launch Game", "Recalibrate", "Abilities\u2026", "Advanced\u2026", "Open Folder" },
            result.Labels);
        Assert.True(result.Start, "Start must be enabled while stopped");
        Assert.False(result.Stop, "Stop must be disabled while stopped");
    }

    [Fact]
    public void ClassicUi_Toggle_Writes_Its_Settings_Key()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            form.CreateControl();
            var beforeSolo = settings.SoloEnabled;
            form.SoloToggleForTest.Checked = !form.SoloToggleForTest.Checked;
            return (Before: beforeSolo, After: settings.SoloEnabled);
        });

        Assert.Null(error);
        Assert.False(result.Before);
        Assert.True(result.After);
    }

    [Fact]
    public void ClassicUi_Launcher_Delegate_Seam_No_Real_BattleNet()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            form.CreateControl();
            var path = "unused";
            form.Launcher = p => { path = p ?? "<null>"; return (true, "stub-launch-ok"); };
            form.InvokeLaunchForTest();
            return (Status: form.StatusLineForTest, Path: path);
        });

        Assert.Null(error);
        Assert.Contains("stub-launch-ok", result.Status);
    }
}
