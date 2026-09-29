using System.Text.Json;
using System.Text.Json.Serialization;

namespace MaxDpsCompanion;

/// <summary>
/// Source-generated JSONL codec for <see cref="TelemetryEvent"/>. Short
/// property names keep a line ~350-500 bytes so a bounded ring stays small;
/// enum values serialize as names for human readability. Recording captures
/// the serialized bytes once, so export is a pure copy with no re-encoding.
/// </summary>
internal static class TelemetryJson
{
    public static byte[] Serialize(TelemetryEvent evt) =>
        JsonSerializer.SerializeToUtf8Bytes(evt, TelemetryJsonContext.Default.TelemetryEvent);

    public static TelemetryEvent? Deserialize(string line) =>
        JsonSerializer.Deserialize(line, TelemetryJsonContext.Default.TelemetryEvent);
}

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    WriteIndented = false)]
[JsonSerializable(typeof(TelemetryEvent))]
internal sealed partial class TelemetryJsonContext : JsonSerializerContext
{
}
