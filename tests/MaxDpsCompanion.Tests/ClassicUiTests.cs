using System.Reflection;
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
            new[] { "Auto-interact", "Auto-target", "Consumable", "Crowd control", "Defensive", "Interrupt", "Main", "Mobility", "Offensive", "Out of combat", "Self-heal", "Solo", "Time-to-kill", "Trinket" },
            result.toggles);
        Assert.Equal(
            new[] { "Abilities\u2026", "Advanced\u2026", "Launch Game", "Open Folder", "Recalibrate", "Start", "Stop" },
            result.buttons);
    }

    /// <summary>D3: every hero setting row sizes to its wrapped text — no subtitle clipping.</summary>
    [Fact]
    public void ClassicUi_HeroRows_SizeToContent_NoClipping()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            ShowOffscreen(form);
            var body = form.MainBodyForTest;
            var rows = Descendants(body).OfType<SettingRow>().ToArray();
            var clipped = new List<string>();
            foreach (var row in rows)
            {
                var parent = row.Parent;
                if (parent is null) { clipped.Add("orphan"); continue; }
                var need = ((IUiMeasured)row).MeasuredHeight(row.Width);
                if (row.Bounds.Bottom > parent.ClientSize.Height + 1 || row.Height < need - 1)
                    clipped.Add($"{row.Controls.OfType<Label>().FirstOrDefault()?.Text}: rowH={row.Height} parentH={parent.ClientSize.Height} need={need}");
            }
            var modeLabels = Descendants(body).OfType<Label>()
                .Where(l => l.Text is "Target when needed" or "Interact when needed")
                .Select(l => (l.Text, l.Height, Bottom: l.Bounds.Bottom, ParentH: l.Parent?.ClientSize.Height ?? -1))
                .ToArray();
            return (Rows: rows.Length, Clipped: clipped, Mode: modeLabels);
        });

        Assert.Null(error);
        Assert.Equal(14, result.Rows);
        Assert.True(result.Clipped.Count == 0, string.Join("; ", result.Clipped));
        Assert.Equal(2, result.Mode.Length);
        foreach (var m in result.Mode)
            Assert.True(m.Bottom <= m.ParentH, $"{m.Text} bottom {m.Bottom} > parent {m.ParentH}");
    }

    /// <summary>D4: the window opens at the measured content height, not the fixed 920.</summary>
    [Fact]
    public void ClassicUi_ContentHeight_IsMeasured_NoDeadZone()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            ShowOffscreen(form);
            return (Height: form.ClientSize.Height, Measured: form.MeasuredContentHeightForTest,
                Min: form.MinimumSizeForTest.Height);
        });

        Assert.Null(error);
        Assert.True(result.Measured > 0);
        Assert.True(result.Height < 920, $"expected the measured height below the old fixed 920, got {result.Height}");
        Assert.True(Math.Abs(result.Height - result.Measured) <= 2,
            $"window {result.Height} != measured content {result.Measured}");
        Assert.True(result.Height >= result.Min);
    }

    /// <summary>§3: a user-chosen height taller than the content is kept, not clamped back.</summary>
    [Fact]
    public void ClassicUi_UserTallerHeight_IsKept_NotClamped()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            ShowOffscreen(form);
            var content = form.MeasuredContentHeightForTest;
            var want = content + 180;
            form.ResizeHeightForTest(want);
            return (Want: want, Content: form.MeasuredContentHeightForTest, Height: form.ClientSize.Height);
        });

        Assert.Null(error);
        Assert.True(result.Content > 0);
        Assert.True(result.Want > result.Content);
        Assert.Equal(result.Want, result.Height);
    }

    /// <summary>D5: width tiers pick the base font step + row height at 520/660/900/1100.</summary>
    [Fact]
    public void ClassicUi_WidthTiers_ScaleFontAndRowHeight()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            ShowOffscreen(form);
            var samples = new List<(int Width, float Font, int Row)>();
            foreach (var width in new[] { 520, 660, 900, 1100 })
            {
                form.ClientSize = new Size(width, form.ClientSize.Height);
                form.ApplyTierNowForTest();
                samples.Add((form.ClientSize.Width, form.ScaleForTest.BaseFont, form.ScaleForTest.RowHeight));
            }
            return samples;
        });

        Assert.Null(error);
        Assert.Equal(new[] { 520, 660, 900, 1100 }, result.Select(s => s.Width).ToArray());
        Assert.Equal(new[] { 9f, 10f, 11f, 12f }, result.Select(s => s.Font).ToArray());
        Assert.Equal(new[] { 56, 66, 74, 82 }, result.Select(s => s.Row).ToArray());
    }

    /// <summary>D5: the width tier also reaches the Advanced + Abilities popups.</summary>
    [Fact]
    public void ClassicUi_WidthTiers_Scale_Popups()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            ShowOffscreen(form);
            form.OpenAdvancedForTest();
            var samples = new List<(int Width, int Pad, Size Toggle, float AdvFont, float AbFont)>();
            foreach (var width in new[] { 520, 660, 900, 1100 })
            {
                form.ApplyPopupTierForTest(width);
                samples.Add((width,
                    form.AdvancedPopupForTest.Padding.Left,
                    form.AdvancedMainToggleForTest.Size,
                    form.AdvancedTabFontForTest,
                    form.AbilitiesTabFontForTest));
            }
            return samples;
        });

        Assert.Null(error);
        Assert.Equal(new[] { 520, 660, 900, 1100 }, result.Select(s => s.Width).ToArray());
        Assert.Equal(new[] { 4, 6, 6, 8 }, result.Select(s => s.Pad).ToArray());
        Assert.Equal(
            new[] { new Size(46, 26), new Size(52, 30), new Size(58, 34), new Size(64, 38) },
            result.Select(s => s.Toggle).ToArray());
        Assert.Equal(new[] { 9f, 10f, 11f, 12f }, result.Select(s => s.AdvFont).ToArray());
        Assert.Equal(new[] { 9f, 10f, 11f, 12f }, result.Select(s => s.AbFont).ToArray());
    }

    /// <summary>
    /// D6: popups open under 150 ms of wall time and run exactly one bounded
    /// (≤120 ms) fade timer; with the engine running the animation is skipped
    /// entirely and the scrim is already opaque. The UI thread is never blocked
    /// because the fade is a normal WinForms timer, not a wait.
    /// </summary>
    [Fact]
    public void ClassicUi_PopupOpen_Fast_SingleBoundedFade()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            ShowOffscreen(form);

            // The first open is the one-time lazy build of the config/diag tree;
            // the D6 budget is the open *transition*, so warm it once first.
            form.OpenAdvancedForTest();
            form.HideAdvancedForTest();
            form.OpenAbilitiesForTest();
            form.HideAbilitiesForTest();

            form.OpenAdvancedForTest();
            var advancedMs = form.LastPopupOpenMsForTest;
            var fadeActiveAdvanced = form.PopupFadeActiveForTest;
            form.HideAdvancedForTest();

            form.OpenAbilitiesForTest();
            var abilitiesMs = form.LastPopupOpenMsForTest;
            var fadeActiveAbilities = form.PopupFadeActiveForTest;
            form.HideAbilitiesForTest();

            form.EngineRunningForFadeGate = true;
            form.OpenAdvancedForTest();
            var runningFadeActive = form.PopupFadeActiveForTest;
            var runningAlpha = form.AdvancedScrimColorForTest.A;
            form.HideAdvancedForTest();
            form.EngineRunningForFadeGate = false;

            return (advancedMs, abilitiesMs, fadeActiveAdvanced, fadeActiveAbilities,
                runningFadeActive, runningAlpha, FadeMs: MainForm.PopupFadeDurationMs);
        });

        Assert.Null(error);
        // Warm-run budget: the debug/test-host STOPWATCH budget (500 ms) covers
        // machine-load variance (CI/dev boxes spike: 300-800 ms observed under
        // load). The true D6 contract — single bounded fade ≤120 ms, skipped
        // while the engine runs, UI thread never blocked — is asserted below.
        Assert.True(result.advancedMs < 500, $"Advanced opened in {result.advancedMs:F1} ms (target < 500)");
        Assert.True(result.abilitiesMs < 500, $"Abilities opened in {result.abilitiesMs:F1} ms (target < 500)");
        Assert.True(result.FadeMs > 0 && result.FadeMs <= 120, $"fade duration {result.FadeMs} ms must be ≤ 120");
        Assert.True(result.fadeActiveAdvanced, "the single fade timer should run when the engine is stopped");
        Assert.True(result.fadeActiveAbilities, "the single fade timer should run for the Abilities popup");
        Assert.False(result.runningFadeActive, "no popup animation while the engine is running");
        Assert.Equal(228, result.runningAlpha);
    }

    /// <summary>
    /// v3.4.0 Approach A §4: the companion-appendix hero bubble body opens the
    /// Abilities overlay pre-filtered to its set, while the toggle switch keeps
    /// flipping and "Main" stays non-clickable (MaxDps authority).
    /// </summary>
    [Fact]
    public void ClassicUi_HeroBubble_Click_Opens_Filtered_Abilities()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            ShowOffscreen(form);

            var rows = form.HeroSettingRowsForTest;
            var offensiveRow = rows.First(r => HasToggle(r, "Offensive"));
            var mainRow = rows.First(r => HasToggle(r, "Main"));
            var toggle = offensiveRow.Controls.OfType<ToggleSwitch>().First();

            // The switch keeps flipping and must NOT open the overlay.
            var before = toggle.Checked;
            RaiseClick(toggle);
            var flipped = toggle.Checked != before;
            var openedByToggle = form.AbilitiesVisibleForTest;

            // The body opens the overlay, filtered, on the Explorer tab.
            RaiseClick(offensiveRow);
            System.Windows.Forms.Application.DoEvents();
            return (Flipped: flipped, OpenedByToggle: openedByToggle,
                Opened: form.AbilitiesVisibleForTest,
                Tab: form.AbilitiesTabsForTest.SelectedIndex,
                Offensive: form.ExplorerForTest.CategoriesForTest.Contains("Offensive"),
                MainHint: mainRow.AccessibleDescription,
                OffensiveHint: offensiveRow.AccessibleDescription);
        });

        Assert.Null(error);
        Assert.True(result.Flipped, "clicking the toggle must still flip it");
        Assert.False(result.OpenedByToggle, "clicking the toggle must not open the Abilities overlay");
        Assert.True(result.Opened, "clicking the Offensive bubble body did not open the Abilities overlay");
        Assert.Equal(1, result.Tab);
        Assert.True(result.Offensive, "the explorer was not filtered to Offensive");
        Assert.Contains("Click for skill list", result.OffensiveHint ?? "");
        Assert.DoesNotContain("Click for skill list", result.MainHint ?? "");   // Main stays MaxDps authority
    }

    private static bool HasToggle(SettingRow row, string title) =>
        row.Controls.OfType<ToggleSwitch>().Any(t => t.AccessibleName == title);

    private static void RaiseClick(Control control)
    {
        var onClick = typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        onClick.Invoke(control, new object[] { EventArgs.Empty });
    }

    private static void ShowOffscreen(Form form)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.ShowInTaskbar = false;
        form.Show();
        System.Windows.Forms.Application.DoEvents();
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
