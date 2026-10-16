using System.Text.Json;
using System.Text.RegularExpressions;

namespace MaxDpsCompanion;

/// <summary>
/// The raw, best-effort ability book for every class/spec, generated from the
/// pinned MaxDps vendor snapshot (<c>vendor/MaxDps/SpellData.lua</c>, retail
/// <c>ns.classSpellData</c> block) by <c>tools/Extract-ClassSpells.ps1</c>.
///
/// The vendor table is a flat <c>token -&gt; spell id</c> map per spec with NO
/// names, NO categories and NO active/passive distinction. This class turns the
/// tokens into display names (best-effort CamelCase decoding + a small alias
/// map for the tokens that decode badly) and flags passive/junk tokens, while
/// <see cref="AbilityCatalog"/> merges the survivors as the lowest-priority
/// knowledge layer (curated &gt; vendor Cooldowns &gt; class spells).
///
/// Honest limits (documented in docs/KNOWLEDGE.md): the classification is
/// modeled, not authoritative — the vendor table does not distinguish a
/// rotational attack from a utility button, so the category heuristics are
/// deliberately conservative and everything uncertain lands in "main rotation".
/// </summary>
internal sealed class ClassSpellBook
{
    public const string ResourceName = "MaxDpsCompanion.Knowledge.class-spells.json";
    public const string VerificationResourceName = "MaxDpsCompanion.Knowledge.spell-verification.json";

    /// <summary>
    /// Official live-client facts for one spell id (wago.tools DB2 export):
    /// the client's own name and icon slug, plus whether the id still exists
    /// in the current build. Unverified ids are omitted from the merged
    /// ability knowledge (they cannot be actual 12.1 skills).
    /// </summary>
    internal sealed record Verification(int SpellId, string Name, string IconSlug, bool Verified);

    /// <summary>One vendor row: the raw token plus its display name and verification.</summary>
    internal sealed record Entry(
        string Class,
        string Spec,
        string Token,
        string Name,
        int SpellId,
        bool IsJunk,
        string? IconSlug,
        bool Verified);

    private readonly List<Entry> _entries;
    private readonly Dictionary<(string Class, string Spec), List<Entry>> _bySpec;
    private readonly Dictionary<int, Entry> _byId;
    private readonly Dictionary<string, List<string>> _specsByClass;
    private readonly Dictionary<int, Verification> _verification;

    private ClassSpellBook(
        List<Entry> entries,
        Dictionary<(string, string), List<Entry>> bySpec,
        Dictionary<int, Entry> byId,
        Dictionary<string, List<string>> specsByClass,
        Dictionary<int, Verification> verification)
    {
        _entries = entries;
        _bySpec = bySpec;
        _byId = byId;
        _specsByClass = specsByClass;
        _verification = verification;
    }

    /// <summary>Every row (including junk) in stable vendor order.</summary>
    public IReadOnlyList<Entry> All => _entries;

    /// <summary>All rows for one class+spec (including junk), in vendor order.</summary>
    public IReadOnlyList<Entry> ForSpec(string? className, string? specName)
    {
        if (className is null || specName is null) return [];
        return _bySpec.TryGetValue((className, specName), out var list) ? list : [];
    }

    /// <summary>The class+spec pairs present in the vendor table, in vendor order.</summary>
    public IReadOnlyList<string> Specs(string? className)
    {
        if (className is null) return [];
        return _specsByClass.TryGetValue(className, out var specs) ? specs : [];
    }

    /// <summary>First entry for a spell id (vendor order), or null.</summary>
    public Entry? TryGet(int spellId) => _byId.TryGetValue(spellId, out var entry) ? entry : null;

    /// <summary>Official icon slug for a spell id when the live client data confirms one.</summary>
    public string? TryGetIconSlug(int spellId) => TryGet(spellId)?.IconSlug;

    public int Count => _entries.Count;

    /// <summary>Official live-client facts for a spell id, or null when unknown.</summary>
    public Verification? TryGetVerification(int spellId) =>
        _verification.TryGetValue(spellId, out var verification) ? verification : null;

    /// <summary>False only when the live client data proves the id is gone.</summary>
    public bool IsVerified(int spellId) => _verification.Count == 0 || _verification.TryGetValue(spellId, out var v) && v.Verified;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static ClassSpellBook? _default;

    /// <summary>The book built from the embedded vendor extract (lazy, immutable).</summary>
    public static ClassSpellBook Default => _default ??= Load(ReadEmbedded(ResourceName), ReadEmbedded(VerificationResourceName));

    public static ClassSpellBook Load(string json) => Load(json, verificationJson: null);

    public static ClassSpellBook Load(string json, string? verificationJson)
    {
        var file = JsonSerializer.Deserialize<ClassSpellFile>(json, JsonOptions)
            ?? throw new InvalidDataException("class-spells.json did not parse");

        var verification = new Dictionary<int, Verification>();
        if (verificationJson is not null)
        {
            var verified = JsonSerializer.Deserialize<VerificationFile>(verificationJson, JsonOptions)
                ?? throw new InvalidDataException("spell-verification.json did not parse");
            foreach (var row in verified.Entries ?? [])
            {
                if (row.Id > 0) verification[row.Id] = new Verification(row.Id, row.Name ?? "", row.Icon ?? "", row.Verified);
            }
        }

        var entries = new List<Entry>();
        var bySpec = new Dictionary<(string, string), List<Entry>>();
        var byId = new Dictionary<int, Entry>();
        var specsByClass = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<(string, string, string, int)>();

        foreach (var row in file.Entries ?? [])
        {
            if (row.Id <= 0) continue;
            var className = (row.Class ?? "").Trim().ToUpperInvariant();
            var specName = (row.Spec ?? "").Trim();
            var token = (row.Token ?? "").Trim();
            if (className.Length == 0 || specName.Length == 0 || token.Length == 0) continue;
            if (!seen.Add((className, specName, token, row.Id))) continue;

            verification.TryGetValue(row.Id, out var official);
            var verified = official is null || official.Verified;
            var name = official is { Name.Length: > 0 } ? official.Name : DecodeName(token);
            var entry = new Entry(className, specName, token, name, row.Id, IsJunk(token, row.Id),
                official?.IconSlug is { Length: > 0 } ? official.IconSlug : null, verified);
            entries.Add(entry);
            if (!bySpec.TryGetValue((className, specName), out var list))
            {
                list = [];
                bySpec[(className, specName)] = list;
                if (!specsByClass.TryGetValue(className, out var specs))
                {
                    specs = [];
                    specsByClass[className] = specs;
                }
                specs.Add(specName);
            }
            list.Add(entry);
            byId.TryAdd(entry.SpellId, entry);
        }

        return new ClassSpellBook(entries, bySpec, byId, specsByClass, verification);
    }

    private static string ReadEmbedded(string name)
    {
        using var stream = typeof(ClassSpellBook).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidDataException($"embedded resource missing: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ---- display names -----------------------------------------------------

    /// <summary>
    /// Tokens whose CamelCase decoding reads badly. Deliberately tiny: the
    /// decoded name is a best-effort label, and the real localised name comes
    /// from the game for anything the curated/vendor layers already carry.
    /// </summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["OdynsFury"] = "Odyn's Fury",
        ["ArgusRage"] = "Argus' Rage",
        ["RaiseAlly"] = "Raise Ally",
        ["BlessingofSpellwarding"] = "Blessing of Spellwarding",
        ["BlessingofSanctuary"] = "Blessing of Sanctuary",
        ["TakeembySurprise"] = "Take 'Em By Surprise",
        ["ChosensRevelry"] = "Chosen's Revelry",
        ["DeathsArrival"] = "Death's Arrival",
        ["ThiefsVersatility"] = "Thief's Versatility",
        ["TricksoftheTrade"] = "Tricks of the Trade",
    };

    /// <summary>Lowercase connectors that can hide inside a token (Tricksof, Coupde, Timeis).</summary>
    private static readonly string[] Connectors = ["the", "and", "of", "to", "de", "is", "a"];

    /// <summary>Real words that end in a connector and must never be split (Thousand, Command...).</summary>
    private static readonly HashSet<string> NoSplitWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Thousand", "Command", "Demand", "Brand", "Grand", "Hand", "Land", "Stand", "Strand",
        "Into", "Auto", "This", "Area", "Idea", "Data", "Beta", "Alpha", "Vista", "Aria",
        "Mana", "Anaconda", "Island", "Understand", "Outland", "Badlands", "Threat",
    };

    /// <summary>
    /// Best-effort CamelCase decoder: "Shadowstep" stays, "SinisterStrike"
    /// becomes "Sinister Strike", "TricksoftheTrade" becomes "Tricks of the
    /// Trade", "ThousandCuts" stays "Thousand Cuts" (the connector suffix
    /// split is guarded against real words).
    /// </summary>
    internal static string DecodeName(string token)
    {
        if (Aliases.TryGetValue(token, out var alias)) return alias;
        var spaced = Regex.Replace(token, "(?<=[a-z0-9])(?=[A-Z])", " ");
        spaced = Regex.Replace(spaced, "(?<=[A-Z])(?=[A-Z][a-z])", " ");

        var words = new List<string>();
        foreach (var raw in spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var word = raw;
            if (!NoSplitWords.Contains(word))
            {
                foreach (var connector in Connectors)
                {
                    if (word.Length >= connector.Length + 4
                        && word.EndsWith(connector, StringComparison.OrdinalIgnoreCase))
                    {
                        words.Add(word[..^connector.Length]);
                        words.Add(connector);
                        word = null;
                        break;
                    }
                }
            }
            if (word is not null) words.Add(word);
        }
        return string.Join(" ", words);
    }

    // ---- passive/junk filter ----------------------------------------------

    /// <summary>
    /// Exact tokens that are never castable buttons (riding, professions,
    /// racial passives, weapon skills, fun items, heirlooms) plus the
    /// talent/passive long tail that the vendor table mixes in with real
    /// abilities. Anything filtered here is still present in the generated
    /// JSON; it is simply not offered as an automation toggle because it can
    /// never arrive as a suggestion. Maintained with the
    /// <c>--dump-class-skills</c> tool; see docs/KNOWLEDGE.md.
    /// </summary>
    private static readonly HashSet<string> JunkTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        // Riding / professions / fun items / heirlooms / racials.
        "AutoAttack", "Parry", "DualWield", "ArmorSkills", "WeaponSkills", "Languages",
        "SafeFall", "PickLock", "PickPocket", "Poisons", "DetectTraps", "DisarmTrap",
        "ApprenticeRiding", "JourneymanRiding", "ExpertRiding", "ArtisanRiding", "MasterRiding",
        "FlightMastersLicense", "ColdWeatherFlying", "CloudSerpentRiding",
        "WorkingOvertime", "Reinforce", "FastTrack", "BountifulBags", "Mr.Popularity",
        "Bartering", "HonorableMention", "ForGreatJustice", "Command", "BattleFatigue",
        // Catalogued racial actives (BloodFury/Berserking/ArcaneTorrent/
        // GiftOfTheNaaru/Stoneform) are deliberately NOT junk: curated
        // scope=Racial rows carry them. Only racial PASSIVES / manual dispels
        // remain filtered here.
        "ArcaneResistance", "Hardiness", "AxeSpecialization", "MaceSpecialization",
        "LeatherSpecialization", "Vitality", "Ruthlessness", "TitheEvasion", "SpellWarding",
        "ArcaneAffinity", "WillOfTheForsaken",
        "EscapeArtist", "EveryManForHimself", "Perception",
        // Old-content / achievement / convenience tokens.
        "VindicaarMatrixCrystal", "TheQuickandtheDead", "GuildMail", "HastyHearth",
        "ReviveBattlePets", "MountUp", "MobileBanking", "PackHobgoblin", "TimeisMoney",
        "GreenskinsWickers", "BestDealsAnywhere", "BetterLivingThroughChemistry",
        "BattleforAzerothPathfinder", "BrokenIslesPathfinder",
        // Passive talents (the vendor table does not mark them; curated here).
        "Detection", "CheatDeath", "CuttoTheChase", "DeadenedNerves", "DeadlyPrecision",
        "DeeperStratagem", "Elusiveness", "Featherfoot", "FleetFooted", "ForcedInduction",
        "GracefulGuile", "IronStomach", "KeepItRolling", "Lethality", "NimbleFingers",
        "RestlessBlades", "ShadowTechniques", "SoothingDarkness", "Subterfuge",
        "SuperiorMixture", "TightSpender", "Vigor", "WithoutaTrace", "Audacity",
        "Ambidexterity", "CombatPotency", "CombatStamina", "CounttheOdds", "Crackshot",
        "DancingSteel", "DealFate", "DeftManeuvers", "DeliveredDoom", "DestinyDefined",
        "DeviousDistractions", "DeviousStratagem", "DirtyTricks", "DisorientingStrikes",
        "DontBeSuspicious", "DoubleJeopardy", "EdgeCase", "FantheHammer", "FatalFlourish",
        "FateIntertwined", "FatefulEnding", "FlawlessForm", "Flickerstrike", "HandofFate",
        "HeavyHitter", "HiddenOpportunity", "HitandRun", "InevitabileEnd", "InexorableMarch",
        "LoadedDice", "MeanStreak", "Mirrors", "NimbleFlurry", "NoScruples", "Opportunity",
        "PreciseCuts", "PrecisionShot", "QuickDraw", "Riposte", "SleightofHand", "Smoke",
        "SoTricky", "StingLikeaBee", "SummarilyDispatched", "SurprisingStrikes",
        "SwiftSlasher", "TemptedFate", "ThousandCuts", "TripleThreat",
        "UnderhandedUpperHand", "UnseenBlade", "FloatLikeaButterfly", "RetractableHook",
        "Shadowheart", "Shadowrunner", "Recuperator", "AcrobaticStrikes", "AirborneIrritant",
        "Alacrity", "RushedSetup", "SleightofHand",
    };

    /// <summary>
    /// Token shapes that are passives/talents in the vendor table: "Improved*",
    /// "*Specialization", "Mastery*", any "*Riding*", poison applications,
    /// Pathfinder/metagame text. Kept as patterns because there are hundreds
    /// and they all follow the same naming convention.
    /// </summary>
    private static readonly Regex[] JunkPatterns =
    [
        new(@"^Improved", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"Specialization$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^Mastery", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"Riding", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"Pathfinder", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"Chemistry", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"Poisons?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"Poisoner$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^Guild", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"Hearth", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^TitanicThrow$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    internal static bool IsJunk(string token, int spellId)
    {
        if (JunkTokens.Contains(token)) return true;
        foreach (var pattern in JunkPatterns)
        {
            if (pattern.IsMatch(token)) return true;
        }
        return false;
    }

    private sealed class ClassSpellFile
    {
        public string? GeneratedFrom { get; set; }
        public string? Note { get; set; }
        public List<ClassSpellRow>? Entries { get; set; }
    }

    private sealed class ClassSpellRow
    {
        public string? Class { get; set; }
        public string? Spec { get; set; }
        public string? Token { get; set; }
        public int Id { get; set; }
    }

    private sealed class VerificationFile
    {
        public string? GeneratedFrom { get; set; }
        public string? Note { get; set; }
        public List<VerificationRow>? Entries { get; set; }
    }

    private sealed class VerificationRow
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Icon { get; set; }
        public bool Verified { get; set; }
    }
}
