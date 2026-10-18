namespace MaxDpsCompanion;

// Wave B: Ext4Entry / Ext4Frame moved to MaxDpsCompanion.Core (PixelProtocol.cs)
// because the Ext4 decoder lives there now; this assembler consumes them
// through the Core project reference.

/// <summary>
/// Why a <see cref="HotbarMap"/> is (not) usable. Mirrors the engine status
/// string: <c>off</c> / <c>unavailable (old addon)</c> / <c>stale</c> /
/// <c>ok n keys</c>.
/// </summary>
internal enum HotbarMapStatus
{
    /// <summary>No valid frame has ever been fed (feature never started).</summary>
    Unavailable = 0,

    /// <summary>The latest frame decodes but has no Ext4 presence bit: the in-game addon is older than v4.0.3.</summary>
    OldAddon = 1,

    /// <summary>Ext4 is present but no cycle has completed within the freshness window (or none ever has).</summary>
    Stale = 2,

    /// <summary>A completed cycle is fresh: the entries are usable.</summary>
    Ok = 3,
}

/// <summary>
/// The assembled Ext4 hotbar map: a stroke can map to several entries (the same
/// key bound to several action-bar slots), so <see cref="Lookup"/> returns every
/// match. Immutable; the assembler swaps a fresh instance in atomically on LAST.
/// </summary>
internal sealed class HotbarMap
{
    private readonly Ext4Entry[] _entries;

    /// <summary>Before any frame is fed.</summary>
    public static readonly HotbarMap Unavailable =
        new(HotbarMapStatus.Unavailable, Array.Empty<Ext4Entry>());

    public HotbarMap(HotbarMapStatus status, IReadOnlyList<Ext4Entry> entries)
    {
        Status = status;
        _entries = entries is Ext4Entry[] array ? array : entries.ToArray();
    }

    public HotbarMapStatus Status { get; }

    public IReadOnlyList<Ext4Entry> Entries => _entries;

    public int Count => _entries.Length;

    /// <summary>Every entry bound to <paramref name="stroke"/> (usually 0 or 1, never null).</summary>
    public IReadOnlyList<Ext4Entry> Lookup(KeyStroke stroke)
    {
        List<Ext4Entry>? hits = null;
        foreach (var entry in _entries)
        {
            if (entry.Stroke != stroke) continue;
            (hits ??= new List<Ext4Entry>()).Add(entry);
        }
        return hits ?? (IReadOnlyList<Ext4Entry>)Array.Empty<Ext4Entry>();
    }

    public bool Contains(KeyStroke stroke) => _entries.Any(e => e.Stroke == stroke);
}

/// <summary>
/// Reassembles the Ext4 hotbar map from the per-capture Ext4 frames (pure logic,
/// no I/O).
///
/// A cycle is <c>seq 0..N</c> (N marked LAST). The addon holds each frame for at
/// least two paint ticks, so repeated reads of one gen+seq are idempotent: a seq
/// already consumed inside the current cycle is ignored, and a repeat of a
/// completed cycle's LAST frame is dropped by the gap rule below (it is seq&gt;0
/// once the cycle state has reset). A seq gap, a generation change or a torn
/// frame discards the partial cycle and waits for the next <c>seq 0</c>. Only
/// LAST swaps the map, and it swaps atomically — the previous map keeps serving
/// until the new cycle closes.
///
/// The map is dropped (Stale, empty) after <see cref="StaleAfterMs"/> with no
/// completed cycle, or immediately (OldAddon) when a frame arrives without the
/// Ext4 presence bit.
/// </summary>
internal sealed class Ext4Assembler
{
    /// <summary>How long a completed map stays fresh without another completed cycle.</summary>
    public const long StaleAfterMs = 5_000;

    private readonly List<Ext4Entry> _partial = new(4);
    private Ext4Entry[] _map = Array.Empty<Ext4Entry>();
    private int _gen = -1;          // -1 = expecting the next cycle's seq 0
    private int _nextSeq;
    private bool _hasMap;
    private long _freshnessMs = long.MinValue;
    private HotbarMap _current = HotbarMap.Unavailable;

    /// <summary>The map computed from the most recent <see cref="Feed"/>.</summary>
    public HotbarMap Map => _current;

    /// <summary>Feeds one captured frame; <paramref name="nowMs"/> is the same monotonic clock the caller uses.</summary>
    public void Feed(BridgeFrame frame, long nowMs)
    {
        if (!frame.Ext4Present)
        {
            // Old/addon-without-Ext4: drop the map and report the skew.
            DiscardPartial();
            _map = Array.Empty<Ext4Entry>();
            _hasMap = false;
            _current = new HotbarMap(HotbarMapStatus.OldAddon, _map);
            return;
        }

        // Start the freshness clock on the first Ext4-present frame so a normal
        // first cycle (~1 s worst case) is never misread as stale.
        if (_freshnessMs == long.MinValue) _freshnessMs = nowMs;

        if (frame.Ext4 is { } ext4) ProcessCycle(ext4, nowMs);
        else DiscardPartial();   // torn frame: keep the completed map, drop the partial cycle

        if (_hasMap && nowMs - _freshnessMs > StaleAfterMs)
        {
            _map = Array.Empty<Ext4Entry>();
            _hasMap = false;
        }

        _current = _hasMap
            ? new HotbarMap(HotbarMapStatus.Ok, _map)
            : new HotbarMap(HotbarMapStatus.Stale, Array.Empty<Ext4Entry>());
    }

    private void ProcessCycle(Ext4Frame ext4, long nowMs)
    {
        if (ext4.Gen != _gen)
        {
            // New generation (or the first frame after a cycle closed): the
            // partial cycle belongs to a dead generation and must not mix in.
            _partial.Clear();
            _gen = ext4.Gen;
            _nextSeq = 0;
        }

        if (ext4.Seq < _nextSeq) return;         // repeat read of a consumed frame: idempotent
        if (ext4.Seq > _nextSeq)                 // gap: discard the partial cycle, wait for seq 0
        {
            DiscardPartial();
            return;
        }

        for (var i = 0; i < ext4.Entries.Count; i++) _partial.Add(ext4.Entries[i]);
        _nextSeq = ext4.Seq + 1;

        if (!ext4.Last) return;

        // Atomic swap: the previous map served right up to this point.
        _map = _partial.ToArray();
        _hasMap = true;
        _freshnessMs = nowMs;
        _partial.Clear();
        _gen = -1;
        _nextSeq = 0;
    }

    private void DiscardPartial()
    {
        _partial.Clear();
        _gen = -1;
        _nextSeq = 0;
    }
}
