using System.Globalization;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// P2-D WinUI port of the shared Abilities preset vocabulary + row text
/// (old Ui/AbilityInspector.cs :12-135 and the Static row helpers in
/// Ui/AbilityExplorer.cs :483-511). Pure helpers; no state, no game API. The
/// Intelligence drill-through and the hero bubbles feed tags through here so
/// the browser and the explorer filter to the same set.
/// </summary>
internal static class AbilityViewPresets
{
    /// <summary>Short display name for a class wire token ("MAGE" -> "Mage").</summary>
    private static readonly Dictionary<string, string> ClassDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DEATHKNIGHT"] = "Death Knight",
        ["DEMONHUNTER"] = "Demon Hunter",
        ["WARRIOR"] = "Warrior",
        ["PALADIN"] = "Paladin",
        ["HUNTER"] = "Hunter",
        ["ROGUE"] = "Rogue",
        ["PRIEST"] = "Priest",
        ["SHAMAN"] = "Shaman",
        ["MAGE"] = "Mage",
        ["WARLOCK"] = "Warlock",
        ["MONK"] = "Monk",
        ["DRUID"] = "Druid",
        ["EVOKER"] = "Evoker",
    };

    public static string ClassDisplay(string wire) =>
        ClassDisplayNames.TryGetValue(wire, out var display) ? display : wire;

    /// <summary>True when a tag names a category/set filter rather than a registry status.</summary>
    public static bool IsCategoryTag(string tag) => tag switch
    {
        "Offensive" or "Defensive" or "Interrupt" or "Mobility"
            or "Self Sustain" or "Self-heal" or "Consumable" or "Trinket"
            or "CrowdControl" or "Crowd control" or "Solo" or "Utility" => true,
        _ => false,
    };

    /// <summary>Canonical category name for a bubble/tag alias.</summary>
    public static string NormalizeCategory(string tag) => tag switch
    {
        "Self-heal" => "Self Sustain",
        "Crowd control" => "CrowdControl",
        _ => tag,
    };

    /// <summary>Human label for a canonical category (used in empty states).</summary>
    public static string CategoryDisplay(string tag) => NormalizeCategory(tag) switch
    {
        "Self Sustain" => "Self-heal",
        "CrowdControl" => "Crowd control",
        _ => tag,
    };

    /// <summary>
    /// Does the ability belong to the category/set? <paramref name="catalog"/>
    /// + class/spec let "CrowdControl" reach the curated CC registry.
    /// </summary>
    public static bool MatchesCategory(
        AbilityDefinition ability, string tag, AbilityCatalog? catalog, string? className, string? specName)
        => NormalizeCategory(tag) switch
        {
            "Offensive" => ability.HasOffensiveCapability,
            "Defensive" => ability.HasDefensiveCapability,
            "Interrupt" => ability.HasInterruptCapability,
            "Mobility" => ability.HasMobilityCapability,
            "Self Sustain" => ability.HasSelfSustainCapability,
            "Consumable" => ability.Category == AbilityCategory.Consumable,
            "Trinket" => ability.Category == AbilityCategory.Trinket,
            "CrowdControl" => ability.Purpose == AbilityPurpose.CrowdControl
                || ability.HasTag("CrowdControl")
                || (catalog is not null && className is not null && specName is not null
                    && catalog.CrowdControlFor(className, specName, ability.SpellId) is not null),
            // Solo = sustain-capable rows: a SelfHeal purpose or the curative tag.
            "Solo" => ability.Purpose == AbilityPurpose.SelfHeal || ability.HasTag("SelfSustain"),
            "Utility" => ability.Category == AbilityCategory.Utility || ability.Automation == AutomationContext.Manual,
            _ => false,
        };

    /// <summary>
    /// Class/spec membership filter. An entry with no recorded class/spec
    /// (a shared/global row) stays visible for every selection.
    /// </summary>
    public static bool MatchesClassSpec(AbilityDefinition ability, string? className, string? specName)
    {
        if (className is not null && ability.Classes.Count > 0 && !Contains(ability.Classes, className)) return false;
        if (specName is not null && ability.Specs.Count > 0 && !Contains(ability.Specs, specName)) return false;
        return true;
    }

    private static bool Contains(IReadOnlyList<string> values, string name)
    {
        for (var i = 0; i < values.Count; i++)
            if (string.Equals(values[i], name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// One-line trigger condition for a row from catalog fields
    /// ("Below 65% HP", "DR: Stun", "Burst window only", "Urgency Red+").
    /// Empty when no explicit condition is known. <paramref name="cc"/> is the
    /// curated CC row for this class/spec/id, when one exists.
    /// </summary>
    public static string ConditionLine(AbilityDefinition ability, CrowdControlEntry? cc = null)
    {
        if (cc is not null) return $"DR: {cc.Dr}";
        if (ability.UseBelowHpPct is int below) return $"Below {below}% HP";
        if (ability.HoldAboveHpPct is int above) return $"Above {above}% HP";
        if (ability.RequiresEnemyCast) return "On enemy cast";
        switch (ability.OffensiveUsage)
        {
            case OffensiveUsage.AoeOnly: return "AoE only \u00B7 not an opener";
            case OffensiveUsage.MajorBurst:
            case OffensiveUsage.MinorBurst:
                return ability.HoldForBurst ? "Burst window only" : "Burst cooldown";
            case OffensiveUsage.Execute: return "Execute range only";
            case OffensiveUsage.SingleTargetOnly: return "Single target only";
            case OffensiveUsage.WindowDriven: return "Burst window only";
        }
        if (ability.IsSurvival) return ability.MinimumUrgency switch
        {
            DefensiveUrgency.Red => "Urgency Red+",
            DefensiveUrgency.Orange => "Urgency Orange+",
            DefensiveUrgency.Yellow => "Urgency Yellow+",
            _ => "",
        };
        if (ability.Purpose == AbilityPurpose.CrowdControl) return "DR-tracked CC";
        return "";
    }

    // ----- row body text (old VirtualAbilityList statics) -----

    internal static string SubRole(AbilityDefinition a)
    {
        if (a.HasInterruptCapability && a.Purpose == AbilityPurpose.Interrupt) return "kick";
        if (a.IsDefensive) return a.Tier.ToString().ToLowerInvariant() + " defensive";
        if (a.Purpose == AbilityPurpose.SelfHeal) return "self-heal";
        if (a.HasMobilityCapability) return a.MobilityKind == MobilityKind.Unknown ? "movement" : a.MobilityKind.ToString().ToLowerInvariant();
        if (a.Purpose == AbilityPurpose.MajorOffensive) return "major offensive";
        if (a.Purpose == AbilityPurpose.MinorOffensive) return "minor offensive";
        if (a.Automation == AutomationContext.Manual) return "manual utility";
        return a.Status.ToString();
    }

    internal static string Why(AbilityDefinition a)
    {
        if (a.Automation == AutomationContext.MaxDpsOnly) return $"MaxDps surfaces it; delegated ({a.Delegation})";
        if (a.NeverAutomatic) return $"manual by design ({a.ManualReason})";
        if (a.HasInterruptCapability) return "companion kicks observed casts";
        if (a.IsSurvival) return "survival rules + MaxDps urgency";
        return $"companion policy ({a.Status})";
    }

    internal static string Hold(AbilityDefinition a)
    {
        if (a.UseBelowHpPct is not null) return $"below {a.UseBelowHpPct}% HP";
        if (a.HoldAboveHpPct is not null) return $"above {a.HoldAboveHpPct}% HP";
        if (a.RequiresEnemyCast) return "target is casting";
        if (a.RidesGcd) return "GCD / key-interval gates";
        return "no condition";
    }

    /// <summary>Full hover sentence: why it can fire, what holds it, urgency.</summary>
    internal static string FullReason(AbilityDefinition ability)
    {
        var reason = $"{ability.Name}: {Why(ability)}; holds {Hold(ability)}.";
        if (ability.MinimumUrgency != DefensiveUrgency.White && ability.IsSurvival)
            reason += $" Urgency {ability.MinimumUrgency}+.";
        return reason;
    }

    /// <summary>Row subline: category · sub-role · condition · cooldown.</summary>
    internal static string SubLine(AbilityDefinition ability, string? condition)
    {
        var line = condition is { Length: > 0 }
            ? $"{ability.Category} \u00B7 {SubRole(ability)} \u00B7 {condition}"
            : $"{ability.Category} \u00B7 {SubRole(ability)}";
        if (ability.CooldownMs > 0)
            line += string.Create(CultureInfo.InvariantCulture, $" \u00B7 {ability.CooldownMs / 1000.0:0.#}s");
        return line;
    }
}
