using System.Drawing;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// S6 (v3.5) Class Browser: STA construction, selectors/modes, warm open-time
/// budget and an owner-drawn paint smoke. Offline only — the live popup remains
/// OWED (docs/TESTING.md §3).
/// </summary>
public class ClassBrowserViewTests
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

    private static void ShowOffscreen(Form form)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.ShowInTaskbar = false;
        form.Show();
        System.Windows.Forms.Application.DoEvents();
    }

    [Fact]
    public void Browser_Builds_Rows_For_Class_Spec_And_Mode()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            form.CreateControl();
            var browser = form.ClassBrowserForTest;
            browser.Open("ROGUE", "Outlaw");
            var all = browser.RowCount;
            browser.ApplyPreset("Offensive", "ROGUE", "Outlaw");
            var offensive = browser.RowCount;
            browser.ApplyPreset("Trinket", "ROGUE", "Outlaw");
            var trinket = browser.RowCount;
            return (all, offensive, trinket, browser.DebugState);
        });

        Assert.Null(error);
        Assert.True(result.all > 0, "the browser should list Rogue/Outlaw abilities");
        Assert.True(result.offensive > 0, "Rogue/Outlaw should have offensive abilities");
        Assert.True(result.offensive <= result.all, "a mode must only narrow the list");
        Assert.True(result.trinket >= 0);
        Assert.Contains("class=ROGUE", result.DebugState);
        Assert.Contains("spec=Outlaw", result.DebugState);
    }

    [Fact]
    public void Browser_Exposes_Every_Mode()
    {
        var (modes, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            form.CreateControl();
            return (IReadOnlyList<string>)form.ClassBrowserForTest.ModeLabelsForTest.ToArray();
        });

        Assert.Null(error);
        Assert.Equal(
            new[]
            {
                "All", "Main", "Offensive", "Defensive", "Interrupt", "CC", "Mobility",
                "Solo self-sustain", "Consumable", "Trinket", "Utility/manual",
            },
            modes);
    }

    /// <summary>
    /// S6 gate: a warm reopen (same class/spec/mode) stays under 150 ms. The
    /// first open pays the filter + sort; the measured call reuses the list.
    /// </summary>
    [Fact]
    public void Browser_Warm_Open_Under_150ms()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            ShowOffscreen(form);
            form.OpenAbilitiesForTest();       // cold: builds the config tree + browser
            form.HideAbilitiesForTest();

            var browser = form.ClassBrowserForTest;
            browser.Open("ROGUE", "Outlaw");   // warm the selection
            browser.Open("ROGUE", "Outlaw");   // measured warm reopen (no rebuild)
            var warm = browser.LastOpenMs;

            form.OpenAbilitiesForTest();       // warm popup open
            var popupWarm = form.LastPopupOpenMsForTest;
            form.HideAbilitiesForTest();
            Console.WriteLine($"MEASURE classBrowserWarmOpenMs={warm:F3} abilitiesPopupWarmOpenMs={popupWarm:F3} rows={browser.RowCount}");
            return (warm, popupWarm, browser.RowCount);
        });

        Assert.Null(error);
        Assert.True(result.RowCount > 0);
        Assert.True(result.warm < 150, $"Class Browser warm open took {result.warm:F1} ms (target < 150)");
        Assert.True(result.popupWarm < 500, $"Abilities popup warm open took {result.popupWarm:F1} ms (target < 500)");
    }

    /// <summary>
    /// S5/S6 no-garble smoke: the owner-drawn row list paints to a bitmap with
    /// no exception. (The old popup garbled because an alpha scrim sat behind
    /// native children; the browser is one opaque owner-drawn layer.)
    /// </summary>
    [Fact]
    public void Browser_Paint_Smoke_No_Garble()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            ShowOffscreen(form);
            form.OpenAbilitiesForTest();
            var browser = form.ClassBrowserForTest;
            browser.ApplyPreset("All", "MAGE", "Fire");
            System.Windows.Forms.Application.DoEvents();

            var width = Math.Max(320, browser.Width);
            var height = Math.Max(200, browser.Height);
            using var bitmap = new Bitmap(width, height);
            browser.DrawToBitmap(bitmap, new Rectangle(0, 0, width, height));
            // A painted surface has the dark background somewhere in the frame.
            var background = DesignTokens.Background;
            var sawBackground = false;
            for (var x = 0; x < width && !sawBackground; x += 8)
                for (var y = 0; y < height && !sawBackground; y += 8)
                    if (bitmap.GetPixel(x, y).ToArgb() == background.ToArgb()) sawBackground = true;

            form.HideAbilitiesForTest();
            return (width, height, sawBackground, browser.RowCount);
        });

        Assert.Null(error);
        Assert.True(result.width > 0 && result.height > 0);
        Assert.True(result.RowCount > 0);
        Assert.True(result.sawBackground, "the owner-drawn list did not paint its background");
    }
}
