using System.Collections.Concurrent;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>One pre-built row set: the filtered/sorted items plus their condition one-liners.</summary>
internal sealed record AbilityRows(
    IReadOnlyList<AbilityDefinition> Items,
    IReadOnlyDictionary<int, string> Conditions);

/// <summary>
/// P2-D port of the S8 off-thread row-build idea behind the old
/// Ui/ClassBrowserPrecompute.cs, retargeted at the two WinUI lists: the Class
/// Browser row set and the Explorer filtered row set are pure functions of
/// (catalog, selection/filters), so they are cached by key and warmed on the
/// thread pool while the app is idle. A cache miss still builds inline once so
/// the screens can never regress to blank, exactly like the old seam.
/// </summary>
internal sealed class AbilityRowsPrecompute
{
    /// <summary>Bound on cached row sets; a regeneration simply re-warms.</summary>
    private const int MaxEntries = 32;

    private readonly AbilityCatalog _catalog;
    private readonly ConcurrentDictionary<string, AbilityRows> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _inflight = new(StringComparer.Ordinal);

    public AbilityRowsPrecompute(AbilityCatalog catalog) => _catalog = catalog;

    public static string BrowserKey(string? className, string? specName, string mode, int hpFloor) =>
        string.Concat("b|", className ?? "-", "|", specName ?? "-", "|", mode, "|", hpFloor.ToString());

    public static string ExplorerKey(
        string query, string status, IReadOnlyCollection<string> categories, string? className, string? specName)
    {
        var cats = categories.Count == 0 ? "" : string.Join(",", categories.OrderBy(c => c, StringComparer.OrdinalIgnoreCase));
        return string.Concat("e|", query, "|", status, "|", cats, "|", className ?? "-", "|", specName ?? "-");
    }

    /// <summary>Class Browser rows: class/spec + mode + min-HP% filter, category sort (old Rebuild :446-495).</summary>
    public AbilityRows GetBrowserRows(string? className, string? specName, string mode, int hpFloor)
    {
        var key = BrowserKey(className, specName, mode, hpFloor);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var list = new List<AbilityDefinition>(96);
        foreach (var ability in _catalog.All)
        {
            if (!AbilityViewPresets.MatchesClassSpec(ability, className, specName)) continue;
            if (!MatchesMode(ability, mode, className, specName)) continue;
            if (hpFloor < 100 && ability.UseBelowHpPct is int gate && gate > hpFloor) continue;
            list.Add(ability);
        }
        list.Sort(static (a, b) =>
        {
            var byCategory = a.Category.CompareTo(b.Category);
            if (byCategory != 0) return byCategory;
            var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : a.SpellId.CompareTo(b.SpellId);
        });

        var rows = new AbilityRows(list, new Dictionary<int, string>());
        return Store(key, rows);
    }

    /// <summary>Explorer rows: status + preset class/spec + categories + query, name sort (old Rebuild :490-522).</summary>
    public AbilityRows GetExplorerRows(
        string query, string status, IReadOnlyCollection<string> categories, string? className, string? specName)
    {
        var key = ExplorerKey(query, status, categories, className, specName);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var list = new List<AbilityDefinition>(128);
        foreach (var ability in _catalog.All)
        {
            if (!MatchesStatus(ability, status)) continue;
            if (!AbilityViewPresets.MatchesClassSpec(ability, className, specName)) continue;
            if (categories.Count > 0 && !MatchesAnyCategory(ability, categories, className, specName)) continue;
            if (query.Length > 0 && !MatchesQuery(ability, query)) continue;
            list.Add(ability);
        }
        list.Sort(static (a, b) =>
        {
            var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : a.SpellId.CompareTo(b.SpellId);
        });

        // v3.4.0 §5: resolve the condition one-liner once per rebuild (the DR
        // line needs the curated CC registry for the scoped class/spec).
        var conditions = new Dictionary<int, string>(list.Count);
        foreach (var ability in list)
        {
            var cc = _catalog.CrowdControlFor(className, specName, ability.SpellId);
            var line = AbilityViewPresets.ConditionLine(ability, cc);
            if (line.Length > 0) conditions[ability.SpellId] = line;
        }

        return Store(key, new AbilityRows(list, conditions));
    }

    /// <summary>Prebuilds the browser rows on the thread pool; best-effort, never blocks, never throws.</summary>
    public void WarmBrowser(string? className, string? specName, string mode, int hpFloor) =>
        Warm(BrowserKey(className, specName, mode, hpFloor), () => GetBrowserRows(className, specName, mode, hpFloor));

    /// <summary>Prebuilds the explorer rows on the thread pool; best-effort, never blocks, never throws.</summary>
    public void WarmExplorer(
        string query, string status, IReadOnlyCollection<string> categories, string? className, string? specName)
    {
        var cats = categories.Count == 0 ? Array.Empty<string>() : categories.ToArray();
        Warm(
            ExplorerKey(query, status, cats, className, specName),
            () => GetExplorerRows(query, status, cats, className, specName));
    }

    /// <summary>How many row sets are cached (diagnostics/tests).</summary>
    public int CachedCount => _cache.Count;

    /// <summary>Drops every cached row set (catalog regeneration hook).</summary>
    public void Invalidate() => _cache.Clear();

    private AbilityRows Store(string key, AbilityRows rows)
    {
        _cache.TryAdd(key, rows);
        if (_cache.Count > MaxEntries)
        {
            // Simple bound: drop everything, the inline build is the fallback.
            _cache.Clear();
            _cache.TryAdd(key, rows);
        }
        // Another thread may have won the race; both sets are equivalent, but
        // return the stored one so callers share a single instance.
        return _cache.TryGetValue(key, out var winner) ? winner : rows;
    }

    private void Warm(string key, Func<AbilityRows> build)
    {
        if (_cache.ContainsKey(key) || !_inflight.TryAdd(key, 1)) return;
        _ = Task.Run(() =>
        {
            try { build(); }
            catch { /* prebuild is best-effort; the inline build is the fallback */ }
            finally { _inflight.TryRemove(key, out _); }
        });
    }

    private bool MatchesMode(AbilityDefinition ability, string mode, string? className, string? specName)
    {
        if (mode == "All") return true;
        if (mode == "Main") return ability.Category == AbilityCategory.Main;
        return AbilityViewPresets.MatchesCategory(ability, mode, _catalog, className, specName);
    }

    private static bool MatchesStatus(AbilityDefinition ability, string status) => status switch
    {
        "Automatic" => ability.Automatable,
        "Companion" => ability.Automation == AutomationContext.Autonomous && ability.HasCandidatePath,
        "MaxDps" => ability.Ownership == IntelligenceOwnership.MaxDps,
        "Manual" => ability.Automation == AutomationContext.Manual,
        "Incomplete" => !ability.HasIntelligence,
        "Unverified" => ability.Automatable && !ability.LiveVerified && ability.Ownership != IntelligenceOwnership.MaxDps,
        "Warnings" => HasWarning(ability),
        _ => true,
    };

    private static bool HasWarning(AbilityDefinition ability)
        => !ability.HasIntelligence
           || (ability.Ownership == IntelligenceOwnership.Unavailable && !ability.ManualByDesign)
           || (ability.MaxDpsOwned && !ability.HasDelegationReason)
           || (ability.Ownership == IntelligenceOwnership.Manual && !ability.HasManualReason);

    private bool MatchesAnyCategory(
        AbilityDefinition ability, IReadOnlyCollection<string> categories, string? className, string? specName)
    {
        foreach (var tag in categories)
            if (AbilityViewPresets.MatchesCategory(ability, tag, _catalog, className, specName))
                return true;
        return false;
    }

    private static bool MatchesQuery(AbilityDefinition ability, string query)
    {
        if (ability.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        if (ability.SpellId.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        if (ability.Category.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        if (ability.Ownership.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        if (ability.Status.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var cls in ability.Classes)
            if (cls.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var spec in ability.Specs)
            if (spec.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
