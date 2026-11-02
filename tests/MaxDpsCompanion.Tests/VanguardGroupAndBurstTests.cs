using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Cross-context v5 contract: Full remains vendor-authoritative, Burst is
/// an EXPLICIT opt-in conserve policy, group mitigation remains eligible.
/// </summary>
public class VanguardGroupAndBurstTests
{
    private static readonly AbilityCatalog Catalog = AbilityCatalog.Default;

    private static CombatContext C(int hp, DefensiveUrgency urgency,
        bool validTtk = false, double ttk = 0,
        string spec = "Arms", Ext5Block? ext5 = null) => new()
    {
        Class = "WARRIOR", Spec = spec, ContextValid = true,
        HpValid = true, HpSource = HpSource.Plain, HpPct = hp, HpPctUpper = hp,
        DefensiveUrgency = urgency, TtkValid = validTtk, TtkSec = ttk,
        Ext5 = ext5, Cast = PlayerCastState.None,
        SlotRange = new TriState[PixelProtocol.SlotCount],
        SlotBuffActive = new TriState[PixelProtocol.SlotCount],
    };

    private static PolicyDecision E(Slot slot, int id, CombatContext ctx,
        PolicyMemory memory, bool solo = false, RotationPreset preset = RotationPreset.Full) =>
        PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = slot, SpellId = id, Context = ctx, Memory = memory,
            NowMs = 10_000, HasTarget = true, InCombat = true,
            Options = new PolicyOptions { SoloEnabled = solo, Preset = preset },
        }, Catalog);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Real_Rapid_HP_Decline_Releases_Existing_Major_DR_In_Group_And_Solo(bool solo)
    {
        var m = new PolicyMemory();
        m.HealthPressure.Observe(C(100, DefensiveUrgency.Yellow), true, 9_000);
        m.HealthPressure.Observe(C(60, DefensiveUrgency.Orange), true, 10_000);
        Assert.True(m.HealthPressure.CriticalSoon);
        Assert.Equal(PolicyVerdict.Use,
            E(Slot.Defensive, 871, C(60, DefensiveUrgency.Orange), m, solo).Verdict);
        Assert.Equal(PolicyVerdict.Use,
            E(Slot.Main, 0, C(60, DefensiveUrgency.Orange), m, solo).Verdict);
    }

    [Fact]
    public void FiveSecond_Target_Only_Holds_90Second_Major_When_Burst_Opted_In()
    {
        var context = C(100, DefensiveUrgency.White, true, 5);
        var standard = E(Slot.Offensive, 1719, context, new PolicyMemory());
        var conserved = E(Slot.Offensive, 1719, context, new PolicyMemory(),
            preset: RotationPreset.Burst);
        Assert.Equal(PolicyVerdict.Use, standard.Verdict);
        Assert.Equal(PolicyVerdict.Hold, conserved.Verdict);
        Assert.Contains("burst preset", conserved.Reason);
        Assert.Equal(PolicyVerdict.Use,
            E(Slot.Main, 0, context, new PolicyMemory(), preset: RotationPreset.Burst).Verdict);
    }

    [Fact]
    public void Long_Fight_And_Confirmed_Boss_Keep_Offensive_Eligible_In_Burst()
    {
        var longFight = C(100, DefensiveUrgency.White, true, 120);
        Assert.Equal(PolicyVerdict.Use,
            E(Slot.Offensive, 1719, longFight, new PolicyMemory(),
                preset: RotationPreset.Burst).Verdict);

        var boss = C(100, DefensiveUrgency.White, true, 5,
            ext5: new Ext5Block(6, 0, 0, 0, 0, default, 0, 0));
        Assert.Equal(PolicyVerdict.Use,
            E(Slot.Offensive, 1719, boss, new PolicyMemory(),
                preset: RotationPreset.Burst).Verdict);
    }

    [Fact]
    public void Unknown_Ttk_Does_Not_Invent_Target_Lifetime()
    {
        var context = C(100, DefensiveUrgency.White, false, 0);
        Assert.Equal(PolicyVerdict.Use,
            E(Slot.Offensive, 1719, context, new PolicyMemory()).Verdict);
        // Burst is allowed to hold until a reliable TTK exists.
        Assert.Equal(PolicyVerdict.Hold,
            E(Slot.Offensive, 1719, context, new PolicyMemory(),
                preset: RotationPreset.Burst).Verdict);
    }

    [Fact]
    public void White_Urgency_Does_Not_Become_An_Early_Defensive_Regardless_Of_Mode()
    {
        var memory = new PolicyMemory();
        memory.HealthPressure.Observe(C(95, DefensiveUrgency.White), true, 9_000);
        memory.HealthPressure.Observe(C(60, DefensiveUrgency.White), true, 10_000);
        Assert.True(memory.HealthPressure.CriticalSoon);
        Assert.Equal(PolicyVerdict.Hold,
            E(Slot.Defensive, 871, C(60, DefensiveUrgency.White), memory).Verdict);
    }
}
