using Xunit;

namespace MaxDpsCompanion.Tests;

public class SteamLaunchPlanTests
{
    [Fact] public void NativeGamepadSetupPreservesOtherGameSettingsAndIsIdempotent()
    {
        const string input = "SET gxApi \"D3D12\"\r\nSET GamePadEnable \"0\"\r\nSET Sound_EnableAllSound \"1\"\r\n";
        var updated = SteamLaunchPlan.EnableNativeGamepadText(input);
        Assert.Equal(input.Replace("GamePadEnable \"0\"", "GamePadEnable \"1\""), updated);
        Assert.Equal(updated, SteamLaunchPlan.EnableNativeGamepadText(updated));
    }
    [Fact] public void MissingNativeGamepadSettingIsAddedWithoutChangingExistingText()
    {
        const string input = "SET gxApi \"D3D12\"";
        Assert.Equal(input + "\r\nSET GamePadEnable \"1\"\r\n", SteamLaunchPlan.EnableNativeGamepadText(input));
    }

    [Theory]
    [InlineData("1.60.1.70790", false)]
    [InlineData("12.1.5.70783", true)]
    [InlineData("160.1.70790", false)]
    public void UsesTextualGameVersionInsteadOfPackedPeVersion(string text, bool accepted) =>
        Assert.Equal(accepted, SteamLaunchPlan.IsSupportedGameVersion(text));

    [Fact] public void UsesExactSteamShortcutWithoutBattleNetOrGameArguments()
    {
        var start = SteamLaunchPlan.CreateStartInfo("13106813115821981696");
        Assert.Equal("steam://rungameid/13106813115821981696", start.FileName);
        Assert.True(start.UseShellExecute);
        Assert.Equal("", start.Arguments);
    }
    [Theory]
    [InlineData("")] [InlineData("WoW")] [InlineData("123")] [InlineData("13106813115821981696/other")]
    public void RejectsMalformedOrNonShortcutIds(string id) =>
        Assert.Throws<ArgumentException>(() => SteamLaunchPlan.CreateStartInfo(id));
    [Fact] public void HostAcceptsOnlyItsConfiguredSteamGameSession()
    {
        Assert.True(SteamLaunchPlan.IsExpectedSession("13106813115821981696", "13106813115821981696"));
        Assert.False(SteamLaunchPlan.IsExpectedSession("13106813115821981696", "14521189564866887680"));
        Assert.False(SteamLaunchPlan.IsExpectedSession("", ""));
    }
}
