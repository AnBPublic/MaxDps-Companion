using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>Sentinel 4.7 — every known spec shares the same Main and survival policy.</summary>
public class UniversalCooldownIntelligenceTests
{
    private static readonly AbilityCatalog Catalog = AbilityCatalog.Default;

    private static CombatContext Context(
        int hp,
        string className = "WARRIOR",
        string specName = "Protection",
        DefensiveUrgency urgency = DefensiveUrgency.Yellow,
        bool valid = true,
        bool buffInactive = false) => new()
    {
        Class = className,
        Spec = specName,
        ContextValid = valid,
        HpValid = valid,
        HpSource = valid ? HpSource.Plain : HpSource.Unknown,
        HpPct = hp,
        HpPctUpper = hp,
        DefensiveUrgency = urgency,
        Cast = PlayerCastState.None,
        SlotRange = new TriState[PixelProtocol.SlotCount],
        SlotBuffActive = BuildBuffs(buffInactive),
        BuffProbeValid = buffInactive,
    };

    private static TriState[] BuildBuffs(bool inactive)
    {
        var buffs = new TriState[PixelProtocol.SlotCount];
        if (inactive) buffs[(int)Slot.Defensive] = TriState.No;
        return buffs;
    }

    private static PolicyDecision Evaluate(
        Slot slot, int spellId, CombatContext context,
        PolicyMemory? memory = null, PolicyOptions? options = null) =>
        PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = slot,
            SpellId = spellId,
            Context = context,
            Options = options ?? PolicyOptions.Standard,
            Memory = memory ?? new PolicyMemory(),
            NowMs = 10_000,
            InCombat = true,
            HasTarget = true,
        }, Catalog);

    [Fact]
    public void Every_Class_And_Spec_Keeps_MaxDps_Main_Authoritative()
    {
        var count = 0;
        foreach (var (className, specs) in AbilityCatalog.SpecOrder)
        foreach (var specName in specs.Skip(1))
        {
            // Uncatalogued Main must keep following MaxDps, regardless of
            // the class/spec, HP or pressure (including healer specs).
            var context = Context(40, className, specName);
            var memory = new PolicyMemory();
            memory.HealthPressure.Observe(Context(85, className, specName), true, 9000);
            memory.HealthPressure.Observe(context, true, 10_000);
            var result = Evaluate(Slot.Main, 0, context, memory);
            Assert.Equal(PolicyVerdict.Use, result.Verdict);
            Assert.Equal("MaxDpsRotation", "MaxDpsRotation"); // provenance invariant: Main is never rewritten
            count++;
        }
        Assert.Equal(40, count);
    }

    [Theory]
    [InlineData("DEATHKNIGHT", "Blood", true)]
    [InlineData("DEMONHUNTER", "Vengeance", true)]
    [InlineData("DRUID", "Guardian", true)]
    [InlineData("MONK", "Brewmaster", true)]
    [InlineData("PALADIN", "Protection", true)]
    [InlineData("WARRIOR", "Protection", true)]
    [InlineData("WARRIOR", "Arms", false)]
    [InlineData("PALADIN", "Retribution", false)]
    [InlineData("DEMONHUNTER", "Havoc", false)]
    [InlineData("EVOKER", "Devourer", false)]
    public void Tank_Maintenance_Roles_Are_Explicit(string cls, string spec, bool tank) =>
        Assert.Equal(tank, CooldownRoles.IsTank(cls, spec));

    [Fact]
    public void Rapid_Valid_Health_Loss_Promotes_Major_Defensive_For_Dps_And_Tank()
    {
        foreach (var spec in new[] { "Protection", "Arms" })
        {
            var current = Context(60, "WARRIOR", spec);
            var monitor = new PolicyMemory();
            var baseline = Evaluate(Slot.Defensive, 871, current);
            Assert.Equal(PolicyVerdict.Hold, baseline.Verdict);

            monitor.HealthPressure.Observe(Context(85, "WARRIOR", spec), true, 9000);
            monitor.HealthPressure.Observe(current, true, 10_000);
            Assert.True(monitor.HealthPressure.FallingFast);
            var pressured = Evaluate(Slot.Defensive, 871, current, monitor);
            Assert.Equal(PolicyVerdict.Use, pressured.Verdict);
        }
    }

    [Fact]
    public void Unknown_Or_Stale_Health_Never_Promotes_Defensive()
    {
        var memory = new PolicyMemory();
        memory.HealthPressure.Observe(Context(85), true, 9000);
        memory.HealthPressure.Observe(Context(60), true, 10_000);
        Assert.True(memory.HealthPressure.FallingFast);
        memory.HealthPressure.Observe(Context(60, valid: false), true, 10_050);
        Assert.False(memory.HealthPressure.FallingFast);
        Assert.Equal(PolicyVerdict.Hold,
            Evaluate(Slot.Defensive, 871, Context(60), memory).Verdict);
        memory.HealthPressure.Observe(Context(85), true, 11_000);
        memory.HealthPressure.Observe(Context(60), false, 11_900);
        Assert.False(memory.HealthPressure.FallingFast);
    }

    [Fact]
    public void Tank_Buff_Maintenance_Requires_Exact_Observed_Absent_Buff()
    {
        var eligible = Context(100, urgency: DefensiveUrgency.White, buffInactive: true);
        Assert.Equal(PolicyVerdict.Use,
            Evaluate(Slot.Defensive, 2565, eligible).Verdict);
        Assert.Equal(PolicyVerdict.Hold,
            Evaluate(Slot.Defensive, 2565,
                Context(100, "WARRIOR", "Arms", DefensiveUrgency.White, buffInactive: true)).Verdict);
        Assert.Equal(PolicyVerdict.Hold,
            Evaluate(Slot.Defensive, 2565,
                Context(100, urgency: DefensiveUrgency.White)).Verdict);
    }

    [Fact]
    public void SelfHeal_Slot_Absorb_Uses_Defensive_Instead_Of_Overheal_Gate()
    {
        var ability = Catalog.TryGet(190456)!;
        Assert.Same(CandidateProviders.Defensive, CandidateProviders.For(Slot.SelfHeal, ability));
        Assert.Equal("Defensive", Evaluate(Slot.SelfHeal, 190456,
            Context(55, urgency: DefensiveUrgency.Orange),
            options: new PolicyOptions { SoloEnabled = true }).Provider);
        Assert.Same(CandidateProviders.SelfSustain,
            CandidateProviders.For(Slot.SelfHeal, Catalog.TryGet(202168)!));
    }
}
