namespace MaxDpsCompanion;

internal enum DoctorSeverity
{
    Ok = 0,
    Info,
    Warn,
    Fail,
}

internal readonly record struct DoctorFinding(string Check, DoctorSeverity Severity, string Detail);

/// <summary>
/// Plain inputs for the install doctor. Every value is observed, never derived:
/// the build commit embedded in the exe, the repo HEAD (when a checkout is
/// reachable), the companion/addon VERSION.txt lines, the configured cell size
/// vs the one the addon actually emits, and the app mask vs the Ext3 mirror.
/// </summary>
internal sealed record DoctorInputs(
    string? ExeCommit,
    string? HeadCommit,
    string? AppVersion,
    string? AddonVersion,
    int SettingsCellSize,
    int? LocatedCellSize,
    int AppMask,
    int? MirrorMask,
    bool MirrorValid);

/// <summary>
/// S8 install-doctor: pure checks that catch the stale-install class of failure
/// (the v2.5 "old exe / old addon / wrong cell size" outage) before a live test.
/// Warn-only where the data is merely missing; hard <see cref="DoctorSeverity.Fail"/>
/// only where the companion and the addon provably disagree (cell size).
/// </summary>
internal static class InstallDoctor
{
    public static IReadOnlyList<DoctorFinding> Audit(DoctorInputs inputs)
    {
        var findings = new List<DoctorFinding>(6);

        // 1. exe build commit vs the repo HEAD it was checked out from.
        if (string.IsNullOrEmpty(inputs.HeadCommit))
            findings.Add(new("Exe vs HEAD", DoctorSeverity.Info, "Repo HEAD unavailable; skipped."));
        else if (string.IsNullOrEmpty(inputs.ExeCommit))
            findings.Add(new("Exe vs HEAD", DoctorSeverity.Warn, $"Exe build commit unknown; repo HEAD {Short(inputs.HeadCommit!)}."));
        else if (!CommitEqual(inputs.ExeCommit!, inputs.HeadCommit!))
            findings.Add(new("Exe vs HEAD", DoctorSeverity.Warn,
                $"Exe was built from {Short(inputs.ExeCommit!)} but HEAD is {Short(inputs.HeadCommit!)} — rebuild the exe."));
        else
            findings.Add(new("Exe vs HEAD", DoctorSeverity.Ok, $"Exe matches HEAD ({Short(inputs.HeadCommit!)})."));

        // 2. configured cell size vs the size the addon actually emits.
        if (inputs.LocatedCellSize is not int located || located <= 0)
        {
            findings.Add(new("Cell size", DoctorSeverity.Info, "Addon cell size not reported; skipped."));
        }
        else if (located != inputs.SettingsCellSize)
        {
            findings.Add(new("Cell size", DoctorSeverity.Fail,
                $"settings CellSize={inputs.SettingsCellSize} but the addon emits {located} px — fix [Bridge] CellSize or run /mdb cellsize {inputs.SettingsCellSize}."));
        }
        else
        {
            findings.Add(new("Cell size", DoctorSeverity.Ok, $"{inputs.SettingsCellSize} px matches the addon."));
        }

        // 3. app toggle mask vs the bridge's Ext3 echo.
        if (!inputs.MirrorValid || inputs.MirrorMask is null)
        {
            findings.Add(new("Toggle mirror", DoctorSeverity.Info, "No valid Ext3 mirror yet; run the engine out of combat."));
        }
        else
        {
            var app = inputs.AppMask & ToggleSync.MaskField;
            var mirror = inputs.MirrorMask.Value & ToggleSync.MaskField;
            if (app != mirror)
                findings.Add(new("Toggle mirror", DoctorSeverity.Warn,
                    $"App mask {Hex(app)} != mirror {Hex(mirror)} (differing: {Bits(app ^ mirror)}) — the addon may have toggles OFF."));
            else
                findings.Add(new("Toggle mirror", DoctorSeverity.Ok, $"App and mirror agree ({Hex(app)})."));
        }

        // 4. installed addon version vs the companion release.
        if (string.IsNullOrEmpty(inputs.AddonVersion))
        {
            findings.Add(new("Addon version", DoctorSeverity.Info, "Addon VERSION.txt not found; skipped."));
        }
        else if (!TryVersion(inputs.AddonVersion!, out var addon))
        {
            findings.Add(new("Addon version", DoctorSeverity.Warn, $"Addon version '{inputs.AddonVersion}' is not parseable."));
        }
        else if (TryVersion(inputs.AppVersion, out var appVersion)
            && (addon.Major != appVersion.Major || addon.Minor != appVersion.Minor))
        {
            findings.Add(new("Addon version", DoctorSeverity.Warn,
                $"Addon {inputs.AddonVersion} vs companion {inputs.AppVersion}. {BridgeHealth.RepairHint}"));
        }
        else
        {
            findings.Add(new("Addon version", DoctorSeverity.Ok, $"Addon {inputs.AddonVersion} matches the companion."));
        }

        return findings;
    }

    /// <summary>One-line roll-up for the UI banner.</summary>
    public static string Summarize(IReadOnlyList<DoctorFinding> findings)
    {
        if (findings.Count == 0) return "Install doctor: no checks ran.";
        var worst = findings.Max(f => f.Severity);
        var issues = findings.Count(f => f.Severity is DoctorSeverity.Warn or DoctorSeverity.Fail);
        return issues == 0
            ? $"Install doctor: OK ({findings.Count} checks)."
            : $"Install doctor: {worst} — {issues} issue(s).";
    }

    /// <summary>Severity tone for the UI (design-token friendly).</summary>
    public static StatusTone ToneOf(IReadOnlyList<DoctorFinding> findings)
    {
        if (findings.Count == 0) return StatusTone.Info;
        return findings.Max(f => f.Severity) switch
        {
            DoctorSeverity.Fail => StatusTone.Warning,
            DoctorSeverity.Warn => StatusTone.Warning,
            DoctorSeverity.Ok => StatusTone.Success,
            _ => StatusTone.Info,
        };
    }

    // ---- file helpers (best-effort, never throw) ----

    /// <summary>First non-empty line of a VERSION.txt-style file, or null.</summary>
    public static string? TryReadVersionFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0) return trimmed;
            }
        }
        catch { /* diagnostics must never throw */ }
        return null;
    }

    /// <summary>
    /// Resolves the git HEAD sha from a checkout reachable from
    /// <paramref name="startDirectory"/> (walks up to <c>.git/HEAD</c>), or null.
    /// Supports both the symbolic ref and detached-HEAD forms.
    /// </summary>
    public static string? TryReadHeadCommit(string? startDirectory)
    {
        try
        {
            var dir = startDirectory;
            for (var depth = 0; depth < 8 && !string.IsNullOrEmpty(dir); depth++)
            {
                var git = Path.Combine(dir!, ".git");
                if (Directory.Exists(git))
                {
                    var headPath = Path.Combine(git, "HEAD");
                    if (!File.Exists(headPath)) return null;
                    var head = File.ReadAllText(headPath).Trim();
                    const string refPrefix = "ref:";
                    if (head.StartsWith(refPrefix, StringComparison.Ordinal))
                    {
                        var refPath = head[refPrefix.Length..].Trim().Replace('/', Path.DirectorySeparatorChar);
                        var resolved = Path.Combine(git, refPath);
                        if (File.Exists(resolved)) return File.ReadAllText(resolved).Trim();
                        // Packed refs fallback.
                        var packed = Path.Combine(git, "packed-refs");
                        if (File.Exists(packed))
                        {
                            foreach (var line in File.ReadLines(packed))
                            {
                                if (line.StartsWith('#') || line.Trim().Length == 0) continue;
                                var parts = line.Split(' ', 2);
                                if (parts.Length == 2 && parts[1].Trim() == head[refPrefix.Length..].Trim())
                                    return parts[0].Trim();
                            }
                        }
                        return null;
                    }
                    return head.Length >= 7 ? head : null; // detached HEAD
                }
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch { /* diagnostics must never throw */ }
        return null;
    }

    // ---- formatting ----

    private static bool CommitEqual(string a, string b) =>
        string.Equals(Short(a), Short(b), StringComparison.OrdinalIgnoreCase);

    private static string Short(string commit) => commit.Length <= 7 ? commit : commit[..7];

    private static bool TryVersion(string? text, out Version version)
    {
        version = new Version(0, 0);
        return !string.IsNullOrWhiteSpace(text) && Version.TryParse(text.Trim(), out version!);
    }

    private static string Hex(int mask) => $"0x{mask & ToggleSync.MaskField:X4}";

    private static readonly (int Bit, string Name)[] MaskBits =
    [
        (ToggleSync.BitMain, "Main"),
        (ToggleSync.BitOffensive, "Offensive"),
        (ToggleSync.BitDefensive, "Defensive"),
        (ToggleSync.BitConsumable, "Consumable"),
        (ToggleSync.BitTrinket, "Trinket"),
        (ToggleSync.BitInterrupt, "Interrupt"),
        (ToggleSync.BitMobility, "Mobility"),
        (ToggleSync.BitSelfHeal, "SelfHeal"),
        (ToggleSync.BitSolo, "Solo"),
        (ToggleSync.BitOutOfCombat, "OOC"),
        (ToggleSync.BitAutoTarget, "AutoTarget"),
        (ToggleSync.BitAutoInteract, "AutoInteract"),
        (ToggleSync.BitTimeToKill, "TTK"),
        (ToggleSync.BitCrowdControl, "CrowdControl"),
    ];

    private static string Bits(int diff)
    {
        var names = new List<string>(4);
        foreach (var (bit, name) in MaskBits)
            if ((diff & (1 << bit)) != 0) names.Add(name);
        return names.Count == 0 ? "none" : string.Join(", ", names);
    }
}
