using System.Globalization;
using System.Text;

namespace MaxDpsCompanion;

/// <summary>
/// Companion-side rotation preset (Stream 3, restrict-only). FULL is the
/// historical behaviour (every gate unchanged). BURST conserves major
/// offensives, consumables and on-use trinkets until the per-target TTK is
/// measurable (a TTK-valid boss), so burst windows are not spent on dying
/// trash. A preset only ever HOLDS candidates — it never adds a press — and
/// the addon can further restrict via its own toggles.
/// </summary>
internal enum RotationPreset
{
    Full = 0,
    Burst = 1,
}

/// <summary>
/// Companion-side target preset (Stream 3, restrict-only). SINGLE_TARGET is the
/// historical behaviour. AOE conserves catalogued single-target-only offensive
/// cooldowns so the curated AoE-ranked candidates already present in the list
/// get priority. Default = current behaviour.
/// </summary>
internal enum TargetPreset
{
    SingleTarget = 0,
    Aoe = 1,
}

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

    // [Window] — background play: keyboard slots are posted to the game window
    // and work while it is in the background; mouse slots still need focus.
    // (The legacy RequireForeground toggle was removed — it was never read by
    // the engine; mouse/interact input has always required the foreground.)
    public bool AllowBackgroundKeys { get; set; } = true;

    /// <summary>Saved client size (0 = use the built-in default). Written on resize/close.</summary>
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }

    /// <summary>Layout tag (classic3 = v3 classic shell); forces size reset on shell change.</summary>
    public string WindowLayout { get; set; } = "";

    // [Pause] — global toggle hotkey, parsed as a System.Windows.Forms.Keys name.
    public string PauseHotkey { get; set; } = "Pause";

    // [Spells] — which MaxDPS icons the helper is allowed to press.
    // Index order == Slot enum: 0 Main, 1 Offensive, 2 Defensive,
    // 3 Consumable, 4 Trinket, 5 Interrupt, 6 Mobility, 7 SelfHeal.
    public bool[] SlotEnabled { get; } = [true, true, true, true, false, false, true, true];

    // [Timing] — PERF (v1.3.9): 33 ms matches the bridge's 30 Hz strip
    // refresh. Sampling faster than the addon repaints re-reads identical
    // frames for nothing; slower adds latency. The engine ALSO stretches
    // this automatically when a capture is expensive (duty-cycle guard),
    // so a slow display path degrades latency instead of stealing frames.
    public int PollIntervalMs { get; set; } = 33;
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

    // [Scheduler] — deterministic action scheduler (v1.6.0). ON by default:
    // owns send order + pacing (link-loss gate on a frozen heartbeat,
    // interrupt/defensive urgency, GCD + minimum key interval, stale/repeat
    // suppression). Enabled=0 falls back to DecisionResult.Fallback() — the
    // byte-identical legacy loop; [Intelligence] reorders that loop when set.
    public bool SchedulerEnabled { get; set; } = true;

    // Heartbeat frozen for this long = the addon stopped rendering (link
    // lost) => NOTHING fires. Mirrors docs/PROTOCOL.md (500 ms at 30 Hz).
    public int SchedulerHeartbeatTimeoutMs { get; set; } = 500;

    // Protocol v1 frames carry no GCD flag; an identical re-suggested stroke
    // is suppressed for this long instead of machine-gunning a dead GCD.
    public int SchedulerRepeatSuppressMs { get; set; } = 900;

    // [Intelligence] — situational intelligence (v2.0). ON by default: the
    // deterministic policy layer evaluates every situational candidate
    // (USE / HOLD / SKIP) using the embedded ability knowledge base and the
    // protocol v5 combat context. OFF = the pre-intelligence behaviour: the
    // scheduler still owns pacing, but no candidate is filtered by knowledge
    // (and the legacy DecisionEngine order is used when the scheduler is off).
    // The MAIN rotation is never gated by knowledge either way.
    public bool IntelligenceEnabled { get; set; } = true;

    // A candidate already pressed since it last changed and unchanged for this
    // long is demoted behind a fresh alternative (never removed).
    public int IntelligenceStaleAfterMs { get; set; } = 1500;

    // [Intelligence] — paint the player HP curve strip (cell 35). ON by default.
    public bool HpCurve { get; set; } = true;

    // [TimeToKill] — per-target time-to-kill estimation (v3.2.0). ON by default:
    // the engine feeds a pure estimator every tick and the offensives/defensives
    // apply the TTK gates. Enabled=0 turns the estimator off entirely and skips
    // every TTK gate (the pre-TTK behaviour). Invalid estimates fail open in
    // either case.
    public bool TimeToKillEnabled { get; set; } = true;

    // [CrowdControl] — companion-side CC appendix opt-in (v3.4.0). OFF by
    // default (CC is an explicit opt-in; Solo-like safety). ON makes curated,
    // auto-eligible crowd-control rows eligible while a target is confirmed and
    // the companion's own DR memory is clear; OFF keeps them manual-by-design.
    // The in-game addon toggle can only further restrict (addon OFF wins,
    // missing key = ON); that cross-surface wiring is OWED live.
    public bool CrowdControlEnabled { get; set; } = false;

    // [Rotation] — companion-side restrict-only presets (Stream 3). Defaults are
    // the historical behaviour. Burst holds major offensives/consumable/trinket
    // until the TTK estimate is valid (a boss-like target); AoE conserves
    // catalogued single-target-only offensive cooldowns. Neither ever adds a
    // press; the in-game addon can further restrict via its own toggles.
    public RotationPreset ModePreset { get; set; } = RotationPreset.Full;
    public TargetPreset TargetMode { get; set; } = TargetPreset.SingleTarget;

    // [Solo] — SOLO / SELF-SUSTAIN mode (v2.0). OFF by default. ON adds the
    // survival-first layer on top of the standard policy: self-heals become
    // eligible below the sustain threshold, defensives may be used for
    // efficiency, and emergency actions outrank the rotation. Requires
    // [Intelligence] Enabled=1.
    public bool SoloEnabled { get; set; } = false;

    /// <summary>Below this HP% every available survival action outranks damage.</summary>
    public int SoloEmergencyHpPct { get; set; } = 35;

    /// <summary>Below this HP% efficient self-sustain becomes eligible.</summary>
    public int SoloSelfSustainHpPct { get; set; } = 65;

    /// <summary>A major defensive waits while a minor runs and HP is above this.</summary>
    public int SoloDefensiveEscalateHpPct { get; set; } = 60;

    /// <summary>Solo HP-banded escalation master switch (default on).</summary>
    public bool SoloEscalationEnabled { get; set; } = true;

    /// <summary>At/below this HP% Solo offers Minor absorbs (default 75, clamp 40-99).</summary>
    public int SoloMinorHpPct { get; set; } = 75;

    /// <summary>At/below this HP% Solo offers Major defensives (default 50, clamp 20-90).</summary>
    public int SoloMajorHpPct { get; set; } = 50;

    /// <summary>At/below this HP% Solo offers immunities (default 30, clamp 5-60).</summary>
    public int SoloImmunityHpPct { get; set; } = 30;

    // [Abilities] — per-ability automatic-use overrides (v2.3.0 defensive
    // intelligence; v2.6 richer modes). The curated catalog default is ON for
    // automatable abilities and OFF for manual-by-design ones (NeverAutomatic).
    // Overrides are comma-separated spell ids: [Abilities] On = turned ON
    // despite a default-OFF curation, Off = turned OFF despite a default-ON
    // curation. Modes = optional id:Mode pairs for the richer future model
    // (<see cref="UserAbilityMode"/>) and never replaces On/Off. An explicit
    // OFF is an absolute automatic-use prohibition (no urgency, MaxDps
    // recommendation, Solo or emergency override); ON is only eligibility,
    // never "spam when ready".
    public AbilityPolicy Abilities { get; set; } = AbilityPolicy.Default;

    // [AbilityOverrides] — per-machine user overrides of the v3.5 taxonomy
    // (T4). Keyed by spell id; an entry may ONLY set mode + minUrgency, so a
    // store file can never re-route an ability, pull it into the main rotation
    // or exempt it from the cast gate, and it can never raise a Never base to
    // Auto/Suggest. The store file is OPTIONAL and lives NEXT TO THE EXE
    // (dist\), which is git-ignored and therefore never committed. Absent file
    // = no overrides. Precedence: registry < overlay < overrides.
    public bool AbilityOverridesEnabled { get; set; } = true;

    /// <summary>Store file name (relative to the settings file / exe).</summary>
    public string AbilityOverridesFile { get; set; } = "ability-overrides.json";

    /// <summary>The loaded per-machine store (never null; Empty when disabled/absent).</summary>
    public AbilityOverrides AbilityOverrides { get; private set; } = AbilityOverrides.Empty;

    // [Telemetry] — local rotation telemetry (v1.5.0). OFF by default: the
    // recorder is opt-in, keeps a bounded in-memory ring of JSONL events
    // (keybinds/flags/timestamps only — no Blizzard values, no network), and
    // nothing is written to disk until an explicit Export.
    public bool TelemetryEnabled { get; set; } = false;

    /// <summary>Ring capacity in events (~400-500 bytes each).</summary>
    public int TelemetryCapacity { get; set; } = 10_000;

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
        if (!File.Exists(path))
        {
            CrowdControlGate.Configure(settings.CrowdControlEnabled);
            settings.LoadAbilityOverrides();
            return settings;
        }

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

        // Publish the companion CC opt-in to the policy gate (the settings class
        // is the single load point; the evaluator's one-line CC call-site has no
        // options plumbing by contract). Default OFF, so an absent section keeps
        // CC manual-by-design.
        CrowdControlGate.Configure(settings.CrowdControlEnabled);
        settings.LoadAbilityOverrides();
        return settings;
    }

    /// <summary>
    /// Resolves the per-machine override file next to the settings file (which
    /// lives next to the exe in production). An absolute configured path wins.
    /// </summary>
    public string AbilityOverridesPath()
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (string.IsNullOrEmpty(directory)) directory = ".";
        return System.IO.Path.Combine(directory, AbilityOverridesFile);
    }

    /// <summary>Loads the optional store; a missing file means "no overrides".</summary>
    private void LoadAbilityOverrides() =>
        AbilityOverrides = AbilityOverridesEnabled
            ? AbilityOverrides.LoadFile(AbilityOverridesPath())
            : AbilityOverrides.Empty;

    private void Apply(string section, string key, string value)
    {
        switch (section, key)
        {
            case ("bridge", "cellsize"): CellSize = ParseInt(value, CellSize); break;
            case ("bridge", "offsetx"): OffsetX = ParseInt(value, OffsetX); break;
            case ("bridge", "offsety"): OffsetY = ParseInt(value, OffsetY); break;
            case ("window", "processname"): ProcessName = value; break;
            case ("window", "allowbackgroundkeys"): AllowBackgroundKeys = ParseBool(value, AllowBackgroundKeys); break;
            case ("window", "width"): WindowWidth = Math.Clamp(ParseInt(value, WindowWidth), 0, 8000); break;
            case ("window", "height"): WindowHeight = Math.Clamp(ParseInt(value, WindowHeight), 0, 8000); break;
            case ("window", "layout"): WindowLayout = value.Trim(); break;
            case ("pause", "button"): PauseHotkey = value; break;
            case ("spells", "spell1"): SlotEnabled[0] = ParseBool(value, SlotEnabled[0]); break;
            case ("spells", "spell2"): SlotEnabled[1] = ParseBool(value, SlotEnabled[1]); break;
            case ("spells", "spell3"): SlotEnabled[2] = ParseBool(value, SlotEnabled[2]); break;
            case ("spells", "spell4"): SlotEnabled[3] = ParseBool(value, SlotEnabled[3]); break;
            case ("spells", "spell5"): SlotEnabled[4] = ParseBool(value, SlotEnabled[4]); break;
            case ("spells", "spell6"): SlotEnabled[5] = ParseBool(value, SlotEnabled[5]); break;
            case ("spells", "spell7"): SlotEnabled[6] = ParseBool(value, SlotEnabled[6]); break;
            case ("spells", "spell8"): SlotEnabled[7] = ParseBool(value, SlotEnabled[7]); break;
            case ("timing", "pollintervalms"): PollIntervalMs = ParseInt(value, PollIntervalMs); break;
            case ("timing", "minkeyintervalms"): MinKeyIntervalMs = ParseInt(value, MinKeyIntervalMs); break;
            case ("timing", "keypressms"): KeyPressMs = ParseInt(value, KeyPressMs); break;
            case ("targeting", "autotargetenabled"): AutoTargetEnabled = ParseBool(value, AutoTargetEnabled); break;
            case ("targeting", "combatonly"): CombatOnly = ParseBool(value, CombatOnly); break;
            case ("targeting", "targetkey"): TargetKey = string.IsNullOrWhiteSpace(value) ? TargetKey : value.Trim(); break;
            case ("interact", "interactenabled"): InteractEnabled = ParseBool(value, InteractEnabled); break;
            case ("interact", "interactkey"): InteractKey = string.IsNullOrWhiteSpace(value) ? InteractKey : value.Trim(); break;
            case ("scheduler", "enabled"): SchedulerEnabled = ParseBool(value, SchedulerEnabled); break;
            case ("scheduler", "heartbeattimeoutms"): SchedulerHeartbeatTimeoutMs = Math.Clamp(ParseInt(value, SchedulerHeartbeatTimeoutMs), 100, 5000); break;
            case ("scheduler", "repeatsuppressms"): SchedulerRepeatSuppressMs = Math.Clamp(ParseInt(value, SchedulerRepeatSuppressMs), 100, 5000); break;
            case ("intelligence", "enabled"): IntelligenceEnabled = ParseBool(value, IntelligenceEnabled); break;
            case ("intelligence", "staleafterms"): IntelligenceStaleAfterMs = ParseInt(value, IntelligenceStaleAfterMs); break;
            case ("intelligence", "hpcurve"): HpCurve = ParseBool(value, HpCurve); break;
            case ("timetokill", "enabled"): TimeToKillEnabled = ParseBool(value, TimeToKillEnabled); break;
            case ("crowdcontrol", "enabled"): CrowdControlEnabled = ParseBool(value, CrowdControlEnabled); break;
            case ("rotation", "mode"): ModePreset = ParseRotationPreset(value); break;
            case ("rotation", "targets"): TargetMode = ParseTargetPreset(value); break;
            case ("solo", "enabled"): SoloEnabled = ParseBool(value, SoloEnabled); break;
            case ("solo", "emergencyhppct"): SoloEmergencyHpPct = Math.Clamp(ParseInt(value, SoloEmergencyHpPct), 5, 90); break;
            case ("solo", "selfsustainhppct"): SoloSelfSustainHpPct = Math.Clamp(ParseInt(value, SoloSelfSustainHpPct), 10, 99); break;
            case ("solo", "defensiveescalatehppct"): SoloDefensiveEscalateHpPct = Math.Clamp(ParseInt(value, SoloDefensiveEscalateHpPct), 5, 99); break;
            case ("solo", "escalationenabled"): SoloEscalationEnabled = ParseBool(value, SoloEscalationEnabled); break;
            case ("solo", "minorhppct"): SoloMinorHpPct = Math.Clamp(ParseInt(value, SoloMinorHpPct), 40, 99); break;
            case ("solo", "majorhppct"): SoloMajorHpPct = Math.Clamp(ParseInt(value, SoloMajorHpPct), 20, 90); break;
            case ("solo", "immunityhppct"): SoloImmunityHpPct = Math.Clamp(ParseInt(value, SoloImmunityHpPct), 5, 60); break;
            case ("abilities", "on"): Abilities = AbilityPolicy.FromParts(value, Abilities.EncodeOff(), Abilities.EncodeModes()); break;
            case ("abilities", "off"): Abilities = AbilityPolicy.FromParts(Abilities.EncodeOn(), value, Abilities.EncodeModes()); break;
            case ("abilities", "modes"): Abilities = AbilityPolicy.FromParts(Abilities.EncodeOn(), Abilities.EncodeOff(), value); break;
            case ("abilityoverrides", "enabled"): AbilityOverridesEnabled = ParseBool(value, AbilityOverridesEnabled); break;
            case ("abilityoverrides", "file"): AbilityOverridesFile = string.IsNullOrWhiteSpace(value) ? AbilityOverridesFile : value.Trim(); break;
            case ("telemetry", "enabled"): TelemetryEnabled = ParseBool(value, TelemetryEnabled); break;
            case ("telemetry", "capacity"): TelemetryCapacity = Math.Clamp(ParseInt(value, TelemetryCapacity), 64, 1_000_000); break;
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
            .AppendLine($"AllowBackgroundKeys={(AllowBackgroundKeys ? 1 : 0)}")
            .AppendLine("; Saved client size (0 = default); written on resize/close.")
            .AppendLine($"Width={WindowWidth}")
            .AppendLine($"Height={WindowHeight}")
            .AppendLine("; Layout tag (classic3 = v3 classic shell); forces size reset on shell change.")
            .AppendLine($"Layout={WindowLayout}")
            .AppendLine()
            .AppendLine("[Pause]")
            .AppendLine($"Button={PauseHotkey}")
            .AppendLine()
            .AppendLine("; spell1=Main, spell2=Offensive, spell3=Defensive, spell4=Consumable,")
            .AppendLine("; spell5=Trinket, spell6=Interrupt, spell7=Mobility, spell8=SelfHeal")
            .AppendLine("[Spells]")
            .AppendLine($"spell1={(SlotEnabled[0] ? 1 : 0)}")
            .AppendLine($"spell2={(SlotEnabled[1] ? 1 : 0)}")
            .AppendLine($"spell3={(SlotEnabled[2] ? 1 : 0)}")
            .AppendLine($"spell4={(SlotEnabled[3] ? 1 : 0)}")
            .AppendLine($"spell5={(SlotEnabled[4] ? 1 : 0)}")
            .AppendLine($"spell6={(SlotEnabled[5] ? 1 : 0)}")
            .AppendLine($"spell7={(SlotEnabled[6] ? 1 : 0)}")
            .AppendLine($"spell8={(SlotEnabled[7] ? 1 : 0)}")
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
            .AppendLine("; Deterministic action scheduler (default on). 0 = legacy MaxDps")
            .AppendLine("; priority loop; [Intelligence] may then reorder it.")
            .AppendLine("; HeartbeatTimeoutMs: frozen heartbeat => link lost => nothing fires.")
            .AppendLine("; RepeatSuppressMs: v1 frames carry no GCD flag; identical repeats wait this long.")
            .AppendLine("[Scheduler]")
            .AppendLine($"Enabled={(SchedulerEnabled ? 1 : 0)}")
            .AppendLine($"HeartbeatTimeoutMs={SchedulerHeartbeatTimeoutMs}")
            .AppendLine($"RepeatSuppressMs={SchedulerRepeatSuppressMs}")
            .AppendLine()
            .AppendLine("; Situational intelligence (default on). 0 = pre-intelligence")
            .AppendLine("; behaviour: no knowledge-based filtering of situation slots.")
            .AppendLine("; The MAIN rotation is never gated by knowledge either way.")
            .AppendLine("; StaleAfterMs: a pressed-and-unchanged candidate is demoted behind a fresh one.")
            .AppendLine("[Intelligence]")
            .AppendLine($"Enabled={(IntelligenceEnabled ? 1 : 0)}")
            .AppendLine($"StaleAfterMs={IntelligenceStaleAfterMs}")
            .AppendLine($"HpCurve={(HpCurve ? 1 : 0)}")
            .AppendLine()
            .AppendLine("; Per-target time-to-kill estimation (default on). 0 = no estimator;")
            .AppendLine("; every TTK gate is skipped. Invalid estimates always fail open.")
            .AppendLine("[TimeToKill]")
            .AppendLine($"Enabled={(TimeToKillEnabled ? 1 : 0)}")
            .AppendLine()
            .AppendLine("; Crowd-control appendix (opt-in, default off). ON makes curated")
            .AppendLine("; auto-eligible CC rows fire only on a confirmed target and never")
            .AppendLine("; chains the same DR category; the addon toggle can only restrict.")
            .AppendLine("[CrowdControl]")
            .AppendLine($"Enabled={(CrowdControlEnabled ? 1 : 0)}")
            .AppendLine()
            .AppendLine("; Companion-side restrict-only presets. Full/SingleTarget = historical")
            .AppendLine("; behaviour. Burst holds majors/consumable/trinket until a boss TTK is")
            .AppendLine("; measurable; Aoe conserves catalogued single-target-only cooldowns.")
            .AppendLine("; A preset only ever holds; the addon can further restrict.")
            .AppendLine("[Rotation]")
            .AppendLine($"Mode={(ModePreset == RotationPreset.Burst ? "Burst" : "Full")}")
            .AppendLine($"Targets={(TargetMode == TargetPreset.Aoe ? "Aoe" : "SingleTarget")}")
            .AppendLine()
            .AppendLine("; Solo / self-sustain mode (default off; requires [Intelligence] Enabled=1).")
            .AppendLine("; EmergencyHpPct: below this, survival actions outrank the rotation.")
            .AppendLine("; SelfSustainHpPct: below this, efficient self-heals become eligible.")
            .AppendLine("; DefensiveEscalateHpPct: a major defensive waits while a minor runs above this.")
            .AppendLine("; EscalationEnabled: Solo HP-banded survival ladder (minor<=MinorHpPct,")
            .AppendLine(";   major<=MajorHpPct, immunity<=ImmunityHpPct) in addition to self-heals.")
            .AppendLine("[Solo]")
            .AppendLine($"Enabled={(SoloEnabled ? 1 : 0)}")
            .AppendLine($"EmergencyHpPct={SoloEmergencyHpPct}")
            .AppendLine($"SelfSustainHpPct={SoloSelfSustainHpPct}")
            .AppendLine($"DefensiveEscalateHpPct={SoloDefensiveEscalateHpPct}")
            .AppendLine($"EscalationEnabled={(SoloEscalationEnabled ? 1 : 0)}")
            .AppendLine($"MinorHpPct={SoloMinorHpPct}")
            .AppendLine($"MajorHpPct={SoloMajorHpPct}")
            .AppendLine($"ImmunityHpPct={SoloImmunityHpPct}")
            .AppendLine()
            .AppendLine("; Per-ability automatic-use overrides, comma-separated spell ids.")
            .AppendLine("; On = explicitly enabled (default-off ability turned ON).")
            .AppendLine("; Off = explicitly disabled (default-on ability turned OFF).")
            .AppendLine("; OFF never fires automatically; ON is eligibility, not spam.")
            .AppendLine("; Modes = optional id:Mode pairs (SoloOnly/NormalOnly/Manual/Never/")
            .AppendLine("; Always/Automatic); written only when a richer mode is set.")
            .AppendLine("[Abilities]")
            .AppendLine($"On={Abilities.EncodeOn()}")
            .AppendLine($"Off={Abilities.EncodeOff()}")
            .AppendLine($"Modes={Abilities.EncodeModes()}")
            .AppendLine()
            .AppendLine("; Per-machine taxonomy overrides (T4). The store file lives next to the")
            .AppendLine("; exe (dist\\), is never committed, and may only set mode + minUrgency.")
            .AppendLine("; Absent file = no overrides. Precedence: registry < overlay < overrides.")
            .AppendLine("[AbilityOverrides]")
            .AppendLine($"Enabled={(AbilityOverridesEnabled ? 1 : 0)}")
            .AppendLine($"File={AbilityOverridesFile}")
            .AppendLine()
            .AppendLine("; Local rotation telemetry (opt-in, default off). Bounded in-memory")
            .AppendLine("; JSONL ring; Export writes it next to the exe. No network, no")
            .AppendLine("; Blizzard values, no personal data.")
            .AppendLine("[Telemetry]")
            .AppendLine($"Enabled={(TelemetryEnabled ? 1 : 0)}")
            .AppendLine($"Capacity={TelemetryCapacity}")
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

    private static RotationPreset ParseRotationPreset(string value) => value.Trim().ToLowerInvariant() switch
    {
        "burst" => RotationPreset.Burst,
        _ => RotationPreset.Full,
    };

    private static TargetPreset ParseTargetPreset(string value) => value.Trim().ToLowerInvariant() switch
    {
        "aoe" or "ae" or "multi" => TargetPreset.Aoe,
        _ => TargetPreset.SingleTarget,
    };

    private static int ParseInt(string value, int fallback) =>        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static bool ParseBool(string value, bool fallback) => value.Trim().ToLowerInvariant() switch
    {
        "1" or "true" or "yes" or "on" => true,
        "0" or "false" or "no" or "off" => false,
        _ => fallback,
    };
}
