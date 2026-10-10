using System.Text.Json;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// v3.0.0 offensive gap-fill + the defensive Orange tier (task r1-offgap).
///
/// The Offensive slot was previously MaxDps-wire-only: an offensive cooldown
/// the user toggled ON never fired when MaxDps did not surface it. This suite
/// pins the companion-side seam:
///  * the curated per-spec offensive lists exist, are verified and name-matched;
///  * the companion detects the gap-fill source by id membership (no wire bit);
///  * a gap-fill fires in combat (or in Solo out of combat) but never
///    out-of-combat in Normal mode;
///  * a MaxDps-named offensive is unaffected (MaxDpsWire);
///  * Defensive short-CD gap-fill fires at Orange while a major still holds
///    until Red.
/// </summary>
public class OffensiveGapFillTests
{
    private const long Now = 10_000;

    private static AbilityCatalog Catalog => AbilityCatalog.Default;

    // ---- catalog knowledge ------------------------------------------------

    [Fact]
    public void Every_Offensive_Extra_Is_Catalogued_And_Autonomous()
    {
        var any = false;
        foreach (var className in AbilityCatalog.ClassOrder.Skip(1))
        {
            foreach (var spec in AbilityCatalog.SpecOrder[className].Skip(1))
            {
                foreach (var id in Catalog.OffensiveGapFill(className, spec))
                {
                    any = true;
                    var ability = Catalog.TryGet(id);
                    Assert.NotNull(ability);
                    Assert.Equal(AutomationContext.Autonomous, ability!.Automation);
                    Assert.True(Catalog.IsOffensiveGapFill(className, spec, id));
                }
            }
        }
        Assert.True(any, "no spec carries an offensive gap-fill list");
    }

    [Fact]
    public void Every_Present_Offensive_Id_Matches_Its_Live_Client_Name()
    {
        var verification = LoadVerificationNames();
        foreach (var className in AbilityCatalog.ClassOrder.Skip(1))
        {
            foreach (var spec in AbilityCatalog.SpecOrder[className].Skip(1))
            {
                foreach (var id in Catalog.OffensiveGapFill(className, spec))
                {
                    Assert.True(verification.ContainsKey(id),
                        $"offensive gap-fill id {id} ({className}/{spec}) is not in the live-client export");
                    Assert.Equal(verification[id], Catalog.TryGet(id)!.Name);
                }
            }
        }
    }

    [Fact]
    public void Warrior_Lists_Put_Shared_Burst_Before_Spec_Specific()
    {
        var arms = Catalog.OffensiveGapFill("WARRIOR", "Arms");
        Assert.Equal(new[] { 107574, 228920, 262161, 260708, 227847 }, arms);   // Avatar + Ravager shared, then Warbreaker/Sweeping Strikes/Bladestorm Arms
        var fury = Catalog.OffensiveGapFill("WARRIOR", "Fury");
        Assert.Equal(new[] { 107574, 228920, 1719 }, fury);      // Avatar + Ravager shared, Recklessness Fury
        // The task's example names Recklessness / Ravager / Avatar; the stale
        // Ravager id 152277 is corrected to the verified 228920 (documented).
        Assert.Contains(1719, fury);
        Assert.Contains(228920, fury);
        Assert.DoesNotContain(152277, fury);
    }

    [Fact]
    public void Offensive_GapFill_Source_Is_Membership_Only()
    {
        // Membership is per class+spec: Fury's list does not leak into Arms.
        Assert.True(Catalog.IsOffensiveGapFill("WARRIOR", "Fury", 1719));
        Assert.False(Catalog.IsOffensiveGapFill("WARRIOR", "Arms", 1719));
        Assert.False(Catalog.IsOffensiveGapFill("MAGE", "Frost", 1719));
        Assert.False(Catalog.IsOffensiveGapFill(null, null, 1719));
        Assert.False(Catalog.IsOffensiveGapFill("WARRIOR", "Fury", 0));
    }

    // ---- policy gates -----------------------------------------------------

    [Fact]
    public void GapFill_Fires_In_Combat_And_Is_Companion_Source()
    {
        var decision = Evaluate(Slot.Offensive, 1719, Context("WARRIOR", "Fury"), inCombat: true);
        Assert.Equal(PolicyVerdict.Use, decision.Verdict);
        Assert.Equal(CandidateSourceKind.CompanionGapFill, decision.Source);
    }

    [Fact]
    public void GapFill_Is_Held_Out_Of_Combat_In_Normal_Mode()
    {
        var decision = Evaluate(Slot.Offensive, 1719, Context("WARRIOR", "Fury"), inCombat: false);
        Assert.Equal(PolicyVerdict.Hold, decision.Verdict);
        Assert.Equal(CandidateSourceKind.CompanionGapFill, decision.Source);
        Assert.Contains("out of combat", decision.Reason);
    }

    [Fact]
    public void GapFill_Fires_Out_Of_Combat_In_Solo_Mode()
    {
        var decision = Evaluate(Slot.Offensive, 1719, Context("WARRIOR", "Fury"),
            options: new PolicyOptions { SoloEnabled = true }, inCombat: false);
        Assert.Equal(PolicyVerdict.Use, decision.Verdict);
    }

    [Fact]
    public void MaxDps_Named_Offensive_Is_Not_Gated_As_GapFill()
    {
        // 1719 is in Fury's list but not Arms': the same id in an Arms session
        // is a MaxDps-wire candidate and keeps the old behaviour (no combat gate).
        var decision = Evaluate(Slot.Offensive, 1719, Context("WARRIOR", "Arms"), inCombat: false);
        Assert.Equal(PolicyVerdict.Use, decision.Verdict);
        Assert.Equal(CandidateSourceKind.MaxDpsWire, decision.Source);
    }

    [Fact]
    public void Off_Policy_Never_Fires_A_GapFill()
    {
        var policy = AbilityPolicy.Default.WithMode(1719, UserAbilityMode.Never);
        var decision = Evaluate(Slot.Offensive, 1719, Context("WARRIOR", "Fury"),
            options: new PolicyOptions { Abilities = policy }, inCombat: true);
        Assert.Equal(PolicyVerdict.Skip, decision.Verdict);
        Assert.Contains("user policy disabled", decision.Reason);
    }

    // ---- defensive Orange tier -------------------------------------------

    [Fact]
    public void Defensive_Short_Cooldown_GapFill_Fires_At_Orange()
    {
        // Feint 1966 (Rogue, Minor, off-GCD, no incoming-cast prerequisite).
        var decision = Evaluate(Slot.Defensive, 1966,
            Context("ROGUE", "Assassination", urgency: DefensiveUrgency.Orange, defenseCatalogSource: true),
            inCombat: true);
        Assert.Equal(PolicyVerdict.Use, decision.Verdict);
        Assert.Equal(CandidateSourceKind.CompanionGapFill, decision.Source);
    }

    [Fact]
    public void Defensive_Major_GapFill_Still_Holds_At_Orange()
    {
        // Evasion 5277 is a major; at Orange it must hold until Red.
        var decision = Evaluate(Slot.Defensive, 5277,
            Context("ROGUE", "Assassination", urgency: DefensiveUrgency.Orange, defenseCatalogSource: true),
            inCombat: true);
        Assert.Equal(PolicyVerdict.Hold, decision.Verdict);
        Assert.Contains("urgency", decision.Reason);
    }

    [Fact]
    public void Defensive_Catalog_Minor_List_Excludes_Majors()
    {
        // The bridge's Orange list must never shadow a ready short CD with a
        // held major.
        var minors = Catalog.DefensiveGapFillMinor("WARRIOR", "Arms");
        Assert.DoesNotContain(871, minors);          // Shield Wall (Major)
        Assert.Contains(23920, minors);              // Spell Reflection (Minor)
        var majors = Catalog.DefensiveGapFill("WARRIOR", "Arms");
        Assert.Contains(871, majors);
    }

    // ---- helpers ----------------------------------------------------------

    private static CombatContext Context(
        string? className = null,
        string? spec = null,
        PlayerCastState cast = PlayerCastState.None,
        DefensiveUrgency urgency = DefensiveUrgency.Unknown,
        bool defenseCatalogSource = false)
    {
        var range = new TriState[PixelProtocol.SlotCount];
        range[(int)Slot.Offensive] = TriState.Yes;
        return new CombatContext
        {
            HpValid = true,
            HpPct = 90,
            Cast = cast,
            TargetCasting = TriState.No,
            SlotRange = range,
            SlotBuffActive = new TriState[PixelProtocol.SlotCount],
            ContextValid = true,
            DefensiveUrgency = urgency,
            DefensiveCatalogSource = defenseCatalogSource,
            Class = className,
            Spec = spec,
        };
    }

    private static PolicyDecision Evaluate(
        Slot slot,
        int spellId,
        CombatContext context,
        PolicyOptions? options = null,
        bool inCombat = true) =>
        PolicyEvaluator.Evaluate(new PolicyInput
        {
            Slot = slot,
            SpellId = spellId,
            Context = context,
            Options = options ?? PolicyOptions.Standard,
            Memory = new PolicyMemory(),
            NowMs = Now,
            InCombat = inCombat,
            HasTarget = true,
        }, Catalog);

    private static Dictionary<int, string> LoadVerificationNames()
    {
        using var stream = typeof(ClassSpellBook).Assembly
            .GetManifestResourceStream(ClassSpellBook.VerificationResourceName)!;
        using var doc = JsonDocument.Parse(stream);
        var result = new Dictionary<int, string>();
        if (!doc.RootElement.TryGetProperty("entries", out var entries)) return result;
        foreach (var row in entries.EnumerateArray())
        {
            if (!row.TryGetProperty("id", out var idProp)) continue;
            var id = idProp.GetInt32();
            var name = row.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
            if (id > 0 && name.Length > 0) result[id] = name;
        }
        return result;
    }
}
