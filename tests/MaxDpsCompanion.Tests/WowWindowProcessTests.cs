using System.Diagnostics;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 4.4.0 "Wayfinder": the process helpers behind Open Game / Game Running /
/// Restart Game. These tests are deliberately defensive — they only ever probe
/// the CURRENT process and a GUID name, so no real WoW client is touched and
/// nothing is killed.
/// </summary>
public class WowWindowProcessTests
{
    private static string CurrentName() => Process.GetCurrentProcess().ProcessName;

    [Fact]
    public void Current_Process_Is_Reported_Running()
    {
        Assert.True(WowWindow.IsProcessRunning(CurrentName()));
    }

    [Fact]
    public void Unknown_Name_Is_Not_Running()
    {
        var name = "mdc-no-such-process-" + Guid.NewGuid().ToString("N");
        Assert.False(WowWindow.IsProcessRunning(name));
    }

    [Fact]
    public void Exe_Suffix_Is_Normalised()
    {
        Assert.True(WowWindow.IsProcessRunning(CurrentName() + ".exe"));
    }

    [Fact]
    public void Empty_Name_Is_Not_Running()
    {
        Assert.False(WowWindow.IsProcessRunning(""));
        Assert.False(WowWindow.IsProcessRunning("   "));
    }

    [Fact]
    public void Killing_With_An_Empty_Name_Is_Refused()
    {
        var (killed, ok, message) = WowWindow.KillGameProcesses("   ", 1000);

        Assert.Equal(0, killed);
        Assert.False(ok);
        Assert.Contains("empty", message);
    }

    [Fact]
    public void Killing_The_Companion_Itself_Is_Refused()
    {
        var (killed, ok, message) = WowWindow.KillGameProcesses(CurrentName(), 1000);

        Assert.Equal(0, killed);
        Assert.False(ok);
        Assert.Contains("companion", message);
    }

    [Fact]
    public void Killing_An_Absent_Process_Reports_Not_Running()
    {
        var name = "mdc-no-such-process-" + Guid.NewGuid().ToString("N");
        var (killed, ok, message) = WowWindow.KillGameProcesses(name, 0);

        Assert.Equal(0, killed);
        Assert.False(ok);
        Assert.Contains(name, message);
    }
}
