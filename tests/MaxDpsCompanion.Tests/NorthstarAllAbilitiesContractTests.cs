using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>Exhaustive policy/accountability contract for EVERY spell in the
/// embedded source, without turning incomplete knowledge into fake truth.</summary>
public class NorthstarAllAbilitiesContractTests
{
    [Fact]
    public void Every_Registered_Ability_Has_A_Deterministic_Decision_Contract()
    {
        var catalog = AbilityCatalog.Default;
        var rows = AbilityDecisionManifest.Build(catalog);
        Assert.Equal(catalog.Count, rows.Count);
        Assert.Equal(rows.Count, rows.Select(r => r.SpellId).Distinct().Count());
        Assert.True(rows.Count > 250, "base registry unexpectedly empty");
        Assert.All(rows, r =>
        {
            Assert.True(r.SpellId > 0);
            Assert.False(string.IsNullOrWhiteSpace(r.Name));
            Assert.False(string.IsNullOrWhiteSpace(r.Disposition));
            Assert.False(string.IsNullOrWhiteSpace(r.RequiredEvidence));
            Assert.False(string.IsNullOrWhiteSpace(r.Limitation));
        });
        Assert.All(rows.Where(r => r.Disposition is "Manual" or "ResearchPending"
                or "Unobservable" or "MaxDpsDelegated"),
            r => Assert.False(r.CompanionMayOffer));
    }

    [Fact]
    public void Every_Spell_Honestly_Reports_12_1_5_Live_Validation_Status()
    {
        var rows = AbilityDecisionManifest.Build(AbilityCatalog.Default);
        Assert.Contains(rows, r => !r.CurrentPatchLiveVerified);
        Assert.Contains(rows, r => r.Disposition == "MaxDpsDelegated");
        Assert.Contains(rows, r => r.Disposition == "Manual");
        Assert.Contains(rows, r => r.Disposition == "CompanionConditional");
        Assert.Contains(rows, r => r.Disposition == "SharedSafetyGate");
    }

    [Fact]
    public void Every_Dps_And_Tank_Spec_Has_A_Visible_Policy_Inventory()
    {
        var rows = SpecReadiness.Build(AbilityCatalog.Default, ClassSpellBook.Default);
        var combat = rows.Where(r => r.Role != CombatRole.Healer).ToArray();
        Assert.Equal(33, combat.Length);
        Assert.All(combat, r => Assert.True(r.Registered > 0,
            $"missing {r.ClassName}/{r.Spec}"));
        // Enumerating 33 specs does NOT constitute verified talent coverage.
        Assert.All(combat, r => Assert.False(r.LiveFullyVerified));
    }

    [Fact]
    public void Manifest_Exports_Full_Set_With_Stable_Header()
    {
        var file=Path.Combine(Path.GetTempPath(),
            $"northstar-contracts-{Guid.NewGuid():N}.tsv");
        try
        {
            AbilityDecisionManifest.WriteTsv(file, AbilityCatalog.Default);
            var lines=File.ReadAllLines(file);
            Assert.Equal(AbilityCatalog.Default.Count+1,lines.Length);
            Assert.StartsWith("spellId\tname\tclasses\tspecs",lines[0]);
            Assert.All(lines.Skip(1), l => Assert.Equal(15,l.Split('\t').Length));
        }
        finally { if(File.Exists(file)) File.Delete(file); }
    }
}
