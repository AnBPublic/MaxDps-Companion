using System.Drawing;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// The in-game addon can be an OLD protocol (v4 = 9 cells, v1 = 8 cells) long
/// after the companion was updated. The engine samples a 35-cell window, fails
/// the v5 decode, then re-decodes the trimmed legacy window — those frames must
/// still drive the main rotation through the current scheduler/policy. This is
/// the regression guard for the "updated exe + stale addon = nothing fires"
/// class of breakage.
/// </summary>
public class LegacyAddonCompatTests
{
    private static readonly bool[] AllEnabled = [true, true, true, true, true, true, true, true];

    /// <summary>The engine's real fallback chain over one 35-cell sample.</summary>
    private static BridgeFrame? DecodeLikeTheEngine(Color[] sample)
    {
        var frame = PixelProtocol.Decode(sample);
        if (frame is not null) return frame;
        frame = PixelProtocol.Decode(PixelProtocol.TrimToV4(sample));
        if (frame is not null) return frame;
        return PixelProtocol.Decode(PixelProtocol.TrimToV1(sample));
    }

    /// <summary>Pad a legacy strip to the engine's 35-cell sample window with desktop background.</summary>
    private static Color[] PadToSampleWindow(Color[] legacy)
    {
        var sample = new Color[PixelProtocol.CellCount];
        Array.Fill(sample, Color.FromArgb(32, 34, 38));   // arbitrary desktop pixels
        Array.Copy(legacy, sample, legacy.Length);
        return sample;
    }

    private static SchedulePlan Advance(ActionScheduler scheduler, BridgeFrame frame, CandidateTracker tracker, long now)
    {
        tracker.Update(frame, now);
        return scheduler.Advance(new ScheduleInput
        {
            Frame = frame,
            OutOfCombatPermitted = true,
            Candidates = tracker.Snapshot(AllEnabled),
            NowMs = now,
            MinKeyIntervalMs = 120,
            StaleAfterMs = 1500,
            HeartbeatTimeoutMs = 500,
            RepeatSuppressMs = 900,
            Context = CombatContext.FromFrame(frame),
            Options = PolicyOptions.Standard,
            Catalog = AbilityCatalog.Default,
        });
    }

    [Fact]
    public void V4_Addon_Sample_Drives_The_Main_Rotation()
    {
        // Exactly the engine's pipeline: a v1.3.9-era 9-cell strip inside the
        // 35-cell sample window, v5 decode fails, the v4 fallback wins, the
        // main slot suggestion reaches the scheduler and is selected.
        var sample = PadToSampleWindow(PixelProtocolCompatTests.BuildV4Frame());
        Assert.Null(PixelProtocol.Decode(sample));   // v5 path fails first

        var frame = DecodeLikeTheEngine(sample);
        Assert.NotNull(frame);
        Assert.Equal(PixelProtocol.SupportedVersionV4, frame!.Version);
        Assert.Equal(new KeyStroke(0x45, false, false, false), frame[Slot.Main]);
        Assert.False(frame.ContextValid);

        var plan = Advance(new ActionScheduler(), frame, new CandidateTracker(), now: 1000);
        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Equal(ScheduleReason.MainRotation, plan.Reason);
    }

    [Fact]
    public void V1_Addon_Sample_Still_Fires_Through_The_Scheduler()
    {
        // 8-cell strips remap slots (Interrupt 3->5) and carry no GCD flag or
        // target flag. v1's missing target flag is UNKNOWN, not "no target":
        // the scheduler fails open exactly like the legacy loop did, so an
        // ancient addon keeps driving the main rotation.
        var legacy = new Color[PixelProtocol.CellCountV1];
        var nibbles = new (int R, int G, int B)[PixelProtocol.CellCountV1];
        nibbles[0] = (15, 0, 15);
        nibbles[1] = (0x53 >> 4, 0x53 & 0x0F, 8);   // Main: VK 0x53 ('S')
        nibbles[PixelProtocol.StatusCellIndexV1] = (1, 3, 0);
        var sum = 0;
        for (var i = 1; i <= PixelProtocol.StatusCellIndexV1; i++) sum += nibbles[i].R + nibbles[i].G + nibbles[i].B;
        nibbles[PixelProtocol.VersionCellIndexV1] = (PixelProtocol.SupportedVersionV1, sum & 0xF, 3);
        for (var i = 0; i < legacy.Length; i++)
            legacy[i] = Color.FromArgb(nibbles[i].R * 17, nibbles[i].G * 17, nibbles[i].B * 17);

        var frame = DecodeLikeTheEngine(PadToSampleWindow(legacy));
        Assert.NotNull(frame);
        Assert.Equal(PixelProtocol.SupportedVersionV1, frame!.Version);
        Assert.False(frame.HasTarget);   // v1 has no target flag

        var plan = Advance(new ActionScheduler(), frame, new CandidateTracker(), now: 1000);
        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Equal(ScheduleReason.MainRotation, plan.Reason);
    }
}
