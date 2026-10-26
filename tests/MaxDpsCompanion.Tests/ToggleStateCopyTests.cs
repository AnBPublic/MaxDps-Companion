using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 4.5.3 toggle copy: the companion names the addon-wins toggle state in the
/// log and in Details, so a user whose in-game Main pill was OFF sees
/// "Main OFF - rotation suspended" instead of "no presses".
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
    public void Delta_FirstValidMirrorPrintsTheSummary()
        => Assert.Equal("Main ON · all others ON", ToggleStateCopy.Delta(false, 0, true, AllOn));

    [Fact]
    public void Delta_NoChange_IsSilent()
        => Assert.Null(ToggleStateCopy.Delta(true, AllOn, true, AllOn));

    [Fact]
    public void Delta_MainOff_NamesTheMasterSwitchAndTheFix()
    {
        var text = ToggleStateCopy.Delta(true, AllOn, true, AllOn & ~(1 << ToggleSync.BitMain));
        Assert.NotNull(text);
        Assert.StartsWith("Main OFF - rotation suspended", text);
        Assert.Contains("/mcb t Main on", text);
    }

    [Fact]
    public void Delta_MainOn_Resumes()
        => Assert.Equal("Main ON - rotation resumed",
            ToggleStateCopy.Delta(true, AllOn & ~(1 << ToggleSync.BitMain), true, AllOn));

    [Fact]
    public void Delta_OtherBits_AreListed()
        => Assert.Equal("Defensive ON, Trinket OFF",
            ToggleStateCopy.Delta(true, AllOn & ~(1 << ToggleSync.BitDefensive),
                true, AllOn & ~(1 << ToggleSync.BitTrinket)));

    [Fact]
    public void Delta_StillNoMirror_IsSilent()
        => Assert.Null(ToggleStateCopy.Delta(false, 0, false, 0));

    [Fact]
    public void Delta_MirrorLost_IsReported()
        => Assert.Equal("addon stopped reporting toggles", ToggleStateCopy.Delta(true, AllOn, false, AllOn));
}
