using System.Globalization;

namespace MaxDpsCompanion;

/// <summary>
/// Optional local recorder: a fixed circular buffer of pre-serialized JSONL
/// lines. The buffer never grows — when full, the oldest event is evicted and
/// counted in <see cref="Dropped"/>. Nothing touches the disk until an
/// explicit <see cref="Export"/>, and nothing ever leaves the machine.
///
/// Threading: <see cref="Append"/> is safe from any thread (engine ticks plus
/// the UI's attach-session event) — the sequence stamp uses an interlocked
/// counter and the ring mutation is locked. <see cref="RecordLink"/> is
/// engine-thread-only; the UI thread may also call <see cref="Count"/>,
/// <see cref="Dropped"/>, <see cref="Export"/> and <see cref="Clear"/>. The
/// lock is uncontended in normal operation.
/// </summary>
internal sealed class TelemetryRecorder
{
    private readonly object _gate = new();
    private readonly byte[][] _lines;
    private int _head;
    private int _count;
    private long _dropped;
    private long _seq;
    private bool? _lastLinkVisible;

    public TelemetryRecorder(int capacity)
    {
        Capacity = Math.Clamp(capacity, 64, 1_000_000);
        _lines = new byte[Capacity][];
    }

    public int Capacity { get; }

    public int Count
    {
        get { lock (_gate) return _count; }
    }

    public long Dropped
    {
        get { lock (_gate) return _dropped; }
    }

    /// <summary>Serializes + appends one event; stamps the monotonic sequence and UTC time.</summary>
    public void Append(TelemetryEvent evt)
    {
        var stamped = evt with
        {
            Seq = Interlocked.Increment(ref _seq),
            Utc = evt.Utc ?? DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        };
        var line = TelemetryJson.Serialize(stamped);
        lock (_gate)
        {
            if (_count == Capacity)
            {
                _lines[_head] = line;
                _head = (_head + 1) % Capacity;
                _dropped++;
            }
            else
            {
                _lines[(_head + _count) % Capacity] = line;
                _count++;
            }
        }
    }

    /// <summary>Appends a link event only when visibility changed (engine thread).</summary>
    public void RecordLink(long tMs, bool visible, string? note, DecodeFault fault = DecodeFault.None)
    {
        if (_lastLinkVisible == visible) return;
        _lastLinkVisible = visible;
        Append(TelemetryEvent.LinkEvent(tMs, visible, note, fault));
    }

    /// <summary>Forgets the link transition state (engine Start).</summary>
    public void ResetLink() => _lastLinkVisible = null;

    /// <summary>
    /// Writes the buffered events in chronological order as UTF-8 JSONL,
    /// newline-terminated, and returns the event count. Does not clear the
    /// buffer and does not block appends: the ring is copied under the lock,
    /// the file is written outside it.
    /// </summary>
    public int Export(string path)
    {
        byte[][] snapshot;
        int count;
        lock (_gate)
        {
            count = _count;
            snapshot = new byte[count][];
            for (var i = 0; i < count; i++) snapshot[i] = _lines[(_head + i) % Capacity]!;
        }
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        foreach (var line in snapshot)
        {
            stream.Write(line, 0, line.Length);
            stream.WriteByte((byte)'\n');
        }
        return count;
    }

    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_lines);
            _head = 0;
            _count = 0;
            _dropped = 0;
        }
        _seq = 0;
    }
}
