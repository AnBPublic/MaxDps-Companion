using System.Globalization;

namespace MaxDpsCompanion;

/// <summary>
/// The user's per-ability automation policy: an explicit ON/OFF override per
/// spell id on top of the curated catalog default (<c>NeverAutomatic</c>).
///
/// Semantics (pinned by tests and documented in docs/KNOWLEDGE.md):
///  * OFF is an absolute automatic-use prohibition — no urgency, Solo mode,
///    emergency HP or MaxDps recommendation overrides it.
///  * ON does not mean "spam when ready": it only makes the ability eligible
///    for the normal policy gates (tier, urgency, range, cast safety, ...).
///
/// Instances are immutable; the UI replaces the reference held by
/// <see cref="AppSettings.Abilities"/> rather than mutating it, so a
/// concurrently ticking engine always reads a consistent snapshot.
/// </summary>
internal sealed class AbilityPolicy
{
    /// <summary>No overrides: every ability follows its curated default.</summary>
    public static AbilityPolicy Default { get; } = new([], [], new Dictionary<int, UserAbilityMode>());

    private readonly HashSet<int> _on;
    private readonly HashSet<int> _off;
    private readonly Dictionary<int, UserAbilityMode> _modes;

    private AbilityPolicy(HashSet<int> on, HashSet<int> off, Dictionary<int, UserAbilityMode> modes)
    {
        _on = on;
        _off = off;
        _modes = modes;
    }

    public bool HasOverrides => _on.Count > 0 || _off.Count > 0 || _modes.Count > 0;

    public IReadOnlyCollection<int> ExplicitOn => _on;
    public IReadOnlyCollection<int> ExplicitOff => _off;

    /// <summary>The explicit mode for one spell id (Default when none is stored).</summary>
    public UserAbilityMode ModeOf(int spellId) =>
        _modes.TryGetValue(spellId, out var mode) ? mode : UserAbilityMode.Default;

    /// <summary>The explicit override for one spell id, or null when it follows the default.</summary>
    public bool? OverrideOf(int spellId) =>
        _on.Contains(spellId) ? true
        : _off.Contains(spellId) ? false
        : null;

    /// <summary>
    /// Effective automation switch: explicit mode first, then the ON/OFF
    /// override, then the curated default. Never/Manual are absolute; the
    /// SoloOnly/NormalOnly modes are eligible here (the evaluator applies the
    /// actual solo/normal gate, where the mode reason is meaningful).
    /// </summary>
    public bool IsEnabled(AbilityDefinition ability)
    {
        switch (ModeOf(ability.SpellId))
        {
            case UserAbilityMode.Never:
            case UserAbilityMode.Manual:
                return false;
            case UserAbilityMode.Always:
            case UserAbilityMode.Automatic:
            case UserAbilityMode.SoloOnly:
            case UserAbilityMode.NormalOnly:
                return true;
            default:
                return OverrideOf(ability.SpellId) ?? !ability.NeverAutomatic;
        }
    }

    /// <summary>Immutable edit: returns a new policy with one override set to (or cleared towards) its default.</summary>
    public AbilityPolicy With(int spellId, bool enabled, bool defaultEnabled)
    {
        if (spellId <= 0) return this;
        var on = new HashSet<int>(_on);
        var off = new HashSet<int>(_off);
        on.Remove(spellId);
        off.Remove(spellId);
        if (enabled != defaultEnabled)
        {
            if (enabled) on.Add(spellId);
            else off.Add(spellId);
        }
        return on.Count == _on.Count && off.Count == _off.Count
            ? this
            : new AbilityPolicy(on, off, _modes);
    }

    /// <summary>
    /// Immutable edit for the richer future mode model (registry §24). The UI
    /// keeps writing ON/OFF today; this exists so the storage/evaluator model
    /// can grow without breaking existing settings.
    /// </summary>
    public AbilityPolicy WithMode(int spellId, UserAbilityMode mode)
    {
        if (spellId <= 0) return this;
        var modes = new Dictionary<int, UserAbilityMode>(_modes);
        if (mode == UserAbilityMode.Default) modes.Remove(spellId);
        else modes[spellId] = mode;
        return new AbilityPolicy(new HashSet<int>(_on), new HashSet<int>(_off), modes);
    }

    public string EncodeOn() => Encode(_on);
    public string EncodeOff() => Encode(_off);

    /// <summary>Encodes the non-default modes as id:Mode pairs (empty when none).</summary>
    public string EncodeModes() =>
        string.Join(",", _modes.OrderBy(kv => kv.Key)
            .Select(kv => $"{kv.Key.ToString(CultureInfo.InvariantCulture)}:{kv.Value}"));

    public static AbilityPolicy FromIds(string? on, string? off) =>
        new(Parse(on), Parse(off), new Dictionary<int, UserAbilityMode>());

    public static AbilityPolicy FromParts(string? on, string? off, string? modes) =>
        new(Parse(on), Parse(off), ParseModes(modes));

    private static string Encode(HashSet<int> ids) =>
        string.Join(",", ids.OrderBy(id => id).Select(id => id.ToString(CultureInfo.InvariantCulture)));

    private static HashSet<int> Parse(string? value)
    {
        var set = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(value)) return set;
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
                set.Add(id);
        }
        return set;
    }

    private static Dictionary<int, UserAbilityMode> ParseModes(string? value)
    {
        var modes = new Dictionary<int, UserAbilityMode>();
        if (string.IsNullOrWhiteSpace(value)) return modes;
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var split = part.IndexOf(':');
            if (split <= 0) continue;
            if (!int.TryParse(part[..split], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
                continue;
            if (!Enum.TryParse<UserAbilityMode>(part[(split + 1)..], ignoreCase: true, out var mode)
                || mode == UserAbilityMode.Default)
                continue;
            modes[id] = mode;
        }
        return modes;
    }
}
