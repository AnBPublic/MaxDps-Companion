namespace MaxDpsCompanion;

/// <summary>
/// One "what the companion suggested" vs "what was actually cast" observation
/// (Stream 3, read-only). Built purely from an exported telemetry file — the
/// recorder writes a <c>send</c> event for a tick just before that tick's
/// <c>tick</c>/<c>pol</c> record (see RotationEngine.Report), so a send is
/// matched to the plan head that justified it.
/// </summary>
internal readonly record struct CastAuditRow(
    long TMs,
    Slot Slot,
    int SpellId,
    string Reason,
    bool Cast,
    long? CastTMs,
    long LatencyMs)
{
    /// <summary>Human "suggested -> cast" status for the grid.</summary>
    public string Status => Cast ? $"cast +{LatencyMs}ms" : "not cast";
}

/// <summary>Aggregate of a suggested-vs-cast audit (read-only, no live claim).</summary>
internal sealed class CastAuditReport
{
    public required IReadOnlyList<CastAuditRow> Rows { get; init; }

    /// <summary>Policy plan heads that named a slot (the suggestions).</summary>
    public int Suggestions { get; init; }

    /// <summary>Suggestions that were actually sent.</summary>
    public int Casts { get; init; }

    /// <summary>Suggestions the per-slot gates held (movement/focus/GCD/link).</summary>
    public int Misses => Suggestions - Casts;

    /// <summary>Cast rate over the recorded suggestions (0 when none).</summary>
    public double HitRate => Suggestions == 0 ? 0 : (double)Casts / Suggestions;

    public static CastAuditReport Empty { get; } = new() { Rows = [] };
}

/// <summary>
/// Read-only suggested-vs-cast auditor over <see cref="TelemetryReader"/> output
/// (Stream 3 §3.4). Deterministic and side-effect free: the same events always
/// produce the same grid. A send is matched to a plan head when its timestamp
/// falls within <see cref="DefaultWindowMs"/> before the tick and the slot
/// matches (an unknown spell id on either side still matches by slot).
/// </summary>
internal static class CastAudit
{
    /// <summary>Default send-to-tick pairing window (same-tick sends).</summary>
    public const long DefaultWindowMs = 500;

    /// <summary>Upper bound on grid rows so a long recording cannot exhaust memory.</summary>
    public const int MaxRows = 500;

    public static CastAuditReport FromFile(string path, long windowMs = DefaultWindowMs)
    {
        var (events, _) = TelemetryReader.Read(path);
        return Build(events, windowMs);
    }

    public static CastAuditReport Build(IReadOnlyList<TelemetryEvent> events, long windowMs = DefaultWindowMs)
    {
        var rows = new List<CastAuditRow>();
        var pending = new List<(long TMs, Slot Slot, int SpellId)>();
        int suggestions = 0, casts = 0;

        foreach (var evt in events)
        {
            switch (evt.Kind)
            {
                case TelemetryKind.Send when evt.Send is { Slot: { } sendSlot } send:
                    pending.Add((evt.TMs, sendSlot, send.SpellId));
                    break;

                case TelemetryKind.Tick when evt.Policy?.Selected is { } selected:
                    suggestions++;
                    var verdict = FirstVerdict(evt.Policy, selected);
                    var spellId = verdict?.SpellId ?? 0;
                    var reason = verdict?.Reason ?? evt.Policy.Reason;

                    var matchIndex = -1;
                    for (var i = pending.Count - 1; i >= 0; i--)
                    {
                        var candidate = pending[i];
                        if (evt.TMs - candidate.TMs > windowMs) break;   // list is time-ordered
                        if (candidate.Slot != selected) continue;
                        if (candidate.SpellId != 0 && spellId != 0 && candidate.SpellId != spellId) continue;
                        matchIndex = i;
                        break;
                    }

                    long? castTMs = matchIndex >= 0 ? pending[matchIndex].TMs : null;
                    if (castTMs is not null)
                    {
                        casts++;
                        pending.RemoveRange(0, matchIndex + 1);
                    }
                    else
                    {
                        pending.RemoveAll(p => evt.TMs - p.TMs > windowMs);
                    }

                    if (rows.Count < MaxRows)
                        rows.Add(new CastAuditRow(
                            evt.TMs, selected, spellId, reason,
                            castTMs is not null, castTMs, castTMs is { } t ? evt.TMs - t : 0));
                    break;
            }
        }

        return new CastAuditReport { Rows = rows, Suggestions = suggestions, Casts = casts };
    }

    private static TelemetryVerdict? FirstVerdict(TelemetryPolicy policy, Slot slot)
    {
        if (policy.Verdicts is not { Length: > 0 } verdicts) return null;
        foreach (var verdict in verdicts)
            if (verdict.Slot == slot) return verdict;
        return null;
    }
}
