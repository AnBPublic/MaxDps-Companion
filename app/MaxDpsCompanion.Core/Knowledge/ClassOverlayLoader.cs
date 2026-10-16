using System.Diagnostics;
using System.Text.Json;

namespace MaxDpsCompanion;

/// <summary>
/// The combat role a per-class overlay entry declares. Deliberately small:
/// the taxonomy only needs to say whether an ability is a major cooldown, a
/// minor/short cooldown, a utility button, a movement tool or crowd control.
/// </summary>
internal enum ClassOverlayRole
{
    Major = 0,
    Minor = 1,
    Utility = 2,
    Mobility = 3,
    CC = 4,
}

/// <summary>
/// Where the entry routes in the companion pipeline. There is intentionally no
/// <c>Main</c> member: an overlay entry may re-route an ability to the
/// Defensive / Mobility / Interrupt providers, but it must never pull an
/// ability into the main rotation (MaxDps owns that). Any route value outside
/// this set — including <c>Main</c> — is a bad enum and the entry is dropped.
/// </summary>
internal enum ClassOverlayRoute
{
    Defensive = 0,
    Mobility = 1,
    Interrupt = 2,
}

/// <summary>
/// The automation intent for an entry. <see cref="Never"/> is the zero value so
/// a missing/unknown mode can never accidentally become automatic.
/// </summary>
internal enum ClassOverlayMode
{
    /// <summary>Absolute manual-only: the companion never presses it.</summary>
    Never = 0,
    /// <summary>Offered/recommended but not auto-fired.</summary>
    Suggest = 1,
    /// <summary>May be fired automatically under the consumer's observable gates.</summary>
    Auto = 2,
}

/// <summary>
/// One validated entry from a <c>Knowledge/classes/&lt;class&gt;.json</c> schema-1
/// overlay. All safety post-processing (verification gating, never-automatic
/// preservation) has already been applied by <see cref="ClassOverlayLoader"/>.
/// </summary>
internal sealed record ClassOverlayEntry(
    int SpellId,
    ClassOverlayRole Role,
    ClassOverlayRoute Route,
    bool EmergencyEscape,
    bool OffGcd,
    ClassOverlayMode Mode,
    int MinUrgency,
    string? Notes);

/// <summary>
/// The immutable result of loading one class overlay. <see cref="Issues"/>
/// carries every entry that was dropped or downgraded, so a malformed file is
/// visible instead of silent. Loading never throws: a bad file yields an empty
/// overlay plus issues.
/// </summary>
internal sealed class ClassOverlay
{
    /// <summary>The only supported overlay schema.</summary>
    public const int SchemaVersion = 1;

    internal ClassOverlay(
        int schema,
        string className,
        IReadOnlyList<ClassOverlayEntry> entries,
        IReadOnlyList<string> issues,
        string? note)
    {
        Schema = schema;
        ClassName = className;
        Entries = entries;
        Issues = issues;
        Note = note;
    }

    public int Schema { get; }
    public string ClassName { get; }
    public IReadOnlyList<ClassOverlayEntry> Entries { get; }
    public IReadOnlyList<string> Issues { get; }
    public string? Note { get; }

    public int Count => Entries.Count;
    public bool HasIssues => Issues.Count > 0;

    public ClassOverlayEntry? For(int spellId)
    {
        foreach (var entry in Entries)
            if (entry.SpellId == spellId) return entry;
        return null;
    }

    public static ClassOverlay Empty(int schema = SchemaVersion, string className = "", string? issue = null) =>
        new(schema, className, [], issue is null ? [] : [issue], null);
}

/// <summary>
/// The verification/never-automatic facts the overlay is validated against.
/// <see cref="Default"/> derives them from the embedded knowledge base:
/// an id is <em>verified</em> only when it is known to the companion
/// (<c>abilities.json</c> / <c>class-spells.json</c>) <em>and</em>
/// <c>spell-verification.json</c> confirms it against the live client; an id
/// is <em>never-automatic</em> when the curated <c>abilities.json</c> marks it
/// so. Tests and other callers can build a scoped instance with
/// <see cref="ForTests"/>.
/// </summary>
internal sealed class ClassOverlayKnowledge
{
    private readonly HashSet<int> _verified;
    private readonly HashSet<int> _neverAutomatic;

    private ClassOverlayKnowledge(HashSet<int> verified, HashSet<int> neverAutomatic)
    {
        _verified = verified;
        _neverAutomatic = neverAutomatic;
    }

    /// <summary>No id is verified and none is never-automatic.</summary>
    public static ClassOverlayKnowledge Empty { get; } = new([], []);

    public bool IsVerified(int spellId) => _verified.Contains(spellId);

    public bool IsNeverAutomatic(int spellId) => _neverAutomatic.Contains(spellId);

    public static ClassOverlayKnowledge ForTests(
        IEnumerable<int>? verified = null, IEnumerable<int>? neverAutomatic = null) =>
        new(verified is null ? [] : [.. verified], neverAutomatic is null ? [] : [.. neverAutomatic]);

    /// <summary>
    /// Abilities that are manual-only by definition, regardless of what the
    /// broad curated <c>NeverAutomatic</c> default says: the plan's explicit
    /// never-automatic list (Imprison / Cyclone / Polymorph / Sap / Banish /
    /// Rescue / Leap of Faith / battle rez) plus the analogous subjugate and
    /// resurrection rows. An overlay may never route these to Auto/Suggest.
    /// The broader curated flag is a conservative default the v3.5 taxonomy
    /// deliberately refines for movement/gap-closers and CC stuns/silences, so
    /// the safety gate is seeded from this structural list (and the
    /// dispel/purge/threat/external families below) rather than from that
    /// default wholesale.
    /// </summary>
    private static readonly int[] StructuralNeverAutomatic =
    [
        118,    // Polymorph (MAGE)
        710,    // Banish (WARLOCK)
        1098,   // Subjugate Demon (WARLOCK)
        6770,   // Sap (ROGUE)
        73325,  // Leap of Faith (PRIEST)
        33786,  // Cyclone (DRUID)
        370665, // Rescue (EVOKER)
        111673, // Control Undead (DEATHKNIGHT)
        2006,   // Resurrection (PALADIN)
        2008,   // Ancestral Spirit (SHAMAN)
        20484,  // Rebirth (DRUID)
        50769,  // Revive (DRUID)
        61999,  // Raise Ally (DEATHKNIGHT)
    ];

    private static readonly HashSet<int> StructuralNeverAutomaticSet = [.. StructuralNeverAutomatic];

    private static ClassOverlayKnowledge? _default;

    public static ClassOverlayKnowledge Default => _default ??= BuildDefault();

    private static ClassOverlayKnowledge BuildDefault()
    {
        try
        {
            var catalog = AbilityCatalog.Default;
            var book = ClassSpellBook.Default;
            var verified = new HashSet<int>();
            var neverAutomatic = new HashSet<int>(StructuralNeverAutomaticSet);

            foreach (var ability in catalog.All)
            {
                // Knowledge requires BOTH presence in a companion source and a
                // live-client verified flag, so an id the client no longer has
                // (or that only the generated class-spell dump names) can never
                // become automatic/suggested.
                if (book.IsVerified(ability.SpellId)) verified.Add(ability.SpellId);

                // Structural manual families keep their curated default: debuff
                // identity, enemy buff identity and ally state are unobservable,
                // so the taxonomy must not automate them. Movement and the
                // CC stun/silence family are deliberately NOT in this gate: the
                // v3.5 taxonomy exists to route them under their own gates.
                if (ability.NeverAutomatic && IsStructuralManual(ability.Purpose))
                    neverAutomatic.Add(ability.SpellId);
            }

            // Class-spell ids that were filtered out of the catalog (junk or
            // otherwise) are still "known" for the verification cross-check.
            foreach (var entry in book.All)
                if (book.IsVerified(entry.SpellId)) verified.Add(entry.SpellId);

            return new ClassOverlayKnowledge(verified, neverAutomatic);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ClassOverlayLoader] knowledge unavailable: {ex.Message}");
            return Empty;
        }
    }

    /// <summary>Manual-by-design families whose observable signal does not exist.</summary>
    private static bool IsStructuralManual(AbilityPurpose purpose) =>
        purpose is AbilityPurpose.Dispel or AbilityPurpose.Purge
            or AbilityPurpose.Threat or AbilityPurpose.External;
}

/// <summary>
/// Schema-1 loader for the per-class taxonomy overlays
/// (<c>Knowledge/classes/&lt;class&gt;.json</c>).
///
/// Shape:
/// <code>
/// { "schema": 1, "class": "DRUID", "note": "...", "entries": [
///     { "spellId": 102401, "role": "Mobility", "route": "Mobility",
///       "emergencyEscape": false, "offGcd": false, "mode": "Auto",
///       "minUrgency": 0, "notes": "gap-closer: out-of-melee+in-range only" }
/// ] }
/// </code>
///
/// The canonical top-level key is <c>entries</c>; the parallel S3b/S3c
/// taxonomy files shipped <c>abilities</c> for the same array. Both are
/// accepted on read and normalized to the one in-memory <c>entries</c> shape
/// (when both are present, <c>entries</c> wins and an issue is recorded).
///
/// The allow-list is exactly
/// <see cref="AllowedEntryFields"/>. Validation is fail-closed per entry: an
/// unknown field, a duplicated field or spell id, a non-positive id, an
/// out-of-range <c>minUrgency</c> or a bad enum value drops that entry and
/// records an issue — the loader itself never throws. Two safety transforms
/// are then applied:
/// <list type="bullet">
///   <item>an unverified id may never be <c>Auto</c>/<c>Suggest</c> — it is
///   downgraded to <c>Never</c> and its note gains "needs verification";</item>
///   <item>an ability in the never-automatic scope (the plan's absolute list
///   plus the structural dispel/purge/threat/external families) is never
///   loosened — its mode is forced to <c>Never</c>.</item>
/// </list>
/// The route enum has no <c>Main</c> member, so an overlay can never route an
/// ability into the main rotation.
/// </summary>
internal static class ClassOverlayLoader
{
    /// <summary>The exact fields an entry may carry. Anything else drops the entry.</summary>
    public static readonly IReadOnlyList<string> AllowedEntryFields =
    [
        "spellId", "role", "route", "emergencyEscape", "offGcd", "mode", "minUrgency", "notes",
    ];

    private static readonly HashSet<string> EntryFields =
        new(AllowedEntryFields, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> TopLevelFields =
        new(["schema", "class", "note", "entries", "abilities"], StringComparer.OrdinalIgnoreCase);

    private static readonly int[] AllowedUrgency = [0, 1, 2, 3, 4];

    /// <summary>Load with the embedded knowledge base as the verification source.</summary>
    public static ClassOverlay Load(string json) => Load(json, ClassOverlayKnowledge.Default);

    /// <summary>Load against a caller-supplied verification/never-automatic scope.</summary>
    public static ClassOverlay Load(string json, ClassOverlayKnowledge knowledge)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(knowledge);

        var issues = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ClassOverlay.Empty(issue: "overlay root is not a JSON object");

            // Schema gate: missing schema defaults to the current schema (small
            // fixtures), a present mismatching schema rejects the whole file.
            var schema = ClassOverlay.SchemaVersion;
            if (TryGet(root, "schema", out var schemaElement))
            {
                if (schemaElement.ValueKind != JsonValueKind.Number || !schemaElement.TryGetInt32(out schema))
                    return ClassOverlay.Empty(issue: "overlay schema is not an integer");
                if (schema != ClassOverlay.SchemaVersion)
                    return ClassOverlay.Empty(schema, issue: $"unsupported overlay schema {schema}");
            }

            var className = TryGet(root, "class", out var classElement) && classElement.ValueKind == JsonValueKind.String
                ? classElement.GetString() ?? ""
                : "";
            var note = TryGet(root, "note", out var noteElement) && noteElement.ValueKind == JsonValueKind.String
                ? noteElement.GetString()
                : null;

            foreach (var property in root.EnumerateObject())
                if (!TopLevelFields.Contains(property.Name))
                    issues.Add($"unknown top-level field '{property.Name}' ignored");

            if (!TryGetEntriesArray(root, issues, out var entriesElement))
            {
                issues.Add("entries (or abilities) array is missing");
                return new ClassOverlay(schema, className, [], issues, note);
            }

            var entries = new List<ClassOverlayEntry>();
            var seenIds = new HashSet<int>();
            var index = 0;
            foreach (var element in entriesElement.EnumerateArray())
            {
                index++;
                if (!TryParseEntry(element, index, knowledge, issues, out var entry)) continue;
                if (!seenIds.Add(entry.SpellId))
                {
                    issues.Add($"entry #{index}: duplicate spellId {entry.SpellId} dropped");
                    continue;
                }
                entries.Add(entry);
            }

            return new ClassOverlay(schema, className, entries, issues, note);
        }
        catch (JsonException ex)
        {
            return ClassOverlay.Empty(issue: $"malformed JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            // The documented contract: a bad overlay is never allowed to crash
            // the companion. Everything unknown degrades to an empty overlay.
            Debug.WriteLine($"[ClassOverlayLoader] load failed: {ex}");
            return ClassOverlay.Empty(issue: $"overlay load failed: {ex.Message}");
        }
    }

    /// <summary>Load an overlay from a file path (IO failures degrade to issues).</summary>
    public static ClassOverlay LoadFile(string path) => LoadFile(path, ClassOverlayKnowledge.Default);

    public static ClassOverlay LoadFile(string path, ClassOverlayKnowledge knowledge)
    {
        try
        {
            return Load(File.ReadAllText(path), knowledge);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ClassOverlayLoader] cannot read '{path}': {ex.Message}");
            return ClassOverlay.Empty(issue: $"could not read '{path}': {ex.Message}");
        }
    }

    private static bool TryParseEntry(
        JsonElement element,
        int index,
        ClassOverlayKnowledge knowledge,
        List<string> issues,
        out ClassOverlayEntry entry)
    {
        entry = null!;
        if (element.ValueKind != JsonValueKind.Object)
        {
            issues.Add($"entry #{index}: not a JSON object dropped");
            return false;
        }

        // Allow-list + duplicate-field detection, all before any value is read.
        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!EntryFields.Contains(property.Name))
            {
                issues.Add($"entry #{index}: unknown field '{property.Name}' dropped");
                return false;
            }
            if (!fields.TryAdd(property.Name, property.Value))
            {
                issues.Add($"entry #{index}: duplicate field '{property.Name}' dropped");
                return false;
            }
        }

        if (!fields.TryGetValue("spellId", out var spellIdElement)
            || spellIdElement.ValueKind != JsonValueKind.Number
            || !spellIdElement.TryGetInt32(out var spellId)
            || spellId <= 0)
        {
            issues.Add($"entry #{index}: spellId is missing or invalid dropped");
            return false;
        }

        if (!TryParseEnum<ClassOverlayRole>(fields, "role", index, issues, out var role)) return false;
        if (!TryParseEnum<ClassOverlayRoute>(fields, "route", index, issues, out var route)) return false;
        if (!TryParseEnum<ClassOverlayMode>(fields, "mode", index, issues, out var mode)) return false;

        var emergencyEscape = false;
        if (fields.TryGetValue("emergencyEscape", out var escapeElement))
        {
            if (escapeElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                issues.Add($"entry #{index}: emergencyEscape is not a boolean dropped");
                return false;
            }
            emergencyEscape = escapeElement.GetBoolean();
        }

        var offGcd = false;
        if (fields.TryGetValue("offGcd", out var gcdElement))
        {
            if (gcdElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                issues.Add($"entry #{index}: offGcd is not a boolean dropped");
                return false;
            }
            offGcd = gcdElement.GetBoolean();
        }

        var minUrgency = 0;
        if (fields.TryGetValue("minUrgency", out var urgencyElement))
        {
            if (urgencyElement.ValueKind != JsonValueKind.Number
                || !urgencyElement.TryGetInt32(out minUrgency)
                || !AllowedUrgency.Contains(minUrgency))
            {
                issues.Add($"entry #{index}: minUrgency is not in 0-4 dropped");
                return false;
            }
        }

        string? notes = null;
        if (fields.TryGetValue("notes", out var notesElement))
        {
            if (notesElement.ValueKind != JsonValueKind.String)
            {
                issues.Add($"entry #{index}: notes is not a string dropped");
                return false;
            }
            notes = notesElement.GetString();
        }

        // Safety transform 1: verified ids only may be Auto/Suggest. Unverified
        // ids are forced Never and always carry the "needs verification" note.
        if (!knowledge.IsVerified(spellId))
        {
            if (mode is ClassOverlayMode.Auto or ClassOverlayMode.Suggest)
            {
                issues.Add($"entry #{index}: spell {spellId} is unverified; mode {mode} downgraded to Never");
                mode = ClassOverlayMode.Never;
            }
            notes = AppendNote(notes, "needs verification");
        }

        // Safety transform 2: the overlay may restrict, never loosen, the
        // curated never-automatic list.
        if (knowledge.IsNeverAutomatic(spellId) && mode != ClassOverlayMode.Never)
        {
            issues.Add($"entry #{index}: spell {spellId} is never-automatic; mode {mode} downgraded to Never");
            mode = ClassOverlayMode.Never;
        }

        entry = new ClassOverlayEntry(spellId, role, route, emergencyEscape, offGcd, mode, minUrgency, notes);
        return true;
    }

    private static bool TryParseEnum<T>(
        Dictionary<string, JsonElement> fields,
        string field,
        int index,
        List<string> issues,
        out T value)
        where T : struct, Enum
    {
        value = default;
        if (!fields.TryGetValue(field, out var element) || element.ValueKind != JsonValueKind.String)
        {
            issues.Add($"entry #{index}: {field} is missing or not a string dropped");
            return false;
        }

        var raw = element.GetString() ?? "";
        if (raw.Length == 0 || char.IsDigit(raw[0]) || !Enum.TryParse(raw, ignoreCase: true, out value) || !Enum.IsDefined(value))
        {
            issues.Add($"entry #{index}: bad {field} '{raw}' dropped");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Resolves the entry array from either the canonical <c>entries</c> key or
    /// the S3b/S3c <c>abilities</c> alias. <c>entries</c> wins when both are
    /// present, with a recorded issue; a present-but-not-an-array key is not a
    /// usable array and the other key is still tried.
    /// </summary>
    private static bool TryGetEntriesArray(JsonElement root, List<string> issues, out JsonElement entries)
    {
        var hasEntries = TryGet(root, "entries", out var entriesValue);
        var hasAbilities = TryGet(root, "abilities", out var abilitiesValue);
        if (hasEntries && hasAbilities)
            issues.Add("both 'entries' and 'abilities' present; using 'entries'");

        if (hasEntries && entriesValue.ValueKind == JsonValueKind.Array)
        {
            entries = entriesValue;
            return true;
        }
        if (hasAbilities && abilitiesValue.ValueKind == JsonValueKind.Array)
        {
            entries = abilitiesValue;
            return true;
        }
        entries = default;
        return false;
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

    private static string? AppendNote(string? notes, string addition)
    {
        if (string.IsNullOrWhiteSpace(notes)) return addition;
        return notes.Contains(addition, StringComparison.OrdinalIgnoreCase) ? notes : $"{notes}; {addition}";
    }
}
