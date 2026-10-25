extern alias win;

using win::MaxDpsCompanion.Ui.Shell;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 4.4.0 "Wayfinder" quiet bridge log. The gate is pure and clock-injected, so
/// every threshold/once-per-outage rule is pinned without a UI thread.
/// </summary>
public class BridgeLogGateTests
{
    [Fact]
    public void Never_Seen_Warns_Once_After_Five_Seconds()
    {
        var gate = new BridgeLogGate();

        Assert.False(gate.Observe(bridgeVisible: false, running: true, foreground: true, nowMs: 0));
        Assert.False(gate.Observe(false, true, true, BridgeLogGate.WarnAfterMs - 1));
        Assert.True(gate.Observe(false, true, true, BridgeLogGate.WarnAfterMs));
        // At most once per run, even after another 5 s.
        Assert.False(gate.Observe(false, true, true, BridgeLogGate.WarnAfterMs * 3));
    }

    [Fact]
    public void Seen_Then_Outage_Warns_Once()
    {
        var gate = new BridgeLogGate();

        Assert.False(gate.Observe(true, true, true, 0));
        Assert.True(gate.EverSeen);

        // The outage clock starts at the first invisible tick.
        Assert.False(gate.Observe(false, true, true, 1000));
        Assert.False(gate.Observe(false, true, true, 5999));
        Assert.True(gate.Observe(false, true, true, 6000));
        Assert.False(gate.Observe(false, true, true, 9000));
    }

    [Fact]
    public void Recovery_Re_Arms_The_Next_Outage()
    {
        var gate = new BridgeLogGate();

        Assert.False(gate.Observe(true, true, true, 0));
        Assert.False(gate.Observe(false, true, true, 1000));
        Assert.True(gate.Observe(false, true, true, 6000));

        // Recovery re-arms the latch.
        Assert.False(gate.Observe(true, true, true, 7000));
        Assert.False(gate.Observe(false, true, true, 8000));
        Assert.False(gate.Observe(false, true, true, 12_999));
        Assert.True(gate.Observe(false, true, true, 13_000));
        Assert.False(gate.Observe(false, true, true, 20_000));
    }

    [Fact]
    public void Background_Never_Warns_And_Does_Not_Accrue_The_Clock()
    {
        var gate = new BridgeLogGate();
        Assert.False(gate.Observe(true, true, true, 0));

        // A long alt-tab must not warn, and must not burn the 5 s threshold.
        Assert.False(gate.Observe(false, true, false, 1000));
        Assert.False(gate.Observe(false, true, false, 60_000));

        // Foreground return starts a fresh outage.
        Assert.False(gate.Observe(false, true, true, 61_000));
        Assert.False(gate.Observe(false, true, true, 65_999));
        Assert.True(gate.Observe(false, true, true, 66_000));
    }

    [Fact]
    public void A_Background_Only_Run_Never_Warns()
    {
        var gate = new BridgeLogGate();

        Assert.False(gate.Observe(false, true, false, 0));
        Assert.False(gate.Observe(false, true, false, 10_000));
        Assert.False(gate.Observe(false, true, false, 60_000));
    }

    [Fact]
    public void Stopping_And_Starting_Resets_The_Run()
    {
        var gate = new BridgeLogGate();

        Assert.False(gate.Observe(false, true, true, 0));
        Assert.False(gate.Observe(false, false, true, 1000));   // engine stopped: reset

        // A new run gets the full 5 s again from its own start.
        Assert.False(gate.Observe(false, true, true, 2000));
        Assert.False(gate.Observe(false, true, true, 6999));
        Assert.True(gate.Observe(false, true, true, 7000));
    }

    [Fact]
    public void Reset_Clears_Seen_And_Warned()
    {
        var gate = new BridgeLogGate();

        Assert.False(gate.Observe(true, true, true, 0));
        gate.Reset();

        Assert.False(gate.EverSeen);
        Assert.False(gate.Observe(false, true, true, 10_000));
        Assert.True(gate.Observe(false, true, true, 15_000));
    }
}
