extern alias win;

using win::MaxDpsCompanion.Ui.Shell;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 4.3.0 "Namesake" release identity: the assembly version is single-sourced
/// from the csproj, the codename from AssemblyMetadata, and the window title is
/// exactly "MaxDPS Companion v4.3.0 Namesake" with NO build hash/time (those
/// moved to the Advanced Diagnostics install doctor card).
///
/// The real runtime title is verified by the bin run
/// <c>MaxDpsCompanion.exe --ui-smoke-test</c> (ui-smoke.txt "window title:"),
/// which reports "MaxDPS Companion v4.3.0 Namesake"; ShellWindow builds it from
/// the same single source asserted here.
/// </summary>
public class ReleaseIdentityTests
{
    [Fact]
    public void Release_AppVersion_Is_4_3_0()
        => Assert.Equal("4.3.0", Native.AppVersion);

    [Fact]
    public void Release_DisplayVersion_Includes_Codename()
        => Assert.Equal("v4.3.0 Namesake", Native.DisplayVersion);

    [Fact]
    public void Release_HeaderTitle_Is_Version_And_Codename_No_Hash()
    {
        // The WinUI shell title (ShellWindow.WindowTitle, used by the ctor and
        // reported live by --ui-smoke-test).
        var title = ShellWindow.WindowTitle;
        Assert.Equal("MaxDPS Companion v4.3.0 Namesake", title);
        Assert.DoesNotContain("(", title);
        Assert.DoesNotContain("[", title);
    }
}
