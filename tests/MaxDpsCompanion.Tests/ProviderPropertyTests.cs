using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Property-style invariants over the whole ability catalog (v2.7 §52):
/// every evaluation carries provider identity + evidence, a user OFF never
/// yields USE, manual-by-design entries never fire by default (and manual
/// utility never fires even when explicitly enabled), unobservable
/// intelligence never yields USE on a companion-owned path, unknown context
/// keeps the documented companion-only fail-safe, and no evidence string
/// ever mentions a secret.
/// </summary>
public class ProviderPropertyTests
{
    private const long Now = 10_000;

    private static AbilityCatalog Catalog => AbilityCatalog.Default;

    private static Slot SlotFor(AbilityCategory category) => category switch
    {
        AbilityCategory.Main => Slot.Main,
        AbilityCategory.Offensive => Slot.Offensive,
        AbilityCategory.Defensive => Slot.Defensive,
        AbilityCategory.Interrupt => Slot.Interrupt,
        AbilityCategory.Consumable => Slot.Consumable,
        AbilityCategory.Trinket => Slot.Trinket,
        AbilityCategory.Mobility => Slot.Mobility,
        AbilityCategory.SelfHeal => Slot.SelfHeal,
        _ => Slot.Offensive,
    };

    /// <summary>A context engineered to satisfy every automatic-use gate.</summary>
    private static CombatContext Ready()
    {
        var range = new TriState[PixelProtocol.SlotCount];
        for (var i = 0; i < range.Length; i++) range[i] = TriState.Yes;
        var buffs = new TriState[PixelProtocol.SlotCount];
        for (var i = 0; i < buffs.Length; i++) buffs[i] = TriState.No;
        return new CombatContext
        {
            HpValid = true,
            HpPct = 20,
            Cast = PlayerCastState.None,
            TargetCasting = TriState.Yes,
            TargetCastInterruptible = TriState.Yes,
            TargetInMelee = TriState.No,
            SlotRange = range,
            SlotBuffActive = buffs,
            ContextValid = true,
            DefensiveUrgency = DefensiveUrgency.Red,
            StaggerUrgency = DefensiveUrgency.Red,
            DefensiveCatalogSource = false,
        };
    }

    private static PolicyDecision Evaluate(AbilityDefinition ability, PolicyOptions options, CombatContext ctx, bool hasTarget = true)
    {
        var slot = SlotFor(ability.Category);
        return PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = slot,
            SpellId = ability.SpellId,
            Context = ctx,
            Options = options,
            Memory = new PolicyMemory(),
            NowMs = Now,
            InCombat = true,
            HasTarget = hasTarget,
        }, Catalog);
    }

    private static readonly PolicyOptions SoloReady = new() { SoloEnabled = true };

    // ---- (a) every ability yields a provider set --------------------------

    [Fact]
    public void Every_Ability_Yields_A_Provider()
    {
        var missing = new List<int>();
        foreach (var ability in Catalog.All)
        {
            var decision = Evaluate(ability, SoloReady, Ready());
            if (string.IsNullOrEmpty(decision.Provider)) missing.Add(ability.SpellId);
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void Every_Ability_Yields_Evidence_On_Compiler_Paths()
    {
        // Every decision produced by the policy carries at least an empty
        // list (never null) so telemetry/UI can render it safely.
        foreach (var ability in Catalog.All)
        {
            var decision = Evaluate(ability, SoloReady, Ready());
            Assert.NotNull(decision.Evidence);
        }
    }

    // ---- (b) user OFF never yields USE ------------------------------------

    [Fact]
    public void User_Off_Never_Yields_Use()
    {
        foreach (var ability in Catalog.All)
        {
            var options = new PolicyOptions
            {
                SoloEnabled = true,
                Abilities = AbilityPolicy.Default.With(ability.SpellId, enabled: false, defaultEnabled: true),
            };
            var decision = Evaluate(ability, options, Ready());
            Assert.NotEqual(PolicyVerdict.Use, decision.Verdict);
        }
    }

    // ---- (c) manual-by-design ---------------------------------------------

    [Fact]
    public void Manual_By_Design_Never_Uses_By_Default()
    {
        var offenders = new List<string>();
        foreach (var ability in Catalog.All)
        {
            if (!ability.ManualByDesign) continue;
            var decision = Evaluate(ability, SoloReady, Ready());
            if (decision.Verdict == PolicyVerdict.Use) offenders.Add($"{ability.SpellId} {ability.Name}");
        }
        Assert.Empty(offenders);
    }

    [Fact]
    public void Manual_Utility_Never_Uses_Even_When_Explicitly_Enabled()
    {
        // The pinned semantics: a curated NeverAutomatic default is not a veto
        // for self-heals/defensives (an explicit ON may make them eligible).
        // Manual UTILITY (CC/purge/dispel/threat) has no observable trigger and
        // is structurally incapable of USE, even when enabled.
        var offenders = new List<string>();
        foreach (var ability in Catalog.All)
        {
            var manualUtility = ability.Category == AbilityCategory.Utility
                || ability.Purpose is AbilityPurpose.Dispel or AbilityPurpose.CrowdControl
                    or AbilityPurpose.Purge or AbilityPurpose.Threat;
            if (!manualUtility) continue;

            var options = new PolicyOptions
            {
                SoloEnabled = true,
                Abilities = AbilityPolicy.Default.With(ability.SpellId, enabled: true, defaultEnabled: false),
            };
            var decision = Evaluate(ability, options, Ready());
            if (decision.Verdict == PolicyVerdict.Use) offenders.Add($"{ability.SpellId} {ability.Name}");
        }
        Assert.Empty(offenders);
    }

    // ---- (d) unobservable intelligence never Uses on an autonomous path ----

    [Fact]
    public void Unobservable_Intelligence_Never_Uses_On_Autonomous_Path()
    {
        var offenders = new List<string>();
        foreach (var ability in Catalog.All)
        {
            if (ability.Automation != AutomationContext.Autonomous) continue;
            if (ability.Status is not (IntelligenceStatus.Incomplete
                or IntelligenceStatus.Unknown or IntelligenceStatus.UnsafeToAutomate)) continue;
            var decision = Evaluate(ability, SoloReady, Ready());
            if (decision.Verdict == PolicyVerdict.Use) offenders.Add($"{ability.SpellId} {ability.Name}");
        }
        Assert.Empty(offenders);
    }

    // ---- (e) unknown context keeps the documented fail-safe ----------------

    [Fact]
    public void Unknown_Context_Never_Uses_Companion_Only_Categories()
    {
        var unknown = CombatContext.Unknown();
        // Mobility (gap closer) cannot prove movement is useful -> not Use.
        var mobility = PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = Slot.Mobility,
            SpellId = 36554,
            Context = unknown,
            Options = SoloReady,
            Memory = new PolicyMemory(),
            NowMs = Now,
            InCombat = true,
            HasTarget = true,
        }, Catalog);
        Assert.NotEqual(PolicyVerdict.Use, mobility.Verdict);

        // Self-heal without a readable HP conserves -> not Use.
        var selfHeal = PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = Slot.SelfHeal,
            SpellId = 202168,
            Context = unknown,
            Options = SoloReady,
            Memory = new PolicyMemory(),
            NowMs = Now,
            InCombat = true,
            HasTarget = true,
        }, Catalog);
        Assert.NotEqual(PolicyVerdict.Use, selfHeal.Verdict);
    }

    [Fact]
    public void Unknown_Context_Interrupt_Fails_Open_By_Current_Contract()
    {
        // The bridge only suggests a kick while MaxDps flags a live cast, and
        // a v4/v1 frame has no sensor block: the interrupt fails open (USE).
        // This is the documented existing contract, pinned here so a future
        // change is deliberate.
        var unknown = CombatContext.Unknown();
        var interrupt = PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = Slot.Interrupt,
            SpellId = 6552,
            Context = unknown,
            Options = PolicyOptions.Standard,
            Memory = new PolicyMemory(),
            NowMs = Now,
            InCombat = true,
            HasTarget = true,
        }, Catalog);
        Assert.Equal(PolicyVerdict.Use, interrupt.Verdict);
    }

    // ---- (f) evidence is secret-free ---------------------------------------

    [Fact]
    public void No_Evidence_Mentions_A_Secret()
    {
        foreach (var ability in Catalog.All)
        {
            var decision = Evaluate(ability, SoloReady, Ready());
            foreach (var fact in decision.Evidence)
                Assert.False(fact.Contains("secret", StringComparison.OrdinalIgnoreCase), fact);
            Assert.False(decision.Reason.Contains("secret", StringComparison.OrdinalIgnoreCase), decision.Reason);
        }
    }
}
