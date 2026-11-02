using Xunit;

namespace MaxDpsCompanion.Tests;

public class VanguardPressureTests
{
    private static CombatContext C(int pct, string cls = "WARRIOR", string spec = "Arms",
        HpSource source = HpSource.Plain, bool valid = true) => new()
    {
        Class = cls, Spec = spec, ContextValid = valid, HpValid = valid,
        HpSource = valid ? source : HpSource.Unknown,
        HpPct = pct, HpPctUpper = pct, DefensiveUrgency = DefensiveUrgency.Yellow,
        Cast = PlayerCastState.None,
        SlotRange = new TriState[PixelProtocol.SlotCount],
        SlotBuffActive = new TriState[PixelProtocol.SlotCount],
    };

    private static PolicyDecision Eval(Slot slot, int spellId, CombatContext ctx,
        PolicyMemory memory, AbilityPolicy? policy = null) =>
        PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = slot, SpellId = spellId, Context = ctx, Memory = memory,
            NowMs = 11_000, HasTarget = true, InCombat = true,
            Options = new PolicyOptions { SoloEnabled = true, Abilities = policy },
        }, AbilityCatalog.Default);

    [Fact]
    public void Measured_Health_Loss_Creates_OneSecond_Critical_Projection()
    {
        foreach (var spec in new[] { "Arms", "Protection" })
        {
            var memory = new PolicyMemory();
            memory.HealthPressure.Observe(C(88, spec: spec), true, 10_000);
            memory.HealthPressure.Observe(C(60, spec: spec), true, 11_000);
            Assert.True(memory.HealthPressure.CriticalSoon);
            Assert.Equal(32, memory.HealthPressure.ProjectedHpPct);
            Assert.Equal(28, memory.HealthPressure.ObservedDropPct);
            Assert.Equal(PolicyVerdict.Use,
                Eval(Slot.Defensive, 871, C(60, spec: spec), memory).Verdict);
            Assert.Equal(PolicyVerdict.Use,
                Eval(Slot.Main, 0, C(60, spec: spec), memory).Verdict);
        }
    }

    [Fact]
    public void All_40_Specs_Fail_Closed_On_Unknown_Curve_Slow_Or_OutOfCombat_HP()
    {
        foreach (var cls in AbilityCatalog.ClassOrder.Skip(1))
        foreach (var spec in AbilityCatalog.SpecOrder[cls].Skip(1))
        {
            var monitor = new HealthPressureMonitor();
            monitor.Observe(C(90, cls, spec), true, 1000);
            monitor.Observe(C(60, cls, spec, HpSource.Curve), true, 2000);
            Assert.False(monitor.CriticalSoon);
            monitor.Observe(C(90, cls, spec), true, 3000);
            monitor.Observe(C(60, cls, spec, valid: false), true, 4000);
            Assert.False(monitor.CriticalSoon);
            monitor.Observe(C(90, cls, spec), true, 5000);
            monitor.Observe(C(60, cls, spec), false, 6000);
            Assert.False(monitor.CriticalSoon);
            monitor.Observe(C(90, cls, spec), true, 7000);
            monitor.Observe(C(60, cls, spec), true, 9000);
            Assert.False(monitor.CriticalSoon);
        }
    }

    [Fact]
    public void Explicit_Off_Remains_Absolute_During_Imminent_Danger()
    {
        var memory = new PolicyMemory();
        memory.HealthPressure.Observe(C(90), true, 10_000);
        memory.HealthPressure.Observe(C(60), true, 11_000);
        Assert.True(memory.HealthPressure.CriticalSoon);
        var disabled = AbilityPolicy.Default.With(871, false, defaultEnabled: true);
        Assert.Equal(PolicyVerdict.Skip,
            Eval(Slot.Defensive, 871, C(60), memory, disabled).Verdict);
    }

    [Fact]
    public void History_Does_Not_Leak_Across_Reversal_Or_Long_Pause()
    {
        var m = new HealthPressureMonitor();
        m.Observe(C(90), true, 8000);
        m.Observe(C(60), true, 11_000);
        Assert.False(m.CriticalSoon);
        m.Observe(C(90), true, 12_000);
        m.Observe(C(60), true, 11_000);
        Assert.False(m.CriticalSoon);
    }
}
