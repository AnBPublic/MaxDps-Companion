using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 4.5.3 hold copy: the status card must say WHY the engine is holding instead
/// of the green "waiting for suggestion", most importantly when the in-game
/// Main toggle is OFF (the 4.0.4 master switch) - that looked like a broken
/// companion.
/// </summary>
public class HoldCopyTests
{
    [Theory]
    [InlineData("holding (addon Main off - rotation suspended)", "Main off in game")]
    [InlineData("Holding (addon Main off - rotation suspended)", "Main off in game")]
    [InlineData("holding (typing in game chat)", "typing in game chat")]
    [InlineData("holding (out of combat)", "out of combat")]
    [InlineData("link lost (addon heartbeat frozen)", "link lost - addon heartbeat")]
    public void Reason_MapsEngineHolds(string status, string expected)
        => Assert.Equal(expected, HoldCopy.Reason(status));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Running")]
    [InlineData("waiting for a target")]
    [InlineData("block found, re-aligned")]
    public void Reason_ReturnsNullForNonHolds(string? status)
        => Assert.Null(HoldCopy.Reason(status));
}
