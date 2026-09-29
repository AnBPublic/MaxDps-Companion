namespace MaxDpsCompanion;

/// <summary>
/// WoW's diminishing-returns categories, used ONLY for the companion's own
/// conservative "do not auto-chain" memory. The companion never infers the
/// target's live DR state (that is unobservable in Midnight); it remembers what
/// it itself applied and fails open whenever that memory is unknown.
/// </summary>
internal enum CcDrCategory
{
    Unknown = 0,
    Stun,
    Incapacitate,
    Disorient,
    Silence,
    Root,
    Fear,
}

/// <summary>
/// One curated crowd-control entry (v3.4.0 CC appendix). The list is
/// hand-curated and every id is VERIFIED against the live-client DB2 export
/// (<c>Knowledge/spell-verification.json</c>, <c>verified:true</c>) plus a
/// vendor/curated row. Unverified ids are deliberately absent — an empty list
/// is an honest empty state, never a guessed id.
///
/// <see cref="AutoEligible"/> is the safety flag: only companion-curated
/// manual-CC rows (the <c>purpose:"CrowdControl"</c> Utility entries already in
/// <c>abilities.json</c>) may ever be fired automatically while the CC toggle
/// is ON. Stuns that already live on a MaxDps-owned rotation row (Storm Bolt,
/// Shockwave, …) are recorded for coverage/DR documentation only and stay
/// <c>AutoEligible=false</c>: MaxDps authority is preserved for them.
/// </summary>
internal sealed record CrowdControlEntry(
    int SpellId,
    string Name,
    CcKind Kind,
    CcDrCategory Dr,
    bool SingleTarget,
    bool Aoe,
    int CdMs,
    bool AutoEligible,
    string ClassName,
    string[] Specs)
{
    /// <summary>True when the entry is one the companion may generate automatically.</summary>
    public bool IsAoe => Aoe || !SingleTarget;
}

/// <summary>
/// The curated CC registry (read path consumed by
/// <see cref="AbilityCatalog.CrowdControlFor(string?, string?, int)"/>). Pure
/// static data; no game API, no wire change.
/// </summary>
internal static class CrowdControlCatalog
{
    private static readonly CrowdControlEntry[] Entries = Build();

    /// <summary>All curated CC entries for a class+spec (empty when none/unknown).</summary>
    public static IReadOnlyList<CrowdControlEntry> For(string? className, string? specName)
    {
        if (className is null || specName is null) return [];
        var result = new List<CrowdControlEntry>();
        foreach (var entry in Entries)
        {
            if (!Matches(entry, className, specName)) continue;
            result.Add(entry);
        }
        return result;
    }

    /// <summary>The curated CC entry for one spell id on a class+spec, or null.</summary>
    public static CrowdControlEntry? Find(string? className, string? specName, int spellId)
    {
        if (className is null || specName is null || spellId <= 0) return null;
        foreach (var entry in Entries)
        {
            if (entry.SpellId != spellId) continue;
            if (Matches(entry, className, specName)) return entry;
        }
        return null;
    }

    /// <summary>Every curated CC id (for audit/coverage tests).</summary>
    public static IReadOnlyList<int> AllIds()
    {
        var ids = new int[Entries.Length];
        for (var i = 0; i < Entries.Length; i++) ids[i] = Entries[i].SpellId;
        return ids;
    }

    private static bool Matches(CrowdControlEntry entry, string className, string specName)
    {
        if (!string.Equals(entry.ClassName, className, StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var spec in entry.Specs)
            if (string.Equals(spec, specName, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // ------------------------------------------------------------------
    // Curated data. Format: Cc(id, name, kind, dr, single, aoe, cdMs,
    // autoEligible, className, specs…). All ids verified:true in
    // spell-verification.json (checked 2026-09-29).
    // ------------------------------------------------------------------

    private static CrowdControlEntry[] Build() =>
    [
        // ---- companion-curated manual CC (AutoEligible: toggle + safe target) ----
        Cc(118, "Polymorph", CcKind.Disorient, CcDrCategory.Disorient, true, false, 0, true, "MAGE", "Arcane", "Fire", "Frost"),
        Cc(339, "Entangling Roots", CcKind.Root, CcDrCategory.Root, true, false, 0, true, "DRUID", "Balance", "Feral", "Guardian", "Restoration"),
        Cc(710, "Banish", CcKind.Banish, CcDrCategory.Incapacitate, true, false, 0, true, "WARLOCK", "Affliction", "Demonology", "Destruction"),
        Cc(853, "Hammer of Justice", CcKind.Stun, CcDrCategory.Stun, true, false, 60000, true, "PALADIN", "Holy", "Protection", "Retribution"),
        Cc(1098, "Subjugate Demon", CcKind.Subjugate, CcDrCategory.Incapacitate, true, false, 0, true, "WARLOCK", "Affliction", "Demonology", "Destruction"),
        Cc(1776, "Gouge", CcKind.Incapacitate, CcDrCategory.Incapacitate, true, false, 0, true, "ROGUE", "Assassination", "Outlaw", "Subtlety"),
        Cc(2094, "Blind", CcKind.Disorient, CcDrCategory.Disorient, true, false, 120000, true, "ROGUE", "Assassination", "Outlaw", "Subtlety"),
        Cc(5246, "Intimidating Shout", CcKind.Fear, CcDrCategory.Fear, false, true, 90000, true, "WARRIOR", "Arms", "Fury", "Protection"),
        Cc(5782, "Fear", CcKind.Fear, CcDrCategory.Fear, true, false, 0, true, "WARLOCK", "Affliction", "Demonology", "Destruction"),
        Cc(6770, "Sap", CcKind.Incapacitate, CcDrCategory.Incapacitate, true, false, 0, true, "ROGUE", "Assassination", "Outlaw", "Subtlety"),
        Cc(8122, "Psychic Scream", CcKind.Fear, CcDrCategory.Fear, false, true, 60000, true, "PRIEST", "Discipline", "Holy", "Shadow"),
        Cc(20066, "Repentance", CcKind.Incapacitate, CcDrCategory.Incapacitate, true, false, 60000, true, "PALADIN", "Holy", "Protection", "Retribution"),
        Cc(33786, "Cyclone", CcKind.Disorient, CcDrCategory.Disorient, true, false, 0, true, "DRUID", "Balance", "Feral", "Guardian", "Restoration"),
        Cc(51514, "Hex", CcKind.Incapacitate, CcDrCategory.Incapacitate, true, false, 30000, true, "SHAMAN", "Elemental", "Enhancement", "Restoration"),
        Cc(111673, "Control Undead", CcKind.Subjugate, CcDrCategory.Incapacitate, true, false, 0, true, "DEATHKNIGHT", "Blood", "Frost", "Unholy"),
        Cc(115078, "Paralysis", CcKind.Incapacitate, CcDrCategory.Incapacitate, true, false, 45000, true, "MONK", "Brewmaster", "Mistweaver", "Windwalker"),
        Cc(116844, "Ring of Peace", CcKind.Silence, CcDrCategory.Incapacitate, false, true, 0, true, "MONK", "Brewmaster", "Mistweaver", "Windwalker"),
        Cc(187650, "Freezing Trap", CcKind.Incapacitate, CcDrCategory.Incapacitate, true, false, 30000, true, "HUNTER", "Beast Mastery", "Marksmanship", "Survival"),
        Cc(217832, "Imprison", CcKind.Incapacitate, CcDrCategory.Incapacitate, true, false, 45000, true, "DEMONHUNTER", "Devourer", "Havoc", "Vengeance"),
        Cc(221562, "Asphyxiate", CcKind.Stun, CcDrCategory.Stun, true, false, 45000, true, "DEATHKNIGHT", "Blood", "Frost", "Unholy"),
        Cc(360806, "Sleep Walk", CcKind.Sleep, CcDrCategory.Disorient, true, false, 30000, true, "EVOKER", "Augmentation", "Devastation", "Preservation"),
        Cc(372048, "Oppressing Roar", CcKind.Disorient, CcDrCategory.Disorient, false, true, 60000, true, "EVOKER", "Augmentation", "Devastation", "Preservation"),

        // ---- coverage only: MaxDps-owned rotation stuns (never auto-overridden) ----
        Cc(107570, "Storm Bolt", CcKind.Stun, CcDrCategory.Stun, true, false, 30000, false, "WARRIOR", "Arms", "Fury", "Protection"),
        Cc(46968, "Shockwave", CcKind.Stun, CcDrCategory.Stun, false, true, 40000, false, "WARRIOR", "Arms", "Fury", "Protection"),
        Cc(207167, "Blinding Sleet", CcKind.Disorient, CcDrCategory.Disorient, false, true, 60000, false, "DEATHKNIGHT", "Blood", "Frost", "Unholy"),
        Cc(5211, "Mighty Bash", CcKind.Stun, CcDrCategory.Stun, true, false, 50000, false, "DRUID", "Balance", "Feral", "Guardian", "Restoration"),
        Cc(102359, "Mass Entanglement", CcKind.Root, CcDrCategory.Root, false, true, 30000, false, "DRUID", "Balance", "Feral", "Guardian", "Restoration"),
        Cc(102793, "Ursol's Vortex", CcKind.Root, CcDrCategory.Root, false, true, 60000, false, "DRUID", "Balance", "Feral", "Guardian", "Restoration"),
        Cc(119381, "Leg Sweep", CcKind.Stun, CcDrCategory.Stun, false, true, 60000, false, "MONK", "Brewmaster", "Mistweaver", "Windwalker"),
        Cc(19577, "Intimidation", CcKind.Stun, CcDrCategory.Stun, true, false, 60000, false, "HUNTER", "Beast Mastery", "Marksmanship", "Survival"),
        Cc(1833, "Cheap Shot", CcKind.Stun, CcDrCategory.Stun, true, false, 0, false, "ROGUE", "Assassination", "Outlaw", "Subtlety"),
        Cc(408, "Kidney Shot", CcKind.Stun, CcDrCategory.Stun, true, false, 0, false, "ROGUE", "Assassination", "Outlaw", "Subtlety"),
        Cc(88625, "Holy Word: Chastise", CcKind.Stun, CcDrCategory.Stun, true, false, 60000, false, "PRIEST", "Discipline", "Holy", "Shadow"),
        Cc(9484, "Shackle Horror", CcKind.Incapacitate, CcDrCategory.Incapacitate, true, false, 0, false, "PRIEST", "Discipline", "Holy", "Shadow"),
        Cc(192058, "Capacitor Totem", CcKind.Stun, CcDrCategory.Stun, false, true, 60000, false, "SHAMAN", "Elemental", "Enhancement", "Restoration"),
        Cc(31661, "Dragon's Breath", CcKind.Disorient, CcDrCategory.Disorient, false, true, 45000, false, "MAGE", "Arcane", "Fire", "Frost"),
        Cc(157980, "Supernova", CcKind.Disorient, CcDrCategory.Disorient, false, true, 60000, false, "MAGE", "Arcane", "Fire", "Frost"),
        Cc(122, "Frost Nova", CcKind.Root, CcDrCategory.Root, false, true, 30000, false, "MAGE", "Arcane", "Fire", "Frost"),
        Cc(113724, "Ring of Frost", CcKind.Incapacitate, CcDrCategory.Incapacitate, false, true, 45000, false, "MAGE", "Arcane", "Fire", "Frost"),
        Cc(30283, "Shadowfury", CcKind.Stun, CcDrCategory.Stun, false, true, 60000, false, "WARLOCK", "Affliction", "Demonology", "Destruction"),
        Cc(5484, "Howl of Terror", CcKind.Fear, CcDrCategory.Fear, false, true, 40000, false, "WARLOCK", "Affliction", "Demonology", "Destruction"),
        Cc(6789, "Mortal Coil", CcKind.Fear, CcDrCategory.Fear, true, false, 45000, false, "WARLOCK", "Affliction", "Demonology", "Destruction"),
        Cc(179057, "Chaos Nova", CcKind.Stun, CcDrCategory.Stun, false, true, 60000, false, "DEMONHUNTER", "Devourer", "Havoc", "Vengeance"),
        Cc(211881, "Fel Eruption", CcKind.Stun, CcDrCategory.Stun, true, false, 45000, false, "DEMONHUNTER", "Devourer", "Havoc", "Vengeance"),
    ];

    private static CrowdControlEntry Cc(
        int id, string name, CcKind kind, CcDrCategory dr,
        bool single, bool aoe, int cdMs, bool auto, string className, params string[] specs) =>
        new(id, name, kind, dr, single, aoe, cdMs, auto, className, specs);
}
