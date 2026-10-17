using Microsoft.UI.Xaml;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// P2-D Abilities wiring (docs/plans/2026-10-11-winui3-v4-migration.md §7-P2-D):
/// the Class Browser + Explorer tabs in the existing Abilities overlay host
/// (old MainForm.BuildAbilitiesOverlay :1519-1528 / ShowAbilities :1688-1709),
/// the drill-through preset seam (:1766-1784), the registry/verdict host the
/// browser consumes, and the S8 why-not-firing snapshot for the Diagnostics
/// card. Views live under Ui/Shell/Abilities; this file is composition + live
/// data only.
/// </summary>
public sealed partial class ShellWindow
{
    private AbilityRowsPrecompute? _rowsPrecompute;
    private ClassBrowserView? _classBrowser;
    private AbilityExplorer? _explorer;
    private readonly Dictionary<string, ClassOverlay> _classOverlays = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Lazy Abilities content: one tab per view, both fed from the same
    /// settings + engine witnesses. Default rows are warmed off-thread so the
    /// first open reuses the precompute (old ClassBrowserPrecompute idle warm).
    /// </summary>
    private void BuildAbilitiesContent()
    {
        _rowsPrecompute = new AbilityRowsPrecompute(AbilityCatalog.Default);
        _classBrowser = new ClassBrowserView(AbilityCatalog.Default, new ClassBrowserHost
        {
            IsEnabled = ability => _settings.Abilities.IsEnabled(ability),
            SetEnabled = (ability, on) =>
            {
                _settings.Abilities = _settings.Abilities.With(ability.SpellId, on, !ability.NeverAutomatic);
                SaveSettings();
            },
            RegistryEntry = ResolveRegistryEntry,
            LiveVerdict = LiveVerdictFor,
            ModeOf = spellId => _settings.Abilities.ModeOf(spellId),
            SetMode = (spellId, mode) =>
            {
                _settings.Abilities = _settings.Abilities.WithMode(spellId, mode);
                SaveSettings();
            },
            UrgencyFloorOf = spellId => _settings.AbilityOverrides.For(spellId)?.MinUrgency,
            SetUrgencyFloor = (spellId, value) =>
            {
                _settings.SetUrgencyOverride(spellId, value);
                _settings.SaveAbilityOverrides();
                SaveSettings();
            },
            ResetOverrides = spellId =>
            {
                _settings.ClearOverride(spellId);
                _settings.SaveAbilityOverrides();
                _settings.Abilities = _settings.Abilities.WithMode(spellId, UserAbilityMode.Default);
                SaveSettings();
            },
        }, _rowsPrecompute);

        _explorer = new AbilityExplorer(
            AbilityCatalog.Default,
            ability => _settings.Abilities.IsEnabled(ability),
            (ability, on) =>
            {
                _settings.Abilities = _settings.Abilities.With(ability.SpellId, on, !ability.NeverAutomatic);
                SaveSettings();
            },
            _rowsPrecompute);

        _abilitiesOverlay!.AddTab("Class browser", _classBrowser);
        _abilitiesOverlay.AddTab("Explorer", _explorer);

        // Warm the default selection + filter rows on the thread pool.
        var defaultClass = AbilityCatalog.ClassOrder.Length > 1 ? AbilityCatalog.ClassOrder[1] : null;
        _rowsPrecompute.WarmBrowser(defaultClass, null, "All", 100);
        _explorer.WarmCurrent();
    }

    /// <summary>
    /// MainForm.ShowAbilities :1701-1704: preselect the live class/spec and
    /// refresh the verdicts every open (warm reopen reuses the cached rows).
    /// </summary>
    private void OpenAbilitiesViews()
    {
        if (_classBrowser is null) return;
        _classBrowser.Open(
            _engine is not null && _engine.TryGetLiveClass(out var cls) ? cls : null,
            _engine is not null && _engine.TryGetLiveSpec(out var spec) ? spec : null);
        _classBrowser.RefreshLive();
    }

    /// <summary>
    /// MainForm.ShowAbilitiesWithPreset :1766-1784: the Intelligence tiles and
    /// hero bubbles open the Abilities overlay filtered to their set on both
    /// views (the Class Browser remains the selected tab).
    /// </summary>
    private void ApplyAbilitiesPreset(string tag)
    {
        _classBrowser?.ApplyPreset(tag);
        _classBrowser?.RefreshLive();
        _explorer?.ApplyPreset(tag);
    }

    // ----- Class Browser host (old MainForm :496-569) -----

    /// <summary>
    /// The resolved registry entry (registry + overlay + user override) for a
    /// spell. The loader applies the safety transforms once per class and the
    /// result is cached; the browser only consumes it.
    /// </summary>
    private ClassOverlayEntry? ResolveRegistryEntry(string className, string specName, int spellId)
    {
        if (string.IsNullOrEmpty(className)) return null;
        var entry = OverlayFor(className).For(spellId);
        return entry is null ? null : _settings.AbilityOverrides.Apply(entry);
    }

    private ClassOverlay OverlayFor(string className)
    {
        if (_classOverlays.TryGetValue(className, out var cached)) return cached;
        ClassOverlay overlay;
        try
        {
            var path = FindClassOverlayPath(className);
            overlay = path is null
                ? ClassOverlay.Empty(className: className)
                : ClassOverlayLoader.LoadFile(path);
        }
        catch
        {
            overlay = ClassOverlay.Empty(className: className);
        }
        _classOverlays[className] = overlay;
        return overlay;
    }

    /// <summary>
    /// Locates <c>Knowledge/classes/&lt;class&gt;.json</c>: next to the exe, or
    /// in the repo tree during development. Null when the class has no overlay.
    /// </summary>
    private static string? FindClassOverlayPath(string className)
    {
        var file = className + ".json";
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "Knowledge", "classes", file),
        };
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            candidates.Add(Path.Combine(dir.FullName, "app", "MaxDpsCompanion", "Knowledge", "classes", file));
            candidates.Add(Path.Combine(dir.FullName, "Knowledge", "classes", file));
        }
        foreach (var candidate in candidates)
            if (File.Exists(candidate)) return candidate;
        return null;
    }

    /// <summary>
    /// The live why-held / why-ready verdict for a row, read from the engine's
    /// published snapshot. No gate is evaluated here (the scheduler owns that).
    /// </summary>
    private string? LiveVerdictFor(AbilityDefinition ability)
    {
        var engine = _engine;
        if (engine is null) return null;
        if (engine.Paused) return "held: paused";
        var current = engine.CurrentPlanHead;
        if (current is not null && string.Equals(current.Action, ability.Name, StringComparison.OrdinalIgnoreCase))
            return "next: " + VerdictReason(current);
        var last = engine.LastAction;
        if (last is not null && string.Equals(last.Action, ability.Name, StringComparison.OrdinalIgnoreCase))
            return "sent: " + VerdictReason(last);
        return null;
    }

    private static string VerdictReason(LiveActionSnapshot snapshot)
    {
        var why = snapshot.Why.Count > 0 ? string.Join("; ", snapshot.Why) : snapshot.Reason;
        return string.IsNullOrWhiteSpace(why) ? "no reasons recorded" : why;
    }

    // ----- S8 why-not-firing (old MainForm.UpdateWhyNotFiring :2021-2042) -----

    /// <summary>
    /// Pushes one why-not-firing snapshot for the current plan head. Fail-open:
    /// a copy bug must never stall the UI timer or the diagnostics page.
    /// </summary>
    private void UpdateWhyNotFiring()
    {
        try
        {
            var engine = _engine;
            if (engine is null || _advancedDiagnostics is null) return;
            var head = engine.CurrentPlanHead;
            var running = engine.IsRunning;
            var reason = head?.Reason ?? engine.LastAction?.Reason;
            var action = head?.Action ?? engine.LastAction?.Action;
            var mainOn = _settings.SlotEnabled.Length > 0 && _settings.SlotEnabled[0];
            var facts = new WhyNotFiringFacts(
                Label: string.IsNullOrEmpty(action) ? "the current slot" : action,
                ToggleOn: head is not null || mainOn,
                EngineRunning: running,
                Paused: engine.Paused,
                SchedulerReason: string.IsNullOrEmpty(reason) ? null : reason,
                PlanHeadAction: string.IsNullOrEmpty(action) ? null : action,
                FrameAgeMs: running ? engine.LastFrameAgeMs() : -1,
                StaleAfterMs: 1500);
            _advancedDiagnostics.SetWhyNotFiring(facts);
        }
        catch
        {
            // fail-open
        }
    }
}
