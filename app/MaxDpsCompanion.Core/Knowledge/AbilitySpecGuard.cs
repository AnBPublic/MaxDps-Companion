namespace MaxDpsCompanion;

/// <summary>
/// Fail-closed class/spec guard for known Companion-owned optional actions.
/// Direct MaxDps Main and flagged recommendations remain authoritative, as
/// their live game source may know a 12.1.5 talent our older catalog missed.
/// Missing observed class/spec does not invent a mismatch; other contextual
/// provider gates still hold unknown signals. No allocation per tick.
/// </summary>
internal static class AbilitySpecGuard
{
    public static bool MatchesObserved(AbilityDefinition ability, CombatContext context)
    {
        if (string.Equals(ability.Scope, AbilityCatalog.RacialScope,
                StringComparison.OrdinalIgnoreCase)) return true;
        if (context.Class is { Length: > 0 } cls && ability.Classes.Count > 0)
        {
            var found = false;
            foreach (var item in ability.Classes)
                if (string.Equals(item, cls, StringComparison.OrdinalIgnoreCase))
                { found = true; break; }
            if (!found) return false;
        }
        if (context.Spec is { Length: > 0 } spec && ability.Specs.Count > 0)
        {
            var found = false;
            foreach (var item in ability.Specs)
                if (string.Equals(item, spec, StringComparison.OrdinalIgnoreCase))
                { found = true; break; }
            if (!found) return false;
        }
        return true;
    }
}
