using Xunit;

namespace MaxDpsCompanion.Tests;

public class NorthstarClassSpecSafetyTests
{
    private static readonly AbilityCatalog Catalog = AbilityCatalog.Default;

    private static CombatContext Context(string cls,string spec) => new()
    {
        Class=cls,Spec=spec,ContextValid=true,HpValid=true,
        HpSource=HpSource.Plain,HpPct=30,HpPctUpper=30,
        DefensiveUrgency=DefensiveUrgency.Red,
        Cast=PlayerCastState.None,
        SlotRange=new TriState[PixelProtocol.SlotCount],
        SlotBuffActive=new TriState[PixelProtocol.SlotCount],
    };

    [Theory]
    [InlineData("WARRIOR","Arms",true)]
    [InlineData("WARRIOR","Fury",true)]
    [InlineData("MAGE","Frost",false)]
    [InlineData("PALADIN","Retribution",false)]
    public void Known_Warrior_Heal_Is_Guarded_By_Class(
        string cls,string spec,bool belongs)
    {
        // Class membership is verified against the catalog for each
        // situational extra; it is never inferred from the keyboard key.
        var heal=Catalog.TryGet(202168)!;
        Assert.Equal(belongs,AbilitySpecGuard.MatchesObserved(heal,Context(cls,spec)));
    }

    [Fact]
    public void Invalid_Class_Does_Not_Trigger_Companion_Sustain()
    {
        var d=PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot=Slot.SelfHeal,SpellId=202168,
            Context=Context("MAGE","Frost"),
            Options=new PolicyOptions {SoloEnabled=true},
            Memory=new PolicyMemory(),NowMs=10000,
            InCombat=true,HasTarget=true,
        },Catalog);
        Assert.Equal(PolicyVerdict.Skip,d.Verdict);
        Assert.Contains("class/spec",d.Reason);
    }

    [Fact]
    public void MaxDps_Main_Is_Not_Second_Guessed_By_Older_Class_Data()
    {
        var result=PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot=Slot.Main,SpellId=202168,
            Context=Context("MAGE","Frost"),
            Options=new PolicyOptions(),
            Memory=new PolicyMemory(),NowMs=10000,
            InCombat=true,HasTarget=true,
        },Catalog);
        // A MaxDps Main pick with an unfamiliar/shifted ID is not an
        // independently generated Companion cast; follow upstream.
        Assert.Equal(PolicyVerdict.Use,result.Verdict);
    }
}
