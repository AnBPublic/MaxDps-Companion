extern alias win;

using System.Xml.Linq;
using win::MaxDpsCompanion.Ui.Shell;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 4.5.0 "Voidlight" release identity: the assembly version is single-sourced
/// from the csproj, the codename from AssemblyMetadata, and the window title is
/// exactly "MaxDPS Companion v4.5.5 Voidlight" with NO build hash/time (those
/// moved to the Advanced Diagnostics install doctor card).
///
/// The real runtime title is verified by the bin run
/// <c>MaxDpsCompanion.exe --ui-smoke-test</c> (ui-smoke.txt "window title:"),
/// which reports "MaxDPS Companion v4.5.5 Voidlight"; ShellWindow builds it from
/// the same single source asserted here.
/// </summary>
public class ReleaseIdentityTests
{
    [Fact]
    public void Release_AppVersion_Is_4_5_3()
        => Assert.Equal("4.5.5", Native.AppVersion);

    [Fact]
    public void Release_DisplayVersion_Includes_Codename()
        => Assert.Equal("v4.5.5 Voidlight", Native.DisplayVersion);

    [Fact]
    public void Release_HeaderTitle_Is_Version_And_Codename_No_Hash()
    {
        // The WinUI shell title (ShellWindow.WindowTitle, used by the ctor and
        // reported live by --ui-smoke-test).
        var title = ShellWindow.WindowTitle;
        Assert.Equal("MaxDPS Companion v4.5.5 Voidlight", title);
        Assert.DoesNotContain("(", title);
        Assert.DoesNotContain("[", title);
    }

    /// <summary>
    /// 4.5.5 guard: app/Version.props is the Core/Mac version (the Windows
    /// csproj keeps its own copy). The two drifted at the 4.5.0-4.5.5 cuts -
    /// the macOS build still reported 4.4.0 - so this test makes the next
    /// release fail unless both identity files move together.
    /// </summary>
    [Fact]
    public void SharedVersionProps_MatchWindowsIdentity_AndCurrentRelease()
    {
        var (version, codename) = ReadSharedVersionProps();
        Assert.Equal(Native.AppVersion, version);
        Assert.Equal(Native.Codename, codename);
        Assert.Equal(ReleaseNotes.Current.Version, version);
        Assert.Equal(ReleaseNotes.Current.Codename, codename);
    }

    /// <summary>Walks up from the test output directory to the checkout root.</summary>
    private static (string Version, string Codename) ReadSharedVersionProps()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "app", "Version.props");
            if (File.Exists(candidate))
            {
                var propertyGroup = XDocument.Load(candidate).Root?.Element("PropertyGroup");
                var version = propertyGroup?.Element("Version")?.Value.Trim();
                var codename = propertyGroup?.Element("Codename")?.Value.Trim();
                Assert.False(string.IsNullOrWhiteSpace(version), $"{candidate} has no <Version>");
                Assert.False(string.IsNullOrWhiteSpace(codename), $"{candidate} has no <Codename>");
                return (version!, codename!);
            }
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "app/Version.props not found above " + AppContext.BaseDirectory);
    }
}
