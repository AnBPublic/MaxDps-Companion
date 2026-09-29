using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// v2.6 ability-intelligence registry: the machine-checkable audit invariants,
/// the registry/runtime agreement, the patch/version guard, the catalogue of
/// interrupts and the research externals + manual utility set.
/// </summary>
public class AbilityRegistryTests
{
    private static AbilityCatalog Catalog => AbilityCatalog.Default;

    // ---- 1. audit invariants -------------------------------------------------

    [Fact]
    public void Audit_Has_Zero_Violations_For_The_Embedded_Catalog()
    {
        var report = AbilityIntelligence.Audit(Catalog);
        Assert.Equal(0, report.Summary.Violations);
        Assert.True(report.Summary.Total > 240, $"catalog unexpectedly small: {report.Summary.Total}");
        Assert.Equal(0, report.Summary.Unknown);
    }

    [Fact]
    public void Automatic_Entries_With_Blocking_Status_Are_MaxDps_Only()
    {
        foreach (var ability in Catalog.All)
        {
            if (ability.NeverAutomatic) continue;
            if (ability.Status is IntelligenceStatus.Incomplete or IntelligenceStatus.Unknown
                or IntelligenceStatus.UnsafeToAutomate)
            {
                Assert.Equal(AutomationContext.MaxDpsOnly, ability.Automation);
            }
        }
    }

    [Fact]
    public void Every_Interrupt_Capable_Entry_States_Its_Kind()
    {
        var any = false;
        foreach (var ability in Catalog.All)
        {
            if (!ability.HasInterruptCapability) continue;
            any = true;
            Assert.NotEqual(InterruptKind.Unknown, ability.InterruptKind);
        }
        Assert.True(any, "catalog carries no interrupt-capable entries");
    }

    [Fact]
    public void Every_Major_Defensive_Carries_A_Major_Or_Immunity_Tier()
    {
        var any = false;
        foreach (var ability in Catalog.All)
        {
            if (ability.Purpose != AbilityPurpose.DefensiveMajor) continue;
            any = true;
            Assert.True(ability.Tier is DefensiveTier.Major or DefensiveTier.Immunity,
                $"{ability.SpellId} {ability.Name} is a major defensive with tier {ability.Tier}");
        }
        Assert.True(any, "catalog carries no major defensives");
    }

    [Fact]
    public void Every_Never_Automatic_Entry_Is_Manual()
    {
        var any = false;
        foreach (var ability in Catalog.All)
        {
            if (!ability.NeverAutomatic) continue;
            any = true;
            Assert.Equal(AutomationContext.Manual, ability.Automation);
        }
        Assert.True(any, "catalog carries no manual-by-design entries");
    }

    // ---- 2. registry / runtime agreement ------------------------------------

    [Fact]
    public void Automation_Allowed_Matches_Status_And_Manual_Flag()
    {
        foreach (var ability in Catalog.All)
        {
            var expected = ability.Status is not (IntelligenceStatus.Incomplete
                or IntelligenceStatus.Unknown or IntelligenceStatus.UnsafeToAutomate)
                && !ability.NeverAutomatic;
            Assert.Equal(expected, ability.AutomationAllowed);
        }
    }

    [Fact]
    public void Extras_And_Defensive_Gap_Fill_Respect_Automation_Scope()
    {
        foreach (var className in AbilityCatalog.ClassOrder.Skip(1))
        {
            foreach (var spec in AbilityCatalog.SpecOrder[className].Skip(1))
            {
                foreach (var id in Catalog.Extras(className, spec, AbilityCategory.Mobility))
                {
                    // Mobility extras intentionally mix gap closers (Autonomous:
                    // the policy may generate them) with movement/escape
                    // utilities (Manual: emergency-only, never auto-generated).
                    // The invalid states are a DELEGATED (MaxDpsOnly) mobility
                    // extra or one whose intelligence is not meaningful.
                    var extra = Catalog.TryGet(id)!;
                    Assert.True(extra.Automation is AutomationContext.Autonomous or AutomationContext.Manual,
                        $"{className}/{spec} mobility extra {id} is {extra.Automation}");
                    Assert.True(extra.Status is not (IntelligenceStatus.Incomplete
                        or IntelligenceStatus.Unknown or IntelligenceStatus.UnsafeToAutomate),
                        $"{className}/{spec} mobility extra {id} status is {extra.Status}");
                }

                // Self-sustain candidates are companion-generated: they must be
                // autonomous with meaningful intelligence.
                foreach (var id in Catalog.Extras(className, spec, AbilityCategory.SelfHeal))
                {
                    var extra = Catalog.TryGet(id)!;
                    Assert.Equal(AutomationContext.Autonomous, extra.Automation);
                    Assert.True(extra.Status is not (IntelligenceStatus.Incomplete
                        or IntelligenceStatus.Unknown or IntelligenceStatus.UnsafeToAutomate),
                        $"{className}/{spec} self-heal extra {id} status is {extra.Status}");
                }

                // The bridge only gap-fills a defensive the companion itself may
                // generate (no class-spell tail, no manual entry).
                foreach (var id in Catalog.DefensiveGapFill(className, spec))
                    Assert.Equal(AutomationContext.Autonomous, Catalog.TryGet(id)!.Automation);
            }
        }
    }

    // ---- 3. patch / version guard -------------------------------------------

    [Fact]
    public void Version_Metadata_Is_Pinned()
    {
        Assert.Equal("12.1", Catalog.GamePatch);
        Assert.Equal(120100, Catalog.InterfaceVersion);
        Assert.Equal(3, AbilityCatalog.CatalogVersion);
        Assert.Equal("12.1", AbilityCatalog.ExpectedGamePatch);
        Assert.Equal(120100, AbilityCatalog.ExpectedInterfaceVersion);
    }

    [Fact]
    public void Curated_File_For_Another_Patch_Is_Rejected()
    {
        const string vendor = """{ "entries": [] }""";
        const string curated = """
            { "gamePatch": "12.0", "interface": 120100, "abilities": [] }
            """;
        var ex = Assert.Throws<InvalidDataException>(() => AbilityCatalog.Load(vendor, curated));
        Assert.Contains("12.0", ex.Message);
        Assert.Contains("12.1", ex.Message);
    }

    // ---- 4. interrupt catalogue ---------------------------------------------

    [Theory]
    [InlineData(6552, "Dedicated")]      // Warrior Pummel
    [InlineData(96231, "Dedicated")]     // Paladin Rebuke
    [InlineData(47528, "Dedicated")]     // Death Knight Mind Freeze
    [InlineData(183752, "Dedicated")]    // Demon Hunter Disrupt
    [InlineData(106839, "Dedicated")]    // Druid Skull Bash
    [InlineData(351338, "Dedicated")]    // Evoker Quell
    [InlineData(147362, "Dedicated")]    // Hunter Counter Shot
    [InlineData(187707, "Dedicated")]    // Hunter Muzzle
    [InlineData(2139, "Dedicated")]      // Mage Counterspell
    [InlineData(116705, "Dedicated")]    // Monk Spear Hand Strike
    [InlineData(15487, "Silence")]       // Priest Silence
    [InlineData(1766, "Dedicated")]      // Rogue Kick
    [InlineData(57994, "Dedicated")]     // Shaman Wind Shear
    [InlineData(119910, "Silence")]      // Warlock Spell Lock
    public void Catalogue_Of_Interrupts_Is_Present_And_Typed(int spellId, string kind)
    {
        var ability = Catalog.TryGet(spellId);
        Assert.NotNull(ability);
        Assert.True(ability!.HasInterruptCapability, $"{spellId} has no interrupt capability");
        Assert.Equal(kind, ability.InterruptKind.ToString());
        Assert.Equal(UnknownPolicy.Use, ability.Unknown);
    }

    // ---- 5. research externals ----------------------------------------------

    [Fact]
    public void Shadowstep_Is_A_Gap_Closer()
    {
        var shadowstep = Catalog.TryGet(36554);
        Assert.NotNull(shadowstep);
        Assert.Equal(AbilityCategory.Mobility, shadowstep!.Category);
        Assert.Equal(AbilityPurpose.GapCloser, shadowstep.Purpose);
        Assert.Equal(MobilityKind.GapCloser, shadowstep.MobilityKind);
        Assert.False(shadowstep.NeverAutomatic);
        Assert.Equal(AutomationContext.Autonomous, shadowstep.Automation);
    }

    [Fact]
    public void Vanish_Is_Manual_By_Design()
    {
        var vanish = Catalog.TryGet(1856);
        Assert.NotNull(vanish);
        Assert.True(vanish!.NeverAutomatic);
        Assert.True(vanish.ManualByDesign);
        Assert.Equal(AutomationContext.Manual, vanish.Automation);
        Assert.Equal(IntelligenceStatus.ManualByDesign, vanish.Status);
    }

    [Fact]
    public void Impending_Victory_Is_A_Quantified_Self_Heal()
    {
        var heal = Catalog.TryGet(202168);
        Assert.NotNull(heal);
        Assert.Equal(AbilityCategory.SelfHeal, heal!.Category);
        Assert.Equal(AbilityPurpose.SelfHeal, heal.Purpose);
        Assert.Equal(30, heal.HealPctMaxHp);
        Assert.Equal(AutomationContext.Autonomous, heal.Automation);
        Assert.False(heal.NeverAutomatic);
    }

    // ---- 6. manual utility set ----------------------------------------------

    [Theory]
    [InlineData(5246, "CrowdControl")] // Intimidating Shout
    [InlineData(118, "CrowdControl")]  // Polymorph
    [InlineData(2094, "CrowdControl")] // Blind
    [InlineData(370, "Purge")]         // Purge
    public void Manual_Utility_Is_Present_And_Manual_By_Design(int spellId, string purpose)
    {
        var ability = Catalog.TryGet(spellId);
        Assert.NotNull(ability);
        Assert.Equal(purpose, ability!.Purpose.ToString());
        Assert.True(ability.NeverAutomatic);
        Assert.Equal(IntelligenceStatus.ManualByDesign, ability.Status);
        Assert.Equal(AutomationContext.Manual, ability.Automation);
    }
}
