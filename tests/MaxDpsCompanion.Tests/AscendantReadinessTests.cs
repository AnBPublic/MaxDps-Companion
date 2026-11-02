using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>v5: explicit 12.1.5 runtime identity, honest older knowledge, and
/// deterministic support audits for every playable spec.</summary>
public class AscendantReadinessTests
{
    [Fact]
    public void Runtime_Target_Does_Not_Falsify_Pinned_Knowledge()
    {
        Assert.Equal("12.1.5", AbilityCatalog.TargetRuntimeGamePatch);
        Assert.Equal(120105, AbilityCatalog.TargetRuntimeInterfaceVersion);
        Assert.Equal("12.1", AbilityCatalog.ExpectedGamePatch);
        Assert.Equal(120100, AbilityCatalog.ExpectedInterfaceVersion);
        Assert.Equal("12.1", AbilityCatalog.Default.GamePatch);
    }

    [Fact]
    public void Every_Specialization_Is_Inventoried_With_Explicit_Role()
    {
        var rows = SpecReadiness.Build(AbilityCatalog.Default, ClassSpellBook.Default);
        Assert.Equal(40, rows.Count);
        Assert.Equal(33, rows.Count(r => r.Role is CombatRole.Tank or CombatRole.Damage));
        Assert.Equal(6, rows.Count(r => r.Role == CombatRole.Tank));
        Assert.Equal(7, rows.Count(r => r.Role == CombatRole.Healer));
        Assert.All(rows, row =>
        {
            Assert.True(row.HasInventory, $"Empty inventory for {row.ClassName}/{row.Spec}");
            Assert.False(row.LiveFullyVerified);
            Assert.InRange(row.Delegated, 0, row.Registered);
            Assert.InRange(row.CompanionEligible, 0, row.Registered);
        });
        Assert.Contains(rows, r => r.OlderPatchMetadata > 0);
    }

    [Theory]
    [InlineData("DEATHKNIGHT","Blood",true)]
    [InlineData("DEMONHUNTER","Vengeance",true)]
    [InlineData("DRUID","Guardian",true)]
    [InlineData("MONK","Brewmaster",true)]
    [InlineData("PALADIN","Protection",true)]
    [InlineData("WARRIOR","Protection",true)]
    [InlineData("EVOKER","Preservation",false)]
    [InlineData("PALADIN","Retribution",false)]
    [InlineData("DEMONHUNTER","Devourer",false)]
    public void Tank_Role_Is_Specific(string cls, string spec, bool tank) =>
        Assert.Equal(tank, SpecReadiness.RoleOf(cls,spec) == CombatRole.Tank);

    private static CombatContext Health(int hp, bool valid = true) => new()
    {
        ContextValid = valid, HpValid = valid,
        HpSource = valid ? HpSource.Plain : HpSource.Unknown,
        HpPct = hp, HpPctUpper = hp,
    };

    [Fact]
    public void Sustained_Loss_Requires_Duration_And_Trustworthy_Health()
    {
        var mon = new HealthPressureMonitor();
        mon.Observe(Health(95), true, 1000);
        mon.Observe(Health(89), true, 1600);
        Assert.False(mon.SustainedPressure);
        mon.Observe(Health(85), true, 2100);
        Assert.True(mon.SustainedPressure);
        Assert.False(mon.FallingFast);
        mon.Observe(Health(84,valid:false), true, 2200);
        Assert.False(mon.SustainedPressure);
    }

    [Fact]
    public void Sustained_Loss_Clears_When_Combat_Ends()
    {
        var mon = new HealthPressureMonitor();
        mon.Observe(Health(90), true, 1000);
        mon.Observe(Health(77), true, 2000);
        Assert.True(mon.SustainedPressure);
        mon.Observe(Health(77), false, 2100);
        Assert.False(mon.SustainedPressure);
    }

    [Theory]
    [InlineData(true, 4.0, true)]
    [InlineData(true, 18.0, false)]
    [InlineData(false, 4.0, false)]
    public void Major_GapFill_Is_Conserved_Only_With_Measured_Short_Lifetime(
        bool valid, double seconds, bool expected)
    {
        var ability = AbilityCatalog.Default.TryGet(1719)!;
        var context = new CombatContext { TtkValid=valid, TtkSec=seconds };
        Assert.Equal(expected, TtkPolicy.ConserveLongGapFill(ability, context, true));
        Assert.False(TtkPolicy.ConserveLongGapFill(ability, context, false));
    }

    [Fact]
    public void Sub90_Or_Unknown_Cooldowns_Are_Not_Conserved()
    {
        var ability = AbilityCatalog.Default.TryGet(1719)!;
        var context = new CombatContext { TtkValid=true, TtkSec=2 };
        Assert.False(TtkPolicy.ConserveLongGapFill(ability with {CooldownMs=45_000}, context, true));
        Assert.False(TtkPolicy.ConserveLongGapFill(ability with {CooldownMs=0}, context, true));
    }
}
