using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Classic-UI regression tests (v3.0.0 workstream A). The headline case is the
/// v2.8.1 scroll defect: a focus-driven auto-scroll offset was silently reset
/// by the live status refresh, leaving a large empty band above the first card.
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
        var (survived, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.ShowInTaskbar = false;
            form.Show();
            System.Windows.Forms.Application.DoEvents();

            var panel = form.HomeForTest.ScrollArea;
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
        Assert.True(survived.Set == 300, $"scroll did not take (got {survived.Set})");
        Assert.Equal(300, survived.After);
    }
}
