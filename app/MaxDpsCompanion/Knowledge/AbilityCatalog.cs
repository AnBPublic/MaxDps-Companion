using System.Reflection;
using System.Text.Json;

namespace MaxDpsCompanion;

/// <summary>
/// The ability knowledge base. Two layers, both embedded resources:
///
///  * <c>Knowledge/vendor-abilities.json</c> — GENERATED from the pinned MaxDps
///    vendor snapshot by <c>tools/Extract-VendorAbilities.ps1</c>. Authoritative
///    for spell ids, names, class/spec membership and the coarse category
///    (Defensive / Offensive / Interrupt).
///  * <c>Knowledge/abilities.json</c> — CURATED policy layer: purpose, tier,
///    GCD behaviour, ranges, cooldowns/durations, hold conditions, conflict
///    groups and the per-spec Mobility/SelfHeal extra lists. Only fields that
///    differ from the category default need to be present.
///
/// Lookup is by spell id and is a pure function of the two files: the catalog
/// is deterministic, immutable after construction and has no game state.
/// The same catalog drives the generated <c>addon/MaxDpsBridge/Catalog.lua</c>
/// (see <see cref="CatalogLuaGenerator"/>) so the bridge and the companion can
/// never disagree about an ability index.
/// </summary>
internal sealed class AbilityCatalog
{
    public const string VendorResourceName = "MaxDpsCompanion.Knowledge.vendor-abilities.json";
    public const string CuratedResourceName = "MaxDpsCompanion.Knowledge.abilities.json";
    public const string ClassSpellsResourceName = "MaxDpsCompanion.Knowledge.class-spells.json";

    /// <summary>
    /// Protocol/generator revision — bumped when the index scheme changes.
    /// v4 adds the v3.3.0 Solo ladder lists (defensiveMajor + immunity) to the
    /// generated Catalog.lua; wire cell layout is unchanged.
    /// </summary>
    public const int CatalogVersion = 4;

    /// <summary>
    /// The live game patch this registry is built for (registry §38/§62). A
    /// curated file stamped with a different patch is REJECTED at load — a
    /// registry for another patch must never silently drive live 12.1.
    /// </summary>
    public const string ExpectedGamePatch = "12.1";

    /// <summary>Expected interface version for <see cref="ExpectedGamePatch"/>.</summary>
    public const int ExpectedInterfaceVersion = 120100;

    /// <summary>Pinned MaxDps vendor snapshot the registry was extracted from/checked against.</summary>
    public const string ExpectedMaxDpsVersion = "11.3.49";

    /// <summary>
    /// Class numeric ids for protocol v5 cell 25 and the generated Lua table.
    /// Fixed alphabetical order; 0 = unknown. Never renumber: it is wire format.
    /// </summary>
    public static readonly string[] ClassOrder =
    [
        "", // 0 = unknown
        "DEATHKNIGHT", "DEMONHUNTER", "DRUID", "EVOKER", "HUNTER", "MAGE",
        "MONK", "PALADIN", "PRIEST", "ROGUE", "SHAMAN", "WARLOCK", "WARRIOR",
    ];

    /// <summary>
    /// Spec ordinal per class for protocol v5 cell 25. Fixed order (vendor spec
    /// table order); 0 = unknown. Never reorder: it is wire format.
    /// </summary>
    public static readonly Dictionary<string, string[]> SpecOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DEATHKNIGHT"] = ["", "Blood", "Frost", "Unholy"],
        ["DEMONHUNTER"] = ["", "Devourer", "Havoc", "Vengeance"],
        ["DRUID"] = ["", "Balance", "Feral", "Guardian", "Restoration"],
        ["EVOKER"] = ["", "Augmentation", "Devastation", "Preservation"],
        ["HUNTER"] = ["", "Beast Mastery", "Marksmanship", "Survival"],
        ["MAGE"] = ["", "Arcane", "Fire", "Frost"],
        ["MONK"] = ["", "Brewmaster", "Mistweaver", "Windwalker"],
        ["PALADIN"] = ["", "Holy", "Protection", "Retribution"],
        ["PRIEST"] = ["", "Discipline", "Holy", "Shadow"],
        ["ROGUE"] = ["", "Assassination", "Outlaw", "Subtlety"],
        ["SHAMAN"] = ["", "Elemental", "Enhancement", "Restoration"],
        ["WARLOCK"] = ["", "Affliction", "Demonology", "Destruction"],
        ["WARRIOR"] = ["", "Arms", "Fury", "Protection"],
    };

    /// <summary>Category precedence when one spell belongs to several buckets.</summary>
    private static readonly AbilityCategory[] CategoryRank =
    [
        AbilityCategory.Interrupt, AbilityCategory.Defensive, AbilityCategory.Mobility,
        AbilityCategory.SelfHeal, AbilityCategory.Utility, AbilityCategory.Offensive,
        AbilityCategory.Consumable, AbilityCategory.Trinket, AbilityCategory.Main,
    ];

    private readonly Dictionary<int, AbilityDefinition> _byId;
    private readonly int[] _indexOf;                    // spellId -> catalog index (1-based; 0 = unknown)
    private readonly Dictionary<int, int> _idByIndex;   // catalog index -> spellId
    private readonly Dictionary<(string Class, string Spec, AbilityCategory Category), int[]> _extras;
    private readonly Dictionary<int, int[]> _aliases;

    public int Count => _byId.Count;
    public IReadOnlyCollection<AbilityDefinition> All => _byId.Values;

    /// <summary>
    /// Symmetric spell-variant aliases (curated <c>aliases</c> block, e.g.
    /// 202168 &lt;-&gt; 34428 and 19647 &lt;-&gt; 119910). The bridge uses them to
    /// resolve a keybind/macro that names a variant id; sorted for
    /// deterministic generation into <c>MDB.SpellAliases</c>.
    /// </summary>
    public IReadOnlyDictionary<int, int[]> Aliases => _aliases;

    /// <summary>Patch this registry was built for (from the curated file; "12.1" for the embedded one).</summary>
    public string GamePatch { get; }

    /// <summary>Interface version the curated file was verified against (0 = not stated).</summary>
    public int InterfaceVersion { get; }

    /// <summary>MaxDps vendor version the curated file was generated from (null = not stated).</summary>
    public string? MaxDpsVersion { get; }

    /// <summary>Date the curated file was last verified (ISO; null = not stated).</summary>
    public string? VerifiedDate { get; }

    // ---- discovery sources (v2.7 coverage manifest; no behavioral effect) ----
    // Each set is "ids a coverage source declared", before priority merging, so
    // the audit can prove nothing discovered was silently dropped.

    /// <summary>Spell ids present in the vendor extraction (Cooldowns tables).</summary>
    public IReadOnlyCollection<int> SourceVendorIds { get; }

    /// <summary>Spell ids the curated policy layer references (abilities + extras).</summary>
    public IReadOnlyCollection<int> SourceCuratedIds { get; }

    /// <summary>Class-spell ids that passed verification and were eligible to merge (including ones already carried by higher layers).</summary>
    public IReadOnlyCollection<int> SourceClassSpellIds { get; }

    /// <summary>Class-spell ids deliberately excluded (unverified in the live client, or junk tokens).</summary>
    public IReadOnlyCollection<int> SourceClassSpellFilteredIds { get; }

    private AbilityCatalog(
        Dictionary<int, AbilityDefinition> byId,
        int[] indexOf,
        Dictionary<int, int> idByIndex,
        Dictionary<(string, string, AbilityCategory), int[]> extras,
        Dictionary<int, int[]> aliases,
        string gamePatch,
        int interfaceVersion,
        string? maxDpsVersion,
        string? verifiedDate,
        IReadOnlyCollection<int> vendorIds,
        IReadOnlyCollection<int> curatedIds,
        IReadOnlyCollection<int> classSpellIds,
        IReadOnlyCollection<int> classSpellFilteredIds)
    {
        _byId = byId;
        _indexOf = indexOf;
        _idByIndex = idByIndex;
        _extras = extras;
        _aliases = aliases;
        GamePatch = gamePatch;
        InterfaceVersion = interfaceVersion;
        MaxDpsVersion = maxDpsVersion;
        VerifiedDate = verifiedDate;
        SourceVendorIds = vendorIds;
        SourceCuratedIds = curatedIds;
        SourceClassSpellIds = classSpellIds;
        SourceClassSpellFilteredIds = classSpellFilteredIds;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static AbilityCatalog? _default;

    /// <summary>The catalog built from the embedded resources (lazy, immutable).</summary>
    public static AbilityCatalog Default => _default ??= LoadEmbedded();

    /// <summary>Testable entry point: load from JSON text (vendor + curated only).</summary>
    public static AbilityCatalog Load(string vendorJson, string curatedJson) =>
        Load(vendorJson, curatedJson, classSpellsJson: null, verificationJson: null);

    /// <summary>
    /// Testable entry point with the optional generated class-spells layer
    /// (vendor SpellData tokens, merged as the lowest-priority knowledge so
    /// the class-skills screen can toggle abilities nobody curated).
    /// </summary>
    public static AbilityCatalog Load(string vendorJson, string curatedJson, string? classSpellsJson) =>
        Load(vendorJson, curatedJson, classSpellsJson, verificationJson: null);

    /// <summary>
    /// Full load: the class-spells layer is filtered against the official
    /// live-client verification when it is supplied, so ids that no longer
    /// exist in the current build are never merged.
    /// </summary>
    public static AbilityCatalog Load(
        string vendorJson, string curatedJson, string? classSpellsJson, string? verificationJson)
    {
        var vendor = JsonSerializer.Deserialize<VendorFile>(vendorJson, JsonOptions)
            ?? throw new InvalidDataException("vendor-abilities.json did not parse");
        var curated = JsonSerializer.Deserialize<CuratedFile>(curatedJson, JsonOptions)
            ?? throw new InvalidDataException("abilities.json did not parse");

        // 0. Patch guard (registry §38/§62): a curated file stamped with a
        //    different patch must never silently drive live 12.1. Absent
        //    metadata (small test fixtures) is accepted; a present mismatch
        //    fails loud, and the metadata is carried on the catalog so the
        //    audit can report it.
        if (!string.IsNullOrWhiteSpace(curated.GamePatch)
            && !string.Equals(curated.GamePatch!.Trim(), ExpectedGamePatch, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"ability registry is stamped for patch {curated.GamePatch} but this build targets {ExpectedGamePatch}");
        if (curated.InterfaceVersion is > 0 and not ExpectedInterfaceVersion)
            throw new InvalidDataException(
                $"ability registry interface {curated.InterfaceVersion} does not match {ExpectedInterfaceVersion}");

        // 1. Base rows: group by spell id. Class/spec membership + categories.
        var rows = new Dictionary<int, List<VendorEntry>>();
        var names = new Dictionary<int, string>();
        foreach (var entry in vendor.Entries ?? [])
        {
            if (entry.Id <= 0) continue;
            if (!rows.TryGetValue(entry.Id, out var list))
            {
                list = [];
                rows[entry.Id] = list;
                names[entry.Id] = entry.Name;
            }
            list.Add(entry);
            // Prefer the shortest name variant (vendor sometimes aliases).
            if (entry.Name.Length < names[entry.Id].Length) names[entry.Id] = entry.Name;
        }

        // 2. Curated overrides, indexed by spell id.
        var overrides = new Dictionary<int, AbilityOverride>();
        foreach (var o in curated.Abilities ?? [])
        {
            if (o.Id <= 0) continue;
            overrides[o.Id] = o;
            if (!string.IsNullOrWhiteSpace(o.Name)) names[o.Id] = o.Name!;
        }

        // 3. Extras: spell ids that only exist because the curated file uses them.
        var extras = new Dictionary<(string, string, AbilityCategory), int[]>();
        foreach (var (className, bySpec) in curated.Extras ?? [])
        {
            if (bySpec is null) continue;
            foreach (var (specName, specExtras) in bySpec)
            {
                if (specExtras is null) continue;
                var mobility = (specExtras.Mobility ?? []).Where(id => id > 0).Distinct().ToArray();
                var selfHeal = (specExtras.SelfHeal ?? []).Where(id => id > 0).Distinct().ToArray();
                var offensive = (specExtras.Offensive ?? []).Where(id => id > 0).Distinct().ToArray();
                if (mobility.Length > 0) extras[(className, specName, AbilityCategory.Mobility)] = mobility;
                if (selfHeal.Length > 0) extras[(className, specName, AbilityCategory.SelfHeal)] = selfHeal;
                if (offensive.Length > 0) extras[(className, specName, AbilityCategory.Offensive)] = offensive;
                foreach (var id in mobility) overrides.TryAdd(id, new AbilityOverride { Id = id, Category = "Mobility" });
                foreach (var id in selfHeal) overrides.TryAdd(id, new AbilityOverride { Id = id, Category = "SelfHeal" });
                // NOTE: offensive list ids are deliberately NOT given a synthetic
                // override. The curated offensive list only references ids that
                // are already catalogued (vendor Offensive rows or a curated
                // entry), and flipping a vendor row's provenance to Curated here
                // would turn a delegated MaxDpsBacked offensive into a
                // companion-backed one the audit (rightly) demands usage data
                // for. The list membership alone is the gap-fill source.
            }
        }

        // 4. Materialize every definition.
        var patch = string.IsNullOrWhiteSpace(curated.GamePatch) ? ExpectedGamePatch : curated.GamePatch!.Trim();
        var byId = new Dictionary<int, AbilityDefinition>();
        foreach (var (id, list) in rows)
            byId[id] = Materialize(id, names[id], list, overrides.GetValueOrDefault(id), curated.Verified, patch);
        foreach (var (id, o) in overrides)
        {
            if (byId.ContainsKey(id)) continue;
            if (string.IsNullOrWhiteSpace(o.Name))
                throw new InvalidDataException($"curated ability {id} has no name and is absent from the vendor table");
            byId[id] = Materialize(id, o.Name!, [], o, curated.Verified, patch);
        }

        // 4c. Curated extras imply class/spec membership: an id only listed as
        //     "Warrior/Fury mobility" must still appear as a Warrior/Fury entry
        //     in the registry (audit coverage matrix + inspector).
        foreach (var ((className, specName, _), ids) in extras)
        {
            foreach (var id in ids)
            {
                if (!byId.TryGetValue(id, out var definition)) continue;
                byId[id] = definition with
                {
                    Classes = AppendMember(definition.Classes, className),
                    Specs = AppendMember(definition.Specs, specName),
                };
            }
        }

        // 4d. Curated spell aliases (variant ids the bridge may meet on a bar
        //     or in a macro). Stored symmetric so a lookup on either id finds
        //     the other, and validated against the catalog so a typo cannot
        //     silently generate a dangling Lua entry.
        var aliases = new Dictionary<int, int[]>();
        if (curated.Aliases is { Count: > 0 })
        {
            var symmetric = new Dictionary<int, SortedSet<int>>();
            void Link(int from, int to)
            {
                if (!byId.ContainsKey(from) || !byId.ContainsKey(to))
                    throw new InvalidDataException($"alias {from} -> {to} references an uncatalogued spell");
                if (!symmetric.TryGetValue(from, out var set)) symmetric[from] = set = [];
                set.Add(to);
            }
            foreach (var (from, targets) in curated.Aliases)
            {
                if (from <= 0 || targets is null) continue;
                foreach (var to in targets)
                {
                    if (to <= 0 || to == from) continue;
                    Link(from, to);
                    Link(to, from);
                }
            }
            foreach (var (id, set) in symmetric) aliases[id] = set.ToArray();
        }

        // 4b. Class-spells layer (LOWEST priority): raw vendor SpellData tokens
        // merged only for ids nobody else carries, so curated > Cooldowns >
        // class spells. Junk/passive tokens are skipped (they can never arrive
        // as suggestions); movement tokens stay manual-by-design; and whenever
        // the official live-client verification is present, ids that are no
        // longer in the current build are dropped entirely.
        var classSpellIds = new List<int>();
        var classSpellFilteredIds = new List<int>();
        if (classSpellsJson is not null)
            MergeClassSpells(ClassSpellBook.Load(classSpellsJson, verificationJson), byId, patch,
                classSpellIds, classSpellFilteredIds);

        // 5. Stable index assignment: ascending spell id, 1-based (0 = unknown).
        var ordered = byId.Keys.OrderBy(id => id).ToArray();
        var maxId = ordered.Length == 0 ? 0 : ordered[^1];
        var indexOf = new int[Math.Max(1, maxId + 1)];
        var idByIndex = new Dictionary<int, int>(ordered.Length);
        for (var i = 0; i < ordered.Length; i++)
        {
            indexOf[ordered[i]] = i + 1;
            idByIndex[i + 1] = ordered[i];
        }

        // 6. Validate extras reference real catalog entries.
        foreach (var ((className, specName, category), ids) in extras)
        {
            foreach (var id in ids)
            {
                if (!byId.ContainsKey(id))
                    throw new InvalidDataException($"extras {className}/{specName}/{category} references uncatalogued spell {id}");
            }
        }

        return new AbilityCatalog(
            byId, indexOf, idByIndex, extras, aliases,
            string.IsNullOrWhiteSpace(curated.GamePatch) ? ExpectedGamePatch : curated.GamePatch!.Trim(),
            curated.InterfaceVersion,
            curated.MaxDpsVersion,
            curated.Verified,
            rows.Keys.ToArray(),
            overrides.Keys.ToArray(),
            classSpellIds,
            classSpellFilteredIds);
    }

    private static AbilityCatalog LoadEmbedded()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return Load(
            Read(assembly, VendorResourceName),
            Read(assembly, CuratedResourceName),
            Read(assembly, ClassSpellsResourceName),
            Read(assembly, ClassSpellBook.VerificationResourceName));
    }

    private static string Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidDataException($"embedded resource missing: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Catalog index for a spell id; 0 when the spell is not catalogued.</summary>
    public int IndexOf(int spellId) =>
        spellId > 0 && spellId < _indexOf.Length ? _indexOf[spellId] : 0;

    /// <summary>Spell id for a catalog index; 0 when the index is out of range.</summary>
    public int SpellIdAt(int index) => index >= 0 && _idByIndex.TryGetValue(index, out var id) ? id : 0;

    public AbilityDefinition? TryGet(int spellId) =>
        _byId.TryGetValue(spellId, out var definition) ? definition : null;

    public AbilityDefinition? TryGetByIndex(int index) => TryGet(SpellIdAt(index));

    /// <summary>Curated Mobility/SelfHeal list for a class+spec (empty when none).</summary>
    public int[] Extras(string? className, string? specName, AbilityCategory category)
    {
        if (className is null || specName is null) return [];
        return _extras.TryGetValue((className, specName, category), out var ids) ? ids : [];
    }

    /// <summary>
    /// Defensive candidates for the bridge's Red-urgency gap-fill: every
    /// vendor-listed defensive for the class+spec that is survival-purpose,
    /// not never-automatic and not a full immunity (a blind immunity can cost
    /// the player's own actions; MaxDps-recommended immunities still fire via
    /// the normal barrier slot). Derived from the vendor pin's per-spec
    /// defensive membership — not hand-maintained — and ordered most
    /// impactful first: Major before Minor/None, then curated Priority, then
    /// ascending spell id. Deterministic.
    /// </summary>
    public int[] DefensiveGapFill(string? className, string? specName)
    {
        if (className is null || specName is null) return [];
        var candidates = new List<AbilityDefinition>();
        foreach (var ability in _byId.Values)
        {
            if (!ability.IsSurvival || ability.NeverAutomatic) continue;
            // Only curated/vendor abilities may be gap-filled: the class-spells
            // layer is a best-effort model and must not widen what the bridge
            // offers when MaxDps names no defensive.
            if (ability.Provenance == AbilityProvenance.ClassSpell) continue;
            if (ability.Tier == DefensiveTier.Immunity || ability.Purpose == AbilityPurpose.Immunity) continue;
            if (!MembersContain(ability.Classes, className) || !MembersContain(ability.Specs, specName)) continue;
            candidates.Add(ability);
        }
        candidates.Sort((a, b) =>
        {
            var rank = TierRank(b.Tier).CompareTo(TierRank(a.Tier));
            if (rank != 0) return rank;
            var priority = b.Priority.CompareTo(a.Priority);
            if (priority != 0) return priority;
            return a.SpellId.CompareTo(b.SpellId);
        });
        return candidates.Select(a => a.SpellId).ToArray();
    }

    /// <summary>
    /// Short-cooldown defensive gap-fill for the bridge's Orange tier (v3.0.0):
    /// the same derivation as <see cref="DefensiveGapFill"/> but restricted to
    /// Minor/None-tier mitigation — the "short CDs" the user turned on that
    /// never fired because the old gap-fill only offered candidates at Red.
    /// Majors/immunities are deliberately excluded: they still require Red.
    /// Deterministic (Tier, then curated Priority, then ascending id).
    /// </summary>
    public int[] DefensiveGapFillMinor(string? className, string? specName)
    {
        if (className is null || specName is null) return [];
        var candidates = new List<AbilityDefinition>();
        foreach (var ability in _byId.Values)
        {
            if (!ability.IsDefensive || ability.NeverAutomatic) continue;
            if (ability.Provenance == AbilityProvenance.ClassSpell) continue;
            if (ability.Tier is DefensiveTier.Major or DefensiveTier.Immunity) continue;
            if (ability.Purpose == AbilityPurpose.Immunity) continue;
            if (!MembersContain(ability.Classes, className) || !MembersContain(ability.Specs, specName)) continue;
            candidates.Add(ability);
        }
        candidates.Sort((a, b) =>
        {
            var priority = b.Priority.CompareTo(a.Priority);
            if (priority != 0) return priority;
            return a.SpellId.CompareTo(b.SpellId);
        });
        return candidates.Select(a => a.SpellId).ToArray();
    }

    /// <summary>
    /// Major-only defensive gap-fill for the Solo escalation ladder (v3.3.0):
    /// the same derivation as <see cref="DefensiveGapFill"/> restricted to
    /// Major-tier mitigation. The bridge offers these only in Solo at/below
    /// the major HP band; group Red-tier behavior keeps using
    /// <see cref="DefensiveGapFill"/>. Deterministic (Priority, then id).
    /// </summary>
    public int[] DefensiveGapFillMajor(string? className, string? specName)
    {
        if (className is null || specName is null) return [];
        var candidates = new List<AbilityDefinition>();
        foreach (var ability in _byId.Values)
        {
            if (!ability.IsSurvival || ability.NeverAutomatic) continue;
            if (ability.Provenance == AbilityProvenance.ClassSpell) continue;
            if (ability.Tier != DefensiveTier.Major) continue;
            if (ability.Purpose == AbilityPurpose.Immunity) continue;
            if (!MembersContain(ability.Classes, className) || !MembersContain(ability.Specs, specName)) continue;
            candidates.Add(ability);
        }
        candidates.Sort((a, b) =>
        {
            var priority = b.Priority.CompareTo(a.Priority);
            if (priority != 0) return priority;
            return a.SpellId.CompareTo(b.SpellId);
        });
        return candidates.Select(a => a.SpellId).ToArray();
    }

    /// <summary>
    /// Immunity gap-fill for the bottom Solo escalation band (v3.3.0): full
    /// immunities the bridge may offer ONLY in Solo at/below the immunity HP
    /// band, when MaxDps names no defensive. Never offered in groups; the
    /// companion policy additionally requires no active immunity and honors
    /// the per-ability ON/OFF switch. Deterministic (Priority, then id).
    /// </summary>
    public int[] ImmunityGapFill(string? className, string? specName)
    {
        if (className is null || specName is null) return [];
        var candidates = new List<AbilityDefinition>();
        foreach (var ability in _byId.Values)
        {
            if (ability.NeverAutomatic) continue;
            if (ability.Provenance == AbilityProvenance.ClassSpell) continue;
            if (ability.Tier != DefensiveTier.Immunity && ability.Purpose != AbilityPurpose.Immunity) continue;
            if (!MembersContain(ability.Classes, className) || !MembersContain(ability.Specs, specName)) continue;
            candidates.Add(ability);
        }
        candidates.Sort((a, b) =>
        {
            var priority = b.Priority.CompareTo(a.Priority);
            if (priority != 0) return priority;
            return a.SpellId.CompareTo(b.SpellId);
        });
        return candidates.Select(a => a.SpellId).ToArray();
    }

    /// <summary>
    /// preference order (shared burst first, spec-specific second). Unlike the
    /// defensive list this is HAND-CURATED in <c>abilities.json</c> because the
    /// vendor's offensive bucket mixes true burst windows with rotational /
    /// resource fillers the companion must never fire blind. The bridge offers
    /// the first ready+bound entry only when MaxDps names no bound offensive,
    /// and the companion detects the gap-fill by id membership (there is no
    /// spare wire source bit — PixelProtocol decode is frozen).
    /// </summary>
    public int[] OffensiveGapFill(string? className, string? specName) =>
        Extras(className, specName, AbilityCategory.Offensive);

    /// <summary>True when the spell is one of the curated offensive gap-fill entries for the class+spec.</summary>
    public bool IsOffensiveGapFill(string? className, string? specName, int spellId)
    {
        if (spellId <= 0) return false;
        foreach (var id in OffensiveGapFill(className, specName))
            if (id == spellId) return true;
        return false;
    }

    /// <summary>
    /// v3.4.0 CC appendix read path: the curated crowd-control entry for a
    /// class+spec+spell id, or null when the spell is not a curated CC row.
    /// Verified ids only (see <see cref="CrowdControlCatalog"/>). Read-only —
    /// it does not alter any existing catalog behaviour.
    /// </summary>
    public CrowdControlEntry? CrowdControlFor(string? className, string? specName, int spellId) =>
        CrowdControlCatalog.Find(className, specName, spellId);

    /// <summary>All curated crowd-control entries for a class+spec (verified ids only).</summary>
    public IReadOnlyList<CrowdControlEntry> CrowdControlFor(string? className, string? specName) =>
        CrowdControlCatalog.For(className, specName);

    /// <summary>
    /// v3.4.0 CC candidate read path (Option A): the AUTO-ELIGIBLE curated
    /// crowd-control ids for a class+spec, in curated preference order, that
    /// the generator emits as the bridge's per-spec <c>cc</c> extras list. The
    /// bridge offers the first ready+bound entry in the reused Interrupt slot
    /// ONLY when MaxDps names no usable interrupt; the companion's
    /// <see cref="CrowdControlGate"/> remains the authority on firing it.
    /// MaxDps-owned stuns (Storm Bolt, Shockwave, …) stay AutoEligible=false
    /// and are never emitted here, so MaxDps authority is preserved. Verified
    /// ids only (see <see cref="CrowdControlCatalog"/>). Read-only — it does
    /// not alter any existing catalog behaviour.
    /// </summary>
    public int[] CrowdControlGapFill(string? className, string? specName)
    {
        if (className is null || specName is null) return [];
        var ids = new List<int>();
        foreach (var entry in CrowdControlCatalog.For(className, specName))
            if (entry.AutoEligible) ids.Add(entry.SpellId);
        return ids.ToArray();
    }

    private static int TierRank(DefensiveTier tier) => tier switch
    {
        DefensiveTier.Major => 3,
        DefensiveTier.Minor => 2,
        DefensiveTier.None => 1,
        _ => 0,
    };

    private static bool MembersContain(IReadOnlyList<string> members, string name)
    {
        for (var i = 0; i < members.Count; i++)
            if (string.Equals(members[i], name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static IReadOnlyList<string> AppendMember(IReadOnlyList<string> members, string name)
    {
        if (MembersContain(members, name)) return members;
        var list = new List<string>(members) { name };
        return list;
    }

    public static int ClassId(string? className)
    {
        if (className is null) return 0;
        for (var i = 1; i < ClassOrder.Length; i++)
            if (string.Equals(ClassOrder[i], className, StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }

    public static string? ClassName(int classId) =>
        classId > 0 && classId < ClassOrder.Length ? ClassOrder[classId] : null;

    public static int SpecId(string className, string? specName)
    {
        if (specName is null || !SpecOrder.TryGetValue(className, out var specs)) return 0;
        for (var i = 1; i < specs.Length; i++)
            if (string.Equals(specs[i], specName, StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }

    public static string? SpecName(string className, int specId) =>
        SpecOrder.TryGetValue(className, out var specs) && specId > 0 && specId < specs.Length
            ? specs[specId]
            : null;

    // ---- class-spells layer ------------------------------------------------

    /// <summary>
    /// Movement keywords, matched on whole decoded-name words ("Shadowstep"
    /// ends with "step", "Heroic Leap" has "Leap") so substrings cannot fire
    /// ("Supercharger" is not a movement ability).
    /// </summary>
    private static readonly string[] MovementWords =
        ["step", "charge", "sprint", "blink", "dash", "torpedo", "disengage", "grapple",
         "harpoon", "gateway", "teleport", "hover", "roll", "steed", "gust", "leap", "hook", "fly"];

    /// <summary>
    /// Defensive keywords, matched on whole words. Attack verbs veto the
    /// classification ("Shield Slam" is a rotational attack, "Shield Wall" is
    /// a defensive).
    /// </summary>
    private static readonly string[] DefensiveWords =
        ["shield", "wall", "block", "barrier", "fortitude", "fortification", "guard", "ward",
         "aegis", "bulwark", "bastion", "carapace", "shell", "bark", "skin", "protection",
         "sacrifice", "absorb", "immunity", "vanish", "evasion", "feint", "cloak", "resolve",
         "determination", "survival", "instinct", "regeneration", "heal", "mend", "blessing",
         "stand", "deflect", "shroud", "dodge", "parry", "soothe"];

    private static readonly string[] OffensiveWords =
        ["avatar", "recklessness", "metamorphosis", "ascendance", "combustion", "crusade",
         "fury", "blades", "might", "wrath", "ascension", "demonic", "tyrant", "darkness",
         "icon", "wings", "stormkeeper", "ancestral"];

    private static readonly string[] AttackWords =
        ["slam", "strike", "cleave", "smash", "throw", "shot", "kick", "punch", "blast",
         "nova", "storm", "breath", "rend", "bite", "stab", "slash", "chop", "lash",
         "volley", "barrage", "bolt", "spike", "fang", "claw", "maim", "wound"];

    /// <summary>
    /// Modeled category for a vendor SpellData token. Deliberately
    /// conservative: movement wins by verb, defense wins by noun unless the
    /// ability is an attack verb, offense needs a strong burst word, and
    /// everything uncertain lands in Main (the catch-all section).
    /// </summary>
    internal static ClassSkillGroup ClassifyToken(string token, string decodedName)
    {
        var words = decodedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lower = new string[words.Length];
        for (var i = 0; i < words.Length; i++) lower[i] = words[i].Trim(':', '\'', '.').ToLowerInvariant();

        // Movement: any word equals or ends with a movement verb.
        for (var i = 0; i < lower.Length; i++)
        {
            foreach (var verb in MovementWords)
            {
                if (lower[i] == verb || lower[i].EndsWith(verb, StringComparison.Ordinal)) return ClassSkillGroup.Movement;
            }
        }

        var isAttack = false;
        for (var i = 0; i < lower.Length; i++)
        {
            foreach (var attack in AttackWords)
            {
                if (lower[i] == attack) { isAttack = true; break; }
            }
            if (isAttack) break;
        }

        for (var i = 0; i < lower.Length; i++)
        {
            foreach (var defensive in DefensiveWords)
            {
                if (lower[i] != defensive) continue;
                return isAttack ? ClassSkillGroup.Main : ClassSkillGroup.Defensive;
            }
        }

        for (var i = 0; i < lower.Length; i++)
        {
            foreach (var offensive in OffensiveWords)
            {
                if (lower[i] == offensive) return ClassSkillGroup.Offensive;
            }
        }

        return ClassSkillGroup.Main;
    }

    /// <summary>
    /// Merges the class-spells book as the lowest-priority layer. Only ids
    /// nobody else carries are added; memberships from every row of the id are
    /// unioned so the screen can group the ability across specs. Junk tokens
    /// are skipped entirely (they never arrive as suggestions), and movement
    /// tokens stay manual-by-design (`NeverAutomatic`).
    /// </summary>
    private static void MergeClassSpells(
        ClassSpellBook book, Dictionary<int, AbilityDefinition> byId, string patch,
        List<int> mergedIds, List<int> filteredIds)
    {
        var grouped = new Dictionary<int, (string Name, string Token, List<string> Classes, List<string> Specs)>();
        foreach (var entry in book.All)
        {
            if (entry.IsJunk || !entry.Verified)
            {
                if (!filteredIds.Contains(entry.SpellId)) filteredIds.Add(entry.SpellId);
                continue;
            }
            if (!mergedIds.Contains(entry.SpellId)) mergedIds.Add(entry.SpellId);
            if (byId.ContainsKey(entry.SpellId)) continue;
            if (!grouped.TryGetValue(entry.SpellId, out var acc))
            {
                acc = (entry.Name, entry.Token, [], []);
                grouped[entry.SpellId] = acc;
            }
            if (!acc.Classes.Contains(entry.Class, StringComparer.OrdinalIgnoreCase)) acc.Classes.Add(entry.Class);
            if (!acc.Specs.Contains(entry.Spec, StringComparer.OrdinalIgnoreCase)) acc.Specs.Add(entry.Spec);
        }

        foreach (var (id, acc) in grouped)
        {
            var group = ClassifyToken(acc.Token, acc.Name);
            var category = group switch
            {
                ClassSkillGroup.Offensive => AbilityCategory.Offensive,
                ClassSkillGroup.Defensive => AbilityCategory.Defensive,
                ClassSkillGroup.Movement => AbilityCategory.Mobility,
                _ => AbilityCategory.Main,
            };
            var defaults = Defaults(id, acc.Name, category, acc.Classes.ToArray(), acc.Specs.ToArray());
            var merged = defaults with
            {
                Purpose = group switch
                {
                    ClassSkillGroup.Offensive => AbilityPurpose.MajorOffensive,
                    ClassSkillGroup.Defensive => AbilityPurpose.DefensiveMajor,
                    ClassSkillGroup.Movement => AbilityPurpose.Movement,
                    _ => AbilityPurpose.Rotational,
                },
                NeverAutomatic = group == ClassSkillGroup.Movement,
                Provenance = AbilityProvenance.ClassSpell,
                Source = "classSpells",
                // The modeled layer is honest about itself: Main/Offensive/
                // Defensive rows are Incomplete (and MaxDps-only: the companion
                // never generates them independently), movement rows are
                // manual-by-design. The audit enforces the distinction.
                Status = group == ClassSkillGroup.Movement
                    ? IntelligenceStatus.ManualByDesign
                    : IntelligenceStatus.Incomplete,
                Automation = group == ClassSkillGroup.Movement
                    ? AutomationContext.Manual
                    : AutomationContext.MaxDpsOnly,
                MaxDps = MaxDpsRelationship.NotSurfaced,
            };
            byId[id] = WithIntelligence(merged, [], null, null, patch);
        }
    }

    // ---- defaults ---------------------------------------------------------

    private static AbilityDefinition Materialize(
        int spellId, string name, List<VendorEntry> rows, AbilityOverride? o, string? verifiedDate, string patch)
    {
        var categories = rows.Select(r => ParseCategory(r.Category)).ToList();
        if (o?.Category is { Length: > 0 } overrideCategory)
        {
            var parsed = ParseCategory(overrideCategory);
            if (!categories.Contains(parsed)) categories.Add(parsed);
        }
        var category = CategoryRank.FirstOrDefault(c => categories.Contains(c), AbilityCategory.Main);

        // Membership: vendor rows first, then curated statement (a curated-only
        // ability must state its class/spec: the audit reports entries with no
        // membership honestly). Extras-derived membership is added in Load.
        var classes = rows.Select(r => r.Class)
            .Concat(o?.Classes ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var specs = rows.Select(r => r.Spec)
            .Concat(o?.Specs ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        // Category defaults first; every curated field then overrides only
        // itself (null = keep the default).
        var def = Defaults(spellId, name, category, classes, specs);
        if (o is null) return WithIntelligence(def, rows, o, verifiedDate, patch);

        // A non-survival purpose (escape/movement/dispel/offensive...) never
        // carries a mitigation tier: the tier drives defensive sequencing, and
        // an ability the policy would never use as mitigation must not block a
        // real defensive through the overlap rule. Symmetrically, a curated
        // GapCloser is automatic-capable by definition (the policy still gates
        // it on confirmed out-of-melee + in-range); everything else in the
        // Mobility category (movement/escape) stays never-automatic.
        var purpose = o.Purpose is null ? def.Purpose : ParseEnum(o.Purpose, def.Purpose);
        var tier = o.Tier is null ? def.Tier : ParseEnum(o.Tier, def.Tier);
        if (!IsSurvivalPurpose(purpose)) tier = DefensiveTier.None;
        var neverAutomatic = o.NeverAutomatic ?? def.NeverAutomatic;
        if (purpose == AbilityPurpose.GapCloser) neverAutomatic = false;
        var minimumUrgency = MinimumUrgencyFor(purpose, tier);

        def = def with
        {
            Name = string.IsNullOrWhiteSpace(o.Name) ? def.Name : o.Name!,
            Category = o.Category is null ? def.Category : ParseCategory(o.Category),
            Purpose = purpose,
            Tier = tier,
            MinimumUrgency = o.MinimumUrgency is null
                ? minimumUrgency
                : ParseEnum(o.MinimumUrgency, minimumUrgency),
            MinimumUrgencyCurated = o.MinimumUrgency is not null,
            UrgencySource = o.UrgencySource is null
                ? def.UrgencySource
                : ParseEnum(o.UrgencySource, def.UrgencySource),
            Gcd = o.Gcd is null ? def.Gcd : ParseEnum(o.Gcd, def.Gcd),
            // Curated GCD data is verified; the category defaults for the
            // intrinsically off-GCD categories (interrupts, items) are verified
            // by game design. Everything else stays unverified and therefore
            // conservative under cast/channel holds.
            GcdVerified = o.Gcd is not null || def.GcdVerified,
            Range = o.Range is null ? def.Range : ParseEnum(o.Range, def.Range),
            Cast = o.Cast is null ? def.Cast : ParseEnum(o.Cast, def.Cast),
            CooldownMs = o.CdMs ?? def.CooldownMs,
            DurationMs = o.DurMs ?? def.DurationMs,
            HealPctMaxHp = o.HealPct ?? def.HealPctMaxHp,
            RequiresEnemyCast = o.RequiresEnemyCast ?? def.RequiresEnemyCast,
            TargetRange = o.TargetRange is null ? def.TargetRange : ParseEnum(o.TargetRange, def.TargetRange),
            RequiresTarget = o.RequiresTarget ?? def.RequiresTarget,
            UseBelowHpPct = o.UseBelowHpPct ?? def.UseBelowHpPct,
            HoldAboveHpPct = o.HoldAboveHpPct ?? def.HoldAboveHpPct,
            HoldWhenBuffActive = o.HoldWhenBuffActive ?? def.HoldWhenBuffActive,
            NeverAutomatic = neverAutomatic,
            ConflictGroup = o.Group ?? def.ConflictGroup,
            Priority = o.Priority ?? def.Priority,
            Unknown = o.UnknownPolicy is null ? def.Unknown : ParseEnum(o.UnknownPolicy, def.Unknown),
            Note = o.Note ?? def.Note,
            Source = o.Source ?? def.Source,
            ResetHint = o.ResetHint ?? def.ResetHint,
            Provenance = AbilityProvenance.Curated,
        };

        return WithIntelligence(def, rows, o, verifiedDate, patch);
    }

    /// <summary>
    /// Derives the v2.6/v2.7 ability-intelligence fields from the materialized
    /// definition plus the curated overrides. Everything derivable is derived
    /// (so every catalog entry has an answer); only fields that need judgement
    /// are curated, and their absence is visible through
    /// <see cref="IntelligenceStatus"/> + <see cref="IntelligenceCompleteness"/>
    /// (registry §7/§33/§56, v2.7 §4-§7).
    /// </summary>
    private static AbilityDefinition WithIntelligence(
        AbilityDefinition def, List<VendorEntry> rows, AbilityOverride? o, string? verifiedDate, string patch)
    {
        var status = DeriveStatus(def, o);
        var automation = DeriveAutomation(def, o);
        if (automation == AutomationContext.Manual
            && status is not (IntelligenceStatus.ManualByDesign or IntelligenceStatus.UnsafeToAutomate))
            status = IntelligenceStatus.ManualByDesign;
        var ownership = DeriveOwnership(def, o, status, automation);
        var sourceType = DeriveSourceType(def, rows, o);
        var ttk = ParseTtkCuration(o);
        return def with
        {
            Kind = o?.Kind is { Length: > 0 } k ? ParseEnum(k, AbilityKind.ActiveCombatAbility) : AbilityKind.ActiveCombatAbility,
            Status = status,
            Automation = automation,
            MaxDps = DeriveMaxDps(rows),
            Opportunity = o?.OpportunityCost is { Length: > 0 } oc
                ? ParseEnum(oc, OpportunityCost.Unknown)
                : OpportunityFor(def.Tier, def.CooldownMs),
            InterruptKind = o?.InterruptKind is { Length: > 0 } ik
                ? ParseEnum(ik, InterruptKind.Unknown)
                : def.Category == AbilityCategory.Interrupt || def.Purpose == AbilityPurpose.Interrupt
                    ? InterruptKind.Dedicated
                    : InterruptKind.Unknown,
            OffensiveUsage = o?.OffensiveUsage is { Length: > 0 } ou
                ? ParseEnum(ou, OffensiveUsage.Unknown)
                : DeriveOffensiveUsage(def),
            MobilityKind = o?.MobilityKind is { Length: > 0 } mk
                ? ParseEnum(mk, MobilityKind.Unknown)
                : DeriveMobilityKind(def.Purpose),
            Requires = DeriveRequirements(def, o),
            TalentNote = o?.Talent,
            HeroTalentNote = o?.HeroTalent,
            PatchVerified = o?.PatchVerified ?? verifiedDate,
            Relations = ParseRelations(o?.Relations),
            CapabilityTags = o?.Capabilities ?? [],
            EnemyCountMin = o?.EnemyCountMin,
            HoldForBurst = o?.HoldForBurst ?? false,
            MinTtkSec = ttk.MinTtkSec,
            ExecuteBelowPct = ttk.ExecuteBelowPct,
            ExecuteFavored = ttk.ExecuteFavored,
            // v2.7 coverage registry (§4-§7): ownership + completeness are
            // independent axes; every MaxDps-owned entry carries a derived or
            // curated delegation reason; every manual entry carries a manual
            // reason; every entry carries patch metadata so staleness is
            // detectable instead of silent.
            Ownership = ownership,
            Completeness = DeriveCompleteness(def, o, status, ownership),
            Delegation = DeriveDelegation(def, o, status, ownership),
            DelegationNote = o?.DelegationNote,
            ManualReason = DeriveManualReason(def, o, ownership),
            IntroducedPatch = o?.IntroducedPatch,
            SourcePatch = o?.SourcePatch ?? patch,
            LastValidatedPatch = o?.LastValidatedPatch ?? patch,
            SourceType = sourceType,
            SourceUrl = o?.SourceUrl,
            SourceConfidence = o?.SourceConfidence is { Length: > 0 } sc
                ? ParseEnum(sc, SourceConfidence.Unknown)
                : ConfidenceFor(sourceType),
            LiveVerified = o?.LiveVerified ?? false,
        };
    }

    /// <summary>The optional v3.2.0 TTK curation fields, parsed from one override.</summary>
    internal readonly record struct TtkCuration(double? MinTtkSec, int? ExecuteBelowPct, bool ExecuteFavored);

    /// <summary>
    /// Parses the optional TTK curation fields. Split out from the materializer
    /// so the present/absent/default behaviour is directly testable without a
    /// curated-file edit (abilities.json is owned by workstream T-B).
    /// </summary>
    internal static TtkCuration ParseTtkCuration(AbilityOverride? o) =>
        new(o?.MinTtkSec, o?.ExecuteBelowPct, o?.ExecuteFavored ?? false);

    /// <summary>
    /// Who owns the when-to-use decision (v2.7 §4). Deterministic:
    /// manual wins; class-spell-derived delegation is MaxDps; the companion
    /// decides for interrupts / self-sustains / mobility (it has the real
    /// gates); defensives are Shared (MaxDps surfaces, companion gates);
    /// main/offensive/items are MaxDps's (the companion only allows or
    /// safety-blocks them). Curated `ownership` can override with a reason.
    /// </summary>
    private static IntelligenceOwnership DeriveOwnership(
        AbilityDefinition def, AbilityOverride? o, IntelligenceStatus status, AutomationContext automation)
    {
        if (o?.Ownership is { Length: > 0 } own
            && Enum.TryParse<IntelligenceOwnership>(own, true, out var parsed))
            return parsed;
        if (automation == AutomationContext.Manual || def.NeverAutomatic) return IntelligenceOwnership.Manual;
        if (automation == AutomationContext.MaxDpsOnly) return IntelligenceOwnership.MaxDps;
        if (status is IntelligenceStatus.Incomplete or IntelligenceStatus.Unknown)
            return IntelligenceOwnership.Unavailable;
        // Explicit item buckets are MaxDps's regardless of their heal/defensive shape.
        if (def.Category is AbilityCategory.Consumable or AbilityCategory.Trinket)
            return IntelligenceOwnership.MaxDps;
        // Purpose first: the curated purpose is the ability's actual role, and it
        // is what the policy branches on (a self-heal curated without an explicit
        // category is still a self-heal, not a main-rotation MaxDps suggestion).
        if (def.Purpose == AbilityPurpose.SelfHeal
            || def.Purpose == AbilityPurpose.Interrupt
            || def.Purpose is AbilityPurpose.GapCloser or AbilityPurpose.Movement or AbilityPurpose.Escape)
            return IntelligenceOwnership.Companion;
        if (def.IsDefensive || def.Purpose == AbilityPurpose.External)
            return IntelligenceOwnership.Shared;
        if (def.Purpose is AbilityPurpose.MajorOffensive or AbilityPurpose.MinorOffensive or AbilityPurpose.Rotational)
            return IntelligenceOwnership.MaxDps;
        return def.Category switch
        {
            AbilityCategory.Defensive => IntelligenceOwnership.Shared,
            AbilityCategory.Interrupt or AbilityCategory.SelfHeal or AbilityCategory.Mobility =>
                IntelligenceOwnership.Companion,
            AbilityCategory.Main or AbilityCategory.Offensive => IntelligenceOwnership.MaxDps,
            AbilityCategory.Utility => status == IntelligenceStatus.MaxDpsBacked
                ? IntelligenceOwnership.Manual
                : IntelligenceOwnership.Companion,
            _ => status == IntelligenceStatus.MaxDpsBacked
                ? IntelligenceOwnership.MaxDps
                : IntelligenceOwnership.Companion,
        };
    }

    /// <summary>
    /// How complete the when-to-use knowledge is (v2.7 §4). Delegated follows
    /// ownership; manual is ManualByDesign; verified/researched rules are
    /// Complete; conservative derived rules are honestly Partial; unsafe or
    /// structurally impossible automation is Unobservable; modeled rows are
    /// ResearchPending. A curated value wins.
    /// </summary>
    private static IntelligenceCompleteness DeriveCompleteness(
        AbilityDefinition def, AbilityOverride? o, IntelligenceStatus status, IntelligenceOwnership ownership)
    {
        if (o?.Completeness is { Length: > 0 } c
            && Enum.TryParse<IntelligenceCompleteness>(c, true, out var parsed))
            return parsed;
        if (ownership == IntelligenceOwnership.Manual) return IntelligenceCompleteness.ManualByDesign;
        if (ownership == IntelligenceOwnership.MaxDps) return IntelligenceCompleteness.Delegated;
        if (status is IntelligenceStatus.Incomplete or IntelligenceStatus.Unknown)
            return IntelligenceCompleteness.ResearchPending;
        if (status == IntelligenceStatus.UnsafeToAutomate) return IntelligenceCompleteness.Unobservable;
        if (status is IntelligenceStatus.Verified or IntelligenceStatus.ResearchBacked)
            return IntelligenceCompleteness.Complete;
        // MaxDpsBacked rows in companion/shared categories run on the
        // companion's conservative gates without a second source: Partial.
        // Heuristic / CompanionRule / ConservativeSafety are deliberate
        // conservative rules, never a full claim.
        return IntelligenceCompleteness.Partial;
    }

    /// <summary>
    /// Why the decision is delegated to MaxDps (v2.7 §3). Every MaxDps-owned
    /// entry gets at least the generic reason plus category/usage-specific
    /// ones (burst windows, talent interaction, enemy count, unobservable
    /// context, historical aliases). A curated list wins outright.
    /// </summary>
    private static DelegationReason DeriveDelegation(
        AbilityDefinition def, AbilityOverride? o, IntelligenceStatus status, IntelligenceOwnership ownership)
    {
        if (o?.Delegation is { Length: > 0 } names)
        {
            var flags = DelegationReason.None;
            foreach (var name in names)
                if (Enum.TryParse<DelegationReason>(name, true, out var flag)) flags |= flag;
            if (flags != DelegationReason.None) return flags;
        }
        if (ownership != IntelligenceOwnership.MaxDps) return DelegationReason.None;
        var why = DelegationReason.MaxDpsAlreadyModelsThis;
        if (def.Category == AbilityCategory.Main || def.Purpose == AbilityPurpose.Rotational)
            why |= DelegationReason.MainRotation;
        if (def.Category == AbilityCategory.Offensive)
            why |= DelegationReason.RotationOrdering;
        if ((o?.HoldForBurst ?? def.HoldForBurst)) why |= DelegationReason.ComplexBuffWindow;
        if (o?.Talent is { Length: > 0 } || o?.HeroTalent is { Length: > 0 }
            || def.TalentNote is { Length: > 0 } || def.HeroTalentNote is { Length: > 0 })
            why |= DelegationReason.TalentInteraction;
        if ((o?.EnemyCountMin ?? def.EnemyCountMin) is > 1) why |= DelegationReason.EnemyCountUnavailable;
        if (def.Purpose is AbilityPurpose.CrowdControl or AbilityPurpose.Dispel or AbilityPurpose.Purge
            or AbilityPurpose.Threat)
            why |= DelegationReason.InsufficientObservableContext;
        if (def.Purpose == AbilityPurpose.External) why |= DelegationReason.TargetSelectionUnavailable;
        if (def.Category is AbilityCategory.Consumable or AbilityCategory.Trinket)
            why |= DelegationReason.NotWorthDuplicating;
        if (status == IntelligenceStatus.Incomplete) why |= DelegationReason.NotWorthDuplicating;
        return why;
    }

    /// <summary>
    /// Mandatory reason for every Manual entry. Curated wins; otherwise the
    /// unobservable context that makes automation unsafe is named by purpose.
    /// </summary>
    private static ManualReason DeriveManualReason(
        AbilityDefinition def, AbilityOverride? o, IntelligenceOwnership ownership)
    {
        if (o?.ManualReason is { Length: > 0 } m
            && Enum.TryParse<ManualReason>(m, true, out var parsed))
            return parsed;
        if (ownership != IntelligenceOwnership.Manual) return ManualReason.None;
        return def.Purpose switch
        {
            AbilityPurpose.CrowdControl => ManualReason.CrowdControlTargetStateUnobservable,
            AbilityPurpose.Dispel => ManualReason.DebuffIdentityUnobservable,
            AbilityPurpose.Purge => ManualReason.BuffIdentityUnobservable,
            AbilityPurpose.Threat => ManualReason.ThreatStateUnobservable,
            AbilityPurpose.External => ManualReason.AllyStateUnobservable,
            AbilityPurpose.Movement or AbilityPurpose.Escape => ManualReason.SafetyRule,
            _ => def.Category == AbilityCategory.Utility ? ManualReason.Other : ManualReason.SafetyRule,
        };
    }

    /// <summary>What the entry's knowledge is based on (v2.7 §7).</summary>
    private static AbilitySourceType DeriveSourceType(
        AbilityDefinition def, List<VendorEntry> rows, AbilityOverride? o)
    {
        if (o?.SourceType is { Length: > 0 } st
            && Enum.TryParse<AbilitySourceType>(st, true, out var parsed))
            return parsed;
        if (def.Provenance == AbilityProvenance.ClassSpell) return AbilitySourceType.DerivedModel;
        if (rows.Count > 0) return AbilitySourceType.VendorPin;
        if (o?.Source is { Length: > 0 } src)
            return src.StartsWith("vendor-only", StringComparison.OrdinalIgnoreCase)
                ? AbilitySourceType.VendorPin
                : AbilitySourceType.Research;
        return AbilitySourceType.Curated;
    }

    private static SourceConfidence ConfidenceFor(AbilitySourceType type) => type switch
    {
        AbilitySourceType.VendorPin => SourceConfidence.High,
        AbilitySourceType.Research or AbilitySourceType.LiveClientDb2 => SourceConfidence.Medium,
        AbilitySourceType.Curated => SourceConfidence.Medium,
        _ => SourceConfidence.Low,
    };

    /// <summary>
    /// Registry honesty: the status is curated when stated, otherwise derived
    /// from provenance. There is deliberately no generic "fully understood"
    /// default — vendor rows are MaxDpsBacked (MaxDps carries the use
    /// intelligence), curated rows are CompanionRule or ResearchBacked, the
    /// modeled class-spell tail is Incomplete, and manual-by-design wins over
    /// everything except an explicit curated status.
    /// </summary>
    private static IntelligenceStatus DeriveStatus(AbilityDefinition def, AbilityOverride? o)
    {
        if (o?.Status is { Length: > 0 } s && Enum.TryParse<IntelligenceStatus>(s, true, out var parsed))
            return parsed;
        if (def.NeverAutomatic) return IntelligenceStatus.ManualByDesign;
        if (def.Provenance == AbilityProvenance.ClassSpell) return IntelligenceStatus.Incomplete;
        if (def.Provenance == AbilityProvenance.Curated)
        {
            if (o?.Source is { Length: > 0 } src)
                return src.StartsWith("vendor-only", StringComparison.OrdinalIgnoreCase)
                    ? IntelligenceStatus.MaxDpsBacked
                    : IntelligenceStatus.ResearchBacked;
            return IntelligenceStatus.CompanionRule;
        }
        return IntelligenceStatus.MaxDpsBacked;
    }

    /// <summary>
    /// Who may offer the ability automatically. Class-spell rows are
    /// MaxDps-only (they may be acted on only when MaxDps itself suggests
    /// them; the companion never generates them independently); manual
    /// abilities are never automatic; everything else is autonomous.
    /// </summary>
    private static AutomationContext DeriveAutomation(AbilityDefinition def, AbilityOverride? o)
    {
        if (o?.Automation is { Length: > 0 } a && Enum.TryParse<AutomationContext>(a, true, out var parsed))
            return parsed;
        if (def.NeverAutomatic) return AutomationContext.Manual;
        if (def.Provenance == AbilityProvenance.ClassSpell) return AutomationContext.MaxDpsOnly;
        return AutomationContext.Autonomous;
    }

    /// <summary>
    /// Which MaxDps surface carries this ability's own use intelligence, from
    /// the vendor extraction's buckets (interrupt > defensive > offensive).
    /// The vendor pin only covers those three buckets, so consumables/trinkets
    /// and ledger extras report NotSurfaced — honestly, not as a claim.
    /// </summary>
    private static MaxDpsRelationship DeriveMaxDps(List<VendorEntry> rows)
    {
        var best = MaxDpsRelationship.NotSurfaced;
        var bestRank = 0;
        foreach (var row in rows)
        {
            var (relationship, rank) = row.Category.Trim().ToLowerInvariant() switch
            {
                "interrupt" => (MaxDpsRelationship.InterruptBucket, 3),
                "defensive" => (MaxDpsRelationship.DefensiveBucket, 2),
                "offensive" => (MaxDpsRelationship.OffensiveBucket, 1),
                _ => (MaxDpsRelationship.NotSurfaced, 0),
            };
            if (rank > bestRank)
            {
                bestRank = rank;
                best = relationship;
            }
        }
        return best;
    }

    /// <summary>Opportunity cost derived from tier and cooldown band (curated overrides).</summary>
    private static OpportunityCost OpportunityFor(DefensiveTier tier, int cooldownMs) =>
        tier == DefensiveTier.Immunity ? OpportunityCost.Critical
        : tier == DefensiveTier.Major || cooldownMs >= 180_000 ? OpportunityCost.High
        : tier == DefensiveTier.Minor || cooldownMs >= 45_000 ? OpportunityCost.Medium
        : OpportunityCost.Low;

    /// <summary>
    /// Offensive usage derived from purpose and cooldown. A vendor offensive
    /// with no curated cooldown stays Unknown — the audit tolerates that only
    /// while the status delegates the decision to MaxDps (MaxDpsBacked).
    /// </summary>
    private static OffensiveUsage DeriveOffensiveUsage(AbilityDefinition def)
    {
        if (def.Purpose == AbilityPurpose.MinorOffensive) return OffensiveUsage.MinorBurst;
        if (def.Purpose != AbilityPurpose.MajorOffensive && def.Category != AbilityCategory.Offensive)
            return OffensiveUsage.Unknown;
        if (def.CooldownMs >= 120_000) return OffensiveUsage.MajorBurst;
        if (def.CooldownMs >= 45_000) return OffensiveUsage.MinorBurst;
        return def.CooldownMs > 0 ? OffensiveUsage.ShortCooldown : OffensiveUsage.Unknown;
    }

    private static MobilityKind DeriveMobilityKind(AbilityPurpose purpose) => purpose switch
    {
        AbilityPurpose.GapCloser => MobilityKind.GapCloser,
        AbilityPurpose.Escape => MobilityKind.Escape,
        _ => MobilityKind.Unknown,
    };

    /// <summary>Contextual requirements: derived from the dedicated flags, unioned with curated flags.</summary>
    private static AbilityRequirement DeriveRequirements(AbilityDefinition def, AbilityOverride? o)
    {
        var requires = AbilityRequirement.None;
        if (def.RequiresTarget) requires |= AbilityRequirement.Target;
        if (def.TargetsEnemy) requires |= AbilityRequirement.Enemy;
        if (def.Purpose == AbilityPurpose.External) requires |= AbilityRequirement.FriendlyTarget;
        if (def.Purpose == AbilityPurpose.GapCloser) requires |= AbilityRequirement.Combat;
        if (def.RequiresEnemyCast) requires |= AbilityRequirement.EnemyCast;
        if (o?.Requires is { Length: > 0 } extras)
        {
            foreach (var name in extras)
                if (Enum.TryParse<AbilityRequirement>(name, true, out var flag))
                    requires |= flag;
        }
        return requires;
    }

    private static AbilityRelation[] ParseRelations(RelationOverride[]? entries)
    {
        if (entries is not { Length: > 0 }) return [];
        var list = new List<AbilityRelation>(entries.Length);
        foreach (var entry in entries)
        {
            if (entry.Id <= 0) continue;
            if (!Enum.TryParse<RelationshipKind>(entry.Kind, true, out var kind)) continue;
            list.Add(new AbilityRelation(kind, entry.Id, entry.Note));
        }
        return list.ToArray();
    }

    private static AbilityDefinition Defaults(
        int spellId, string name, AbilityCategory category, string[] classes, string[] specs) => category switch
    {
        AbilityCategory.Main => new AbilityDefinition(
            spellId, name, category, AbilityPurpose.Rotational, DefensiveTier.None,
            GcdKind.OnGcd, RangeKind.Unknown, CastKind.Unknown,
            0, 0, 0, false, RangeRequirement.Any, true, null, null, false, false, null, 50,
            UnknownPolicy.Use, classes, specs, null, null),
        AbilityCategory.Offensive => new AbilityDefinition(
            spellId, name, category, AbilityPurpose.MajorOffensive, DefensiveTier.None,
            GcdKind.Unknown, RangeKind.Unknown, CastKind.Unknown,
            0, 0, 0, false, RangeRequirement.Any, true, null, null, false, false, null, 50,
            UnknownPolicy.Use, classes, specs, null, null),
        AbilityCategory.Defensive => new AbilityDefinition(
            spellId, name, category, AbilityPurpose.DefensiveMajor, DefensiveTier.Major,
            GcdKind.OffGcd, RangeKind.Unknown, CastKind.Instant,
            0, 0, 0, false, RangeRequirement.Any, false, null, null, true, false, "Defensive.Major", 50,
            UnknownPolicy.Use, classes, specs, null, null)
        { MinimumUrgency = DefensiveUrgency.Red },
        AbilityCategory.Interrupt => new AbilityDefinition(
            spellId, name, category, AbilityPurpose.Interrupt, DefensiveTier.None,
            GcdKind.OffGcd, RangeKind.Melee, CastKind.Instant,
            0, 0, 0, false, RangeRequirement.OutOfMelee, true, null, null, false, false, null, 90,
            UnknownPolicy.Use, classes, specs, null, null)
        { GcdVerified = true },
        AbilityCategory.Consumable => new AbilityDefinition(
            spellId, name, category, AbilityPurpose.Consumable, DefensiveTier.None,
            GcdKind.OffGcd, RangeKind.SelfOnly, CastKind.Instant,
            0, 0, 0, false, RangeRequirement.Any, false, null, null, false, false, null, 40,
            UnknownPolicy.Use, classes, specs, null, null)
        { GcdVerified = true },
        AbilityCategory.Trinket => new AbilityDefinition(
            spellId, name, category, AbilityPurpose.Trinket, DefensiveTier.None,
            GcdKind.OffGcd, RangeKind.SelfOnly, CastKind.Instant,
            0, 0, 0, false, RangeRequirement.Any, false, null, null, false, false, null, 30,
            UnknownPolicy.Use, classes, specs, null, null)
        { GcdVerified = true },
        AbilityCategory.Mobility => new AbilityDefinition(
            spellId, name, category, AbilityPurpose.Movement, DefensiveTier.None,
            GcdKind.Unknown, RangeKind.Unknown, CastKind.Instant,
            0, 0, 0, false, RangeRequirement.Any, true, null, null, false, true, "Mobility", 30,
            UnknownPolicy.Hold, classes, specs, null, null),
        AbilityCategory.SelfHeal => new AbilityDefinition(
            spellId, name, category, AbilityPurpose.SelfHeal, DefensiveTier.None,
            GcdKind.OnGcd, RangeKind.SelfOnly, CastKind.Unknown,
            0, 0, 0, false, RangeRequirement.Any, false, null, null, false, false, "SelfHeal", 30,
            UnknownPolicy.Hold, classes, specs, null, null),
        // Manual utility (CC/purge/threat/dispel): never automatic by default;
        // the curated purpose carries the policy behaviour (Hold), and the
        // unknown state conservatively holds.
        AbilityCategory.Utility => new AbilityDefinition(
            spellId, name, category, AbilityPurpose.CrowdControl, DefensiveTier.None,
            GcdKind.Unknown, RangeKind.Unknown, CastKind.Unknown,
            0, 0, 0, false, RangeRequirement.Any, true, null, null, false, true, "Utility", 20,
            UnknownPolicy.Hold, classes, specs, null, null),
        _ => throw new InvalidOperationException($"unsupported category {category}"),
    };

    private static bool IsSurvivalPurpose(AbilityPurpose purpose) => purpose is
        AbilityPurpose.DefensiveMajor or AbilityPurpose.DefensiveMinor or AbilityPurpose.Immunity
        or AbilityPurpose.Absorb or AbilityPurpose.Reflect or AbilityPurpose.SelfHeal
        or AbilityPurpose.External;

    /// <summary>
    /// Tier default for <see cref="AbilityDefinition.MinimumUrgency"/>:
    /// big mitigation needs the vendor red stage, everything else the yellow
    /// stage. Curated per-ability values override this.
    /// </summary>
    private static DefensiveUrgency MinimumUrgencyFor(AbilityPurpose purpose, DefensiveTier tier)
    {
        if (!IsSurvivalPurpose(purpose)) return DefensiveUrgency.Yellow;
        return tier is DefensiveTier.Major or DefensiveTier.Immunity
            ? DefensiveUrgency.Red
            : DefensiveUrgency.Yellow;
    }

    private static AbilityCategory ParseCategory(string value) => value.Trim().ToLowerInvariant() switch
    {
        "main" => AbilityCategory.Main,
        "offensive" => AbilityCategory.Offensive,
        "defensive" => AbilityCategory.Defensive,
        "interrupt" => AbilityCategory.Interrupt,
        "consumable" => AbilityCategory.Consumable,
        "trinket" => AbilityCategory.Trinket,
        "mobility" => AbilityCategory.Mobility,
        "selfheal" => AbilityCategory.SelfHeal,
        "utility" => AbilityCategory.Utility,
        _ => throw new InvalidDataException($"unknown category '{value}'"),
    };

    private static T ParseEnum<T>(string value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) ? parsed : fallback;

    // ---- JSON DTOs --------------------------------------------------------

    private sealed class VendorFile
    {
        public string? GeneratedFrom { get; set; }
        public string? Note { get; set; }
        public List<VendorEntry>? Entries { get; set; }
    }

    private sealed class VendorEntry
    {
        public string Class { get; set; } = "";
        public string Spec { get; set; } = "";
        public string Category { get; set; } = "";
        public string Name { get; set; } = "";
        public int Id { get; set; }
    }

    internal sealed class CuratedFile
    {
        public int Version { get; set; }
        public string? Verified { get; set; }
        public string? GeneratedFrom { get; set; }
        public string? GamePatch { get; set; }

        /// <summary>JSON key is the short "interface" (matches the game's own terms).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("interface")]
        public int InterfaceVersion { get; set; }

        public string? MaxDpsVersion { get; set; }
        public string? CompanionVersion { get; set; }
        public List<AbilityOverride>? Abilities { get; set; }
        public Dictionary<string, Dictionary<string, SpecExtras>>? Extras { get; set; }

        /// <summary>
        /// Curated spell aliases: id -> variant ids (e.g. "202168": [34428]).
        /// Stored symmetric by the loader and emitted as <c>MDB.SpellAliases</c>.
        /// </summary>
        public Dictionary<int, List<int>>? Aliases { get; set; }
    }

    internal sealed class RelationOverride
    {
        public string Kind { get; set; } = "";
        public int Id { get; set; }
        public string? Note { get; set; }
    }

    internal sealed class SpecExtras
    {
        public List<int>? Mobility { get; set; }
        public List<int>? SelfHeal { get; set; }

        /// <summary>Curated major offensive gap-fill candidates (shared burst first, spec-specific second).</summary>
        public List<int>? Offensive { get; set; }
    }

    internal sealed class AbilityOverride
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Category { get; set; }
        public string? Purpose { get; set; }
        public string? Tier { get; set; }
        public string? Gcd { get; set; }
        public string? Range { get; set; }
        public string? Cast { get; set; }
        public int? CdMs { get; set; }
        public int? DurMs { get; set; }
        public int? HealPct { get; set; }
        public bool? RequiresEnemyCast { get; set; }
        public string? TargetRange { get; set; }
        public bool? RequiresTarget { get; set; }
        public int? UseBelowHpPct { get; set; }
        public int? HoldAboveHpPct { get; set; }
        public bool? HoldWhenBuffActive { get; set; }
        public bool? NeverAutomatic { get; set; }
        public string? MinimumUrgency { get; set; }
        public string? UrgencySource { get; set; }
        public string? Group { get; set; }
        public int? Priority { get; set; }
        public string? UnknownPolicy { get; set; }
        public string? Note { get; set; }
        public string? Source { get; set; }

        /// <summary>Optional curated "resets on kill"-style hint; informational only.</summary>
        public string? ResetHint { get; set; }

        // v2.6 ability intelligence registry fields (all optional).
        public string? Kind { get; set; }
        public string? Status { get; set; }
        public string? Automation { get; set; }
        public string? OpportunityCost { get; set; }
        public string? InterruptKind { get; set; }
        public string? OffensiveUsage { get; set; }
        public string? MobilityKind { get; set; }
        public string[]? Requires { get; set; }
        public string[]? Classes { get; set; }
        public string[]? Specs { get; set; }
        public string? Talent { get; set; }
        public string? HeroTalent { get; set; }
        public string? PatchVerified { get; set; }
        public RelationOverride[]? Relations { get; set; }
        public string[]? Capabilities { get; set; }
        public int? EnemyCountMin { get; set; }
        public bool? HoldForBurst { get; set; }

        // v3.2.0 TTK fields (optional; absent = tier default / execute off).
        public double? MinTtkSec { get; set; }
        public int? ExecuteBelowPct { get; set; }
        public bool? ExecuteFavored { get; set; }

        // v2.7 coverage registry fields (all optional; derivations are documented
        // in AbilityCatalog.DeriveOwnership / DeriveCompleteness / ...).
        public string? Ownership { get; set; }
        public string? Completeness { get; set; }
        public string[]? Delegation { get; set; }
        public string? DelegationNote { get; set; }
        public string? ManualReason { get; set; }
        public string? IntroducedPatch { get; set; }
        public string? SourcePatch { get; set; }
        public string? LastValidatedPatch { get; set; }
        public string? SourceType { get; set; }
        public string? SourceUrl { get; set; }
        public string? SourceConfidence { get; set; }
        public bool? LiveVerified { get; set; }
    }
}
