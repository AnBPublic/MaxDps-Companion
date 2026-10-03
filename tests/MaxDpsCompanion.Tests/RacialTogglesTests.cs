using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// v3.x racial toggles (spec docs/plans/2026-10-01-racial-toggles.md).
///
/// Racials are normal catalog rows tagged <c>scope=Racial</c>: no wire change,
/// no race field. They carry a real Purpose so the existing providers route
/// them (Offensive/Defensive/SelfSustain/Utility), and the bridge's known-spell
/// filter selects only the race the player actually is. CC/mobility racials
/// stay manual (Never); passives are skipped entirely.
///
/// Reviewer fix #2: disputed ids were verified against wowhead/warcraft.wiki
/// (live 12.1) and the activatable id is now catalogued (Fireblood 265221 not
/// the 273104 aura; Arcane Pulse 260364 not the 260369 trigger; Thorn Bloom
/// 1237885 not the 1238467 aura). Low-confidence + live-unverified racials are
/// forced Manual by <see cref="AbilityCatalog"/> (Azerite Surge 451897).
/// </summary>
public class RacialTogglesTests
{
    private static AbilityCatalog Catalog => AbilityCatalog.Default;

    /// <summary>Every catalogued racial row (38), by category.</summary>
    private static readonly int[] OffensiveRacials =
    [
        20572, 33697, 33702, 26297, 274738, 265221, 255647, 260364, 69041, 451897,
        28730, 50613, 80483, 155145, 202719, 232633, 25046, 69179, 129597, 312411, 1237885,
    ];

    private static readonly int[] SelfHealRacials =
    [
        28880, 59542, 59543, 59544, 59545, 59547, 59548, 121093, 291944, 312924,
    ];

    private static readonly int[] DefensiveRacials = [20594, 58984];

    private static readonly int[] ManualRacials = [287712, 20549, 107079, 357214, 255654];

    private static int[] AllRacials => [.. OffensiveRacials, .. DefensiveRacials, .. SelfHealRacials, .. ManualRacials];

    [Fact]
    public void Racial_Rows_Carry_Scope_And_No_Class_Membership()
    {
        var bloodFury = Catalog.TryGet(20572);
        Assert.NotNull(bloodFury);
        Assert.Equal(AbilityCatalog.RacialScope, bloodFury!.Scope);
        Assert.Empty(bloodFury.Classes);
        Assert.Empty(bloodFury.Specs);
    }

    [Fact]
    public void Racial_Row_Count_Is_Exact()
    {
        // Pins the reviewed coverage: 21 offensive + 2 defensive + 10 self-heal
        // + 5 manual CC/mobility. A drop or an accidental passive must fail here.
        Assert.Equal(38, Catalog.All.Count(a => a.Scope == AbilityCatalog.RacialScope));
    }

    [Fact]
    public void Multi_Id_Racials_Are_Each_Catalogued()
    {
        foreach (var id in AllRacials)
        {
            var ability = Catalog.TryGet(id);
            Assert.True(ability is not null, $"racial {id} missing from the catalog");
            Assert.Equal(AbilityCatalog.RacialScope, ability!.Scope);
        }
    }

    [Fact]
    public void Racial_Offensives_Route_To_The_Offensive_Provider()
    {
        foreach (var id in OffensiveRacials)
        {
            var ability = Catalog.TryGet(id)!;
            Assert.True(ability.Purpose is AbilityPurpose.MajorOffensive or AbilityPurpose.MinorOffensive);
            Assert.Same(CandidateProviders.Offensive, CandidateProviders.For(Slot.Offensive, ability));
        }
    }

    [Fact]
    public void Racial_Defensive_Minors_Route_To_The_Defensive_Provider()
    {
        foreach (var id in DefensiveRacials)
        {
            var ability = Catalog.TryGet(id)!;
            Assert.True(ability.IsDefensive);
            Assert.Same(CandidateProviders.Defensive, CandidateProviders.For(Slot.Defensive, ability));
        }
    }

    [Fact]
    public void Racial_Self_Heals_Route_To_SelfSustain()
    {
        foreach (var id in SelfHealRacials)
        {
            var ability = Catalog.TryGet(id)!;
            Assert.Equal(AbilityCategory.SelfHeal, ability.Category);
            Assert.Same(CandidateProviders.SelfSustain, CandidateProviders.For(Slot.SelfHeal, ability));
        }
    }

    [Fact]
    public void Racial_Shadowmeld_And_Stoneform_Require_Orange_Urgency()
    {
        // Review fix #3: Shadowmeld drops combat and Stoneform is a 2min
        // cleanse; a blind Yellow auto-press can waste either, so both are
        // curated to Orange+ and are not lowered by a live MaxDps suggestion.
        foreach (var id in new[] { 58984, 20594 })
        {
            var ability = Catalog.TryGet(id)!;
            Assert.Equal(DefensiveUrgency.Orange, ability.MinimumUrgency);
            Assert.True(ability.MinimumUrgencyCurated, $"{id} urgency must be curated, not tier-default");
        }
    }

    [Fact]
    public void Racial_Low_Confidence_Unverified_Is_Manual()
    {
        // Review fix #2: no racial is live-verified offline; a Low-confidence,
        // live-unverified id is forced never-automatic (Azerite Surge 451897 is
        // the triggered pulse, not a confirmed activatable).
        foreach (var id in AllRacials)
            Assert.False(Catalog.TryGet(id)!.LiveVerified, $"racial {id} must not claim live verification");

        var azerite = Catalog.TryGet(451897)!;
        Assert.True(azerite.NeverAutomatic);
        Assert.Equal(AutomationContext.Manual, azerite.Automation);
        Assert.False(azerite.Automatable);
    }

    [Fact]
    public void Racial_Offensive_Ids_Are_Recognised_As_GapFill()
    {
        // The bridge emits racial ids into every spec's offensive list, so the
        // companion must classify them as gap-fill (not MaxDpsWire) even though
        // the class-bound OffensiveGapFill arrays themselves are unchanged.
        Assert.Contains(20572, Catalog.RacialIds(AbilityCategory.Offensive));
        Assert.True(Catalog.IsOffensiveGapFill("WARRIOR", "Arms", 20572));
        Assert.True(Catalog.IsOffensiveGapFill("MAGE", "Frost", 26297));
        Assert.False(Catalog.IsOffensiveGapFill("WARRIOR", "Arms", 6552));
    }

    [Fact]
    public void Racial_Cc_And_Mobility_Stay_Manual()
    {
        foreach (var id in ManualRacials)
        {
            var ability = Catalog.TryGet(id);
            Assert.True(ability is not null, $"manual racial {id} missing");
            Assert.True(ability!.NeverAutomatic, $"{id} must stay NeverAutomatic");
            Assert.Equal(AutomationContext.Manual, ability.Automation);
        }
    }

    [Fact]
    public void Racial_Passives_And_Travel_Are_Not_Catalogued()
    {
        // Passives were deliberately skipped; a racial-scope row must always be
        // a classified active (never Unknown/Rotational), so nothing passive
        // leaked in.
        foreach (var ability in Catalog.All.Where(a => a.Scope == AbilityCatalog.RacialScope))
        {
            Assert.NotEqual(IntelligenceStatus.Incomplete, ability.Status);
            Assert.NotEqual(AbilityPurpose.Unknown, ability.Purpose);
            Assert.NotEqual(AbilityPurpose.Rotational, ability.Purpose);
        }
    }

    [Fact]
    public void Racial_JunkTokens_Are_Not_Regressed()
    {
        // Review fix #5: the catalogued racial actives must not be junk-filtered
        // out of the class-spells layer, while racial passives/manual dispels
        // stay filtered.
        Assert.False(ClassSpellBook.IsJunk("BloodFury", 20572));
        Assert.False(ClassSpellBook.IsJunk("ArcaneTorrent", 28730));
        Assert.False(ClassSpellBook.IsJunk("GiftOfTheNaaru", 59545));
        Assert.False(ClassSpellBook.IsJunk("Berserking", 26297));
        Assert.False(ClassSpellBook.IsJunk("Stoneform", 20594));

        Assert.True(ClassSpellBook.IsJunk("ArcaneResistance", 822));
        Assert.True(ClassSpellBook.IsJunk("Hardiness", 20573));
        Assert.True(ClassSpellBook.IsJunk("WillOfTheForsaken", 7744));
    }

    [Fact]
    public void Racial_Ids_Are_Emitted_Into_The_Generated_Catalog()
    {
        var lua = CatalogLuaGenerator.Generate(Catalog);
        Assert.Contains("20572", lua);
        Assert.Contains("265221", lua);   // corrected Fireblood activatable
        Assert.Contains("260364", lua);   // corrected Arcane Pulse activatable
        Assert.Contains("59545", lua);
        Assert.Contains("20594", lua);
        Assert.Contains(20572, Catalog.RacialIds(AbilityCategory.Offensive));
        Assert.Contains(20594, Catalog.RacialIds(AbilityCategory.Defensive));
        Assert.Contains(59545, Catalog.RacialIds(AbilityCategory.SelfHeal));

        // Self-heal racials must ride the selfHeal list so the SelfHeal toggle
        // governs them (review fix #4); dropped aura/passive ids must be absent.
        foreach (var id in SelfHealRacials)
            Assert.Contains($"{id}", lua);
        Assert.DoesNotContain("291628", lua); // Brush It Off passive dropped
        Assert.DoesNotContain("273104", lua); // Fireblood buff aura
        Assert.DoesNotContain("260369", lua); // Arcane Pulse trigger
        Assert.DoesNotContain("1238467", lua); // Thorn Bloom aura
    }

    [Fact]
    public void Racial_Committed_Catalog_Fixture_Matches()
    {
        // Pins the checked-in fixture to the racial ids the generator emits
        // (the full byte-for-byte match is AbilityCatalogTests
        // .Generated_Lua_Matches_Committed_Catalog_File).
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "Catalog.lua");
        Assert.True(File.Exists(path), $"missing {path}; regenerate with --gen-catalog");
        var lua = File.ReadAllText(path);
        Assert.Contains("265221", lua);
        Assert.Contains("1237885", lua);
        Assert.Contains("121093", lua);
        Assert.DoesNotContain("291628", lua);
    }
}
