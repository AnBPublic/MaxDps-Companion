using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 2026-09-30 fail-closed out-of-combat hold fix. Covers the shared
/// <see cref="CombatGate"/> predicate, the mirror-gated
/// <see cref="MovementGuard"/> auto-target/interact gates and the scheduler's
/// OOC hold, per docs/plans/2026-09-30-ooc-hold-fix.md.
/// </summary>
public class OocHoldTests
{
    private static readonly KeyStroke KeyE = new(0x45, false, false, false);

    private static BridgeFrame Frame(
        BridgeState state = BridgeState.Active,
        bool inCombat = false,
        bool hasTarget = true)
    {
        var flags = 0;
        if (inCombat) flags |= PixelProtocol.StatusFlagInCombat;
        if (hasTarget) flags |= PixelProtocol.StatusFlagHasTarget;
        return new BridgeFrame
        {
            State = state,
            Heartbeat = 1,
            Version = PixelProtocol.SupportedVersion,
            StatusFlags = flags,
            Slots = [KeyE, null, null, null, null, null, null, null],
        };
    }

    private static ActionCandidate MainCandidate() =>
        new(Slot.Main, KeyE, true, true, FirstSeenMs: 0, LastChangedMs: 0, LastPressedMs: 0, EverPressed: false);

    // ---- shared predicate -------------------------------------------------

    [Fact]
    public void Predicate_In_Combat_Always_Permitted()
    {
        Assert.True(CombatGate.OutOfCombatPermitted(
            inCombat: true, combatOnly: true, mirrorOutOfCombatSet: false,
            BridgeState.Active, hasTarget: false));
    }

    [Fact]
    public void Predicate_Ooc_CombatOnly_Holds_Even_With_Mirror()
    {
        Assert.False(CombatGate.OutOfCombatPermitted(
            inCombat: false, combatOnly: true, mirrorOutOfCombatSet: true,
            BridgeState.Active, hasTarget: true));
    }

    [Fact]
    public void Predicate_Ooc_CombatOnlyFalse_MirrorClear_Holds()
    {
        Assert.False(CombatGate.OutOfCombatPermitted(
            inCombat: false, combatOnly: false, mirrorOutOfCombatSet: false,
            BridgeState.Active, hasTarget: true));
    }

    [Fact]
    public void Predicate_Ooc_CombatOnlyFalse_MirrorSet_Active_Target_Permits()
    {
        Assert.True(CombatGate.OutOfCombatPermitted(
            inCombat: false, combatOnly: false, mirrorOutOfCombatSet: true,
            BridgeState.Active, hasTarget: true));
    }

    [Fact]
    public void Predicate_Ooc_Active_Without_Target_Holds()
    {
        Assert.False(CombatGate.OutOfCombatPermitted(
            inCombat: false, combatOnly: false, mirrorOutOfCombatSet: true,
            BridgeState.Active, hasTarget: false));
    }

    [Fact]
    public void Predicate_Ooc_NeedTarget_NeedInteract_Permit_Without_Active_Or_Target()
    {
        // 2026-09-30 review follow-up: the auto-target / auto-interact asks
        // must pass the coarse OOC gate (their per-toggle mirror bits are
        // checked in MovementGuard), otherwise the overlay's AutoTarget /
        // AutoInteract toggles are dead out of combat.
        Assert.True(CombatGate.OutOfCombatPermitted(
            inCombat: false, combatOnly: false, mirrorOutOfCombatSet: true,
            BridgeState.NeedTarget, hasTarget: false));
        Assert.True(CombatGate.OutOfCombatPermitted(
            inCombat: false, combatOnly: false, mirrorOutOfCombatSet: true,
            BridgeState.NeedInteract, hasTarget: false));
    }

    [Fact]
    public void Predicate_Ooc_NeedTarget_Still_Holds_Without_Permission()
    {
        Assert.False(CombatGate.OutOfCombatPermitted(
            inCombat: false, combatOnly: true, mirrorOutOfCombatSet: true,
            BridgeState.NeedTarget, hasTarget: false));
        Assert.False(CombatGate.OutOfCombatPermitted(
            inCombat: false, combatOnly: false, mirrorOutOfCombatSet: false,
            BridgeState.NeedTarget, hasTarget: false));
    }

    [Fact]
    public void MirrorOoc_Requires_Valid_And_Set()
    {
        var bit = 1 << ToggleSync.BitOutOfCombat;
        Assert.True(CombatGate.MirrorOutOfCombatSet(mirrorValid: true, bit));
        Assert.False(CombatGate.MirrorOutOfCombatSet(mirrorValid: false, bit));
        Assert.False(CombatGate.MirrorOutOfCombatSet(mirrorValid: true, 0));
    }

    // ---- movement guard (auto-target / auto-interact) ---------------------

    [Fact]
    public void AutoTarget_Ooc_CombatOnlyFalse_Requires_Mirror_Bit()
    {
        // CombatOnly=false alone is not permission out of combat.
        Assert.False(MovementGuard.ShouldAutoTarget(
            BridgeState.NeedTarget, autoTargetEnabled: true, combatOnly: false,
            mirrorOutOfCombatSet: false, mirrorAutoTargetSet: true,
            nowMs: 10_000, lastActiveMs: long.MinValue));
        Assert.True(MovementGuard.ShouldAutoTarget(
            BridgeState.NeedTarget, autoTargetEnabled: true, combatOnly: false,
            mirrorOutOfCombatSet: true, mirrorAutoTargetSet: true,
            nowMs: 10_000, lastActiveMs: long.MinValue));
    }

    [Fact]
    public void AutoTarget_Mirror_Off_Holds_Even_With_Mirror_Ooc_AddonWins()
    {
        // ADDON-WINS: the overlay can veto AutoTarget regardless of the
        // companion kill-switch being ON or the OOC bit being set.
        Assert.False(MovementGuard.ShouldAutoTarget(
            BridgeState.NeedTarget, autoTargetEnabled: true, combatOnly: false,
            mirrorOutOfCombatSet: true, mirrorAutoTargetSet: false,
            nowMs: 10_000, lastActiveMs: 0));
    }

    [Fact]
    public void AutoInteract_Ooc_CombatOnlyFalse_Requires_Mirror_Bit()
    {
        Assert.False(MovementGuard.ShouldAutoInteract(
            BridgeState.NeedInteract, interactEnabled: true, combatOnly: false,
            mirrorOutOfCombatSet: false, mirrorAutoInteractSet: true,
            nowMs: 10_000, lastActiveMs: long.MinValue));
        Assert.True(MovementGuard.ShouldAutoInteract(
            BridgeState.NeedInteract, interactEnabled: true, combatOnly: false,
            mirrorOutOfCombatSet: true, mirrorAutoInteractSet: true,
            nowMs: 10_000, lastActiveMs: long.MinValue));
    }

    [Fact]
    public void AutoInteract_Mirror_Off_Holds_Even_With_Mirror_Ooc_AddonWins()
    {
        Assert.False(MovementGuard.ShouldAutoInteract(
            BridgeState.NeedInteract, interactEnabled: true, combatOnly: false,
            mirrorOutOfCombatSet: true, mirrorAutoInteractSet: false,
            nowMs: 10_000, lastActiveMs: 0));
    }

    [Fact]
    public void AutoTarget_Ooc_Reachable_When_Overlay_Permits()
    {
        // Finding-1 regression: with CombatOnly=false and the mirror OOC +
        // AutoTarget bits set, an out-of-combat NeedTarget frame clears the
        // coarse engine gate AND the movement guard, so the TargetKey is
        // actually pressed (previously the Active&&hasTarget gate held first).
        const bool combatOnly = false;
        const bool mirrorOoc = true;
        var gate = CombatGate.OutOfCombatPermitted(
            inCombat: false, combatOnly, mirrorOutOfCombatSet: mirrorOoc,
            BridgeState.NeedTarget, hasTarget: false);
        var fire = MovementGuard.ShouldAutoTarget(
            BridgeState.NeedTarget, autoTargetEnabled: true, combatOnly,
            mirrorOutOfCombatSet: mirrorOoc, mirrorAutoTargetSet: true,
            nowMs: 10_000, lastActiveMs: long.MinValue);

        Assert.True(gate);
        Assert.True(fire);
    }

    [Fact]
    public void Mirror_AutoTarget_And_Interact_Helpers()
    {
        Assert.True(CombatGate.MirrorAutoTargetSet(true, 1 << ToggleSync.BitAutoTarget));
        Assert.False(CombatGate.MirrorAutoTargetSet(false, 1 << ToggleSync.BitAutoTarget));
        Assert.False(CombatGate.MirrorAutoTargetSet(true, 0));
        Assert.True(CombatGate.MirrorAutoInteractSet(true, 1 << ToggleSync.BitAutoInteract));
        Assert.False(CombatGate.MirrorAutoInteractSet(true, 0));
    }

    // ---- scheduler plan builder ------------------------------------------

    private static SchedulePlan Advance(BridgeFrame? frame, bool oocPermitted, long now = 1000) =>
        new ActionScheduler().Advance(new ScheduleInput
        {
            Frame = frame,
            Candidates = [MainCandidate()],
            NowMs = now,
            MinKeyIntervalMs = 120,
            StaleAfterMs = 1500,
            HeartbeatTimeoutMs = 500,
            RepeatSuppressMs = 900,
            OutOfCombatPermitted = oocPermitted,
        });

    [Fact]
    public void Scheduler_Ooc_Paused_Holds_No_Key()
    {
        var plan = Advance(
            Frame(state: BridgeState.Paused, inCombat: false, hasTarget: true),
            oocPermitted: CombatGate.OutOfCombatPermitted(
                inCombat: false, combatOnly: true, mirrorOutOfCombatSet: true,
                BridgeState.Paused, hasTarget: true));

        Assert.Empty(plan.Actions);
        Assert.Null(plan.Selected);
    }

    [Fact]
    public void Scheduler_Ooc_No_Frame_Holds()
    {
        var plan = Advance(frame: null, oocPermitted: false);

        Assert.Equal(ScheduleReason.StaleFrame, plan.Reason);
        Assert.Empty(plan.Actions);
    }

    [Fact]
    public void Scheduler_Ooc_CombatOnlyFalse_MirrorClear_Holds()
    {
        var plan = Advance(
            Frame(state: BridgeState.Active, inCombat: false, hasTarget: true),
            oocPermitted: CombatGate.OutOfCombatPermitted(
                inCombat: false, combatOnly: false, mirrorOutOfCombatSet: false,
                BridgeState.Active, hasTarget: true));

        Assert.Equal(ScheduleReason.OutOfCombat, plan.Reason);
        Assert.Empty(plan.Actions);
    }

    [Fact]
    public void Scheduler_Ooc_CombatOnlyFalse_Active_Target_MirrorSet_Fires()
    {
        var plan = Advance(
            Frame(state: BridgeState.Active, inCombat: false, hasTarget: true),
            oocPermitted: CombatGate.OutOfCombatPermitted(
                inCombat: false, combatOnly: false, mirrorOutOfCombatSet: true,
                BridgeState.Active, hasTarget: true));

        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Single(plan.Actions);
    }

    [Fact]
    public void Scheduler_Legacy_40Cell_No_Ext3_In_Combat_Still_Fires()
    {
        // Finding-3 regression: an old 40-cell addon (no Ext3 block, so
        // ToggleSync.EffectiveMask is fail-closed 0, and the Ext3-mirror
        // consumers see no addon truth) must NOT blank the rotation in combat.
        // The engine computes the OOC predicate with inCombat=true, which
        // always passes regardless of the mirror.
        var sync = new ToggleSync();
        Assert.Equal(0, sync.EffectiveMask);

        var plan = Advance(
            Frame(state: BridgeState.Active, inCombat: true, hasTarget: true),
            oocPermitted: CombatGate.OutOfCombatPermitted(
                inCombat: true, combatOnly: true, mirrorOutOfCombatSet: false,
                BridgeState.Active, hasTarget: true));

        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Single(plan.Actions);
    }

    [Fact]
    public void Scheduler_Ooc_NeedTarget_Gate_Permits_To_Reach_AutoTarget()
    {
        // The scheduler gate shares the predicate: an OOC NeedTarget frame with
        // the overlay bit set is not held as OutOfCombat. The scheduler itself
        // schedules nothing for NeedTarget (no target), so the plan holds
        // NoTarget — never OutOfCombat.
        var plan = Advance(
            Frame(state: BridgeState.NeedTarget, inCombat: false, hasTarget: false),
            oocPermitted: CombatGate.OutOfCombatPermitted(
                inCombat: false, combatOnly: false, mirrorOutOfCombatSet: true,
                BridgeState.NeedTarget, hasTarget: false));

        Assert.Equal(ScheduleReason.NoTarget, plan.Reason);
        Assert.Empty(plan.Actions);
    }
}
