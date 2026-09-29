using System.Text;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Ring-buffer semantics: bounded memory (oldest evicted, never an OOM
/// recording), chronological export, export does not clear, and the sequence
/// stamp keeps correlation monotonic.
/// </summary>
public class TelemetryRingBufferTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"mdb-tel-ring-{Guid.NewGuid():N}.jsonl");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private static TelemetryEvent Tick(long tMs) => new() { Kind = TelemetryKind.Tick, TMs = tMs };

    private static List<TelemetryEvent> ReadLines(string path)
    {
        var events = new List<TelemetryEvent>();
        foreach (var raw in File.ReadLines(path))
            if (!string.IsNullOrWhiteSpace(raw)) events.Add(TelemetryJson.Deserialize(raw)!);
        return events;
    }

    [Fact]
    public void Append_Below_Capacity_Keeps_All_Events()
    {
        var recorder = new TelemetryRecorder(64);

        for (var i = 0; i < 10; i++) recorder.Append(Tick(i));

        Assert.Equal(10, recorder.Count);
        Assert.Equal(0, recorder.Dropped);
    }

    [Fact]
    public void Overflow_Evicts_Oldest_And_Counts_Dropped()
    {
        var recorder = new TelemetryRecorder(64);

        for (var i = 0; i < 70; i++) recorder.Append(Tick(i));
        recorder.Export(_path);
        var exported = ReadLines(_path);

        Assert.Equal(64, recorder.Count);
        Assert.Equal(6, recorder.Dropped);
        Assert.Equal(64, exported.Count);
        Assert.Equal(6, exported[0].TMs);   // oldest surviving event
        Assert.Equal(69, exported[^1].TMs); // newest event
    }

    [Fact]
    public void Export_Is_Chronological_And_Does_Not_Clear()
    {
        var recorder = new TelemetryRecorder(64);
        for (var i = 0; i < 5; i++) recorder.Append(Tick(i * 100));

        var first = recorder.Export(_path);
        var second = recorder.Export(_path + ".again");

        Assert.Equal(5, first);
        Assert.Equal(5, second);
        Assert.Equal(5, recorder.Count);
        Assert.Equal(File.ReadAllText(_path), File.ReadAllText(_path + ".again"));
        Assert.Equal(new long[] { 0, 100, 200, 300, 400 }, ReadLines(_path).Select(e => e.TMs));
        File.Delete(_path + ".again");
    }

    [Fact]
    public void Export_Writes_One_Utf8_Line_Per_Event()
    {
        var recorder = new TelemetryRecorder(64);
        recorder.Append(Tick(1));
        recorder.Append(Tick(2));

        recorder.Export(_path);

        var bytes = File.ReadAllBytes(_path);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Equal(2, text.TrimEnd('\n').Split('\n').Length);
        Assert.EndsWith("\n", text);
    }

    [Fact]
    public void Sequences_Are_Monotonic_And_Clear_Resets()
    {
        var recorder = new TelemetryRecorder(64);
        for (var i = 0; i < 3; i++) recorder.Append(Tick(i));
        recorder.Export(_path);

        var sequences = ReadLines(_path).Select(e => e.Seq).ToArray();
        Assert.Equal(new long[] { 1, 2, 3 }, sequences);

        recorder.Clear();
        Assert.Equal(0, recorder.Count);
        Assert.Equal(0, recorder.Dropped);
    }
}
