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
        sb.AppendLine("-- offensive: curated major offensive gap-fill candidates (shared burst");
        sb.AppendLine("--   first, spec-specific second); used only when MaxDps names no bound");
        sb.AppendLine("--   offensive. The companion detects this source by id membership.");
        sb.AppendLine("MDB.Extras = {");
        foreach (var className in AbilityCatalog.ClassOrder)
        {
            if (className.Length == 0) continue;
            if (!AbilityCatalog.SpecOrder.TryGetValue(className, out var specs)) continue;
            sb.Append($"  [\"{className}\"] = {{");
            var any = false;
            for (var i = 1; i < specs.Length; i++)
            {
                var mobility = catalog.Extras(className, specs[i], AbilityCategory.Mobility);
                var selfHeal = catalog.Extras(className, specs[i], AbilityCategory.SelfHeal);
                var offensive = catalog.OffensiveGapFill(className, specs[i]);
                var defensive = catalog.DefensiveGapFill(className, specs[i]);
                var defensiveMinor = catalog.DefensiveGapFillMinor(className, specs[i]);
                if (mobility.Length == 0 && selfHeal.Length == 0 && offensive.Length == 0
                    && defensive.Length == 0 && defensiveMinor.Length == 0) continue;
                any = true;
                sb.Append($" [\"{Escape(specs[i])}\"] = {{");
                if (mobility.Length > 0) sb.Append($" mobility = {{ {string.Join(", ", mobility)} }},");
                if (selfHeal.Length > 0) sb.Append($" selfHeal = {{ {string.Join(", ", selfHeal)} }},");
                if (offensive.Length > 0) sb.Append($" offensive = {{ {string.Join(", ", offensive)} }},");
                if (defensive.Length > 0) sb.Append($" defensive = {{ {string.Join(", ", defensive)} }},");
                if (defensiveMinor.Length > 0) sb.Append($" defensiveMinor = {{ {string.Join(", ", defensiveMinor)} }},");
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
