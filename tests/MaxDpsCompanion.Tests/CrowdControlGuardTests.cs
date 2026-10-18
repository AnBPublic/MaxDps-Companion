using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// v4.2.0 break-on-damage CC guard: a manual Blind/Gouge/Sap/Polymorph or a
/// companion CC application holds Main/Offensive/Interrupt for the expected
/// duration; a manual party-CC hold does the same for 8 s. The guard is a hold
/// only - it never generates a press.
/// </summary>
[Collection("GlobalStaticState")]
public class CrowdControlGuardTests
{
    private const long Now = 50_000;

    [Fact]
    public void BreakOnDamage_Cc_Starts_And_Expires_The_Attack_Guard()
    {
        var memory = new PolicyMemory();
        memory.NoteCcBreakApplied(2094, "Blind", Now, 8_000);
        Assert.True(memory.CcBreakGuardActive(Now));
        Assert.Equal(8_000, memory.CcBreakGuardRemainingMs(Now));
        Assert.Contains("Blind", memory.DamageHoldReason(Now));
        Assert.Contains("attacks held", memory.DamageHoldReason(Now));
        Assert.False(memory.CcBreakGuardActive(Now + 8_000));
        Assert.Null(memory.DamageHoldReason(Now + 8_000));
    }

    [Fact]
    public void Guard_Caps_Long_Pve_Cc_At_Ten_Seconds()
    {
        var memory = new PolicyMemory();
        memory.NoteCcBreakApplied(118, "Polymorph", Now, 60_000);
        Assert.Equal(PolicyMemory.DefaultCcHoldCapMs, memory.CcBreakGuardRemainingMs(Now));
        Assert.False(memory.CcBreakGuardActive(Now + PolicyMemory.DefaultCcHoldCapMs));
    }

    [Fact]
    public void Manual_Party_Cc_Hold_Toggles_And_Clears()
    {
        var memory = new PolicyMemory();
        memory.NoteManualCcHold(Now, 8_000);
        Assert.True(memory.ManualCcHoldActive(Now));
        Assert.Contains("manual CC hold", memory.DamageHoldReason(Now));
        memory.ClearManualCcHold();
        Assert.False(memory.ManualCcHoldActive(Now));
        Assert.Null(memory.DamageHoldReason(Now));
    }

    [Fact]
    public void Policy_Holds_Damage_Slots_But_Not_Defensive_Or_Items()
    {
        var memory = new PolicyMemory();
        memory.NoteCcBreakApplied(2094, "Blind", Now, 8_000);
        var context = new CombatContext
        {
            SlotRange = new TriState[PixelProtocol.SlotCount],
            SlotBuffActive = new TriState[PixelProtocol.SlotCount],
            ContextValid = true,
        };

        PolicyDecision Eval(Slot slot) => PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = slot,
            SpellId = 0,
            Context = context,
            Options = PolicyOptions.Standard,
            Memory = memory,
            NowMs = Now,
            InCombat = true,
            HasTarget = true,
        }, AbilityCatalog.Default);

        var main = Eval(Slot.Main);
        Assert.Equal(PolicyVerdict.Hold, main.Verdict);
        Assert.Contains("Blind", main.Reason);

        var offensive = Eval(Slot.Offensive);
        Assert.Equal(PolicyVerdict.Hold, offensive.Verdict);

        var interrupt = Eval(Slot.Interrupt);
        Assert.Equal(PolicyVerdict.Hold, interrupt.Verdict);

        var defensive = Eval(Slot.Defensive);
        Assert.Equal(PolicyVerdict.Use, defensive.Verdict);

        var consumable = Eval(Slot.Consumable);
        Assert.Equal(PolicyVerdict.Use, consumable.Verdict);
    }

    [Fact]
    public void Scheduler_Records_BreakOnDamage_Cc_And_The_StunIneffective_Latch()
    {
        var scheduler = new ActionScheduler();
        var stroke = new KeyStroke(0x51, false, false, false);

        var frame = new BridgeFrame
        {
            State = BridgeState.Active,
            Heartbeat = 1,
            Version = 5,
            StatusFlags = PixelProtocol.StatusFlagInCombat | PixelProtocol.StatusFlagHasTarget,
            Slots = new KeyStroke?[PixelProtocol.SlotCount],
            TargetCasting = TriState.Yes,
        };

        scheduler.Observe(frame, 1_000);
        scheduler.NoteSent(1_000, Slot.Interrupt, stroke, 2094); // Blind
        Assert.True(scheduler.Policy.CcBreakGuardActive(1_000));
        Assert.Contains("Blind", scheduler.Policy.DamageHoldReason(1_000));

        // A stun sent on a confirmed cast that leaves the target still casting
        // after ~800 ms is marked ineffective for this target.
        scheduler.NoteSent(1_100, Slot.Interrupt, stroke, 408); // Kidney Shot
        scheduler.Observe(frame, 1_900);
        Assert.True(CrowdControlVetoes.StunIneffective);

        // Target loss clears both the latch and the guard.
        var noTarget = new BridgeFrame
        {
            State = BridgeState.Active,
            Heartbeat = 2,
            Version = 5,
            StatusFlags = PixelProtocol.StatusFlagInCombat,
            Slots = new KeyStroke?[PixelProtocol.SlotCount],
            TargetCasting = TriState.Unknown,
        };
        scheduler.Observe(noTarget, 2_000);
        Assert.False(CrowdControlVetoes.StunIneffective);
        Assert.False(scheduler.Policy.CcBreakGuardActive(2_000));
    }
}
