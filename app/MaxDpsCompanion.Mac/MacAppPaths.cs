namespace MaxDpsCompanion.Mac;

// Mac app-directory layout: settings.ini, probe.txt and caches live in
// ~/Library/Application Support/MaxDpsCompanion/ (the tracked settings.ini
// seed is copied there on first run; per-machine edits are never committed).
// Core counterpart of the Windows next-to-exe layout.

internal sealed class MacAppPaths : IAppPaths
{
    public string AppDir { get; }

    public MacAppPaths()
    {
        AppDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library/Application Support/MaxDpsCompanion");
    }

    public string SettingsPath => Path.Combine(AppDir, "settings.ini");

    public string AssetDir => Path.Combine(AppDir, "assets");
}
