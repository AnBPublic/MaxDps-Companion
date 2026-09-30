using System.Collections.Concurrent;

namespace MaxDpsCompanion;

/// <summary>
/// S8 perf: keeps device-scaled copies of the game skill icons so the class
/// browser rows never rescale the full-size JPEG on every paint. The source
/// <see cref="SpellIconCache"/> stores the CDN image at its native size; a row
/// paints at ~44 logical px, and a HighQualityBicubic draw per row per frame is
/// the hot path while scrolling. Scaled bitmaps are keyed by
/// (spell id, pixel size); a fresh source icon evicts its scaled entries so a
/// catalog refresh still surfaces.
/// </summary>
internal sealed class ScaledIconCache
{
    private readonly ConcurrentDictionary<(int SpellId, int Size), Bitmap> _scaled = new();

    // Null-source seam for tests; production uses the process-wide icon cache.
    private readonly Func<int, Image?> _source;

    public static ScaledIconCache Instance { get; } =
        new(source: spellId => SpellIconCache.Instance.TryGet(spellId), subscribe: true);

    public ScaledIconCache(Func<int, Image?> source, bool subscribe = false)
    {
        _source = source;
        if (subscribe) SpellIconCache.Instance.IconReady += id => Invalidate(id);
    }

    /// <summary>
    /// The scaled bitmap for a spell at a square <paramref name="sizePx"/>, or
    /// null while the source icon is not cached yet. Never throws, never blocks;
    /// the caller keeps painting its placeholder tile.
    /// </summary>
    public Image? Get(int spellId, int sizePx)
    {
        if (spellId <= 0 || sizePx <= 0) return null;
        if (_scaled.TryGetValue((spellId, sizePx), out var cached)) return cached;

        var source = _source(spellId);
        if (source is null) return null;

        try
        {
            var scaled = new Bitmap(sizePx, sizePx);
            using (var graphics = Graphics.FromImage(scaled))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, sizePx, sizePx));
            }
            _scaled.TryAdd((spellId, sizePx), scaled);
            return _scaled.TryGetValue((spellId, sizePx), out var winner) ? winner : scaled;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Drops the scaled copies of one spell, or all of them when null.</summary>
    public void Invalidate(int? spellId)
    {
        foreach (var key in _scaled.Keys)
        {
            if (spellId is null || key.SpellId == spellId.Value) _scaled.TryRemove(key, out _);
        }
    }

    private void Invalidate(int spellId) => Invalidate((int?)spellId);
}
