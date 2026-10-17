using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 4.1 A2.11: TargetKey / InteractKey parse once per configured value. The
/// cache must keep a failed parse's error text (an unparseable InteractKey
/// still explains itself) and re-parse when the text changes.
/// </summary>
public class ParsedStrokeCacheTests
{
    [Fact]
    public void Caches_A_Successful_Parse_Until_The_Text_Changes()
    {
        var calls = 0;
        var cache = new ParsedStrokeCache(text =>
        {
            calls++;
            return (text == "F", new KeyStroke(0x46, false, false, false), text == "F" ? null : "unknown key");
        });

        Assert.True(cache.TryGet("F", out var stroke, out var error));
        Assert.Equal((byte)0x46, stroke.VirtualKey);
        Assert.Null(error);

        Assert.True(cache.TryGet("F", out _, out _));
        Assert.Equal(1, calls);

        Assert.False(cache.TryGet("X", out _, out var error2));
        Assert.Equal("unknown key", error2);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Parses_Real_Modifier_Chords()
    {
        var cache = new ParsedStrokeCache();

        Assert.True(cache.TryGet("Alt+MB5", out var stroke, out var error));
        Assert.Equal((byte)0x06, stroke.VirtualKey);
        Assert.True(stroke.Alt);
        Assert.Null(error);
    }

    [Fact]
    public void Keeps_The_Real_Parse_Error()
    {
        var cache = new ParsedStrokeCache();

        Assert.False(cache.TryGet("not-a-key", out _, out var error));
        Assert.NotNull(error);
        Assert.Contains("unknown key", error);
    }
}
