namespace MaxDpsCompanion;

/// <summary>
/// Facts about one toggle/slot for the "why isn't this firing?" explainer.
/// Deliberately plain values: toggle state + scheduler verdict + frame
/// staleness, all read-only observations of the existing pipeline.
/// </summary>
internal readonly record struct WhyNotFiringFacts(
    string Label,
    bool ToggleOn,
    bool EngineRunning,
    bool Paused,
    string? SchedulerReason,
    string? PlanHeadAction,
    int FrameAgeMs,
    int StaleAfterMs);

/// <summary>
/// S8 diagnostics: turns the existing scheduler verdict + toggle state + the
/// age of the last decoded frame into a short, honest list of why a slot is not
/// firing. Pure formatting — no game read, no scheduler change, testable.
/// </summary>
internal static class WhyNotFiring
{
    public static IReadOnlyList<string> Explain(in WhyNotFiringFacts facts)
    {
        var lines = new List<string>(6);

        if (!facts.ToggleOn)
        {
            lines.Add("Toggle is OFF — this slot is prohibited from firing.");
            return lines;
        }
        lines.Add("Toggle is ON.");

        if (!facts.EngineRunning)
        {
            lines.Add("Engine is stopped — nothing is being scheduled.");
            return lines;
        }
        if (facts.Paused)
            lines.Add("Engine is paused.");

        var stale = facts.FrameAgeMs < 0 || (facts.StaleAfterMs > 0 && facts.FrameAgeMs > facts.StaleAfterMs);
        if (stale)
        {
            lines.Add(facts.FrameAgeMs < 0
                ? "No decoded frame yet — candidate freshness is unknown."
                : $"Candidate is stale: last frame {facts.FrameAgeMs} ms ago (> {facts.StaleAfterMs} ms).");
        }

        if (string.IsNullOrEmpty(facts.SchedulerReason))
        {
            lines.Add("Scheduler has no verdict for this tick yet.");
        }
        else
        {
            var head = string.IsNullOrEmpty(facts.PlanHeadAction) ? "" : $" (head: {facts.PlanHeadAction})";
            lines.Add($"Scheduler verdict: {facts.SchedulerReason}{head}.");
            if (!stale) lines.Add("Candidate is fresh.");
        }

        return lines;
    }

    public static string Summary(in WhyNotFiringFacts facts) => string.Join(" ", Explain(facts));
}
