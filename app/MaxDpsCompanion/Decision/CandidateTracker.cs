namespace MaxDpsCompanion;

/// <summary>
/// Per-slot observation history for the decision layer: what stroke each slot
/// currently carries, when it first appeared / last changed, and whether it
/// has been pressed since that change. Pure bookkeeping — it never reads game
/// memory and never sends input.
/// </summary>
internal sealed class CandidateTracker
{
    private readonly KeyStroke?[] _stroke = new KeyStroke?[PixelProtocol.SlotCount];
    private readonly int[] _spellId = new int[PixelProtocol.SlotCount];
    private readonly long[] _firstSeenMs = new long[PixelProtocol.SlotCount];
    private readonly long[] _lastChangedMs = new long[PixelProtocol.SlotCount];
    private readonly long[] _lastPressedMs = new long[PixelProtocol.SlotCount];
    private readonly bool[] _pressedSinceChange = new bool[PixelProtocol.SlotCount];

    /// <summary>
    /// Records one decoded frame. A slot whose stroke disappeared clears its
    /// history; a slot whose stroke OR spell id changed starts a new
    /// observation window (a same-key different-spell swap is a new
    /// suggestion, not a stuck one).
    /// </summary>
    public void Update(BridgeFrame frame, long nowMs)
    {
        for (var i = 0; i < PixelProtocol.SlotCount; i++)
        {
            var stroke = frame.Slots[i];
            if (stroke is null)
            {
                _stroke[i] = null;
                _spellId[i] = 0;
                _pressedSinceChange[i] = false;
                continue;
            }
            var spellId = frame.SpellId((Slot)i);
            if (_stroke[i] is { } previous && previous == stroke.Value && _spellId[i] == spellId) continue;
            _stroke[i] = stroke;
            _spellId[i] = spellId;
            _firstSeenMs[i] = nowMs;
            _lastChangedMs[i] = nowMs;
            _pressedSinceChange[i] = false;
        }
    }

    /// <summary>Marks the slot as acted on (called only after a successful send).</summary>
    public void NotePressed(Slot slot, long nowMs)
    {
        var i = (int)slot;
        _lastPressedMs[i] = nowMs;
        _pressedSinceChange[i] = true;
    }

    /// <summary>
    /// Snapshot for the decision layer. Only slots currently carrying a stroke
    /// produce a candidate; Actionable excludes movement-bound keys (the send
    /// path refuses those with a hold note).
    /// </summary>
    public ActionCandidate[] Snapshot(bool[] slotEnabled)
    {
        var candidates = new List<ActionCandidate>(PixelProtocol.SlotCount);
        for (var i = 0; i < PixelProtocol.SlotCount; i++)
        {
            if (_stroke[i] is not { } stroke) continue;
            var enabled = i < slotEnabled.Length && slotEnabled[i];
            candidates.Add(new ActionCandidate(
                (Slot)i, stroke, enabled,
                Actionable: !MovementGuard.IsMovementStroke(stroke),
                _firstSeenMs[i], _lastChangedMs[i], _lastPressedMs[i], _pressedSinceChange[i],
                _spellId[i]));
        }
        return candidates.ToArray();
    }

    /// <summary>Drops all history (engine Start; a fresh session must not inherit stale timestamps).</summary>
    public void Reset()
    {
        Array.Clear(_stroke);
        Array.Clear(_spellId);
        Array.Clear(_firstSeenMs);
        Array.Clear(_lastChangedMs);
        Array.Clear(_lastPressedMs);
        Array.Clear(_pressedSinceChange);
    }
}
