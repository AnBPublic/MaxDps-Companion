using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// v3.4.0 Approach A §4-§6: the shared preset vocabulary behind the clickable
/// hero bubbles and the pre-filtered Abilities/Class skills views. Offline
/// only — the live popup remains OWED (docs/TESTING.md §3).
/// </summary>
public class ApproachADrillThroughTests
{
    private static (T? Result, Exception? Error) RunOnSta<T>(Func<T> action)
    {
        T? result = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "STA UI thread timed out");
        return (result, error);
    }

    [Fact]
    public void Preset_Vocabulary_Normalizes_And_Matches()
    {
        Assert.Equal("Self Sustain", AbilityViewPresets.NormalizeCategory("Self-heal"));
        Assert.Equal("CrowdControl", AbilityViewPresets.NormalizeCategory("Crowd control"));
        Assert.True(AbilityViewPresets.IsCategoryTag("Offensive"));
        Assert.True(AbilityViewPresets.IsCategoryTag("Solo"));
        Assert.False(AbilityViewPresets.IsCategoryTag("Companion"));
        Assert.False(AbilityViewPresets.IsCategoryTag("Warnings"));

        var catalog = AbilityCatalog.Default;
        var offensive = catalog.All.First(a => a.HasOffensiveCapability);
        Assert.True(AbilityViewPresets.MatchesCategory(offensive, "Offensive", catalog, null, null));

        var polymorph = catalog.TryGet(118);
        Assert.NotNull(polymorph);
        Assert.True(AbilityViewPresets.MatchesCategory(polymorph!, "Crowd control", catalog, "MAGE", "Fire"));
        Assert.True(AbilityViewPresets.MatchesCategory(polymorph!, "CrowdControl", catalog, "MAGE", "Fire"));

        var solo = catalog.All.First(a => a.Purpose == AbilityPurpose.SelfHeal || a.HasTag("SelfSustain"));
        Assert.True(AbilityViewPresets.MatchesCategory(solo, "Solo", catalog, null, null));
    }

    [Fact]
    public void ConditionLine_Uses_Catalog_Fields()
    {
        var catalog = AbilityCatalog.Default;

        var below = catalog.All.First(a => a.UseBelowHpPct is not null);
        Assert.StartsWith("Below ", AbilityViewPresets.ConditionLine(below));

        var aoe = catalog.All.FirstOrDefault(a => a.OffensiveUsage == OffensiveUsage.AoeOnly);
        if (aoe is not null)
            Assert.Contains("AoE only", AbilityViewPresets.ConditionLine(aoe));

        var polymorph = catalog.TryGet(118);
        Assert.NotNull(polymorph);
        var dr = AbilityViewPresets.ConditionLine(polymorph!, catalog.CrowdControlFor("MAGE", "Fire", 118));
        Assert.Contains("DR: ", dr);
    }

    [Fact]
    public void Explorer_Preset_Type_Filter_And_EmptyState_Names_Class()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            form.CreateControl();
            var explorer = form.ExplorerForTest;
            explorer.ApplyPreset("Offensive");
            var offensive = explorer.FilteredCount;
            explorer.ApplyPreset("Trinket", "MAGE", "Fire");
            var trinket = explorer.FilteredCount;
            var empty = explorer.EmptyStateForTest;
            return (offensive, trinket, empty);
        });

        Assert.Null(error);
        Assert.True(result.offensive > 0, "the catalog should contain offensive abilities");
        Assert.Equal(0, result.trinket);
        Assert.Contains("Trinket", result.empty);
        Assert.Contains("Mage/Fire", result.empty);
    }

    [Fact]
    public void ClassSkills_Preset_Filters_And_Names_Toggle()
    {
        var (result, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            form.CreateControl();
            var view = form.ClassSkillsForTest!;
            view.Open("ROGUE", "Outlaw", null);
            var allRows = RowCount(form.ClassSkillsDebugState);
            view.Open("ROGUE", "Outlaw", "Offensive");
            var offensiveRows = RowCount(form.ClassSkillsDebugState);
            view.Open("ROGUE", "Outlaw", "Trinket");
            var trinketRows = RowCount(form.ClassSkillsDebugState);
            var emptyText = view.EmptyTextForTest;
            return (allRows, offensiveRows, trinketRows, emptyText);
        });

        Assert.Null(error);
        Assert.True(result.allRows > 0, "the full class list should have rows");
        Assert.True(result.offensiveRows > 0, "Rogue/Outlaw should have offensive abilities");
        Assert.True(result.offensiveRows < result.allRows, "the preset must narrow the list");
        Assert.Equal(0, result.trinketRows);
        Assert.Contains("No Trinket skills for Rogue/Outlaw", result.emptyText);
    }

    private static int RowCount(string debugState)
    {
        const string token = "rows=";
        var start = debugState.IndexOf(token, StringComparison.Ordinal);
        if (start < 0) return -1;
        start += token.Length;
        var end = start;
        while (end < debugState.Length && char.IsDigit(debugState[end])) end++;
        return int.Parse(debugState[start..end], System.Globalization.CultureInfo.InvariantCulture);
    }
}
