using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// The generated class-spells layer: token name decoding, the passive/junk
/// filter, the catalog merge (curated &gt; vendor &gt; class spells) and the
/// class-skill screen tree (shared vs per-spec, section grouping).
/// </summary>
public class ClassSpellBookTests
{
    private static ClassSpellBook Book => ClassSpellBook.Default;
    private static AbilityCatalog Catalog => AbilityCatalog.Default;

    // ---- name decoding -----------------------------------------------------

    [Theory]
    [InlineData("SinisterStrike", "Sinister Strike")]
    [InlineData("SliceandDice", "Slice and Dice")]
    [InlineData("BlessingofFreedom", "Blessing of Freedom")]
    [InlineData("AntiMagicShell", "Anti Magic Shell")]
    [InlineData("ImprovedBloodthirst", "Improved Bloodthirst")]
    [InlineData("Shadowstep", "Shadowstep")]
    [InlineData("OdynsFury", "Odyn's Fury")]
    [InlineData("TricksoftheTrade", "Tricks of the Trade")]
    [InlineData("ThousandCuts", "Thousand Cuts")]
    [InlineData("TimeisMoney", "Time is Money")]
    [InlineData("CoupdeGrace", "Coup de Grace")]
    public void DecodeName_Produces_A_Readable_Label(string token, string expected) =>
        Assert.Equal(expected, ClassSpellBook.DecodeName(token));

    // ---- passive / junk filter --------------------------------------------

    [Theory]
    [InlineData("SafeFall", true)]
    [InlineData("ImprovedBloodthirst", true)]
    [InlineData("MasteryUnshackledFury", true)]
    [InlineData("LeatherSpecialization", true)]
    [InlineData("ArtisanRiding", true)]
    [InlineData("BattleforAzerothPathfinder", true)]
    [InlineData("AtrophicPoison", true)]
    [InlineData("MasterPoisoner", true)]
    [InlineData("SliceandDice", false)]
    [InlineData("Shadowstep", false)]
    [InlineData("ShieldWall", false)]
    [InlineData("Vanish", false)]
    [InlineData("PoisonedKnife", false)]
    [InlineData("Stealth", false)]
    public void JunkFilter_Blocks_Passives_And_Keeps_Actives(string token, bool junk) =>
        Assert.Equal(junk, ClassSpellBook.IsJunk(token, 1));

    // ---- the real generated book ------------------------------------------

    [Fact]
    public void Default_Book_Carries_Every_Class_And_Spec()
    {
        Assert.True(Book.Count > 5000, $"book too small: {Book.Count}");
        foreach (var className in AbilityCatalog.ClassOrder)
        {
            if (className.Length == 0) continue;
            Assert.NotEmpty(Book.Specs(className));
        }
        Assert.NotEmpty(Book.ForSpec("ROGUE", "Outlaw"));
        Assert.NotNull(Book.TryGet(1856));   // Vanish
        Assert.NotNull(Book.TryGet(36554));  // Shadowstep
    }

    [Fact]
    public void Default_Book_Decodes_Rogue_Tokens()
    {
        var sinisters = Book.ForSpec("ROGUE", "Assassination").FirstOrDefault(e => e.SpellId == 193315);
        Assert.NotNull(sinisters);
        Assert.Equal("Sinister Strike", sinisters!.Name);

        var slice = Book.TryGet(315496);
        Assert.NotNull(slice);
        Assert.Equal("Slice and Dice", slice!.Name);
    }

    // ---- catalog merge -----------------------------------------------------

    [Fact]
    public void ClassSpell_Ids_Are_Merged_As_The_Lowest_Priority_Layer()
    {
        // Sinister Strike 193315 is in no vendor Cooldowns list and no curated
        // entry, so it can only come from the class-spells layer.
        var sinister = Catalog.TryGet(193315);
        Assert.NotNull(sinister);
        Assert.Equal(AbilityProvenance.ClassSpell, sinister!.Provenance);
        Assert.Equal(AbilityPurpose.Rotational, sinister.Purpose);
        Assert.False(sinister.NeverAutomatic);
        Assert.Contains("ROGUE", sinister.Classes);
        Assert.Contains("Assassination", sinister.Specs);
    }

    [Fact]
    public void Curated_And_Vendor_Win_Over_Class_Spells()
    {
        // Vanish is in the vendor Cooldowns defensive lists AND curated.
        var vanish = Catalog.TryGet(1856);
        Assert.NotNull(vanish);
        Assert.NotEqual(AbilityProvenance.ClassSpell, vanish!.Provenance);

        // Kick is a vendor interrupt entry.
        var kick = Catalog.TryGet(1766);
        Assert.NotNull(kick);
        Assert.NotEqual(AbilityProvenance.ClassSpell, kick!.Provenance);
    }

    // ---- official live-client verification ---------------------------------

    [Fact]
    public void Verification_Supplies_The_Official_Name_And_Icon()
    {
        var sinister = Book.TryGet(193315);
        Assert.NotNull(sinister);
        Assert.Equal("Sinister Strike", sinister!.Name);
        Assert.True(sinister.Verified);
        Assert.False(string.IsNullOrEmpty(sinister.IconSlug));

        var evasion = Book.TryGet(5277);
        Assert.NotNull(evasion);
        Assert.False(string.IsNullOrEmpty(evasion!.IconSlug));
    }

    [Fact]
    public void Verification_Removed_Ids_Are_Not_Merged()
    {
        // Crimson Tempest (121411) is in the vendor SpellData token table but
        // absent from the live 12.1 client SpellName export -> unverified ->
        // never merged into the ability knowledge.
        var entry = Book.TryGet(121411);
        if (entry is not null)
        {
            Assert.False(entry.Verified);
            Assert.Null(Catalog.TryGet(121411));
        }
    }

    [Fact]
    public void Verification_Marks_Every_Entry()
    {
        var verified = Book.All.Count(e => e.Verified);
        Assert.True(verified > 3000, $"verified={verified}");
        Assert.Contains(Book.All, e => !e.Verified);
        Assert.NotNull(Book.TryGetIconSlug(53));    // Backstab
        Assert.NotNull(Book.TryGetIconSlug(36554)); // Shadowstep
    }

    [Fact]
    public void Merged_Movement_Tokens_Stay_Manual_By_Design()
    {
        // A movement token nobody curated (Blink-family style): merged as
        // Movement with NeverAutomatic so enabling it keeps the emergency rule.
        var mergedMovement = Catalog.All
            .Where(a => a.Provenance == AbilityProvenance.ClassSpell
                && a.Purpose == AbilityPurpose.Movement)
            .ToList();
        Assert.NotEmpty(mergedMovement);
        Assert.All(mergedMovement, a => Assert.True(a.NeverAutomatic));
    }

    [Fact]
    public void GapFill_Ignores_The_Modeled_ClassSpell_Layer()
    {
        // The bridge catalog must not grow through a best-effort model.
        var gapFill = Catalog.DefensiveGapFill("WARRIOR", "Arms");
        Assert.All(gapFill, id => Assert.NotEqual(AbilityProvenance.ClassSpell, Catalog.TryGet(id)!.Provenance));
    }

    // ---- class-skill tree --------------------------------------------------

    [Fact]
    public void Tree_Shared_Bucket_Carries_Class_Wide_Abilities_Once()
    {
        var shared = ClassSkillTree.SharedIds(Catalog, Book, "ROGUE");
        Assert.Contains(1856, shared);   // Vanish, all specs
        Assert.Contains(1766, shared);   // Kick, all specs
        Assert.Contains(2983, shared);   // Sprint, all specs

        var outlaw = ClassSkillTree.Build(Catalog, Book, "ROGUE", "Outlaw");
        Assert.Contains(outlaw.Shared, a => a.SpellId == 1856);
        // Shared abilities are not repeated in the spec sections.
        Assert.DoesNotContain(outlaw.Groups.Values.SelectMany(list => list), a => a.SpellId == 1856);
    }

    [Fact]
    public void Tree_Keeps_Shadowstep_Toggleable_For_Rogues()
    {
        // The user's exact complaint: Shadowstep had no toggle. It is a
        // curated mobility extra for every rogue spec, so it must appear.
        var build = ClassSkillTree.Build(Catalog, Book, "ROGUE", "Outlaw");
        var all = build.Shared.Concat(build.Groups.Values.SelectMany(list => list)).ToList();
        Assert.Contains(all, a => a.SpellId == 36554);
    }

    [Fact]
    public void Tree_Groups_By_Section()
    {
        // Purpose-driven grouping is deterministic.
        Assert.Equal(ClassSkillGroup.Main, ClassSkillTree.GroupOf(Catalog.TryGet(193315)!));
        Assert.Equal(ClassSkillGroup.Offensive, ClassSkillTree.GroupOf(Catalog.TryGet(13750)!));
        Assert.Equal(ClassSkillGroup.Defensive, ClassSkillTree.GroupOf(Catalog.TryGet(5277)!));
        Assert.Equal(ClassSkillGroup.Movement, ClassSkillTree.GroupOf(Catalog.TryGet(36554)!));

        // A spec-specific extra lands in the spec panel, not the shared bucket.
        var protection = ClassSkillTree.Build(Catalog, Book, "WARRIOR", "Protection");
        Assert.Contains(protection.Groups[ClassSkillGroup.Defensive], a => a.SpellId == 198304); // Intervene, Prot only
        var arms = ClassSkillTree.Build(Catalog, Book, "WARRIOR", "Arms");
        Assert.DoesNotContain(arms.Groups.Values.SelectMany(list => list), a => a.SpellId == 198304);
    }

    [Fact]
    public void Tree_Covers_All_Classes()
    {
        foreach (var className in AbilityCatalog.ClassOrder)
        {
            if (className.Length == 0) continue;
            var specs = AbilityCatalog.SpecOrder[className];
            for (var i = 1; i < specs.Length; i++)
            {
                var build = ClassSkillTree.Build(Catalog, Book, className, specs[i]);
                var count = build.Shared.Count + build.Groups.Values.Sum(list => list.Count);
                Assert.True(count > 0, $"{className}/{specs[i]} has no abilities");
            }
        }
    }

    // ---- main-rotation opt-out (policy) ------------------------------------

    [Fact]
    public void User_Off_On_A_Main_Rotation_Spell_Skips_It()
    {
        // Sinister Strike merged as Main/Rotational: turning it OFF is the
        // per-spell suggestion veto (the scheduler falls through to the next
        // suggested stroke).
        var ability = Catalog.TryGet(193315);
        Assert.NotNull(ability);

        var options = new PolicyOptions
        {
            Abilities = AbilityPolicy.Default.With(193315, enabled: false, defaultEnabled: true),
        };
        var context = new CombatContext
        {
            ContextValid = true,
            SlotRange = new TriState[PixelProtocol.SlotCount],
            SlotBuffActive = new TriState[PixelProtocol.SlotCount],
        };
        var decision = PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = Slot.Main,
            SpellId = 193315,
            Context = context,
            Options = options,
            Memory = new PolicyMemory(),
            NowMs = 1000,
            InCombat = true,
            HasTarget = true,
        }, Catalog);

        Assert.Equal(PolicyVerdict.Skip, decision.Verdict);
        Assert.Equal("user policy disabled", decision.Reason);
    }

    [Fact]
    public void Main_Rotation_Spells_Default_On()
    {
        var ability = Catalog.TryGet(193315);
        Assert.NotNull(ability);
        Assert.True(AbilityPolicy.Default.IsEnabled(ability!));
    }
}
