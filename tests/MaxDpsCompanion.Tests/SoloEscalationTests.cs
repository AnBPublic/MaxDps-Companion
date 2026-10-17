using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// v3.3.0 Solo HP-banded escalation: the survival ladder widens beyond
/// self-heals (minor absorb &lt;=75, heal &lt;=65, major &lt;=50, immunity
/// &lt;=30). Solo-only HP-substitution bypasses the White-urgency hold for
/// gap-fill defensives; groups and MaxDps-flagged candidates are unchanged.
/// </summary>
public class SoloEscalationTests
{
    private const long Now = 10_000;
    private static AbilityCatalog Catalog => AbilityCatalog.Default;

    private static CombatContext Context(
        int hp,
        DefensiveUrgency urgency = DefensiveUrgency.White,
        bool catalogSource = true) => new()
    {
        HpValid = true,
        HpPct = hp,
        Cast = PlayerCastState.None,
        TargetCasting = TriState.No,
        TargetCastInterruptible = TriState.Unknown,
        TargetInMelee = TriState.Unknown,
        SlotRange = new TriState[PixelProtocol.SlotCount],
        SlotBuffActive = new TriState[PixelProtocol.SlotCount],
        DefensiveUrgency = urgency,
        StaggerUrgency = DefensiveUrgency.Unknown,
        DefensiveCatalogSource = catalogSource,
        ContextValid = true,
    };

    private static PolicyDecision Evaluate(
        Slot slot, int spellId, CombatContext context, PolicyOptions? options = null) =>
        PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = slot,
            SpellId = spellId,
            Context = context,
            Options = options ?? new PolicyOptions { SoloEnabled = true },
            Memory = new PolicyMemory(),
            NowMs = Now,
            InCombat = true,
            HasTarget = true,
        }, Catalog);

    private static PolicyOptions Solo => new() { SoloEnabled = true };

    [Fact]
    public void Solo_Minor_At_74_Uses_Despite_White_Urgency()
    {
        // Feint (1966, Minor): Solo 74% is inside the minor band (<=75), so
        // the White-urgency hold is bypassed.
        var result = Evaluate(Slot.Defensive, 1966, Context(hp: 74), Solo);
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.Contains("solo ladder", result.Reason);
    }

    [Fact]
    public void Solo_Minor_Above_Band_Holds()
    {
        var result = Evaluate(Slot.Defensive, 1966, Context(hp: 80), Solo);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("minor band", result.Reason);
    }

    [Fact]
    public void Solo_Major_At_51_Holds_And_At_49_Uses()
    {
        // Shield Wall (871, Major): 51% is above the major band (<=50).
        var held = Evaluate(Slot.Defensive, 871, Context(hp: 51), Solo);
        Assert.Equal(PolicyVerdict.Hold, held.Verdict);
        Assert.Contains("major band", held.Reason);

        var used = Evaluate(Slot.Defensive, 871, Context(hp: 49), Solo);
        Assert.Equal(PolicyVerdict.Use, used.Verdict);
        Assert.Contains("solo ladder", used.Reason);
    }

    [Fact]
    public void Solo_Immunity_At_31_Holds_And_At_30_Uses()
    {
        // Ice Block (45438, Immunity): band is <=30.
        var held = Evaluate(Slot.Defensive, 45438, Context(hp: 31), Solo);
        Assert.Equal(PolicyVerdict.Hold, held.Verdict);
        Assert.Contains("immunity band", held.Reason);

        var used = Evaluate(Slot.Defensive, 45438, Context(hp: 30), Solo);
        Assert.Equal(PolicyVerdict.Use, used.Verdict);
        Assert.True(used.Emergency);
        Assert.Contains("emergency", used.Reason);
    }

    [Fact]
    public void Solo_Immunity_Blocked_When_One_Is_Active()
    {
        var memory = new PolicyMemory();
        var cloak = Catalog.TryGet(31224)!;
        memory.NoteUse(cloak, Now - 1000);
        var result = PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = Slot.Defensive,
            SpellId = 45438,
            Context = Context(hp: 25),
            Options = Solo,
            Memory = memory,
            NowMs = Now,
            InCombat = true,
            HasTarget = true,
        }, Catalog);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("immunity already active", result.Reason);
    }

    [Fact]
    public void Group_NonSolo_Keeps_Classic_White_Hold()
    {
        // Same minor at 74% with Solo OFF: White still holds (group parity).
        var result = Evaluate(Slot.Defensive, 1966, Context(hp: 74),
            new PolicyOptions { SoloEnabled = false });
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("white", result.Reason);
    }

    [Fact]
    public void Escalation_Off_Keeps_Classic_White_Hold()
    {
        var options = new PolicyOptions { SoloEnabled = true, SoloEscalation = false };
        var result = Evaluate(Slot.Defensive, 1966, Context(hp: 74), options);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("white", result.Reason);
    }

    [Fact]
    public void MaxDps_Flagged_Candidate_Ignores_Solo_Bands()
    {
        // MaxDps's own recommendation (catalogSource false) keeps the classic
        // urgency path: White holds even in Solo.
        var result = Evaluate(Slot.Defensive, 1966, Context(hp: 74, catalogSource: false), Solo);
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("white", result.Reason);
    }

    [Fact]
    public void Solo_Band_Ordering_Validator_Never_Inverts()
    {
        var (minor, major, imm) = PolicyOptions.ValidateSoloBands(50, 75, 60);
        Assert.True(imm < major && major < minor);
    }

    [Fact]
    public void GapFill_Lists_Cover_Major_And_Immunity()
    {
        // Every spec with a vendor defensive has a Major list; immunities are
        // offered only via the Solo ladder list, never via DefensiveGapFill.
        var armsMajors = Catalog.DefensiveGapFillMajor("WARRIOR", "Arms");
        Assert.Contains(871, armsMajors);
        Assert.DoesNotContain(45438, Catalog.DefensiveGapFill("MAGE", "Frost"));
        Assert.Contains(45438, Catalog.ImmunityGapFill("MAGE", "Frost"));
        Assert.Contains(642, Catalog.ImmunityGapFill("PALADIN", "Retribution"));
        Assert.Contains(31224, Catalog.ImmunityGapFill("ROGUE", "Assassination"));
        Assert.Contains(196555, Catalog.ImmunityGapFill("DEMONHUNTER", "Havoc"));
    }

    [Fact]
    public void GapFill_Major_And_Immunity_Respect_Automation_Scope()
    {
        foreach (var className in AbilityCatalog.ClassOrder.Skip(1))
        {
            foreach (var spec in AbilityCatalog.SpecOrder[className].Skip(1))
            {
                foreach (var id in Catalog.DefensiveGapFillMajor(className, spec))
                    Assert.Equal(AutomationContext.Autonomous, Catalog.TryGet(id)!.Automation);
                foreach (var id in Catalog.ImmunityGapFill(className, spec))
                    Assert.Equal(AutomationContext.Autonomous, Catalog.TryGet(id)!.Automation);
            }
        }
    }

    [Fact]
    public void SelfHeal_Extras_Cover_Previous_Gaps()
    {
        // v3.3.0 data: mages sustain via barriers, shamans via HST, priests
        // via PW:Life / Vampiric Embrace, Preservation via Rewind.
        Assert.Contains(11426, Catalog.Extras("MAGE", "Frost", AbilityCategory.SelfHeal));
        Assert.Contains(235313, Catalog.Extras("MAGE", "Fire", AbilityCategory.SelfHeal));
        Assert.Contains(235450, Catalog.Extras("MAGE", "Arcane", AbilityCategory.SelfHeal));
        Assert.Contains(5394, Catalog.Extras("SHAMAN", "Elemental", AbilityCategory.SelfHeal));
        Assert.Contains(373481, Catalog.Extras("PRIEST", "Holy", AbilityCategory.SelfHeal));
        Assert.Contains(15286, Catalog.Extras("PRIEST", "Shadow", AbilityCategory.SelfHeal));
        Assert.Contains(363534, Catalog.Extras("EVOKER", "Preservation", AbilityCategory.SelfHeal));
    }

    [Fact]
    public void Generated_Lua_Emits_Ladder_Lists()
    {
        var lua = CatalogLuaGenerator.Generate(Catalog);
        Assert.Contains("defensiveMajor", lua);
        Assert.Contains("immunity", lua);
    }
}
