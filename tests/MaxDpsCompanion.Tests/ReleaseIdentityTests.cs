using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 3.5.2.2 "Fullcover" release identity: the assembly version is single-sourced
/// from the csproj, the codename from AssemblyMetadata, and the window title is
/// exactly "MaxDPS Companion v3.5.2.2 Fullcover" with NO build hash/time (those
/// moved to the Advanced Diagnostics install doctor card).
/// </summary>
public class ReleaseIdentityTests
{
    [Fact]
    public void Release_AppVersion_Is_3_5_2_2()
        => Assert.Equal("3.5.2.2", Native.AppVersion);

    [Fact]
    public void Release_DisplayVersion_Includes_Codename()
        => Assert.Equal("v3.5.2.2 Fullcover", Native.DisplayVersion);

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
        Assert.Equal("MaxDPS Companion v3.5.2.2 Fullcover", title);
        Assert.DoesNotContain("(", title);
        Assert.DoesNotContain("[", title);
    }
}
