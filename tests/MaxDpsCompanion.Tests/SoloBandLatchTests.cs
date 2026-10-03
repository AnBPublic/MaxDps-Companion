using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Stream 1 §1.2: the pure enter/exit latch for the Solo HP bands and its
/// wiring into the defensive ladder. The latch adds an exit gap (enter+5)
/// without owning state; a cold caller degrades to the plain threshold.
/// </summary>
public class SoloBandLatchTests
{
    private const long Now = 10_000;
    private static AbilityCatalog Catalog => AbilityCatalog.Default;

    [Fact]
    public void Cold_Call_Uses_Plain_Enter_Threshold()
    {
        Assert.True(SoloBandLatch.Latched(enterHp: 75, hp: 75, wasEngaged: false));
        Assert.True(SoloBandLatch.Latched(enterHp: 75, hp: 40, wasEngaged: false));
        Assert.False(SoloBandLatch.Latched(enterHp: 75, hp: 76, wasEngaged: false));
    }

    [Fact]
    public void Engaged_Stays_Latched_Through_The_Exit_Gap()
    {
        // Enter 75, exit 80: latched all the way to 79, released at 80.
        Assert.True(SoloBandLatch.Latched(enterHp: 75, hp: 76, wasEngaged: true));
        Assert.True(SoloBandLatch.Latched(enterHp: 75, hp: 79, wasEngaged: true));
        Assert.False(SoloBandLatch.Latched(enterHp: 75, hp: 80, wasEngaged: true));
        Assert.False(SoloBandLatch.Latched(enterHp: 75, hp: 90, wasEngaged: true));
    }

    [Fact]
    public void ExitFor_Defaults_To_A_Five_Point_Gap()
    {
        Assert.Equal(5, SoloBandLatch.DefaultExitGap);
        Assert.Equal(80, SoloBandLatch.ExitFor(75));
        Assert.Equal(75, SoloBandLatch.ExitFor(75, 0));
        Assert.Equal(75, SoloBandLatch.ExitFor(75, -3));
    }

    [Fact]
    public void Inverted_Pair_Is_Repaired_Not_Flapped()
    {
        // exit <= enter would be a degenerate latch; the pure type repairs it
        // locally to exit = enter + 1 so an engaged caller still has one point
        // of hysteresis (latched at 50, released at 51).
        Assert.True(SoloBandLatch.Latched(enterHp: 50, exitHp: 40, hp: 50, wasEngaged: true));
        Assert.False(SoloBandLatch.Latched(enterHp: 50, exitHp: 40, hp: 51, wasEngaged: true));
        Assert.True(SoloBandLatch.Latched(enterHp: 50, exitHp: 40, hp: 50, wasEngaged: false));
        Assert.False(SoloBandLatch.Latched(enterHp: 50, exitHp: 40, hp: 51, wasEngaged: false));
    }

    [Fact]
    public void Engaged_Ladder_Keeps_A_Major_Eligible_In_The_Exit_Gap()
    {
        // 871 Shield Wall (Major, Warrior/Arms). Band is 50; with a minor
        // mitigation already running the latch keeps the major eligible at 54
        // (exit 55) where a cold evaluation holds at the 50 edge.
        var held = Evaluate(871, hp: 54, engaged: false);
        Assert.Equal(PolicyVerdict.Hold, held.Verdict);
        Assert.Contains("major band", held.Reason);

        var used = Evaluate(871, hp: 54, engaged: true);
        Assert.Equal(PolicyVerdict.Use, used.Verdict);
        Assert.Contains("solo ladder", used.Reason);
    }

    [Fact]
    public void Engaged_Ladder_Still_Holds_Once_Past_The_Exit_Gap()
    {
        // exit = 55; 55 is the release point, so the major is held again.
        var held = Evaluate(871, hp: 55, engaged: true);
        Assert.Equal(PolicyVerdict.Hold, held.Verdict);
        Assert.Contains("major band", held.Reason);
    }

    private static PolicyDecision Evaluate(int spellId, int hp, bool engaged)
    {
        var memory = new PolicyMemory();
        if (engaged)
        {
            // A minor mitigation is running: the ladder is engaged.
            memory.NoteUse(Catalog.TryGet(1966)!, Now - 500);
        }
        var context = new CombatContext
        {
            HpValid = true,
            HpPct = hp,
            Cast = PlayerCastState.None,
            TargetCasting = TriState.No,
            TargetCastInterruptible = TriState.Unknown,
            TargetInMelee = TriState.Unknown,
            SlotRange = new TriState[PixelProtocol.SlotCount],
            SlotBuffActive = new TriState[PixelProtocol.SlotCount],
            DefensiveUrgency = DefensiveUrgency.White,
            StaggerUrgency = DefensiveUrgency.Unknown,
            DefensiveCatalogSource = true,
            ContextValid = true,
        };
        return PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = Slot.Defensive,
            SpellId = spellId,
            Context = context,
            Options = new PolicyOptions { SoloEnabled = true },
            Memory = memory,
            NowMs = Now,
            InCombat = true,
            HasTarget = true,
        }, Catalog);
    }
}
