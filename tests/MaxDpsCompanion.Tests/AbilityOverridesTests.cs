using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// The per-machine T4 override store: allow-list validation (unknown field /
/// duplicate / bad enum / bad urgency drop the entry and log, never crash), the
/// "mode + minUrgency only" shape (route/offGcd/role are structurally absent),
/// the registry &lt; overlay &lt; overrides precedence, the Never-is-absolute
/// rule, JSON round-trip, and the <see cref="AppSettings"/> integration that
/// loads the optional file next to the exe.
/// </summary>
public class AbilityOverridesTests
{
    // ---- load / parse ------------------------------------------------------

    [Fact]
    public void Load_Parses_Mode_And_Urgency_Keyed_By_SpellId()
    {
        const string json = """
        { "schema": 1, "overrides": [
          { "spellId": 48792, "mode": "Suggest", "minUrgency": 3 },
          { "spellId": 205727, "mode": "Auto" } ] }
        """;

        var store = AbilityOverrides.Load(json);

        Assert.Equal(AbilityOverrides.SchemaVersion, store.Schema);
        Assert.Empty(store.Issues);
        Assert.True(store.HasOverrides);
        Assert.Equal(2, store.BySpellId.Count);

        var major = store.For(48792);
        Assert.NotNull(major);
        Assert.Equal(ClassOverlayMode.Suggest, major!.Mode);
        Assert.Equal(3, major.MinUrgency);

        var minor = store.For(205727);
        Assert.NotNull(minor);
        Assert.Equal(ClassOverlayMode.Auto, minor!.Mode);
        Assert.Null(minor.MinUrgency);
    }

    [Fact]
    public void Load_Allows_An_Urgency_Only_Override()
    {
        var store = AbilityOverrides.Load("""{ "overrides": [ { "spellId": 100, "minUrgency": 4 } ] }""");

        var entry = Assert.Single(store.BySpellId.Values);
        Assert.Null(entry.Mode);
        Assert.Equal(4, entry.MinUrgency);
    }

    [Theory]
    [InlineData("""{ "overrides": [ { "spellId": 1, "mode": "Auto", "route": "Main" } ] }""")]
    [InlineData("""{ "overrides": [ { "spellId": 1, "mode": "Auto", "offGcd": true } ] }""")]
    [InlineData("""{ "overrides": [ { "spellId": 1, "mode": "Auto", "role": "Major" } ] }""")]

    public void Route_OffGcd_And_Role_Cannot_Be_Set(string json)
    {
        var store = AbilityOverrides.Load(json);

        Assert.Empty(store.BySpellId);
        Assert.Contains(store.Issues, i => i.Contains("unknown field", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Duplicate_Field_Drops_Entry_And_Logs()
    {
        var store = AbilityOverrides.Load("""{ "overrides": [ { "spellId": 1, "spellId": 2, "mode": "Auto" } ] }""");

        Assert.Empty(store.BySpellId);
        Assert.Contains(store.Issues, i => i.Contains("duplicate field", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Duplicate_SpellId_Drops_The_Second_Entry_And_Logs()
    {
        const string json = """
        { "overrides": [
          { "spellId": 7, "mode": "Never" },
          { "spellId": 7, "mode": "Auto" } ] }
        """;

        var store = AbilityOverrides.Load(json);

        Assert.Single(store.BySpellId);
        Assert.Equal(ClassOverlayMode.Never, store.For(7)!.Mode);
        Assert.Contains(store.Issues, i => i.Contains("duplicate spellId", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("""{ "overrides": [ { "spellId": 0, "mode": "Auto" } ] }""")]
    [InlineData("""{ "overrides": [ { "spellId": "1", "mode": "Auto" } ] }""")]
    [InlineData("""{ "overrides": [ { "mode": "Auto" } ] }""")]
    public void Bad_SpellId_Drops_Entry(string json)
    {
        var store = AbilityOverrides.Load(json);

        Assert.Empty(store.BySpellId);
        Assert.Contains(store.Issues, i => i.Contains("spellId", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("""{ "overrides": [ { "spellId": 1, "mode": "Always" } ] }""")]
    [InlineData("""{ "overrides": [ { "spellId": 1, "mode": "2" } ] }""")]
    [InlineData("""{ "overrides": [ { "spellId": 1, "mode": 3 } ] }""")]
    public void Bad_Mode_Drops_Entry(string json)
    {
        var store = AbilityOverrides.Load(json);

        Assert.Empty(store.BySpellId);
        Assert.Contains(store.Issues, i => i.Contains("mode", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("""{ "overrides": [ { "spellId": 1, "mode": "Auto", "minUrgency": 5 } ] }""")]
    [InlineData("""{ "overrides": [ { "spellId": 1, "mode": "Auto", "minUrgency": -1 } ] }""")]
    [InlineData("""{ "overrides": [ { "spellId": 1, "mode": "Auto", "minUrgency": "x" } ] }""")]
    public void Out_Of_Range_MinUrgency_Drops_Entry(string json)
    {
        var store = AbilityOverrides.Load(json);

        Assert.Empty(store.BySpellId);
        Assert.Contains(store.Issues, i => i.Contains("minUrgency", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_No_Op_Override_Is_Dropped()
    {
        var store = AbilityOverrides.Load("""{ "overrides": [ { "spellId": 1 } ] }""");

        Assert.Empty(store.BySpellId);
        Assert.Contains(store.Issues, i => i.Contains("neither", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Malformed_Json_Returns_Empty_Store_And_Logs()
    {
        var store = AbilityOverrides.Load("{ not json");

        Assert.Empty(store.BySpellId);
        Assert.Contains(store.Issues, i => i.Contains("malformed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Wrong_Schema_Rejects_The_File()
    {
        var store = AbilityOverrides.Load("""{ "schema": 2, "overrides": [ { "spellId": 1, "mode": "Auto" } ] }""");

        Assert.Empty(store.BySpellId);
        Assert.Contains(store.Issues, i => i.Contains("unsupported overrides schema", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_Overrides_Array_Returns_Empty_Store()
    {
        var store = AbilityOverrides.Load("""{ "schema": 1, "note": "x" }""");

        Assert.Empty(store.BySpellId);
        Assert.Contains(store.Issues, i => i.Contains("overrides", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(store.Issues, i => i.Contains("unknown top-level field", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Non_Object_Root_Returns_Empty_Store()
    {
        var store = AbilityOverrides.Load("[ 1, 2 ]");

        Assert.Empty(store.BySpellId);
        Assert.NotEmpty(store.Issues);
    }

    // ---- file + round trip -------------------------------------------------

    [Fact]
    public void LoadFile_Missing_Path_Is_The_No_Overrides_State_With_No_Issue()
    {
        var store = AbilityOverrides.LoadFile(Path.Combine(Path.GetTempPath(), "mdb-no-such-overrides.json"));

        Assert.False(store.HasOverrides);
        Assert.Empty(store.Issues);
    }

    [Fact]
    public void ToJson_Round_Trips_Sorted_And_Ignores_Transient()
    {
        var dir = NewTempDir();
        try
        {
            var store = AbilityOverrides.Load("""
                { "overrides": [
                    { "spellId": 20, "minUrgency": 1 },
                    { "spellId": 5, "mode": "Auto", "minUrgency": 4 } ] }
                """);
            var path = Path.Combine(dir, "ability-overrides.json");

            Assert.True(store.SaveFile(path));
            var reloaded = AbilityOverrides.LoadFile(path);

            Assert.False(reloaded.HasIssues);
            Assert.Equal(2, reloaded.BySpellId.Count);
            Assert.Equal(ClassOverlayMode.Auto, reloaded.For(5)!.Mode);
            Assert.Equal(4, reloaded.For(5)!.MinUrgency);
            Assert.Null(reloaded.For(20)!.Mode);
            Assert.Equal(1, reloaded.For(20)!.MinUrgency);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void With_And_Without_Edit_Immutably()
    {
        var store = AbilityOverrides.Empty
            .With(10, ClassOverlayMode.Suggest, 2)
            .With(20, ClassOverlayMode.Never, null);

        Assert.Equal(2, store.BySpellId.Count);
        Assert.Equal(ClassOverlayMode.Suggest, store.For(10)!.Mode);

        var cleared = store.Without(10);
        Assert.Single(cleared.BySpellId);
        Assert.Null(cleared.For(10));
        Assert.NotNull(cleared.For(20));

        // A no-op edit removes rather than stores.
        Assert.Null(store.With(20, null, null).For(20));
        Assert.Null(AbilityOverrides.Empty.With(0, ClassOverlayMode.Auto, null).For(0));
    }

    // ---- resolution: mode + urgency, never touch the rest ------------------

    private static ClassOverlayEntry Entry(
        int spellId,
        ClassOverlayMode mode,
        int minUrgency,
        ClassOverlayRole role = ClassOverlayRole.Major,
        ClassOverlayRoute route = ClassOverlayRoute.Defensive,
        bool emergencyEscape = true,
        bool offGcd = true,
        string? notes = "overlay note") =>
        new(spellId, role, route, emergencyEscape, offGcd, mode, minUrgency, notes);

    [Fact]
    public void Apply_Sets_Mode_And_Urgency_And_Preserves_Everything_Else()
    {
        var store = AbilityOverrides.Load("""{ "overrides": [ { "spellId": 100, "mode": "Suggest", "minUrgency": 1 } ] }""");
        var baseEntry = Entry(100, ClassOverlayMode.Auto, 4);

        var effective = store.Apply(baseEntry);

        Assert.Equal(ClassOverlayMode.Suggest, effective.Mode);
        Assert.Equal(1, effective.MinUrgency);
        // Untouched fields prove the override cannot re-route / re-role / toggle offGcd.
        Assert.Equal(ClassOverlayRole.Major, effective.Role);
        Assert.Equal(ClassOverlayRoute.Defensive, effective.Route);
        Assert.True(effective.EmergencyEscape);
        Assert.True(effective.OffGcd);
        Assert.Equal("overlay note", effective.Notes);
    }

    [Fact]
    public void Apply_Leaves_An_Unrelated_Entry_Byte_Identical()
    {
        var store = AbilityOverrides.Load("""{ "overrides": [ { "spellId": 100, "mode": "Never" } ] }""");
        var baseEntry = Entry(101, ClassOverlayMode.Auto, 2);

        Assert.Same(baseEntry, store.Apply(baseEntry));
    }

    [Fact]
    public void Apply_Uses_Overlay_Urgency_When_The_Override_Omits_It()
    {
        var store = AbilityOverrides.Load("""{ "overrides": [ { "spellId": 100, "mode": "Auto" } ] }""");

        Assert.Equal(4, store.Apply(Entry(100, ClassOverlayMode.Suggest, 4)).MinUrgency);
    }

    [Fact]
    public void Never_Base_Cannot_Be_Raised_By_An_Override()
    {
        var store = AbilityOverrides.Load("""
            { "overrides": [
                { "spellId": 200, "mode": "Auto", "minUrgency": 4 },
                { "spellId": 201, "mode": "Suggest" } ] }
            """);

        var effective = store.Apply(Entry(200, ClassOverlayMode.Never, 0));
        Assert.Equal(ClassOverlayMode.Never, effective.Mode);
        Assert.Equal(ClassOverlayMode.Never, store.ResolveMode(201, ClassOverlayMode.Never, null));
        // The urgency floor may still be tightened even while the mode stays Never.
        Assert.Equal(4, effective.MinUrgency);
    }

    [Fact]
    public void Precedence_Is_Registry_Lower_Than_Overlay_Lower_Than_Overrides()
    {
        var store = AbilityOverrides.Load("""{ "overrides": [ { "spellId": 300, "mode": "Auto" } ] }""");

        // Registry only (no overlay entry): the override wins.
        Assert.Equal(ClassOverlayMode.Auto, store.ResolveMode(300, ClassOverlayMode.Suggest, null));

        // Overlay present and stricter than the registry: the overlay wins over
        // the registry, and the override still wins over the overlay.
        Assert.Equal(ClassOverlayMode.Auto, store.ResolveMode(300, ClassOverlayMode.Auto, ClassOverlayMode.Suggest));

        // No override: the overlay outranks the registry, then the registry.
        Assert.Equal(ClassOverlayMode.Suggest, store.ResolveMode(301, ClassOverlayMode.Auto, ClassOverlayMode.Suggest));
        Assert.Equal(ClassOverlayMode.Auto, store.ResolveMode(301, ClassOverlayMode.Auto, null));
    }

    // ---- AppSettings integration ------------------------------------------

    [Fact]
    public void AppSettings_Loads_The_Store_Next_To_The_Settings_File()
    {
        var dir = NewTempDir();
        try
        {
            var ini = Path.Combine(dir, "settings.ini");
            File.WriteAllText(ini, "[AbilityOverrides]\nEnabled=1\nFile=ability-overrides.json\n");
            File.WriteAllText(Path.Combine(dir, "ability-overrides.json"),
                """{ "schema": 1, "overrides": [ { "spellId": 48792, "mode": "Suggest", "minUrgency": 3 } ] }""");

            var settings = AppSettings.Load(ini);

            Assert.Equal(Path.Combine(dir, "ability-overrides.json"), settings.AbilityOverridesPath());
            Assert.True(settings.AbilityOverrides.HasOverrides);
            Assert.Equal(ClassOverlayMode.Suggest, settings.AbilityOverrides.For(48792)!.Mode);
            Assert.Equal(3, settings.AbilityOverrides.For(48792)!.MinUrgency);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AppSettings_Disabling_Overrides_Ignores_A_Present_File()
    {
        var dir = NewTempDir();
        try
        {
            var ini = Path.Combine(dir, "settings.ini");
            File.WriteAllText(ini, "[AbilityOverrides]\nEnabled=0\n");
            File.WriteAllText(Path.Combine(dir, "ability-overrides.json"),
                """{ "overrides": [ { "spellId": 48792, "mode": "Auto" } ] }""");

            var settings = AppSettings.Load(ini);

            Assert.False(settings.AbilityOverrides.HasOverrides);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AppSettings_Missing_Store_File_Is_No_Overrides()
    {
        var dir = NewTempDir();
        try
        {
            var ini = Path.Combine(dir, "settings.ini");
            File.WriteAllText(ini, "[Bridge]\nCellSize=8\n");

            var settings = AppSettings.Load(ini);

            Assert.True(settings.AbilityOverridesEnabled);
            Assert.False(settings.AbilityOverrides.HasOverrides);
            Assert.Empty(settings.AbilityOverrides.Issues);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AppSettings_Save_Round_Trips_The_AbilityOverrides_Section()
    {
        var dir = NewTempDir();
        try
        {
            var ini = Path.Combine(dir, "settings.ini");
            var settings = AppSettings.Load(ini);
            settings.AbilityOverridesEnabled = false;
            settings.AbilityOverridesFile = "custom-overrides.json";
            settings.Save();

            var reloaded = AppSettings.Load(ini);

            Assert.False(reloaded.AbilityOverridesEnabled);
            Assert.Equal("custom-overrides.json", reloaded.AbilityOverridesFile);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mdb-overrides-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
