using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// v3.0.0 C4: self-sustain consumes the curve HP (with source-labelled reasons),
/// the overheal guard uses the curve band's UPPER bound, and the Ext2 SelfHeal2
/// candidate is evaluated as an alternate for the same SelfHeal slot when the
/// primary does not Use. Rank and one-action-per-tick are unchanged.
/// </summary>
public class SelfSustainAlternateTests
{
    private const int LoH = 633;        // Lay on Hands (useBelowHpPct 20, heal 100)
    private const int WoG = 85673;      // Word of Glory (heal 20)
    private const int VictoryRush = 34428;
    private static readonly KeyStroke SelfHealKey = new(0x48, false, false, false);
    private static readonly KeyStroke AlternateKey = new(0x47, false, false, false);
    private static readonly bool[] AllSlots = [true, true, true, true, true, true, true, true];

    private static BridgeFrame Frame(
        int? plainHp, int curveBand, bool hpCurve, int primarySpell, int altSpell,
        TriState selfHealRange, TriState altRange, bool withAlternate)
    {
        var b = new TestV5Frame()
            .Cast(PlayerCastState.None)
            .Target(inMelee: true, casting: false, interruptible: false)
            .Ranges((Slot.Main, TriState.Yes), (Slot.SelfHeal, selfHealRange))
            .ClassSpec("PALADIN", 3)
            .Slot(Slot.SelfHeal, SelfHealKey.VirtualKey)
            .SpellId(Slot.SelfHeal, primarySpell);
        if (plainHp is { } hp) b.Vitals(hp);
        if (curveBand >= 0) b.Ext2(hpCurve, curveBand);
        else if (withAlternate) b.Ext2(hpCurveActive: false, curveBand: 0); // present, no curve
        if (withAlternate) b.SelfHeal2(AlternateKey, altSpell, altRange);
        var cells = curveBand >= 0 || withAlternate ? b.BuildExt2(heartbeat: 7) : b.Build(heartbeat: 7);
        return PixelProtocol.Decode(cells)!;
    }

    private static SchedulePlan Advance(BridgeFrame frame, long now = 1000)
    {
        var scheduler = new ActionScheduler();
        var tracker = new CandidateTracker();
        tracker.Update(frame, now);
        return scheduler.Advance(new ScheduleInput
        {
            Frame = frame,
            OutOfCombatPermitted = true,
            Candidates = tracker.Snapshot(AllSlots),
            NowMs = now,
            MinKeyIntervalMs = 120,
            StaleAfterMs = 1500,
            HeartbeatTimeoutMs = 500,
            RepeatSuppressMs = 900,
            Context = CombatContext.FromFrame(frame),
            Options = new PolicyOptions { SoloEnabled = true },
            Catalog = AbilityCatalog.Default,
            CollectPolicyVerdicts = true,
        });
    }

    [Fact]
    public void Alternate_Wins_When_Primary_Is_Held_By_Its_Ceiling()
    {
        // Lay on Hands is held above 20% HP; Word of Glory (the second
        // ready+bound candidate) becomes the self-sustain action.
        var plan = Advance(Frame(plainHp: 50, curveBand: -1, hpCurve: false,
            LoH, WoG, TriState.Yes, TriState.Yes, withAlternate: true));

        Assert.Equal(Slot.SelfHeal, plan.Selected);
        Assert.Single(plan.Actions);
        Assert.Equal(WoG, plan.Actions[0].SpellId);
        Assert.Equal(AlternateKey, plan.Actions[0].Stroke);

        var verdict = Assert.Single(plan.Verdicts);
        Assert.True(verdict.Alternate);
        Assert.Equal(WoG, verdict.SpellId);
        Assert.Equal(PolicyVerdict.Use, verdict.Verdict);
    }

    [Fact]
    public void Primary_Use_Wins_And_Alternate_Is_Not_Needed()
    {
        // Emergency HP: Lay on Hands Uses, so the alternate is never consulted.
        var plan = Advance(Frame(plainHp: 15, curveBand: -1, hpCurve: false,
            LoH, WoG, TriState.Yes, TriState.Yes, withAlternate: true));

        Assert.Equal(Slot.SelfHeal, plan.Selected);
        Assert.Equal(LoH, plan.Actions[0].SpellId);
        Assert.Equal(SelfHealKey, plan.Actions[0].Stroke);
        var verdict = Assert.Single(plan.Verdicts);
        Assert.False(verdict.Alternate);
    }

    [Fact]
    public void Curve_Hp_Reason_Carries_The_Source()
    {
        var plan = Advance(Frame(plainHp: null, curveBand: 6, hpCurve: true,
            LoH, WoG, TriState.Yes, TriState.Yes, withAlternate: true));

        var verdict = Assert.Single(plan.Verdicts);
        Assert.True(verdict.Alternate);
        Assert.Contains("~40% (curve)", verdict.Reason);
    }

    [Fact]
    public void Alternate_Respects_Its_Own_Range()
    {
        // Victory Rush needs a target in melee; the SelfHeal2 range says OUT,
        // so the alternate is Unavailable and nothing fires for the slot.
        var plan = Advance(Frame(plainHp: 50, curveBand: -1, hpCurve: false,
            LoH, VictoryRush, TriState.Yes, TriState.No, withAlternate: true));

        Assert.Null(plan.Selected);
        Assert.DoesNotContain(plan.Actions, a => a.Slot == Slot.SelfHeal);
    }

    [Fact]
    public void Overheal_Guard_Uses_The_Curve_Upper_Bound()
    {
        var catalog = AbilityCatalog.Default;
        var provider = CandidateProviders.SelfSustain;
        var ability = catalog.TryGet(WoG)! with { HealPctMaxHp = 65, UseBelowHpPct = null };
        var opts = new PolicyOptions { SoloEnabled = true };

        PolicyDecision Evaluate(BridgeFrame frame)
        {
            var context = CombatContext.FromFrame(frame);
            return provider.Evaluate(new ProviderInput
            {
                Input = new PolicyInput
                {
                    Slot = Slot.SelfHeal,
                    SpellId = WoG,
                    Context = context,
                    Options = opts,
                    Memory = new PolicyMemory(),
                    NowMs = 1000,
                    InCombat = true,
                    HasTarget = true,
                },
                Ability = ability,
                Catalog = catalog,
                Range = TriState.Yes,
            });
        }

        // Band 9 = 60% (upper 63%): a 65% heal would mostly overheal -> Hold.
        var curve = Evaluate(Frame(plainHp: null, curveBand: 9, hpCurve: true, WoG, WoG, TriState.Yes, TriState.Unknown, withAlternate: false));
        Assert.Equal(PolicyVerdict.Hold, curve.Verdict);
        Assert.Contains("overheal", curve.Reason);

        // Same lower bound as an exact plain 60% reading: 40% missing is
        // materially useful -> Use.
        var plain = Evaluate(Frame(plainHp: 60, curveBand: -1, hpCurve: false, WoG, WoG, TriState.Yes, TriState.Unknown, withAlternate: false));
        Assert.Equal(PolicyVerdict.Use, plain.Verdict);
    }
}
