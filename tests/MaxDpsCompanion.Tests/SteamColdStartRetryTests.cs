using Xunit;
namespace MaxDpsCompanion.Tests;
public class SteamColdStartRetryTests
{
    [Fact] public void RetriesOnlyOnceAfterTheLauncherHasHadTimeToInitialize()
    {
        var retry = new SteamColdStartRetry(100);
        Assert.False(retry.TryTake(15099, false, true));
        Assert.False(retry.TryTake(15100, false, false));
        Assert.True(retry.TryTake(15100, false, true));
        Assert.False(retry.TryTake(40000, false, true));
    }
    [Fact] public void AnObservedGamePermanentlyCancelsTheColdRetrySoClosingItCannotRelaunchIt()
    {
        var retry = new SteamColdStartRetry(0);
        Assert.False(retry.TryTake(5000, true, true));
        Assert.False(retry.TryTake(40000, false, true));
    }
}
