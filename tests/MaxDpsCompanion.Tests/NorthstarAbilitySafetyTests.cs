using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>v6: hard human controls across spell replacements and every
/// content type, never speculative companion-generated actions.</summary>
public class NorthstarAbilitySafetyTests
{
    private static AbilityCatalog Catalog => AbilityCatalog.Default;

    private static CombatContext Combat(string cls="WARRIOR", string spec="Arms") => new()
    {
        Class=cls, Spec=spec, ContextValid=true, HpValid=true,
        HpSource=HpSource.Plain, HpPct=19, HpPctUpper=19,
        DefensiveUrgency=DefensiveUrgency.Red,
        Cast=PlayerCastState.None,
        SlotRange=new TriState[PixelProtocol.SlotCount],
        SlotBuffActive=new TriState[PixelProtocol.SlotCount],
    };

    private static PolicyDecision Eval(Slot slot,int spellId,AbilityPolicy policy,
        bool solo=true,CombatContext? context=null) =>
        PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot=slot, SpellId=spellId,
            Context=context??Combat(),
            Options=new PolicyOptions {SoloEnabled=solo,Abilities=policy},
            Memory=new PolicyMemory(), NowMs=10000, InCombat=true,HasTarget=true,
        }, Catalog);

    [Theory]
    [InlineData((int)Slot.Main)]
    [InlineData((int)Slot.SelfHeal)]
    [InlineData((int)Slot.Defensive)]
    public void Explicit_Disable_Base_Blocks_Its_Learned_Talent_Replacement(int slot)
    {
        // The verified curated symmetric variant family is Victory Rush /
        // Impending Victory. No role, slot, emergency or Solo override.
        var off=AbilityPolicy.Default.With(202168,false,true);
        Assert.True(off.ExplicitlyBlocked(34428,Catalog));
        Assert.Equal(PolicyVerdict.Skip,Eval((Slot)slot,34428,off).Verdict);
        Assert.False(AbilityPolicy.Default.ExplicitlyBlocked(34428,Catalog));
    }

    [Fact]
    public void Explicit_Manual_Mode_Blocks_Alias_In_Group_And_Solo()
    {
        var p=AbilityPolicy.Default.WithMode(34428,UserAbilityMode.Manual);
        Assert.Equal(PolicyVerdict.Skip,Eval(Slot.Main,202168,p,true).Verdict);
        Assert.Equal(PolicyVerdict.Skip,Eval(Slot.Main,202168,p,false).Verdict);
    }

    [Fact]
    public void Explicit_On_Cannot_Override_A_Different_Alias_Off()
    {
        var p=AbilityPolicy.Default.WithMode(34428,UserAbilityMode.Always)
            .With(202168,false,true);
        Assert.True(p.ExplicitlyBlocked(34428,Catalog));
        Assert.Equal(PolicyVerdict.Skip,Eval(Slot.SelfHeal,34428,p).Verdict);
    }

    [Fact]
    public void Explicit_Off_Dominates_Same_Spell_Always_Mode()
    {
        var p=AbilityPolicy.Default.WithMode(871,UserAbilityMode.Always)
            .With(871,false,true);
        Assert.False(p.IsEnabled(Catalog.TryGet(871)!));
        Assert.Equal(PolicyVerdict.Skip,Eval(Slot.Defensive,871,p).Verdict);
    }

    [Fact]
    public void Unknown_Companion_Only_Skills_Are_Never_Guessed()
    {
        var p=AbilityPolicy.Default;
        Assert.NotEqual(PolicyVerdict.Use,Eval(Slot.SelfHeal,999999,p).Verdict);
        Assert.NotEqual(PolicyVerdict.Use,Eval(Slot.Mobility,999999,p).Verdict);
    }
}
