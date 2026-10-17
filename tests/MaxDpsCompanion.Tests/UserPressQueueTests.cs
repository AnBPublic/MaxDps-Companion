using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// v4.0.3 user-press priority queue: the pure state machine that turns a
/// recognised hotbar press into a stroke delivered at the right moment
/// (off-GCD immediately; GCD-riding as the observed GCD falls), plus the
/// off-GCD classifier. Fake-clock; no I/O.
/// </summary>
public class UserPressQueueTests
{
    private static readonly KeyStroke KeyF = new(0x46, false, false, false);
    private static readonly KeyStroke KeyFive = new(0x35, false, false, false);
    private static readonly KeyStroke KeyQ = new(0x51, false, false, false);

    private static UserPress Press(KeyStroke stroke, int spellId = 46968, bool isItem = false,
        bool ready = true, bool ridesGcd = true, long detectedMs = 0) =>
        new(stroke, spellId, isItem, ready, ridesGcd, detectedMs);

    [Fact]
    public void Gcd_Riding_Press_Waits_For_The_Window_Then_Confirms()
    {
        var queue = new UserPressQueue();
        queue.OnPress(Press(KeyF, detectedMs: 0));
        Assert.True(queue.HasPending);

        Assert.Equal(UserPressActionKind.None, queue.Poll(100, onGcd: true, keyDown: false).Kind);
        Assert.Equal(UserPressActionKind.None, queue.Poll(200, onGcd: false, keyDown: true).Kind);   // still held

        var deliver = queue.Poll(250, onGcd: false, keyDown: false);
        Assert.Equal(UserPressActionKind.Deliver, deliver.Kind);
        Assert.Equal(KeyF, deliver.Stroke);
        queue.NoteSent(250);

        Assert.True(queue.HasPending);   // sent, awaiting confirmation
        Assert.Equal(UserPressActionKind.None, queue.Poll(300, onGcd: true, keyDown: false).Kind);
        Assert.False(queue.HasPending);  // GCD observed: confirmed and cleared
    }

    [Fact]
    public void Off_Gcd_Press_Delivers_Immediately_Even_Mid_Gcd()
    {
        var queue = new UserPressQueue();
        queue.OnPress(Press(KeyFive, isItem: true, ready: true, ridesGcd: false, detectedMs: 0));

        var deliver = queue.Poll(10, onGcd: true, keyDown: false);
        Assert.Equal(UserPressActionKind.Deliver, deliver.Kind);
        Assert.Equal(KeyFive, deliver.Stroke);
    }

    [Fact]
    public void NonReady_Gcd_Riding_Press_Waits_For_The_Window()
    {
        var queue = new UserPressQueue();
        queue.OnPress(Press(KeyF, ready: false, ridesGcd: true, detectedMs: 0));

        Assert.Equal(UserPressActionKind.None, queue.Poll(10, onGcd: true, keyDown: false).Kind);
        Assert.Equal(UserPressActionKind.Deliver, queue.Poll(20, onGcd: false, keyDown: false).Kind);
    }

    [Fact]
    public void Sent_Unconfirmed_Press_Stays_Pending()
    {
        var queue = new UserPressQueue();
        queue.OnPress(Press(KeyF, detectedMs: 0));
        queue.NoteSent(0);

        Assert.Equal(UserPressActionKind.None, queue.Poll(300, onGcd: false, keyDown: false).Kind);
        Assert.True(queue.HasPending);
    }

    [Fact]
    public void Retry_After_The_Confirm_Window_Then_Drop()
    {
        var queue = new UserPressQueue();
        queue.OnPress(Press(KeyF, detectedMs: 0));

        Assert.Equal(UserPressActionKind.Deliver, queue.Poll(0, onGcd: false, keyDown: false).Kind);
        queue.NoteSent(0);
        Assert.Equal(UserPressActionKind.None, queue.Poll(300, onGcd: false, keyDown: false).Kind);

        var retry = queue.Poll(600, onGcd: false, keyDown: false);
        Assert.Equal(UserPressActionKind.Deliver, retry.Kind);   // exactly one retry
        Assert.True(queue.HeadIsRetry);
        queue.NoteSent(600);

        var drop = queue.Poll(1200, onGcd: false, keyDown: false);
        Assert.Equal(UserPressActionKind.Drop, drop.Kind);
        Assert.Equal("no GCD observed", drop.Reason);
        Assert.False(queue.HasPending);
    }

    [Fact]
    public void Retry_Confirmed_By_Gcd_Clears_The_Press()
    {
        var queue = new UserPressQueue();
        queue.OnPress(Press(KeyF, detectedMs: 0));
        queue.NoteSent(0);

        Assert.Equal(UserPressActionKind.Deliver, queue.Poll(600, onGcd: false, keyDown: false).Kind);
        queue.NoteSent(600);
        Assert.Equal(UserPressActionKind.None, queue.Poll(700, onGcd: true, keyDown: false).Kind);
        Assert.False(queue.HasPending);
    }

    [Fact]
    public void Unsent_Press_Expires_With_A_Reason()
    {
        var queue = new UserPressQueue();
        queue.OnPress(Press(KeyF, detectedMs: 0));

        var drop = queue.Poll(UserPressQueue.ExpiryMs + 1, onGcd: true, keyDown: false);
        Assert.Equal(UserPressActionKind.Drop, drop.Kind);
        Assert.Equal("window expired", drop.Reason);
        Assert.False(queue.HasPending);
    }

    [Fact]
    public void Same_Stroke_Coalesces_Inside_The_Window()
    {
        var queue = new UserPressQueue();
        queue.OnPress(Press(KeyF, detectedMs: 0));
        queue.OnPress(Press(KeyF, detectedMs: 200));

        Assert.Equal(1, queue.PendingCount);

        queue.OnPress(Press(KeyF, detectedMs: 300));   // > 250 ms after the queued one: replaces the single slot
        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public void Queue_Holds_One_Press_And_The_Newest_Wins()
    {
        var queue = new UserPressQueue();
        queue.OnPress(Press(KeyF, detectedMs: 0));
        queue.OnPress(Press(KeyFive, detectedMs: 300));
        queue.OnPress(Press(KeyQ, detectedMs: 600));

        Assert.Equal(1, queue.PendingCount);
        Assert.Equal(KeyQ, queue.Head!.Value.Stroke);   // the newest press replaced the older ones
    }

    [Fact]
    public void Reset_Clears_Everything()
    {
        var queue = new UserPressQueue();
        queue.OnPress(Press(KeyF));
        queue.Reset();

        Assert.False(queue.HasPending);
        Assert.Null(queue.Head);
    }

    [Fact]
    public void Fresh_Press_Edge_Requires_A_New_Down()
    {
        // The 4.0.3 hotfix guard: the scan edge is down && !was ONLY. The
        // GetAsyncKeyState low bit ("pressed since the last call") is a shared
        // latch and must never fire an edge on its own — a stale latch once
        // kept a pending press alive and starved the core rotation.
        Assert.True(KeyStateProbe.IsFreshPress(unchecked((short)0x8000), wasDown: false));   // down, fresh
        Assert.False(KeyStateProbe.IsFreshPress(unchecked((short)0x8000), wasDown: true));   // held: a repeat is not a press
        Assert.True(KeyStateProbe.IsFreshPress(unchecked((short)0x8001), wasDown: false));   // down + latch
        Assert.False(KeyStateProbe.IsFreshPress(unchecked((short)0x8001), wasDown: true));
        Assert.False(KeyStateProbe.IsFreshPress(0x0001, wasDown: false));                    // latch alone: never an edge
        Assert.False(KeyStateProbe.IsFreshPress(0x0000, wasDown: false));
    }

    [Fact]
    public void Classifier_Items_Unknowns_And_Known_Spells()
    {
        var catalog = AbilityCatalog.Default;

        // Items are off-GCD by construction.
        Assert.False(UserPressClassifier.RidesGcd(new Ext4Entry(KeyFive, 5512, IsItem: true, Ready: true), catalog));

        // Unknown classification conservatively rides the GCD (and with no
        // catalog at all, every spell is unknown).
        Assert.True(UserPressClassifier.RidesGcd(new Ext4Entry(KeyFive, 9_999_999, IsItem: false, Ready: true), catalog));
        Assert.True(UserPressClassifier.RidesGcd(new Ext4Entry(KeyFive, 34428, IsItem: false, Ready: true), null));

        // Known spells answer from the knowledge base.
        Assert.False(UserPressClassifier.RidesGcd(new Ext4Entry(KeyFive, 498, IsItem: false, Ready: true), catalog));     // Divine Protection: OffGcd
        Assert.True(UserPressClassifier.RidesGcd(new Ext4Entry(KeyFive, 34428, IsItem: false, Ready: true), catalog));    // Victory Rush: OnGcd
        Assert.True(UserPressClassifier.RidesGcd(new Ext4Entry(KeyFive, 202168, IsItem: false, Ready: true), catalog));   // Impending Victory: OnGcd
    }

    [Fact]
    public void Replay_User_Sends_Are_Counted_And_Never_Feed_Policy_Memory()
    {
        // The v4.0.3 telemetry contract: a "user" send is recorded (diagnostics
        // and the send counter) but the replay must not feed it into the policy
        // memory the live engine never fed either — otherwise a recorded verdict
        // could false-mismatch after the user pressed a trinket/cooldown.
        var events = new TelemetryEvent[]
        {
            TelemetryEvent.Sent(0, "user", null, KeyF, 0, 498, "queued"),
            TelemetryEvent.Sent(120, "spell", Slot.Trinket, KeyFive, 120),
        };

        var result = ReplayRunner.Run(events);

        Assert.Equal(2, result.Sends);
        Assert.Equal(0, result.Mismatches);
        Assert.Equal(0, result.VerdictMismatches);
        Assert.Equal(0, result.Errors);
    }
}
