using System.Globalization;
using System.Text;

namespace MaxDpsCompanion;

/// <summary>
/// INI-backed settings, kept hand-editable in the same shape as settings.ini.
/// </summary>
internal sealed class AppSettings
{
    public string Path { get; private set; } = "settings.ini";

    // [Bridge] — must match the addon's /mdb offset and /mdb cellsize.
    // Aethys-proven default 8: BlockLocator measures the real size off the
    // screen, so a stale default self-corrects on first Start.
    public int CellSize { get; set; } = 8;
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }

    // [Window]
    public string ProcessName { get; set; } = "Wow";
    public bool RequireForeground { get; set; } = true;

    // [Window] — background play: keyboard slots are posted to the game window
    // and work while it is in the background; mouse slots still need focus.
    public bool AllowBackgroundKeys { get; set; } = true;

    // [Pause] — global toggle hotkey, parsed as a System.Windows.Forms.Keys name.
    public string PauseHotkey { get; set; } = "Pause";

    // [Spells] — which MaxDPS icons the helper is allowed to press.
    // spell1=Main, spell2=Cooldown, spell3=Interrupt, spell4=Defensive, spell5=Consumable.
    public bool[] SlotEnabled { get; } = [true, true, true, true, false];

    // [Timing]
    public int PollIntervalMs { get; set; } = 50;
    public int MinKeyIntervalMs { get; set; } = 120;
    public int KeyPressMs { get; set; } = 25;

    // [Targeting] — Tab fallback via the user's own TargetKey when the
    // bridge reports need-target. Kill-switch default OFF. The OFF/COMBAT/
    // ALL mode lives on the in-game T button; CombatOnly narrows the
    // companion side too (hold after a long stretch without Active).
    public bool AutoTargetEnabled { get; set; } = false;
    public bool CombatOnly { get; set; } = true;
    public string TargetKey { get; set; } = "Tab";

    // [Interact] — interact-key fallback when the bridge reports need-interact
    // (state 4). Kill-switch default OFF; default key F (retail interact).
    public bool InteractEnabled { get; set; } = false;
    public string InteractKey { get; set; } = "F";

    // [Color] — display-chain profile learned from `/mdb calibrate`.
    // Absent section = legacy behaviour (fixed magenta test, shared ramp).
    public ColorProfile Color { get; } = new();

    // [Launch] — Battle.net override for the Launch Game button.
    // Empty = auto-detect. The launcher's remembered account signs in;
    // no credentials are stored anywhere.
    public string BNetPath { get; set; } = "";

    public static AppSettings Load(string path)
    {
        var settings = new AppSettings { Path = path };
        if (!File.Exists(path)) return settings;

        var section = "";
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim().ToLowerInvariant();
                continue;
            }

            var split = line.IndexOf('=');
            if (split <= 0) continue;

            var key = line[..split].Trim().ToLowerInvariant();
            var value = line[(split + 1)..].Trim();
            settings.Apply(section, key, value);
        }

        return settings;
    }

    private void Apply(string section, string key, string value)
    {
        switch (section, key)
        {
            case ("bridge", "cellsize"): CellSize = ParseInt(value, CellSize); break;
            case ("bridge", "offsetx"): OffsetX = ParseInt(value, OffsetX); break;
            case ("bridge", "offsety"): OffsetY = ParseInt(value, OffsetY); break;
            case ("window", "processname"): ProcessName = value; break;
            case ("window", "requireforeground"): RequireForeground = ParseBool(value, RequireForeground); break;
            case ("window", "allowbackgroundkeys"): AllowBackgroundKeys = ParseBool(value, AllowBackgroundKeys); break;
            case ("pause", "button"): PauseHotkey = value; break;
            case ("spells", "spell1"): SlotEnabled[0] = ParseBool(value, SlotEnabled[0]); break;
            case ("spells", "spell2"): SlotEnabled[1] = ParseBool(value, SlotEnabled[1]); break;
            case ("spells", "spell3"): SlotEnabled[2] = ParseBool(value, SlotEnabled[2]); break;
            case ("spells", "spell4"): SlotEnabled[3] = ParseBool(value, SlotEnabled[3]); break;
            case ("spells", "spell5"): SlotEnabled[4] = ParseBool(value, SlotEnabled[4]); break;
            case ("timing", "pollintervalms"): PollIntervalMs = ParseInt(value, PollIntervalMs); break;
            case ("timing", "minkeyintervalms"): MinKeyIntervalMs = ParseInt(value, MinKeyIntervalMs); break;
            case ("timing", "keypressms"): KeyPressMs = ParseInt(value, KeyPressMs); break;
            case ("targeting", "autotargetenabled"): AutoTargetEnabled = ParseBool(value, AutoTargetEnabled); break;
            case ("targeting", "combatonly"): CombatOnly = ParseBool(value, CombatOnly); break;
            case ("targeting", "targetkey"): TargetKey = string.IsNullOrWhiteSpace(value) ? TargetKey : value.Trim(); break;
            case ("interact", "interactenabled"): InteractEnabled = ParseBool(value, InteractEnabled); break;
            case ("interact", "interactkey"): InteractKey = string.IsNullOrWhiteSpace(value) ? InteractKey : value.Trim(); break;
            case ("color", "magic"): ParseTriple(value, Color.MagicR, Color.MagicG, Color.MagicB, out var mr, out var mg, out var mb); Color.MagicR = mr; Color.MagicG = mg; Color.MagicB = mb; break;
            case ("color", "black"): ParseTriple(value, 0, 0, 0, out var br, out var bg, out var bb); Color.Black[0] = br; Color.Black[1] = bg; Color.Black[2] = bb; break;
            case ("color", "white"): ParseTriple(value, 255, 255, 255, out var wr, out var wg, out var wb); Color.White[0] = wr; Color.White[1] = wg; Color.White[2] = wb; break;
            case ("color", "tolerance"): Color.Tolerance = ParseInt(value, Color.Tolerance); break;
            case ("color", "learnedat"): Color.LearnedAt = value.Trim(); break;
            case ("launch", "bnetpath"): BNetPath = value.Trim(); break;
        }
    }

    public void Save()
    {
        var text = new StringBuilder()
            .AppendLine("; MaxDPS Companion settings.")
            .AppendLine("; CellSize/OffsetX/OffsetY must match the addon's '/mdb status' output.")
            .AppendLine()
            .AppendLine("[Bridge]")
            .AppendLine($"CellSize={CellSize}")
            .AppendLine($"OffsetX={OffsetX}")
            .AppendLine($"OffsetY={OffsetY}")
            .AppendLine()
            .AppendLine("[Window]")
            .AppendLine($"ProcessName={ProcessName}")
            .AppendLine($"RequireForeground={(RequireForeground ? 1 : 0)}")
            .AppendLine($"AllowBackgroundKeys={(AllowBackgroundKeys ? 1 : 0)}")
            .AppendLine()
            .AppendLine("[Pause]")
            .AppendLine($"Button={PauseHotkey}")
            .AppendLine()
            .AppendLine("; spell1=Main, spell2=Cooldown, spell3=Interrupt, spell4=Defensive, spell5=Consumable")
            .AppendLine("[Spells]")
            .AppendLine($"spell1={(SlotEnabled[0] ? 1 : 0)}")
            .AppendLine($"spell2={(SlotEnabled[1] ? 1 : 0)}")
            .AppendLine($"spell3={(SlotEnabled[2] ? 1 : 0)}")
            .AppendLine($"spell4={(SlotEnabled[3] ? 1 : 0)}")
            .AppendLine($"spell5={(SlotEnabled[4] ? 1 : 0)}")
            .AppendLine()
            .AppendLine("[Timing]")
            .AppendLine($"PollIntervalMs={PollIntervalMs}")
            .AppendLine($"MinKeyIntervalMs={MinKeyIntervalMs}")
            .AppendLine($"KeyPressMs={KeyPressMs}")
            .AppendLine()
            .AppendLine("; TargetKey is pressed when the bridge reports need-target and AutoTarget is on.")
            .AppendLine("[Targeting]")
            .AppendLine($"AutoTargetEnabled={(AutoTargetEnabled ? 1 : 0)}")
            .AppendLine($"CombatOnly={(CombatOnly ? 1 : 0)}")
            .AppendLine($"TargetKey={TargetKey}")
            .AppendLine()
            .AppendLine("; InteractKey is pressed when the bridge reports need-interact and Interact is on.")
            .AppendLine("[Interact]")
            .AppendLine($"InteractEnabled={(InteractEnabled ? 1 : 0)}")
            .AppendLine($"InteractKey={InteractKey}")
            .AppendLine()
            .AppendLine("; Display-chain colour profile learned from '/mdb calibrate'.")
            .AppendLine("; Absent LearnedAt = legacy behaviour. Tolerance is per-channel.")
            .AppendLine("[Color]")
            .AppendLine($"Magic={Color.MagicR},{Color.MagicG},{Color.MagicB}")
            .AppendLine($"Black={Color.Black[0]},{Color.Black[1]},{Color.Black[2]}")
            .AppendLine($"White={Color.White[0]},{Color.White[1]},{Color.White[2]}")
            .AppendLine($"Tolerance={Color.Tolerance}")
            .AppendLine($"LearnedAt={Color.LearnedAt}")
            .AppendLine()
            .AppendLine("; BNetPath empty = auto-detect Battle.net. Launch Game uses the")
            .AppendLine("; launcher's remembered account — no credentials are stored anywhere.")
            .AppendLine("[Launch]")
            .AppendLine($"BNetPath={BNetPath}")
            .ToString();

        File.WriteAllText(Path, text);
    }

    private static void ParseTriple(string value, int fr, int fg, int fb, out int r, out int g, out int b)
    {
        r = fr; g = fg; b = fb;
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3) return;
        if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pr)) r = Math.Clamp(pr, 0, 255);
        if (int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pg)) g = Math.Clamp(pg, 0, 255);
        if (int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pb)) b = Math.Clamp(pb, 0, 255);
    }

    private static int ParseInt(string value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static bool ParseBool(string value, bool fallback) => value.Trim().ToLowerInvariant() switch
    {
        "1" or "true" or "yes" or "on" => true,
        "0" or "false" or "no" or "off" => false,
        _ => fallback,
    };
}
