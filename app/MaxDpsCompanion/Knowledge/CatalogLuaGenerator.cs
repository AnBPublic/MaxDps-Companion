using System.Text;

namespace MaxDpsCompanion;

/// <summary>
/// Emits <c>addon/MaxDpsBridge/Catalog.lua</c> from the ability catalog: the
/// class/spec wire ids and the per-spec Mobility/SelfHeal extra lists the
/// bridge resolves keybinds for. Generated (never hand-edited) and pinned by
/// <c>CatalogLuaSyncTests</c>, so the C# policy and the in-game bridge cannot
/// disagree about which abilities the companion-only slots may offer.
/// </summary>
internal static class CatalogLuaGenerator
{
    public static string Generate(AbilityCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var sb = new StringBuilder(16 * 1024);

        sb.AppendLine("-- ============================ GENERATED FILE =============================");
        sb.AppendLine("-- Catalog.lua - generated from the companion's ability knowledge base");
        sb.AppendLine("-- (app/MaxDpsCompanion/Knowledge/*.json). DO NOT EDIT BY HAND.");
        sb.AppendLine("--");
        sb.AppendLine("-- Regenerate:  MaxDpsCompanion.exe --gen-catalog");
        sb.AppendLine("-- Verified by: tests/MaxDpsCompanion.Tests/CatalogLuaSyncTests.cs");
        sb.AppendLine("--");
        sb.AppendLine("-- Contents: class/spec numeric ids (protocol v5 cell 33) and the");
        sb.AppendLine("-- curated Mobility / SelfHeal spell lists the companion-only slots use.");
        sb.AppendLine("-- ========================================================================");
        sb.AppendLine();
        sb.AppendLine("local _, MDB = ...;");
        sb.AppendLine();
        sb.AppendLine($"MDB.CATALOG_REVISION = {AbilityCatalog.CatalogVersion};");
        sb.AppendLine();
        sb.AppendLine("-- Class ids: fixed alphabetical order (wire format; never reorder).");
        sb.Append("MDB.ClassIds = {");
        for (var i = 1; i < AbilityCatalog.ClassOrder.Length; i++)
            sb.Append($" [\"{AbilityCatalog.ClassOrder[i]}\"]={i},");
        sb.AppendLine(" }");
        sb.AppendLine();
        sb.AppendLine("-- Spec ordinals per class (wire format; never reorder).");
        sb.AppendLine("MDB.SpecIds = {");
        foreach (var className in AbilityCatalog.ClassOrder)
        {
            if (className.Length == 0) continue;
            if (!AbilityCatalog.SpecOrder.TryGetValue(className, out var specs)) continue;
            sb.Append($"  [\"{className}\"] = {{");
            for (var i = 1; i < specs.Length; i++)
                sb.Append($" [\"{Escape(specs[i])}\"]={i},");
            sb.AppendLine(" },");
        }
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("-- Spell variants: id -> alias ids the bridge may meet on a bar or in a");
        sb.AppendLine("-- macro (override/base/alias resolution). Symmetric; sorted by id.");
        sb.AppendLine("MDB.SpellAliases = {");
        foreach (var (id, aliasIds) in catalog.Aliases.OrderBy(kv => kv.Key))
        {
            sb.Append($"  [{id}] = {{ ");
            sb.Append(string.Join(", ", aliasIds.OrderBy(a => a)));
            sb.AppendLine(" },");
        }
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("-- Curated companion-only slot candidates, in preference order.");
        sb.AppendLine("-- mobility: gap closers + movement; the policy decides USE/HOLD.");
        sb.AppendLine("-- selfHeal: self-sustain candidates; Solo mode only.");
        sb.AppendLine("-- defensive: Red-urgency gap-fill candidates for the Defensive slot");
        sb.AppendLine("--   (derived from the vendor per-spec defensive lists, Major first, ");
        sb.AppendLine("--   immunities excluded); used only when MaxDps names no bound defensive.");
        sb.AppendLine("-- defensiveMinor: short-cooldown (Minor/None) gap-fill candidates for");
        sb.AppendLine("--   the Defensive slot's Orange tier (v3.0.0); majors still need Red.");
        sb.AppendLine("-- defensiveMajor: Major-tier gap-fill candidates for the v3.3.0 Solo");
        sb.AppendLine("--   escalation ladder (Solo at/below the major HP band only).");
        sb.AppendLine("-- immunity: full-immunity candidates for the v3.3.0 Solo ladder bottom");
        sb.AppendLine("--   band (Solo at/below the immunity HP band only; never in groups).");
        sb.AppendLine("-- offensive: curated major offensive gap-fill candidates (shared burst");
        sb.AppendLine("--   first, spec-specific second); used only when MaxDps names no bound");
        sb.AppendLine("--   offensive. The companion detects this source by id membership.");
        sb.AppendLine("-- cc: auto-eligible curated crowd-control candidates (v3.4.0). The");
        sb.AppendLine("--   bridge reuses the Interrupt slot for these ONLY when MaxDps names");
        sb.AppendLine("--   no ready+bound interrupt (never while a live interrupt is pending).");
        sb.AppendLine("--   No wire source bit exists; the companion's CrowdControlGate is the");
        sb.AppendLine("--   authority on USE/HOLD. MaxDps-owned stuns are never emitted here.");
        sb.AppendLine("-- Racial-scope rows (scope=Racial) are appended to every spec's");
        sb.AppendLine("--   offensive/defensiveMinor/selfHeal lists. Race is implicit: the");
        sb.AppendLine("--   bridge's known-spell filter only finds a keybind for the race the");
        sb.AppendLine("--   player actually is, so a non-matching race simply skips the entry.");
        sb.AppendLine("MDB.Extras = {");

        // v3.x racial toggles: scope=Racial rows are carried for EVERY
        // class/spec (the bridge's known-spell filter selects the player's
        // race). Appended AFTER the class-bound entries so a class cooldown is
        // never preempted; the class-bound gap-fill arrays stay byte-identical.
        // Self-heal racials (Gift of the Naaru, Regeneratin', H.O.L.O.) ride
        // the selfHeal list, NOT defensiveMinor: routing them through the
        // Defensive slot bypassed the SelfHeal toggle (review fix #4). Racial
        // Defensive-category rows (Stoneform/Shadowmeld) ride defensiveMinor;
        // the bridge marks that slot with the dedicated DefensiveCatalogSource
        // wire bit, so no id-membership shadow list is needed for them (only
        // offensives, which have no source bit, need IsOffensiveGapFill).
        var racialOffensive = catalog.RacialIds(AbilityCategory.Offensive);
        var racialDefensive = catalog.RacialIds(AbilityCategory.Defensive);
        var racialSelfHeal = catalog.RacialIds(AbilityCategory.SelfHeal);

        foreach (var className in AbilityCatalog.ClassOrder)
        {
            if (className.Length == 0) continue;
            if (!AbilityCatalog.SpecOrder.TryGetValue(className, out var specs)) continue;
            sb.Append($"  [\"{className}\"] = {{");
            var any = false;
            for (var i = 1; i < specs.Length; i++)
            {
                var mobility = catalog.Extras(className, specs[i], AbilityCategory.Mobility);
                var selfHeal = catalog.Extras(className, specs[i], AbilityCategory.SelfHeal)
                    .Concat(racialSelfHeal).ToArray();
                var offensive = catalog.OffensiveGapFill(className, specs[i]).Concat(racialOffensive).ToArray();
                var defensive = catalog.DefensiveGapFill(className, specs[i]);
                var defensiveMinor = catalog.DefensiveGapFillMinor(className, specs[i]).Concat(racialDefensive).ToArray();
                var defensiveMajor = catalog.DefensiveGapFillMajor(className, specs[i]);
                var immunity = catalog.ImmunityGapFill(className, specs[i]);
                var cc = catalog.CrowdControlGapFill(className, specs[i]);
                if (mobility.Length == 0 && selfHeal.Length == 0 && offensive.Length == 0
                    && defensive.Length == 0 && defensiveMinor.Length == 0
                    && defensiveMajor.Length == 0 && immunity.Length == 0
                    && cc.Length == 0) continue;
                any = true;
                sb.Append($" [\"{Escape(specs[i])}\"] = {{");
                if (mobility.Length > 0) sb.Append($" mobility = {{ {string.Join(", ", mobility)} }},");
                if (selfHeal.Length > 0) sb.Append($" selfHeal = {{ {string.Join(", ", selfHeal)} }},");
                if (offensive.Length > 0) sb.Append($" offensive = {{ {string.Join(", ", offensive)} }},");
                if (defensive.Length > 0) sb.Append($" defensive = {{ {string.Join(", ", defensive)} }},");
                if (defensiveMinor.Length > 0) sb.Append($" defensiveMinor = {{ {string.Join(", ", defensiveMinor)} }},");
                if (defensiveMajor.Length > 0) sb.Append($" defensiveMajor = {{ {string.Join(", ", defensiveMajor)} }},");
                if (immunity.Length > 0) sb.Append($" immunity = {{ {string.Join(", ", immunity)} }},");
                if (cc.Length > 0) sb.Append($" cc = {{ {string.Join(", ", cc)} }},");
                sb.Append(" },");
            }
            if (any) sb.AppendLine();
            else sb.AppendLine();
            sb.AppendLine("  },");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
