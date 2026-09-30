using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Classic-UI shell tests (v3.0.0 workstream A). Everything runs on a
/// dedicated STA thread — the same contract as --ui-smoke-test.
/// </summary>
public class UiShellTests
{
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int MkLButton = 0x0001;

    private static void ClickControl(Control control)
    {
        var handle = control.Handle;
        var lParam = (IntPtr)((control.Height / 2 << 16) | (control.Width / 2 & 0xFFFF));
        SendMessage(handle, WmMouseMove, IntPtr.Zero, lParam);
        SendMessage(handle, WmLButtonDown, (IntPtr)MkLButton, lParam);
        SendMessage(handle, WmLButtonUp, IntPtr.Zero, lParam);
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

    [Fact]
    public void Shell_Smoke_Test_Has_No_Structural_Findings()
    {
        var (findings, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            form.CreateControl();
            return form.RunSmokeTest();
        });

        Assert.Null(error);
        Assert.NotNull(findings);
        Assert.True(findings!.Count == 0, string.Join("\n", findings));
    }

    [Fact]
    public void Popups_Open_Close_And_Esc_Closes_Topmost()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            form.CreateControl();
            ShowOffscreen(form);

            var closedAtStart = !form.AnyPopupVisibleForTest;
            form.OpenAdvancedForTest();
            var advancedOpen = form.AnyPopupVisibleForTest;
            var advancedTabs = form.AdvancedTabsForTest.TabPages.Count;
            var escClosedAdvanced = form.HandleEscapeForTest();
            var advancedAfterEsc = form.AnyPopupVisibleForTest;

            form.OpenAbilitiesForTest();
            var abilitiesOpen = form.AnyPopupVisibleForTest;
            var abilitiesTabs = form.AbilitiesTabsForTest.TabPages.Count;
            var escHandled = form.HandleEscapeForTest();
            var abilitiesAfterEsc = form.AnyPopupVisibleForTest;

            return (closedAtStart, advancedOpen, advancedTabs, escClosedAdvanced, advancedAfterEsc,
                abilitiesOpen, abilitiesTabs, escHandled, abilitiesAfterEsc);
        });

        Assert.Null(error);
        Assert.True(result.closedAtStart);
        Assert.True(result.advancedOpen);
        Assert.Equal(3, result.advancedTabs);
        Assert.True(result.escClosedAdvanced);
        Assert.False(result.advancedAfterEsc);
        Assert.True(result.abilitiesOpen);
        Assert.Equal(1, result.abilitiesTabs);
        Assert.True(result.escHandled);
        Assert.False(result.abilitiesAfterEsc);
    }

    [Fact]
    public void Explorer_Filters_Searches_And_Keeps_Catalog_Responsive()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            form.CreateControl();
            var explorer = form.ExplorerForTest;
            explorer.ApplyPreset("All");
            var all = explorer.FilteredCount;
            explorer.SelectStatus("Automatic");
            var automatic = explorer.FilteredCount;
            explorer.SelectStatus("All");
            explorer.SetQuery("Charge");
            var searched = explorer.FilteredCount;
            explorer.SetQuery("0");
            var numeric = explorer.FilteredCount;
            return (all, automatic, searched, numeric);
        });

        Assert.Null(error);
        Assert.True(result.all > 100, $"expected a large catalog, got {result.all}");
        Assert.True(result.automatic > 0);
        Assert.True(result.automatic <= result.all);
        Assert.True(result.searched > 0);
        Assert.True(result.numeric > 0);
    }

    [Fact]
    public void Intelligence_Tile_Click_Drills_To_Abilities_Explorer()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            form.CreateControl();
            ShowOffscreen(form);
            form.IntelligenceForTest.EnsureBuilt();

            // The Intelligence tab lives in the Advanced popup; lay it out so
            // the tile has a real, non-empty client rect before the click.
            form.OpenAdvancedForTest();
            form.AdvancedTabsForTest.SelectedIndex = 2;
            System.Windows.Forms.Application.DoEvents();

            var tile = form.IntelligenceForTest.TilesForTest["Companion"];
            ClickControl(tile);
            System.Windows.Forms.Application.DoEvents();

            return (form.AbilitiesVisibleForTest,
                form.AbilitiesTabsForTest.SelectedIndex,
                ReadExplorerStatus(form.ExplorerForTest));
        });

        Assert.Null(error);
        Assert.True(result.Item1, "drilling from the Intelligence tile did not open the Abilities popup");
        Assert.Equal(0, result.Item2);
        Assert.Equal("Companion", result.Item3);
    }

    /// <summary>Reads the explorer's active status filter (private field, test-only).</summary>
    private static string ReadExplorerStatus(AbilityExplorer explorer)
    {
        var filter = typeof(AbilityExplorer)
            .GetField("_filter", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(explorer)!;
        return (string)filter.GetType().GetProperty("Status")!.GetValue(filter)!;
    }

    [Fact]
    public void Intelligence_Report_Matches_Catalog()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            form.CreateControl();
            form.IntelligenceForTest.EnsureBuilt();
            var report = form.IntelligenceForTest.Report;
            return (report?.Registered ?? 0, report?.Automatable ?? 0, AbilityCatalog.Default.Count);
        });

        Assert.Null(error);
        Assert.Equal(result.Item3, result.Item1);
        Assert.True(result.Item2 > 0);
        Assert.True(result.Item2 <= result.Item1);
    }

    /// <summary>Shows the form offscreen so real mouse messages take the shown-window path.</summary>
    private static void ShowOffscreen(Form form)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.ShowInTaskbar = false;
        form.Show();
        System.Windows.Forms.Application.DoEvents();
    }

    [Fact]
    public void Toggle_Switch_Mouse_Click_Toggles()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            ShowOffscreen(form);
            var toggle = new ToggleSwitch();
            var host = new Panel { Width = 100, Height = 60, Location = new Point(10, 10) };
            host.Controls.Add(toggle);
            form.Controls.Add(host);
            System.Windows.Forms.Application.DoEvents();
            var events = new List<string>();
            toggle.MouseDown += (_, _) => events.Add("down");
            toggle.MouseUp += (_, _) => events.Add("up");
            toggle.Click += (_, _) => events.Add("click");
            toggle.CheckedChanged += (_, _) => events.Add($"changed:{toggle.Checked}");
            ClickControl(toggle);
            System.Windows.Forms.Application.DoEvents();
            return (toggle.Checked, string.Join("+", events));
        });

        Assert.True(result.Checked, $"toggle did not check (events={result.Item2})");
    }

    [Fact]
    public void Wrapping_Label_Re_Measures_On_Width_Change()
    {
        var (heights, error) = RunOnSta(() =>
        {
            var label = new Label
            {
                Text = "Requires Combat intelligence. Below 65% HP efficient self-heals become eligible; below 35% HP survival actions outrank damage.",
                AutoSize = true,
                MaximumSize = new Size(860, 0),
                Font = DesignTokens.Type(DesignTokens.BodySize),
            };
            var wideHeight = UiMeasure.Height(label, 1200);
            var narrowHeight = UiMeasure.Height(label, 300);
            return (wideHeight, narrowHeight);
        });

        Assert.Null(error);
        Assert.True(heights.wideHeight < heights.narrowHeight,
            $"expected wrapping to grow: wide={heights.wideHeight} narrow={heights.narrowHeight}");
        Assert.True(heights.wideHeight > 0 && heights.narrowHeight > 0);
    }
}
