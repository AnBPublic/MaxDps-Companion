using System.Collections.Concurrent;

namespace MaxDpsCompanion;

/// <summary>
/// S8 perf: off-thread precompute + cache for the Class Browser row tree.
///
/// The tree depends only on the selected (class, spec) and
/// <see cref="ClassSkillTree.Build"/> is pure over (catalog, spell book), so it
/// is run once per key on the thread pool while the app is idle and the result
/// is handed back through the S5 <see cref="ClassSkillsView.TreeBuilder"/> seam.
/// Opening the browser then reuses the cached rows instead of paying the build
/// on the UI thread; a duplicate warm is a no-op and every failure falls back to
/// the inline build so the screen can never go blank.
/// </summary>
internal sealed class ClassBrowserPrecompute
{
    private readonly AbilityCatalog _catalog;
    private readonly ClassSpellBook _book;
    private readonly ConcurrentDictionary<string, SpecSkillList> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _inflight = new(StringComparer.Ordinal);

    public ClassBrowserPrecompute(AbilityCatalog catalog, ClassSpellBook book)
    {
        _catalog = catalog;
        _book = book;
    }

    /// <summary>Stable cache key; null class/spec collapse to a single "-|-" slot.</summary>
    public static string Key(string? className, string? specName) =>
        string.Concat(className ?? "-", "|", specName ?? "-");

    /// <summary>
    /// The S5 seam target. Signature matches
    /// <see cref="ClassSkillsView.TreeBuilder"/> exactly; a cache miss builds the
    /// tree inline once and stores it, so the call shape never changes.
    /// </summary>
    public SpecSkillList Build(AbilityCatalog catalog, ClassSpellBook book, string className, string specName) =>
        GetOrBuild(catalog, book, className, specName);

    public SpecSkillList GetOrBuild(AbilityCatalog catalog, ClassSpellBook book, string className, string specName)
    {
        var key = Key(className, specName);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var built = ClassSkillTree.Build(catalog, book, className, specName);
        _cache.TryAdd(key, built);
        // Another thread may have won the race; both trees are equivalent, but
        // return the stored one so callers share a single instance.
        return _cache.TryGetValue(key, out var winner) ? winner : built;
    }

    /// <summary>True when the (class, spec) tree is already cached.</summary>
    public bool IsCached(string? className, string? specName) => _cache.ContainsKey(Key(className, specName));

    /// <summary>
    /// Prebuilds the (class, spec) tree on the thread pool. Best-effort: never
    /// throws, never blocks the caller, and a key already cached or already
    /// being warmed is skipped.
    /// </summary>
    public void Warm(string? className, string? specName)
    {
        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(specName)) return;
        var key = Key(className, specName);
        if (_cache.ContainsKey(key) || !_inflight.TryAdd(key, 1)) return;
        var cls = className!;
        var spec = specName!;
        _ = Task.Run(() =>
        {
            try { GetOrBuild(_catalog, _book, cls, spec); }
            catch { /* prebuild is best-effort; the inline build is the fallback */ }
            finally { _inflight.TryRemove(key, out _); }
        });
    }

    /// <summary>How many row trees are cached (diagnostics/tests).</summary>
    public int CachedCount => _cache.Count;

    /// <summary>Drops every cached tree (catalog / spell-book regeneration hook).</summary>
    public void Invalidate() => _cache.Clear();
}
