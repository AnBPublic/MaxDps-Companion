using System.Collections.Concurrent;
using System.Drawing;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MaxDpsCompanion;

/// <summary>
/// Game skill icons for the class-skill screen, fetched once from the public
/// WoW CDN and cached next to the exe (<c>assets/icons/{spellId}.jpg</c>).
///
/// The companion never reads game textures, so the pipeline is:
///   1. spell id -&gt; icon slug via Wowhead's public tooltip endpoint
///      (https://nether.wowhead.com/tooltip/spell/{id}?locale=0),
///   2. icon slug -&gt; image via the zamimg icon CDN
///      (https://wow.zamimg.com/images/wow/icons/large/{slug}.jpg).
/// Both hosts are fixed; the slug is validated so a hostile response can never
/// escape the cache directory. Everything is best-effort and silent: offline,
/// a timeout, a bad response or a missing icon all degrade to the drawn
/// placeholder tile the row shows instead. No icon is ever required for the
/// screen to work, and nothing else in the app uses the network.
/// </summary>
internal sealed class SpellIconCache
{
    public static SpellIconCache Instance { get; } = new(slugProvider: id => ClassSpellBook.Default.TryGetIconSlug(id));

    private const string TooltipHost = "nether.wowhead.com";
    private static readonly Regex SlugPattern = new("^[a-z0-9_]{1,96}$", RegexOptions.Compiled);

    private readonly string _directory;
    private readonly HttpClient _http;
    private readonly Func<int, string?>? _slugProvider;
    private readonly Func<DateTime> _utcNow;
    private readonly ConcurrentDictionary<int, (Bitmap Bitmap, DateTime LoadedUtc)> _memory = new();
    private readonly ConcurrentDictionary<int, byte> _missing = new();
    private readonly ConcurrentDictionary<int, byte> _inflight = new();

    /// <summary>
    /// How long a memory-cached bitmap stays fresh (Stream 3). Past this age the
    /// entry is evicted and re-read from disk so a catalog regeneration / icon
    /// refresh can surface without an app restart.
    /// </summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Raised (background thread) after a new icon becomes available.</summary>
    public event Action<int>? IconReady;

    /// <summary>
    /// <paramref name="slugProvider"/> supplies the official client icon slug
    /// (from the class-spells verification) so the common case is ONE request
    /// to the Blizzard render CDN. Null falls back to Wowhead tooltip
    /// discovery, which some networks block. <paramref name="utcNow"/> is a test
    /// seam for the TTL clock; production uses UTC now.
    /// </summary>
    public SpellIconCache(string? directory = null, HttpMessageHandler? handler = null, Func<int, string?>? slugProvider = null, Func<DateTime>? utcNow = null)
    {
        _directory = directory ?? Path.Combine(Program.AppDir, "assets", "icons");
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(6);
        _slugProvider = slugProvider;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        try { Directory.CreateDirectory(_directory); } catch { /* cache is best-effort */ }
    }

    /// <summary>
    /// Drops every in-memory bitmap and negative entry so the next
    /// <see cref="TryGet"/> re-reads disk / re-requests (catalog regeneration
    /// hook). In-flight fetches are left to complete.
    /// </summary>
    public void Invalidate()
    {
        _memory.Clear();
        _missing.Clear();
    }

    /// <summary>
    /// The cached bitmap for a spell, or null when it is not on disk yet.
    /// A miss also queues the fetch; the caller subscribes to
    /// <see cref="IconReady"/> to repaint. Never blocks, never throws.
    /// </summary>
    public Image? TryGet(int spellId)
    {
        if (spellId <= 0) return null;
        if (_memory.TryGetValue(spellId, out var cached))
        {
            if (_utcNow() - cached.LoadedUtc < Ttl) return cached.Bitmap;
            _memory.TryRemove(spellId, out _);   // stale: fall through and re-read disk
        }
        if (_missing.ContainsKey(spellId)) return null;

        var path = Path.Combine(_directory, $"{spellId}.jpg");
        if (File.Exists(path))
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var raw = Image.FromStream(stream);
                var bitmap = new Bitmap(raw);
                _memory[spellId] = (bitmap, _utcNow());
                return bitmap;
            }
            catch
            {
                _missing[spellId] = 1;
                return null;
            }
        }

        Request(spellId);
        return null;
    }

    private void Request(int spellId)
    {
        if (!_inflight.TryAdd(spellId, 1)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                // 1. Official slug from the live-client verification: one
                //    direct render-CDN request, then the zamimg mirror.
                var slug = _slugProvider?.Invoke(spellId);
                if (!string.IsNullOrEmpty(slug) && SlugPattern.IsMatch(slug) && await DownloadIcon(spellId, slug))
                    return;
                // 2. Fallback discovery through the (often blocked) tooltip API.
                var discovered = await FetchSlug(spellId);
                if (discovered is null || !await DownloadIcon(spellId, discovered))
                {
                    _missing[spellId] = 1;
                    return;
                }
            }
            catch
            {
                _missing[spellId] = 1;   // offline / timeout / bad response: placeholder stays
            }
            finally
            {
                _inflight.TryRemove(spellId, out _);
            }
        });
    }

    private async Task<bool> DownloadIcon(int spellId, string slug)
    {
        foreach (var url in new[]
        {
            $"https://render.worldofwarcraft.com/us/icons/56/{slug}.jpg",
            $"https://wow.zamimg.com/images/wow/icons/large/{slug}.jpg",
        })
        {
            try
            {
                var bytes = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
                if (bytes.Length == 0) continue;
                Directory.CreateDirectory(_directory);
                await File.WriteAllBytesAsync(Path.Combine(_directory, $"{spellId}.jpg"), bytes).ConfigureAwait(false);
                IconReady?.Invoke(spellId);
                return true;
            }
            catch
            {
                // try the next mirror
            }
        }
        return false;
    }

    private async Task<string?> FetchSlug(int spellId)
    {
        using var response = await _http.GetAsync($"https://{TooltipHost}/tooltip/spell/{spellId}?locale=0")
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("icon", out var icon) || icon.ValueKind != JsonValueKind.String)
            return null;
        var slug = icon.GetString();
        // A hostile/odd response must never escape the cache directory.
        return slug is not null && SlugPattern.IsMatch(slug) ? slug : null;
    }
}
