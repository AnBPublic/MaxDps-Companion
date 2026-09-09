using System.Diagnostics;

namespace MaxDpsCompanion;

/// <summary>
/// Locates and launches the Battle.net desktop app for the Launch Game button.
/// Resolution order: settings BNetPath override, WoW registry InstallPath
/// (sibling Battle.net folder), well-known install locations. The game is
/// started through the registered battlenet://WoW/ handler when available,
/// otherwise Battle.net.exe --exec='launch WoW' minimized. Login always relies
/// on the Battle.net remembered account auto-login - credentials are never
/// stored, read, or passed anywhere in this companion.
/// </summary>
internal static class BattleNetLauncher
{
    private const string WowInstallKeyWow64 =
        @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft";
    private const string WowInstallKey =
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Blizzard Entertainment\World of Warcraft";

    private static readonly string[] FallbackExePaths =
    [
        @"C:\Program Files (x86)\Battle.net\Battle.net.exe",
        @"C:\Program Files\Battle.net\Battle.net.exe",
    ];

    // Retail/PTR/beta client folder leaves; stripped to reach the WoW root,
    // whose parent normally also holds the Battle.net folder.
    private static readonly string[] ClientFolderLeaves =
    [
        "_retail_", "_ptr_", "_beta_", "_classic_", "_classic_era_",
        "_classic_ptr_", "_xptr_",
    ];

    // Retail game UID as the Battle.net client knows it (Classic would be
    // "WoWC"). Passed to --exec exactly like the client's own Play button.
    private const string GameUid = "WoW";

    /// <summary>Auto-detected Battle.net.exe path, or null when not found.</summary>
    public static string? InstallPath => Detect();

    public static bool IsInstalled => InstallPath is not null;

    private static bool ClientRunning()
    {
        try
        {
            return Process.GetProcessesByName("Battle.net").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Launches the game. Returns false with a human-readable message when
    /// Battle.net cannot be found or started.
    /// </summary>
    public static bool TryLaunch(string? overridePath, out string message)
    {
        var exe = !string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath!.Trim())
            ? overridePath!.Trim()
            : InstallPath;

        // Primary path: the same method as the client's own Play button.
        // --exec is handled by the running client (or starts it), which
        // launches WoW with its SSO ticket — the remembered account signs
        // in automatically, manual login is skipped, and the BNet UI is
        // not raised. This never touches credentials: none are stored,
        // read, or passed anywhere here.
        if (exe is not null)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"--exec=\"launch {GameUid}\"",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Minimized,
                });
                message = ClientRunning()
                    ? $"Asked Battle.net to launch {GameUid} (remembered account signs in via SSO)."
                    : $"Starting Battle.net ({exe}) to launch {GameUid} (remembered account signs in via SSO; first start may take a moment).";
                return true;
            }
            catch
            {
                // Fall through to the protocol-handler fallback below.
            }
        }

        // Fallback: the registered battlenet:// handler needs no path and
        // opens the client on the WoW page; the user then presses Play.
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"battlenet://{GameUid}/",
                UseShellExecute = true,
            });
            message = exe is null
                ? $"Opening Battle.net for {GameUid} (press Play; remembered account signs in)."
                : $"Opening Battle.net for {GameUid} ({exe}; press Play if the game does not start).";
            return true;
        }
        catch (Exception ex)
        {
            message = exe is null
                ? "Battle.net not found. Install it, or set the path under Advanced (Battle.net)."
                : $"Could not start Battle.net ({ex.Message}).";
            return false;
        }
    }

    private static string? Detect()
    {
        foreach (var candidate in RegistryCandidates())
            if (File.Exists(candidate)) return candidate;
        foreach (var fallback in FallbackExePaths)
            if (File.Exists(fallback)) return fallback;
        return null;
    }

    private static IEnumerable<string> RegistryCandidates()
    {
        var installPath = ReadWowInstallPath();
        if (string.IsNullOrWhiteSpace(installPath)) yield break;

        var dir = installPath!.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // Strip client leaves (_retail_ etc.) to reach the WoW root.
        while (ClientFolderLeaves.Contains(Path.GetFileName(dir), StringComparer.OrdinalIgnoreCase))
            dir = Path.GetDirectoryName(dir) ?? dir;

        var parent = Path.GetDirectoryName(dir);
        if (!string.IsNullOrEmpty(parent))
            yield return Path.Combine(parent, "Battle.net", "Battle.net.exe");
        yield return Path.Combine(dir, "Battle.net", "Battle.net.exe");
    }

    private static string? ReadWowInstallPath()
    {
        try
        {
            foreach (var key in new[] { WowInstallKeyWow64, WowInstallKey })
            {
                var value = Microsoft.Win32.Registry.GetValue(key, "InstallPath", null) as string;
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        catch
        {
            // Registry unreadable (permissions): fall back to well-known paths.
        }
        return null;
    }
}
