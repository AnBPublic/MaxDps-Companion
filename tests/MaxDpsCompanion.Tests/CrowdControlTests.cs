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
            var result = Evaluate(Slot.Offensive, 5246, Context(), inCombat: false); // Intimidating Shout
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
    public void Slot6_NonStun_Cc_Bypasses_Interrupt_Vetoes_When_No_Interrupt_Is_Pending()
    {
        CrowdControlGate.Enabled = true;
        try
        {
            // A computed-but-no-cast context: a real interrupt on slot 6 would be
            // vetoed ("no live target cast"), but a non-Stun/Silence CC (Fear,
            // 5246) is not casting-only and must still fire on a confirmed target
            // (the bridge only offers CC after MaxDps names no usable interrupt).
            var result = Evaluate(Slot.Interrupt, 5246, // Intimidating Shout (Fear)
                Context("WARRIOR", "Arms",
                    range: Range(Slot.Interrupt, TriState.Yes),
                    targetCasting: TriState.No));
            Assert.Equal(PolicyVerdict.Use, result.Verdict);
            Assert.Equal("CrowdControl", result.Provider);

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
    public void Slot6_MaxDps_Owned_Stuns_Are_Casting_Only_Candidates()
    {
        // v3.5: Storm Bolt 107570 and Shockwave 46968 are now auto-eligible and
        // ride the generated per-spec cc list. They are MaxDps-owned rotation
        // rows (registry Incomplete, Purpose Rotational), so the companion
        // keeps them on the delegated interrupt path — which is casting-only by
        // construction (InterruptVetoes holds when no cast is observed).
        Assert.Contains(107570, Catalog.CrowdControlGapFill("WARRIOR", "Arms"));
        Assert.Contains(46968, Catalog.CrowdControlGapFill("WARRIOR", "Arms"));
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
    public void Cc_GapFill_Emits_Only_AutoEligible_Ids_For_Every_Class()
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
            Assert.NotEmpty(ids);
            Assert.Equal(ids.Length, ids.Distinct().Count());
            foreach (var id in ids)
            {
                var entry = Catalog.CrowdControlFor(className, spec, id);
                Assert.NotNull(entry);
                Assert.True(entry!.AutoEligible);
            }
        }
    }

    // ---- DR / anti-chain memory ----

    [Fact]
    public void Same_Dr_Category_Is_Not_Auto_Chained()
    {
        CrowdControlGate.Enabled = true;
        var memory = new CrowdControlDiminishing();
        var saved = CrowdControlVetoes.Memory;
        CrowdControlVetoes.Memory = memory;
        try
        {
            memory.NoteUse(CcDrCategory.Stun, Now); // e.g. a stun was just applied
            var result = Evaluate(Slot.Offensive, 853, Context("PALADIN", "Holy",
                range: Range(Slot.Offensive, TriState.Yes), targetCasting: TriState.Yes));
            Assert.Equal(PolicyVerdict.Hold, result.Verdict);
            Assert.Contains("DR", result.Reason);
        }
        finally { CrowdControlVetoes.Memory = saved; CrowdControlGate.Reset(); }
    }

    [Fact]
    public void Dr_Memory_Fails_Open_After_The_Window()
    {
        var memory = new CrowdControlDiminishing();
        memory.NoteUse(CcDrCategory.Stun, Now);
        Assert.True(memory.IsDiminished(CcDrCategory.Stun, Now + 1));
        Assert.False(memory.IsDiminished(CcDrCategory.Stun, Now + CrowdControlDiminishing.DefaultWindowMs));
        Assert.False(memory.IsDiminished(CcDrCategory.Root, Now + 1)); // different category
        Assert.False(memory.IsDiminished(CcDrCategory.Unknown, Now + 1)); // unknown fails open
    }

    // ---- coverage ----

    [Fact]
    public void Catalog_Covers_Every_Class_With_An_Auto_Eligible_Entry()
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
            Assert.Contains(entries, e => e.AutoEligible);
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
        // v3.5: Storm Bolt / Shockwave are rotation rows on MaxDps authority
        // but now ride the curated CC list; the casting-only gate (Stun kind)
        // keeps them from ever firing as a blind stun.
        var bolt = Catalog.CrowdControlFor("WARRIOR", "Arms", 107570)!;
        var wave = Catalog.CrowdControlFor("WARRIOR", "Arms", 46968)!;
        Assert.True(bolt.AutoEligible);
        Assert.True(wave.AutoEligible);
        Assert.Equal(CcKind.Stun, bolt.Kind);
        Assert.Equal(CcKind.Stun, wave.Kind);
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

    [Fact]
    public void CrowdControlToggle_RoundTrips_The_Existing_Setting()
    {
        var toggle = new CrowdControlToggle();
        var settings = new AppSettings { CrowdControlEnabled = true };
        toggle.LoadFrom(settings);
        Assert.True(toggle.ToggleForTest.Checked);

        toggle.ToggleForTest.Checked = false;
        Assert.False(toggle.ApplyTo(settings));
        Assert.False(settings.CrowdControlEnabled);
    }
}


