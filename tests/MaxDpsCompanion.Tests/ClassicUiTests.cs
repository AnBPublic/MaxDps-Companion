using MaxDpsCompanion.Ui.Shell;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// P2-E port of the v3 classic-UI tests (docs/plans/2026-10-11-winui3-v4-migration.md §6).
///
/// FINAL FATES (14 original tests):
///  * REINSTATED (4): DefaultAndMinimum_Sizes (window consts), Toggle_Writes_
///    Its_Settings_Key (ToggleModel), WidthTiers_ScaleFontAndRowHeight and
///    WidthTiers_Scale_Popups (frozen UiScale tiers).
///  * DELETED (10), each a WinForms control/internal assertion that has no
///    XAML-free equivalent; replacements noted:
///    - ScrollSurvivesRefresh (AutoScrollPosition/VerticalScroll internals) —
///      WinUI uses ScrollViewer; covered by --ui-smoke-test visual tree walk.
///    - BottomButtons_ExactLabels (control list + Enabled state) — labels are
///      built in ShellWindow.Content.cs; covered by --ui-snapshot review.
///    - Launcher_Delegate_Seam (test-only delegate) — WinUI calls the engine's
///      BattleNetLauncher directly; live launch stays OWED (would start BNet).
///    - ValueOnlyRefresh_DoesNotLayout (WinForms Layout events) — the WinUI
///      refresh body is timed by --bench-ui instead.
///    - Popups_Do_Not_Steal_Main_Controls (single-parent WinForms defect) —
///      WinUI overlays are separate in-window layers; --ui-smoke-test opens
///      both overlays and validates the result.
///    - HeroRows_SizeToContent_NoClipping, ContentHeight_Is_Measured_NoDeadZone,
///      UserTallerHeight_Is_Kept_NotClamped (WinForms measure/resize internals) —
///      XAML measures sizes; --ui-smoke-test walks the tree for zero-size rows.
///    - PopupOpen_Fast_StaticOpaqueScrim (WinForms paint internals + open
///      timing) — the WinUI scrim is a static XAML rectangle; timing OWED live.
///    - HeroBubble_Click_Opens_Filtered_Abilities (mouse click plumbing) —
///      AbilityViewPresets matching is tested by ApproachADrillThroughTests;
///      the click wiring is covered by the overlay drill in --ui-smoke-test.
/// </summary>
public class ClassicUiTests
{
    [Fact]
    public void ClassicUi_DefaultAndMinimum_Sizes()
    {
        // Frosted constants from the WinUI shell (const access is inlined at
        // compile time — no XAML runtime is loaded by this test).
        Assert.Equal(660, ShellWindow.DefaultWidthDip);
        Assert.Equal(920, ShellWindow.DefaultHeightDip);
        Assert.Equal(520, Win32Host.MinWidthDip);
        Assert.Equal(560, Win32Host.MinHeightDip);
    }

    [Fact]
    public void ClassicUi_Toggle_Writes_Its_Settings_Key()
    {
        // MainForm.SoloToggleForTest.Checked flip parity through the frozen model.
        var settings = new AppSettings();
        var model = ToggleModel.FromSettings(settings);
        Assert.False(model.Solo);
        Assert.False(settings.SoloEnabled);

        model.Solo = true;
        model.ApplyToSettings(settings);

        Assert.True(settings.SoloEnabled);
    }

    /// <summary>D5 (old test): width tiers pick the base font step + row height at 520/660/900/1100.</summary>
    [Fact]
    public void ClassicUi_WidthTiers_ScaleFontAndRowHeight()
    {
        var samples = new List<(int Width, float Font, int Row, UiTier Tier)>();
        foreach (var width in new[] { 520, 660, 900, 1100 })
        {
            var scale = UiScale.For(width);
            samples.Add((width, scale.BaseFont, scale.RowHeight, scale.Tier));
        }

        Assert.Equal(new[] { 520, 660, 900, 1100 }, samples.Select(s => s.Width).ToArray());
        Assert.Equal(new[] { 9f, 10f, 11f, 12f }, samples.Select(s => s.Font).ToArray());
        Assert.Equal(new[] { 56, 66, 74, 82 }, samples.Select(s => s.Row).ToArray());
        Assert.Equal(new[] { UiTier.Compact, UiTier.Classic, UiTier.Roomy, UiTier.Wide }, samples.Select(s => s.Tier).ToArray());
    }

    /// <summary>D5: the width tier reaches the popup toggle geometry and type (old Advanced + Abilities fonts).</summary>
    [Fact]
    public void ClassicUi_WidthTiers_Scale_Popups()
    {
        var samples = new List<(int Width, double ToggleW, double ToggleH, float Font)>();
        foreach (var width in new[] { 520, 660, 900, 1100 })
        {
            var scale = UiScale.For(width);
            samples.Add((width, scale.ToggleWidth, scale.ToggleHeight, scale.BaseFont));
        }

        Assert.Equal(new[] { 520, 660, 900, 1100 }, samples.Select(s => s.Width).ToArray());
        Assert.Equal(
            new[] { (46d, 26d), (52d, 30d), (58d, 34d), (64d, 38d) },
            samples.Select(s => (s.ToggleW, s.ToggleH)).ToArray());
        Assert.Equal(new[] { 9f, 10f, 11f, 12f }, samples.Select(s => s.Font).ToArray());
        // NOTE: the old WinForms popup padding (4/6/6/8) is gone; the WinUI
        // tier carries CardPadding (12/18/20/24) instead, asserted here so the
        // metric is not silently lost.
        Assert.Equal(new[] { 12, 18, 20, 24 }, new[] { 520, 660, 900, 1100 }.Select(w => UiScale.For(w).CardPadding).ToArray());
    }
}
