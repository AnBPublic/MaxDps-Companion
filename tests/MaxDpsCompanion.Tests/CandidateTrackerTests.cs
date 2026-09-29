using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Tracker bookkeeping: observation windows, press history and the snapshot
/// the decision layer consumes. Pure data — no game state involved.
/// </summary>
public class CandidateTrackerTests
{
    private static readonly KeyStroke StrokeR = new(0x52, false, false, false);
    private static readonly KeyStroke StrokeE = new(0x45, false, false, false);
    private static readonly KeyStroke StrokeW = new(0x57, false, false, false);

    private static BridgeFrame Frame(params (Slot Slot, KeyStroke? Stroke)[] slots)
    {
        var array = new KeyStroke?[PixelProtocol.SlotCount];
        foreach (var (slot, stroke) in slots) array[(int)slot] = stroke;
        return new BridgeFrame
        {
            State = BridgeState.Active,
            Heartbeat = 1,
            Version = PixelProtocol.SupportedVersion,
            StatusFlags = 0,
            Slots = array,
        };
    }

    private static readonly bool[] AllEnabled = [true, true, true, true, true, true];

    [Fact]
    public void Update_Starts_Observation_On_First_Sight()
    {
        var tracker = new CandidateTracker();
        tracker.Update(Frame((Slot.Main, StrokeR)), 1000);

        var candidate = Assert.Single(tracker.Snapshot(AllEnabled));
        Assert.Equal(Slot.Main, candidate.Slot);
        Assert.Equal(StrokeR, candidate.Stroke);
        Assert.Equal(1000, candidate.FirstSeenMs);
        Assert.Equal(1000, candidate.LastChangedMs);
        Assert.Equal(0, candidate.LastPressedMs);
        Assert.False(candidate.EverPressed);
        Assert.True(candidate.Enabled);
        Assert.True(candidate.Actionable);
    }

    [Fact]
    public void Update_Keeps_Timestamps_For_Unchanged_Stroke()
    {
        var tracker = new CandidateTracker();
        tracker.Update(Frame((Slot.Main, StrokeR)), 1000);
        tracker.Update(Frame((Slot.Main, StrokeR)), 2000);

        var candidate = Assert.Single(tracker.Snapshot(AllEnabled));
        Assert.Equal(1000, candidate.FirstSeenMs);
        Assert.Equal(1000, candidate.LastChangedMs);
        Assert.Equal(1000, candidate.AgeMs(2000));
    }

    [Fact]
    public void Update_Resets_Window_And_Press_Flag_On_Changed_Stroke()
    {
        var tracker = new CandidateTracker();
        tracker.Update(Frame((Slot.Main, StrokeR)), 1000);
        tracker.NotePressed(Slot.Main, 1200);
        tracker.Update(Frame((Slot.Main, StrokeR)), 1500);
        Assert.True(Assert.Single(tracker.Snapshot(AllEnabled)).EverPressed);

        tracker.Update(Frame((Slot.Main, StrokeE)), 2000);
        var changed = Assert.Single(tracker.Snapshot(AllEnabled));
        Assert.Equal(StrokeE, changed.Stroke);
        Assert.Equal(2000, changed.FirstSeenMs);
        Assert.Equal(2000, changed.LastChangedMs);
        Assert.False(changed.EverPressed);
        Assert.Equal(1200, changed.LastPressedMs);
    }

    [Fact]
    public void Update_Clears_Slot_When_Suggestion_Disappears()
    {
        var tracker = new CandidateTracker();
        tracker.Update(Frame((Slot.Main, StrokeR)), 1000);
        tracker.Update(Frame(), 2000);

        Assert.Empty(tracker.Snapshot(AllEnabled));
    }

    [Fact]
    public void Snapshot_Maps_Enabled_Flag_From_Settings()
    {
        var tracker = new CandidateTracker();
        tracker.Update(Frame((Slot.Main, StrokeR)), 1000);

        var disabled = Assert.Single(tracker.Snapshot([false, true, true, true, true, true]));
        Assert.False(disabled.Enabled);

        var enabled = Assert.Single(tracker.Snapshot(AllEnabled));
        Assert.True(enabled.Enabled);
    }

    [Fact]
    public void Snapshot_Marks_Movement_Key_Not_Actionable()
    {
        var tracker = new CandidateTracker();
        tracker.Update(Frame((Slot.Main, StrokeW)), 1000);

        var candidate = Assert.Single(tracker.Snapshot(AllEnabled));
        Assert.False(candidate.Actionable);
        Assert.True(candidate.Enabled); // actionability is metadata; the send path holds
    }

    [Fact]
    public void Snapshot_Reports_All_Present_Slots()
    {
        var tracker = new CandidateTracker();
        tracker.Update(Frame(
            (Slot.Main, StrokeR),
            (Slot.Defensive, StrokeE),
            (Slot.Trinket, StrokeW)), 1000);

        var candidates = tracker.Snapshot(AllEnabled);
        Assert.Equal(3, candidates.Length);
        Assert.Contains(candidates, c => c.Slot == Slot.Main);
        Assert.Contains(candidates, c => c.Slot == Slot.Defensive);
        Assert.Contains(candidates, c => c.Slot == Slot.Trinket);
    }

    [Fact]
    public void Reset_Clears_All_History()
    {
        var tracker = new CandidateTracker();
        tracker.Update(Frame((Slot.Main, StrokeR)), 1000);
        tracker.NotePressed(Slot.Main, 1100);
        tracker.Reset();

        Assert.Empty(tracker.Snapshot(AllEnabled));

        tracker.Update(Frame((Slot.Main, StrokeR)), 5000);
        var candidate = Assert.Single(tracker.Snapshot(AllEnabled));
        Assert.Equal(5000, candidate.FirstSeenMs);
        Assert.False(candidate.EverPressed);
    }
}
