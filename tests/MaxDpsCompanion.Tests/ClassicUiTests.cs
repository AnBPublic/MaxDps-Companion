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

    /// <summary>
    /// A7 perf guard: the 250 ms timer body is value-only. Fifty refreshes with
    /// every field changing must not perform layout anywhere in the window or
    /// the open popup (no Add/Remove/Bounds/Visible churn on a timer path).
    /// </summary>
    [Fact]
    public void ClassicUi_ValueOnlyRefresh_DoesNotLayout()
    {
        var (layouts, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.ShowInTaskbar = false;
            form.Show();
            System.Windows.Forms.Application.DoEvents();

            form.OpenAdvancedForTest();
            form.AdvancedTabsForTest.SelectedIndex = 1; // Diagnostics: live value labels
            System.Windows.Forms.Application.DoEvents();

            var count = 0;
            void OnLayout(object? _, LayoutEventArgs __) => count++;
            foreach (var control in Descendants(form)) control.Layout += OnLayout;

            count = 0;
            form.PumpChangingSnapshotsForTest(50);
            return count;
        });

        Assert.Null(error);
        Assert.Equal(0, layouts);
    }

    /// <summary>
    /// v3.0.0 defect D1/D2: the hero slot/mode toggles and the Launch Game /
    /// Recalibrate / Open Folder buttons were ALSO added to the lazily-built
    /// Advanced popup. WinForms gives a control exactly one parent, so building
    /// the popup re-parented them off the main window — the "cards show no
    /// toggles" / "bottom buttons missing" screenshot. Opening every popup must
    /// leave the main body's own controls in place.
    /// </summary>
    [Fact]
    public void ClassicUi_Popups_Do_Not_Steal_Main_Controls()
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

            // Build every lazy popup tab exactly once, as a user would.
            form.OpenAdvancedForTest();
            form.AdvancedTabsForTest.SelectedIndex = 1;
            System.Windows.Forms.Application.DoEvents();
            form.AdvancedTabsForTest.SelectedIndex = 2;
            System.Windows.Forms.Application.DoEvents();
            form.OpenAbilitiesForTest();
            form.AbilitiesTabsForTest.SelectedIndex = 1;
            System.Windows.Forms.Application.DoEvents();
            form.HideAbilitiesForTest();
            form.HideAdvancedForTest();
            System.Windows.Forms.Application.DoEvents();

            var body = form.MainBodyForTest;
            var toggles = Descendants(body).OfType<ToggleSwitch>()
                .Select(t => t.AccessibleName ?? "?")
                .OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var buttons = Descendants(body).OfType<ChamferButton>()
                .Select(b => b.Text)
                .OrderBy(x => x, StringComparer.Ordinal).ToArray();
            return (toggles, buttons);
        });

        Assert.Null(error);
        Assert.Equal(
            new[] { "Auto-interact", "Auto-target", "Consumable", "Defensive", "Interrupt", "Main", "Mobility", "Offensive", "Out of combat", "Self-heal", "Solo", "Trinket" },
            result.toggles);
        Assert.Equal(
            new[] { "Abilities\u2026", "Advanced\u2026", "Launch Game", "Open Folder", "Recalibrate", "Start", "Stop" },
            result.buttons);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        var stack = new Stack<Control>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var control = stack.Pop();
            yield return control;
            foreach (Control child in control.Controls) stack.Push(child);
        }
    }
}
