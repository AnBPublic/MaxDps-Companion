using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 4.5.6 toggle copy: Details carries the live addon-wins summary, and the
/// companion log writes one line only when a toggle is switched OFF in the
/// in-game addon. No in-game chat line, no ON/first-sight/mirror-loss lines.
/// </summary>
public class ToggleStateCopyTests
{
    private const int AllOn = (1 << ToggleSync.MaskBitCount) - 1;

    [Fact]
    public void Summary_AllOn()
        => Assert.Equal("Main ON · all others ON", ToggleStateCopy.Summary(true, AllOn));

    [Fact]
    public void Summary_MainOff()
        => Assert.StartsWith("Main OFF (master switch)",
            ToggleStateCopy.Summary(true, AllOn & ~(1 << ToggleSync.BitMain)));

    [Fact]
    public void Summary_MainOff_ListsOtherOffBits()
    {
        var mask = AllOn & ~(1 << ToggleSync.BitMain) & ~(1 << ToggleSync.BitTrinket);
        var text = ToggleStateCopy.Summary(true, mask);
        Assert.StartsWith("Main OFF (master switch)", text);
        Assert.Contains("Trinket", text);
        Assert.DoesNotContain("Main,", text);
    }

    [Fact]
    public void Summary_MainOn_ListsOffBits()
    {
        var mask = AllOn & ~(1 << ToggleSync.BitTrinket) & ~(1 << ToggleSync.BitOutOfCombat);
        Assert.Equal("Main ON · OFF: Trinket, OOC", ToggleStateCopy.Summary(true, mask));
    }

    [Fact]
    public void Summary_NoMirror()
        => Assert.Equal("addon not reporting (no toggle mirror)", ToggleStateCopy.Summary(false, 0));

    [Fact]
    public void OffDelta_FirstValidMirror_IsSilent()
        => Assert.Null(ToggleStateCopy.OffDelta(false, 0, true, AllOn));

    [Fact]
    public void OffDelta_NoChange_IsSilent()
        => Assert.Null(ToggleStateCopy.OffDelta(true, AllOn, true, AllOn));

    [Fact]
    public void OffDelta_MainOff_NamesTheMasterSwitchAndTheFix()
    {
        var text = ToggleStateCopy.OffDelta(true, AllOn, true, AllOn & ~(1 << ToggleSync.BitMain));
        Assert.NotNull(text);
        Assert.StartsWith("Main switched OFF in the in-game addon - rotation suspended", text);
        Assert.Contains("/mcb t Main on", text);
    }

    [Fact]
    public void OffDelta_MainOn_IsSilent()
        => Assert.Null(ToggleStateCopy.OffDelta(
            true, AllOn & ~(1 << ToggleSync.BitMain), true, AllOn));

    [Fact]
    public void OffDelta_OtherOff_NamesTheToggle()
        => Assert.Equal("Trinket switched OFF in the in-game addon",
            ToggleStateCopy.OffDelta(true, AllOn, true, AllOn & ~(1 << ToggleSync.BitTrinket)));

    [Fact]
    public void OffDelta_OtherOn_IsSilent()
        => Assert.Null(ToggleStateCopy.OffDelta(
            true, AllOn & ~(1 << ToggleSync.BitTrinket), true, AllOn));

    [Fact]
    public void OffDelta_MultipleOff_IsOneLine()
        => Assert.Equal("Defensive, Trinket switched OFF in the in-game addon",
            ToggleStateCopy.OffDelta(true, AllOn, true,
                AllOn & ~(1 << ToggleSync.BitDefensive) & ~(1 << ToggleSync.BitTrinket)));

    [Fact]
    public void OffDelta_MainOff_AlsoListsOtherOffBits()
    {
        var text = ToggleStateCopy.OffDelta(true, AllOn, true,
            AllOn & ~(1 << ToggleSync.BitMain) & ~(1 << ToggleSync.BitTrinket));
        Assert.NotNull(text);
        Assert.Contains("also off: Trinket", text);
    }

    [Fact]
    public void OffDelta_StillNoMirror_IsSilent()
        => Assert.Null(ToggleStateCopy.OffDelta(false, 0, false, 0));

    [Fact]
    public void OffDelta_MirrorLost_IsSilent()
        => Assert.Null(ToggleStateCopy.OffDelta(true, AllOn, false, AllOn));
}