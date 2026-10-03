namespace MaxDpsCompanion;

/// <summary>
/// One recorded enemy kill (v3.7 adaptive TTK). The companion never reads a
/// damage/DPS field off the wire, so the "burn rate" is the HP-fraction proxy
/// <c>(StartFrac - EndFrac) / LifeSec</c> built from the same coarse HP bands
/// the estimator already consumes. Times are the estimator's fake clock
/// (<c>AtMs</c> = departure timestamp), so the whole window is deterministic
/// and replayable.
/// </summary>
/// <param name="AtMs">Fake-clock departure time (ms).</param>
/// <param name="LifeSec">Observed lifetime of the target (s).</param>
/// <param name="StartFrac">HP fraction at first observation (0..1).</param>
/// <param name="EndFrac">HP fraction at departure (0..1).</param>
internal readonly record struct KillRecord(
    long AtMs,
    double LifeSec,
    double StartFrac,
    double EndFrac);

/// <summary>
/// Bounded, time-pruned rolling window of the last N kills (v3.7 adaptive TTK).
/// Pure and fake-clock: no real clock, no I/O. <see cref="RateQuantile"/> uses
/// the *nearest-rank* definition (rank = ceil(q/100 · n), clamped), which is
/// deliberately pessimistic — the p75 burn rate is the rate the player managed
/// on all but the slowest quarter of the window, so a wrongly-high rate can
/// never make the companion fire a cooldown it should have held.
///
/// Backing list is small (cap default 8), so the O(n log n) sort on each query
/// is irrelevant.
/// </summary>
internal sealed class KillHistory
{
    private readonly int _cap;
    private readonly long _maxAgeMs;
    private readonly List<KillRecord> _records = [];

    public KillHistory(int cap, long maxAgeMs)
    {
        _cap = Math.Max(1, cap);
        _maxAgeMs = Math.Max(0, maxAgeMs);
    }

    /// <summary>Number of kills currently in the window (pruned, not yet evicted by cap).</summary>
    public int Count => _records.Count;

    /// <summary>
    /// Appends a kill, drops records older than <see cref="KillRecord.AtMs"/> by
    /// <c>maxAgeMs</c>, then evicts the oldest until the cap holds.
    /// </summary>
    public void Add(KillRecord record)
    {
        _records.Add(record);
        Prune(record.AtMs);
        if (_records.Count > _cap)
            _records.RemoveRange(0, _records.Count - _cap);
    }

    /// <summary>Drops every record older than <paramref name="nowMs"/> - <c>maxAgeMs</c>.</summary>
    public void Prune(long nowMs)
    {
        if (_maxAgeMs <= 0)
        {
            _records.Clear();
            return;
        }
        _records.RemoveAll(r => nowMs - r.AtMs > _maxAgeMs);
    }

    /// <summary>Empties the window (e.g. 90 s with no target and out of combat).</summary>
    public void Clear() => _records.Clear();

    /// <summary>
    /// The nearest-rank percentile of the per-kill burn rates, where
    /// <paramref name="q"/> is a percentage (75 = p75). Returns 0 when the
    /// window is empty or has no usable sample. A heal-clamped record (negative
    /// delta) contributes a 0 rate rather than a negative one.
    /// </summary>
    public double RateQuantile(double q)
    {
        if (_records.Count == 0) return 0;

        var rates = new List<double>(_records.Count);
        foreach (var record in _records)
        {
            if (record.LifeSec <= 0) continue;
            var delta = record.StartFrac - record.EndFrac;
            rates.Add(delta > 0 ? delta / record.LifeSec : 0);
        }
        if (rates.Count == 0) return 0;

        rates.Sort();
        var rank = (int)Math.Ceiling(Math.Clamp(q, 0, 100) / 100.0 * rates.Count);
        rank = Math.Clamp(rank, 1, rates.Count);
        return rates[rank - 1];
    }

    /// <summary>Median observed lifetime (s); 0 when the window is empty; mean of the two middles when even.</summary>
    public double MedianLifeSec()
    {
        if (_records.Count == 0) return 0;

        var lives = new List<double>(_records.Count);
        foreach (var record in _records) lives.Add(record.LifeSec);
        lives.Sort();
        var mid = lives.Count / 2;
        return lives.Count % 2 == 1
            ? lives[mid]
            : (lives[mid - 1] + lives[mid]) / 2.0;
    }
}
