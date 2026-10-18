using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// In-game-config shell flag (spec docs/plans/2026-10-03-ingame-config.md
/// §1/§14): the settings.ini key InGameConfigMode=0/1 and the legacy --exp CLI
/// alias. Since 4.3.0 the in-game-config shell is the DEFAULT;
/// InGameConfigMode=0 opts back into the classic shell. P2-E port
/// (docs/plans/2026-10-11-winui3-v4-migration.md §6).
///
/// FINAL FATES (13 methods = 14 cases):
///  * REINSTATED (5 methods / 6 cases): the settings key default (now true),
///    ini parse (2 cases), --exp force-on, --exp absent, and Save round-trip —
///    all XAML-free and unchanged.
///  * DELETED (8): Exp_Layout_Contains_Zero_Toggle_Controls,
///    Exp_Layout_Is_Minimal_Single_Log_And_One_Status_Pill,
///    Exp_Button_Row_Has_Wired_OpenGame_Last, Exp_Status_Pill_Shows_State_And_
///    Fps_Only, Exp_Status_Line_Is_A_Short_Phrase..., Exp_Transport_Is_Two_Rows_
///    Of_Three_Columns, Exp_Stop_Is_Enabled_Only_While_Running and
///    Classic_Layout_Still_Has_Two_Logs. Each asserted WinForms control
///    internals (ToggleSwitch/TableLayoutPanel/RollingLog counts, Text
///    properties, TableLayout coordinates, Enabled state) that no longer
///    exist. The toggle-free contract survives as a live check:
///    <c>MaxDpsCompanion.exe --ui-smoke-test</c> walks the shown WinUI tree and
///    fails if any ToggleSwitch is mounted (ShellValidation; the in-game-config
///    shell runs by default, the legacy <c>--exp</c> alias still forces it on);
///    the transport/status/button layout is verified by
///    --ui-snapshot-page=main.
/// </summary>
public class InGameConfigModeTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"mdb-ingameconfig-{Guid.NewGuid():N}.ini");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void InGameConfigMode_Defaults_To_True()
    {
        var settings = AppSettings.Load(_path);

        Assert.True(settings.InGameConfigMode);
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
    public void Legacy_Exp_Argument_Forces_Mode_On_Regardless_Of_Ini()
    {
        File.WriteAllText(_path, "[Window]\nInGameConfigMode=0\n");
        var settings = AppSettings.Load(_path);
        Assert.False(settings.InGameConfigMode);

        Program.ApplyExpFlag(settings, new[] { "--exp" });

        Assert.True(settings.InGameConfigMode);
    }

    [Fact]
    public void Legacy_Exp_Argument_Absent_Leaves_Ini_Value_Untouched()
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
}
