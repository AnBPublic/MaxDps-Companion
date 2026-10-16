using System.Reflection;

namespace MaxDpsCompanion;

// Portable product-version helper: the AppVersion/Codename/DisplayVersion/
// BuildVersion logic copied from Native.cs minus the P/Invoke. Must NOT
// reference Native — Core compiles standalone on any OS.
//
// Single-sourcing is preserved: AppVersion comes from the assembly identity
// (csproj <Version>) and Codename from the AssemblyMetadata "Codename"
// attribute (csproj <Codename>), so the visible title can never drift from
// the build the way the old hardcoded string did.

/// <summary>Single-sourced product version, codename and build stamp.</summary>
internal static class AppVersionInfo
{
    /// <summary>
    /// Product version from the assembly identity. 4-part display when a
    /// patch revision is set (e.g. 3.5.2.2); the always-zero Revision is
    /// still dropped for the classic 3-part display (1.3.4).
    /// </summary>
    public static string AppVersion
    {
        get
        {
            var v = typeof(AppVersionInfo).Assembly.GetName().Version;
            if (v is null) return "dev";
            return v.Revision > 0
                ? $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}"
                : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>
    /// Release codename from the AssemblyMetadata "Codename" attribute.
    /// Falls back to "Unyielding" when the attribute is absent, so the title
    /// can never render a bare "v3.7.9 ".
    /// </summary>
    public static string Codename
    {
        get
        {
            var attr = typeof(AppVersionInfo).Assembly
                .GetCustomAttributes(typeof(AssemblyMetadataAttribute), inherit: false)
                .OfType<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => string.Equals(a.Key, "Codename", StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(attr?.Value) ? CodenameFallback : attr!.Value;
        }
    }

    private const string CodenameFallback = "Unyielding";

    /// <summary>
    /// Full display form used by the title bar: "v3.7.9 Unyielding". Never
    /// contains a build hash/time — those live in <see cref="BuildVersion"/>.
    /// </summary>
    public static string DisplayVersion => $"v{AppVersion} {Codename}";

    /// <summary>
    /// Build stamp from the generated ThisAssemblyGen (commit + time, written
    /// by build.ps1), or "dev build" when the type is absent (plain
    /// `dotnet build`) or the commit is "dev". Probed by reflection so Core
    /// has no hard dependency on the generated file.
    /// </summary>
    public static string BuildVersion
    {
        get
        {
            var commit = ReadStamp("GitCommit");
            if (string.IsNullOrWhiteSpace(commit) || commit == "dev") return "dev build";
            var stamp = ReadStamp("BuildTime");
            return string.IsNullOrWhiteSpace(stamp) ? commit : $"{commit} ({stamp})";
        }
    }

    private static string? ReadStamp(string name)
    {
        try
        {
            var field = typeof(AppVersionInfo).Assembly
                .GetType("MaxDpsCompanion.ThisAssemblyGen")
                ?.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return field?.GetRawConstantValue() as string ?? field?.GetValue(null) as string;
        }
        catch
        {
            return null;
        }
    }
}
