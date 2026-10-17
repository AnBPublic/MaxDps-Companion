namespace MaxDpsCompanion;

/// <summary>
/// One recognised hotbar press: the exact stroke the user pressed, the mapped
/// action-bar entry's identity, and the engine's classification. Delivery goes
/// out at the first allowed moment (off-GCD immediately; GCD-riding as the
/// observed GCD falls). The queue NEVER gates the rotation — the engine polls
/// it before the plan each tick, so a due press reaches the game first and the
/// rotation continues on the next tick.
/// </summary>
internal readonly record struct UserPress(
    KeyStroke Stroke, int SpellId, bool IsItem, bool Ready, bool RidesGcd, long DetectedMs);

internal enum UserPressActionKind { None, Deliver, Drop }

/// <summary>What the engine should do with the queue head this tick.</summary>
internal readonly record struct UserPressAction(UserPressActionKind Kind, KeyStroke Stroke, string? Reason)
{
    public static readonly UserPressAction None = new(UserPressActionKind.None, default, null);
    public static UserPressAction Deliver(KeyStroke stroke) => new(UserPressActionKind.Deliver, stroke, null);
    public static UserPressAction Drop(string reason) => new(UserPressActionKind.Drop, default, reason);
}

/// <summary>
/// Off-GCD classification for a hotbar entry (spec §2 "GCD class"): items are
/// off-GCD by construction; a known spell answers from the knowledge base
/// (<see cref="AbilityDefinition.RidesGcd"/>); an unknown classification
/// conservatively assumes YES — the press waits for the GCD window instead of
/// being fired blind during an active GCD.
/// </summary>
internal static class UserPressClassifier
{
    public static bool RidesGcd(Ext4Entry entry, AbilityCatalog? catalog)
    {
        if (entry.IsItem) return false;
        if (catalog?.TryGet(entry.Id) is { } ability) return ability.RidesGcd;
        return true;
    }
}

/// <summary>
/// v4.0.3 user-press priority queue: a pure, deterministic state machine (no
/// I/O; fake-clock tested). The engine feeds it presses recognised on watched
/// hotbar strokes and polls it once per tick with the decoded GCD flag and the
/// head key's physical state.
///
/// Semantics (spec docs/plans/2026-10-11-hotbar-press-queue.md):
/// <list type="bullet">
///   <item>a queued press is DELIVERED at its first allowed moment: a GCD-riding
///     stroke waits for the observed GCD to fall (it then casts immediately in
///     that window); an off-GCD stroke goes out immediately, even mid-GCD;</item>
///   <item>the queue NEVER suppresses the plan: the engine polls it before the
///     rotation each tick, so a due press reaches the game first and the
///     rotation continues on the next tick (4.0.3 hotfix — the earlier design
///     held the plan's GCD-riding sends while a press was pending, which could
///     starve the core rotation whenever a stale press lingered);</item>
///   <item>the stroke is only sent after the key is physically released (a
///     synthetic KEYUP while the user still holds the key reads as a real
///     release in WoW);</item>
///   <item>one confirmation window (~600 ms) plus one retry; a press that
///     cannot be confirmed expires with a reason;</item>
///   <item>a press that stays undelivered past the expiry window (one GCD +
///     queue margin) is dropped with "window expired".</item>
/// </list>
/// </summary>
internal sealed class UserPressQueue
{
    /// <summary>A rapid repeat of the same stroke inside this window is one press.</summary>
    public const long CoalesceMs = 250;

    /// <summary>A sent press expects the GCD to start inside this window before retry/drop.</summary>
    public const long ConfirmMs = 600;

    /// <summary>A press not delivered inside this window is dropped (one GCD + queue margin).</summary>
    public const long ExpiryMs = 1200;

    /// <summary>Send attempts per press (the initial send + 1 retry).</summary>
    public const int RetryLimit = 1;

    /// <summary>Bounded FIFO; when full, the newest press wins and the oldest is dropped.</summary>
    public const int MaxPending = 1;

    private sealed class Pending
    {
        public required UserPress Press { get; init; }
        public bool Sent { get; set; }
        public long SentMs { get; set; }
        public int Attempts { get; set; }
    }

    private readonly List<Pending> _items = new(MaxPending);

    public bool HasPending => _items.Count > 0;

    public int PendingCount => _items.Count;

    /// <summary>The queue head (diagnostics/telemetry/name lookup), or null.</summary>
    public UserPress? Head => _items.Count > 0 ? _items[0].Press : null;

    /// <summary>True when the head press has already been sent once, so a Deliver is a retry.</summary>
    public bool HeadIsRetry => _items.Count > 0 && _items[0].Attempts > 0;

    public void Reset() => _items.Clear();

    /// <summary>Records a recognised press; coalesces a same-stroke repeat inside the window.</summary>
    public void OnPress(UserPress press)
    {
        foreach (var item in _items)
        {
            if (item.Press.Stroke == press.Stroke && press.DetectedMs - item.Press.DetectedMs <= CoalesceMs)
                return;
        }
        if (_items.Count >= MaxPending) _items.RemoveAt(0);   // the newest press wins
        _items.Add(new Pending { Press = press });
    }

    /// <summary>The engine attempted the head stroke (initial send or retry).</summary>
    public void NoteSent(long nowMs)
    {
        if (_items.Count == 0) return;
        var head = _items[0];
        head.Sent = true;
        head.SentMs = nowMs;
        head.Attempts++;
    }

    /// <summary>Drops the head without sending (an engine-side gate refused it permanently).</summary>
    public void DropHead()
    {
        if (_items.Count > 0) _items.RemoveAt(0);
    }

    /// <summary>
    /// Advances the queue one tick. <paramref name="onGcd"/> is the decoded GCD
    /// flag; <paramref name="keyDown"/> is the head stroke's physical key state.
    /// </summary>
    public UserPressAction Poll(long nowMs, bool onGcd, bool keyDown)
    {
        if (_items.Count == 0) return UserPressAction.None;
        var head = _items[0];

        if (nowMs - head.Press.DetectedMs > ExpiryMs)
        {
            _items.RemoveAt(0);
            return UserPressAction.Drop("window expired");
        }

        if (!head.Sent)
        {
            // Wait for the GCD window (GCD-riding only) and for the physical
            // release. An off-GCD press is delivered immediately, even mid-GCD.
            if ((head.Press.RidesGcd && onGcd) || keyDown) return UserPressAction.None;
            return UserPressAction.Deliver(head.Press.Stroke);
        }

        // Sent: confirmed as soon as a GCD starts (the delivered cast owns it).
        if (onGcd)
        {
            _items.RemoveAt(0);
            return UserPressAction.None;
        }
        if (nowMs - head.SentMs >= ConfirmMs)
        {
            if (head.Attempts <= RetryLimit) return UserPressAction.Deliver(head.Press.Stroke);   // one retry
            _items.RemoveAt(0);
            return UserPressAction.Drop(head.Press.RidesGcd ? "no GCD observed" : "not confirmed");
        }
        return UserPressAction.None;
    }
}
