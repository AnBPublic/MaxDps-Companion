using System.Text.Json;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// T-B: schema-conformance tests for the curated TTK fields in
/// <c>Knowledge/abilities.json</c> (v3.2.0 TTK intelligence, §5 B3).
///
/// The fields (<c>minTtkSec</c>, <c>executeBelowPct</c>, <c>executeFavored</c>)
/// are consumed by T-A's <c>AbilityCatalog</c>/<c>CandidateProviders</c>
/// parsing. This suite deliberately reads the RAW embedded JSON so it stays
/// valid on the T-B branch before T-A's model fields exist (merge order
/// T-B -> T-A), and so it keeps validating the data even if the schema later
/// grows.
///
/// Rules pinned here:
///  * every MajorOffensive / Transformation / Summon entry either states an
///    explicit <c>minTtkSec</c> or its <see cref="OffensiveUsage"/> tier has a
///    documented default (§3.3) — absence is intentional, never an oversight;
///  * explicit values are in range 0..300;
///  * execute data is SPARSE (Deathmark only), and every <c>executeBelowPct</c>
///    carries <c>executeFavored: true</c> with 0 &lt; pct &lt;= 35;
///  * the documented deviations hold (Army 30s, short CDs 5s, summons 20s);
///  * curated ids with a name are name-matched against
///    <c>spell-verification.json</c>, and every curated major offensive is
///    live-verified there.
/// </summary>
public class TtkCurationTests
{
    /// <summary>
    /// §3.3 tier defaults (T-A; when the curated field is absent the ability's
    /// <c>OffensiveUsage</c> selects the default). <c>null</c> = the defensive
    /// path owns the ability, so a TTK gate does not apply.
    /// </summary>
    private static readonly Dictionary<string, int?> DocumentedTierDefaults = new()
    {
        ["MajorBurst"] = 12,
        ["Transformation"] = 20,
        ["Summon"] = 20,
        ["WindowDriven"] = 10,
        ["ShortCooldown"] = 5,
        ["ProcDriven"] = 5,
        ["AoeOnly"] = 5,
        ["SingleTargetOnly"] = 5,
        ["ResourceDriven"] = 5,
        ["DefensiveOffensiveHybrid"] = null, // defensive path, no offensive TTK gate
        ["MinorBurst"] = 10,                 // not enumerated in §3.3 -> unknown default
        ["Execute"] = 10,                    // not enumerated in §3.3 -> unknown default
        ["Manual"] = 10,                     // not enumerated in §3.3 -> unknown default
        ["Unknown"] = 10,
    };

    private static readonly HashSet<string> TransformationOrSummon = ["Transformation", "Summon"];

    // ---- inputs -----------------------------------------------------------

    private static JsonDocument LoadCurated() =>
        JsonDocument.Parse(ResourceStream(AbilityCatalog.CuratedResourceName));

    private static JsonDocument LoadVerification() =>
        JsonDocument.Parse(ResourceStream(ClassSpellBook.VerificationResourceName));

    private static Stream ResourceStream(string name) =>
        typeof(AbilityCatalog).Assembly.GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"embedded resource {name} missing");

    private static string? Str(JsonElement row, string prop) =>
        row.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static int? Int(JsonElement row, string prop) =>
        row.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;

    private static bool? Bool(JsonElement row, string prop) =>
        row.TryGetProperty(prop, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.GetBoolean() : null;

    private static List<JsonElement> CuratedAbilities()
    {
        using var doc = LoadCurated();
        var list = new List<JsonElement>();
        if (doc.RootElement.TryGetProperty("abilities", out var arr))
            foreach (var row in arr.EnumerateArray()) list.Add(row.Clone());
        return list;
    }

    private static Dictionary<int, string> VerificationNames()
    {
        using var doc = LoadVerification();
        var map = new Dictionary<int, string>();
        if (!doc.RootElement.TryGetProperty("entries", out var entries)) return map;
        foreach (var row in entries.EnumerateArray())
        {
            var id = Int(row, "id") ?? 0;
            var name = Str(row, "name");
            if (id > 0 && !string.IsNullOrWhiteSpace(name)) map[id] = name!;
        }
        return map;
    }

    private static bool IsMajor(JsonElement row) =>
        Str(row, "purpose") == "MajorOffensive"
        || TransformationOrSummon.Contains(Str(row, "offensiveUsage") ?? "");

    // ---- tests ------------------------------------------------------------

    [Fact]
    public void Major_Offensives_Are_Explicit_Or_Documented_Default()
    {
        var majors = CuratedAbilities().Where(IsMajor).ToList();
        Assert.True(majors.Count >= 50, $"expected the curated major-offensive set, found {majors.Count}");

        foreach (var row in majors)
        {
            var id = Int(row, "id")!.Value;
            var usage = Str(row, "offensiveUsage") ?? "Unknown";
            var minTtk = Int(row, "minTtkSec");
            if (minTtk is { } v)
                Assert.True(v >= 0 && v <= 300, $"{id}: minTtkSec {v} out of range 0..300");
            else
                Assert.True(DocumentedTierDefaults.TryGetValue(usage, out var def) && def is not null,
                    $"{id} ({Str(row, "name")}): usage '{usage}' has no documented default and no explicit minTtkSec");
        }
    }

    [Fact]
    public void Documented_Default_Table_Matches_Section_3_3()
    {
        Assert.Equal(12, DocumentedTierDefaults["MajorBurst"]);
        Assert.Equal(20, DocumentedTierDefaults["Transformation"]);
        Assert.Equal(20, DocumentedTierDefaults["Summon"]);
        Assert.Equal(10, DocumentedTierDefaults["WindowDriven"]);
        Assert.Equal(5, DocumentedTierDefaults["ShortCooldown"]);
        Assert.Equal(5, DocumentedTierDefaults["ProcDriven"]);
        Assert.Equal(5, DocumentedTierDefaults["AoeOnly"]);
        Assert.Equal(5, DocumentedTierDefaults["SingleTargetOnly"]);
        Assert.Equal(5, DocumentedTierDefaults["ResourceDriven"]);
        Assert.Null(DocumentedTierDefaults["DefensiveOffensiveHybrid"]);
        Assert.Equal(10, DocumentedTierDefaults["Unknown"]);
    }

    [Fact]
    public void Long_Setup_Summons_And_Short_Cooldowns_Override_The_Default()
    {
        var byId = CuratedAbilities().ToDictionary(r => Int(r, "id")!.Value);

        // Army of the Dead: 8 min CD, long rune setup -> raised to 30s.
        Assert.Equal(30, Int(byId[42650], "minTtkSec"));

        // The other summons keep the Summon tier default, stated explicitly.
        foreach (var id in new[] { 1122, 34433, 49206, 205180, 265187 })
            Assert.Equal(20, Int(byId[id], "minTtkSec"));

        // 45-75s short cooldowns read as waste-safe at the ShortCooldown default.
        foreach (var id in new[] { 207289, 375982 })
            Assert.Equal(5, Int(byId[id], "minTtkSec"));
    }

    [Fact]
    public void Execute_Synergy_Is_Sparse_And_Consistent()
    {
        var withPct = CuratedAbilities()
            .Where(r => Int(r, "executeBelowPct") is not null)
            .ToList();

        // Sparse by design: guides confirm an execute synergy for Deathmark only.
        Assert.Single(withPct);
        Assert.Equal(360194, Int(withPct[0], "id"));

        foreach (var row in withPct)
        {
            var id = Int(row, "id")!.Value;
            var pct = Int(row, "executeBelowPct")!.Value;
            Assert.True(Bool(row, "executeFavored") == true,
                $"{id}: executeBelowPct requires executeFavored:true (inert otherwise)");
            Assert.True(pct > 0 && pct <= 35, $"{id}: executeBelowPct {pct} must be 0 < pct <= 35");
        }

        Assert.Equal(35, Int(withPct[0], "executeBelowPct"));
        Assert.True(Bool(withPct[0], "executeFavored"));

        // executeFavored without a threshold would be meaningless.
        foreach (var row in CuratedAbilities().Where(r => Bool(r, "executeFavored") == true))
            Assert.NotNull(Int(row, "executeBelowPct"));
    }

    [Fact]
    public void Ttk_Fields_Only_Appear_On_Major_Offensives()
    {
        foreach (var row in CuratedAbilities())
        {
            var id = Int(row, "id")!.Value;
            var hasTtk = Int(row, "minTtkSec") is not null
                || Int(row, "executeBelowPct") is not null
                || Bool(row, "executeFavored") is not null;
            if (hasTtk)
                Assert.True(IsMajor(row), $"{id} ({Str(row, "name")}): TTK fields on a non-major entry");
        }
    }

    [Fact]
    public void Curated_Names_Match_The_Live_Verification()
    {
        var names = VerificationNames();
        foreach (var row in CuratedAbilities())
        {
            var id = Int(row, "id")!.Value;
            var curatedName = Str(row, "name");
            if (string.IsNullOrWhiteSpace(curatedName)) continue;
            if (names.TryGetValue(id, out var live))
                Assert.True(string.Equals(curatedName, live, StringComparison.Ordinal),
                    $"{id}: curated name '{curatedName}' != spell-verification '{live}'");
            else if (IsMajor(row))
                Assert.Fail($"{id} ({curatedName}): curated major offensive is absent from spell-verification.json");
        }
    }
}
