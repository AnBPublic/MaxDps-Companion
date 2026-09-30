using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MaxDpsCompanion;

/// <summary>
/// One per-machine user override of a taxonomy ability. Deliberately tiny:
/// a user may only move the <see cref="ClassOverlayMode"/> and the
/// <see cref="MinUrgency"/> floor. There is no role, route, <c>offGcd</c> or
/// <c>emergencyEscape</c> field, so a store file can never re-route an ability,
/// pull it into the main rotation or exempt it from the cast gate — those
/// remain overlay/registry properties and are preserved verbatim by
/// <see cref="AbilityOverrides.Apply(ClassOverlayEntry)"/>.
/// </summary>
internal sealed record AbilityOverride(
    int SpellId,
    ClassOverlayMode? Mode,
    int? MinUrgency)
{
    /// <summary>Neither field set, so the entry would change nothing.</summary>
    public bool IsNoOp => Mode is null && MinUrgency is null;
}

/// <summary>
/// The per-machine user override store (T4): an optional
/// <c>ability-overrides.json</c> next to the exe (in <c>dist\</c>, which is
/// git-ignored and therefore never committed) keyed by spell id.
///
/// Shape:
/// <code>
/// { "schema": 1, "overrides": [
///     { "spellId": 48792, "mode": "Suggest", "minUrgency": 3 }
/// ] }
/// </code>
///
/// The allow-list is exactly <c>spellId</c> / <c>mode</c> / <c>minUrgency</c>.
/// Any other field (notably a <c>route</c>, <c>offGcd</c> or <c>role</c>), a
/// duplicate field or spell id, a non-positive id, a bad mode or an
/// out-of-range urgency drops that entry and records an issue. Loading never
/// throws: a bad file yields an empty store plus issues.
///
/// Precedence is registry &lt; overlay &lt; overrides. An override wins over the
/// overlay entry and the registry default, but <see cref="ClassOverlayMode.Never"/>
/// is absolute: a <c>Never</c> (registry- or overlay-derived) base can never be
/// raised to <c>Suggest</c>/<c>Auto</c> by an override — <see cref="ResolveMode"/>
/// keeps it <c>Never</c>.
/// </summary>
internal sealed class AbilityOverrides
{
    /// <summary>The only supported store schema.</summary>
    public const int SchemaVersion = 1;

    /// <summary>The exact fields an override may carry. Anything else drops it.</summary>
    public static readonly IReadOnlyList<string> AllowedEntryFields = ["spellId", "mode", "minUrgency"];

    private static readonly HashSet<string> EntryFields =
        new(AllowedEntryFields, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> TopLevelFields =
        new(["schema", "overrides"], StringComparer.OrdinalIgnoreCase);

    private static readonly int[] AllowedUrgency = [0, 1, 2, 3, 4];

    private readonly Dictionary<int, AbilityOverride> _bySpellId;

    private AbilityOverrides(int schema, Dictionary<int, AbilityOverride> bySpellId, IReadOnlyList<string> issues)
    {
        Schema = schema;
        _bySpellId = bySpellId;
        Issues = issues;
    }

    public int Schema { get; }

    /// <summary>The accepted overrides, keyed by spell id.</summary>
    public IReadOnlyDictionary<int, AbilityOverride> BySpellId => _bySpellId;

    /// <summary>Every dropped/downgraded entry, so a malformed file is visible.</summary>
    public IReadOnlyList<string> Issues { get; }

    public bool HasOverrides => _bySpellId.Count > 0;

    public bool HasIssues => Issues.Count > 0;

    /// <summary>No overrides: registry and overlay alone decide.</summary>
    public static AbilityOverrides Empty { get; } = new(SchemaVersion, [], []);

    public AbilityOverride? For(int spellId) =>
        _bySpellId.TryGetValue(spellId, out var entry) ? entry : null;

    /// <summary>Effective mode with precedence registry &lt; overlay &lt; overrides.</summary>
    public ClassOverlayMode ResolveMode(int spellId, ClassOverlayMode registryMode, ClassOverlayMode? overlayMode)
    {
        // The overlay (already registry-gated at load) outranks the registry.
        var baseMode = overlayMode ?? registryMode;

        if (!_bySpellId.TryGetValue(spellId, out var entry) || entry.Mode is not { } mode)
            return baseMode;

        // Never is absolute: the override can restrict, never loosen it.
        if (baseMode == ClassOverlayMode.Never) return ClassOverlayMode.Never;
        return mode;
    }

    /// <summary>Effective urgency floor: the override wins when it sets one.</summary>
    public int ResolveMinUrgency(int spellId, int baseMinUrgency) =>
        _bySpellId.TryGetValue(spellId, out var entry) && entry.MinUrgency is { } urgency
            ? urgency
            : baseMinUrgency;

    /// <summary>
    /// Applies this store's override for <paramref name="baseEntry"/> (the
    /// overlay-resolved entry) and copies every other field unchanged, so the
    /// override can never touch role / route / <c>emergencyEscape</c> /
    /// <c>offGcd</c> / notes. A <c>Never</c> base stays <c>Never</c>.
    /// </summary>
    public ClassOverlayEntry Apply(ClassOverlayEntry baseEntry)
    {
        ArgumentNullException.ThrowIfNull(baseEntry);
        if (!_bySpellId.TryGetValue(baseEntry.SpellId, out var entry)) return baseEntry;

        return baseEntry with
        {
            // The overlay entry already carries the registry-gated base mode.
            Mode = ResolveMode(baseEntry.SpellId, baseEntry.Mode, overlayMode: null),
            MinUrgency = entry.MinUrgency ?? baseEntry.MinUrgency,
        };
    }

    public static AbilityOverrides Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var issues = new List<string>();
        var bySpellId = new Dictionary<int, AbilityOverride>();

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new AbilityOverrides(SchemaVersion, bySpellId, ["overrides root is not a JSON object"]);

            // Missing schema defaults to the current schema; a present
            // mismatching schema rejects the whole file.
            var schema = SchemaVersion;
            if (TryGet(root, "schema", out var schemaElement))
            {
                if (schemaElement.ValueKind != JsonValueKind.Number || !schemaElement.TryGetInt32(out schema))
                    return new AbilityOverrides(SchemaVersion, bySpellId, ["overrides schema is not an integer"]);
                if (schema != SchemaVersion)
                    return new AbilityOverrides(schema, bySpellId, [$"unsupported overrides schema {schema}"]);
            }

            foreach (var property in root.EnumerateObject())
                if (!TopLevelFields.Contains(property.Name))
                    issues.Add($"unknown top-level field '{property.Name}' ignored");

            if (!TryGet(root, "overrides", out var element) || element.ValueKind != JsonValueKind.Array)
            {
                issues.Add("overrides array is missing");
                return new AbilityOverrides(schema, bySpellId, issues);
            }

            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                index++;
                if (!TryParse(item, index, issues, out var entry)) continue;
                if (!bySpellId.TryAdd(entry.SpellId, entry))
                    issues.Add($"override #{index}: duplicate spellId {entry.SpellId} dropped");
            }

            return new AbilityOverrides(schema, bySpellId, issues);
        }
        catch (JsonException ex)
        {
            return new AbilityOverrides(SchemaVersion, bySpellId, [$"malformed JSON: {ex.Message}"]);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AbilityOverrides] load failed: {ex}");
            return new AbilityOverrides(SchemaVersion, bySpellId, [$"overrides load failed: {ex.Message}"]);
        }
    }

    /// <summary>
    /// Loads the per-machine store from <paramref name="path"/>. A missing file
    /// is the normal no-overrides state and yields <see cref="Empty"/> with no
    /// issue; any other read failure is reported as an issue, never thrown.
    /// </summary>
    public static AbilityOverrides LoadFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return Empty;
            return Load(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AbilityOverrides] cannot read '{path}': {ex.Message}");
            return new AbilityOverrides(SchemaVersion, [], [$"could not read '{path}': {ex.Message}"]);
        }
    }

    /// <summary>Serializes the accepted overrides (sorted by spell id).</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema", SchemaVersion);
            writer.WriteStartArray("overrides");
            foreach (var entry in _bySpellId.Values.OrderBy(entry => entry.SpellId))
            {
                writer.WriteStartObject();
                writer.WriteNumber("spellId", entry.SpellId);
                if (entry.Mode is { } mode) writer.WriteString("mode", mode.ToString());
                if (entry.MinUrgency is { } urgency) writer.WriteNumber("minUrgency", urgency);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Writes the store next to the exe. IO failures are swallowed.</summary>
    public bool SaveFile(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, ToJson());
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AbilityOverrides] cannot write '{path}': {ex.Message}");
            return false;
        }
    }

    /// <summary>Immutable edit: returns a new store with one override set (or removed when a no-op).</summary>
    public AbilityOverrides With(int spellId, ClassOverlayMode? mode, int? minUrgency)
    {
        if (spellId <= 0) return this;
        var entry = new AbilityOverride(spellId, mode, minUrgency);
        if (entry.IsNoOp) return Without(spellId);
        if (minUrgency is { } urgency && !AllowedUrgency.Contains(urgency)) return this;

        var next = new Dictionary<int, AbilityOverride>(_bySpellId) { [spellId] = entry };
        return new AbilityOverrides(SchemaVersion, next, Issues);
    }

    /// <summary>Immutable edit: drops any override for one spell id.</summary>
    public AbilityOverrides Without(int spellId)
    {
        if (!_bySpellId.ContainsKey(spellId)) return this;
        var next = new Dictionary<int, AbilityOverride>(_bySpellId);
        next.Remove(spellId);
        return new AbilityOverrides(SchemaVersion, next, Issues);
    }

    private static bool TryParse(JsonElement element, int index, List<string> issues, out AbilityOverride entry)
    {
        entry = null!;
        if (element.ValueKind != JsonValueKind.Object)
        {
            issues.Add($"override #{index}: not a JSON object dropped");
            return false;
        }

        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!EntryFields.Contains(property.Name))
            {
                issues.Add($"override #{index}: unknown field '{property.Name}' dropped");
                return false;
            }
            if (!fields.TryAdd(property.Name, property.Value))
            {
                issues.Add($"override #{index}: duplicate field '{property.Name}' dropped");
                return false;
            }
        }

        if (!fields.TryGetValue("spellId", out var spellIdElement)
            || spellIdElement.ValueKind != JsonValueKind.Number
            || !spellIdElement.TryGetInt32(out var spellId)
            || spellId <= 0)
        {
            issues.Add($"override #{index}: spellId is missing or invalid dropped");
            return false;
        }

        ClassOverlayMode? mode = null;
        if (fields.TryGetValue("mode", out var modeElement))
        {
            if (modeElement.ValueKind != JsonValueKind.String)
            {
                issues.Add($"override #{index}: mode is not a string dropped");
                return false;
            }
            var raw = modeElement.GetString() ?? "";
            if (raw.Length == 0 || char.IsDigit(raw[0])
                || !Enum.TryParse<ClassOverlayMode>(raw, ignoreCase: true, out var parsed)
                || !Enum.IsDefined(parsed))
            {
                issues.Add($"override #{index}: bad mode '{raw}' dropped");
                return false;
            }
            mode = parsed;
        }

        int? minUrgency = null;
        if (fields.TryGetValue("minUrgency", out var urgencyElement))
        {
            if (urgencyElement.ValueKind != JsonValueKind.Number
                || !urgencyElement.TryGetInt32(out var urgency)
                || !AllowedUrgency.Contains(urgency))
            {
                issues.Add($"override #{index}: minUrgency is not in 0-4 dropped");
                return false;
            }
            minUrgency = urgency;
        }

        entry = new AbilityOverride(spellId, mode, minUrgency);
        if (entry.IsNoOp)
        {
            issues.Add($"override #{index}: sets neither mode nor minUrgency dropped");
            return false;
        }
        return true;
    }

    private static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }
        value = default;
        return false;
    }
}
