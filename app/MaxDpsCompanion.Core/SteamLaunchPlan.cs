using System.Diagnostics;
using System.Globalization;

namespace MaxDpsCompanion;

internal static class SteamLaunchPlan
{
    internal static string EnableNativeGamepadText(string config)
    {
        const string setting = "SET GamePadEnable \"1\"";
        var pattern = new System.Text.RegularExpressions.Regex("(?m)^SET GamePadEnable [^\r\n]*");
        return pattern.IsMatch(config) ? pattern.Replace(config, setting) : config + (config.EndsWith("\n", StringComparison.Ordinal) ? "" : "\r\n") + setting + "\r\n";
    }

    internal static bool IsSupportedGameVersion(string? text) => Version.TryParse(text, out var version) && version.Major == 12 && version.Minor == 1;

    internal static bool IsShortcutId(string? id) => ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && (uint)value == 0x02000000 && (value >> 32) >= 0x80000000;

    internal static ProcessStartInfo CreateStartInfo(string id)
    {
        if (!IsShortcutId(id)) throw new ArgumentException("Configure a valid non-Steam game shortcut ID.", nameof(id));
        return new ProcessStartInfo { FileName = $"steam://rungameid/{id}", UseShellExecute = true };
    }

    internal static bool IsExpectedSession(string expected, string? actual) =>
        IsShortcutId(expected) && string.Equals(expected, actual, StringComparison.Ordinal);
}
