namespace MaxDpsCompanion;

internal sealed class SteamColdStartRetry(long requestedAt)
{
    private bool _done;
    internal bool TryTake(long now, bool gameRunning, bool battleNetRunning)
    {
        if (gameRunning) _done = true;
        if (_done || !battleNetRunning || now - requestedAt < 15_000) return false;
        _done = true;
        return true;
    }
}
