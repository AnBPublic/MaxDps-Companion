namespace MaxDpsCompanion;

/// <summary>
/// One immutable reading of the in-game toggle mirror plus the combat flag.
/// Published as a single reference (4.1 A2.4): the UI thread can never observe
/// "valid" with a stale mask/epoch/blocked nibble, and the writer never needs
/// a lock.
/// </summary>
internal sealed record ToggleMirrorState(bool Valid, int Mask, int Epoch, int Blocked, bool InCombat)
{
    public static readonly ToggleMirrorState Empty = new(false, 0, 0, 0, false);
}

/// <summary>
/// 4.1 A2.4: the Ext3 toggle mirror published as ONE immutable reference.
/// Before 4.1 the engine wrote <c>_mirrorValid = true</c> BEFORE the mask /
/// epoch / blocked fields, so the UI thread's ToggleSync could read a valid
/// mirror with a stale mask for one tick.
/// </summary>
internal sealed class ToggleMirror
{
    private ToggleMirrorState _state = ToggleMirrorState.Empty;

    /// <summary>The current snapshot (a single reference read; safe from any thread).</summary>
    public ToggleMirrorState State => Volatile.Read(ref _state);

    public bool Valid => State.Valid;

    public int Mask => State.Mask;

    public bool InCombat => State.InCombat;

    /// <summary>
    /// Publishes a new snapshot. A missing Ext3 block clears validity but keeps
    /// the last mask/epoch/blocked nibble (the pre-4.1 behaviour consumers rely
    /// on when an old addon drops the block for one frame).
    /// </summary>
    public void Publish(Ext3Block? block, bool inCombat)
    {
        var current = State;
        var next = new ToggleMirrorState(
            Valid: block is not null,
            Mask: block?.Mask ?? current.Mask,
            Epoch: block?.Epoch ?? current.Epoch,
            Blocked: block?.Blocked ?? current.Blocked,
            InCombat: inCombat);
        Volatile.Write(ref _state, next);
    }

    /// <summary>Engine-shaped read: the Ext3 block when valid, plus the combat flag.</summary>
    public bool TryRead(out Ext3Block? ext3, out bool inCombat)
    {
        var state = State;
        inCombat = state.InCombat;
        ext3 = state.Valid ? new Ext3Block(state.Mask, state.Epoch, state.Blocked) : null;
        return state.Valid;
    }
}
