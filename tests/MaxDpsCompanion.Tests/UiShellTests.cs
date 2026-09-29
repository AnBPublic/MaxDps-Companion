using System.Runtime.InteropServices;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// UI 2.0 shell tests (v2.7 §47): the meaningful smoke pass in-process plus
/// navigation, explorer filtering and accessibility assertions. Everything
/// runs on a dedicated STA thread — the same contract as --ui-smoke-test.
/// </summary>
public class UiShellTests
{
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int MkLButton = 0x0001;

    /// <summary>Sends a real left-click (down+up) at the centre of a control.</summary>
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
    public void Navigation_Changes_Active_Page_And_Rail_State()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            form.CreateControl();
            var shell = form.ShellForTest;
            shell.Navigate(PageId.Diagnostics);
            var diagnosticsActive = shell.ActivePage == PageId.Diagnostics;
            shell.Navigate(PageId.Abilities);
            var abilitiesActive = shell.ActivePage == PageId.Abilities;
            var railItems = shell.Rail.Items.Count;
            var activeItems = 0;
            foreach (var item in shell.Rail.Items)
                if (item.Active) activeItems++;
            var accessible = true;
            foreach (var item in shell.Rail.Items)
                if (string.IsNullOrWhiteSpace(item.AccessibleName) || string.IsNullOrWhiteSpace(item.AccessibleDescription))
                    accessible = false;
            return (diagnosticsActive, abilitiesActive, railItems, activeItems, accessible);
        });

        Assert.Null(error);
        Assert.True(result.diagnosticsActive);
        Assert.True(result.abilitiesActive);
        Assert.Equal(5, result.railItems);
        Assert.Equal(1, result.activeItems);
        Assert.True(result.accessible);
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
    public void Intelligence_Report_Matches_Catalog_And_Drills_To_Abilities()
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

    [Fact]
    public void Home_Update_Renders_Long_Values_Without_Clipping_Or_Findings()
    {
        var (findings, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            form.CreateControl();
            var page = form.HomeForTest;
            page.Update(new HomeSnapshot(
                StatusTone.Success, "Wow window found",
                StatusTone.Success, "MaxDps linked (rotation active)",
                StatusTone.Success, "Running",
                "WARRIOR / Fury", "Normal mode", "Scheduler + intelligence on",
                "Impending Victory", "Pummel",
                "HP 22% at/below 35%; emergency self-sustain",
                "3279 registered \u00B7 47 companion \u00B7 3077 delegated",
                "patch 12.1 \u00B7 catalog v3",
                "Holding (out of combat)", StatusTone.Muted));
            page.PerformLayout();
            return UiShellValidation.Validate(page, "Home");
        });

        Assert.Null(error);
        Assert.True(findings!.Count == 0, string.Join("\n", findings));
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
    public void Rail_Item_Mouse_Click_Navigates()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            ShowOffscreen(form);
            var shell = form.ShellForTest;
            var abilities = shell.Rail.Items.First(i => i.Page == PageId.Abilities);
            var events = new List<string>();
            abilities.MouseDown += (_, _) => events.Add("down");
            abilities.MouseUp += (_, _) => events.Add("up");
            abilities.Click += (_, _) => events.Add("click");
            ClickControl(abilities);
            System.Windows.Forms.Application.DoEvents();
            var afterMessage = shell.ActivePage;
            var railSet = abilities.Rail is not null;
            // Wiring probe: invoke the protected OnClick exactly as the base
            // Control would after a real click.
            typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(abilities, [EventArgs.Empty]);
            System.Windows.Forms.Application.DoEvents();
            var afterOnClick = shell.ActivePage;
            return (afterMessage, afterOnClick, railSet, string.Join("+", events));
        });

        Assert.True(result.afterMessage == PageId.Abilities,
            $"mouse message path failed (rail={result.railSet} events={result.Item4})");
        Assert.Equal(PageId.Abilities, result.afterOnClick);
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
    public void Home_Live_Update_Keeps_The_First_Card_At_The_Top()
    {
        var (firstCardTop, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            form.CreateControl();
            var page = form.HomeForTest;
            for (var i = 0; i < 3; i++)
            {
                page.Update(new HomeSnapshot(
                    StatusTone.Success, "WoW window found \u00B7 strip decoded",
                    StatusTone.Success, "MaxDps link active",
                    StatusTone.Success, "Running",
                    "WARRIOR / Fury", "Normal", "Scheduler + intelligence on",
                    "Impending Victory", "Pummel",
                    "HP 22% at/below 35%; emergency self-sustain",
                    "3200 automatable \u00B7 3279 registered \u00B7 79 manual",
                    "patch 12.1 \u00B7 catalog v3",
                    "Holding (out of combat)", StatusTone.Muted));
                page.PerformLayout();
                System.Windows.Forms.Application.DoEvents();
            }
            var card = first (page, typeof(GlassCard));
            return card?.Top ?? -1;
        });

        Assert.Null(error);
        Assert.True(firstCardTop >= 0 && firstCardTop < 160,
            $"first card top after live updates is {firstCardTop} (expected < 160)");
    }

    private static Control? first(Control root, Type type)
    {
        foreach (Control child in root.Controls)
        {
            if (type.IsInstanceOfType(child)) return child;
            var found = first(child, type);
            if (found is not null) return found;
        }
        return null;
    }

    [Fact]
    public void Bundled_Geist_Font_Loads_From_Embedded_Resources()
    {
        UiFonts.Ensure();
        Assert.True(UiFonts.FamilyAvailable, "embedded Geist faces did not load (fallback chain in use)");
        Assert.Contains("Geist", UiFonts.FamilyName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Geist", DesignTokens.FamilyName, StringComparison.OrdinalIgnoreCase);
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
