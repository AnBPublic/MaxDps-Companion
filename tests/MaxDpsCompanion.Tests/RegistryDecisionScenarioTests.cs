using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Canonical v2.6 registry-decision scenarios: the five-state policy over the
/// embedded registry, the user ability policy (ON/OFF + richer modes), the
/// registry enforcement for the modeled class-spell tail and the interrupt
/// matrix.
/// </summary>
public class RegistryDecisionScenarioTests
{
    private const long Now = 10_000;
    private const int Pummel = 6552;
    private const int Recklessness = 1719;
    private const int Vanish = 1856;
    private const int ImpendingVictory = 202168;
    private const int Backstab = 53;         // class-spell tail: Incomplete / MaxDpsOnly

    private static AbilityCatalog Catalog => AbilityCatalog.Default;

    private static CombatContext Context(
        bool hpValid = true,
        int hp = 100,
        PlayerCastState cast = PlayerCastState.None,
        TriState targetCasting = TriState.No,
        TriState targetInterruptible = TriState.Unknown,
        TriState[]? range = null,
        DefensiveUrgency urgency = DefensiveUrgency.Unknown) => new()
    {
        HpValid = hpValid,
        HpPct = hp,
        Cast = cast,
        TargetCasting = targetCasting,
        TargetCastInterruptible = targetInterruptible,
        TargetInMelee = TriState.Unknown,
        SlotRange = range ?? new TriState[PixelProtocol.SlotCount],
        SlotBuffActive = new TriState[PixelProtocol.SlotCount],
        DefensiveUrgency = urgency,
        ContextValid = true,
    };

    private static TriState[] Range(params (Slot Slot, TriState State)[] entries)
    {
        var range = new TriState[PixelProtocol.SlotCount];
        foreach (var (slot, state) in entries) range[(int)slot] = state;
        return range;
    }

    private static PolicyDecision Evaluate(
        Slot slot,
        int spellId,
        CombatContext? context = null,
        PolicyOptions? options = null,
        bool hasTarget = true) =>
        PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = slot,
            SpellId = spellId,
            Context = context ?? Context(),
            Options = options ?? PolicyOptions.Standard,
            Memory = new PolicyMemory(),
            NowMs = Now,
            InCombat = true,
            HasTarget = hasTarget,
        }, Catalog);

    // ---- normal-mode emergency self-sustain --------------------------------

    [Fact]
    public void Normal_Mode_Emergency_Self_Heal_Fires()
    {
        // NEW v2.6: normal mode (Solo OFF) at/below emergency HP fires the
        // self-sustain candidate as an emergency survival action.
        var result = Evaluate(Slot.SelfHeal, ImpendingVictory, Context(hp: 30));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.True(result.Emergency);
    }

    [Fact]
    public void Normal_Mode_Above_Emergency_Holds_The_Self_Heal()
    {
        var result = Evaluate(Slot.SelfHeal, ImpendingVictory, Context(hp: 50));
        Assert.Equal(PolicyVerdict.Hold, result.Verdict);
        Assert.Contains("above emergency", result.Reason);
    }

    // ---- user policy is absolute -------------------------------------------

    [Fact]
    public void User_Off_Beats_Red_Urgency_And_Emergency_Hp()
    {
        var options = new PolicyOptions
        {
            SoloEnabled = true,
            Abilities = AbilityPolicy.Default.With(Vanish, enabled: false, defaultEnabled: true),
        };
        var result = Evaluate(Slot.Defensive, Vanish,
            Context(hp: 20, urgency: DefensiveUrgency.Red), options);
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
        Assert.Equal("user policy disabled", result.Reason);
    }

    [Fact]
    public void User_On_Vanish_Is_Emergency_Only_Not_A_Gap_Closer()
    {
        // Vanish is an ESCAPE, not a gap-closer: the interface purpose routes
        // it through the emergency-only defensive path even when the user
        // explicitly enables it. This test pins the current evaluator
        // semantics: routine urgency holds, emergency HP uses.
        var options = new PolicyOptions
        {
            SoloEnabled = true,
            Abilities = AbilityPolicy.Default.With(Vanish, enabled: true, defaultEnabled: false),
        };

        var routine = Evaluate(Slot.Defensive, Vanish, Context(hp: 80, urgency: DefensiveUrgency.Yellow), options);
        Assert.Equal(PolicyVerdict.Hold, routine.Verdict);

        var panic = Evaluate(Slot.Defensive, Vanish, Context(hp: 20, urgency: DefensiveUrgency.Red), options);
        Assert.Equal(PolicyVerdict.Use, panic.Verdict);
        Assert.True(panic.Emergency);
    }

    // ---- registry enforcement for the modeled class-spell tail --------------

    [Fact]
    public void Incomplete_Entry_On_A_Companion_Slot_Is_Skipped()
    {
        var options = new PolicyOptions
        {
            Abilities = AbilityPolicy.Default.With(Backstab, enabled: true, defaultEnabled: true),
        };
        var result = Evaluate(Slot.SelfHeal, Backstab, Context(hp: 30), options);
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
        Assert.Contains("intelligence incomplete", result.Reason);
    }

    [Fact]
    public void Incomplete_Entry_On_A_MaxDps_Slot_Falls_Back_To_Generic()
    {
        var result = Evaluate(Slot.Main, Backstab, Context(range: Range((Slot.Main, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.Contains("MaxDps main candidate", result.Reason);
    }

    // ---- interrupt matrix ---------------------------------------------------

    [Fact]
    public void Interrupt_Without_A_Live_Cast_Skips()
    {
        var result = Evaluate(Slot.Interrupt, Pummel,
            Context(targetCasting: TriState.No, targetInterruptible: TriState.Unknown,
                range: Range((Slot.Interrupt, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
        Assert.Contains("no live target cast", result.Reason);
    }

    [Fact]
    public void Interrupt_Not_Interruptible_Skips()
    {
        var result = Evaluate(Slot.Interrupt, Pummel,
            Context(targetCasting: TriState.Yes, targetInterruptible: TriState.No,
                range: Range((Slot.Interrupt, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
        Assert.Contains("not interruptible", result.Reason);
    }

    [Fact]
    public void Interrupt_In_Range_Uses_Dedicated()
    {
        var result = Evaluate(Slot.Interrupt, Pummel,
            Context(targetCasting: TriState.Yes, targetInterruptible: TriState.Yes,
                range: Range((Slot.Interrupt, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
        Assert.Contains("interrupt (Dedicated)", result.Reason);
    }

    [Fact]
    public void Interrupt_Out_Of_Range_Is_Unavailable()
    {
        var result = Evaluate(Slot.Interrupt, Pummel,
            Context(targetCasting: TriState.Yes, targetInterruptible: TriState.Yes,
                range: Range((Slot.Interrupt, TriState.No))));
        Assert.Equal(PolicyVerdict.Unavailable, result.Verdict);
        Assert.Contains("out of ability range", result.Reason);
    }

    [Fact]
    public void Interrupt_Unknown_Range_Fails_Open_When_Curated_Use()
    {
        // 6552's curated source carries UnknownPolicy.Use: an unknown range
        // probe must not block a time-critical kick.
        Assert.Equal(UnknownPolicy.Use, Catalog.TryGet(Pummel)!.Unknown);
        var result = Evaluate(Slot.Interrupt, Pummel,
            Context(targetCasting: TriState.Yes, targetInterruptible: TriState.Yes,
                range: Range((Slot.Interrupt, TriState.Unknown))));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
    }

    [Fact]
    public void Interrupt_User_Off_Skips()
    {
        var options = new PolicyOptions
        {
            Abilities = AbilityPolicy.Default.With(Pummel, enabled: false, defaultEnabled: true),
        };
        var result = Evaluate(Slot.Interrupt, Pummel,
            Context(targetCasting: TriState.Yes, targetInterruptible: TriState.Yes,
                range: Range((Slot.Interrupt, TriState.Yes))), options);
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
        Assert.Equal("user policy disabled", result.Reason);
    }

    [Fact]
    public void Interrupt_May_Fire_During_A_Channel()
    {
        // Kicks are off-GCD by design; the execution-safety channel hold exempts
        // the interrupt slot.
        var result = Evaluate(Slot.Interrupt, Pummel,
            Context(cast: PlayerCastState.Channeling, targetCasting: TriState.Yes,
                targetInterruptible: TriState.Yes, range: Range((Slot.Interrupt, TriState.Yes))));
        Assert.Equal(PolicyVerdict.Use, result.Verdict);
    }

    // ---- ability policy modes -----------------------------------------------

    [Fact]
    public void SoloOnly_Mode_Holds_Off_Solo_And_Uses_On_Solo()
    {
        var policy = AbilityPolicy.Default.WithMode(Recklessness, UserAbilityMode.SoloOnly);
        var context = Context(range: Range((Slot.Offensive, TriState.Yes)));

        var off = Evaluate(Slot.Offensive, Recklessness, context,
            new PolicyOptions { SoloEnabled = false, Abilities = policy });
        Assert.Equal(PolicyVerdict.Hold, off.Verdict);
        Assert.Contains("solo only", off.Reason);

        var on = Evaluate(Slot.Offensive, Recklessness, context,
            new PolicyOptions { SoloEnabled = true, Abilities = policy });
        Assert.Equal(PolicyVerdict.Use, on.Verdict);
    }

    [Fact]
    public void Manual_Mode_Skips_With_Its_Own_Reason()
    {
        var options = new PolicyOptions
        {
            Abilities = AbilityPolicy.Default.WithMode(Recklessness, UserAbilityMode.Manual),
        };
        var result = Evaluate(Slot.Offensive, Recklessness,
            Context(range: Range((Slot.Offensive, TriState.Yes))), options);
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
        Assert.Equal("user policy: manual", result.Reason);
    }

    [Fact]
    public void Never_Mode_Skips_With_The_Disabled_Reason()
    {
        var options = new PolicyOptions
        {
            Abilities = AbilityPolicy.Default.WithMode(Recklessness, UserAbilityMode.Never),
        };
        var result = Evaluate(Slot.Offensive, Recklessness,
            Context(range: Range((Slot.Offensive, TriState.Yes))), options);
        Assert.Equal(PolicyVerdict.Skip, result.Verdict);
        Assert.Equal("user policy disabled", result.Reason);
    }

    [Fact]
    public void Modes_Encode_And_Parse_Round_Trip()
    {
        var policy = AbilityPolicy.Default
            .WithMode(6552, UserAbilityMode.SoloOnly)
            .WithMode(871, UserAbilityMode.Manual)
            .WithMode(1719, UserAbilityMode.Never);

        Assert.Equal("871:Manual,1719:Never,6552:SoloOnly", policy.EncodeModes());

        var parsed = AbilityPolicy.FromParts(policy.EncodeOn(), policy.EncodeOff(), policy.EncodeModes());
        Assert.Equal(UserAbilityMode.SoloOnly, parsed.ModeOf(6552));
        Assert.Equal(UserAbilityMode.Manual, parsed.ModeOf(871));
        Assert.Equal(UserAbilityMode.Never, parsed.ModeOf(1719));
        Assert.Equal(UserAbilityMode.Default, parsed.ModeOf(12345));

        // Clearing a mode removes it from the encoding.
        Assert.Equal("871:Manual,1719:Never", policy.WithMode(6552, UserAbilityMode.Default).EncodeModes());
    }

    [Fact]
    public void AppSettings_Persists_Richer_Ability_Modes()
    {
        // Wired in v2.6: [Abilities] Modes=id:Mode round-trips through
        // AppSettings.Load/Save without disturbing the On/Off sets.
        var path = Path.Combine(Path.GetTempPath(), $"mdb-modes-{Guid.NewGuid():N}.ini");
        try
        {
            File.WriteAllText(path, "[Abilities]\nOn=202168\nOff=871\nModes=6552:SoloOnly,1719:Manual\n");
            var settings = AppSettings.Load(path);
            Assert.Equal(UserAbilityMode.SoloOnly, settings.Abilities.ModeOf(6552));
            Assert.Equal(UserAbilityMode.Manual, settings.Abilities.ModeOf(1719));
            Assert.Contains(202168, settings.Abilities.ExplicitOn);
            Assert.Contains(871, settings.Abilities.ExplicitOff);

            settings.Save();
            var reloaded = AppSettings.Load(path);
            Assert.Equal(UserAbilityMode.SoloOnly, reloaded.Abilities.ModeOf(6552));
            Assert.Equal(UserAbilityMode.Manual, reloaded.Abilities.ModeOf(1719));
            Assert.Contains(202168, reloaded.Abilities.ExplicitOn);
            Assert.Contains(871, reloaded.Abilities.ExplicitOff);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }

        var parsed = AbilityPolicy.FromParts(on: null, off: null, modes: "6552:SoloOnly");
        Assert.Equal(UserAbilityMode.SoloOnly, parsed.ModeOf(6552));
    }
}
