using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// T4 (spec <c>docs/plans/2026-10-03-no-downtime-main.md</c>): the MAIN slot
/// must never be stranded. These tests pin the scheduler half of the fix — a
/// repeated no-op Main press re-arms at <see cref="ActionScheduler.MainReprobeMs"/>
/// (400 ms) instead of the old 1.5 s / 3 s ladder, a changed Main identity
/// drops the superseded backoff and presses the next tick, and the companion
/// scheduler never invents a filler of its own (the per-spec filler lives in
/// <c>addon/MaxDpsBridge/MainFallback.lua</c>). The addon half is pinned by the
/// Q1 block in <c>tests/secret_harness.lua</c>.
///
/// The two spell ids are the verified Fury pair from
/// <c>addon/MaxDpsBridge/MainFallback.lua</c> (name-checked against the
/// read-only <c>vendor/</c> tree): Rampage 184367 (80 Rage, power-starved) and
/// Bloodthirst 23881 (rage generator, the fallback). They are never invented.
/// </summary>
public class MainNoDowntimeTests
{
    private const int RampageSpellId = 184367;    // Fury: 80 Rage -> power-starved
    private const int BloodthirstSpellId = 23881; // Fury: rage generator -> filler

    private static readonly KeyStroke KeyE = new(0x45, false, false, false); // Rampage
    private static readonly KeyStroke KeyF = new(0x46, false, false, false); // Bloodthirst

    private static BridgeFrame Frame((Slot Slot, KeyStroke? Stroke)[] slots, int heartbeat)
    {
        var array = new KeyStroke?[PixelProtocol.SlotCount];
        foreach (var (slot, stroke) in slots) array[(int)slot] = stroke;
        return new BridgeFrame
        {
            State = BridgeState.Active,
            Heartbeat = heartbeat,
            Version = PixelProtocol.SupportedVersion,
            // In combat, target present, GCD clear (a "no GCD" rage-blind tick).
            StatusFlags = PixelProtocol.StatusFlagInCombat | PixelProtocol.StatusFlagHasTarget,
            Slots = array,
        };
    }

    private static ActionCandidate Candidate(Slot slot, KeyStroke stroke, int spellId) =>
        new(slot, stroke, true, true, 0, 0, 0, false, spellId);

    private static ScheduleInput Input(long now, BridgeFrame frame, params ActionCandidate[] candidates) => new()
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
    public void Fury_Rampage_RageBlind_Main_Reprobes_Within_MainReprobeMs()
    {
        var scheduler = new ActionScheduler();
        var rampage = Candidate(Slot.Main, KeyE, RampageSpellId);

        // Three rapid presses of the SAME Main identity (Rampage with no Rage)
        // trip MainSameSpellNoOpCap. No GCD ever starts (no OnGcd frame), which
        // is exactly the rage-blind case that used to hold the slot empty.
        Assert.Equal(Slot.Main, scheduler.Advance(Input(0, Frame([(Slot.Main, KeyE)], 1), rampage)).Selected);
        scheduler.NoteSent(0, Slot.Main, KeyE, RampageSpellId);
        Assert.Equal(Slot.Main, scheduler.Advance(Input(120, Frame([(Slot.Main, KeyE)], 2), rampage)).Selected);
        scheduler.NoteSent(120, Slot.Main, KeyE, RampageSpellId);
        Assert.Equal(Slot.Main, scheduler.Advance(Input(240, Frame([(Slot.Main, KeyE)], 3), rampage)).Selected);
        scheduler.NoteSent(240, Slot.Main, KeyE, RampageSpellId);

        // Next tick: the repeat backs off, but the Main floor is the fast
        // MainReprobeMs (<= 400), never the old 1.5 s / 3 s ladder.
        var backoff = scheduler.Advance(Input(360, Frame([(Slot.Main, KeyE)], 4), rampage));
        Assert.Null(backoff.Selected);
        Assert.Equal(ScheduleReason.RetryBackoff, backoff.Reason);
        Assert.True(ActionScheduler.MainReprobeMs <= 400);
        Assert.Equal(360 + ActionScheduler.MainReprobeMs,
            scheduler.FailedUntilFor(Slot.Main, KeyE, RampageSpellId));

        // Silent until the floor expires, then re-probed: the pacing gate still
        // enforces MinKeyInterval and the hold is bounded by floor + interval.
        Assert.Null(scheduler.Advance(Input(759, Frame([(Slot.Main, KeyE)], 5), rampage)).Selected);
        var reprobe = scheduler.Advance(Input(760, Frame([(Slot.Main, KeyE)], 6), rampage));
        Assert.Equal(Slot.Main, reprobe.Selected);
        Assert.Equal(KeyE, reprobe.Actions[0].Stroke);
        Assert.Equal(RampageSpellId, reprobe.Actions[0].SpellId);
        Assert.True(760 - 240 >= 120, "re-probe must respect MinKeyInterval");
        Assert.True(760 - 360 <= ActionScheduler.MainReprobeMs + 120,
            "Main must never stay silent beyond MainReprobeMs + MinKeyInterval");
    }

    [Fact]
    public void Changed_Main_Pick_Clears_The_Superseded_Backoff_And_Presses_Next_Tick()
    {
        var scheduler = new ActionScheduler();
        var rampage = Candidate(Slot.Main, KeyE, RampageSpellId);
        var bloodthirst = Candidate(Slot.Main, KeyF, BloodthirstSpellId);

        // Rampage wins a tick, so the scheduler remembers its identity.
        Assert.Equal(Slot.Main,
            scheduler.Advance(Input(0, Frame([(Slot.Main, KeyE)], 1), rampage)).Selected);
        scheduler.NoteSent(0, Slot.Main, KeyE, RampageSpellId);

        // The press failed silently: Rampage is suppressed for the base window.
        scheduler.NoteFailure(Slot.Main, KeyE, 0, RampageSpellId);
        Assert.Equal(ActionScheduler.RejectedSuppressMs,
            scheduler.FailedUntilFor(Slot.Main, KeyE, RampageSpellId));

        // Same suggestion -> still held (the backoff is real).
        Assert.Null(scheduler.Advance(Input(100, Frame([(Slot.Main, KeyE)], 2), rampage)).Selected);

        // The bridge now offers a DIFFERENT Main (Bloodthirst): the identity
        // change drops Rampage's failure memory, and the new pick presses the
        // very next tick once MinKeyInterval has elapsed.
        var plan = scheduler.Advance(Input(120, Frame([(Slot.Main, KeyF)], 3), bloodthirst));
        Assert.Equal(Slot.Main, plan.Selected);
        Assert.Equal(KeyF, plan.Actions[0].Stroke);
        Assert.Equal(BloodthirstSpellId, plan.Actions[0].SpellId);
        Assert.Equal(0, scheduler.FailedUntilFor(Slot.Main, KeyE, RampageSpellId));
    }

    [Fact]
    public void Stationary_NoOp_Ticks_Never_Silence_Main_Beyond_Reprobe_Plus_Interval()
    {
        var scheduler = new ActionScheduler();
        var rampage = Candidate(Slot.Main, KeyE, RampageSpellId);
        var sends = new List<long>();

        // 20 stationary ticks at the production 50 ms cadence: the same
        // suggestion, the same target, never a GCD (a no-op key). Every plan
        // that selects Main is "sent" and the presses must keep coming.
        for (var i = 0; i < 20; i++)
        {
            var now = i * 50L;
            var plan = scheduler.Advance(Input(now, Frame([(Slot.Main, KeyE)], i + 1), rampage));
            if (plan.Selected != Slot.Main) continue;
            sends.Add(now);
            scheduler.NoteSent(now, Slot.Main, KeyE, RampageSpellId);
        }

        Assert.True(sends.Count >= 4, $"expected repeated Main presses, got {sends.Count}");
        var gaps = sends.Zip(sends.Skip(1), (a, b) => b - a).ToArray();
        Assert.All(gaps, gap => Assert.True(gap >= 120, $"gap {gap} < MinKeyInterval"));
        Assert.All(gaps, gap => Assert.True(
            gap <= ActionScheduler.MainReprobeMs + 120,
            $"gap {gap} > MainReprobeMs + MinKeyInterval"));
    }

    [Fact]
    public void Filler_Is_Never_Invented_When_No_Candidate_Is_Ranked()
    {
        // Empty ranked set + no bridge Main: the scheduler holds NoCandidate
        // and returns no action. The per-spec filler is addon-side only; the
        // companion must never synthesize a Main press of its own.
        var hold = new ActionScheduler().Advance(Input(0, Frame([], 1)));
        Assert.Equal(ScheduleReason.NoCandidate, hold.Reason);
        Assert.Null(hold.Selected);
        Assert.Empty(hold.Actions);

        // Even a bridge Main stroke with no candidate in the ranked list
        // produces no action: the candidate list is the only source.
        var strayHold = new ActionScheduler().Advance(Input(0, Frame([(Slot.Main, KeyE)], 1)));
        Assert.Equal(ScheduleReason.NoCandidate, strayHold.Reason);
        Assert.Empty(strayHold.Actions);
    }
}
