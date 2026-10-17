using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// P2-D WinUI port of the old Ui/ScaledIconCache.cs (S8 perf): keeps
/// device-scaled copies of the game skill icons so browser/explorer rows never
/// rescale the full-size JPEG on every bind. The source stays the engine's
/// <see cref="SpellIconCache"/> (net-5 wire: spell id -&gt; cached image); a new
/// icon evicts its scaled copies so a catalog refresh still surfaces.
///
/// WinUI difference: rows paint a <see cref="BitmapImage"/>, so each
/// (spell id, pixel size) is scaled with the same GDI+ HighQualityBicubic pass
/// as the old cache, encoded to PNG once and decoded into a BitmapImage.
/// <see cref="Get"/> must run on the UI thread (BitmapImage is not
/// free-threaded); the source lookup and scaling are thread-safe, and
/// <see cref="IconReady"/> is raised on the engine's background thread exactly
/// like <see cref="SpellIconCache.IconReady"/> — consumers marshal before
/// touching a row.
/// </summary>
internal sealed class ScaledIconCache
{
    private sealed class Entry
    {
        /// <summary>PNG bytes of the source icon (survives a TTL eviction of the engine bitmap).</summary>
        public byte[] Png = [];

        public readonly ConcurrentDictionary<int, BitmapImage> Sizes = new();
    }

    private readonly ConcurrentDictionary<int, Entry> _entries = new();

    // Null-source seam for tests; production uses the process-wide icon cache.
    private readonly Func<int, Image?> _source;

    public static ScaledIconCache Instance { get; } =
        new(spellId => SpellIconCache.Instance.TryGet(spellId), subscribe: true);

    /// <summary>Raised (background thread) after a new source icon becomes available.</summary>
    public event Action<int>? IconReady;

    public ScaledIconCache(Func<int, Image?> source, bool subscribe = false)
    {
        _source = source;
        if (subscribe)
        {
            SpellIconCache.Instance.IconReady += id =>
            {
                Invalidate(id);
                IconReady?.Invoke(id);
            };
        }
    }

    /// <summary>
    /// The scaled image for a spell at a square <paramref name="sizePx"/>, or
    /// null while the source icon is not cached yet. Never throws, never blocks
    /// the caller; the row keeps its drawn placeholder tile until
    /// <see cref="IconReady"/> fires and the row calls back in.
    /// </summary>
    public BitmapImage? Get(int spellId, int sizePx)
    {
        if (spellId <= 0 || sizePx <= 0) return null;
        var entry = GetEntry(spellId);
        if (entry is null) return null;
        if (entry.Sizes.TryGetValue(sizePx, out var cached)) return cached;

        try
        {
            using var source = Image.FromStream(new MemoryStream(entry.Png));
            using var scaled = new Bitmap(sizePx, sizePx);
            using (var graphics = Graphics.FromImage(scaled))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, sizePx, sizePx));
            }
            using var png = new MemoryStream();
            scaled.Save(png, ImageFormat.Png);

            var image = new BitmapImage();
            // Store before the async decode: concurrent re-binds share one image.
            if (!entry.Sizes.TryAdd(sizePx, image)) return entry.Sizes[sizePx];
            _ = LoadAsync(image, png.ToArray());
            return image;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Drops the scaled copies of one spell, or all of them when null.</summary>
    public void Invalidate(int? spellId)
    {
        if (spellId is null)
        {
            _entries.Clear();
            return;
        }
        _entries.TryRemove(spellId.Value, out _);
    }

    private Entry? GetEntry(int spellId)
    {
        if (_entries.TryGetValue(spellId, out var cached)) return cached;
        var source = _source(spellId);
        if (source is null) return null;
        try
        {
            using var png = new MemoryStream();
            source.Save(png, ImageFormat.Png);
            var entry = new Entry { Png = png.ToArray() };
            _entries.TryAdd(spellId, entry);
            return _entries[spellId];
        }
        catch
        {
            return null;
        }
    }

    private static async Task LoadAsync(BitmapImage image, byte[] bytes)
    {
        try
        {
            var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            await image.SetSourceAsync(stream);
        }
        catch
        {
            // Decode failure keeps the drawn placeholder; never throw into a row.
        }
    }
}
