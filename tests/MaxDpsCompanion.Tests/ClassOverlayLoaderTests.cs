using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// The schema-1 per-class taxonomy overlay loader (S3a): allow-list validation
/// (unknown field / duplicate / bad enum drop the entry and log, never crash),
/// the verification gate (only verified ids may be Auto/Suggest) and the
/// never-automatic safety gate (the overlay may restrict, never loosen).
/// Also checks the four shipped <c>Knowledge/classes/*.json</c> files load
/// cleanly and obey the same safety rules.
/// </summary>
public class ClassOverlayLoaderTests
{
    // ---- valid entry -------------------------------------------------------

    [Fact]
    public void Load_Parses_Every_Allowed_Field()
    {
        const string json = """
        { "schema": 1, "class": "DEATHKNIGHT", "entries": [
          { "spellId": 48792, "role": "Major", "route": "Defensive", "emergencyEscape": false,
            "offGcd": true, "mode": "Auto", "minUrgency": 4, "notes": "major: Red" } ] }
        """;

        var overlay = ClassOverlayLoader.Load(json, ClassOverlayKnowledge.ForTests(verified: [48792]));

        Assert.Equal(ClassOverlay.SchemaVersion, overlay.Schema);
        Assert.Equal("DEATHKNIGHT", overlay.ClassName);
        Assert.Empty(overlay.Issues);
        var entry = Assert.Single(overlay.Entries);
        Assert.Equal(48792, entry.SpellId);
        Assert.Equal(ClassOverlayRole.Major, entry.Role);
        Assert.Equal(ClassOverlayRoute.Defensive, entry.Route);
        Assert.False(entry.EmergencyEscape);
        Assert.True(entry.OffGcd);
        Assert.Equal(ClassOverlayMode.Auto, entry.Mode);
        Assert.Equal(4, entry.MinUrgency);
        Assert.Equal("major: Red", entry.Notes);
    }

    [Fact]
    public void Load_Defaults_Optional_Fields()
    {
        const string json = """{ "entries": [ { "spellId": 1, "role": "Minor", "route": "Defensive", "mode": "Never" } ] }""";

        var overlay = ClassOverlayLoader.Load(json, ClassOverlayKnowledge.ForTests(verified: [1]));

        var entry = Assert.Single(overlay.Entries);
        Assert.False(entry.EmergencyEscape);
        Assert.False(entry.OffGcd);
        Assert.Equal(0, entry.MinUrgency);
        Assert.Null(entry.Notes);
    }

    // ---- allow-list validation --------------------------------------------

    [Fact]
    public void Unknown_Field_Drops_Entry_And_Logs()
    {
        const string json = """
        { "entries": [ { "spellId": 1, "role": "Minor", "route": "Defensive", "mode": "Never", "bogus": 1 } ] }
        """;

        var overlay = ClassOverlayLoader.Load(json, ClassOverlayKnowledge.Empty);

        Assert.Empty(overlay.Entries);
        Assert.Contains(overlay.Issues, i => i.Contains("unknown field", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Duplicate_Field_Drops_Entry_And_Logs()
    {
        const string json = """
        { "entries": [ { "spellId": 1, "spellId": 2, "role": "Minor", "route": "Defensive", "mode": "Never" } ] }
        """;

        var overlay = ClassOverlayLoader.Load(json, ClassOverlayKnowledge.Empty);

        Assert.Empty(overlay.Entries);
        Assert.Contains(overlay.Issues, i => i.Contains("duplicate field", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Duplicate_SpellId_Drops_The_Second_Entry_And_Logs()
    {
        const string json = """
        { "entries": [
          { "spellId": 7, "role": "Minor", "route": "Defensive", "mode": "Never" },
          { "spellId": 7, "role": "Major", "route": "Defensive", "mode": "Never" } ] }
        """;

        var overlay = ClassOverlayLoader.Load(json, ClassOverlayKnowledge.Empty);

        Assert.Single(overlay.Entries);
        Assert.Contains(overlay.Issues, i => i.Contains("duplicate spellId", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("""{ "spellId": 1, "role": "Boss", "route": "Defensive", "mode": "Never" }""")]
    [InlineData("""{ "spellId": 1, "role": "Minor", "route": "Main", "mode": "Never" }""")]
    [InlineData("""{ "spellId": 1, "role": "Minor", "route": "Defensive", "mode": "Always" }""")]
    [InlineData("""{ "spellId": 1, "role": "Minor", "route": "Defensive", "mode": "2" }""")]
    public void Bad_Enum_Drops_Entry_And_Logs(string entryJson)
    {
        var overlay = ClassOverlayLoader.Load($"{{ \"entries\": [ {entryJson} ] }}", ClassOverlayKnowledge.Empty);

        Assert.Empty(overlay.Entries);
        Assert.Contains(overlay.Issues, i => i.Contains("bad", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("""{ "spellId": 0, "role": "Minor", "route": "Defensive", "mode": "Never" }""")]
    [InlineData("""{ "role": "Minor", "route": "Defensive", "mode": "Never" }""")]
    [InlineData("""{ "spellId": "1", "role": "Minor", "route": "Defensive", "mode": "Never" }""")]
    public void Bad_SpellId_Drops_Entry(string entryJson)
    {
        var overlay = ClassOverlayLoader.Load($"{{ \"entries\": [ {entryJson} ] }}", ClassOverlayKnowledge.Empty);

        Assert.Empty(overlay.Entries);
        Assert.Contains(overlay.Issues, i => i.Contains("spellId", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("""{ "spellId": 1, "role": "Minor", "route": "Defensive", "mode": "Never", "minUrgency": 5 }""")]
    [InlineData("""{ "spellId": 1, "role": "Minor", "route": "Defensive", "mode": "Never", "minUrgency": -1 }""")]
    [InlineData("""{ "spellId": 1, "role": "Minor", "route": "Defensive", "mode": "Never", "minUrgency": "x" }""")]
    public void Out_Of_Range_MinUrgency_Drops_Entry(string entryJson)
    {
        var overlay = ClassOverlayLoader.Load($"{{ \"entries\": [ {entryJson} ] }}", ClassOverlayKnowledge.Empty);

        Assert.Empty(overlay.Entries);
        Assert.Contains(overlay.Issues, i => i.Contains("minUrgency", StringComparison.OrdinalIgnoreCase));
    }

    // ---- verification gate -------------------------------------------------

    [Fact]
    public void Unverified_Auto_Is_Downgraded_To_Never_With_Note()
    {
        const string json = """
        { "entries": [ { "spellId": 99, "role": "Minor", "route": "Defensive", "mode": "Auto", "minUrgency": 4 } ] }
        """;

        var overlay = ClassOverlayLoader.Load(json, ClassOverlayKnowledge.Empty);

        var entry = Assert.Single(overlay.Entries);
        Assert.Equal(ClassOverlayMode.Never, entry.Mode);
        Assert.Contains("needs verification", entry.Notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(overlay.Issues, i => i.Contains("unverified", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Unverified_Suggest_Is_Downgraded_To_Never_With_Note()
    {
        const string json = """
        { "entries": [ { "spellId": 99, "role": "Utility", "route": "Defensive", "mode": "Suggest" } ] }
        """;

        var overlay = ClassOverlayLoader.Load(json, ClassOverlayKnowledge.Empty);

        var entry = Assert.Single(overlay.Entries);
        Assert.Equal(ClassOverlayMode.Never, entry.Mode);
        Assert.Contains("needs verification", entry.Notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verified_Auto_Is_Kept()
    {
        const string json = """
        { "entries": [ { "spellId": 100, "role": "Major", "route": "Defensive", "mode": "Auto", "minUrgency": 4, "notes": "kept" } ] }
        """;

        var overlay = ClassOverlayLoader.Load(json, ClassOverlayKnowledge.ForTests(verified: [100]));

        var entry = Assert.Single(overlay.Entries);
        Assert.Equal(ClassOverlayMode.Auto, entry.Mode);
        Assert.Equal("kept", entry.Notes);
        Assert.Empty(overlay.Issues);
    }

    [Fact]
    public void Unverified_Never_Gains_The_Needs_Verification_Note()
    {
        const string json = """
        { "entries": [ { "spellId": 99, "role": "CC", "route": "Interrupt", "mode": "Never" } ] }
        """;

        var entry = Assert.Single(ClassOverlayLoader.Load(json, ClassOverlayKnowledge.Empty).Entries);
        Assert.Equal(ClassOverlayMode.Never, entry.Mode);
        Assert.Contains("needs verification", entry.Notes, StringComparison.OrdinalIgnoreCase);
    }

    // ---- never-automatic gate ---------------------------------------------

    [Fact]
    public void NeverAutomatic_Auto_Is_Forced_To_Never()
    {
        const string json = """
        { "entries": [ { "spellId": 200, "role": "CC", "route": "Interrupt", "mode": "Auto", "notes": "x" } ] }
        """;

        var knowledge = ClassOverlayKnowledge.ForTests(verified: [200], neverAutomatic: [200]);
        var overlay = ClassOverlayLoader.Load(json, knowledge);

        var entry = Assert.Single(overlay.Entries);
        Assert.Equal(ClassOverlayMode.Never, entry.Mode);
        Assert.Contains(overlay.Issues, i => i.Contains("never-automatic", StringComparison.OrdinalIgnoreCase));
    }

    // ---- never crashes -----------------------------------------------------

    [Fact]
    public void Malformed_Json_Returns_Empty_Overlay_And_Logs()
    {
        var overlay = ClassOverlayLoader.Load("{ this is not json", ClassOverlayKnowledge.Empty);

        Assert.Empty(overlay.Entries);
        Assert.Contains(overlay.Issues, i => i.Contains("malformed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Wrong_Schema_Rejects_The_File()
    {
        const string json = """{ "schema": 2, "entries": [] }""";

        var overlay = ClassOverlayLoader.Load(json, ClassOverlayKnowledge.Empty);

        Assert.Empty(overlay.Entries);
        Assert.Contains(overlay.Issues, i => i.Contains("unsupported overlay schema", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_Entries_Array_Returns_Empty_Overlay()
    {
        var overlay = ClassOverlayLoader.Load("""{ "schema": 1 }""", ClassOverlayKnowledge.Empty);

        Assert.Empty(overlay.Entries);
        Assert.Contains(overlay.Issues, i => i.Contains("entries", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Non_Object_Root_Returns_Empty_Overlay()
    {
        var overlay = ClassOverlayLoader.Load("[ 1, 2 ]", ClassOverlayKnowledge.Empty);

        Assert.Empty(overlay.Entries);
        Assert.NotEmpty(overlay.Issues);
    }

    [Fact]
    public void LoadFile_Missing_Path_Does_Not_Throw()
    {
        var overlay = ClassOverlayLoader.LoadFile(Path.Combine(Path.GetTempPath(), "mdb-no-such-overlay.json"), ClassOverlayKnowledge.Empty);

        Assert.Empty(overlay.Entries);
        Assert.Contains(overlay.Issues, i => i.Contains("could not read", StringComparison.OrdinalIgnoreCase));
    }

    // ---- shipped data ------------------------------------------------------

    [Theory]
    [InlineData("DEATHKNIGHT", 20)]
    [InlineData("DEMONHUNTER", 17)]
    [InlineData("DRUID", 22)]
    [InlineData("EVOKER", 16)]
    public void Shipped_Class_Overlay_Loads_Clean_And_Safe(string className, int minimumEntries)
    {
        var path = OverlayPath(className);
        Assert.True(File.Exists(path), $"missing overlay: {path}");

        var overlay = ClassOverlayLoader.Load(File.ReadAllText(path));

        Assert.Equal(ClassOverlay.SchemaVersion, overlay.Schema);
        Assert.Equal(className, overlay.ClassName);
        Assert.True(overlay.Count >= minimumEntries, $"{className}: {overlay.Count} < {minimumEntries}");
        Assert.True(overlay.Issues.Count == 0, $"{className}: {string.Join("; ", overlay.Issues)}");

        foreach (var entry in overlay.Entries)
        {
            Assert.True(entry.SpellId > 0);
            Assert.InRange(entry.MinUrgency, 0, 4);
            Assert.True(Enum.IsDefined(entry.Route));
            Assert.True(Enum.IsDefined(entry.Role));
            Assert.True(Enum.IsDefined(entry.Mode));
            if (entry.Mode is ClassOverlayMode.Auto or ClassOverlayMode.Suggest)
                Assert.DoesNotContain("needs verification", entry.Notes ?? "", StringComparison.OrdinalIgnoreCase);
            if (entry.Notes is not null && entry.Notes.Contains("needs verification", StringComparison.OrdinalIgnoreCase))
                Assert.Equal(ClassOverlayMode.Never, entry.Mode);
        }
    }

    [Theory]
    [InlineData("DEMONHUNTER", 217832)]  // Imprison
    [InlineData("DRUID", 33786)]         // Cyclone
    [InlineData("EVOKER", 370665)]       // Rescue
    [InlineData("DEATHKNIGHT", 61999)]   // Raise Ally
    [InlineData("DRUID", 20484)]         // Rebirth
    public void Shipped_Never_Automatic_List_Stays_Never(string className, int spellId)
    {
        var entry = ClassOverlayLoader.Load(File.ReadAllText(OverlayPath(className))).For(spellId);

        Assert.NotNull(entry);
        Assert.Equal(ClassOverlayMode.Never, entry!.Mode);
    }

    [Theory]
    [InlineData("DEATHKNIGHT", 49576)]
    [InlineData("DEMONHUNTER", 189110)]
    [InlineData("DRUID", 102401)]
    public void Shipped_Gap_Closers_Route_To_Mobility(string className, int spellId)
    {
        var entry = ClassOverlayLoader.Load(File.ReadAllText(OverlayPath(className))).For(spellId);

        Assert.NotNull(entry);
        Assert.Equal(ClassOverlayRoute.Mobility, entry!.Route);
        Assert.Equal(ClassOverlayRole.Mobility, entry.Role);
        Assert.Equal(ClassOverlayMode.Auto, entry.Mode);
        Assert.Contains("out of melee", entry.Notes!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Shipped_Defensive_Tiers_Use_Yellow_Major_Red()
    {
        var dk = ClassOverlayLoader.Load(File.ReadAllText(OverlayPath("DEATHKNIGHT")));

        var minor = dk.For(205727); // Anti-Magic Barrier
        Assert.NotNull(minor);
        Assert.Equal(ClassOverlayRole.Minor, minor!.Role);
        Assert.Equal(2, minor.MinUrgency);

        var major = dk.For(48792); // Icebound Fortitude
        Assert.NotNull(major);
        Assert.Equal(ClassOverlayRole.Major, major!.Role);
        Assert.Equal(4, major.MinUrgency);

        var immunity = ClassOverlayLoader.Load(File.ReadAllText(OverlayPath("DEMONHUNTER"))).For(196555); // Netherwalk
        Assert.NotNull(immunity);
        Assert.Equal(ClassOverlayMode.Suggest, immunity!.Mode);
        Assert.Contains("last resort", immunity.Notes!, StringComparison.OrdinalIgnoreCase);
    }

    private static string OverlayPath(string className) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "app", "MaxDpsCompanion", "Knowledge", "classes",
            className + ".json"));
}
