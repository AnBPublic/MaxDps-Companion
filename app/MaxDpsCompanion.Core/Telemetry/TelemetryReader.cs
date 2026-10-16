using System.Text.Json;

namespace MaxDpsCompanion;

/// <summary>
/// Streaming JSONL reader for exported telemetry. Blank lines are skipped;
/// unparseable lines are counted and never abort the replay — a truncated
/// ring export must still be diagnosable.
/// </summary>
internal static class TelemetryReader
{
    public static (List<TelemetryEvent> Events, int BadLines) Read(string path)
    {
        var events = new List<TelemetryEvent>();
        var badLines = 0;
        foreach (var raw in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            try
            {
                var evt = TelemetryJson.Deserialize(raw);
                if (evt is null || evt.Format != TelemetryFormat.Version || string.IsNullOrEmpty(evt.Kind)) badLines++;
                else events.Add(evt);
            }
            catch (JsonException)
            {
                badLines++;
            }
        }
        return (events, badLines);
    }
}
