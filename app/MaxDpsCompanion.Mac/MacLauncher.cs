using System.Diagnostics;

namespace MaxDpsCompanion.Mac;

// Mac Battle.net launcher. Mirrors the Windows BattleNetLauncher: the primary
// path is Battle.net's own "--exec=launch WoW" (the same call the client's
// Play button makes, so the remembered account signs in — no credentials are
// ever stored, read or passed), with the registered battlenet:// handler as
// the fallback. Directly opening the game bundle is deliberately NOT used:
// that skips the launcher's SSO ticket and would prompt for a login.
//
// Product code "WoW" (retail) is the same UID the Windows launcher passes;
// the client accepts it case-insensitively (the install's .build.info
// Product field is "wow").

internal static class MacLauncher
{
    private const string GameUid = "WoW";

    /// <summary>Battle.net.app locations, most specific first.</summary>
    private static readonly string[] Candidates =
    [
        "/Applications/Battle.net.app",
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Applications/Battle.net.app"),
        "/Applications/World of Warcraft/Battle.net.app",
    ];

    public static string? InstallPath => Candidates.FirstOrDefault(Directory.Exists);

    public static bool IsInstalled => InstallPath is not null;

    /// <summary>The launcher binary inside the .app bundle.</summary>
    private static string? LauncherBinary(string appBundle) =>
        Path.Combine(appBundle, "Contents", "MacOS", "Battle.net") is var bin
        && File.Exists(bin) ? bin : null;

    public static bool TryLaunch(string? overridePath, out string message)
    {
        var app = !string.IsNullOrWhiteSpace(overridePath) && Directory.Exists(overridePath!.Trim())
            ? overridePath!.Trim()
            : InstallPath;

        // (1) PRIMARY: the same call the client's own Play button makes. The
        // running (or freshly started) client resolves the SSO ticket and
        // launches the game directly — no manual Play press needed.
        if (app is not null && LauncherBinary(app) is { } bin)
        {
            try
            {
                var psi = new ProcessStartInfo(bin) { UseShellExecute = false };
                psi.ArgumentList.Add($"--exec=launch {GameUid}");
                using var p = Process.Start(psi);
                message = $"Asked Battle.net to launch {GameUid} (remembered account signs in via SSO).";
                return true;
            }
            catch { /* fall through to the protocol handler */ }
        }

        // (2) FALLBACK: the registered battlenet:// handler opens the client on
        // the WoW page; the user then presses Play.
        try
        {
            var psi = new ProcessStartInfo("open") { UseShellExecute = false };
            psi.ArgumentList.Add($"battlenet://{GameUid}/");
            using var p = Process.Start(psi);
            message = app is null
                ? $"Opening Battle.net for {GameUid} (press Play if the game does not start)."
                : $"Opening Battle.net for {GameUid} ({app}; press Play if the game does not start).";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Could not start Battle.net ({ex.Message}). Install it, or set the path in settings.";
            return false;
        }
    }
}
