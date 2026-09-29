using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// v2.7 intelligence coverage registry: ownership/completeness axes, mandatory
/// delegation/manual reasons, patch metadata, stale/newer detection and the
/// generated coverage manifest (v2.7 §4-§7). These tests pin the property that
/// a registry row can never be mistaken for implemented intelligence.
/// </summary>
public class AbilityCoverageTests
{
    private static AbilityCatalog Catalog => AbilityCatalog.Default;

    // ---- 1. disposition axes -------------------------------------------------

    [Fact]
    public void Every_Registered_Entry_Has_Patch_Metadata()
    {
        foreach (var ability in Catalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(ability.SourcePatch),
                $"{ability.SpellId} {ability.Name} has no source patch metadata");
            Assert.False(string.IsNullOrWhiteSpace(ability.LastValidatedPatch),
                $"{ability.SpellId} {ability.Name} has no last-validated patch metadata");
        }
    }

    [Fact]
    public void Every_Automatable_Entry_Has_A_Candidate_Path()
    {
        var any = false;
        foreach (var ability in Catalog.All)
        {
            if (!ability.Automatable) continue;
            any = true;
            Assert.True(ability.HasCandidatePath,
                $"{ability.SpellId} {ability.Name} is automatable without a candidate path or MaxDps delegation");
            Assert.NotEqual(IntelligenceOwnership.Unavailable, ability.Ownership);
        }
        Assert.True(any, "catalog carries no automatable entries");
    }

    [Fact]
    public void Every_MaxDps_Owned_Entry_Has_A_Delegation_Reason()
    {
        var any = false;
        foreach (var ability in Catalog.All)
        {
            if (ability.Ownership != IntelligenceOwnership.MaxDps) continue;
            any = true;
            Assert.True(ability.HasDelegationReason,
                $"{ability.SpellId} {ability.Name} is MaxDps-owned without a delegation reason");
        }
        Assert.True(any, "catalog carries no MaxDps-owned entries");
    }

    [Fact]
    public void Every_Manual_Entry_Has_A_Manual_Reason_And_Cannot_Automate()
    {
        var any = false;
        foreach (var ability in Catalog.All)
        {
            if (ability.Ownership != IntelligenceOwnership.Manual) continue;
            any = true;
            Assert.True(ability.HasManualReason, $"{ability.SpellId} {ability.Name} is manual without a reason");
            Assert.True(ability.NeverAutomatic, $"{ability.SpellId} {ability.Name} is manual without the structural flag");
            Assert.Equal(AutomationContext.Manual, ability.Automation);
            Assert.False(ability.HasCandidatePath, $"{ability.SpellId} {ability.Name} manual but has a candidate path");
        }
        Assert.True(any, "catalog carries no manual entries");
    }

    [Fact]
    public void Incomplete_Entries_Are_Unavailable_Or_Delegated_Never_Companion()
    {
        foreach (var ability in Catalog.All)
        {
            if (ability.Status is not (IntelligenceStatus.Incomplete or IntelligenceStatus.Unknown)) continue;
            Assert.True(ability.Ownership is IntelligenceOwnership.MaxDps or IntelligenceOwnership.Manual
                or IntelligenceOwnership.Unavailable,
                $"{ability.SpellId} {ability.Name} incomplete but owned by {ability.Ownership}");
            if (ability.Ownership == IntelligenceOwnership.MaxDps)
                Assert.True(ability.HasDelegationReason);
        }
    }

    // ---- 2. the v2.7 spec examples -------------------------------------------

    [Fact]
    public void Spec_Examples_Map_To_The_Documented_Dispositions()
    {
        var pummel = Catalog.TryGet(6552)!;
        Assert.Equal(IntelligenceOwnership.Companion, pummel.Ownership);
        Assert.Equal(IntelligenceCompleteness.Complete, pummel.Completeness);
        Assert.Equal(IntelligenceOwnership.Manual, Catalog.TryGet(5246)!.Ownership);                 // Intimidating Shout
        Assert.Equal(IntelligenceCompleteness.ManualByDesign, Catalog.TryGet(5246)!.Completeness);
        Assert.Equal(ManualReason.CrowdControlTargetStateUnobservable, Catalog.TryGet(5246)!.ManualReason);
        Assert.Equal(IntelligenceOwnership.Companion, Catalog.TryGet(202168)!.Ownership);            // Impending Victory
        var recklessness = Catalog.TryGet(1719)!;
        Assert.Equal(IntelligenceOwnership.MaxDps, recklessness.Ownership);
        Assert.Equal(IntelligenceCompleteness.Delegated, recklessness.Completeness);
        Assert.True(recklessness.Delegation.HasFlag(DelegationReason.RotationOrdering) ||
                    recklessness.Delegation.HasFlag(DelegationReason.ComplexBuffWindow),
            $"Recklessness delegation reasons are {recklessness.Delegation}");
    }

    // ---- 3. coverage manifest -------------------------------------------------

    [Fact]
    public void Coverage_Manifest_Is_Clean_And_Internally_Consistent()
    {
        var report = AbilityCoverage.Build(Catalog);
        Assert.True(report.Clean, $"coverage not clean: missing {report.Missing}, stale {report.Stale}");
        Assert.Equal(0, report.Missing);
        Assert.Equal(0, report.Stale);
        Assert.Equal(Catalog.Count, report.Registered);
        Assert.True(report.Discovered >= report.Registered, "discovered must cover the registry");

        var audit = AbilityIntelligence.Audit(Catalog);
        Assert.Equal(0, audit.TotalViolations);
        Assert.NotNull(audit.Coverage);
        Assert.Equal(audit.Summary.Automatic, report.Automatable);
        Assert.Equal(audit.Summary.Manual, report.Manual);
        Assert.Equal(audit.Summary.Incomplete, report.ResearchPending);
        Assert.True(
            audit.Summary.Automatic == report.CompanionGenerated + report.MaxDpsDelegated + report.SharedGated,
            "every automatable entry must land in exactly one generation class");
        Assert.Equal(0, report.LiveVerified); // no live behavior pass has been recorded yet
        Assert.True(report.LiveUnverified > 0);
        Assert.Equal(Catalog.SourceClassSpellFilteredIds.Count, report.Filtered);
    }

    [Fact]
    public void Coverage_Json_Exposes_The_Required_Classes()
    {
        var json = AbilityCoverage.ToJson(AbilityCoverage.Build(Catalog));
        foreach (var key in new[]
                 {
                     "\"discovered\"", "\"registered\"", "\"automatable\"", "\"companionGenerated\"",
                     "\"maxDpsDelegated\"", "\"sharedGated\"", "\"manual\"", "\"unobservable\"",
                     "\"researchPending\"", "\"stale\"", "\"missing\"", "\"duplicates\"",
                     "\"liveVerified\"", "\"liveUnverified\"", "\"filtered\"", "\"clean\": true",
                 })
            Assert.Contains(key, json);
    }

    // ---- 4. stale / newer intelligence can never silently pass ---------------

    [Fact]
    public void Stale_Knowledge_Fails_The_Audit()
    {
        const string vendor = """{ "entries": [] }""";
        const string curated = """
            { "gamePatch": "12.1", "interface": 120100, "verified": "2026-09-28",
              "abilities": [
                { "id": 900001, "name": "Stale Fixture", "category": "SelfHeal", "purpose": "SelfHeal",
                  "sourcePatch": "12.0", "lastValidatedPatch": "12.0", "source": "fixture" } ] }
            """;
        var catalog = AbilityCatalog.Load(vendor, curated);
        var ability = catalog.TryGet(900001)!;
        Assert.True(AbilityCoverage.IsStale(ability, catalog.GamePatch));
        var row = AbilityIntelligence.Audit(ability, catalog);
        Assert.True(row.HasViolation);
        Assert.Contains(row.Findings, f => f.Check == "stale" && f.Severity == AuditSeverity.Violation);
    }

    [Fact]
    public void Newer_Knowledge_Fails_The_Audit()
    {
        const string vendor = """{ "entries": [] }""";
        const string curated = """
            { "gamePatch": "12.1", "interface": 120100, "verified": "2026-09-28",
              "abilities": [
                { "id": 900002, "name": "Future Fixture", "category": "SelfHeal", "purpose": "SelfHeal",
                  "sourcePatch": "12.2", "lastValidatedPatch": "12.2", "source": "fixture" } ] }
            """;
        var catalog = AbilityCatalog.Load(vendor, curated);
        var ability = catalog.TryGet(900002)!;
        Assert.True(AbilityCoverage.IsNewer(ability, catalog.GamePatch));
        var row = AbilityIntelligence.Audit(ability, catalog);
        Assert.True(row.HasViolation);
        Assert.Contains(row.Findings, f => f.Check == "patch-newer" && f.Severity == AuditSeverity.Violation);
    }

    [Fact]
    public void A_Current_Patch_Entry_Is_Neither_Stale_Nor_Newer()
    {
        var pummel = Catalog.TryGet(6552)!;
        Assert.False(AbilityCoverage.IsStale(pummel, Catalog.GamePatch));
        Assert.False(AbilityCoverage.IsNewer(pummel, Catalog.GamePatch));
    }

    // ---- 5. curated overrides win ---------------------------------------------

    [Fact]
    public void Curated_Ownership_Completeness_And_Reasons_Are_Honoured()
    {
        const string vendor = """{ "entries": [] }""";
        const string curated = """
            { "gamePatch": "12.1", "interface": 120100, "verified": "2026-09-28",
              "abilities": [
                { "id": 900003, "name": "Curated Axes", "category": "Offensive", "purpose": "MajorOffensive",
                  "cdMs": 120000, "ownership": "Companion", "completeness": "Partial",
                  "delegation": ["ComplexBuffWindow"], "delegationNote": "curated note",
                  "source": "fixture research" } ] }
            """;
        var catalog = AbilityCatalog.Load(vendor, curated);
        var ability = catalog.TryGet(900003)!;
        Assert.Equal(IntelligenceOwnership.Companion, ability.Ownership);
        Assert.Equal(IntelligenceCompleteness.Partial, ability.Completeness);
        Assert.Equal(DelegationReason.ComplexBuffWindow, ability.Delegation);
        Assert.Equal("curated note", ability.DelegationNote);
        // A delegation reason on a Companion-owned entry is a hygiene warning,
        // not a violation: the curated data is explicit about its intent.
        var row = AbilityIntelligence.Audit(ability, catalog);
        Assert.False(row.HasViolation);
        Assert.Contains(row.Findings, f => f.Check == "delegation-hygiene" && f.Severity == AuditSeverity.Warning);
    }

    [Fact]
    public void Curated_Manual_Reason_Is_Honoured()
    {
        const string vendor = """{ "entries": [] }""";
        const string curated = """
            { "gamePatch": "12.1", "interface": 120100, "verified": "2026-09-28",
              "abilities": [
                { "id": 900004, "name": "Curated Manual", "category": "Utility", "purpose": "CrowdControl",
                  "neverAutomatic": true, "manualReason": "RaidCoordination", "status": "ManualByDesign",
                  "source": "fixture research" } ] }
            """;
        var catalog = AbilityCatalog.Load(vendor, curated);
        var ability = catalog.TryGet(900004)!;
        Assert.Equal(IntelligenceOwnership.Manual, ability.Ownership);
        Assert.Equal(ManualReason.RaidCoordination, ability.ManualReason);
        Assert.False(AbilityIntelligence.Audit(ability, catalog).HasViolation);
    }

    [Fact]
    public void New_Relationship_Kinds_Parse_From_Curated_Json()
    {
        const string vendor = """{ "entries": [] }""";
        const string curated = """
            { "gamePatch": "12.1", "interface": 120100, "verified": "2026-09-28",
              "abilities": [
                { "id": 900005, "name": "Relation Fixture", "category": "Interrupt", "purpose": "Interrupt",
                  "source": "fixture research",
                  "relations": [ { "kind": "ReplacedBy", "id": 119910, "note": "live id" },
                                 { "kind": "PairsWith", "id": 6552 } ] } ] }
            """;
        var catalog = AbilityCatalog.Load(vendor, curated);
        var ability = catalog.TryGet(900005)!;
        Assert.Contains(ability.Relations, r => r.Kind == RelationshipKind.ReplacedBy && r.SpellId == 119910);
        Assert.Contains(ability.Relations, r => r.Kind == RelationshipKind.PairsWith && r.SpellId == 6552);
    }
}
