using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 4.1 A2.7: a missing game process must not re-enumerate every process on
/// every 33 ms tick. The sweep is throttled to the 1 s handle-verify cadence,
/// and a configured-name change bypasses the throttle.
/// </summary>
public class WowWindowThrottleTests
{
    [Fact]
    public void Missing_Window_Sweeps_At_Most_Once_Per_Second()
    {
        var now = 1000L;
        var lookups = 0;
        var window = new WowWindow(() => now, _ => { lookups++; return IntPtr.Zero; });

        Assert.False(window.Refresh("Wow"));       // sweep 1
        Assert.False(window.Refresh("Wow"));       // throttled
        now += 500;
        Assert.False(window.Refresh("Wow"));       // still throttled
        Assert.Equal(1, lookups);

        now += 501;                                // 1001 ms since the miss
        Assert.False(window.Refresh("Wow"));       // sweep 2
        Assert.Equal(2, lookups);
    }

    [Fact]
    public void A_Name_Change_Bypasses_The_Throttle()
    {
        var now = 1000L;
        var names = new List<string>();
        var window = new WowWindow(() => now, name => { names.Add(name); return IntPtr.Zero; });

        Assert.False(window.Refresh("Wow"));
        Assert.False(window.Refresh("WowClassic"));   // different names: immediate re-resolve
        Assert.Equal(2, names.Count);
        Assert.Equal("WowClassic", names[1]);
    }

    [Fact]
    public void A_Found_Window_Is_Returned_And_Handed_Out()
    {
        var window = new WowWindow(() => 1000, _ => new IntPtr(0x1234));

        Assert.True(window.Refresh("Wow"));
        Assert.Equal(new IntPtr(0x1234), window.Handle);
        // 0x1234 is not a live HWND on the test box, so the identity helpers
        // stay false — the sweep result itself is what this pins.
        Assert.False(window.IsGameWindow(new IntPtr(0x9999)));
    }

    [Fact]
    public void Reset_Clears_The_Miss_Throttle()
    {
        var now = 1000L;
        var lookups = 0;
        var window = new WowWindow(() => now, _ => { lookups++; return IntPtr.Zero; });

        Assert.False(window.Refresh("Wow"));
        window.Reset();
        Assert.False(window.Refresh("Wow"));   // immediate after an explicit reset
        Assert.Equal(2, lookups);
    }
}
