using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Serializes the CC suite: <see cref="CrowdControlGate"/> / Memory are
/// process-global statics, and <c>AppSettings.Load</c> (applied by several
/// other test classes) reconfigures the gate. Running this collection with
/// parallelization disabled keeps those writers from flipping the gate mid-test.
/// </summary>
[CollectionDefinition("GlobalStaticState", DisableParallelization = true)]
public sealed class GlobalStaticStateCollection
{
}

/// <summary>
/// v3.4.0 CC appendix: curated coverage, the opt-in gate, the conservative
/// anti-chain memory, and the safety rules (never an opener, target required,
/// MaxDps-owned stuns never overridden). Drives the real evaluator/catalog.
/// </summary>
[Collection("GlobalStaticState")]
public class CrowdControlTests
{
    private const long Now = 50_000;

    /// <summary>Each xunit test gets a fresh instance; reset the shared gate/memory.</summary>
    public CrowdControlTests()
    {
        CrowdControlGate.Reset();
        CrowdControlVetoes.Memory = new CrowdControlDiminishing();
        CrowdControlVetoes.ClearStunIneffective();
    }

    private static AbilityCatalog Catalog => AbilityCatalog.Default;

    private static CombatContext Context(
        string className = "WARRIOR",
        string spec = "Arms",
        PlayerCastState cast = PlayerCastState.None,
        TriState targetInMelee = TriState.Unknown,
        TriState[]? range = null,
        TriState targetCasting = TriState.Unknown,
        TriState targetCastInterruptible = TriState.Unknown) => new()
    {
        Class = className,
        Spec = spec,
        Cast = cast,
        TargetInMelee = targetInMelee,
        TargetCasting = targetCasting,
        TargetCastInterruptible = targetCastInterruptible,
        SlotRange = range ?? new TriState[PixelProtocol.SlotCount],
        SlotBuffActive = new TriState[PixelProtocol.SlotCount],
        ContextValid = true,
    };

    private static TriState[] Range(Slot slot, TriState state)
    {
        var range = new TriState[PixelProtocol.SlotCount];
        range[(int)slot] = state;
        return range;
    }

    private static PolicyDecision Evaluate(
        Slot slot,
        int spellId,
        CombatContext context,
        bool inCombat = true,
        bool hasTarget = true,
        PolicyMemory? memory = null) =>
        PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = slot,
            SpellId = spellId,
            Context = context,
            Options = PolicyOptions.Standard,
            Memory = memory ?? new PolicyMemory(),
            NowMs = Now,
            InCombat = inCombat,
            HasTarget = hasTarget,
        }, Catalog);

    // ---- gate ----

    [Fact]
    public void Gate_Off_Keeps_Curated_Cc_Manual_By_Design()
    {
        CrowdControlGate.Reset();
        var result = Evaluate(Slot.Offensive, 853, Context("PALADIN", "Holy")); // Hammer of Justice
        Assert.NotEqual(PolicyVerdict.Use, result.Verdict);
        Assert.NotEqual("CrowdControl", result.Provider);
    }

    [Fact]
    public void Gate_On_Fires_SingleTarget_Stun_On_Confirmed_Casting_Target()
    {
        CrowdControlGate.Enabled = true;
        try
        {
            // v3.5 Q1 casting-only: a Stun fires only while the target is
            // observably casting (an interrupt substitute).
            var result = Evaluate(Slot.Offensive, 853, Context("PALADIN", "Holy",
                range: Range(Slot.Offensive, TriState.Yes), targetCasting: TriState.Yes));
            Assert.Equal(PolicyVerdict.Use, result.Verdict);
            Assert.Equal("CrowdControl", result.Provider);
            Assert.Equal(CandidateSourceKind.CompanionGapFill, result.Source);
        }
        finally { CrowdControlGate.Reset(); }
    }

    [Fact]
    public void Casting_Only_Stun_Holds_When_No_Target_Cast_Observed()
    {
        CrowdControlGate.Enabled = true;
        try
        {
            // Same confirmed target + range, but no observed cast: the blind
            // stun must hold (v3.5 Q1).
            var result = Evaluate(Slot.Offensive, 853, Context("PALADIN", "Holy",
                range: Range(Slot.Offensive, TriState.Yes), targetCasting: TriState.No));
            Assert.Equal(PolicyVerdict.Hold, result.Verdict);
            Assert.Equal("CrowdControl", result.Provider);
            Assert.Contains("casting-only", result.Reason);
        }
        finally { CrowdControlGate.Reset(); }
    }

    [Fact]
    public void Gate_On_Aoe_Cc_Never_Opens_Out_Of_Combat()
    {
        CrowdControlGate.Enabled = true;
        try
        {
            // Ring of Peace is a silence-zone interrupt fallback (still
            // auto-eligible); an AoE CC is never an opener.
            var result = Evaluate(Slot.Offensive, 116844,
                Context("MONK", "Brewmaster"), inCombat: false);
            Assert.Equal(PolicyVerdict.Hold, result.Verdict);
            Assert.Contains("opener", result.Reason);
        }
        finally { CrowdControlGate.Reset(); }
    }

    [Fact]
    public void Gate_On_Holds_Without_A_Target()
    {
        CrowdControlGate.Enabled = true;
        try
        {
            var result = Evaluate(Slot.Offensive, 853, Context("PALADIN", "Holy"), hasTarget: false);
            Assert.Equal(PolicyVerdict.Hold, result.Verdict);
            Assert.Contains("target", result.Reason);
        }
        finally { CrowdControlGate.Reset(); }
    }

    [Fact]
    public void Gate_On_Out_Of_Range_Is_Unavailable()
    {
        CrowdControlGate.Enabled = true;
        try
        {
            var result = Evaluate(Slot.Offensive, 853, Context("PALADIN", "Holy", range: Range(Slot.Offensive, TriState.No)));
            Assert.Equal(PolicyVerdict.Unavailable, result.Verdict);
        }
        finally { CrowdControlGate.Reset(); }
    }

    [Fact]
    public void Gate_On_Unverified_Id_Is_Not_Intercepted()
    {
        CrowdControlGate.Enabled = true;
        try
        {
            var result = Evaluate(Slot.Offensive, 900020, Context());
            Assert.NotEqual("CrowdControl", result.Provider);
        }
        finally { CrowdControlGate.Reset(); }
    }

    [Fact]
    public void Gate_On_Respects_An_Explicit_User_Off()
    {
        CrowdControlGate.Enabled = true;
        try
        {
            var result = PolicyEvaluator.Evaluate(new PolicyInput
            {
                Slot = Slot.Offensive,
                SpellId = 853,
                Context = Context("PALADIN", "Holy", range: Range(Slot.Offensive, TriState.Yes)),
                Options = new PolicyOptions
                {
                    Abilities = AbilityPolicy.Default.With(853, enabled: false, defaultEnabled: true),
                },
                Memory = new PolicyMemory(),
                NowMs = Now,
                InCombat = true,
                HasTarget = true,
            }, Catalog);
            Assert.NotEqual(PolicyVerdict.Use, result.Verdict);
            Assert.NotEqual("CrowdControl", result.Provider);
        }
        finally { CrowdControlGate.Reset(); }
    }

    // ---- slot-6 (Interrupt) CC reuse — Option A ----

    [Fact]
    public void Slot6_Cc_Fires_On_The_Reused_Interrupt_Slot()
    {
        CrowdControlGate.Enabled = true;
        try
        {
            var result = Evaluate(Slot.Interrupt, 853,
                Context("PALADIN", "Holy", range: Range(Slot.Interrupt, TriState.Yes),
                    targetCasting: TriState.Yes));
            Assert.Equal(PolicyVerdict.Use, result.Verdict);
            Assert.Equal("CrowdControl", result.Provider);
            Assert.Equal(CandidateSourceKind.CompanionGapFill, result.Source);
        }
        finally { CrowdControlGate.Reset(); }
    }

    [Fact]
    public void Slot6_NonStun_Cc_Is_Manual_And_Never_AutoFires()
    {
        CrowdControlGate.Enabled = true;
        try
        {
            // v4.2.0: break-on-damage CC (Fear 5246) is manual-only. It must
            // never be generated by the CC appendix, even with the toggle ON.
            var result = Evaluate(Slot.Interrupt, 5246, // Intimidating Shout (Fear)
                Context("WARRIOR", "Arms",
                    range: Range(Slot.Interrupt, TriState.Yes),
                    targetCasting: TriState.No));
            Assert.NotEqual(PolicyVerdict.Use, result.Verdict);
            Assert.Contains("manual by design", result.Reason);

            // Sanity: a real interrupt id in the same context stays held by the
            // Interrupt veto — the CC path must never widen the interrupt slot.
            var kick = Evaluate(Slot.Interrupt, 96231, // Rebuke
                Context("PALADIN", "Holy",
                    range: Range(Slot.Interrupt, TriState.Yes),
                    targetCasting: TriState.No));
            Assert.NotEqual(PolicyVerdict.Use, kick.Verdict);
            Assert.NotEqual("CrowdControl", kick.Provider);
        }
        finally { CrowdControlGate.Reset(); }
    }

    [Fact]
    public void Slot6_MaxDps_Owned_Stun_Is_Casting_Only_Candidate_And_Shockwave_Demoted()
    {
        // v3.5: Storm Bolt 107570 is auto-eligible and rides the generated
        // per-spec cc list. It is a MaxDps-owned rotation row (registry
        // Incomplete, Purpose Rotational), so the companion keeps it on the
        // delegated interrupt path — casting-only by construction
        // (InterruptVetoes holds/Skips when no cast is observed).
        Assert.Contains(107570, Catalog.CrowdControlGapFill("WARRIOR", "Arms"));
        // v3.6 demotes Shockwave 46968 to Suggest: no enemy-count signal, so
        // the AoE stun cannot be proven safe in M+/cleave (as Leg Sweep).
        Assert.DoesNotContain(46968, Catalog.CrowdControlGapFill("WARRIOR", "Arms"));
        Assert.False(Catalog.CrowdControlFor("WARRIOR", "Arms", 46968)!.AutoEligible);

        CrowdControlGate.Enabled = true;
        try
        {
            // No observed cast -> the stun does not fire (never a blind stun).
            var held = Evaluate(Slot.Interrupt, 107570,
                Context("WARRIOR", "Arms", range: Range(Slot.Interrupt, TriState.Yes),
                    targetCasting: TriState.No));
            Assert.NotEqual(PolicyVerdict.Use, held.Verdict);
            Assert.Contains("cast", held.Reason);

            // A confirmed cast lets the delegated interrupt path fire it.
            var fired = Evaluate(Slot.Interrupt, 107570,
                Context("WARRIOR", "Arms", range: Range(Slot.Interrupt, TriState.Yes),
                    targetCasting: TriState.Yes));
            Assert.Equal(PolicyVerdict.Use, fired.Verdict);
            Assert.NotEqual("CrowdControl", fired.Provider);
        }
        finally { CrowdControlGate.Reset(); }
    }

    [Fact]
    public void Cc_GapFill_Emits_Only_AutoEligible_InterruptFallback_Ids()
    {
        (string Class, string Spec)[] classes =
        [
            ("DEATHKNIGHT", "Blood"), ("DEMONHUNTER", "Havoc"), ("DRUID", "Balance"),
            ("EVOKER", "Devastation"), ("HUNTER", "Beast Mastery"), ("MAGE", "Fire"),
            ("MONK", "Windwalker"), ("PALADIN", "Retribution"), ("PRIEST", "Shadow"),
            ("ROGUE", "Assassination"), ("SHAMAN", "Elemental"), ("WARLOCK", "Affliction"),
            ("WARRIOR", "Arms"),
        ];
        foreach (var (className, spec) in classes)
        {
            var ids = Catalog.CrowdControlGapFill(className, spec);
            Assert.Equal(ids.Length, ids.Distinct().Count());
            foreach (var id in ids)
            {
                var entry = Catalog.CrowdControlFor(className, spec, id);
                Assert.NotNull(entry);
                Assert.True(entry!.AutoEligible);
                Assert.True(entry.Kind is CcKind.Stun or CcKind.Silence);
                Assert.False(entry.BreaksOnDamage);
                Assert.Equal(CcRole.InterruptFallback, entry.Role);
            }
        }

        // Rogue has exactly one automatic row: Kidney Shot. Blind/Gouge/Sap
        // are manual by design, no matter what the global CC toggle says.
        Assert.Equal(new[] { 408 }, Catalog.CrowdControlGapFill("ROGUE", "Assassination"));
        Assert.False(Catalog.CrowdControlFor("ROGUE", "Assassination", 2094)!.AutoEligible);
        Assert.False(Catalog.CrowdControlFor("ROGUE", "Assassination", 1776)!.AutoEligible);
        Assert.False(Catalog.CrowdControlFor("ROGUE", "Assassination", 6770)!.AutoEligible);
    }

    // ---- DR / anti-chain memory ----

    [Fact]
    public void Second_Stun_At_50_Percent_Is_Allowed_When_Useful()
    {
        CrowdControlGate.Enabled = true;
        var memory = new CrowdControlDiminishing();
        var saved = CrowdControlVetoes.Memory;
        CrowdControlVetoes.Memory = memory;
        try
        {
            memory.NoteUse(CcDrCategory.Stun, Now, 6_000); // Hammer of Justice
            var state = memory.Evaluate(CcDrCategory.Stun, Now + 1, 6_000, pvp: true);
            Assert.Equal(1, state.Applications);
            Assert.Equal(3_000, state.ExpectedDurationMs);
            Assert.False(state.Immune);
            var result = Evaluate(Slot.Offensive, 853, Context("PALADIN", "Holy",
                range: Range(Slot.Offensive, TriState.Yes), targetCasting: TriState.Yes));
            Assert.Equal(PolicyVerdict.Use, result.Verdict);
        }
        finally { CrowdControlVetoes.Memory = saved; CrowdControlGate.Reset(); }
    }

    [Fact]
    public void Third_Stun_Is_Held_After_Two_Applications()
    {
        CrowdControlGate.Enabled = true;
        var memory = new CrowdControlDiminishing();
        var saved = CrowdControlVetoes.Memory;
        CrowdControlVetoes.Memory = memory;
        try
        {
            memory.NoteUse(CcDrCategory.Stun, Now, 6_000);
            memory.NoteUse(CcDrCategory.Stun, Now + 1_000, 6_000);
            var result = Evaluate(Slot.Offensive, 853, Context("PALADIN", "Holy",
                range: Range(Slot.Offensive, TriState.Yes), targetCasting: TriState.Yes));
            Assert.Equal(PolicyVerdict.Hold, result.Verdict);
            Assert.Contains("immune", result.Reason);
        }
        finally { CrowdControlVetoes.Memory = saved; CrowdControlGate.Reset(); }
    }

    [Fact]
    public void Dr_Ladder_Halves_And_Resets_Twenty_Seconds_After_The_End()
    {
        var memory = new CrowdControlDiminishing();
        var first = memory.Evaluate(CcDrCategory.Stun, Now, 2_000, pvp: false);
        Assert.Equal(0, first.Applications);
        Assert.Equal(2_000, first.ExpectedDurationMs);

        memory.NoteUse(CcDrCategory.Stun, Now, 2_000, pvp: false);
        var second = memory.Evaluate(CcDrCategory.Stun, Now + 1, 2_000, pvp: false);
        Assert.Equal(1, second.Applications);
        Assert.Equal(1_000, second.ExpectedDurationMs);
        Assert.False(second.Immune); // PvE is never fully immune since 12.0

        memory.NoteUse(CcDrCategory.Stun, Now + 1, 2_000, pvp: false);
        var third = memory.Evaluate(CcDrCategory.Stun, Now + 2, 2_000, pvp: false);
        Assert.Equal(2, third.Applications);
        Assert.Equal(500, third.ExpectedDurationMs);
        Assert.True(third.TooWeak(CrowdControlDiminishing.MinimumUsefulStunMs));

        // The second 1 s stun ends at Now+1001; the window then lasts 20 s.
        Assert.Equal(2, memory.Evaluate(CcDrCategory.Stun, Now + 21_000, 2_000, pvp: false).Applications);
        Assert.Equal(0, memory.Evaluate(CcDrCategory.Stun, Now + 21_001, 2_000, pvp: false).Applications);

        Assert.False(memory.Evaluate(CcDrCategory.Root, Now + 1, 2_000, pvp: true).Immune);
        Assert.False(memory.Evaluate(CcDrCategory.Unknown, Now + 1, 2_000, pvp: true).Immune);
    }

    [Fact]
    public void Rogue_BreakOnDamage_Cc_Is_Manual_Only()
    {
        var blind = Catalog.CrowdControlFor("ROGUE", "Assassination", 2094)!;
        var gouge = Catalog.CrowdControlFor("ROGUE", "Assassination", 1776)!;
        var sap = Catalog.CrowdControlFor("ROGUE", "Assassination", 6770)!;

        Assert.Equal(CcRole.Manual, blind.Role);
        Assert.Equal(CcRole.Manual, gouge.Role);
        Assert.Equal(CcRole.Manual, sap.Role);
        Assert.True(blind.BreaksOnDamage);
        Assert.True(gouge.BreaksOnDamage);
        Assert.True(sap.BreaksOnDamage);
        Assert.True(sap.RequiresStealth);
        Assert.True(sap.RequiresOutOfCombat);

        CrowdControlGate.Enabled = true;
        try
        {
            foreach (var id in new[] { 2094, 1776, 6770 })
            {
                var decision = Evaluate(Slot.Interrupt, id,
                    Context("ROGUE", "Assassination",
                        range: Range(Slot.Interrupt, TriState.Yes),
                        targetCasting: TriState.No));
                Assert.NotEqual(PolicyVerdict.Use, decision.Verdict);
            }
        }
        finally { CrowdControlGate.Reset(); }
    }

    [Fact]
    public void Stun_Ineffective_Latch_Blocks_A_Second_Stun()
    {
        CrowdControlGate.Enabled = true;
        CrowdControlVetoes.NoteStunIneffective();
        try
        {
            var decision = Evaluate(Slot.Interrupt, 408,
                Context("ROGUE", "Assassination",
                    range: Range(Slot.Interrupt, TriState.Yes),
                    targetCasting: TriState.Yes));
            Assert.Equal(PolicyVerdict.Hold, decision.Verdict);
            Assert.Contains("ineffective", decision.Reason);
        }
        finally { CrowdControlVetoes.ClearStunIneffective(); CrowdControlGate.Reset(); }
    }

    [Fact]
    public void Kidney_Shot_Delegated_Path_Uses_The_Dr_Ladder()
    {
        CrowdControlGate.Enabled = true;
        var memory = new CrowdControlDiminishing();
        var saved = CrowdControlVetoes.Memory;
        CrowdControlVetoes.Memory = memory;
        try
        {
            var context = Context("ROGUE", "Assassination",
                range: Range(Slot.Interrupt, TriState.Yes),
                targetCasting: TriState.Yes);
            var entry = Catalog.CrowdControlFor("ROGUE", "Assassination", 408)!;
            Assert.True(entry.AutoEligible);
            Assert.Equal(2_000, entry.BaseDurationMs);

            var first = Evaluate(Slot.Interrupt, 408, context);
            Assert.Equal(PolicyVerdict.Use, first.Verdict); // 1st: full 2 s, recorded
            Assert.Equal(1, memory.Evaluate(CcDrCategory.Stun, Now, 2_000).Applications);

            var second = Evaluate(Slot.Interrupt, 408, context);
            Assert.Equal(PolicyVerdict.Use, second.Verdict); // 2nd: 1 s, recorded
            Assert.Equal(2, memory.Evaluate(CcDrCategory.Stun, Now, 2_000).Applications);

            var third = Evaluate(Slot.Interrupt, 408, context);
            Assert.Equal(PolicyVerdict.Hold, third.Verdict); // 3rd: PvP immune
            Assert.Contains("immune", third.Reason);
        }
        finally { CrowdControlVetoes.Memory = saved; CrowdControlGate.Reset(); }
    }

    // ---- coverage ----

    [Fact]
    public void Catalog_Covers_Every_Class_With_A_Curated_Cc_Entry()
    {
        (string Class, string Spec)[] classes =
        [
            ("DEATHKNIGHT", "Blood"), ("DEMONHUNTER", "Havoc"), ("DRUID", "Balance"),
            ("EVOKER", "Devastation"), ("HUNTER", "Beast Mastery"), ("MAGE", "Fire"),
            ("MONK", "Windwalker"), ("PALADIN", "Retribution"), ("PRIEST", "Shadow"),
            ("ROGUE", "Assassination"), ("SHAMAN", "Elemental"), ("WARLOCK", "Affliction"),
            ("WARRIOR", "Arms"),
        ];
        foreach (var (className, spec) in classes)
        {
            var entries = CrowdControlCatalog.For(className, spec);
            Assert.NotEmpty(entries);
            // Automatic rows are interrupt fallbacks only; every other row is
            // manual and never generated by the companion.
            foreach (var entry in entries.Where(e => e.AutoEligible))
            {
                Assert.True(entry.Kind is CcKind.Stun or CcKind.Silence);
                Assert.False(entry.BreaksOnDamage);
            }
        }
    }

    [Fact]
    public void Every_Curated_Cc_Id_Resolves_In_The_Catalog()
    {
        foreach (var id in CrowdControlCatalog.AllIds())
            Assert.NotNull(Catalog.TryGet(id));
    }

    [Fact]
    public void MaxDps_Owned_Stuns_Are_Auto_Eligible_Casting_Only()
    {
        // v3.5/v3.6: Storm Bolt is a rotation row on MaxDps authority but rides
        // the curated CC list; the casting-only gate (Stun kind) keeps it from
        // ever firing as a blind stun. Shockwave is demoted back to Suggest
        // (no enemy-count signal) and must not ride the list.
        var bolt = Catalog.CrowdControlFor("WARRIOR", "Arms", 107570)!;
        var wave = Catalog.CrowdControlFor("WARRIOR", "Arms", 46968)!;
        Assert.True(bolt.AutoEligible);
        Assert.False(wave.AutoEligible);
        Assert.Equal(CcKind.Stun, bolt.Kind);
        Assert.Equal(CcKind.Stun, wave.Kind);
    }

    // ---- v3.6 all-class auto-fire ------------------------------------------

    [Theory]
    [InlineData(408, "ROGUE", "Assassination", "Stun")]
    [InlineData(408, "ROGUE", "Outlaw", "Stun")]
    [InlineData(408, "ROGUE", "Subtlety", "Stun")]
    [InlineData(5211, "DRUID", "Balance", "Stun")]
    [InlineData(5211, "DRUID", "Feral", "Stun")]
    [InlineData(5211, "DRUID", "Guardian", "Stun")]
    [InlineData(5211, "DRUID", "Restoration", "Stun")]
    [InlineData(19577, "HUNTER", "Beast Mastery", "Stun")]
    [InlineData(19577, "HUNTER", "Marksmanship", "Stun")]
    [InlineData(19577, "HUNTER", "Survival", "Stun")]
    [InlineData(78675, "DRUID", "Balance", "Silence")]
    [InlineData(15487, "PRIEST", "Shadow", "Silence")]
    public void V36_Flipped_And_New_Cc_Ids_Are_AutoEligible_StunOrSilence(
        int id, string className, string spec, string kind)
    {
        var entry = Catalog.CrowdControlFor(className, spec, id);
        Assert.NotNull(entry);
        Assert.True(entry!.AutoEligible);
        Assert.Equal(kind, entry.Kind.ToString());
        Assert.Contains(id, Catalog.CrowdControlGapFill(className, spec));
    }

    [Theory]
    [InlineData("WARRIOR", "Arms", 46968)]
    [InlineData("WARRIOR", "Fury", 46968)]
    [InlineData("WARRIOR", "Protection", 46968)]
    [InlineData("MONK", "Brewmaster", 119381)]
    [InlineData("MONK", "Mistweaver", 119381)]
    [InlineData("MONK", "Windwalker", 119381)]
    public void V36_Aoe_Stuns_Stay_Suggest(string className, string spec, int id)
    {
        var entry = Catalog.CrowdControlFor(className, spec, id);
        Assert.NotNull(entry);
        Assert.False(entry!.AutoEligible);
        Assert.DoesNotContain(id, Catalog.CrowdControlGapFill(className, spec));
    }

    [Theory]
    [InlineData("ROGUE", "Assassination", 1833)]
    [InlineData("ROGUE", "Subtlety", 163505)]
    [InlineData("DRUID", "Feral", 22570)]
    [InlineData("PRIEST", "Shadow", 64044)]
    [InlineData("WARLOCK", "Destruction", 30283)]
    [InlineData("MAGE", "Frost", 113724)]
    [InlineData("SHAMAN", "Elemental", 192058)]
    [InlineData("WARLOCK", "Affliction", 89766)]
    [InlineData("DEATHKNIGHT", "Unholy", 47476)]
    public void V36_NoList_Ids_Stay_Suggest_Or_Absent(string className, string spec, int id)
    {
        var entry = Catalog.CrowdControlFor(className, spec, id);
        Assert.True(entry is null || !entry.AutoEligible,
            $"{id} must not be auto-eligible (spec NO list)");
        Assert.DoesNotContain(id, Catalog.CrowdControlGapFill(className, spec));
    }

    [Fact]
    public void V36_Maim_22570_Is_Not_In_The_Cc_Catalog()
    {
        Assert.Null(CrowdControlCatalog.Find("DRUID", "Feral", 22570));
        Assert.DoesNotContain(22570, CrowdControlCatalog.AllIds());
    }

    [Fact]
    public void V36_Silence_15487_Is_Shadow_Only()
    {
        Assert.NotNull(Catalog.CrowdControlFor("PRIEST", "Shadow", 15487));
        Assert.Null(Catalog.CrowdControlFor("PRIEST", "Holy", 15487));
        Assert.Null(Catalog.CrowdControlFor("PRIEST", "Discipline", 15487));
        Assert.Null(Catalog.CrowdControlFor("WARLOCK", "Affliction", 15487));
    }

    [Fact]
    public void V36_SolarBeam_78675_Is_Balance_Only()
    {
        Assert.NotNull(Catalog.CrowdControlFor("DRUID", "Balance", 78675));
        Assert.Null(Catalog.CrowdControlFor("DRUID", "Feral", 78675));
        Assert.Null(Catalog.CrowdControlFor("DRUID", "Guardian", 78675));
        Assert.Null(Catalog.CrowdControlFor("DRUID", "Restoration", 78675));
        Assert.Null(Catalog.CrowdControlFor("SHAMAN", "Elemental", 78675));
    }

    [Fact]
    public void V36_Provider_Silence_15487_Is_Casting_Only_And_Gate_Off_Holds()
    {
        // 15487 is a curated (ResearchBacked) interrupt, so it reaches the CC
        // provider directly. Gate OFF => not fired without an observed cast.
        CrowdControlGate.Reset();
        var off = Evaluate(Slot.Interrupt, 15487,
            Context("PRIEST", "Shadow", range: Range(Slot.Interrupt, TriState.Yes),
                targetCasting: TriState.No));
        Assert.NotEqual(PolicyVerdict.Use, off.Verdict);

        CrowdControlGate.Enabled = true;
        try
        {
            var fired = Evaluate(Slot.Interrupt, 15487,
                Context("PRIEST", "Shadow", range: Range(Slot.Interrupt, TriState.Yes),
                    targetCasting: TriState.Yes));
            Assert.Equal(PolicyVerdict.Use, fired.Verdict);
            Assert.Equal("CrowdControl", fired.Provider);

            var notCasting = Evaluate(Slot.Interrupt, 15487,
                Context("PRIEST", "Shadow", range: Range(Slot.Interrupt, TriState.Yes),
                    targetCasting: TriState.No));
            Assert.Equal(PolicyVerdict.Hold, notCasting.Verdict);
            Assert.Equal("CrowdControl", notCasting.Provider);

            var unknown = Evaluate(Slot.Interrupt, 15487,
                Context("PRIEST", "Shadow", range: Range(Slot.Interrupt, TriState.Yes),
                    targetCasting: TriState.Unknown));
            Assert.Equal(PolicyVerdict.Hold, unknown.Verdict);
        }
        finally { CrowdControlGate.Reset(); }
    }

    [Theory]
    [InlineData(408, "ROGUE", "Assassination")]
    [InlineData(5211, "DRUID", "Balance")]
    [InlineData(19577, "HUNTER", "Beast Mastery")]
    [InlineData(78675, "DRUID", "Balance")]
    public void V36_Delegated_Cc_Ids_Are_Casting_Only_In_Slot6(int id, string className, string spec)
    {
        // These class-spell rows are registry Incomplete, so the companion CC
        // provider is not reached: the generated cc list offers them on slot 6
        // and the delegated interrupt path keeps them casting-only (never a
        // blind stun). Gate OFF must likewise never fire without a live cast.
        var ability = Catalog.TryGet(id);
        Assert.NotNull(ability);
        Assert.Equal(IntelligenceStatus.Incomplete, ability!.Status);
        Assert.Contains(id, Catalog.CrowdControlGapFill(className, spec));

        CrowdControlGate.Reset();
        var off = Evaluate(Slot.Interrupt, id,
            Context(className, spec, range: Range(Slot.Interrupt, TriState.Yes),
                targetCasting: TriState.No));
        Assert.NotEqual(PolicyVerdict.Use, off.Verdict);

        CrowdControlGate.Enabled = true;
        try
        {
            var held = Evaluate(Slot.Interrupt, id,
                Context(className, spec, range: Range(Slot.Interrupt, TriState.Yes),
                    targetCasting: TriState.No));
            Assert.NotEqual(PolicyVerdict.Use, held.Verdict);
            Assert.Contains("cast", held.Reason);

            var fired = Evaluate(Slot.Interrupt, id,
                Context(className, spec, range: Range(Slot.Interrupt, TriState.Yes),
                    targetCasting: TriState.Yes));
            Assert.Equal(PolicyVerdict.Use, fired.Verdict);
        }
        finally { CrowdControlGate.Reset(); }
    }

    [Fact]
    public void Dr_Categories_Are_Documented_Per_Entry()
    {
        Assert.Equal(CcDrCategory.Stun, Catalog.CrowdControlFor("PALADIN", "Holy", 853)!.Dr);
        Assert.Equal(CcDrCategory.Root, Catalog.CrowdControlFor("DRUID", "Balance", 339)!.Dr);
        Assert.Equal(CcDrCategory.Disorient, Catalog.CrowdControlFor("MAGE", "Fire", 118)!.Dr);
    }

    // ---- companion settings + UI ----

    [Fact]
    public void AppSettings_CrowdControl_Defaults_Off()
    {
        Assert.False(new AppSettings().CrowdControlEnabled);
        Assert.False(CrowdControlGate.Enabled);
    }

    [Fact]
    public void AppSettings_Parses_And_Publishes_The_Cc_Toggle()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mdb-cc-{Guid.NewGuid():N}.ini");
        System.IO.File.WriteAllText(path, "[CrowdControl]\nEnabled=1\n");
        try
        {
            var settings = AppSettings.Load(path);
            Assert.True(settings.CrowdControlEnabled);
            Assert.True(CrowdControlGate.Enabled);
        }
        finally
        {
            CrowdControlGate.Reset();
            try { System.IO.File.Delete(path); } catch { }
        }
    }

    // P2-E: the old CrowdControlToggle seam test asserted the WinForms
    // UserControl in Ui/SettingsPages.cs. The toggle now lives on the frozen
    // ToggleModel (same AppSettings key + CrowdControlGate publish), so the
    // round-trip is pinned here against the model instead of a control.
    [Fact]
    public void AppSettings_Parses_BreakGuard_And_Manual_Hold()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mdb-ccguard-{Guid.NewGuid():N}.ini");
        System.IO.File.WriteAllText(path,
            "[CrowdControl]\nEnabled=1\nBreakGuard=0\nManualHoldMs=12000\nHoldHotkey=F9\n");
        try
        {
            var settings = AppSettings.Load(path);
            Assert.True(settings.CrowdControlEnabled);
            Assert.False(settings.CcBreakGuardEnabled);
            Assert.Equal(12_000, settings.CcManualHoldMs);
            Assert.Equal("F9", settings.CcHoldHotkey);
        }
        finally
        {
            CrowdControlGate.Reset();
            try { System.IO.File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void CrowdControlToggle_Model_RoundTrips_The_Existing_Setting()
    {
        var settings = new AppSettings { CrowdControlEnabled = true };
        var model = ToggleModel.FromSettings(settings);
        Assert.True(model.CrowdControl);
        Assert.True(CrowdControlGate.Enabled);

        model.CrowdControl = false;
        model.ApplyToSettings(settings);

        Assert.False(settings.CrowdControlEnabled);
        Assert.False(CrowdControlGate.Enabled);
    }
}


