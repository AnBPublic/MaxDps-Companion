using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 4.1 A2.5: a user press delivered through the companion is input like any
/// other, so it must spend the scheduler's minimum key interval
/// (<see cref="ActionScheduler.NoteExternalSend"/>) exactly like the
/// auto-target / auto-interact paths already did. Before 4.1 the automation
/// could fire immediately after a delivered user press.
/// </summary>
public class ActionSchedulerExternalSendTests
{
    private static readonly KeyStroke KeyE = new(0x45, false, false, false);
    private static readonly KeyStroke KeyR = new(0x52, false, false, false);

    private static BridgeFrame Frame()
    {
        var slots = new KeyStroke?[PixelProtocol.SlotCount];
        slots[(int)Slot.Main] = KeyE;
        slots[(int)Slot.Offensive] = KeyR;
        return new BridgeFrame
        {
            State = BridgeState.Active,
            Heartbeat = 1,
            Version = PixelProtocol.SupportedVersion,
            StatusFlags = PixelProtocol.StatusFlagInCombat | PixelProtocol.StatusFlagHasTarget,
            Slots = slots,
        };
    }

    private static ActionCandidate Candidate(Slot slot, KeyStroke stroke) =>
        new(slot, stroke, Enabled: true, Actionable: true, FirstSeenMs: 0, LastChangedMs: 0,
            LastPressedMs: 0, EverPressed: false);

    private static ScheduleInput Input(long now, BridgeFrame? frame, params ActionCandidate[] candidates) => new()
    {
        Frame = frame,
        OutOfCombatPermitted = true,
        Candidates = candidates,
        NowMs = now,
        MinKeyIntervalMs = 120,
        StaleAfterMs = 1500,
        HeartbeatTimeoutMs = 500,
        RepeatSuppressMs = 900,
    };

    [Fact]
    public void External_Send_Spends_The_Min_Key_Interval()
    {
        var scheduler = new ActionScheduler();
        scheduler.NoteExternalSend(1000);

        // Offensive: the generic min-interval branch (Main only re-holds the
        // identical stroke, the T2 main-immediate rule).
        var plan = scheduler.Advance(Input(1050, Frame(), Candidate(Slot.Offensive, KeyR)));

        Assert.Empty(plan.Actions);
        Assert.Equal(ScheduleReason.MinInterval, plan.Reason);
    }

    [Fact]
    public void External_Send_Does_Not_Block_After_The_Interval()
    {
        var scheduler = new ActionScheduler();
        scheduler.NoteExternalSend(1000);

        var plan = scheduler.Advance(Input(1120, Frame(), Candidate(Slot.Offensive, KeyR)));

        Assert.NotEmpty(plan.Actions);
    }

    [Fact]
    public void Without_An_External_Send_The_First_Tick_Is_Immediate()
    {
        // Baseline for the pin above: a fresh scheduler sends on tick 1.
        var plan = new ActionScheduler().Advance(Input(50, Frame(), Candidate(Slot.Offensive, KeyR)));

        Assert.NotEmpty(plan.Actions);
    }
}
