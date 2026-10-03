using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 3.7.0 "Vigil" release identity: the assembly version is single-sourced
/// from the csproj, the codename from AssemblyMetadata, and the window title is
/// exactly "MaxDPS Companion v3.7.0 Vigil" with NO build hash/time (those
/// moved to the Advanced Diagnostics install doctor card).
/// </summary>
public class ReleaseIdentityTests
{
    [Fact]
    public void Release_AppVersion_Is_3_7_0()
        => Assert.Equal("3.7.0", Native.AppVersion);

    [Fact]
    public void Release_DisplayVersion_Includes_Codename()
        => Assert.Equal("v3.7.0 Vigil", Native.DisplayVersion);

    [Fact]
    public void Release_HeaderTitle_Is_Version_And_Codename_No_Hash()
    {
        string title = "";
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings();
                using var form = new MainForm(settings);
                form.CreateControl();
                title = form.HeaderTitleForTest;
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "STA UI thread timed out");

        Assert.Null(error);
        Assert.Equal("MaxDPS Companion v3.7.0 Vigil", title);
        Assert.DoesNotContain("(", title);
        Assert.DoesNotContain("[", title);
    }
}
