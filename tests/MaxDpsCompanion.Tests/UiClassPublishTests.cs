using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 2026-10-01 regression: the engine must publish the decoded class/spec for
/// the UI even when the frame can never reach a send path (fail-closed
/// out-of-combat hold). The v5 class/spec cells decode independently of the
/// sensor block, so a degraded-context frame still carries its class; before
/// this fix <c>Tick</c> returned at the OOC gate before any publish, so
/// <see cref="RotationEngine.TryGetLiveClass"/> read null and the badge showed
/// "AUTO DETECT". Purely additive: the send paths still assign their own real
/// context before any scheduler/policy decision.
/// </summary>
public class UiClassPublishTests
{
    private static readonly KeyStroke KeyE = new(0x45, false, false, false);

    private static BridgeFrame OocFrame(
        string? className,
        string? specName,
        bool contextValid = false,
        bool inCombat = false,
        bool hasTarget = true)
    {
        var flags = 0;
        if (contextValid) flags |= PixelProtocol.StatusFlagContextValid;
        if (inCombat) flags |= PixelProtocol.StatusFlagInCombat;
        if (hasTarget) flags |= PixelProtocol.StatusFlagHasTarget;
        return new BridgeFrame
        {
            State = BridgeState.Active,
            Heartbeat = 1,
            Version = PixelProtocol.SupportedVersion,
            StatusFlags = flags,
            Slots = [KeyE, null, null, null, null, null, null, null],
            ClassName = className,
            SpecName = specName,
        };
    }

    [Fact]
    public void Ooc_Degraded_Context_Still_Publishes_Class_And_Spec()
    {
        var frame = OocFrame("Warrior", "Arms");
        // Sanity: this frame's sensor block is degraded, so the projection the
        // send paths use would drop the class (the diagnosed bug).
        Assert.False(frame.ContextValid);
        Assert.Null(CombatContext.FromFrame(frame, hpCurve: true).Class);

        using var engine = new RotationEngine(new AppSettings());
        engine.PublishUiContext(frame);

        Assert.True(engine.TryGetLiveClass(out var cls));
        Assert.Equal("Warrior", cls);
        Assert.True(engine.TryGetLiveSpec(out var spec));
        Assert.Equal("Arms", spec);
    }

    [Fact]
    public void Ooc_Publish_Leaves_Send_Paths_Untouched_And_Still_Holding()
    {
        var frame = OocFrame("Warrior", "Arms");
        using var engine = new RotationEngine(new AppSettings());
        engine.PublishUiContext(frame);

        // The fail-closed OOC gate the engine runs AFTER the publish still
        // holds this frame (CombatOnly default true), so Tick returns before
        // any send path.
        Assert.False(CombatGate.OutOfCombatPermitted(
            frame.InCombat, combatOnly: true, mirrorOutOfCombatSet: false,
            frame.State, frame.HasTarget));

        // No send-path side effects were produced by the UI publish.
        Assert.Null(engine.LastAction);
        Assert.Null(engine.CurrentPlanHead);
    }

    [Fact]
    public void ContextValid_Frame_Publishes_Full_Projection()
    {
        var frame = OocFrame("Priest", "Shadow", contextValid: true);
        using var engine = new RotationEngine(new AppSettings());
        engine.PublishUiContext(frame);

        Assert.True(engine.TryGetLiveClass(out var cls));
        Assert.Equal("Priest", cls);
        Assert.True(engine.TryGetLiveSpec(out var spec));
        Assert.Equal("Shadow", spec);
    }

    [Fact]
    public void No_Class_Decoded_Leaves_The_Readout_Unset()
    {
        var frame = OocFrame(className: null, specName: null);
        using var engine = new RotationEngine(new AppSettings());
        engine.PublishUiContext(frame);

        Assert.False(engine.TryGetLiveClass(out _));
        Assert.False(engine.TryGetLiveSpec(out _));
    }
}
